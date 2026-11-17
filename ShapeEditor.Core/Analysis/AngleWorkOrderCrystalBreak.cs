using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序生成器（续）：<b>O-v2 碎晶路线</b>（docs/angle-route-optimal-v2.md §2）。
///
/// <para>
/// 这是 n = 4 碎晶角的第一顺位路线。前态严格按 O-v2 的四列布局：
/// </para>
/// <code>
/// A（目标列）      = 正规核心：目标晶体 + 「能抬多高抬多高」的普通格 + 临时晶体（紧列、c/T 交替）
/// S（第 2 象限）   = 满高普通格，在被提升格的<b>源层</b>放针（构造 E 的针位）
/// 第 3 象限        = 空
/// F（第 4 象限）   = 从地面到最高锚层的紧列，在每个临时晶体块的锚层（最上层）放晶体
/// 一刀（构造 D）   = 刀口跨过保险丝与临时晶体的连接，整段临时晶体碎掉，被提升格落回目标层
/// </code>
///
/// <para>
/// 核心列完全由扫描得出：<see cref="ResolveRecursiveLiftPlan"/> 把每个「晶体段底端正下方为空」
/// 的普通格提到段底（能抬多高抬多高），<see cref="BuildCoreColumn"/> 再把最高晶体以下的
/// 空位填成临时晶体。S / F 由提升记录与临时晶体块直接算出，<b>没有打表</b>。
/// </para>
///
/// <para>
/// <b>一刀不够时</b>（临时晶体是多个不连续块，例如 <c>T.T.c</c>、<c>T.T.T.c</c>）：
/// 按「<c>K = 临时晶体块集合</c>」<b>逐块切</b>——每轮取当前最上面的一块，一刀，
/// <c>K = K \ {块}</c>，<b><c>K = ∅</c> 即停</b>（终止条件是集合空，不是轮数上限）；
/// 每刀之前按 §0.12.2 生成五步「交换支撑柱」工序（旋转 / 装新柱 / 接管 / 拆旧柱与保险丝 /
/// 在刀后逆旋转）。每一步都用真实 <see cref="ShapeOps.Rotate"/> /
/// <see cref="ShapePhysics.Cut"/> 回放，并逐格核对目标列。
/// </para>
/// <para>
/// <b>只切一块</b>是关键：其余块留下的临时晶体正好当下一刀的刹车，
/// 上一刀被提升的普通格才会停在自己的落点。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>O-v2 碎晶路线的入口；返回 null 表示这条路线不适用或回放失败。</summary>
    private static AngleWorkOrder? TryBuildCrystalBreakOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (target.PartCount != 4 ||
            cap < target.LayerCount ||
            kind != AngleBuildKind.NeedsCrystalBreak)
        {
            return null;
        }

        string tight = ColumnTextPadded(target, column, target.LayerCount);
        string targetColumn = ColumnTextPadded(target, column, cap);

        if (!ShapeSignature.CheckColumn(tight).IsLegal ||
            !targetColumn.Contains(ShapeSignature.CrystalCode) ||
            targetColumn.Contains(ShapeSignature.PinCode))
        {
            return null;
        }

        int highest = targetColumn.LastIndexOf(ShapeSignature.CrystalCode);

        if (highest <= 0)
        {
            return null;
        }

        // ── ① 正规核心：提升 + 填临时晶体（全部由扫描得出）──
        int[] destinations = ResolveRecursiveLiftPlan(targetColumn, highest);
        char[]? core = BuildCoreColumn(targetColumn, destinations, highest);

        if (core is null)
        {
            return null;
        }

        string coreText = new(core);

        if (!ShapeSignature.IsTightColumn(coreText) ||
            !ShapeSignature.CheckColumn(coreText).IsLegal)
        {
            return null;
        }

        List<(int Bottom, int Top)> blocks = FindTemporaryBlocks(targetColumn, core);

        /*
         * 没有提升记录也可以走：晶体直接坐在普通格上、洞在普通格下方时
         * （例如 T..Tc），核心只是把洞填成临时晶体，一刀碎掉、普通格原位不动。
         * 这时托柱没有针（满高普通格）也能通过回放。
         */
        if (blocks.Count == 0)
        {
            return null;
        }

        // 先试「一刀碎全部块」（O-v2 §2 的规范形态）。
        AngleWorkOrder? single = ReplayCrystalBreak(
            target,
            column,
            cap,
            kind,
            coreText,
            targetColumn,
            singleCut: true);

        if (single is not null)
        {
            return single;
        }

        // 一刀不够：切最上面的块，再一刀切剩下的块（轮间走五步交换支撑柱）。
        return ReplayCrystalBreak(
            target,
            column,
            cap,
            kind,
            coreText,
            targetColumn,
            singleCut: false);
    }

    /// <summary>
    /// 用真实物理回放一份碎晶计划。切割排程只有两种固定形态（不是候选搜索）：
    /// ① <paramref name="singleCut"/>：全部临时晶体块一刀碎（锚层可被同一刀覆盖时）；
    /// ② 否则：<b>逐块切</b>——每轮取当前最上面的一块，一刀；刀前按 §0.12.2 五步换柱，
    ///    直到 <c>K</c>（临时晶体块集合）清空。刀数 = 初始块数。
    /// </summary>
    private static AngleWorkOrder? ReplayCrystalBreak(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        string coreText,
        string targetColumn,
        bool singleCut)
    {
        const int parts = 4;

        int holderColumn = Wrap(column + 1, parts);
        int bridgeColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);
        char color = CrystalColor(target, column);

        List<(int Bottom, int Top)> blocks =
            FindTemporaryBlocks(targetColumn, coreText.ToCharArray());

        if (blocks.Count == 0)
        {
            return null;
        }

        var steps = new List<AngleWorkStep>();

        // ── 第一刀 ──
        List<(int Bottom, int Top)> firstCut = singleCut
            ? new List<(int Bottom, int Top)>(blocks)
            : new List<(int Bottom, int Top)>
            {
                blocks.OrderByDescending(block => block.Top).First()
            };

        ShapeDocument? state = BuildCrystalBreakPreState(
            cap,
            parts,
            column,
            holderColumn,
            bridgeColumn,
            coreText,
            targetColumn,
            firstCut,
            color,
            out _);

        if (state is null)
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            "O-v2 碎晶前态：A = 正规核心（提升 + 临时晶体）、S = 托柱（源层放针）、" +
            "第 3 象限空、F = 保险丝（每个临时晶体块的锚层放晶体）" +
            $"；四列短代码 = {ExportCodes(state)}",
            state,
            AngleWorkStepKind.PreState));

        ShapeDocument? afterFirst = CutAndRotateBack(
            state,
            rotation,
            out CutResult? firstCutResult);

        if (afterFirst is null ||
            !ShapeOps.IsStable(afterFirst) ||
            !ShapeSignature.AllColumnsLegal(afterFirst, out _))
        {
            return null;
        }

        if (!NativeCrystalsPreserved(afterFirst, target, column) ||
            !AuxiliaryCrystalsCleared(afterFirst, holderColumn, bridgeColumn))
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            "第 1 刀（真实 Cut 后落定）：临时晶体整块碎掉，被提升的普通格落回目标层" +
            $"；目标列 = {ColumnTextPadded(afterFirst, column, cap)}",
            afterFirst,
            AngleWorkStepKind.Cut));

        ShapeDocument current = afterFirst;
        string currentText = ColumnTextPadded(current, column, cap);

        if (singleCut)
        {
            if (currentText != targetColumn)
            {
                return null;
            }

            return BuildCrystalBreakOrder(
                target,
                column,
                cap,
                kind,
                steps,
                current,
                holderColumn,
                bridgeColumn,
                rotation,
                blocks,
                multiCut: false);
        }

        /*
         * ── 多刀（docs §三）：K = 临时晶体块集合，while K ≠ ∅ 逐块切 ──
         *
         * 每轮只切「当前最上面的一块」：
         *   · 若该块在刀口另一侧 ⇒ 先按 §0.12.2 生成五步交换支撑柱；
         *   · 一刀 → 落定；
         *   · K = K \ {块}，直到 K 清空（终止条件是集合空，不是轮数上限）。
         *
         * 只切一块是关键：其余块留下的临时晶体正好当下一刀的刹车，
         * 上一刀被提升的普通格才会停在自己的落点。
         */
        if (currentText == targetColumn)
        {
            // 第一刀已经把目标列做对（剩下的块已经不在了）。
            return BuildCrystalBreakOrder(
                target,
                column,
                cap,
                kind,
                steps,
                current,
                holderColumn,
                bridgeColumn,
                rotation,
                blocks,
                multiCut: false);
        }

        int cuts = 1;
        bool reachedTarget = false;
        for (int round = 0; round <= blocks.Count; round++)
        {
            if (!NativeCrystalsPreserved(current, target, column) ||
                !AuxiliaryCrystalsCleared(current, holderColumn, bridgeColumn))
            {
                return null;
            }

            currentText = ColumnTextPadded(current, column, cap);

            List<(int Bottom, int Top)> remaining =
                FindTemporaryBlocks(targetColumn, currentText.ToCharArray());

            if (remaining.Count == 0)
            {
                if (currentText != targetColumn)
                {
                    return null;
                }

                reachedTarget = true;
                break;
            }

            // 取一块：从上到下（顶层的块先切）。
            (int Bottom, int Top) next = remaining[0];

            foreach ((int bottom, int top) in remaining)
            {
                if (top > next.Top)
                {
                    next = (bottom, top);
                }
            }

            if (!AppendSupportExchange(
                    steps,
                    current,
                    column,
                    cap,
                    parts,
                    holderColumn,
                    bridgeColumn,
                    targetColumn,
                    next,
                    cuts,
                    color,
                    out ShapeDocument? exchangeState,
                    out _))
            {
                return null;
            }

            if (exchangeState is null)
            {
                return null;
            }

            ShapeDocument? afterCut = CutAndRotateBack(
                exchangeState,
                rotation,
                out _);

            if (afterCut is null ||
                !ShapeOps.IsStable(afterCut) ||
                !ShapeSignature.AllColumnsLegal(afterCut, out _))
            {
                return null;
            }

            // 每一刀都是在「顺转 1 个象限」的坐标系里切的，转回来后才回到原象限。
            ShapeDocument rotatedBack = ShapeOps.Rotate(afterCut, -1);

            if (!ShapeOps.IsStable(rotatedBack))
            {
                return null;
            }

            steps.Add(new AngleWorkStep(
                $"第 {cuts + 1} 刀（真实 Cut 后落定）：切第 {next.Bottom + 1}–" +
                $"{next.Top + 1} 层的临时晶体块，上方普通格落回目标层" +
                $"；目标列 = {ColumnTextPadded(rotatedBack, column, cap)}",
                afterCut,
                AngleWorkStepKind.Cut));

            steps.Add(new AngleWorkStep(
                $"第 {cuts} 轮换柱 ⑤ 逆旋转 1 个象限，回到原象限（换柱工序的最后一步）" +
                $"；四列短代码 = {ExportCodes(rotatedBack)}",
                rotatedBack,
                AngleWorkStepKind.Exchange));

            cuts++;
            current = rotatedBack;

            // 进展检查：每刀必须清掉一个块，否则说明这一刀没切到。
            List<(int Bottom, int Top)> after = FindTemporaryBlocks(
                targetColumn,
                ColumnTextPadded(current, column, cap).ToCharArray());

            if (after.Count >= remaining.Count)
            {
                return null;
            }
        }

        if (!reachedTarget)
        {
            return null;
        }

        return BuildCrystalBreakOrder(
            target,
            column,
            cap,
            kind,
            steps,
            current,
            holderColumn,
            bridgeColumn,
            rotation,
            blocks,
            multiCut: cuts > 1);
    }

    /// <summary>
    /// 装一份碎晶前态：目标列 = 核心列；托柱 = 满高普通格 + 被切块对应提升格的源层针；
    /// 保险丝柱 = 紧列到最高锚层，每个被切块锚层放晶体。
    /// </summary>
    private static ShapeDocument? BuildCrystalBreakPreState(
        int cap,
        int parts,
        int column,
        int holderColumn,
        int bridgeColumn,
        string coreText,
        string targetColumn,
        IReadOnlyList<(int Bottom, int Top)> cutBlocks,
        char color,
        out string? failure)
    {
        failure = null;

        if (cutBlocks.Count == 0)
        {
            failure = "没有要切的临时晶体块";
            return null;
        }

        List<(int Core, int Target)> lifts = FindLifts(
                targetColumn,
                coreText.ToCharArray())
            .Where(lift => lift.Core > lift.Target)
            .ToList();

        var activeLifts = lifts
            .Where(lift => cutBlocks.Any(block =>
                lift.Core - 1 >= block.Bottom &&
                lift.Core - 1 <= block.Top))
            .ToList();

        string support = BuildCrystalBreakSupport(cap, activeLifts);
        string fuse = BuildCrystalBreakFuse(cap, cutBlocks);

        var state = new ShapeDocument(cap, parts);

        WriteColumn(state, column, coreText, color);
        WriteColumn(state, holderColumn, support, color);
        WriteColumn(state, bridgeColumn, fuse, color);

        if (!ShapeSignature.AllColumnsLegal(state, out int badColumn))
        {
            failure = $"第 {badColumn + 1} 象限不过五项条件";
            return null;
        }

        if (!ShapeOps.IsStable(state))
        {
            failure = "前态不稳定";
            return null;
        }

        if (FuseTouchesPreservedCrystal(
                state,
                column,
                bridgeColumn,
                cutBlocks.Select(block => block.Top).ToList(),
                targetColumn))
        {
            failure = "保险丝晶体与目标原生晶体连通（会误伤目标）";
            return null;
        }

        return state;
    }

    /// <summary>O-v2 托柱：满高普通格，在被提升格的源层（Core）放针。</summary>
    private static string BuildCrystalBreakSupport(
        int cap,
        IReadOnlyList<(int Core, int Target)> activeLifts)
    {
        char[] support = new char[cap];
        Array.Fill(support, ShapeSignature.SolidCode);

        foreach ((int core, int target) in activeLifts)
        {
            if (core <= target || core < 0 || core >= cap)
            {
                continue;
            }

            support[core] = ShapeSignature.PinCode;
        }

        // 五项条件：针必须有直接支撑（托柱是满高普通格，这一条恒成立；
        // 仍保留检查，防止将来改成非满高布局）。
        for (int layer = 1; layer < cap; layer++)
        {
            if (support[layer] == ShapeSignature.PinCode &&
                support[layer - 1] == ShapeSignature.EmptyCode)
            {
                support[layer] = ShapeSignature.SolidCode;
            }
        }

        if (support[0] == ShapeSignature.PinCode)
        {
            support[0] = ShapeSignature.SolidCode;
        }

        return new string(support);
    }

    /// <summary>
    /// O-v2 保险丝柱：从地面到最高锚层的紧列，每个被切块锚层（最上层）放一颗晶体，其余普通格；
    /// 最高锚层以上留空（短代码与 O-v2 表一致）。
    /// </summary>
    private static string BuildCrystalBreakFuse(
        int cap,
        IReadOnlyList<(int Bottom, int Top)> cutBlocks)
    {
        char[] fuse = new char[cap];
        Array.Fill(fuse, ShapeSignature.EmptyCode);

        int maxTop = -1;

        foreach ((int _, int top) in cutBlocks)
        {
            if (top >= 0 && top < cap && top > maxTop)
            {
                maxTop = top;
            }
        }

        if (maxTop < 0)
        {
            return new string(fuse);
        }

        for (int layer = 0; layer <= maxTop; layer++)
        {
            fuse[layer] = ShapeSignature.SolidCode;
        }

        foreach ((int _, int top) in cutBlocks)
        {
            if (top >= 0 && top <= maxTop)
            {
                fuse[top] = ShapeSignature.CrystalCode;
            }
        }

        return new string(fuse);
    }

    /// <summary>
    /// §0.12.2 的五步交换支撑柱（多刀形态，每一刀前一轮）：旋转 1 个象限 → 装新托柱 →
    /// 新柱接管 → 拆旧柱并装下一刀保险丝 → （真正的下一刀）→ 逆旋转。
    ///
    /// <para>
    /// 这里生成前四步与「进入下一刀」的快照；第五步（逆旋转）在下一刀之后由
    /// <see cref="ReplayCrystalBreak"/> 追加，这样刀口始终落在支撑柱与目标列之间。
    /// 每一步都是真实 <see cref="ShapeOps.Rotate"/> / 直接改列后的稳定快照。
    /// </para>
    /// <para>
    /// <paramref name="cutBlock"/> 是<b>下一刀要切的那一块</b>：新托柱的针按这一块的提升记录放，
    /// 新保险丝也只在它的锚层放晶体。其余块的临时晶体必须留在刀口这一侧（不被误切），
    /// 它们正是这一刀的刹车。
    /// </para>
    /// </summary>
    private static bool AppendSupportExchange(
        List<AngleWorkStep> steps,
        ShapeDocument current,
        int column,
        int cap,
        int parts,
        int holderColumn,
        int bridgeColumn,
        string targetColumn,
        (int Bottom, int Top) cutBlock,
        int round,
        char color,
        out ShapeDocument? exchangeState,
        out string? failure)
    {
        exchangeState = null;
        failure = null;

        int oldSupportAfterRotation = Wrap(column + 2, parts);
        int rotatedTarget = Wrap(column + 1, parts);
        string currentTargetText = ColumnTextPadded(current, column, cap);

        // ① 顺转 1 个象限：旧支撑柱落到 column+2，目标列到 column+1。
        ShapeDocument rotated = ShapeOps.Rotate(current, 1);

        if (!ShapeOps.IsStable(rotated))
        {
            failure = "交换①（旋转）后不稳定";
            return false;
        }

        steps.Add(new AngleWorkStep(
            $"第 {round} 轮换柱 ① 顺转 1 个象限：旧支撑柱落到第 " +
            $"{oldSupportAfterRotation + 1} 象限，目标列到第 " +
            $"{Wrap(column + 1, parts) + 1} 象限" +
            $"；四列短代码 = {ExportCodes(rotated)}",
            rotated,
            AngleWorkStepKind.Exchange));

        // ② 在另一侧（原 column 象限）装新支撑柱：满高普通格 + 下一刀的针。
        List<(int Core, int Target)> lifts = FindLifts(
                targetColumn,
                currentTargetText.ToCharArray())
            .Where(lift => lift.Core > lift.Target)
            .ToList();

        var activeLifts = lifts
            .Where(lift =>
                lift.Core - 1 >= cutBlock.Bottom &&
                lift.Core - 1 <= cutBlock.Top)
            .ToList();

        string support = BuildCrystalBreakSupport(cap, activeLifts);

        ShapeDocument withNewSupport = rotated.Clone();

        WriteColumn(withNewSupport, column, support, color);

        if (!ShapeOps.IsStable(withNewSupport) ||
            ColumnTextPadded(withNewSupport, rotatedTarget, cap) !=
                currentTargetText)
        {
            failure = "交换②（装新柱）后目标列被改动或不稳定";
            return false;
        }

        steps.Add(new AngleWorkStep(
            $"第 {round} 轮换柱 ② 在另一侧装新支撑柱（连续接地、覆盖目标高度，按下一刀放针）" +
            $"；四列短代码 = {ExportCodes(withNewSupport)}",
            withNewSupport,
            AngleWorkStepKind.Support));

        // ③ 新柱接管：两侧同时托住目标列。
        steps.Add(new AngleWorkStep(
            $"第 {round} 轮换柱 ③ 新柱接管支撑（此时目标列被两侧同时托住）" +
            $"；四列短代码 = {ExportCodes(withNewSupport)}",
            withNewSupport,
            AngleWorkStepKind.Exchange));

        // ④ 拆旧柱与旧保险丝，并在旧柱位置装下一刀的保险丝（只在下一块的锚层放晶体）。
        string fuse = BuildCrystalBreakFuse(cap, new[] { cutBlock });

        ShapeDocument withFuse = withNewSupport.Clone();

        WriteColumn(withFuse, oldSupportAfterRotation, fuse, color);

        if (!ShapeOps.IsStable(withFuse) ||
            ColumnTextPadded(withFuse, rotatedTarget, cap) !=
                currentTargetText)
        {
            failure = "交换④（拆旧柱 / 装保险丝）后目标列被改动或不稳定";
            return false;
        }

        steps.Add(new AngleWorkStep(
            $"第 {round} 轮换柱 ④ 拆掉旧支撑柱与旧保险丝，在同一侧装下一刀的保险丝" +
            $"；四列短代码 = {ExportCodes(withFuse)}",
            withFuse,
            AngleWorkStepKind.Support));

        exchangeState = withFuse;
        return true;
    }

    /// <summary>真实 Cut 后取西半边并转回原象限。</summary>
    private static ShapeDocument? CutAndRotateBack(
        ShapeDocument state,
        int rotation,
        out CutResult? cut)
    {
        cut = ShapePhysics.Cut(ShapeOps.Rotate(state, rotation));

        if (!cut.Success || cut.West is null)
        {
            return null;
        }

        return ShapeOps.Rotate(cut.West, -rotation);
    }

    /// <summary>目标列里的原生晶体必须一颗不少（层号也一致）。</summary>
    private static bool NativeCrystalsPreserved(
        ShapeDocument state,
        ShapeDocument target,
        int column)
    {
        int layers = Math.Min(state.LayerCount, target.LayerCount);

        for (int layer = 0; layer < layers; layer++)
        {
            if (target[layer, column].Type == CellType.Crystal &&
                state[layer, column].Type != CellType.Crystal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>辅助列的晶体必须全部碎掉。</summary>
    private static bool AuxiliaryCrystalsCleared(
        ShapeDocument state,
        params int[] columns)
    {
        foreach (int part in columns)
        {
            for (int layer = 0; layer < state.LayerCount; layer++)
            {
                if (state[layer, part].Type == CellType.Crystal)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>工序单：Support（构造 C 交接）→ Target。</summary>
    private static AngleWorkOrder? BuildCrystalBreakOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        List<AngleWorkStep> steps,
        ShapeDocument final,
        int holderColumn,
        int bridgeColumn,
        int rotation,
        IReadOnlyList<(int Bottom, int Top)> blocks,
        bool multiCut)
    {
        var handover = new ShapeDocument(cap, 4);

        for (int layer = 0; layer < cap; layer++)
        {
            handover[layer, column] = final[layer, column].Clone();
        }

        FillPillar(handover, holderColumn);

        if (!ShapeOps.IsStable(handover) ||
            !MatchesTargetColumn(handover, target, column, cap, out _))
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            "构造 C：托柱换成满高普通柱、拆掉保险丝柱，目标列逐格不变",
            handover,
            AngleWorkStepKind.Support));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        var limitations = BaseLimitations(
            "O-v2 碎晶路线（docs/angle-route-optimal-v2.md §2）：A = 正规核心、" +
            "S = 托柱（源层放针）、F = 保险丝（块锚层放晶体），" +
            "全部由沿列扫描得出，没有打表；切割用真实 ShapePhysics.Cut 回放。");

        limitations.Add(
            "临时晶体块：" +
            string.Join(
                "、",
                blocks.Select(block =>
                    $"第 {block.Bottom + 1}–{block.Top + 1} 层")) +
            "；保险丝在锚层（块最高层）放晶体。");

        limitations.Add(
            "S 的针位 = 被提升格的源层（落点层放针的等价写法）：" +
            "针不参与横向粘连，普通格才会落到目标层。");

        if (multiCut)
        {
            int cuts = steps.Count(step => step.Kind == AngleWorkStepKind.Cut);

            limitations.Add(
                $"多刀：共 {cuts} 刀（= 临时晶体块数），每轮只切当前最上面的一块；" +
                "刀前按 §0.12.2 五步交换支撑柱" +
                "（顺转 1 象限 → 装新柱 → 新柱接管 → 拆旧柱并装下一刀保险丝 → 刀后逆旋转），" +
                "每一步都带完整四列短代码并真实回放；K 清空即停。");
        }

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.CrystalBreak,
            Algorithm = AngleAlgorithm.Algorithm4,
            Route = AngleRoute.CrystalBreak,
            Rotation = rotation,
            AuxiliaryColumns = new[] { holderColumn, bridgeColumn },
            Rank = 0,
            Limitations = limitations
        };
    }

    /// <summary>完整四列短代码（用于工序说明 / 回放核对）。</summary>
    private static string ExportCodes(ShapeDocument state)
    {
        return new ShortCodeExporter().Export(state, omitTopEmptyLayers: false);
    }
}

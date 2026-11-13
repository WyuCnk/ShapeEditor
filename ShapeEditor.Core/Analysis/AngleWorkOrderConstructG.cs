using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序生成器（续）：n = 4 的<b>构造 G（四象限单孔环下落）</b>。
///
/// <para>
/// 依据 <c>docs/angle-work-order-algorithm.md</c> §15：目标列与环伙伴 B 在晶体段那一带
/// 合成一个「环」，环里的针把上下两段普通格隔开；环下方的保险丝晶体在切割里碎掉之后，
/// 环内的普通材料<b>只降一层</b>（落点下方可以是空的），而目标原生晶体一颗不少。
/// </para>
///
/// <para><b>扫描方式（§15.2 / §15.8 的实现要点）</b></para>
/// <list type="number">
/// <item>提升计划由 <see cref="ResolveStaircaseLiftPlan"/> 一次给出（阶梯式，§4.8.1），核心列由「提升 + 正规核心」得出；</item>
/// <item>每个「核心层 &gt; 目标层」的普通格是一条<b>下落记录</b> <c>q → d</c>；
/// 它的<b>闸门</b> <c>g = q − 1</c> 必须是临时晶体（切掉它，q 层的普通格才落得下来）；</item>
/// <item>最高的那条下落记录正上方必须是<b>连续三颗目标晶体</b>，其最下层记为 <c>s</c>
/// （这就是环锁定的晶体段，也是 G1）；</item>
/// <item>环伙伴 B 的第 <c>s+1</c> 层放<b>针</b>（环内），环下方每个闸门放一颗<b>晶体</b>
/// （辅助列必须是紧列，半破时与临时晶体一起碎掉），其余层实心（T）；</item>
/// <item>保险丝柱 B′ 在<b>每个闸门所在层</b>放一颗晶体（<c>k = g</c>，正是临时晶体块的顶层），
/// 其余层实心；</item>
/// <item>半破一刀：刀口跨过 B′ 与目标列闸门晶体的横向连接，闸门晶体与整块临时晶体一起碎掉，
/// 再由真实 <see cref="ShapePhysics.Cut"/> 逐格回放验证 §15.3 的四条不变量。</item>
/// </list>
///
/// <para>
/// <b>G5 / G8 按 §15.10 的修正实现</b>：B 的闸门层现在放晶体而不是洞
/// （docs §5 规则六 / §6 第 7 条要求辅助列一律紧列），
/// 保险丝也可以放在 B 以外的单独一列（本实现固定在刀口另一侧的 B′）。
/// <b>G8a</b> 由「闸门层在目标里不是晶体、核心列里是临时晶体」两条扫描共同保证。
/// </para>
///
/// <para>
/// <b>禁止打表</b>：<c>s</c>、<c>q</c>、<c>d</c>、<c>g</c>、<c>k</c> 全部由沿列扫描得出，
/// 分支谓词只有「三连晶」「两个下落记录」「闸门是不是临时晶体」这类结构谓词（§10）。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    #region 入口

    /// <summary>
    /// 路线 F 的<b>首选</b>恢复手段：构造 G（§15 单孔环下落）。
    ///
    /// <para>
    /// 返回 null 表示「G1–G14 不成立，或回放没过不变量」，由
    /// <see cref="TryBuildGenericFourQuadrantOrder"/>（构造 D 的碎晶等价路线）接手——
    /// 旧路线不会被删掉，覆盖率只增不减。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildConstructGOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (target.PartCount != 4 || cap < target.LayerCount)
        {
            return null;
        }

        string targetColumn = ColumnTextPadded(target, column, cap);
        string tightTarget = ColumnTextPadded(target, column, target.LayerCount);

        // 与路线 F 同一道闸门：五项条件、有晶体、无针、F 成立。
        if (!ShapeSignature.CheckColumn(tightTarget).IsLegal ||
            !targetColumn.Contains(ShapeSignature.CrystalCode) ||
            targetColumn.Contains(ShapeSignature.PinCode) ||
            !PillarSCriterion.RecursiveTerminalF(tightTarget))
        {
            return null;
        }

        int highestCrystal = targetColumn.LastIndexOf(ShapeSignature.CrystalCode);

        if (highestCrystal <= 0)
        {
            return null;
        }

        // 构造 G 的阶梯式提升计划（§4.8.1）只解一次（不再按候选计划枚举 + 回放筛选）。
        int[] destinations = ResolveStaircaseLiftPlan(targetColumn, highestCrystal);

        char[]? core = BuildCoreColumn(
            targetColumn,
            destinations,
            highestCrystal);

        if (core is null)
        {
            return null;
        }

        return TryPlanConstructG(
            target,
            column,
            cap,
            kind,
            targetColumn,
            new string(core));
    }

    #endregion

    #region G1–G11：由扫描得出参数并装前态

    /// <summary>
    /// 对一条提升计划做 G1–G14 的判定；全部成立才装前态并回放。
    /// </summary>
    private static AngleWorkOrder? TryPlanConstructG(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        string targetColumn,
        string coreText)
    {
        // ── 下落记录：核心层 > 目标层 的普通格（由提升表直接得出）──
        List<(int Core, int Target)> drops = FindLifts(targetColumn, coreText.ToCharArray())
            .Where(lift => lift.Core > lift.Target)
            .ToList();

        if (drops.Count == 0)
        {
            return null;
        }

        // 闸门 = 每条下落记录的源层减一；必须互不相同（否则两枚普通格抢同一个洞）。
        var gates = new HashSet<int>();

        foreach ((int source, int destination) in drops)
        {
            // ── G2：窗口里只有普通格和空（没有针、没有目标晶体）──
            if (coreText[source] != ShapeSignature.SolidCode ||
                targetColumn[destination] != ShapeSignature.SolidCode)
            {
                return null;
            }

            for (int layer = destination + 1; layer < source; layer++)
            {
                char code = coreText[layer];

                if (code == ShapeSignature.PinCode ||
                    (code == ShapeSignature.CrystalCode &&
                     targetColumn[layer] == ShapeSignature.CrystalCode))
                {
                    return null;
                }
            }

            /*
             * ── G3：落点层必须能被腾空 ──
             * 三种情形都算「腾得开」：
             *   ① 核心列里是临时晶体（这一刀把它碎掉）；
             *   ② 核心列里本来是空的；
             *   ③ 它是另一条下落记录的源层（那枚普通格自己也会落走）。
             * 第 ③ 种就是 §4.8.1 的阶梯式抬升：落点被上一级普通格占着，它同时也在下落。
             */
            bool destinationVacated =
                (coreText[destination] == ShapeSignature.CrystalCode &&
                 targetColumn[destination] != ShapeSignature.CrystalCode) ||
                coreText[destination] == ShapeSignature.EmptyCode ||
                drops.Exists(drop => drop.Core == destination);

            if (!destinationVacated)
            {
                return null;
            }

            /*
             * 闸门晶体：核心列里是临时晶体 ⇒ 切掉它，源层那枚普通格落一层。
             * 闸门层在目标里不能是晶体（G8a），否则碎它必然连带毁掉目标晶体。
             */
            int gate = source - 1;

            if (gate < 0 ||
                coreText[gate] != ShapeSignature.CrystalCode ||
                targetColumn[gate] == ShapeSignature.CrystalCode ||
                !gates.Add(gate))
            {
                return null;
            }
        }

        // ── G1：最高下落点正上方是连续三颗目标晶体 → 环锁定的晶体段，底端 s ──
        int s = drops[^1].Core + 1;

        if (s + 2 >= cap)
        {
            return null;
        }

        for (int layer = s; layer <= s + 2; layer++)
        {
            if (coreText[layer] != ShapeSignature.CrystalCode ||
                targetColumn[layer] != ShapeSignature.CrystalCode)
            {
                return null;
            }
        }

        // ── 临时晶体块（相对目标）：保险丝锚层 = 每个块的顶层 ──
        List<(int Bottom, int Top)> blocks = FindTemporaryBlocks(targetColumn, coreText.ToCharArray());

        if (blocks.Count == 0)
        {
            return null;
        }

        var anchors = new List<int>();

        foreach ((int _, int top) in blocks)
        {
            // G8 / G8a：保险丝必须在环下方，且该层在目标里不是晶体。
            if (top >= s ||
                top >= cap ||
                targetColumn[top] == ShapeSignature.CrystalCode ||
                coreText[top] != ShapeSignature.CrystalCode)
            {
                return null;
            }

            // 每个临时晶体块都必须正好是一个闸门块（否则切完会多掉一层）。
            if (!gates.Contains(top))
            {
                return null;
            }

            anchors.Add(top);
        }

        // 反过来：每个闸门都必须是某个临时晶体块的顶层，否则洞打不开。
        if (gates.Any(gate => !anchors.Contains(gate)))
        {
            return null;
        }

        // ── G11：保险丝柱 B′ 只在每个锚层放晶体 ──
        var bridge = new char[cap];
        Array.Fill(bridge, ShapeSignature.SolidCode);

        foreach (int anchor in anchors)
        {
            bridge[anchor] = ShapeSignature.CrystalCode;
        }

        // 闸门层若恰好也是针层，先试「闸门晶体让位给针」，再试「针让位给晶体」（有界两种）。
        for (int variant = 0; variant < 2; variant++)
        {
            // ── G5–G7：环伙伴 B 的针在环里（s+1），闸门层放晶体（紧列）──
            string? candidate = BuildRingPartnerColumn(
                targetColumn,
                cap,
                gates,
                s,
                gatePinWins: variant == 1);

            if (candidate is null)
            {
                continue;
            }

            AngleWorkOrder? order = ReplayConstructG(
                target,
                column,
                cap,
                kind,
                targetColumn,
                coreText,
                candidate,
                new string(bridge),
                drops,
                blocks,
                anchors,
                s);

            if (order is not null)
            {
                return order;
            }
        }

        return null;
    }

    /// <summary>
    /// 环伙伴 B：全实心，闸门层放<b>晶体</b>（紧列），每个「≥3 的晶体段」底端上一层放针（§15.2 G5–G7）。
    ///
    /// <para>
    /// 辅助列一律是紧列（docs §5 规则六 / §6 第 7 条）：早先把闸门层留洞的写法已撤回。
    /// 闸门晶体与目标列的临时晶体横向相连，半破时随整簇一起碎掉，
    /// 目标列只降一层（实测 <c>T.ccc</c>：<c>.TTPT</c> → <c>cTTPT</c>）。
    /// </para>
    /// <para>
    /// 针必须落在环里（<c>s+1</c>）：它不参与横向粘连 ⇒ 碎晶后上方晶体段靠
    /// 「针 ↔ 晶体段」的支撑环保持原位，下方的普通格则和 B 同层的实心格一起落到闸门层。
    /// 闸门层与针层冲突时，晶体不能与针并存：<paramref name="gatePinWins"/> 为 true
    /// 时保留针（仍是紧列），为 false 时该候选作废（交给下一变体）。
    /// </para>
    /// </summary>
    private static string? BuildRingPartnerColumn(
        string targetColumn,
        int cap,
        HashSet<int> gates,
        int s,
        bool gatePinWins = false)
    {
        var partner = new char[cap];
        Array.Fill(partner, ShapeSignature.SolidCode);

        // 每个 ≥3 目标晶体段的底端 + 1 放针（环内）。
        foreach ((int bottom, int length) in CrystalRuns(targetColumn))
        {
            if (length < 3)
            {
                continue;
            }

            int pin = bottom + 1;

            if (pin >= cap)
            {
                return null;
            }

            if (gates.Contains(pin) && gatePinWins)
            {
                continue;
            }

            partner[pin] = ShapeSignature.PinCode;
        }

        foreach (int gate in gates)
        {
            if (partner[gate] == ShapeSignature.PinCode)
            {
                // 针与晶体不能并存：针优先，保留针；否则交给下一个变体。
                if (!gatePinWins)
                {
                    return null;
                }

                continue;
            }

            // 闸门层放晶体（不是洞）：辅助列必须紧列，半破时整簇碎掉。
            partner[gate] = ShapeSignature.CrystalCode;
        }

        // 针必须有直接支撑（五项条件之一）。
        for (int layer = 1; layer < cap; layer++)
        {
            if (partner[layer] == ShapeSignature.PinCode &&
                partner[layer - 1] == ShapeSignature.EmptyCode)
            {
                return null;
            }
        }

        if (partner[0] == ShapeSignature.PinCode)
        {
            return null;
        }

        // G9 / G10：环在 [s, s+2] 闭合 —— B 侧 T-P-T 咬住目标侧 c-c-c。
        if (s + 2 >= cap ||
            partner[s] != ShapeSignature.SolidCode ||
            partner[s + 1] != ShapeSignature.PinCode ||
            partner[s + 2] != ShapeSignature.SolidCode)
        {
            return null;
        }

        return new string(partner);
    }

    #endregion

    #region G12–G14 + §15.3 不变量：真实 Cut 回放

    /// <summary>
    /// 把一份 G 候选跑真实 <see cref="ShapePhysics.Cut"/>，逐条核对 §15.3 的四条不变量。
    /// </summary>
    private static AngleWorkOrder? ReplayConstructG(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        string targetColumn,
        string coreText,
        string partnerText,
        string bridgeText,
        List<(int Core, int Target)> drops,
        List<(int Bottom, int Top)> blocks,
        List<int> anchors,
        int s)
    {
        const int parts = 4;
        const int half = 2;

        int holderColumn = Wrap(column + 1, parts);
        int farColumn = Wrap(column + 2, parts);
        int bridgeColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);
        char color = CrystalColor(target, column);

        ShapeDocument preState = BuildThreeColumnState(
            cap,
            parts,
            coreText,
            partnerText,
            color,
            column,
            holderColumn);

        /*
         * 辅助列最少化：第三象限整列为空（只留「支撑 B + 保险丝 B′」），
         * 与用户要求的「支撑 + 保险丝」一致。
         */

        WriteColumn(preState, bridgeColumn, bridgeText, color);

        // §6 验收 1–3：前态每列合法、整体稳定、目标列 ≠ 目标。
        if (!ShapeSignature.AllColumnsLegal(preState, out _) ||
            !ShapeOps.IsStable(preState) ||
            ShapeSignature.ColumnText(preState, column) == targetColumn)
        {
            return null;
        }

        ShapeDocument rotated = ShapeOps.Rotate(preState, rotation);

        int targetRotated = Wrap(column + rotation, parts);
        int holderRotated = Wrap(holderColumn + rotation, parts);
        int bridgeRotated = Wrap(bridgeColumn + rotation, parts);

        // G14：保留侧（west = 第 0、1 位）必须同时含目标列与 B。
        if (targetRotated >= half || holderRotated >= half)
        {
            return null;
        }

        // G12：B′ 必须落在被丢弃的一侧，且在每个锚层与目标列的闸门晶体横向相邻。
        if (bridgeRotated < half)
        {
            return null;
        }

        foreach (int anchor in anchors)
        {
            if (rotated[anchor, targetRotated].Type != CellType.Crystal ||
                rotated[anchor, bridgeRotated].Type != CellType.Crystal)
            {
                return null;
            }
        }

        CutResult cut = ShapePhysics.Cut(rotated);

        if (!cut.Success || cut.West is null)
        {
            return null;
        }

        ShapeDocument west = cut.West;

        if (!ShapeOps.IsStable(west) ||
            !ShapeSignature.AllColumnsLegal(west, out _))
        {
            return null;
        }

        // ── 不变量三：碎掉的只有临时晶体与保险丝，目标列的原生晶体一颗不少 ──
        int expectedCrystals = targetColumn.Count(
            code => code == ShapeSignature.CrystalCode);

        if (CrystalsInColumn(west, targetRotated) != expectedCrystals)
        {
            return null;
        }

        for (int layer = 0; layer < cap; layer++)
        {
            if (targetColumn[layer] == ShapeSignature.CrystalCode &&
                west[layer, targetRotated].Type != CellType.Crystal)
            {
                return null;
            }
        }

        // 辅助列的晶体必须全部被打碎（B 本来就没有晶体，B′ 的保险丝要全没）。
        if (CrystalsInColumn(west, bridgeRotated) != 0 ||
            CrystalsInColumn(west, holderRotated) != 0)
        {
            return null;
        }

        ShapeDocument result = ShapeOps.Rotate(west, -rotation);

        // ── 不变量一：最终列逐格等于目标（材料落回它该在的层）──
        if (!MatchesTargetColumn(result, target, column, cap, out _))
        {
            return null;
        }

        foreach ((int source, int destination) in drops)
        {
            // 每一枚被提升的普通格都落进它的目标层（逐格、含形状/颜色）。
            if (result[destination, column].Type != CellType.Shape ||
                !ShapeOps.SameCell(result[destination, column], preState[source, column]))
            {
                return null;
            }

            // 不变量二：落点以下的材料、源层以上的材料都保持原位。
            for (int layer = 0; layer < destination; layer++)
            {
                if (targetColumn[layer] == ShapeSignature.SolidCode &&
                    coreText[layer] == ShapeSignature.SolidCode &&
                    !ShapeOps.SameCell(result[layer, column], preState[layer, column]))
                {
                    return null;
                }
            }

            for (int layer = source + 1; layer < cap; layer++)
            {
                if (targetColumn[layer] == ShapeSignature.SolidCode &&
                    coreText[layer] == ShapeSignature.SolidCode &&
                    !ShapeOps.SameCell(result[layer, column], preState[layer, column]))
                {
                    return null;
                }
            }
        }

        // 不变量四：环正下方那枚普通格落走后，源层必须真的空出来
        // （它上面就是环锁定的晶体段，没有材料会补进来）。
        if (result[drops[^1].Core, column].Type != CellType.Empty)
        {
            return null;
        }

        // §6 验收 5：构造 C 交接后仍然稳定、目标列逐格不变。
        var handover = new ShapeDocument(cap, parts);

        for (int layer = 0; layer < cap; layer++)
        {
            handover[layer, column] = result[layer, column].Clone();
        }

        FillPillar(handover, holderColumn);

        if (!ShapeOps.IsStable(handover) ||
            !MatchesTargetColumn(handover, target, column, cap, out _))
        {
            return null;
        }

        return BuildConstructGOrder(
            target,
            column,
            kind,
            preState,
            result,
            handover,
            rotation,
            new[] { holderColumn, bridgeColumn },
            coreText,
            partnerText,
            bridgeText,
            drops,
            blocks,
            anchors,
            s);
    }

    #endregion

    #region 工序单

    private static AngleWorkOrder BuildConstructGOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        ShapeDocument preState,
        ShapeDocument cutResult,
        ShapeDocument handover,
        int rotation,
        int[] auxiliaryColumns,
        string coreText,
        string partnerText,
        string bridgeText,
        List<(int Core, int Target)> drops,
        List<(int Bottom, int Top)> blocks,
        List<int> anchors,
        int s)
    {
        var limitations = BaseLimitations(
            "构造 G（§15 四象限单孔环下落）的验证直接使用 ShapePhysics 的现有语义：" +
            "Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。" +
            "每个候选都逐格核对 §15.3 的四条不变量（只降一层 / 其余材料原位 / " +
            "只碎辅助晶体 / 空位搬回源层）。");

        string dropText = string.Join(
            "、",
            drops.Select(drop =>
                $"第 {drop.Core + 1} 层 → 第 {drop.Target + 1} 层" +
                $"（闸门 = 第 {drop.Core} 层）"));

        limitations.Add($"构造 G 的下落：{dropText}。");
        limitations.Add(
            $"环：目标列第 {s + 1}–{s + 3} 层的三连晶 ↔ 环伙伴第 {s + 1} 层 T、" +
            $"第 {s + 2} 层针、第 {s + 3} 层 T（针在环里，锁住晶体段）。");
        limitations.Add(
            "闸门（每次只降一层）：" +
            string.Join("、", drops.Select(drop => $"第 {drop.Core} 层在环伙伴放晶体")) +
            "；辅助列全程紧列，闸门晶体与临时晶体同簇碎掉。");
        limitations.Add(
            "保险丝：每个临时晶体块的顶层 " +
            string.Join("、", anchors.Select(anchor => $"第 {anchor + 1} 层")) +
            " 在保险丝柱放晶体，半破时与临时晶体一起碎掉；" +
            "它们都在环下方、且在目标里不是晶体（G8 / G8a）。");
        limitations.Add($"核心列 = {coreText}；环伙伴 B = {partnerText}；保险丝柱 B′ = {bridgeText}。");
        limitations.Add(
            "临时晶体块：" +
            string.Join(
                "、",
                blocks.Select(block => $"第 {block.Bottom + 1}–{block.Top + 1} 层")) +
            "（整块一起碎，不能只删其中一颗）。");

        var steps = new List<AngleWorkStep>
        {
            new(
                "构造 G 前态：提升 + 正规核心（临时晶体）+ 环伙伴 B（环内放针、闸门放晶体）" +
                " + 保险丝柱 B′（锚层放晶体）；第三列留空（辅助列最少化）",
                preState,
                AngleWorkStepKind.PreState),

            new(
                (rotation == 0
                    ? string.Empty
                    : $"先顺转 {rotation} 个象限，再") +
                "切割（碎晶并落定）：刀口穿过保险丝与闸门晶体的连接，临时晶体整块碎掉，" +
                "环内普通材料只降一层",
                cutResult,
                AngleWorkStepKind.Cut),

            new(
                "构造 C：托柱换成满高普通柱、拆掉环伙伴与保险丝柱，目标列逐格不变",
                handover,
                AngleWorkStepKind.Support),

            new(
                "清除辅助象限，得到目标角",
                target,
                AngleWorkStepKind.Target)
        };

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.G,
            Algorithm = AngleAlgorithm.Algorithm4,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Rank = 0,
            Limitations = limitations
        };
    }

    #endregion

    #region 列扫描小工具

    /// <summary>目标列里每个连续晶体段的（底端 0 起、段长）。</summary>
    private static IEnumerable<(int Bottom, int Length)> CrystalRuns(string column)
    {
        int index = 0;

        while (index < column.Length)
        {
            if (column[index] != ShapeSignature.CrystalCode)
            {
                index++;
                continue;
            }

            int start = index;

            while (index < column.Length &&
                   column[index] == ShapeSignature.CrystalCode)
            {
                index++;
            }

            yield return (start, index - start);
        }
    }

    /// <summary>一列里晶体的颗数。</summary>
    private static int CrystalsInColumn(ShapeDocument document, int column)
    {
        int count = 0;

        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            if (document[layer, column].Type == CellType.Crystal)
            {
                count++;
            }
        }

        return count;
    }

    #endregion
}

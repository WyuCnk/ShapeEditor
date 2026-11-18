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
    /// <summary>
    /// 模板 B 的「不要盲切」比较闸门：为 true 时正在用现有管线算「原路线」，
    /// 这次比较里不得再触发模板 B（否则会无限递归）。
    /// </summary>
    [ThreadStatic]
    private static bool comparingOneCutTight;

    /// <summary>
    /// O-v2 碎晶路线的入口；返回 null 表示这条路线不适用或回放失败。
    ///
    /// <para>
    /// <paramref name="allowOneCutTight"/> 为 <c>false</c> 时只跑模板 A：
    /// 算法 4 的论文首选是构造 G（也是单刀碎晶），模板 B 只在 G / 通用实现都失败后补位，
    /// 以免把 G 已经做得更好（M5 换柱更少）的列换成模板 B。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildCrystalBreakOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        bool allowOneCutTight = true)
    {
        if (!TryBuildCrystalBreakCore(
                target,
                column,
                cap,
                kind,
                out string coreText,
                out string targetColumn,
                out List<(int Bottom, int Top)> blocks))
        {
            return null;
        }

        // 先试「一刀碎全部块」（O-v2 §2 的规范形态，模板 A）。
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

        /*
         * 模板 A 失败：先按模板 B（OneCutTight，docs §8.2）再判一次、再试一次。
         *
         * 模板 B 与模板 A 的区别只在辅助列 S：模板 A 一律「满高普通格 + 源层放针」，
         * 它假设被提升的普通格都能在本列找到刹车；模板 B 显式判「悬空落点」，
         * 命中时把落体做成【主列普通格 + 辅助列普通格】的双格，由辅助列自己的挡块停住。
         * 双格下落比单格多降一层，才可能落进悬空落点。
         */
        if (allowOneCutTight &&
            AngleWorkOrderDiagnostics.EnableOneCutTight)
        {
            AngleWorkOrder? tightOrder = TryBuildOneCutTightOrder(
                target,
                column,
                cap,
                kind,
                coreText,
                targetColumn,
                blocks);

            if (tightOrder is not null)
            {
                return tightOrder;
            }
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
    /// 模板 B 的独立入口：算法 4 的构造 G / 通用实现都失败后，再试一次模板 B。
    /// 正规核心与临时块与模板 A 完全相同（同一份 A）。
    /// </summary>
    private static AngleWorkOrder? TryBuildOneCutTightCandidate(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (!AngleWorkOrderDiagnostics.EnableOneCutTight ||
            !TryBuildCrystalBreakCore(
                target,
                column,
                cap,
                kind,
                out string coreText,
                out string targetColumn,
                out List<(int Bottom, int Top)> blocks))
        {
            return null;
        }

        return TryBuildOneCutTightOrder(
            target,
            column,
            cap,
            kind,
            coreText,
            targetColumn,
            blocks);
    }

    /// <summary>
    /// 碎晶路线的公共前置扫描：判定形态、建正规核心、找临时晶体块。
    /// 模板 A / B 用的是同一份核心，所以抽在这里。
    /// </summary>
    private static bool TryBuildCrystalBreakCore(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        out string coreText,
        out string targetColumn,
        out List<(int Bottom, int Top)> blocks)
    {
        coreText = string.Empty;
        targetColumn = string.Empty;
        blocks = new List<(int Bottom, int Top)>();

        if (target.PartCount != 4 ||
            cap < target.LayerCount ||
            kind != AngleBuildKind.NeedsCrystalBreak)
        {
            return false;
        }

        string tight = ColumnTextPadded(target, column, target.LayerCount);
        targetColumn = ColumnTextPadded(target, column, cap);

        if (!ShapeSignature.CheckColumn(tight).IsLegal ||
            !targetColumn.Contains(ShapeSignature.CrystalCode) ||
            targetColumn.Contains(ShapeSignature.PinCode))
        {
            return false;
        }

        int highest = targetColumn.LastIndexOf(ShapeSignature.CrystalCode);

        if (highest <= 0)
        {
            return false;
        }

        // ── ① 正规核心：提升 + 填临时晶体（全部由扫描得出）──
        int[] destinations = ResolveRecursiveLiftPlan(targetColumn, highest);
        char[]? core = BuildCoreColumn(targetColumn, destinations, highest);

        if (core is null)
        {
            return false;
        }

        coreText = new string(core);

        if (!ShapeSignature.IsTightColumn(coreText) ||
            !ShapeSignature.CheckColumn(coreText).IsLegal)
        {
            return false;
        }

        blocks = FindTemporaryBlocks(targetColumn, core);

        /*
         * 没有提升记录也可以走：晶体直接坐在普通格上、洞在普通格下方时
         * （例如 T..Tc），核心只是把洞填成临时晶体，一刀碎掉、普通格原位不动。
         * 这时托柱没有针（满高普通格）也能通过回放。
         */
        return blocks.Count != 0;
    }

    #region 模板 B：OneCutTight（docs/angle-route-optimal-v2.md §8.2）

    /*
     * ── 引理（docs/angle-route-optimal-v2.md §7.2，推理方 2026-11-18）──
     *
     * 在一次「不重新分组」的下落中，活动块经过固定格时，新出现的侧面接触不会使它停住；
     * 挡停只能发生在活动块所占某一列的正下方，或地面。
     * ⇒ 单格只能在本列受挡；跨两列的双格可以在邻列受挡。
     *
     * 这条引理解释了模板 A 为什么会漏列：它没有区分「单格自停」与「双格借邻列挡停」。
     * 在【悬空落点】（落点下面一格是空）上，单格只能一路落到地面或同列材料上，
     * 于是模板 A 的「源层放针」托柱救不了它；模板 B 把落体扩成
     * 【主列普通格 + 辅助列普通格】的双格，由辅助列自己的挡块停住。
     * 双格要成立，上方必须至少 3 连晶当支撑环（§8.2 的判定条件）。
     */

    /// <summary>目标列里一个「正下方为空」的晶体段记录（模板 B 的 O(H) 扫描产物）。</summary>
    private readonly record struct OneCutTightSegment(
        int Bottom,
        int Top,
        int Landing,
        bool Hung);

    /// <summary>
    /// 模板 B（<c>OneCutTight</c>）的判定谓词（docs §8.2）：
    /// 该目标列是否存在「一刀 + 双格环下落」的前态。
    ///
    /// <para>
    /// 复杂度 <b>O(H)</b>、额外空间 <b>O(1)</b>，不打表、不枚举；
    /// 只依赖目标列本身（不需要生成前态）。
    /// </para>
    /// </summary>
    public static bool MatchesOneCutTight(string targetColumn)
    {
        return MatchesOneCutTightPredicate(targetColumn, out _);
    }

    /// <summary>
    /// 模板 B（<c>OneCutTight</c>）的判定（docs §8.2），并给出悬空落点记录。
    ///
    /// <para>自底向上扫「正下方为空」的晶体段，<c>d = 该段下方最近普通格的目标层</c>；</para>
    /// <list type="bullet">
    /// <item><c>d &gt; 0</c> 且 <c>t[d−1]</c> 为空 ⇒ 该段是【悬空落点】；</item>
    /// <item>没有悬空落点 ⇒ 通过（被提升格都能在本列自停，直接释放）；</item>
    /// <item>恰有一个 ⇒ 需该段 ≥3 连晶，且它是第一条记录，或第二条记录且
    /// 「地面到第一条记录的 d 层」连续非空；</item>
    /// <item>两个及以上 ⇒ 不通过。</item>
    /// </list>
    /// <para>
    /// 复杂度 <b>O(H)</b>、额外空间 <b>O(1)</b>：一趟自底向上的扫描，
    /// 只记「最近普通格层」与几个计数；晶体段一次跳到段顶，不打表、不枚举。
    /// </para>
    /// </summary>
    private static bool MatchesOneCutTightPredicate(
        string targetColumn,
        out OneCutTightSegment hung)
    {
        hung = default;

        int highest = targetColumn.LastIndexOf(ShapeSignature.CrystalCode);

        if (highest < 0)
        {
            return false;
        }

        int records = 0;
        int firstLanding = -1;
        int hungRecords = 0;
        int hungRecordIndex = -1;
        int lastSolid = -1;

        for (int layer = 0; layer <= highest; layer++)
        {
            char code = targetColumn[layer];

            if (code == ShapeSignature.SolidCode)
            {
                lastSolid = layer;
                continue;
            }

            if (code != ShapeSignature.CrystalCode ||
                layer == 0 ||
                targetColumn[layer - 1] != ShapeSignature.EmptyCode)
            {
                continue;
            }

            // 段底：向上量到段的最高层。段内没有普通格，所以可以直接跳到段顶。
            int top = layer;

            while (top + 1 <= highest &&
                   targetColumn[top + 1] == ShapeSignature.CrystalCode)
            {
                top++;
            }

            int landing = lastSolid;
            bool isHung = landing > 0 &&
                          targetColumn[landing - 1] == ShapeSignature.EmptyCode;

            if (records == 0)
            {
                firstLanding = landing;
            }

            if (isHung)
            {
                hungRecords++;
                hungRecordIndex = records;
                hung = new OneCutTightSegment(layer, top, landing, true);
            }

            records++;
            layer = top;
        }

        if (hungRecords == 0)
        {
            return true;
        }

        if (hungRecords > 1)
        {
            return false;
        }

        if (hung.Top - hung.Bottom + 1 < 3)
        {
            return false;
        }

        if (hungRecordIndex == 0)
        {
            return true;
        }

        if (hungRecordIndex != 1 || firstLanding < 0)
        {
            return false;
        }

        for (int layer = 0; layer <= firstLanding; layer++)
        {
            if (targetColumn[layer] == ShapeSignature.EmptyCode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 模板 B（<c>OneCutTight</c>）的候选：模板 A 的一刀回放失败后再判一次。
    ///
    /// <para><b>前态构造</b>（docs §8.2 / 任务书任务 1）：</para>
    /// <code>
    /// A = 正规核心（空位填临时晶体，c/T 交替）
    /// S = 双格托柱：悬空落点的落体做成「主列普通格 + 辅助列普通格」，
    ///     落点下一层放普通格当刹车，落点到伙伴层之间放临时晶体（切开后成洞）；
    ///     其余被提升格一律在整段下落路径放针，保持单格、不粘连
    /// 第 3 象限 = 空
    /// F = 保险丝：紧列到最高锚层，每个临时段锚层放一颗晶体
    /// 一刀
    /// </code>
    /// <para>
    /// 前态完全由扫描得出（提升记录 + 悬空落点记录），没有打表、没有枚举；
    /// 一刀用真实 <see cref="ShapePhysics.Cut"/> 回放，末端目标列逐格核对。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildOneCutTightOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        string coreText,
        string targetColumn,
        IReadOnlyList<(int Bottom, int Top)> blocks)
    {
        // 正在用现有管线算「原路线」：这次比较里不得再产出模板 B。
        if (comparingOneCutTight)
        {
            return null;
        }

        if (!MatchesOneCutTightPredicate(targetColumn, out OneCutTightSegment hung))
        {
            return null;
        }

        List<(int Core, int Target)> lifts = FindLifts(
                targetColumn,
                coreText.ToCharArray())
            .Where(lift => lift.Core > lift.Target)
            .ToList();

        int hungCore = hung.Hung ? hung.Bottom - 1 : -1;

        if (hung.Hung &&
            (hungCore < 0 ||
             hungCore >= coreText.Length ||
             coreText[hungCore] != ShapeSignature.SolidCode))
        {
            return null;
        }

        if (!BuildOneCutTightSupport(
                cap,
                lifts,
                hungCore,
                targetColumn,
                out string support))
        {
            return null;
        }

        const int parts = 4;

        int holderColumn = Wrap(column + 1, parts);
        int bridgeColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);
        char color = CrystalColor(target, column);

        string fuse = BuildCrystalBreakFuse(cap, blocks);

        var state = new ShapeDocument(cap, parts);

        WriteColumn(state, column, coreText, color);
        WriteColumn(state, holderColumn, support, color);
        WriteColumn(state, bridgeColumn, fuse, color);

        if (!ShapeSignature.AllColumnsLegal(state, out _) ||
            !ShapeOps.IsStable(state))
        {
            return null;
        }

        if (FuseTouchesPreservedCrystal(
                state,
                column,
                bridgeColumn,
                blocks.Select(block => block.Top).ToList(),
                targetColumn))
        {
            return null;
        }

        ShapeDocument? afterCut = CutAndRotateBack(state, rotation, out _);

        if (afterCut is null ||
            !ShapeOps.IsStable(afterCut) ||
            !ShapeSignature.AllColumnsLegal(afterCut, out _) ||
            !NativeCrystalsPreserved(afterCut, target, column) ||
            !AuxiliaryCrystalsCleared(afterCut, holderColumn, bridgeColumn) ||
            ColumnTextPadded(afterCut, column, cap) != targetColumn)
        {
            return null;
        }

        var steps = new List<AngleWorkStep>
        {
            new(
                "模板 B（OneCutTight）前态：A = 正规核心；S = 双格托柱" +
                (hung.Hung
                    ? $"（悬空落点：第 {hung.Bottom + 1}–{hung.Top + 1} 层晶体段，落点第 " +
                      $"{hung.Landing + 1} 层，双格由第 {hung.Landing} 层的辅助列挡块停住）"
                    : "（没有悬空落点，被提升格都在本列自停、直接释放）") +
                "；第 3 象限空；F = 保险丝（每个临时段锚层一颗晶体）" +
                $"；四列短代码 = {ExportCodes(state)}",
                state,
                AngleWorkStepKind.PreState),
            new(
                "一刀（真实 Cut 后落定）：跨刀口的晶体整簇碎掉，被提升的普通格落回目标层" +
                (hung.Hung ? "（悬空落点处由双格借辅助列挡停）" : string.Empty) +
                $"；目标列 = {ColumnTextPadded(afterCut, column, cap)}",
                afterCut,
                AngleWorkStepKind.Cut)
        };

        AngleWorkOrder? candidate = BuildCrystalBreakOrder(
            target,
            column,
            cap,
            kind,
            steps,
            afterCut,
            holderColumn,
            bridgeColumn,
            rotation,
            blocks,
            multiCut: false,
            oneCutTight: true);

        if (candidate is null)
        {
            return null;
        }

        return IsBetterThanAlternativeRoute(candidate, target, column, cap)
            ? candidate
            : null;
    }

    /// <summary>
    /// 「不要盲切」闸门（docs/work-order-metrics.md §1 / docs §7.1 的完整口径）：
    /// 模板 B 的 S 里可能预制了针（M2 &gt; 0），也可能比同样单刀的构造 G 多一次换柱。
    /// 所以只有按 M1 → M7 字典序<b>不差于原路线</b>时才切到模板 B。
    ///
    /// <para>
    /// 「原路线」= 把模板 B 关掉、用现有管线跑出来的第一条可行工序。
    /// 比较时两个指标都用修正后的 M2（含辅助列里预制的针）。
    /// </para>
    /// </summary>
    private static bool IsBetterThanAlternativeRoute(
        AngleWorkOrder candidate,
        ShapeDocument target,
        int column,
        int cap)
    {
        // 比较用的「原路线」本身不得再触发模板 B（否则无限递归）。
        if (comparingOneCutTight)
        {
            return true;
        }

        AngleWorkOrder alternative;

        comparingOneCutTight = true;

        try
        {
            alternative = BuildCore(target, column, cap, true, true);
        }
        finally
        {
            comparingOneCutTight = false;
        }

        if (!alternative.IsComplete)
        {
            return true;
        }

        return CompareLexicographic(candidate.Metrics, alternative.Metrics) <= 0;
    }

    /// <summary>M1 → M7 字典序：负数表示 left 更优，0 表示打平，正数表示 left 更差。</summary>
    private static int CompareLexicographic(
        AngleWorkOrderMetrics left,
        AngleWorkOrderMetrics right)
    {
        IReadOnlyList<int> a = left.ToVector();
        IReadOnlyList<int> b = right.ToVector();

        for (int index = 0; index < a.Count; index++)
        {
            if (a[index] != b[index])
            {
                return a[index] < b[index] ? -1 : 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// 模板 B 的托柱：
    /// <list type="bullet">
    /// <item><b>悬空落点</b>（<paramref name="hungCore"/> ≥ 0）：伙伴层 = 核心层放普通格，
    /// 刹车层 = 落点下一层放普通格，中间 <c>[落点 .. 核心层−1]</c> 放临时晶体
    /// （与主列临时段一齐被这一刀碎掉，腾出双格的下落通道）；</item>
    /// <item><b>自停落点</b>：<c>[落点 .. 核心层]</c> 整段放针，被提升格保持单格、
    /// 与辅助列不粘连，落点由本列材料停住；</item>
    /// <item>双格掉走以后，最高原生晶体段的中层放一枚针，造「环」把它挂住
    /// （§7.2：针不参与水平粘连，但上下都是同一组的普通格 ⇒ 成环 ⇒ 不下落）。</item>
    /// </list>
    /// </summary>
    private static bool BuildOneCutTightSupport(
        int cap,
        IReadOnlyList<(int Core, int Target)> lifts,
        int hungCore,
        string targetColumn,
        out string support)
    {
        support = string.Empty;

        if (cap <= 0)
        {
            return false;
        }

        char[] codes = new char[cap];
        Array.Fill(codes, ShapeSignature.SolidCode);

        foreach ((int core, int targetLayer) in lifts)
        {
            if (core == hungCore)
            {
                if (targetLayer <= 0 ||
                    targetLayer >= core ||
                    core >= cap)
                {
                    return false;
                }

                // 伙伴层（普通格）与刹车层（普通格）本身就是 T，这里写明是为了可读性。
                codes[core] = ShapeSignature.SolidCode;
                codes[targetLayer - 1] = ShapeSignature.SolidCode;

                for (int layer = targetLayer; layer < core; layer++)
                {
                    codes[layer] = ShapeSignature.CrystalCode;
                }

                continue;
            }

            if (core >= cap || targetLayer < 0)
            {
                return false;
            }

            for (int layer = targetLayer; layer <= core; layer++)
            {
                codes[layer] = ShapeSignature.PinCode;
            }
        }

        if (hungCore >= 0)
        {
            if (!TryFindOneCutTightAnchor(
                    targetColumn,
                    hungCore,
                    out int anchorMiddle))
            {
                return false;
            }

            codes[anchorMiddle] = ShapeSignature.PinCode;
        }

        support = new string(codes);
        return true;
    }

    /// <summary>
    /// 双格掉走以后，上方（原生晶体段 + 辅助列上半段）失去同列支撑；
    /// 取「<paramref name="aboveLayer"/> 以上、最高的原生晶体段」，要求 ≥3 连晶，
    /// 返回它的中层 —— 在那里放针就能让辅助列上下两格与晶体段成环。
    /// </summary>
    private static bool TryFindOneCutTightAnchor(
        string targetColumn,
        int aboveLayer,
        out int anchorMiddle)
    {
        anchorMiddle = -1;

        int highest = targetColumn.LastIndexOf(ShapeSignature.CrystalCode);
        int bestBottom = -1;
        int bestTop = -1;

        for (int layer = 0; layer <= highest; layer++)
        {
            if (targetColumn[layer] != ShapeSignature.CrystalCode ||
                (layer > 0 &&
                 targetColumn[layer - 1] == ShapeSignature.CrystalCode))
            {
                continue;
            }

            int top = layer;

            while (top + 1 <= highest &&
                   targetColumn[top + 1] == ShapeSignature.CrystalCode)
            {
                top++;
            }

            if (layer > aboveLayer && layer > bestBottom)
            {
                bestBottom = layer;
                bestTop = top;
            }

            layer = top;
        }

        if (bestBottom < 0 || bestTop - bestBottom + 1 < 3)
        {
            return false;
        }

        int middle = bestBottom + ((bestTop - bestBottom + 1) / 2);

        if (middle - 1 < bestBottom || middle + 1 > bestTop)
        {
            return false;
        }

        anchorMiddle = middle;
        return true;
    }

    #endregion

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
        bool multiCut,
        bool oneCutTight = false)
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
            oneCutTight
                ? "O-v2 碎晶路线·模板 B（OneCutTight，docs/angle-route-optimal-v2.md §8.2）：" +
                  "A = 正规核心、S = 双格托柱、F = 保险丝（块锚层放晶体），" +
                  "全部由沿列扫描得出（O(H)，不打表、不枚举）；" +
                  "一刀用真实 ShapePhysics.Cut 回放，末列逐格核对。"
                : "O-v2 碎晶路线（docs/angle-route-optimal-v2.md §2）：A = 正规核心、" +
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
            oneCutTight
                ? "模板 B 的 S：悬空落点处的落体是【主列普通格 + 辅助列普通格】的双格，" +
                  "落点下一层放普通格当刹车；落点到伙伴层之间放临时晶体，切开后成洞。" +
                  "其余提升格在整段路径放针，保持单格。双格掉走后，由最高原生晶体段中层的针" +
                  "与辅助列上下两格成环，把上方材料挂住。"
                : "S 的针位 = 被提升格的源层（落点层放针的等价写法）：" +
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

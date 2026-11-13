using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 半边工序的候选（docs/half-side-work-order.md §4.2）。
///
/// <para>
/// 论文算法 8 只给了两条候选（目标自身 / 联合逆提升），对 <c>cT.T.cPT</c>
/// 这一格两条都拒绝，但半边确实可得 ⇒ 需要第三条候选。
/// </para>
/// <para>
/// <b>k = n/2 列推广（P3）</b>：n ≥ 6 时半边是连续 3 列以上，三个候选的判定
/// 与施工都按「半边宽度 = n/2」推广；n = 4（k = 2）的判定与布局保持原样。
/// </para>
/// </summary>
public enum HalfCandidate
{
    /// <summary>算法 8 第一候选：Q 自身（每列都属 S 且 Q 稳定、一次下落仍是 Q）。</summary>
    Algo8Self,

    /// <summary>算法 8 第二候选：联合逆提升（把一对相邻普通格整体抬高）。</summary>
    Algo8Uplift,

    /// <summary>
    /// 候选 3：临时晶体 + 保险丝 + 一刀。
    /// 目标列把 (s−2, s) 之间的普通格提到 s−1、洞填临时晶体；
    /// 环伙伴在顶部三层与目标列咬合；保险丝柱在临时晶体每一块的顶层各放一颗晶体跨刀口。
    /// </summary>
    TempCrystalFuse
}

/// <summary>
/// 半边工序的目标：一段<b>连续 n/2 列</b>（对齐交换链的轴划分）。
///
/// <paramref name="Columns"/> 是原始象限索引，按环顺序排列（k = n/2 列全部列入）；
/// <paramref name="PrimaryColumn"/> 是本次求解的那个角所在象限
/// （半边里第一个被核对的列）。
/// </summary>
public sealed record WorkOrderTarget(
    IReadOnlyList<int> Columns,
    int PrimaryColumn);

/// <summary>
/// 半边工序（k 列一起做，k = n/2）。
///
/// <para>
/// 半边 = 连续 n/2 列（n = 4 时是 2 列，n = 6 时是 3 列，n = 8 时是 4 列）。
/// 本记录描述「k 列一起做」的那一刀（或那次完整下落）。
/// 子配方最多一层（docs/angle-work-order-algorithm.md §5.1），
/// 所以这里<b>不递归</b>：<see cref="Steps"/> 的前态全是紧列 / S 列。
/// </para>
/// <para>
/// <see cref="Note"/> 为 null 表示 k 列都逐格回放通过；非 null 时一律以
/// <see cref="AngleWorkOrderBuilder.UncoveredPrefix"/> 开头（候选命中形态、
/// 但回放没过），且 <see cref="IsComplete"/> 为 false。
/// </para>
/// </summary>
public sealed record HalfWorkOrder(
    WorkOrderTarget Target,
    HalfCandidate Candidate,
    IReadOnlyList<AngleWorkStep> Steps,
    bool IsComplete,
    string? Note,
    IReadOnlyList<string> Limitations,
    int Rank)
{
    /// <summary>
    /// k 列是否都是「带子配方」的目标列（算法 8 的候选 1 / 2）。
    ///
    /// <para>
    /// 候选 1 / 2 的前态就是 k 根 <b>S 列</b>：每一列都能按单角工序（目标列 + 托柱）
    /// 单独做出来，再直接拼合，所以 k 列都允许带洞（docs §6 第 7 条：
    /// 带子配方的列不要求紧列）。
    /// </para>
    /// <para>
    /// 候选 3 在 n = 4 时只有 Q1 是目标列，环伙伴 Q2 与保险丝 Q4 是论文意义上的
    /// 「辅助列」，必须紧列，因此该候选保持 <c>false</c>；
    /// n ≥ 6 时半边的每一列都是整图里真实存在的列（整图每列合法 ⇒ 每列都是 S），
    /// 只有环伙伴 / 保险丝是施工出来的辅助列，所以该标记为 <c>true</c>。
    /// </para>
    /// </summary>
    public bool TargetColumnsHaveSubRecipe { get; init; }

    /// <summary>
    /// 候选 3 切割前需要顺转的象限数（顺时针为正，与 <see cref="ShapeOps.Rotate"/> 一致）；
    /// 候选 1 / 2 不切割，恒为 0。
    ///
    /// <para>
    /// 装配层要靠它把「前态」摆到固定刀口的一侧：
    /// <c>Cut(Rotate(前态, Rotation)).West</c> 就是目标半边。
    /// </para>
    /// </summary>
    public int Rotation { get; init; }
}

/// <summary>
/// 角工序生成器（续）：<b>半边工序</b>（docs/half-side-work-order.md）。
///
/// <para><b>分层</b>（§4.1 / P1 + P2 + P3 的路由）</para>
/// <code>
/// 五项 ✘                         → NotProducible
/// n ≥ 6                          → 现有算法（不变；半边只在 BuildHalf / 装配层使用）
/// n = 4 且 S ✔                   → 现有角工序（不变）
/// n = 4 且 S ✘ 但五项 ✔          → 半边工序，按论文顺序试三个候选：
///                                   候选 1（Q 自身）→ 候选 2（联合逆提升）
///                                   → 候选 3（临时晶体 + 保险丝 + 一刀）
///                                   ├ 任一成功 → 角工序 + Half 字段
///                                   └ 全失败   → NotProducible（保持现状语义）
/// </code>
///
/// <para><b>k = n/2 列推广（P3）</b>：半边宽度不再是常量 2，而是 <c>n/2</c>：
/// <see cref="HalfProjection"/> 取从 <c>column</c> 起的连续 k 列，其余象限置空；
/// 三个候选的判定与「半边目标」都按 k 列核对。n = 4 时 k = 2，所有布局与
/// 判定退化成原来的两列版本（逐字一致）。</para>
///
/// <para><b>候选 1 / 2（论文算法 8，spec §2.7）</b></para>
/// <list type="number">
/// <item><b>候选 1 Q 自身</b>：半边 k 列都属于该 n 的 S（n = 4 用五项 ∧ (D∨F)；
/// n ≥ 6 时 S ⟺ 五项），且 Q 稳定（一次下落后仍等于 Q）⇒ 前态就是 Q，
/// 步骤链 <c>PreState → Target</c>，无需切割。</item>
/// <item><b>候选 2 联合逆提升</b>：k 列各有晶体且最高晶体<b>不全等高</b>；
/// 取最低那枚的最大粘连组 G、最低层 b；在 b 以下找最高的非空层 a，
/// 要求 a 层至少有一对<b>相邻</b>列都是普通格（水平 <c>TT</c>，不必全列）、
/// <c>a &lt; b−1</c>、<c>a+1 … b−1</c> 所有列都空；把那对普通格整体提到 b−1 得 Q′；
/// 再要求 Q′ 的 k 列都属于 S 且<b>真实一次下落</b>恰好得到 Q。</item>
/// </list>
///
/// <para><b>候选 3 的实测配方</b>（n = 4，目标 <c>cT.T.cPT</c>，8 层）</para>
/// <code>
/// Q1 目标列   cTccTcPT   ← 把 s−2 的普通格提到 s−1，洞全填临时晶体
/// Q2 环伙伴   TTTcTccc   ← 顶部三层 ccc 与 Q1 的 c-P-T 咬合成环
/// Q3 （空）
/// Q4 保险丝柱 TTTc....   ← 只在临时晶体那一层（s−2）放一颗晶体，跨刀口
/// 一刀半破 → Q1 = cT.T.cPT ✔，Q2 = TTTT.ccc
/// </code>
/// <para>
/// <b>k ≥ 3 的推广</b>：环伙伴仍取半边内部与目标列相邻的那一列（<c>column+1</c>），
/// 环因此完全落在同一半边内部；半边的其余列（<c>column+2 … column+k−1</c>）
/// 原样保留，由真实回放核对；保险丝柱按<b>每个临时晶体块的顶层</b>各放一颗晶体
/// （n = 4 只有一个块，退化成原来的一颗）。
/// </para>
///
/// <para><b>为什么这样能成</b></para>
/// <list type="number">
/// <item>Q1 的临时晶体与 Q2 / Q4 的晶体同属跨刀口连通块，半破时整块碎掉；
/// Q1 的原生晶体都在西侧、不跨刀口，一颗不少。</item>
/// <item>Q1 抬到 s−1 的普通格与 Q2 同层普通格横向粘连，两格成一组整体下落一层；
/// Q2 在 s−3 的普通格当刹车，双格恰好停在 s−2（即目标层）。</item>
/// <item>环顶：Q1 的 s+1 是顶针（不参与横向粘连），Q2 在 s..s+2 的竖直三连晶
/// 从上方绕过去，与 Q1 的 s 层晶体、s+2 层普通格构成「针 ↔ 晶体段」的循环支撑，
/// 碎晶后顶层晶体段保持原位。</item>
/// </list>
///
/// <para>
/// <b>禁止打表</b>：<c>s</c> 由沿列扫描得出（最高晶体层），其余层号都是相对
/// <c>s</c> 的结构偏移；分支谓词只有「顶部三层是不是 c-P-T」「s−1 是不是洞」
/// 「临时晶体块是否与原生晶体纵向融合」这类扫描结论。
/// </para>
/// </summary>
public static partial class AngleWorkOrderBuilder
{
    /// <summary>半边的最小象限数（n = 4 才有两列半边的特化；n ≥ 6 走 k 列推广）。</summary>
    private const int HalfMinParts = 4;

    #region 候选派发（论文顺序：1 → 2 → 3）

    /// <summary>
    /// 半边工序的候选派发：论文算法 8 的候选 1、候选 2，最后才是本仓库补的候选 3。
    ///
    /// <para>
    /// 每个候选都自己完成「扫描判定 → 造前态 → 真实物理回放 → 逐格核对」；
    /// 返回 null 表示该候选不适用或回放失败，调用方继续试下一个。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildHalfOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        if (target.PartCount < HalfMinParts ||
            target.PartCount % 2 != 0 ||
            cap < target.LayerCount)
        {
            return null;
        }

        // 论文优先：第一候选（Q 自身）→ 第二候选（联合逆提升）→ 第三候选（兜底）。
        AngleWorkOrder? order = TryBuildAlgo8Self(
            target,
            column,
            cap,
            kind);

        if (order is not null)
        {
            return order;
        }

        order = TryBuildAlgo8Uplift(
            target,
            column,
            cap,
            kind);

        if (order is not null)
        {
            return order;
        }

        return TryBuildTempCrystalFuse(
            target,
            column,
            cap,
            kind);
    }

    /// <summary>
    /// 独立入口：把一段半边（连续 n/2 列）按「候选 1 → 2 → 3」求解，
    /// 返回第一个通过真实回放的半边工序。
    ///
    /// <para>
    /// 角工序的 <see cref="Build"/> 只在「n = 4、五项 ✔ 但 S ✘」时进入半边分支，
    /// 因此候选 1（要求 k 列都属于 S）不可能经由 <see cref="Build"/> 命中；
    /// 它需要这个不经过单角 S 闸门的入口（交换链 / 整图判定也按定理 6 直接讨论半边）。
    /// n ≥ 6 时同样由这个入口给出 k 列半边（整图装配层使用）。
    /// </para>
    /// <para>
    /// 返回 null 表示三个候选都不适用或回放失败；不等于「半边不可得」。
    /// </para>
    /// </summary>
    public static HalfWorkOrder? BuildHalf(
        ShapeDocument target,
        int column,
        int cap)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (column < 0 || column >= target.PartCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        AngleWorkOrder? order = TryBuildHalfOrder(
            target,
            column,
            cap,
            AngleClassifier.Classify(target, column));

        if (order is not { IsComplete: true } || order.Half is null)
        {
            return null;
        }

        /*
         * BuildHalf 的语义是「<b>这个目标</b>的那 k 列半边可得」：
         * 候选 3 会把环伙伴列改写成构造形态（那是给单角 Build 用的），
         * 若它给出的半边与目标的这 k 列不一致，就不能算这份目标的半边。
         * 逐格比较（类型 / 形状码 / 颜色），层数不同时多出来的层必须为空。
         */
        if (!SameHalfColumns(
                order.Half.Steps[^1].Shape,
                target,
                HalfColumns(column, target.PartCount),
                target.PartCount))
        {
            return null;
        }

        return order.Half;
    }

    /// <summary>
    /// 逐格比较「某 k 列」：类型 / 形状码 / 颜色都要一致；
    /// 两侧层数不同时，多出来的层必须为空。
    /// </summary>
    private static bool SameHalfColumns(
        ShapeDocument? shape,
        ShapeDocument? other,
        int[] columns,
        int parts)
    {
        if (shape is null || other is null)
        {
            return false;
        }

        int layers = Math.Max(shape.LayerCount, other.LayerCount);

        for (int layer = 0; layer < layers; layer++)
        {
            foreach (int column in columns)
            {
                ShapeCell left = layer < shape.LayerCount
                    ? shape[layer, Wrap(column, parts)]
                    : ShapeCell.Empty();

                ShapeCell right = layer < other.LayerCount
                    ? other[layer, Wrap(column, parts)]
                    : ShapeCell.Empty();

                if (!ShapeOps.SameCell(left, right))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 把目标里的半边（从 <paramref name="column"/> 起连续 n/2 列）投影成独立图形，
    /// 并补足到 <paramref name="cap"/> 层（其余象限整列为空）。
    /// 题设的半边 Q 只讨论这 k 列，目标其余象限不属于本次半边工序。
    /// </summary>
    private static ShapeDocument HalfProjection(
        ShapeDocument target,
        int column,
        int cap)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        var half = new ShapeDocument(cap, parts);

        for (int layer = 0;
             layer < target.LayerCount && layer < cap;
             layer++)
        {
            for (int index = 0; index < width; index++)
            {
                int part = Wrap(column + index, parts);

                half[layer, part] = target[layer, part].Clone();
            }
        }

        return half;
    }

    /// <summary>半边覆盖的 k 个象限索引（按环顺序）。</summary>
    private static int[] HalfColumns(
        int column,
        int parts)
    {
        int width = parts / 2;
        var columns = new int[width];

        for (int index = 0; index < width; index++)
        {
            columns[index] = Wrap(column + index, parts);
        }

        return columns;
    }

    /// <summary>
    /// 某个 n 下的「带柱列 S」判据：
    /// n = 4 用论文定理 3（五项 ∧ (D∨F)）；n ≥ 6 用定理 2（S ⟺ 五项）。
    /// </summary>
    private static bool IsSColumn(
        string text,
        int parts)
    {
        return parts >= 6
            ? ShapeSignature.CheckColumn(text).IsLegal
            : PillarSCriterion.IsPillarColumn(text);
    }

    /// <summary>k 列是否全部都属于该 n 的 S。</summary>
    private static bool AllHalfColumnsAreS(
        ShapeDocument half,
        int column,
        int parts)
    {
        int width = parts / 2;

        for (int index = 0; index < width; index++)
        {
            int part = Wrap(column + index, parts);

            if (!IsSColumn(ShapeSignature.ColumnText(half, part), parts))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>某一列最高晶体的层号（0 起），没有晶体返回 −1。层号由沿列扫描得出。</summary>
    private static int HighestCrystal(
        ShapeDocument shape,
        int column)
    {
        for (int layer = shape.LayerCount - 1;
             layer >= 0;
             layer--)
        {
            if (shape[layer, column].Type == CellType.Crystal)
            {
                return layer;
            }
        }

        return -1;
    }

    /// <summary>
    /// 找包含指定格子的<b>最大粘连组</b>，返回组的最低层 b（0 起）。
    /// 分组规则直接复用物理层 <see cref="PhysicsGrid.BuildGroups"/>（规格 §1.2：
    /// 普通格 / 晶体横向粘连、晶体纵向粘连、顶针各自成组），保证与下落语义一致。
    /// </summary>
    private static int GroupBottom(
        ShapeDocument shape,
        int layer,
        int column)
    {
        List<List<(int Layer, int Part)>> groups = PhysicsGrid.BuildGroups(
            PhysicsGrid.Clone(shape),
            shape.LayerCount,
            shape.PartCount);

        foreach (List<(int Layer, int Part)> group in groups)
        {
            bool contains = false;
            int bottom = int.MaxValue;

            foreach ((int memberLayer, int memberPart) in group)
            {
                if (memberLayer == layer &&
                    memberPart == column)
                {
                    contains = true;
                }

                bottom = Math.Min(bottom, memberLayer);
            }

            if (contains)
            {
                return bottom;
            }
        }

        return -1;
    }

    #endregion

    #region 候选 1：算法 8 第一候选（Q 自身）

    /// <summary>
    /// 候选 1：Q 自身（论文算法 8 第一候选，spec §2.7）。
    ///
    /// <para><b>逐条扫描判定</b></para>
    /// <list type="number">
    /// <item>半边的 k 列（从 <paramref name="column"/> 起的连续 n/2 列）都属于该 n 的 S
    /// （n = 4：<see cref="PillarSCriterion"/> 五项 ∧ (D∨F)；n ≥ 6：五项）；</item>
    /// <item>Q 稳定：一次 <see cref="ShapePhysics.Collapse"/> 后仍逐格等于 Q；</item>
    /// <item>都通过 ⇒ 前态 = Q 本身，步骤链 <c>PreState → Target</c>，不切割、不下落。</item>
    /// </list>
    ///
    /// <para>
    /// k 列都是 S ⇒ 每一列都能按单角工序（目标列 + 托柱）单独做出来，
    /// 再直接拼合成这 k 列半边；所以 k 列都按「带子配方」处理，允许带洞。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildAlgo8Self(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        ShapeDocument half = HalfProjection(target, column, cap);

        // 第一候选 ①：k 列都属于该 n 的 S。
        if (!AllHalfColumnsAreS(half, column, parts))
        {
            return null;
        }

        // 第一候选 ②：Q 稳定（一次下落后仍等于 Q）——用真实物理回放。
        ShapeDocument? collapsed = ShapePhysics.Collapse(half).Document;

        if (!ShapeOps.Same(collapsed, half))
        {
            return null;
        }

        // 第一候选 ③：前态 = Q 本身，无需切割。
        var limitations = BaseLimitations(
            $"半边工序（候选 1：算法 8 第一候选 Q 自身）：半边 = 连续 {width} 列（n/2 = {width}）。" +
            "k 列都属于该象限数下的 S，且 Q 一次下落后仍是 Q，所以前态就是目标半边本身；" +
            "每列各自按单角工序做好后直接拼合，不需要碎晶、不需要切割。");

        var columns = HalfColumns(column, parts);

        limitations.Add(
            "扫描结果：" + DescribeColumns(half, columns, parts) +
            $"；{width} 列都通过该 n 的 S 判据（" +
            (parts >= 6 ? "n ≥ 6：五项条件" : "n = 4：五项 ∧ (D∨F)") + "）。");
        limitations.Add(
            "前态即目标半边：一次完整下落逐格核对无变化（Q 稳定）；" +
            "k 列都是带子配方的目标列，允许带洞。");
        limitations.Add(
            "没有辅助列：另外 " + (parts - width) +
            " 个象限整列为空（不参与产物的象限允许留空）；" +
            "这 k 列不是辅助列，而是各自可独立制作的目标列。");

        return BuildPaperHalfOrder(
            target,
            column,
            kind,
            HalfCandidate.Algo8Self,
            half,
            half,
            collapseStep: null,
            columns,
            limitations);
    }

    #endregion

    #region 候选 2：算法 8 第二候选（联合逆提升）

    /// <summary>
    /// 候选 2：联合逆提升（论文算法 8 第二候选，spec §2.7；k 列推广）。
    ///
    /// <para><b>逐条扫描判定</b></para>
    /// <list type="number">
    /// <item>k 列各有晶体，且 k 枚最高晶体<b>不全等高</b>（有列没有晶体、或全部等高 ⇒ 拒绝）；</item>
    /// <item>取较低的那一枚，按规格 §1.2 找它的最大粘连组 G，其最低层记为 b；</item>
    /// <item>在 b 以下找<b>最高非空层</b> a（任一列有材料就算）；</item>
    /// <item>要求：a 层<b>至少一对相邻列都是普通格</b>（水平 <c>TT</c>，不必全列）、
    /// <c>a &lt; b−1</c>、<c>a+1 … b−1</c> 所有列都为空；</item>
    /// <item>把那对普通格整体提到 b−1，其余格子不动 ⇒ 候选前态 Q′；</item>
    /// <item>Q′ 的 k 列都属 S，且真实一次下落恰好得到 Q ⇒ 接受。</item>
    /// </list>
    ///
    /// <para>
    /// 所有层号都由扫描得出：最高晶体 = 沿列向下找第一个晶体；b = 粘连组的最低层；
    /// a = b 以下最高非空层。没有按 n × 层数的查表。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildAlgo8Uplift(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        int parts = target.PartCount;
        int width = parts / 2;
        ShapeDocument half = HalfProjection(target, column, cap);
        int[] columns = HalfColumns(column, parts);

        // 第二候选 1：k 列各有晶体，且最高晶体不全等高。
        var highest = new int[width];
        int lowest = int.MaxValue;

        for (int index = 0; index < width; index++)
        {
            highest[index] = HighestCrystal(half, columns[index]);

            if (highest[index] < 0)
            {
                return null;
            }

            lowest = Math.Min(lowest, highest[index]);
        }

        bool allEqual = true;

        for (int index = 0; index < width; index++)
        {
            if (highest[index] != lowest)
            {
                allEqual = false;
                break;
            }
        }

        if (allEqual)
        {
            return null;
        }

        // 第二候选 2 / 3 / 4 / 5 / 6：对每一枚「最低的最高晶体」各试一次。
        for (int lowerIndex = 0; lowerIndex < width; lowerIndex++)
        {
            if (highest[lowerIndex] != lowest)
            {
                continue;
            }

            AngleWorkOrder? order = TryLiftHalf(
                target,
                column,
                cap,
                kind,
                half,
                columns,
                lowest,
                columns[lowerIndex]);

            if (order is not null)
            {
                return order;
            }
        }

        return null;
    }

    /// <summary>候选 2：以 <paramref name="lowerColumn"/> 那枚较低晶体为起点尝试联合逆提升。</summary>
    private static AngleWorkOrder? TryLiftHalf(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        ShapeDocument half,
        int[] columns,
        int lowerLayer,
        int lowerColumn)
    {
        int parts = target.PartCount;
        int width = parts / 2;

        // 第二候选 2：较低那枚的最大粘连组 G 的最低层 b。
        int bottom = GroupBottom(half, lowerLayer, lowerColumn);

        if (bottom <= 0)
        {
            return null;
        }

        // 第二候选 3：b 以下最高非空层 a（任一列有材料就算）。
        int liftFrom = -1;

        for (int layer = bottom - 1;
             layer >= 0;
             layer--)
        {
            bool nonEmpty = false;

            foreach (int part in columns)
            {
                if (half[layer, part].Type != CellType.Empty)
                {
                    nonEmpty = true;
                    break;
                }
            }

            if (nonEmpty)
            {
                liftFrom = layer;
                break;
            }
        }

        if (liftFrom < 0)
        {
            return null;
        }

        // 第二候选 4：a < b−1，且 a+1 … b−1 所有列都空。
        if (liftFrom >= bottom - 1)
        {
            return null;
        }

        for (int layer = liftFrom + 1;
             layer <= bottom - 1;
             layer++)
        {
            foreach (int part in columns)
            {
                if (half[layer, part].Type != CellType.Empty)
                {
                    return null;
                }
            }
        }

        // 第二候选 5：a 层至少一对相邻列都是普通格（水平 TT），把它俩一起提到 b−1。
        int liftTo = bottom - 1;
        int liftedPairFirst = -1;

        for (int index = 0; index + 1 < width; index++)
        {
            int first = columns[index];
            int second = columns[index + 1];

            if (half[liftFrom, first].Type != CellType.Shape ||
                half[liftFrom, second].Type != CellType.Shape)
            {
                continue;
            }

            liftedPairFirst = index;
            break;
        }

        if (liftedPairFirst < 0)
        {
            return null;
        }

        int liftFirst = columns[liftedPairFirst];
        int liftSecond = columns[liftedPairFirst + 1];

        ShapeDocument preState = half.Clone();

        ShapeCell firstCell = half[liftFrom, liftFirst].Clone();
        ShapeCell secondCell = half[liftFrom, liftSecond].Clone();

        preState[liftFrom, liftFirst] = ShapeCell.Empty();
        preState[liftFrom, liftSecond] = ShapeCell.Empty();

        preState[liftTo, liftFirst] = firstCell;
        preState[liftTo, liftSecond] = secondCell;

        // 第二候选 6：Q′ 的 k 列都属 S，且真实一次下落恰好得到 Q。
        if (!AllHalfColumnsAreS(preState, column, parts))
        {
            return null;
        }

        ShapeDocument? collapsed = ShapePhysics.Collapse(preState).Document;

        if (!ShapeOps.Same(collapsed, half))
        {
            return null;
        }

        var limitations = BaseLimitations(
            $"半边工序（候选 2：算法 8 联合逆提升）：半边 = 连续 {width} 列（n/2 = {width}）。" +
            "把较低晶体粘连组下方那一对相邻普通格整体抬高，使 k 根 S 列拼合后" +
            "一次完整下落恰好得到目标半边；k 列都按带子配方的目标列处理，允许带洞。");

        limitations.Add(
            "扫描结果：" + DescribeColumns(half, columns, parts) +
            $"；粘连组最低层 b = 第 {bottom + 1} 层（起点：第 {lowerColumn + 1} 象限第 {lowerLayer + 1} 层）。");
        limitations.Add(
            $"逆提升：a = 第 {liftFrom + 1} 层（b 以下最高非空层，相邻普通格在第 " +
            $"{liftFirst + 1}、{liftSecond + 1} 象限）→ 第 {liftTo + 1} 层；" +
            $"a+1 … b−1 所有列都空。" + DescribeColumns(preState, columns, parts, "前态 Q′："));
        limitations.Add(
            "步骤链：前态 Q′ 装配后自动下落一次即半边目标（自动物理，不单列「下落」步骤）；" +
            "没有切割，k 列都通过该 n 的 S 判据。");
        limitations.Add(
            "没有辅助列：另外 " + (parts - width) +
            " 个象限整列为空（不参与产物的象限允许留空）；" +
            "这 k 列不是辅助列，而是各自可独立制作的目标列。");

        return BuildPaperHalfOrder(
            target,
            column,
            kind,
            HalfCandidate.Algo8Uplift,
            preState,
            half,
            collapsed,
            columns,
            limitations);
    }

    #endregion

    #region 候选 3：临时晶体 + 保险丝 + 一刀

    /// <summary>
    /// 候选 3：临时晶体 + 保险丝 + 一刀（k 列推广）。
    ///
    /// <para>
    /// 返回 null 表示目标列<b>不在候选 3 的形态范围</b>里（顶部三层不是 c-P-T、
    /// s−1 不是洞、临时晶体块与原生晶体纵向融合……），调用方按原语义报「不可制作」。
    /// 返回非 null 时：要么是回放验证通过的完整工序（含 <see cref="HalfWorkOrder"/>），
    /// 要么是形态命中但回放没过的「未覆盖」工序（IsComplete = false，只保留已验证前缀）。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildTempCrystalFuse(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind)
    {
        int parts = target.PartCount;
        int width = parts / 2;

        if (parts < HalfMinParts ||
            parts % 2 != 0 ||
            cap < target.LayerCount)
        {
            return null;
        }

        string text = ShapeSignature.ColumnText(target, column);

        /*
         * 目标列必须是合法列（五项 ✔）：
         *   n = 4 时这一族正好是「S ✘」的合法角（D、F 都不成立）；
         *   n ≥ 6 时 S ⟺ 五项，候选 1 会先命中，这里只在候选 1 / 2 失败时被调用，
         *   因此不再要求「S ✘」，只要求合法。
         */
        if (!ShapeSignature.CheckColumn(text).IsLegal)
        {
            return null;
        }

        if (parts == 4 && PillarSCriterion.IsPillarColumn(text))
        {
            return null;
        }

        /*
         * 顶部三层的环：
         *   s   = 最高晶体层（沿列扫描，不是查表）
         *   s+1 = 顶针（不参与横向粘连 ⇒ 环必须从环伙伴的竖直晶体段绕过去）
         *   s+2 = 普通格（与环伙伴顶层晶体横向咬合，闭合循环支撑）
         */
        int s = text.LastIndexOf(ShapeSignature.CrystalCode);

        if (s < 2 || s + 2 != text.Length - 1)
        {
            return null;
        }

        if (text[s + 1] != ShapeSignature.PinCode ||
            text[s + 2] != ShapeSignature.SolidCode)
        {
            return null;
        }

        /*
         * 抬升：目标在 s−1 必须留洞、s−2 必须是普通格。
         * 把 s−2 的普通格搬到 s−1（与环伙伴同层普通格配对），s−2 让出的位置
         * 和其余洞一起填临时晶体——这正是「不写打表、只按结构扫描」的形态谓词。
         */
        if (text[s - 1] != ShapeSignature.EmptyCode ||
            text[s - 2] != ShapeSignature.SolidCode)
        {
            return null;
        }

        var preColumn = new char[cap];

        Array.Fill(preColumn, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < text.Length && layer < cap; layer++)
        {
            preColumn[layer] = text[layer];
        }

        preColumn[s - 1] = text[s - 2];
        preColumn[s - 2] = ShapeSignature.EmptyCode;

        for (int layer = 0; layer < s; layer++)
        {
            if (preColumn[layer] == ShapeSignature.EmptyCode)
            {
                preColumn[layer] = ShapeSignature.CrystalCode;
            }
        }

        /*
         * 临时晶体块：preColumn 是晶体、目标列不是晶体的连续层段。
         * n = 4 的实测形态只有一个块（上端正是 s−2）；
         * k ≥ 3 / 多缺口形态可能有多个块，每块都要单独接一根保险丝。
         */
        List<(int Bottom, int Top)> blocks = TemporaryCrystalBlocks(
            preColumn,
            text,
            s);

        if (blocks.Count == 0)
        {
            return null;
        }

        foreach ((int blockBottom, int blockTop) in blocks)
        {
            /*
             * 临时晶体块的上 / 下端不能与目标原生晶体纵向融合：
             * 融合之后碎晶会连带炸掉目标列的原生晶体（§5 规则二）。
             */
            if (blockTop + 1 < text.Length &&
                text[blockTop + 1] == ShapeSignature.CrystalCode)
            {
                return null;
            }

            if (blockBottom > 0 &&
                text[blockBottom - 1] == ShapeSignature.CrystalCode)
            {
                return null;
            }
        }

        int partnerColumn = Wrap(column + 1, parts);
        int fuseColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);
        char color = CrystalColor(target, column);

        /*
         * 环伙伴（紧列，位于同一半边内部、与目标列相邻）：
         *   0 … s−3  普通格（s−3 这一枚是双格下落时的刹车）
         *   s−2      晶体（与临时晶体同层，一刀碎掉，腾出落点）
         *   s−1      普通格（与 Q1 抬升的普通格横向粘连、配对下落）
         *   s … s+2  三连晶（顶部咬合 Q1 的 c-P-T，构成循环支撑）
         */
        var partner = new char[cap];
        var fuse = new char[cap];

        Array.Fill(partner, ShapeSignature.EmptyCode);
        Array.Fill(fuse, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < s - 2; layer++)
        {
            partner[layer] = ShapeSignature.SolidCode;
        }

        partner[s - 2] = ShapeSignature.CrystalCode;
        partner[s - 1] = ShapeSignature.SolidCode;
        partner[s] = ShapeSignature.CrystalCode;
        partner[s + 1] = ShapeSignature.CrystalCode;
        partner[s + 2] = ShapeSignature.CrystalCode;

        /*
         * 保险丝柱（紧列）：在每个临时晶体块的「顶层」各放一颗晶体，
         * 与 Q1 的临时晶体横向相邻（跨刀口），其余层用普通格垫满。
         * n = 4 时只有一个块，上端 = s−2，退化成原来的一颗保险丝。
         */
        int highestBlockTop = 0;

        foreach ((int _, int blockTop) in blocks)
        {
            highestBlockTop = Math.Max(highestBlockTop, blockTop);
        }

        for (int layer = 0; layer <= highestBlockTop; layer++)
        {
            fuse[layer] = ShapeSignature.SolidCode;
        }

        foreach ((int _, int blockTop) in blocks)
        {
            fuse[blockTop] = ShapeSignature.CrystalCode;
        }

        string partnerText = new string(partner);
        string fuseText = new string(fuse);

        var preState = new ShapeDocument(cap, parts);

        /*
         * Q1：保留目标列的原生格（形状码 / 颜色逐格沿用），
         * 抬升的那一格沿用 s−2 的原生格，临时晶体用目标晶体颜色。
         */
        for (int layer = 0; layer < cap; layer++)
        {
            preState[layer, column] =
                layer < target.LayerCount &&
                layer != s - 1 &&
                text[layer] == preColumn[layer]
                    ? target[layer, column].Clone()
                    : CellFromCode(preColumn[layer], color);
        }

        preState[s - 1, column] = target[s - 2, column].Clone();

        WriteColumn(preState, partnerColumn, partnerText, color);
        WriteColumn(preState, fuseColumn, fuseText, color);

        /*
         * k ≥ 3：半边的其余列（column+2 … column+k−1）原样保留——
         * 它们也是目标半边的一部分，由真实回放逐格核对。
         * k = 2 时这个循环不执行，其余象限保持为空（与原两列版本一致）。
         */
        for (int index = 2; index < width; index++)
        {
            int part = Wrap(column + index, parts);

            for (int layer = 0; layer < cap; layer++)
            {
                preState[layer, part] = layer < target.LayerCount
                    ? target[layer, part].Clone()
                    : ShapeCell.Empty();
            }
        }

        string expectedPartner = BuildHalfPartnerColumn(s, cap);

        ShapeDocument expectedHalf = BuildHalfExpectation(
            target,
            column,
            cap,
            partnerColumn,
            expectedPartner,
            color,
            parts);

        int[] auxiliaryColumns = parts == 4
            ? new[] { partnerColumn, Wrap(column + 2, parts), fuseColumn }
            : new[] { partnerColumn, fuseColumn };

        var limitations = BaseLimitations(
            $"半边工序（候选 3：临时晶体 + 保险丝 + 一刀）：半边 = 连续 {width} 列（n/2 = {width}），" +
            "与交换链的轴划分一致。Q1 抬升 + 临时晶体、环伙伴顶部 c-P-T ↔ c-c-c 咬合、" +
            "保险丝柱在每个临时晶体块的顶层各放一颗晶体；半破后 k 列逐格核对。");

        limitations.Add(
            $"扫描结果：s = 第 {s + 1} 层（最高晶体），抬升层 = 第 {s - 1} 层 → 第 {s} 层，" +
            $"临时晶体块 = " + DescribeBlocks(blocks) + "。");
        limitations.Add(
            $"前态：Q1 = {Trim(preColumn)}，" +
            $"环伙伴（第 {partnerColumn + 1} 象限）= {partnerText.TrimEnd(ShapeSignature.EmptyCode)}，" +
            $"保险丝（第 {fuseColumn + 1} 象限）= {fuseText.TrimEnd(ShapeSignature.EmptyCode)}。" +
            (width > 2
                ? "其余半边列原样保留：" + DescribeColumns(
                    preState,
                    HalfColumns(column, parts),
                    parts,
                    string.Empty)
                : ""));
        limitations.Add(
            $"切割轴：顺转 {rotation} 个象限，使保留侧（西 = 第 1…{width} 位）正好是" +
            "「目标列 + 环伙伴 + 其余半边列」，保险丝柱落在被丢弃的一侧。");
        limitations.Add(
            $"半破后的 k 列：" + DescribeColumns(expectedHalf, HalfColumns(column, parts), parts) +
            "（都按类型 / 形状码 / 颜色逐格核对）。");
        limitations.Add(
            "辅助列全程紧列（docs §5 规则六 / §6 第 7 条 / §17.7）：" +
            "环伙伴需要让位的那一层放晶体、保险丝在临时晶体块的顶层放晶体，" +
            "半破时与临时晶体一起碎掉。");

        // ── 验收条件一 / 七：前态每列合法且紧列，目标列 ≠ 目标 ──
        int[] columnsWithSubRecipe = parts >= 6
            ? HalfColumns(column, parts)
            : Array.Empty<int>();

        if (ShapeSignature.ColumnText(preState, column) == text ||
            !ShapeSignature.AllColumnsLegal(preState, out _) ||
            !ShapeOps.IsStable(preState) ||
            HasIllegalColumnHoles(
                preState,
                columnsWithSubRecipe,
                out _))
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "前态不过验收（前态目标列 = 目标 / 有非法列 / 不稳定 / 辅助列有洞），" +
                "没有可保留的已验证前缀",
                preState: null,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                columns: HalfColumns(column, parts),
                limitations);
        }

        // ── 一刀半破：真实 ShapePhysics.Cut ──
        ShapeDocument rotated = ShapeOps.Rotate(preState, rotation);

        int targetRotated = Wrap(column + rotation, parts);
        int partnerRotated = Wrap(partnerColumn + rotation, parts);
        int fuseRotated = Wrap(fuseColumn + rotation, parts);

        // 保留侧必须同时含目标列与环伙伴；保险丝柱必须落在被丢弃的一侧。
        if (targetRotated >= width ||
            partnerRotated >= width ||
            fuseRotated < width)
        {
            return null;
        }

        /*
         * 保险丝必须真的跨刀口：旋转后它在被丢弃侧，
         * 在每个临时晶体块的顶层与目标列的临时晶体横向相邻——
         * 这样半破才会把每一块连保险丝整段碎掉。
         */
        foreach ((int _, int blockTop) in blocks)
        {
            if (rotated[blockTop, fuseRotated].Type != CellType.Crystal ||
                rotated[blockTop, targetRotated].Type != CellType.Crystal)
            {
                return null;
            }
        }

        CutResult cut = ShapePhysics.Cut(rotated);

        if (!cut.Success || cut.West is null)
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "真实 Cut 失败：" + (cut.Error ?? "未知原因"),
                preState,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                columns: HalfColumns(column, parts),
                limitations);
        }

        ShapeDocument west = cut.West;

        if (!ShapeOps.IsStable(west) ||
            !ShapeSignature.AllColumnsLegal(west, out _))
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "半破后的西半边不稳定或含非法列，k 列无法逐格对上目标半边",
                preState,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                columns: HalfColumns(column, parts),
                limitations);
        }

        // 断口两侧的晶体必须全部碎掉：临时晶体（含全部块）+ 环伙伴的闸门晶体；
        // 目标列的原生晶体一颗不少、环伙伴顶部三连晶保留。
        int targetCrystals = CountCrystalsInText(text);

        if (CrystalsInColumn(west, targetRotated) != targetCrystals ||
            CrystalsInColumn(west, partnerRotated) != 3)
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "半破没有只碎辅助晶体：临时晶体 / 闸门晶体残留，或目标列晶体被误伤",
                preState,
                cutResult: null,
                rotation,
                auxiliaryColumns,
                columns: HalfColumns(column, parts),
                limitations);
        }

        ShapeDocument result = ShapeOps.Rotate(west, -rotation);

        // ── k 列逐格核对（类型 / 形状码 / 颜色）──
        if (!ShapeOps.Same(result, expectedHalf))
        {
            return BuildHalfFailure(
                target,
                column,
                kind,
                "半破后的 k 列与目标半边不符：" +
                DescribeColumns(result, HalfColumns(column, parts), parts) +
                "（期望 " + DescribeColumns(
                    expectedHalf,
                    HalfColumns(column, parts),
                    parts,
                    string.Empty) + "）",
                preState,
                result,
                rotation,
                auxiliaryColumns,
                columns: HalfColumns(column, parts),
                limitations);
        }

        return BuildHalfOrder(
            target,
            column,
            kind,
            preState,
            result,
            expectedHalf,
            rotation,
            auxiliaryColumns,
            HalfColumns(column, parts),
            text,
            expectedPartner,
            limitations);
    }

    /// <summary>
    /// 临时晶体块：<paramref name="preColumn"/> 是晶体、目标列文本不是晶体的连续层段
    /// （只在上端 &lt; <paramref name="s"/> 的范围里找）。返回按层号升序的 (底, 顶)。
    /// </summary>
    private static List<(int Bottom, int Top)> TemporaryCrystalBlocks(
        char[] preColumn,
        string text,
        int s)
    {
        var blocks = new List<(int Bottom, int Top)>();

        int layer = 0;

        while (layer < s)
        {
            bool temporary =
                preColumn[layer] == ShapeSignature.CrystalCode &&
                (layer >= text.Length ||
                 text[layer] != ShapeSignature.CrystalCode);

            if (!temporary)
            {
                layer++;
                continue;
            }

            int bottom = layer;

            while (layer < s &&
                   preColumn[layer] == ShapeSignature.CrystalCode &&
                   (layer >= text.Length ||
                    text[layer] != ShapeSignature.CrystalCode))
            {
                layer++;
            }

            blocks.Add((bottom, layer - 1));
        }

        return blocks;
    }

    /// <summary>把临时晶体块写成「第 a–b 层 / 第 c 层」。</summary>
    private static string DescribeBlocks(
        List<(int Bottom, int Top)> blocks)
    {
        var parts = new List<string>();

        foreach ((int bottom, int top) in blocks)
        {
            parts.Add(bottom == top
                ? $"第 {bottom + 1} 层"
                : $"第 {bottom + 1}–{top + 1} 层");
        }

        return string.Join("、", parts);
    }

    /// <summary>把 k 列写成「第 i 象限 = 列文本」，用于说明文字。</summary>
    private static string DescribeColumns(
        ShapeDocument shape,
        int[] columns,
        int parts,
        string prefix = "扫描结果：")
    {
        var lines = new List<string>();

        foreach (int part in columns)
        {
            lines.Add(
                $"第 {part + 1} 象限 = " +
                Trim(ShapeSignature.ColumnText(shape, part)));
        }

        return prefix + string.Join("，", lines);
    }

    #endregion

    #region 工序单

    /// <summary>
    /// 把候选 1 / 2 的 k 列半边装成角工序 + <see cref="HalfWorkOrder"/>。
    ///
    /// <para>
    /// 候选 1：<paramref name="collapseStep"/> 为 null ⇒ 步骤链
    /// <c>PreState(Q) → Target(Q)</c>，不需要切割或下落（Q 稳定）。
    /// </para>
    /// <para>
    /// 候选 2：<paramref name="collapseStep"/> 是真实 <see cref="ShapePhysics.Collapse"/>
    /// 的结果 ⇒ 步骤链 <c>PreState(Q′) → Collapse → Target(Q)</c>，下落一次。
    /// </para>
    /// <para>
    /// k 列都是带子配方的目标列（各自按 S 单角工序制作），所以
    /// <see cref="HalfWorkOrder.TargetColumnsHaveSubRecipe"/> 为 true、允许带洞；
    /// 另外 n−k 个象限整列为空，没有辅助列。三个候选都不含 Pin，<c>Rank = 0</c>。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildPaperHalfOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        HalfCandidate candidate,
        ShapeDocument preState,
        ShapeDocument expectedHalf,
        ShapeDocument? collapseStep,
        int[] columns,
        List<string> limitations)
    {
        string candidateName = candidate switch
        {
            HalfCandidate.Algo8Self => "候选 1（Q 自身）",
            HalfCandidate.Algo8Uplift => "候选 2（联合逆提升）",
            _ => throw new ArgumentOutOfRangeException(
                nameof(candidate),
                candidate,
                "BuildPaperHalfOrder 只处理算法 8 的两条候选。")
        };

        string preStateText = DescribeColumns(preState, columns, preState.PartCount);
        string targetText = DescribeColumns(expectedHalf, columns, expectedHalf.PartCount);

        var halfSteps = new List<AngleWorkStep>
        {
            new(
                $"半边工序前态（{candidateName}）：{preStateText}",
                preState,
                AngleWorkStepKind.PreState)
        };

        /*
         * 候选 2 的「一次完整下落」是装配 Q′ 之后自动发生的物理，不是独立操作；
         * 按新步骤规范并进 Target 步（末步 Shape 就是落定后的结果），
         * 不再单独输出 Collapse 步。
         */

        halfSteps.Add(new AngleWorkStep(
            $"半边目标（{candidateName}）：{targetText}（k 列逐格核对类型 / 形状码 / 颜色）",
            expectedHalf,
            AngleWorkStepKind.Target));

        var half = new HalfWorkOrder(
            new WorkOrderTarget(columns, column),
            candidate,
            halfSteps,
            true,
            null,
            limitations,
            0)
        {
            // k 列都能由单角工序（S 形态）单独做出 ⇒ 带子配方，允许带洞。
            TargetColumnsHaveSubRecipe = true,
            Rotation = 0
        };

        var steps = new List<AngleWorkStep>
        {
            new(
                $"半边工序（{candidateName}）前态：{columns.Length} 列一起做，" +
                "每一列都属于该象限数下的 S",
                preState,
                AngleWorkStepKind.PreState)
        };

        steps.Add(new AngleWorkStep(
            collapseStep is null
                ? "k 列逐格等于目标半边，得到目标角（无需切割）"
                : "k 列逐格等于目标半边，得到目标角（Q′ 装配后自动下落一次，下落并进本步）",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = 0,
            // 候选 1 / 2 没有辅助列：另外 n−k 个象限整列为空。
            AuxiliaryColumns = Array.Empty<int>(),
            Rank = 0,
            Half = half,
            Limitations = limitations
        };
    }

    /// <summary>把验证通过的 k 列半边装成角工序 + <see cref="HalfWorkOrder"/>。</summary>
    private static AngleWorkOrder BuildHalfOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        ShapeDocument preState,
        ShapeDocument cutResult,
        ShapeDocument expectedHalf,
        int rotation,
        int[] auxiliaryColumns,
        int[] columns,
        string targetText,
        string expectedPartnerText,
        List<string> limitations)
    {
        int partnerColumn = auxiliaryColumns[0];

        var halfSteps = new List<AngleWorkStep>
        {
            new(
                "半边工序前态：目标列抬升 + 洞填临时晶体；" +
                "环伙伴顶部三层 ccc 咬合、闸门层放晶体；保险丝柱在每个临时晶体块的顶层放晶体",
                preState,
                AngleWorkStepKind.PreState),

            new(
                (rotation == 0
                    ? string.Empty
                    : $"先顺转 {rotation} 个象限，再") +
                "一刀半破（候选 3）：刀口跨过保险丝晶体与目标列的临时晶体，" +
                "临时晶体整块碎掉，抬升的普通格与环伙伴同层普通格配对只落一层",
                cutResult,
                AngleWorkStepKind.Cut),

            new(
                $"半边目标：第 {column + 1} 象限 = {targetText}，" +
                $"第 {partnerColumn + 1} 象限 = " +
                $"{expectedPartnerText.TrimEnd(ShapeSignature.EmptyCode)}（k 列逐格核对）",
                expectedHalf,
                AngleWorkStepKind.Target)
        };

        var half = new HalfWorkOrder(
            new WorkOrderTarget(columns, column),
            HalfCandidate.TempCrystalFuse,
            halfSteps,
            true,
            null,
            limitations,
            0)
        {
            /*
             * n ≥ 6 时半边的每一列都是整图里真实存在的目标列（每列合法 ⇒ 每列是 S），
             * 只有环伙伴 / 保险丝是辅助列；n = 4 时两列版本只豁免 Q1（保持原语义）。
             */
            TargetColumnsHaveSubRecipe = target.PartCount >= 6,
            Rotation = rotation
        };

        var steps = new List<AngleWorkStep>
        {
            new(
                "半边工序（候选 3）前态：k 列一起做——目标列抬升 + 临时晶体，" +
                "环伙伴提供顶部循环支撑，保险丝柱跨刀口",
                preState,
                AngleWorkStepKind.PreState),

            new(
                "一刀半破：仅碎掉临时晶体 / 闸门晶体 / 保险丝，" +
                $"半破后第 {column + 1} 象限逐格等于目标",
                cutResult,
                AngleWorkStepKind.Cut),

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
            // Q1 前态本身是紧列（没有洞），这个标记只是防御性的。
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Rank = 0,
            Half = half,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 候选 3 的形态命中、但回放没通过：如实报「未覆盖」，
    /// <b>只保留真实验证过的前缀</b>（前态 / 真实 Cut 结果），不伪造 Target 步。
    /// </summary>
    private static AngleWorkOrder BuildHalfFailure(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        string reason,
        ShapeDocument? preState,
        ShapeDocument? cutResult,
        int rotation,
        int[] auxiliaryColumns,
        int[] columns,
        List<string> limitations)
    {
        string note = UncoveredPrefix +
                      "半边工序（候选 3：临时晶体 + 保险丝 + 一刀）回放未通过：" +
                      reason;

        var halfSteps = new List<AngleWorkStep>();

        if (preState is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "半边工序前态（已验证：每列合法、紧列、稳定）",
                preState,
                AngleWorkStepKind.PreState));
        }

        if (cutResult is not null)
        {
            halfSteps.Add(new AngleWorkStep(
                "一刀半破的真实 Cut 结果（k 列尚未对上目标半边）",
                cutResult,
                AngleWorkStepKind.Cut));
        }

        halfSteps.Add(new AngleWorkStep(
            note,
            null,
            AngleWorkStepKind.Note));

        var half = new HalfWorkOrder(
            new WorkOrderTarget(columns, column),
            HalfCandidate.TempCrystalFuse,
            halfSteps,
            false,
            note,
            limitations,
            0);

        var steps = new List<AngleWorkStep>();

        if (preState is not null)
        {
            steps.Add(new AngleWorkStep(
                "半边工序前态（已验证）",
                preState,
                AngleWorkStepKind.PreState));
        }

        if (cutResult is not null)
        {
            steps.Add(new AngleWorkStep(
                "一刀半破的真实 Cut 结果（尚未对上目标半边）",
                cutResult,
                AngleWorkStepKind.Cut));
        }

        steps.Add(new AngleWorkStep(note, null, AngleWorkStepKind.Note));

        var failureLimitations = new List<string>(limitations)
        {
            "未覆盖（尚未实现）：半边工序的候选 3 命中了这个形态，但回放没通过；" +
            "这不是「不可制作」的判定。"
        };

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            note)
        {
            IsComplete = false,
            Construction = AngleConstruction.Half,
            Algorithm = AngleAlgorithm.None,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Rank = 0,
            Half = half,
            Limitations = failureLimitations
        };
    }

    #endregion

    #region 列扫描小工具（半边工序专用）

    /// <summary>
    /// 半边环伙伴半破后的期望形态：
    /// <c>0 … s−2</c> 是普通格，<c>s−1</c> 是洞（配对下落让出的那一层），
    /// <c>s … s+2</c> 是顶部三连晶。
    /// </summary>
    private static string BuildHalfPartnerColumn(
        int s,
        int cap)
    {
        var cells = new char[cap];

        Array.Fill(cells, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < s - 1; layer++)
        {
            cells[layer] = ShapeSignature.SolidCode;
        }

        cells[s] = ShapeSignature.CrystalCode;
        cells[s + 1] = ShapeSignature.CrystalCode;
        cells[s + 2] = ShapeSignature.CrystalCode;

        return new string(cells);
    }

    /// <summary>
    /// 目标半边的期望图形：半边的 k 列逐格复制目标（环伙伴那一列再按
    /// <see cref="BuildHalfPartnerColumn"/> 写入：普通格 <c>C/u</c>、晶体用目标晶体颜色）。
    /// </summary>
    private static ShapeDocument BuildHalfExpectation(
        ShapeDocument target,
        int column,
        int cap,
        int partnerColumn,
        string partnerText,
        char color,
        int parts)
    {
        int width = parts / 2;
        var expected = new ShapeDocument(cap, parts);

        for (int index = 0; index < width; index++)
        {
            int part = Wrap(column + index, parts);

            for (int layer = 0; layer < cap; layer++)
            {
                expected[layer, part] = layer < target.LayerCount
                    ? target[layer, part].Clone()
                    : ShapeCell.Empty();
            }
        }

        WriteColumn(expected, partnerColumn, partnerText, color);

        return expected;
    }

    /// <summary>把列代码转成单元格（普通格用 <c>C/u</c>，晶体用指定颜色）。</summary>
    private static ShapeCell CellFromCode(
        char code,
        char crystalColor)
    {
        return code switch
        {
            ShapeSignature.CrystalCode => ShapeCell.Crystal(crystalColor),
            ShapeSignature.PinCode => ShapeCell.Pin(),
            ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
            _ => ShapeCell.Empty()
        };
    }

    /// <summary>列文本里晶体的颗数。</summary>
    private static int CountCrystalsInText(
        string text)
    {
        int count = 0;

        foreach (char code in text)
        {
            if (code == ShapeSignature.CrystalCode)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>去掉列文本末尾的空层，只用于说明文字。</summary>
    private static string Trim(
        string text)
    {
        return text.TrimEnd(ShapeSignature.EmptyCode);
    }

    /// <summary>去掉列代码数组末尾的空层，只用于说明文字。</summary>
    private static string Trim(
        char[] cells)
    {
        return new string(cells).TrimEnd(ShapeSignature.EmptyCode);
    }

    #endregion
}

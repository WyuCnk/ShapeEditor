using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 角工序里的一步。
///
/// <see cref="Shape"/> 是这一步执行完后的图形快照；<see cref="AngleWorkStepKind.Note"/>
/// 步允许为 null（只承载说明文字）。
/// </summary>
public sealed record AngleWorkStep(
    string Title,
    ShapeDocument? Shape,
    AngleWorkStepKind Kind);

/// <summary>角工序的步骤类型。</summary>
public enum AngleWorkStepKind
{
    /// <summary>碎晶前的角（带辅助象限）。</summary>
    PreState,

    /// <summary>切割（半破）：沿刀口打碎桥柱/辅助列里的晶体。</summary>
    Cut,

    /// <summary>下落：碎晶后材料落位。</summary>
    Collapse,

    /// <summary>顶针：一次顶针完成批量下落（构造 H）。</summary>
    Pin,

    /// <summary>构造 C：支柱交接——装新支撑柱、拆旧柱与辅助列。</summary>
    Support,

    /// <summary>结晶：补晶体。</summary>
    Crystallize,

    /// <summary>目标角（清除辅助象限后）。</summary>
    Target,

    /// <summary>说明/限制，不改变图形。</summary>
    Note
}

/// <summary>
/// 碎晶角用的构造（规格 §3）。
/// </summary>
public enum AngleConstruction
{
    /// <summary>没有可用构造。</summary>
    None,

    /// <summary>构造 G：四象限单孔环下落，一次降一层（多层时重复多轮）。</summary>
    G,

    /// <summary>构造 F：至少六象限的受控下落，一次降到目标 d 层。</summary>
    F,

    /// <summary>构造 H：四象限的一次顶针批量下落（一次 Pin 完成整段下降）。</summary>
    H,

    /// <summary>构造 A：逐层结晶的紧列（严格接地、无缺口）。</summary>
    A,

    /// <summary>构造 C：支柱交接——装新支撑柱、拆旧柱与辅助列。</summary>
    C,

    /// <summary>填充+暴破：把塔下缺口填成晶体，再用切割打碎这一段晶体。</summary>
    FillBreak
}

/// <summary>
/// 角算法的论文分档（TODO.md §6）。
///
/// <para>
/// 选档只由「五项条件 + 晶体分布 + 象限数 + 底格」决定，<b>先选算法再动手</b>；
/// 每一档内部可以尝试多种构造，构造失败时才退回填充+暴破 / G / F / H 兜底。
/// </para>
/// </summary>
public enum AngleAlgorithm
{
    /// <summary>没有分档（例如只有说明、不构造前态的简单角旧路径）。</summary>
    None,

    /// <summary>无晶体：构造 C——逐层追加 T/P。</summary>
    NoCrystal,

    /// <summary>Simple（最高晶体连续接地）：构造 A（紧列）+ 构造 C（尾部）。</summary>
    SimpleTail,

    /// <summary>n ≥ 6 的算法 1：提升（把缺口下方的普通格抬到缺口顶端再碎晶）。</summary>
    Algorithm1,

    /// <summary>n ≥ 6 的算法 2：正规核心（把晶体塔下的缺口直接填成紧核心再碎晶）。</summary>
    Algorithm2,

    /// <summary>n ≥ 6 的算法 3：构造 F 送回（一次受控下落到 d 层）。</summary>
    Algorithm3,

    /// <summary>n = 4 的算法 4：递归提升 + 构造 G 环下落。</summary>
    Algorithm4,

    /// <summary>n = 4、底格是针的算法 5：构造 H 一次性批量恢复。</summary>
    Algorithm5,

    /// <summary>n = 4、D 成立的算法 6：底块提升 → 正规化 → 构造 H → 构造 C。</summary>
    Algorithm6,

    /// <summary>分档构造都没覆盖，使用了原有的填充+暴破 / G / F / H 兜底链。</summary>
    Fallback
}

/// <summary>
/// 一个角的工序单。
///
/// <para>
/// <see cref="Note"/> 为 null 表示工序<b>已逐步验证通过</b>；非 null 表示失败，
/// 且只有两种前缀：
/// </para>
/// <list type="bullet">
/// <item><see cref="AngleWorkOrderBuilder.NotProduciblePrefix"/>：五项条件不过，
/// 角本身不合法 ⇒ <b>不可制作</b>；</item>
/// <item><see cref="AngleWorkOrderBuilder.UncoveredPrefix"/>：角合法，但生成器
/// 还没覆盖这个形态 ⇒ <b>未覆盖</b>（不代表不可制作）。</item>
/// </list>
/// <para>
/// <see cref="Limitations"/> 始终给出 UI 需要展示的限制说明。
/// </para>
/// <para>
/// <see cref="Rotation"/> 是切割前需要顺转的象限数（顺时针为正，与
/// <see cref="ShapeOps.Rotate"/> 一致）；<see cref="AuxiliaryColumns"/> 是
/// 前态里辅助列所在的原始象限索引（按环顺序）。
/// </para>
/// </summary>
public sealed record AngleWorkOrder(
    int Column,
    AngleBuildKind Kind,
    IReadOnlyList<AngleWorkStep> Steps,
    string? Note)
{
    /// <summary>UI 展示用的限制/前提说明；即使工序验证通过也可能非空。</summary>
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    /// <summary>工序是否从头到尾用真实 <see cref="ShapePhysics"/> 验证通过。</summary>
    public bool IsComplete { get; init; }

    /// <summary>实际使用的构造。</summary>
    public AngleConstruction Construction { get; init; } = AngleConstruction.None;

    /// <summary>切割前需要顺转的象限数（顺时针为正）。</summary>
    public int Rotation { get; init; }

    /// <summary>前态里的辅助列（原始象限索引，按环顺序）。</summary>
    public IReadOnlyList<int> AuxiliaryColumns { get; init; } = Array.Empty<int>();

    /// <summary>
    /// 实际采用的论文分档算法（见 <see cref="AngleAlgorithm"/>）。
    /// <see cref="AngleAlgorithm.Fallback"/> 表示分档构造没覆盖、用了兜底链。
    /// </summary>
    public AngleAlgorithm Algorithm { get; init; } = AngleAlgorithm.None;

    /// <summary>
    /// 角工序自己的顶针次数。<b>只统计角自己的顶针</b>：
    /// 造塔子配方的 rank 不计入（子配方必须是交换链，rank 0），
    /// 所以它恒等于 <see cref="Steps"/> 里 <see cref="AngleWorkStepKind.Pin"/> 步的数量。
    /// </summary>
    public int Rank { get; init; }

    /// <summary>
    /// 造塔子配方（算法 6 的「构造 H」）：用 <see cref="ExchangeChain.TryBuild"/>
    /// 装配出的 rank 0 配方，作为子节点嵌进角工序单，UI 可展开、<see cref="Replay"/>
    /// 可端到端重算。其它算法为 null。
    /// </summary>
    public Recipe? SubRecipe { get; init; }
}

/// <summary>
/// 角工序生成器：按论文分档（TODO.md §6）选算法，再逐档输出列级步骤。
///
/// <code>
/// 角：五项条件 → 不合法即不可制作
/// ├─ 无晶体                         → 构造 C（逐层追加 T/P）
/// ├─ Simple（最高晶体连续接地）      → 构造 A（紧列）+ 构造 C（尾部）
/// └─ NeedsCrystalBreak
///    ├─ n ≥ 6                       → 算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回）
///    └─ n = 4
///       ├─ 底格是针                  → 算法 5（构造 H 一次性批量恢复）
///       ├─ D 成立（最低晶体下 ≥2 空） → 算法 6（底块提升 → 正规化 → 构造 H → 构造 C）
///       └─ 其余                      → 算法 4（递归提升 + 构造 G 环下落）
/// </code>
///
/// <para>
/// <b>算法 6 的工序单</b>（正解见 TODO.md §3.1.1）：
/// </para>
/// <code>
/// [子配方] 造塔 X   ← ExchangeChain.TryBuild(X)（rank 0，四片 + 两次交换可展开）
/// [步骤]   顶针     ← 一次 ShapePhysics.Pin
/// [步骤]   构造 C   ← 装新支撑柱、拆旧柱与辅助列
/// [结果]   目标角（S 形态）
/// </code>
/// <para>
/// 造塔子配方只做「装配」（<see cref="ExchangeChain"/>），<b>不</b>调用
/// <see cref="ProductionPipeline.Solve"/>：那会把整图求解层的判定混进角工序，
/// 并且在子配方里藏进顶针、破坏 rank 记账。角工序的 <see cref="AngleWorkOrder.Rank"/>
/// 只统计角自己的顶针。
/// </para>
/// <para>
/// <b>兜底链</b>（原有填充+暴破 / G / F / H）没有删除，但降为最末兜底：
/// 只有当某一档论文算法在该形态上回放验证失败时才使用，见
/// <see cref="AngleAlgorithm.Fallback"/>。每个分支的每一步都用真实
/// <see cref="ShapePhysics"/> 回放并逐格核对，绝不保留未经验证的步骤。
/// </para>
/// <para>
/// <b>为什么末步不是结晶</b>：<see cref="ShapePhysics.Crystallize"/> 是全局操作，
/// 会把刚做出来的缺口一起填平。碎晶角的正确顺序是「先有晶体塔（构造 A 逐层结晶）
/// → 再碎晶造缺口」，所以本工序不包含 Collapse 之后的 Crystallize 步。
/// </para>
/// </summary>
public static class AngleWorkOrderBuilder
{
    /// <summary>未覆盖说明的统一前缀。</summary>
    public const string UncoveredPrefix = "未覆盖：";

    /// <summary>
    /// 不可制作说明的统一前缀。<b>只用于存在不合法角</b>（五项条件不过），
    /// 任何「还没实现」的情形都不允许写成不可制作。
    /// </summary>
    public const string NotProduciblePrefix = "不可制作：";

    /// <summary>
    /// 未覆盖的含义说明：避免 UI 把它误读成「不可制作」。
    /// 唯一能否定的判据是「存在不合法角」，那由分析层单独给出。
    /// </summary>
    public const string UncoveredDisclaimer =
        "未覆盖只说明角工序生成器还没覆盖这个形态，不代表不可制作。";

    /// <summary>
    /// 生成角工序：先过五项条件闸门，再按 §6 分档选算法；分档构造失败时才退回兜底链。
    ///
    /// <para>
    /// <paramref name="enforceFiveConditions"/> 默认 <c>true</c>：五项条件不过就报
    /// 「不可制作」，这是 UI / 生产判定的唯一入口语义。
    /// 传 <c>false</c> 只给「构造机器」的合成语料测试用（例如用不合法的列去单独
    /// 验证 Cut/Collapse 的回放语义），<b>不是</b>生产路径的选项。
    /// </para>
    /// </summary>
    public static AngleWorkOrder Build(
        ShapeDocument target,
        int column,
        int cap,
        bool enforceFiveConditions = true)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (column < 0 || column >= target.PartCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        AngleBuildKind kind = AngleClassifier.Classify(target, column);

        /*
         * 一、五项条件闸门（§6 第一行）：
         * 「角：五项条件 → 不合法即不可制作」。
         * 这是本生成器默认唯一会输出「不可制作」的地方。
         */
        if (enforceFiveConditions)
        {
            ColumnCheck check = ShapeSignature.CheckColumn(target, column);

            if (!check.IsLegal)
            {
                return NotProducible(target, column, kind, check);
            }
        }

        /*
         * 二、尺寸校验：这些不是「不可制作」，只是本生成器没法在当前画布上
         * 画前态，沿用原来的「未覆盖」语义。
         */
        if (cap < target.LayerCount)
        {
            return Incomplete(
                column,
                kind,
                $"cap（{cap}）小于目标层数（{target.LayerCount}），无法在前态里保留完整目标列。");
        }

        if (!ShapeRules.IsValidLayerCount(cap))
        {
            return Incomplete(
                column,
                kind,
                $"cap（{cap}）不在 {ShapeRules.MinLayerCount}–{ShapeRules.MaxLayerCount} 之间。");
        }

        if (!ShapeRules.IsValidPartCount(target.PartCount))
        {
            return Incomplete(
                column,
                kind,
                $"象限数（{target.PartCount}）必须是偶数。");
        }

        int layers = cap;
        int parts = target.PartCount;
        int half = parts / 2;

        /*
         * 三、分档（§6）：先按论文选分支，不要先试「填充+暴破」。
         */
        AngleAlgorithm algorithm = SelectAlgorithm(target, column, kind);

        AngleWorkOrder? tierOrder = TryTierOrder(
            algorithm,
            target,
            column,
            layers,
            parts,
            half,
            cap,
            kind);

        string? tierCycleReason = null;

        if (tierOrder is not null && tierOrder.IsComplete)
        {
            /*
             * 最后一道循环闸门：任何分档构造若拿「目标列本身」当前态，
             * 验收等式就平凡成立。这里统一再挡一次，避免某个新构造
             * 绕过各自的候选检查。
             */
            if (HasCyclicPreState(tierOrder, target))
            {
                tierCycleReason = CyclicPreStateReason;
                tierOrder = null;
            }
            else
            {
                /*
                 * 档内链（算法 1 → 2 → 3）会自己标出真正成功的那一档，
                 * 这里只给没打标的实现补上分档标记。
                 */
                return tierOrder.Algorithm == AngleAlgorithm.None
                    ? Label(tierOrder, algorithm, attempt: false)
                    : tierOrder;
            }
        }

        /*
         * 四、算法 4 的「递归提升 + 构造 G 环下落」直接由兜底链承载
         * （填充+暴破的抬升形态 + 单层/多轮构造 G），所以这里不再重复跑一遍。
         */
        AngleWorkOrder legacy = BuildLegacyChain(
            target,
            column,
            cap,
            kind,
            layers,
            parts,
            half);

        if (legacy.IsComplete && HasCyclicPreState(legacy, target))
        {
            legacy = AppendFailure(legacy, CyclicPreStateReason);
        }

        if (algorithm == AngleAlgorithm.Algorithm4)
        {
            return Label(legacy, AngleAlgorithm.Algorithm4, attempt: false);
        }

        string tierReason = tierCycleReason ?? (tierOrder?.Note is null
            ? "该形态没有找到回放验证通过的列级步骤"
            : StripUncoveredPrefix(tierOrder.Note));

        if (legacy.IsComplete)
        {
            /*
             * 分档构造没覆盖、兜底链成功：如实标成 Fallback，
             * 并在限制说明里写清「实际用的是兜底构造」。
             */
            return AddLimitation(
                Label(legacy, AngleAlgorithm.Fallback, attempt: false),
                $"论文分档（{DescribeAlgorithm(algorithm)}）在该形态上未通过回放验证，" +
                $"改用兜底构造（{legacy.Construction}）：{tierReason}");
        }

        /*
         * 五、分档与兜底都没通过：保留已验证前缀，并区分「未覆盖」与「不可制作」。
         */
        return AppendFailure(
            Label(legacy, algorithm, attempt: true),
            $"论文分档（{DescribeAlgorithm(algorithm)}）未覆盖：{tierReason}");
    }

    /// <summary>
    /// 兜底链：原有的「填充+暴破 → 构造 H / 多轮 G / F / 旧填充方案」。
    /// 顺序与旧实现逐字一致，只作为论文分档的回退。
    /// </summary>
    private static AngleWorkOrder BuildLegacyChain(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        int layers,
        int parts,
        int half)
    {
        AngleWorkOrder fillBreakOrder = TryBuildFillBreak(
            target,
            column,
            layers,
            parts,
            half,
            kind);

        if (fillBreakOrder.IsComplete)
        {
            return fillBreakOrder;
        }

        AngleWorkOrder fallbackOrder = BuildLegacy(
            target,
            column,
            cap);

        if (fallbackOrder.IsComplete)
        {
            return fallbackOrder;
        }

        return AppendFailure(
            fallbackOrder,
            "首选填充+暴破也未通过：" +
            StripUncoveredPrefix(fillBreakOrder.Note ?? "未知原因"));
    }

    #region 分档选择

    /// <summary>
    /// 按 §6 选档：
    /// 无晶体 → 构造 C；Simple → 构造 A + 尾部 C；
    /// 碎晶角按 n ≥ 6 / n = 4 的底格与 D 条件细分。
    /// </summary>
    private static AngleAlgorithm SelectAlgorithm(
        ShapeDocument target,
        int column,
        AngleBuildKind kind)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        // ── 无晶体：构造 C（逐层追加 T/P）──
        if (highest < 0)
        {
            return AngleAlgorithm.NoCrystal;
        }

        // ── Simple（最高晶体连续接地）：构造 A（紧列）+ 构造 C（尾部）──
        if (kind == AngleBuildKind.Simple)
        {
            return AngleAlgorithm.SimpleTail;
        }

        int parts = target.PartCount;

        // ── n ≥ 6：算法 1 → 算法 2 → 算法 3 ──
        if (parts >= 6)
        {
            return AngleAlgorithm.Algorithm1;
        }

        // ── n = 4 ──
        if (parts == 4)
        {
            // 底格是针 ⇒ 算法 5（构造 H 一次性批量恢复）。
            // 注意 PillarSCriterion 的定义 D 也把「底格是针」算作成立，
            // 所以必须先判底格，再判 D。
            if (target[0, column].Type == CellType.Pin)
            {
                return AngleAlgorithm.Algorithm5;
            }

            // D 成立（最低晶体以下 ≥ 2 个空格）⇒ 算法 6。
            if (PillarSCriterion.BottomResourceD(
                    ShapeSignature.ColumnText(target, column)))
            {
                return AngleAlgorithm.Algorithm6;
            }

            // 其余 ⇒ 算法 4（递归提升 + 构造 G 环下落）。
            return AngleAlgorithm.Algorithm4;
        }

        // n = 2 等没有 4 象限结构的形态：仍按 4 的兜底处理。
        return AngleAlgorithm.Algorithm4;
    }

    /// <summary>
    /// 执行某一档的构造。返回 null 表示该档没有专用构造（只有兜底链）。
    /// </summary>
    private static AngleWorkOrder? TryTierOrder(
        AngleAlgorithm algorithm,
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int cap,
        AngleBuildKind kind)
    {
        switch (algorithm)
        {
            case AngleAlgorithm.NoCrystal:
                return BuildNoCrystalOrder(target, column, kind);

            case AngleAlgorithm.SimpleTail:
                return BuildSimpleTailOrder(target, column, kind);

            case AngleAlgorithm.Algorithm1:
            {
                /*
                 * 算法 1（提升）：只使用「把缺口下方的普通格抬到缺口顶端」的填法。
                 */
                AngleWorkOrder lift = TryBuildFillBreak(
                    target,
                    column,
                    layers,
                    parts,
                    half,
                    kind,
                    FillPlanMode.LiftOnly);

                if (lift.IsComplete)
                {
                    return Label(lift, AngleAlgorithm.Algorithm1, attempt: true);
                }

                /*
                 * 算法 2（正规核心）：只使用「把缺口直接填成紧核心」的填法。
                 */
                AngleWorkOrder direct = TryBuildFillBreak(
                    target,
                    column,
                    layers,
                    parts,
                    half,
                    kind,
                    FillPlanMode.DirectOnly);

                if (direct.IsComplete)
                {
                    return Label(direct, AngleAlgorithm.Algorithm2, attempt: true);
                }

                /*
                 * 算法 3（构造 F 送回）：一次受控下落到 d 层。
                 */
                int towerBottom = ResolveTowerBottom(target, column) - 1;
                int dropLayer = ResolveDropLayer(target, column);

                AngleWorkOrder f = towerBottom < 0 || dropLayer < 0
                    ? Incomplete(
                        column,
                        kind,
                        "算法 3（构造 F 送回）需要晶体塔正下方有可下降的落点，" +
                        "该列没有。",
                        construction: AngleConstruction.F)
                    : BuildCutPath(
                        target,
                        column,
                        layers,
                        parts,
                        half,
                        towerBottom,
                        dropLayer,
                        kind,
                        AngleConstruction.F);

                if (f.IsComplete)
                {
                    return Label(f, AngleAlgorithm.Algorithm3, attempt: true);
                }

                return AppendFailure(
                    Label(lift, AngleAlgorithm.Algorithm1, attempt: true),
                    "算法 2（正规核心）与算法 3（构造 F 送回）也未通过：" +
                    StripUncoveredPrefix(
                        f.Note ??
                        direct.Note ??
                        "未知原因"));
            }

            case AngleAlgorithm.Algorithm5:
            {
                /*
                 * 算法 5（底格是针）：构造 H——一次顶针把整段块批量恢复。
                 */
                int s = ResolveTowerBottom(target, column) - 1;
                int d = ResolveDropLayer(target, column);

                if (s <= 0 || d < 0)
                {
                    return null;
                }

                AngleWorkOrder h = TryBuildH(
                    target,
                    column,
                    layers,
                    parts,
                    s,
                    d,
                    kind);

                return h.IsComplete
                    ? Label(h, AngleAlgorithm.Algorithm5, attempt: true)
                    : null;
            }

            case AngleAlgorithm.Algorithm6:
                return TryBuildAlgorithm6(
                    target,
                    column,
                    layers,
                    parts,
                    kind);

            case AngleAlgorithm.Algorithm4:
            default:
                // 算法 4 的构造就是兜底链本身（提升 + 构造 G），由 Build 统一执行。
                return null;
        }
    }

    /// <summary>晶体塔底端（0 起）；最高晶体段从地面开始时返回 0。</summary>
    private static int ResolveTowerBottom(
        ShapeDocument target,
        int column)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        if (highest < 0)
        {
            return 0;
        }

        int t = highest;

        while (t > 0 &&
               target[t - 1, column].Type == CellType.Crystal)
        {
            t--;
        }

        return t;
    }

    /// <summary>晶体塔正下方窗口里的最高非空层（0 起）；没有时返回 -1。</summary>
    private static int ResolveDropLayer(
        ShapeDocument target,
        int column)
    {
        int s = ResolveTowerBottom(target, column) - 1;

        for (int layer = s - 1;
             layer >= 0;
             layer--)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                return layer;
            }
        }

        return -1;
    }

    /// <summary>分档名（给 Note / Limitations 用）。</summary>
    private static string DescribeAlgorithm(
        AngleAlgorithm algorithm)
    {
        return algorithm switch
        {
            AngleAlgorithm.NoCrystal => "无晶体 → 构造 C",
            AngleAlgorithm.SimpleTail => "Simple → 构造 A + 构造 C（尾部）",
            AngleAlgorithm.Algorithm1 => "算法 1（提升）",
            AngleAlgorithm.Algorithm2 => "算法 2（正规核心）",
            AngleAlgorithm.Algorithm3 => "算法 3（构造 F 送回）",
            AngleAlgorithm.Algorithm4 => "算法 4（递归提升 + 构造 G 环下落）",
            AngleAlgorithm.Algorithm5 => "算法 5（构造 H 一次性批量恢复）",
            AngleAlgorithm.Algorithm6 => "算法 6（底块提升 → 正规化 → 构造 H → 构造 C）",
            _ => "兜底构造"
        };
    }

    #endregion

    #region 构造 C / 构造 A（无晶体与 Simple 档）

    /// <summary>
    /// 无晶体档：构造 C——逐层追加 T/P。
    ///
    /// <para>
    /// 这一档没有辅助列、也没有碎晶/顶针操作：目标列本身就是「从地面往上逐层
    /// 叠 T/P」的结果，所以工序单只给一条计划说明（每一层叠什么），不伪造
    /// 碎晶/下落步骤。这也是 <see cref="AngleWorkOrderBuilder"/> 对 Simple 角
    /// 一直以来的约定（见测试「角工序:简单角只有说明」）。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildNoCrystalOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind)
    {
        string plan = DescribeColumnPlan(target, column);

        var limitations = BaseLimitations(
            "构造 C（逐层追加 T/P）：该列没有晶体，不需要碎晶，也没有辅助列；" +
            "从地面往上按计划逐层叠 T/P 即可，因此工序单不列碎晶步骤。");

        limitations.Add("逐层计划（底 → 顶）：" + plan);

        return new AngleWorkOrder(
            column,
            kind,
            new[]
            {
                new AngleWorkStep(
                    "简单角（无晶体）：按构造 C 逐层追加 T/P，无需碎晶工序",
                    null,
                    AngleWorkStepKind.Note)
            },
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.C,
            Algorithm = AngleAlgorithm.NoCrystal,
            Rank = 0,
            Limitations = limitations
        };
    }

    /// <summary>
    /// Simple 档（最高晶体连续接地）：构造 A（紧列）+ 构造 C（尾部）。
    ///
    /// <para>
    /// 紧列部分从地面逐层结晶到最高晶体（构造 A），尾部（最高晶体之上）
    /// 逐层追加 T/P（构造 C）。同样没有辅助列、没有碎晶操作，所以只给一条
    /// 计划说明，把两段的层号写清楚。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildSimpleTailOrder(
        ShapeDocument target,
        int column,
        AngleBuildKind kind)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        var tight = new List<int>();

        for (int layer = 0; layer <= highest; layer++)
        {
            tight.Add(layer);
        }

        string plan = DescribeColumnPlan(target, column);

        var limitations = BaseLimitations(
            "构造 A（紧列）+ 构造 C（尾部）：最高晶体连续接地 ⇒ 从地面到最高晶体" +
            "逐层结晶成紧列（构造 A），最高晶体之上按计划逐层追加 T/P（构造 C）；" +
            "该形态没有缺口，不需要碎晶。");

        limitations.Add(
            $"紧列段 = 第 1–{highest + 1} 层；尾部 = 第 {highest + 2}–{target.LayerCount} 层。");

        limitations.Add("逐层计划（底 → 顶）：" + plan);

        return new AngleWorkOrder(
            column,
            kind,
            new[]
            {
                new AngleWorkStep(
                    "简单角：按构造 A 做紧列 + 构造 C 追加尾部，无需碎晶工序",
                    null,
                    AngleWorkStepKind.Note)
            },
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.A,
            Algorithm = AngleAlgorithm.SimpleTail,
            Rank = 0,
            Limitations = limitations
        };
    }

    /// <summary>把一列写成「底 → 顶」的逐层计划说明。</summary>
    private static string DescribeColumnPlan(
        ShapeDocument target,
        int column)
    {
        var parts = new List<string>(target.LayerCount);

        for (int layer = 0; layer < target.LayerCount; layer++)
        {
            parts.Add(
                $"第 {layer + 1} 层 " +
                Describe(target[layer, column]));
        }

        return string.Join("，", parts);
    }

    #endregion

    #region 算法 6（n = 4、D 成立）：底块提升 → 正规化 → 构造 H → 构造 C

    /// <summary>
    /// 算法 6：n = 4、D 成立（最低晶体以下至少两空）时的正解（TODO.md §3.1.1）。
    ///
    /// <para><b>四步</b>：</para>
    /// <list type="number">
    /// <item><b>底块提升 / 正规化</b>：把目标列的晶体整体下移一层（抵消顶针的抬升），
    /// 普通格留在原位——顶针会把它们抬起一层，再由构造 H 让它们整批落回原位。</item>
    /// <item><b>构造 H（造塔 X）</b>：给塔补三根辅助列（支撑柱 S / 落块柱 B /
    /// 引爆柱 F），使 X 稳定、每列过五项条件、且 <see cref="ExchangeChain.TryBuild"/>
    /// 能用四片 + 两次交换装出来（rank 0）。</item>
    /// <item><b>顶针</b>：一次真实 <see cref="ShapePhysics.Pin"/>，把塔抬一层并让
    /// 引爆柱顶端的晶体整簇破碎，落块柱带着目标列的普通格整批落回目标层。</item>
    /// <item><b>构造 C（支柱交接）</b>：把支撑柱换成一根满高新柱、拆掉旧柱与
    /// 辅助列，目标列逐格保持不变；最后一步提取目标角。</item>
    /// </list>
    /// <para>
    /// 候选塔全部用真实物理回放：稳定 → 每列五项条件 → 交换链（含
    /// <see cref="Replay.Verify"/>）→ 顶针后目标列逐格对齐 → 支柱交接后仍稳定。
    /// 任何一步不过就换下一份候选；全部不过返回 null，由调用方退回兜底链。
    /// 这里没有任何「按 n × 层数」的查表：候选由目标列的结构生成。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildAlgorithm6(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind)
    {
        if (parts != 4)
        {
            return null;
        }

        // ── 第一步：底块提升 + 正规化（晶体下移一层，普通格留原位）──
        string? normalizedTower = BuildNormalizedTowerColumn(
            target,
            column,
            layers);

        if (normalizedTower is null)
        {
            return null;
        }

        int[] aux =
        {
            Wrap(column - 1, parts),
            Wrap(column + 1, parts),
            Wrap(column + 2, parts)
        };

        var limitations = BaseLimitations(
            "算法 6：底块提升 → 正规化 → 构造 H（一次顶针）→ 构造 C（支柱交接）。" +
            "造塔 X 用交换链装配（rank 0），角工序的 rank 只统计这一次顶针。");

        limitations.Add(
            "X 的每一列在补一根满层柱之后都稳定，所以可以用交换链装配（§5.2 的前提）。");

        limitations.Add(
            "本档不含末步结晶：Crystallize 是全局操作，会把刚做出来的缺口填平。");

        /*
         * 「缺口形态」预筛：构造 H 的落块柱每个下降通道只搬运一枚普通格。
         * 正规化塔的晶体正下方若叠着两枚以上普通格，顶针后它们要各自落到
         * 不同层、而晶体还必须留在原位——一次顶针做不到（`.TT.c` 就是
         * 这种形态，顶层普通格被下层挡住，落到目标层下面一层）。
         * 这类形态直接交给填充+暴破，不要在 H 的五重枚举里白跑整圈。
         */
        if (!CanUseConstructH(normalizedTower))
        {
            return null;
        }

        /*
         * 塔列优先用「缺口错开层」的变体：普通格不留在缺口下方，而是紧贴
         * 晶体，由落块柱完成最后一层位移。这样塔的目标列不会再包含一个
         * 正在求解的子形态（例如 `.T..c` 的正规化塔 `.T.c.` 里含 `.T.c`），
         * 否则造塔本身又要多一次顶破。正规化塔仍作为后备候选。
         */
        foreach (string towerColumn in EnumerateTowerColumns(normalizedTower))
        {
            foreach ((int supportPosition, int blockPosition, int fusePosition)
                     in EnumerateRoleAssignments(aux))
            {
                foreach (string supportText in EnumerateSupportColumns(layers))
                {
                    foreach (string blockText in EnumerateBlockColumns(
                                 towerColumn,
                                 target,
                                 column,
                                 layers))
                    {
                        AngleWorkOrder? order = TryAlgorithm6Candidate(
                            target,
                            column,
                            layers,
                            parts,
                            kind,
                            towerColumn,
                            supportText,
                            blockText,
                            supportPosition,
                            blockPosition,
                            fusePosition,
                            limitations);

                        if (order is not null)
                        {
                            return order;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 构造 H 是否适用于这个塔列：晶体正下方只能有一枚普通格。
    ///
    /// <para>
    /// 顶针把整列上移一层后，落块柱把「源层」的普通格整批放到目标层；
    /// 若晶体下面叠着两枚普通格，下面的那枚会挡住上面那枚的下降通道，
    /// 两枚不可能同时停在各自的目标层。这是形态层面的限制，先筛掉即可，
    /// 不必进入 support × block × fuse 的枚举。
    /// </para>
    /// </summary>
    private static bool CanUseConstructH(
        string towerColumn)
    {
        int crystal = towerColumn.LastIndexOf(ShapeSignature.CrystalCode);

        if (crystal < 0)
        {
            return true;
        }

        int stackedSolids = 0;

        for (int layer = crystal - 1;
             layer >= 0 &&
             towerColumn[layer] == ShapeSignature.SolidCode;
             layer--)
        {
            stackedSolids++;
        }

        return stackedSolids <= 1;
    }

    /// <summary>
    /// 塔列候选：优先「缺口错开层」的变体，其次才是正规化塔列。
    /// </summary>
    private static IEnumerable<string> EnumerateTowerColumns(
        string normalizedTower)
    {
        string? offset = OffsetGapBelowCrystal(normalizedTower);

        if (offset is not null &&
            !string.Equals(offset, normalizedTower, StringComparison.Ordinal))
        {
            yield return offset;
        }

        yield return normalizedTower;
    }

    /// <summary>
    /// 把「普通格 / 空 / 晶体」的纵向缺口错开一层：普通格上移，紧贴晶体。
    ///
    /// <para>
    /// 正规化塔列把晶体下移一层、普通格留在原位，于是普通格与晶体之间
    /// 隔着一个空格。例如 <c>.T..c</c> 的塔列 <c>.T.c.</c> 前四层正好是
    /// 子形态 <c>.T.c</c>。把普通格上移一格得到 <c>..Tc.</c>，缺口落到
    /// 普通格下方，塔列里不再出现这个子形态，造塔可以一次成型。
    /// </para>
    /// </summary>
    private static string? OffsetGapBelowCrystal(
        string towerColumn)
    {
        char[] cells = towerColumn.ToCharArray();

        bool changed = false;

        int index = 0;

        while (index + 2 < cells.Length)
        {
            bool matches =
                cells[index] == ShapeSignature.SolidCode &&
                cells[index + 1] == ShapeSignature.EmptyCode &&
                cells[index + 2] == ShapeSignature.CrystalCode;

            if (!matches)
            {
                index++;
                continue;
            }

            cells[index] = ShapeSignature.EmptyCode;
            cells[index + 1] = ShapeSignature.SolidCode;

            changed = true;
            index += 2;
        }

        return changed ? new string(cells) : null;
    }

    /// <summary>
    /// 正规化目标列：晶体下移一层（抵消顶针抬升），普通格留在原位。
    ///
    /// <para>
    /// 例如 <c>.T.c</c>（4 层）⇒ 塔列 <c>.Tc.</c>：晶体从 4 层落到 3 层，
    /// 顶针后又回到 4 层；第 2 层的普通格被顶针抬到 3 层，再由构造 H 整批落回。
    /// 任何冲突（晶体落地、与普通格/晶体重叠、出现针）都返回 null。
    /// </para>
    /// </summary>
    private static string? BuildNormalizedTowerColumn(
        ShapeDocument target,
        int column,
        int layers)
    {
        var cells = new char[layers];

        Array.Fill(cells, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell cell = layer < target.LayerCount
                ? target[layer, column]
                : ShapeCell.Empty();

            switch (cell.Type)
            {
                case CellType.Empty:
                    break;

                case CellType.Shape:
                    if (cells[layer] != ShapeSignature.EmptyCode)
                    {
                        return null;
                    }

                    cells[layer] = ShapeSignature.SolidCode;
                    break;

                case CellType.Crystal:
                    if (layer == 0 ||
                        cells[layer - 1] != ShapeSignature.EmptyCode)
                    {
                        return null;
                    }

                    cells[layer - 1] = ShapeSignature.CrystalCode;
                    break;

                default:
                    // 针不参与算法 6（底格是针走算法 5）。
                    return null;
            }
        }

        // 塔列第 1 层必须为空：顶针会在前态底层非空的位置补针，
        // 补出来的针会顶到目标角第 1 层。
        return cells[0] == ShapeSignature.EmptyCode
            ? new string(cells)
            : null;
    }

    /// <summary>
    /// 三根辅助列的角色分配：支撑柱 S、落块柱 B、引爆柱 F。
    /// 穷举 3! 种分配（环上位置固定为角列两侧与角列 +2 列）。
    /// </summary>
    private static IEnumerable<(int Support, int Block, int Fuse)>
        EnumerateRoleAssignments(int[] aux)
    {
        yield return (aux[0], aux[1], aux[2]);
        yield return (aux[1], aux[0], aux[2]);
        yield return (aux[2], aux[1], aux[0]);
        yield return (aux[1], aux[2], aux[0]);
        yield return (aux[0], aux[2], aux[1]);
        yield return (aux[2], aux[0], aux[1]);
    }

    /// <summary>
    /// 支撑柱候选：第 1–h 层普通格，并在第 p 层放一枚针（顶针后针上移一层，
    /// 用来托住目标列在目标层的晶体）；外加满高纯普通柱。
    /// </summary>
    private static IEnumerable<string> EnumerateSupportColumns(
        int layers)
    {
        if (layers < 2)
        {
            yield break;
        }

        for (int height = 2; height <= layers; height++)
        {
            for (int pin = 1; pin < height; pin++)
            {
                char[] cells = new string(
                        ShapeSignature.SolidCode,
                        height)
                    .ToCharArray();

                cells[pin] = ShapeSignature.PinCode;

                yield return new string(cells) +
                             new string(
                                 ShapeSignature.EmptyCode,
                                 layers - height);
            }
        }

        yield return new string(ShapeSignature.SolidCode, layers);
    }

    /// <summary>
    /// 落块柱候选：底层放一枚晶体（与引爆柱同簇，顶针后一起碎掉），
    /// 上面在目标列的普通格所在层放普通格——顶针把它们抬起一层，
    /// 碎晶后它们和目标列的普通格一起整批落回目标层。
    /// </summary>
    private static IEnumerable<string> EnumerateBlockColumns(
        string towerColumn,
        ShapeDocument target,
        int column,
        int layers)
    {
        var solidLayers = new List<int>();

        for (int layer = 0; layer < layers; layer++)
        {
            if (layer < target.LayerCount &&
                target[layer, column].Type == CellType.Shape)
            {
                solidLayers.Add(layer);
            }
        }

        // 先试「只补目标列普通格所在层」，再逐层加高，最后是满高柱。
        for (int height = 1; height < layers; height++)
        {
            for (int variant = 0; variant < 2; variant++)
            {
                var cells = new char[layers];

                Array.Fill(cells, ShapeSignature.EmptyCode);

                cells[0] = ShapeSignature.CrystalCode;

                if (variant == 0)
                {
                    foreach (int solid in solidLayers)
                    {
                        if (solid > 0)
                        {
                            cells[solid] = ShapeSignature.SolidCode;
                        }
                    }
                }
                else
                {
                    for (int layer = 1; layer <= height; layer++)
                    {
                        cells[layer] = ShapeSignature.SolidCode;
                    }
                }

                yield return new string(cells);
            }
        }

        // 满高落块柱：底晶 + 上面全普通格。
        yield return
            ShapeSignature.CrystalCode +
            new string(ShapeSignature.SolidCode, layers - 1);

        /*
         * 「缺口错开层」用的落块柱：晶体段从地面连续到塔列晶体下方，
         * 上面两格是普通格（分别对齐塔列晶体与其下方那格）。
         * 顶针后引爆柱把这段晶体整簇打碎，这两枚普通格正好带着
         * 目标列的普通格落到目标层——`.T..c` 的一次成型塔就靠它。
         */
        int crystalLayer = towerColumn.LastIndexOf(
            ShapeSignature.CrystalCode);

        if (crystalLayer >= 2 && crystalLayer < layers)
        {
            var cells = new char[layers];

            Array.Fill(cells, ShapeSignature.EmptyCode);

            for (int layer = 0; layer < crystalLayer - 1; layer++)
            {
                cells[layer] = ShapeSignature.CrystalCode;
            }

            cells[crystalLayer - 1] = ShapeSignature.SolidCode;
            cells[crystalLayer] = ShapeSignature.SolidCode;

            yield return new string(cells);
        }
    }

    /// <summary>把一份候选塔装出来并用真实物理回放整条链。</summary>
    private static AngleWorkOrder? TryAlgorithm6Candidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        string towerColumn,
        string supportText,
        string blockText,
        int supportPosition,
        int blockPosition,
        int fusePosition,
        List<string> limitations)
    {
        var tower = new ShapeDocument(layers, parts);

        /*
         * 角列的晶体颜色必须与目标一致：顶针保留颜色，
         * 否则顶针后目标列会因颜色不同而对不上。
         */
        char crystalColor = CrystalColor(target, column);

        WriteColumn(tower, column, towerColumn, crystalColor);
        WriteColumn(tower, supportPosition, supportText, crystalColor);
        WriteColumn(tower, blockPosition, blockText, crystalColor);
        WriteColumn(
            tower,
            fusePosition,
            new string(ShapeSignature.CrystalCode, layers),
            crystalColor);

        // ── 构造 H 的复用前提（§5.2）：先看稳定与五项条件 ──
        if (!ShapeOps.IsStable(tower) ||
            !ShapeSignature.AllColumnsLegal(tower, out _))
        {
            return null;
        }

        /*
         * 先用「缺口形态」筛：先做最便宜的真实顶针，只有顶针后目标列
         * 逐格等于目标的候选才值得去跑昂贵的交换链装配（ExchangeChain +
         * Replay.Verify）。这一步把 `.TT.c` 这类根本走不通的形态挡在
         * 交换链枚举之外，耗时从整圈枚举降到只剩少量候选。
         */
        PhysicsResult pinResult = ShapePhysics.Pin(tower);

        if (!pinResult.Success || pinResult.Document is null)
        {
            return null;
        }

        ShapeDocument pinned = pinResult.Document;

        if (!MatchesTargetColumn(
                pinned,
                target,
                column,
                layers,
                out _))
        {
            return null;
        }

        // ── 造塔子配方：只允许交换链（rank 0），并端到端校验 ──
        if (!ExchangeChain.TryBuild(tower, out Recipe? subRecipe) ||
            subRecipe is null ||
            subRecipe.Rank != 0 ||
            !Replay.Verify(subRecipe, tower, out _))
        {
            return null;
        }

        // ── 构造 C（支柱交接）：装新支撑柱、拆旧柱与辅助列 ──
        ShapeDocument? handover = BuildPillarHandover(
            pinned,
            target,
            column,
            layers,
            parts,
            out int newSupportColumn);

        if (handover is null)
        {
            return null;
        }

        var steps = new List<AngleWorkStep>
        {
            new(
                "造塔 X（子配方：交换链装配，rank 0；角列 = 目标列晶体下移一层，" +
                "另配支撑柱/落块柱/引爆柱）",
                tower,
                AngleWorkStepKind.PreState),

            new(
                "顶针：一次 Pin 把塔整体抬一层——引爆柱顶端超限整簇碎裂，" +
                "落块柱带着目标列的普通格落回目标层",
                pinned,
                AngleWorkStepKind.Pin),

            new(
                $"构造 C：装新支撑柱（第 {newSupportColumn + 1} 象限满高普通柱），" +
                "拆掉旧柱与辅助列，目标列逐格保持",
                handover,
                AngleWorkStepKind.Support),

            new(
                "清除辅助象限，得到目标角（S 形态）",
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
            Construction = AngleConstruction.C,
            Algorithm = AngleAlgorithm.Algorithm6,
            Rotation = 0,
            AuxiliaryColumns = new[]
            {
                supportPosition,
                blockPosition,
                fusePosition
            },
            SubRecipe = subRecipe,
            Rank = 1,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 构造 C：只保留顶针后的目标列，装一根满高新支撑柱，拆掉旧柱和全部辅助列。
    /// 交接后的图形必须稳定、目标列逐格不变。
    /// </summary>
    private static ShapeDocument? BuildPillarHandover(
        ShapeDocument pinned,
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        out int supportColumn)
    {
        foreach (int candidate in new[]
                 {
                     Wrap(column - 1, parts),
                     Wrap(column + 1, parts)
                 })
        {
            var handover = new ShapeDocument(layers, parts);

            for (int layer = 0; layer < layers; layer++)
            {
                handover[layer, column] =
                    pinned[layer, column].Clone();
            }

            WriteColumn(
                handover,
                candidate,
                new string(ShapeSignature.SolidCode, layers),
                CrystalColor(target, column));

            if (!ShapeOps.IsStable(handover))
            {
                continue;
            }

            if (!MatchesTargetColumn(
                    handover,
                    target,
                    column,
                    layers,
                    out _))
            {
                continue;
            }

            supportColumn = candidate;

            return handover;
        }

        supportColumn = -1;

        return null;
    }

    /// <summary>把一列列文本写进图形（晶体用指定颜色，角列的颜色必须与目标一致）。</summary>
    private static void WriteColumn(
        ShapeDocument document,
        int column,
        string text,
        char crystalColor)
    {
        int target = Wrap(column, document.PartCount);

        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            char code = layer < text.Length
                ? text[layer]
                : ShapeSignature.EmptyCode;

            document[layer, target] = code switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal(crystalColor),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }
    }

    #endregion

    /// <summary>填充+暴破的候选填法（§6 分档用）。</summary>
    private enum FillPlanMode
    {
        /// <summary>兜底：直接填与抬升填的任意组合。</summary>
        All,

        /// <summary>算法 2（正规核心）：只直接填。</summary>
        DirectOnly,

        /// <summary>算法 1（提升）：只抬升填。</summary>
        LiftOnly
    }

    /// <summary>
    /// 单段缺口的填法。
    ///
    /// <para>
    /// <see cref="LeaveGap"/> 只用于兜底链：目标列里的这段缺口本来就存在，
    /// 不必先填成晶体再造出来。用它保住缺口上方的普通格——
    /// 缺口上方若是晶体，抬升填法会把普通格抬高一层，切口打开后它会
    /// 直接落过两层（<c>T.T.c → TT..c</c>）；不改这一段、只填下方缺口，
    /// 普通格落到目标层时正好由保留侧的托柱侧向托住。
    /// </para>
    /// </summary>
    private enum FillChoice
    {
        /// <summary>整段缺口直接填成晶体。</summary>
        Direct,

        /// <summary>把缺口下方的普通格抬到缺口顶端，再把它让出的位置填成晶体。</summary>
        Lift,

        /// <summary>保持缺口原样（不填、不抬升）。</summary>
        LeaveGap
    }

    /// <summary>
    /// 目标列是否只有最上面一段晶体（单晶塔）。
    ///
    /// <para>
    /// 单晶塔的角（<c>T.T.c</c>、<c>P.T.c</c>、<c>.TT.c</c>）只有一枚晶体；
    /// 它下方的临时晶体被打碎后，中间普通格要靠托柱侧向托住。
    /// 多晶列则有晶体塔，缺口应当由构造 G/H 的环下落来造。
    /// </para>
    /// </summary>
    private static bool IsSingleCrystalTower(
        ShapeDocument target,
        int column)
    {
        int crystals = 0;

        for (int layer = 0; layer < target.LayerCount; layer++)
        {
            if (target[layer, column].Type == CellType.Crystal)
            {
                crystals++;
            }
        }

        return crystals == 1;
    }

    /// <summary>
    /// 循环前态的判据：前态的目标列与目标列相同，或前态没有任何临时晶体/抬升。
    ///
    /// <para>
    /// 验收若是「前态 → 切 → 下落 == 目标」，当前态等于目标时等式平凡成立，
    /// 会把「先把目标本身做出来、再切掉辅助柱」这种循环误报成完成。
    /// 所以必须要求前态的目标列与目标不同，并且真的含一处临时晶体或一次抬升。
    /// </para>
    /// </summary>
    private const string CyclicPreStateReason = "前态与目标相同（循环）";

    private static bool IsCyclicPreState(
        ShapeDocument preState,
        ShapeDocument target,
        int column,
        bool hasLift)
    {
        if (ShapeSignature.ColumnText(preState, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            return true;
        }

        if (hasLift)
        {
            return false;
        }

        return !HasTemporaryCrystal(preState, target);
    }

    /// <summary>
    /// 工序单层面的循环判据：前态步的目标列是否等于目标列。
    ///
    /// <para>
    /// 这是 <see cref="Build"/> / 兜底链的统一闸门，防止某个构造分支
    /// 绕过各自的候选检查，把「前态 = 目标」的平凡回放当成完成。
    /// </para>
    /// </summary>
    private static bool HasCyclicPreState(
        AngleWorkOrder order,
        ShapeDocument target)
    {
        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind != AngleWorkStepKind.PreState ||
                step.Shape is null)
            {
                continue;
            }

            return ShapeSignature.ColumnText(step.Shape, order.Column) ==
                   ShapeSignature.ColumnText(target, order.Column);
        }

        return false;
    }

    /// <summary>
    /// 前态里是否存在目标里没有的晶体（按同一格比较）。
    ///
    /// <para>
    /// 临时晶体是「这一步真的在施工」的证据：碎晶前态里的填充晶体、
    /// 造塔前态里晶体塔之外的多余晶体都算。只有辅助普通柱的简单前态不算。
    /// </para>
    /// </summary>
    private static bool HasTemporaryCrystal(
        ShapeDocument preState,
        ShapeDocument target)
    {
        for (int part = 0; part < preState.PartCount; part++)
        {
            for (int layer = 0; layer < preState.LayerCount; layer++)
            {
                if (preState[layer, part].Type != CellType.Crystal)
                {
                    continue;
                }

                bool alsoInTarget =
                    part < target.PartCount &&
                    layer < target.LayerCount &&
                    target[layer, part].Type == CellType.Crystal;

                if (!alsoInTarget)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 原有策略：构造 H（一次顶针）、多轮构造 G、构造 F，
    /// 以及缺口不在晶体塔正下方时的旧填充+暴破方案。
    /// 现在是论文分档失败后的兜底实现（不删，只降级）。
    /// </summary>
    private static AngleWorkOrder BuildLegacy(
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

        AngleBuildKind kind = AngleClassifier.Classify(target, column);

        if (kind == AngleBuildKind.Simple)
        {
            return new AngleWorkOrder(
                column,
                kind,
                new[]
                {
                    new AngleWorkStep(
                        "简单角，无需工序",
                        null,
                        AngleWorkStepKind.Note)
                },
                null)
            {
                IsComplete = true
            };
        }

        if (cap < target.LayerCount)
        {
            return Incomplete(
                column,
                kind,
                $"cap（{cap}）小于目标层数（{target.LayerCount}），无法在前态里保留完整目标列。");
        }

        if (!ShapeRules.IsValidLayerCount(cap))
        {
            return Incomplete(
                column,
                kind,
                $"cap（{cap}）不在 {ShapeRules.MinLayerCount}–{ShapeRules.MaxLayerCount} 之间。");
        }

        if (!ShapeRules.IsValidPartCount(target.PartCount))
        {
            return Incomplete(
                column,
                kind,
                $"象限数（{target.PartCount}）必须是偶数。");
        }

        int layers = cap;
        int parts = target.PartCount;
        int half = parts / 2;

        // === 目标列的形态分析 ===

        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        // t = 最高晶体段的底端（0 起）。
        int t = highest;

        while (t > 0 &&
               target[t - 1, column].Type == CellType.Crystal)
        {
            t--;
        }

        int topLength = highest - t + 1;
        int s = t - 1;

        if (t <= 0)
        {
            return Incomplete(
                column,
                kind,
                "最高晶体段从地面开始，缺口不在晶体塔正下方，G/F 不适用。");
        }

        // === 缺口不在晶体塔正下方：填充 + 暴破 ===

        if (target[s, column].Type != CellType.Empty)
        {
            /*
             * 塔底正下方不是空位，G/F/H 的逆变换都要求把 d 层的普通格搬到 s 层，
             * 这里没有 s 层的空位可用。
             *
             * 改用「填充 + 暴破」：把塔下的缺口区域填成晶体（相当于用构造 A
             * 在这里结晶），再用切割把这一整段晶体打碎，缺口就由切割造出来。
             */
            if (parts < 4)
            {
                return Incomplete(
                    column,
                    kind,
                    "缺口不在晶体塔正下方，填充+暴破至少要 4 个象限（目标列 + 托柱 + 桥柱）。");
            }

            return BuildLegacyFillBreak(
                target,
                column,
                layers,
                parts,
                half,
                t,
                kind);
        }

        int d = -1;

        for (int layer = s - 1;
             layer >= 0;
             layer--)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                d = layer;
                break;
            }
        }

        if (d < 0)
        {
            return Incomplete(
                column,
                kind,
                "晶体塔下方的窗口里没有普通格，G/F 没有可下降的材料。");
        }

        if (target[d, column].Type != CellType.Shape)
        {
            return Incomplete(
                column,
                kind,
                $"缺口下方的最高非空格（第 {d + 1} 层）不是普通格；G/F 只下降普通格。");
        }

        bool singleLayerGap = d == s - 1;

        if (parts == 4)
        {
            // 构造 G 的 B 列要在 t+1 层放针；晶体塔本身至少三连晶（到 t+2），
            // 因此 t+2 必须仍落在 cap 之内。
            bool gPinRoom = s + 2 < layers;
            bool gTowerOk = topLength >= 3;

            if (singleLayerGap)
            {
                if (gTowerOk && gPinRoom)
                {
                    return BuildCutPath(
                        target,
                        column,
                        layers,
                        parts,
                        half,
                        s,
                        d,
                        kind,
                        AngleConstruction.G);
                }

                string reason;

                if (!gTowerOk)
                {
                    reason = "构造 G 需要晶体塔至少三连晶（t、t+1、t+2 都是晶体），该列不满足。";
                }
                else
                {
                    reason = "构造 G 需要在 t+1 层放针，但晶体塔已经顶到 cap，放不下 G 的支撑环。";
                }

                return Incomplete(column, kind, reason);
            }

            // === n = 4、多层缺口：优先一次顶针（构造 H），否则多轮 G ===

            AngleWorkOrder hOrder = TryBuildH(
                target,
                column,
                layers,
                parts,
                s,
                d,
                kind);

            if (hOrder.IsComplete)
            {
                return hOrder;
            }

            if (gTowerOk && gPinRoom)
            {
                AngleWorkOrder gOrder = BuildMultiRoundG(
                    target,
                    column,
                    layers,
                    parts,
                    half,
                    t,
                    s,
                    d,
                    kind);

                if (gOrder.IsComplete)
                {
                    return gOrder;
                }

                return AppendFailure(
                    gOrder,
                    "构造 H（一次顶针）也未通过：" +
                    StripUncoveredPrefix(hOrder.Note ?? "不适用"));
            }

            return AppendFailure(
                hOrder,
                "多轮构造 G 也不适用：晶体塔不足三连晶（t、t+1、t+2 都要是晶体）。");
        }

        if (parts >= 6)
        {
            return BuildCutPath(
                target,
                column,
                layers,
                parts,
                half,
                s,
                d,
                kind,
                AngleConstruction.F);
        }

        return Incomplete(
            column,
            kind,
            "象限数既不是 4 也不 ≥ 6，构造 G/F 都不适用。");
    }

    #region 填充 + 暴破（首选）

    /// <summary>
    /// 首选方案：填充 + 暴破。
    ///
    /// <para>
    /// 前态 = 目标列（缺口已填成晶体）+ 托柱（目标列西侧）+ 桥柱（东侧）。
    /// 目标列最高晶体以下的每一段缺口都填成晶体：
    /// </para>
    /// <list type="bullet">
    /// <item><b>直接填</b>：缺口上下都不是保留晶体时，整段缺口填成晶体；</item>
    /// <item><b>抬升填</b>：缺口正下方是普通格时，先把这枚普通格抬到缺口顶端，
    /// 再把它让出的位置填成晶体。托柱在抬升格所在层放顶针，
    /// 顶针不参与水平连通，抬升格在碎晶后才会真正下落。</item>
    /// </list>
    /// <para>
    /// 旋转后一次 <see cref="ShapePhysics.Cut"/> 沿 n/2 刀口把
    /// 「填充晶体 + 桥柱晶体」整块打碎，Collapse 后抬升的普通格落回原位、
    /// 缺口由碎晶造出。每一份候选都用真实 Cut/Collapse 回放并逐格核对目标列，
    /// 核对不过就丢弃，绝不保留未经验证的步骤。
    /// </para>
    /// <para>
    /// <paramref name="mode"/> 供 §6 分档使用：算法 1「提升」只看抬升填法，
    /// 算法 2「正规核心」只看直接填法，兜底链用 <see cref="FillPlanMode.All"/>。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildFillBreak(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind,
        FillPlanMode mode = FillPlanMode.All)
    {
        var limitations = BaseLimitations(
            "填充+暴破：前态把目标列最高晶体以下的缺口填成晶体，" +
            "Cut 沿固定刀口 n/2 把跨刀口的填充晶体与桥柱晶体整块打碎，" +
            "Collapse 后抬升的普通格落回原位、目标列逐格对齐目标。");

        string modeText = mode switch
        {
            FillPlanMode.DirectOnly => "算法 2（正规核心）：只把晶体塔下的缺口直接填成紧核心。",
            FillPlanMode.LiftOnly => "算法 1（提升）：只把缺口下方的普通格抬到缺口顶端再碎晶。",
            _ => "兜底：允许直接填与抬升填的任意组合。"
        };

        limitations.Add(modeText);

        if (parts < 4)
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破至少要 4 个象限（目标列 + 托柱 + 桥柱）。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        if (highest <= 0)
        {
            return Incomplete(
                column,
                kind,
                "目标列没有可碎晶的晶体塔，填充+暴破不适用。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        // === 满高托柱：n = 4 的 S 形态，目标格靠同层侧向粘连停在目标层 ===
        // 只属于兜底链：算法 1/2 是论文的「提升 / 正规核心」，不借用托柱。

        AngleWorkOrder? cyclicHolder = null;

        if (mode == FillPlanMode.All &&
            CanUsePillarHolder(target, column, parts, highest))
        {
            AngleWorkOrder holderOrder = TryBuildPillarHolder(
                target,
                column,
                layers,
                parts,
                half,
                kind);

            if (holderOrder.IsComplete)
            {
                return holderOrder;
            }

            /*
             * 托柱前态 = 目标列本身，按 §7.1 是循环，不算完成。
             * 先记下这个「看起来能做、其实是循环」的候选；如果后面的
             * 缺口填法也都不成立，最终 Note 要如实写出循环原因。
             */
            if (holderOrder.Note is not null &&
                holderOrder.Note.Contains(
                    CyclicPreStateReason,
                    StringComparison.Ordinal))
            {
                cyclicHolder = holderOrder;
            }
        }

        // === 目标列最高晶体以下的缺口段 ===

        var gaps = new List<(int Bottom, int Top)>();

        int layer = 0;

        while (layer < highest)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                layer++;
                continue;
            }

            int bottom = layer;

            while (layer + 1 < highest &&
                   target[layer + 1, column].Type == CellType.Empty)
            {
                layer++;
            }

            gaps.Add((bottom, layer));
            layer++;
        }

        if (gaps.Count == 0)
        {
            return Incomplete(
                column,
                kind,
                "目标列最高晶体以下没有缺口，填充+暴破不适用。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        // === 每段缺口的候选填法：优先直接填，其次抬升普通格 ===

        var options = new List<FillChoice[]>(gaps.Count);

        foreach ((int bottom, int top) in gaps)
        {
            bool bottomIsCrystal =
                bottom > 0 &&
                target[bottom - 1, column].Type == CellType.Crystal;

            bool topIsCrystal =
                target[top + 1, column].Type == CellType.Crystal;

            bool direct = !bottomIsCrystal && !topIsCrystal;

            bool lift =
                bottom > 0 &&
                target[bottom - 1, column].Type == CellType.Shape &&
                (bottom < 2 ||
                 target[bottom - 2, column].Type != CellType.Crystal);

            if (!direct && !lift)
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {bottom + 1}–{top + 1} 层的缺口上下都挨着保留晶体，" +
                    "且下方没有可抬升的普通格，填充+暴破会把保留晶体一起打碎。",
                    limitations,
                    construction: AngleConstruction.FillBreak);
            }

            bool allowDirect =
                mode != FillPlanMode.LiftOnly;

            bool allowLift =
                mode != FillPlanMode.DirectOnly;

            bool useDirect = direct && allowDirect;
            bool useLift = lift && allowLift;

            if (!useDirect && !useLift)
            {
                /*
                 * 分档限定下这段缺口没有可用填法（例如算法 1 只允许抬升，
                 * 而这段缺口只能直接填）。这属于「本档不适用」，交给下一档。
                 */
                return Incomplete(
                    column,
                    kind,
                    $"第 {bottom + 1}–{top + 1} 层的缺口没有本档允许的填法：{modeText}",
                    limitations,
                    construction: AngleConstruction.FillBreak);
            }

            var gapOptions = new List<FillChoice>();

            if (useDirect)
            {
                gapOptions.Add(FillChoice.Direct);
            }

            if (useLift)
            {
                gapOptions.Add(FillChoice.Lift);
            }

            /*
             * 兜底链多给一个「保持缺口原样」的候选：缺口本来就存在于目标列，
             * 只要下方的临时晶体被打碎后普通格能由托柱托到目标层，就不必
             * 再用抬升填法把普通格抬高一整层（那会让它多掉一层）。
             *
             * 只对「单晶塔 + 晶体正下方的单层缺口」开放：
             * <c>T.T.c</c> / <c>P.T.c</c> 的中间普通格上方就是这层缺口，
             * 抬升填法会把它抬高一整层、再落两层。多晶列里晶体塔之间的
             * 缺口要靠构造 G/H 的晶体环下落来造，不能靠少填一段缺口绕过；
             * 缺口有多层时抬升本来就是必要的（它负责把缺口整段搬开），
             * 也留给构造 G/H。
             */
            if (mode == FillPlanMode.All &&
                top == bottom &&
                top + 1 == highest &&
                IsSingleCrystalTower(target, column))
            {
                gapOptions.Add(FillChoice.LeaveGap);
            }

            options.Add(gapOptions.ToArray());
        }

        int combinations = 1;

        foreach (FillChoice[] option in options)
        {
            combinations *= option.Length;
        }

        AngleWorkOrder? bestFailure = null;

        for (int code = 0; code < combinations; code++)
        {
            int remaining = code;

            var plan = new List<(int Bottom, int Top, FillChoice Choice)>(
                gaps.Count);

            for (int index = 0; index < gaps.Count; index++)
            {
                FillChoice[] option = options[index];

                FillChoice choice = option[remaining % option.Length];

                remaining /= option.Length;

                plan.Add((gaps[index].Bottom, gaps[index].Top, choice));
            }

            AngleWorkOrder attempt = TryFillBreakCandidate(
                target,
                column,
                layers,
                parts,
                half,
                kind,
                plan,
                limitations);

            if (attempt.IsComplete)
            {
                return attempt;
            }

            if (bestFailure is null ||
                attempt.Steps.Count > bestFailure.Steps.Count)
            {
                bestFailure = attempt;
            }
        }

        if (bestFailure is null)
        {
            return cyclicHolder ?? Incomplete(
                column,
                kind,
                "填充+暴破没有可用候选。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        if (cyclicHolder is not null)
        {
            return AppendFailure(
                cyclicHolder,
                StripUncoveredPrefix(
                    bestFailure.Note ?? "填充+暴破没有可用候选。"));
        }

        return bestFailure;
    }

    /// <summary>
    /// 按给定的填充方案构造前态并真实回放：
    /// PreState → Cut → Collapse → Target，任何一步核对不过都只保留已验证前缀。
    /// </summary>
    private static AngleWorkOrder TryFillBreakCandidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind,
        List<(int Bottom, int Top, FillChoice Choice)> plan,
        List<string> limitations)
    {
        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers && layer < target.LayerCount;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        var fills = new List<int>();

        foreach ((int bottom, int top, FillChoice choice) in plan)
        {
            if (choice == FillChoice.LeaveGap)
            {
                continue;
            }

            if (choice == FillChoice.Direct)
            {
                for (int layer = bottom; layer <= top; layer++)
                {
                    fills.Add(layer);
                }

                continue;
            }

            // 抬升：普通格搬到缺口顶端，它让出的位置整段填成晶体。
            preState[top, column] = target[bottom - 1, column].Clone();

            for (int layer = bottom - 1; layer <= top - 1; layer++)
            {
                fills.Add(layer);
            }
        }

        foreach (int layer in fills)
        {
            preState[layer, column] = ShapeCell.Crystal(color);
        }

        int holderColumn = Wrap(column - 1, parts);
        int eColumn = Wrap(column + 1, parts);

        // 托柱：满高普通柱，留在西半边托住晶体塔与保留晶体。
        // 构造 D 的原话是「保留目标和原普通柱，删去辅助列」：切割的保留侧
        // 必须同时含目标列与托柱，普通格才不会在碎晶后失去侧向支撑。
        FillPillar(preState, holderColumn);

        // 桥柱：满高普通柱，只在填充层带晶体，跨刀口带走填充晶体。
        FillPillar(preState, eColumn);

        bool hasLift = false;

        foreach ((int bottom, int top, FillChoice choice) in plan)
        {
            _ = bottom;

            if (choice == FillChoice.Lift)
            {
                hasLift = true;

                // 顶针不参与水平连通：抬升格在碎晶后才会下落。
                preState[top, holderColumn] = ShapeCell.Pin();
            }
        }

        foreach (int layer in fills)
        {
            preState[layer, eColumn] = ShapeCell.Crystal(color);
        }

        int p = half - 1;
        int rotation = Wrap(p - column, parts);
        int pRotated = Wrap(p, parts);

        var auxiliaryColumns = new[] { holderColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（塔下缺口已填充成晶体，带托柱/桥柱）",
                preState,
                AngleWorkStepKind.PreState)
        };

        /*
         * 循环判据：前态的目标列若和 target 一样，说明「切→落」这条等式
         * 平凡成立，什么都没造。必须先挡掉，否则会误报完成。
         */
        if (IsCyclicPreState(preState, target, column, hasLift))
        {
            return Incomplete(
                column,
                kind,
                CyclicPreStateReason,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的前态不稳定（一次下落后会变形），后续碎晶验证无法进行。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        var rotated = ShapeOps.Rotate(preState, rotation);

        ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

        CutResult realCut = ShapePhysics.Cut(rotated);

        if (!realCut.Success ||
            realCut.West is null ||
            realCut.East is null)
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的结构性半破与 ShapePhysics.Cut 的结果对不上。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        // === 结构性核对：填充晶体真的被打碎，保留晶体没有被误伤 ===

        bool hadFills = true;
        bool fillsGone = true;

        foreach (int layer in fills)
        {
            if (rotated[layer, pRotated].Type != CellType.Crystal)
            {
                hadFills = false;
            }

            if (rawWest[layer, pRotated].Type == CellType.Crystal)
            {
                fillsGone = false;
            }
        }

        bool keptCrystals = true;

        for (int layer = 0;
             layer < layers && layer < target.LayerCount;
             layer++)
        {
            if (target[layer, column].Type == CellType.Crystal &&
                rawWest[layer, pRotated].Type != CellType.Crystal)
            {
                keptCrystals = false;
            }
        }

        if (!hadFills || !fillsGone || !keptCrystals)
        {
            return Incomplete(
                column,
                kind,
                "切割没有只打碎填充晶体（或误伤了保留晶体），因此不保留 Cut 步。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，打碎填充晶体与桥柱晶体"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "打碎填充晶体与桥柱晶体",
            rawBack,
            AngleWorkStepKind.Cut));

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        if (!MatchesTargetColumn(
                collapsedBack,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破后的目标列与目标不符：" + difference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "填充+暴破结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            "下落：碎晶后材料落位，目标列的缺口由切割造出",
            collapsedBack,
            AngleWorkStepKind.Collapse));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.FillBreak,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 是否可以用「满高托柱」作为碎晶前态。
    ///
    /// 条件是：n = 4、底层为空、最高晶体就在画布顶层（没有尾部）、
    /// 且目标列属于定理 3 的 S（五项条件 ∧ (D ∨ F)）。
    /// 满足时目标列本身就是碎晶后的形态，
    /// 它的每一格只能靠同层侧向粘连停在目标层。
    /// </summary>
    private static bool CanUsePillarHolder(
        ShapeDocument target,
        int column,
        int parts,
        int highest)
    {
        return parts == 4 &&
               highest == target.LayerCount - 1 &&
               target[0, column].Type == CellType.Empty &&
               PillarSCriterion.IsPillarColumn(target, column);
    }

    /// <summary>
    /// 「满高托柱」：前态 = 目标列 + 西侧满高普通柱（覆盖到 cap）+ 东侧辅助桥柱。
    ///
    /// <para>
    /// 托柱从地面连续覆盖到 cap，和目标列同层相邻，所以碎晶/半破之后目标列的
    /// 每一格都和托柱粘在同一个粘连组里，靠<b>侧向粘连</b>停在目标层，
    /// 不会因为下方是空位而下落。半破沿 n/2 刀口只丢弃东半边的辅助桥柱，
    /// 目标列与托柱留在西半边；目标列逐格与目标核对。
    /// </para>
    /// <para>
    /// 必须满足「碎晶后目标列每一目标格同层左右至少一列是实体」；
    /// 这里在 Cut 之后、Collapse 之前逐格检查，检查不过就退回其它构造。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildPillarHolder(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "满高托柱的验证直接使用 ShapePhysics.Cut/Collapse：" +
            "前态是「目标列 + 西侧满高托柱 + 东侧辅助桥柱」，半破沿 n/2 刀口" +
            "丢弃东半边，目标列与托柱留在西半边。");

        limitations.Add(
            "该形态属于 n = 4 的 S（最低晶体以下至少两空）：目标列本身就是" +
            "碎晶后的形态，目标格停在目标层靠的是同层侧向粘连，不是下方支撑。" +
            "不能先把缺口填成紧列：临时晶体与保留晶体竖直同簇，" +
            "切割会把保留晶体一起打碎。");

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers && layer < target.LayerCount;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        int holderColumn = Wrap(column - 1, parts);
        int eColumn = Wrap(column + 1, parts);

        // 托柱：满高普通柱，覆盖 cap 的每一层（不是只覆盖到目标高度）。
        FillPillar(preState, holderColumn);

        // 桥柱：东侧辅助件，半破后整体丢弃。
        FillPillar(preState, eColumn);

        int p = half - 1;
        int rotation = Wrap(p - column, parts);

        var auxiliaryColumns = new[] { holderColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（目标列 + 满高托柱，带东侧辅助柱）",
                preState,
                AngleWorkStepKind.PreState)
        };

        /*
         * 满高托柱的前态里目标列就是目标本身，没有任何临时晶体或抬升。
         * 按 §7.1 的循环判据它不能算完成——否则「先做出目标、再切掉辅助柱」
         * 会平凡通过，`.TT.c` 这类形态就被兜底路径误报成可制作。
         */
        if (IsCyclicPreState(preState, target, column, hasLift: false))
        {
            return Incomplete(
                column,
                kind,
                CyclicPreStateReason,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱的前态不稳定（一次下落后会变形），无法继续验证。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        var rotated = ShapeOps.Rotate(preState, rotation);

        ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

        CutResult realCut = ShapePhysics.Cut(rotated);

        if (!realCut.Success ||
            realCut.West is null ||
            realCut.East is null)
        {
            return Incomplete(
                column,
                kind,
                "满高托柱的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱的结构性半破与 ShapePhysics.Cut 的结果对不上。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        // 碎晶后、下落前：目标列每一目标格必须仍在目标层，
        // 且同层左右至少一列是可粘连的实体（托柱与目标列同层相邻，
        // 这是目标格不下落的唯一依据）。
        if (!HasLateralSupport(
                rawBack,
                target,
                column,
                layers,
                out string? supportDifference))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱碎晶后的前态没有托住目标格：" + supportDifference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "满高托柱碎晶结果（未通过验证，仅供参考）",
                    rawBack,
                    AngleWorkStepKind.Note));
        }

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        if (!MatchesTargetColumn(
                collapsedBack,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "满高托柱下落结果与目标列不符：" + difference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "满高托柱结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，保留目标列与满高托柱，丢弃东侧辅助柱"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "保留目标列与满高托柱，丢弃东侧辅助柱",
            rawBack,
            AngleWorkStepKind.Cut));

        steps.Add(new AngleWorkStep(
            "下落：目标列每一格都由同层托柱侧向托住，不会下落",
            collapsedBack,
            AngleWorkStepKind.Collapse));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.FillBreak,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 碎晶后、下落前的侧向支撑检查：目标列的每一目标格必须仍在目标层，
    /// 且同层左右至少有一列是可粘连的实体（普通格或晶体；顶针不粘连）。
    /// </summary>
    private static bool HasLateralSupport(
        ShapeDocument afterCut,
        ShapeDocument target,
        int column,
        int layers,
        out string? difference)
    {
        int parts = afterCut.PartCount;

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell expected = layer < target.LayerCount
                ? target[layer, column]
                : ShapeCell.Empty();

            if (expected.Type == CellType.Empty)
            {
                continue;
            }

            ShapeCell got = afterCut[layer, column];

            if (!ShapeOps.SameCell(expected, got))
            {
                difference =
                    $"第 {layer + 1} 层期望 {Describe(expected)}，" +
                    $"实际 {Describe(got)}；碎晶后目标列 = " +
                    ShapeSignature.ColumnText(afterCut, column);

                return false;
            }

            ShapeCell left = afterCut[layer, Wrap(column - 1, parts)];
            ShapeCell right = afterCut[layer, Wrap(column + 1, parts)];

            bool leftAttaches =
                left.Type is CellType.Shape or CellType.Crystal;

            bool rightAttaches =
                right.Type is CellType.Shape or CellType.Crystal;

            if (!leftAttaches && !rightAttaches)
            {
                difference =
                    $"第 {layer + 1} 层的目标格碎晶后同层左右都没有实体" +
                    "（会失去侧向支撑）；碎晶后目标列 = " +
                    ShapeSignature.ColumnText(afterCut, column);

                return false;
            }
        }

        difference = null;

        return true;
    }

    #endregion

    #region 构造 G / F（切割路径）

    /// <summary>
    /// 构造 G（单层）或 F（n ≥ 6）的切割路径：前态 → Cut → Collapse → Target。
    /// </summary>
    private static AngleWorkOrder BuildCutPath(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int s,
        int d,
        AngleBuildKind kind,
        AngleConstruction construction)
    {
        var limitations = BaseLimitations(
            construction == AngleConstruction.G
                ? "构造 G 的验证直接使用 ShapePhysics 的现有语义：" +
                  "Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。"
                : "构造 F 的验证直接使用 ShapePhysics 的现有语义：" +
                  "Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。");

        ShapeDocument preState = BuildPreState(
            target,
            layers,
            column,
            s,
            d,
            construction,
            out int bColumn,
            out int eColumn,
            out int nColumn);

        var auxiliaryColumns = construction == AngleConstruction.G
            ? new[] { bColumn, eColumn }
            : new[] { nColumn, bColumn, eColumn };

        int p = half - 2;

        int rotation = Wrap(p - column, parts);

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（带辅助象限）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "前态不稳定（一次下落后会变形），后续碎晶/下落验证无法进行。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        int bRotated = Wrap(bColumn + rotation, parts);

        int brokenFrom = d;
        int brokenTo = construction == AngleConstruction.G
            ? d
            : s - 1;

        // === 切割：先做一次「不塌陷」的结构性半破，再用真实 Cut 核对 ===

        var rotated = ShapeOps.Rotate(preState, rotation);

        ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

        CutResult realCut = ShapePhysics.Cut(rotated);

        if (!realCut.Success ||
            realCut.West is null ||
            realCut.East is null)
        {
            return Incomplete(
                column,
                kind,
                "ShapePhysics.Cut 失败：" + (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        bool rawMatchesReal = ShapeOps.Same(rawCollapsed, realCut.West);

        if (!rawMatchesReal)
        {
            return Incomplete(
                column,
                kind,
                "结构性半破与 ShapePhysics.Cut 的结果对不上，不能保证 Cut 步是真实的。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        bool hadBridgeCrystals = true;

        for (int layer = brokenFrom;
             layer <= brokenTo;
             layer++)
        {
            if (rotated[layer, bRotated].Type != CellType.Crystal)
            {
                hadBridgeCrystals = false;
            }
        }

        bool bridgeCrystalsGone = true;

        for (int layer = brokenFrom;
             layer <= brokenTo;
             layer++)
        {
            if (rawWest[layer, bRotated].Type == CellType.Crystal)
            {
                bridgeCrystalsGone = false;
            }
        }

        bool targetCrystalsKept = true;

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            if (rotated[layer, p].Type == CellType.Crystal &&
                rawWest[layer, p].Type != CellType.Crystal)
            {
                targetCrystalsKept = false;
            }
        }

        if (!hadBridgeCrystals ||
            !bridgeCrystalsGone ||
            !targetCrystalsKept)
        {
            return Incomplete(
                column,
                kind,
                "切割没有按预期打碎桥柱晶体（或误伤了目标列晶体），" +
                "因此不保留 Cut 步。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，打碎桥柱晶体"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，打碎桥柱晶体",
            rawBack,
            AngleWorkStepKind.Cut));

        // === 下落 ===

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        if (!MatchesTargetColumn(
                collapsedBack,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "下落结果与目标列不符：" + difference,
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "下落结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            "下落：材料落位，缺口移到晶体塔正下方",
            collapsedBack,
            AngleWorkStepKind.Collapse));

        steps.Add(CrystallizeNote());

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = construction,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    #endregion

    #region 构造 H（一次顶针）

    /// <summary>
    /// 构造 H：四象限的一次顶针批量下落。
    ///
    /// <para>
    /// 环上依次是 <c>目标 A | 晶体桥柱 B | 满高纯晶体柱 E | 针槽柱 N</c>。
    /// 所有层号都按<b>顶针后</b>对齐：顶针把前态第 z 层抬到第 z+1 层。
    /// 目标列的源普通格写在顶针前的 s−1 层，顶针后位于 s，再沿被 E 连带打碎的
    /// B 晶体通道落到 d。
    /// </para>
    /// <para>
    /// 只有 n = 4 时四个辅助列刚好占满整圈；这里专门用于<b>多层缺口</b>
    /// （一次 Pin 完成整段下降）。若前提不满足或回放对不上，返回带
    /// Note 的未完成单，由调用方退回多轮 G。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildH(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int s,
        int d,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "构造 H 的验证直接使用 ShapePhysics.Pin：" +
            "顶针把整体上移一层，超限晶体整簇破碎后下落一次。");

        // === 构造 H 的前提 ===

        if (d < 1)
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的落点 d 必须至少是第 2 层（1 起）：顶针会新增第 1 层顶针，" +
                "普通格不能落到第 1 层。",
                limitations,
                construction: AngleConstruction.H);
        }

        if (target[d, column].Type != CellType.Shape)
        {
            return Incomplete(
                column,
                kind,
                "构造 H 只搬运普通格，该列落点不是普通格。",
                limitations,
                construction: AngleConstruction.H);
        }

        if (target[0, column].Type is not (CellType.Pin or CellType.Empty))
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的顶针会在第 1 层补顶针（若前态底层非空），" +
                "因此目标底层必须是顶针或空。",
                limitations,
                construction: AngleConstruction.H);
        }

        if (target[0, column].Type == CellType.Pin)
        {
            if (d == 1)
            {
                return Incomplete(
                    column,
                    kind,
                    "构造 H 的底层顶针与落点 d = 1 冲突（顶针会占住第 1 层）。",
                    limitations,
                    construction: AngleConstruction.H);
            }

            if (target[1, column].Type == CellType.Empty)
            {
                return Incomplete(
                    column,
                    kind,
                    "构造 H 要生成底层顶针，前态第 1 层必须非空，" +
                    "但它会被顶针抬到第 2 层；该列第 2 层为空。",
                    limitations,
                    construction: AngleConstruction.H);
            }
        }
        else if (d > 1 && target[1, column].Type != CellType.Empty)
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的底层没有顶针时，前态第 1 层必须为空，" +
                "该列第 2 层却非空，逆变换对不上。",
                limitations,
                construction: AngleConstruction.H);
        }

        // === 前态：目标列整体下移一层（源格放到 s−1），辅助列按顶针后层号对齐 ===

        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 1;
             layer < layers;
             layer++)
        {
            preState[layer - 1, column] = layer < target.LayerCount
                ? target[layer, column].Clone()
                : ShapeCell.Empty();
        }

        // 落点 d 在顶针前是 d−1 层；这里必须先空出来，源格才能落进去。
        preState[d - 1, column] = ShapeCell.Empty();

        // 源普通格：目标 d 层的材料，顶针前位于 s−1 层。
        preState[s - 1, column] = target[d, column].Clone();

        if (target[0, column].Type == CellType.Pin)
        {
            // 前态第 1 层非空 ⇒ 顶针补出目标第 1 层的顶针；
            // 同时这个格子被抬到第 2 层，正好是目标第 2 层。
            preState[0, column] = target[1, column].Clone();
        }

        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);
        int nColumn = Wrap(column - 1, parts);

        // B：第 z 层放晶体 ⇔ 顶针后的 z+1 ∈ I = [d, s−1]。
        FillPillar(preState, bColumn);

        for (int layer = d - 1;
             layer <= s - 2;
             layer++)
        {
            preState[layer, bColumn] = ShapeCell.Crystal(color);
        }

        // E：满高纯晶体柱；顶针后顶端超限，整根晶体柱碎掉。
        for (int layer = 0;
             layer < layers;
             layer++)
        {
            preState[layer, eColumn] = ShapeCell.Crystal(color);
        }

        // N：第 z 层放针 ⇔ 顶针后的 z+1 ∈ J = {s}；其他层普通格。
        FillPillar(preState, nColumn);
        preState[s - 1, nColumn] = ShapeCell.Pin();

        var auxiliaryColumns = new[] { nColumn, bColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（带辅助象限，按顶针后层号对齐）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "构造 H 的前态不稳定（一次下落后会变形），后续顶针验证无法进行。",
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns);
        }

        // === 一次顶针 ===

        PhysicsResult pinResult = ShapePhysics.Pin(preState);

        if (!pinResult.Success || pinResult.Document is null)
        {
            return Incomplete(
                column,
                kind,
                "ShapePhysics.Pin 失败：" + (pinResult.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns);
        }

        ShapeDocument pinned = pinResult.Document;

        if (!MatchesTargetColumn(
                pinned,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "一次顶针后的目标列与目标不符：" + difference,
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns,
                new AngleWorkStep(
                    "一次顶针结果（未通过验证，仅供参考）",
                    pinned,
                    AngleWorkStepKind.Note));
        }

        // 结构性核对：B 的晶体桥必须已被 E 的整柱破碎连带删除。
        int bRotated = bColumn;

        bool bridgeGone = true;

        for (int layer = d;
             layer <= s - 1;
             layer++)
        {
            if (pinned[layer, bRotated].Type == CellType.Crystal)
            {
                bridgeGone = false;
            }
        }

        if (!bridgeGone)
        {
            return Incomplete(
                column,
                kind,
                "顶针后晶体桥没有全部删除，构造 H 的前提不成立。",
                limitations,
                steps,
                AngleConstruction.H,
                0,
                auxiliaryColumns);
        }

        steps.Add(new AngleWorkStep(
            "顶针：一次顶针把整体抬一层，E 顶端超限碎裂并连带删掉 B 的晶体桥，" +
            "源普通格沿通道落到目标 d 层",
            pinned,
            AngleWorkStepKind.Pin));

        steps.Add(CrystallizeNote());

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.H,
            Rotation = 0,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    #endregion

    #region 多轮构造 G

    /// <summary>
    /// 多轮构造 G：缺口高 k 层就做 k 轮，每轮只把源普通格降一层。
    ///
    /// <para>
    /// 第 k 轮的源格在 <c>s−k</c> 层，空位 h 取它正下方（<c>s−k−1</c>）；
    /// 每轮都重新装 B（第 h 层晶体、第 t+1 层针）与 E（第 h 层晶体），
    /// 切割碎晶后源格整体下降一层，空位被搬到晶体塔正下方。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildMultiRoundG(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int t,
        int s,
        int d,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "多轮构造 G 的验证直接使用 ShapePhysics 的现有语义：" +
            "每轮 Cut 沿固定刀口 n/2 把跨刀口晶体整块打碎、两半各自下落一次。");

        char color = CrystalColor(target, column);

        int p = half - 2;
        int rotation = Wrap(p - column, parts);

        // 首轮前态：把 d 层的普通格搬到 s 层，缺口留在 d..s−1。
        var current = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            current[layer, column] = layer < target.LayerCount
                ? target[layer, column].Clone()
                : ShapeCell.Empty();
        }

        current[s, column] = target[d, column].Clone();
        current[d, column] = ShapeCell.Empty();

        var steps = new List<AngleWorkStep>();

        int rounds = s - d;

        for (int round = 0;
             round < rounds;
             round++)
        {
            int sourceLayer = s - round;
            int h = sourceLayer - 1;

            ShapeDocument preState = BuildRoundGPreState(
                current,
                layers,
                parts,
                column,
                t,
                h,
                color);

            int bColumn = Wrap(column + 1, parts);
            int eColumn = Wrap(column + 2, parts);

            var auxiliaryColumns = new[] { bColumn, eColumn };

            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? "碎晶前的角（带辅助象限）"
                    : $"第 {round + 1}/{rounds} 轮碎晶前的角（带辅助象限）",
                preState,
                AngleWorkStepKind.PreState));

            if (!ShapeOps.IsStable(preState))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮前态不稳定（一次下落后会变形），后续碎晶/下落验证无法进行。",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            var rotated = ShapeOps.Rotate(preState, rotation);

            ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

            CutResult realCut = ShapePhysics.Cut(rotated);

            if (!realCut.Success ||
                realCut.West is null ||
                realCut.East is null)
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮 ShapePhysics.Cut 失败：" +
                    (realCut.Error ?? "未知原因"),
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            ShapeDocument rawCollapsed =
                ShapePhysics.Collapse(rawWest).Document!;

            if (!ShapeOps.Same(rawCollapsed, realCut.West))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮结构性半破与 ShapePhysics.Cut 的结果对不上。",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            int bRotated = Wrap(bColumn + rotation, parts);

            bool bridgeGone =
                rawWest[h, bRotated].Type != CellType.Crystal;

            ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

            ShapeDocument collapsedBack =
                ShapeOps.Rotate(rawCollapsed, -rotation);

            // 本轮预期：源格从 sourceLayer 落到 h，其他格子不动。
            var expected = new ShapeDocument(layers, parts);

            for (int layer = 0;
                 layer < layers;
                 layer++)
            {
                expected[layer, column] = current[layer, column].Clone();
            }

            expected[h, column] = current[sourceLayer, column].Clone();
            expected[sourceLayer, column] = ShapeCell.Empty();

            if (!bridgeGone)
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮切割没有打碎辅助列的晶体桥。",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns);
            }

            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? (rotation == 0
                        ? "切割：沿 n/2 刀口半破，打碎桥柱晶体"
                        : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，打碎桥柱晶体")
                    : $"第 {round + 1}/{rounds} 轮切割：打碎桥柱晶体",
                rawBack,
                AngleWorkStepKind.Cut));

            if (!MatchesColumn(
                    collapsedBack,
                    expected,
                    column,
                    layers,
                    out string? difference))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮下落结果与预期不符：{difference}",
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns,
                    new AngleWorkStep(
                        $"第 {round + 1} 轮下落结果（未通过验证，仅供参考）",
                        collapsedBack,
                        AngleWorkStepKind.Note));
            }

            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? "下落：材料落位，缺口移到晶体塔正下方"
                    : $"第 {round + 1}/{rounds} 轮下落：源普通格下降一层",
                collapsedBack,
                AngleWorkStepKind.Collapse));

            current = new ShapeDocument(layers, parts);

            for (int layer = 0;
                 layer < layers;
                 layer++)
            {
                current[layer, column] = collapsedBack[layer, column].Clone();
            }
        }

        if (!MatchesTargetColumn(
                current,
                target,
                column,
                layers,
                out string? finalDifference))
        {
            return Incomplete(
                column,
                kind,
                "多轮 G 全部完成后目标列仍与目标不符：" + finalDifference,
                limitations,
                steps,
                AngleConstruction.G,
                rotation,
                new[] { Wrap(column + 1, parts), Wrap(column + 2, parts) },
                new AngleWorkStep(
                    "多轮 G 最终结果（未通过验证，仅供参考）",
                    current,
                    AngleWorkStepKind.Note));
        }

        steps.Add(CrystallizeNote());

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.G,
            Rotation = rotation,
            AuxiliaryColumns = new[]
            {
                Wrap(column + 1, parts),
                Wrap(column + 2, parts)
            },
            Limitations = limitations
        };
    }

    /// <summary>
    /// 多轮 G 每一轮的前态：当前目标列原样 + B/E 两列辅助，
    /// B 第 h 层晶体、第 t+1 层针，E 第 h 层晶体。
    /// </summary>
    private static ShapeDocument BuildRoundGPreState(
        ShapeDocument current,
        int layers,
        int parts,
        int column,
        int t,
        int h,
        char color)
    {
        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            preState[layer, column] = current[layer, column].Clone();
        }

        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);

        FillPillar(preState, bColumn);
        preState[h, bColumn] = ShapeCell.Crystal(color);
        preState[t + 1, bColumn] = ShapeCell.Pin();

        FillPillar(preState, eColumn);
        preState[h, eColumn] = ShapeCell.Crystal(color);

        return preState;
    }

    #endregion

    #region 填充 + 暴破（缺口不在塔下时的兜底）

    /// <summary>
    /// 缺口不在晶体塔正下方时的兜底构造：填充 + 暴破。
    ///
    /// <para>
    /// 前态直接使用目标列，但把塔下的空格<b>填充成晶体</b>；目标列西侧放一根
    /// 满高普通柱（holder）托住塔，东侧放一根只在这些填充层带晶体的桥柱 E。
    /// 半破沿 n/2 刀口把「填充晶体 + E 的晶体」一起打碎，缺口就由切割造出来；
    /// holder 留在西半边，保证塔不会随碎晶一起下落。
    /// </para>
    /// <para>
    /// 只填充<b>不与目标原有晶体上下相邻</b>的空格（safe 候选）；若 safe 为空，
    /// 再退而尝试全部空格。任何候选都必须用真实 Cut 回放并逐格核对目标列。
    /// </para>
    /// </summary>
    private static AngleWorkOrder BuildLegacyFillBreak(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int t,
        AngleBuildKind kind)
    {
        var limitations = BaseLimitations(
            "填充+暴破的验证直接使用 ShapePhysics 的现有语义：" +
            "结晶只把目标列塔下的缺口填成晶体；Cut 沿 n/2 刀口把跨刀口的填充晶体" +
            "与桥柱晶体整块打碎，两半各自下落一次。");

        var allEmpty = new List<int>();

        for (int layer = 0;
             layer < t;
             layer++)
        {
            if (target[layer, column].Type == CellType.Empty)
            {
                allEmpty.Add(layer);
            }
        }

        if (allEmpty.Count == 0)
        {
            return Incomplete(
                column,
                kind,
                "缺口不在晶体塔正下方，但塔下没有可填充的空位，无法用填充+暴破。",
                limitations,
                construction: AngleConstruction.FillBreak);
        }

        var safe = new List<int>();

        foreach (int layer in allEmpty)
        {
            bool belowCrystal =
                layer > 0 &&
                target[layer - 1, column].Type == CellType.Crystal;

            bool aboveCrystal =
                layer + 1 < target.LayerCount &&
                target[layer + 1, column].Type == CellType.Crystal;

            if (!belowCrystal && !aboveCrystal)
            {
                safe.Add(layer);
            }
        }

        var candidates = new List<List<int>>();

        if (safe.Count > 0)
        {
            candidates.Add(safe);
        }

        if (allEmpty.Count > 0 &&
            (safe.Count == 0 || !SameLayers(safe, allEmpty)))
        {
            candidates.Add(allEmpty);
        }

        AngleWorkOrder? bestFailure = null;

        foreach (List<int> fill in candidates)
        {
            AngleWorkOrder attempt = TryLegacyFillBreakCandidate(
                target,
                column,
                layers,
                parts,
                half,
                kind,
                fill,
                limitations);
            if (attempt.IsComplete)
            {
                return attempt;
            }

            if (bestFailure is null ||
                attempt.Steps.Count > bestFailure.Steps.Count)
            {
                bestFailure = attempt;
            }
        }

        return bestFailure ?? Incomplete(
            column,
            kind,
            "填充+暴破没有可用候选。",
            limitations,
            construction: AngleConstruction.FillBreak);
    }

    private static AngleWorkOrder TryLegacyFillBreakCandidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind,
        List<int> fill,
        List<string> limitations)
    {
        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers &&
             layer < target.LayerCount;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        foreach (int layer in fill)
        {
            preState[layer, column] = ShapeCell.Crystal(color);
        }

        int holderColumn = Wrap(column - 1, parts);
        int eColumn = Wrap(column + 1, parts);

        // holder：满高普通柱，留在西半边托住晶体塔。
        FillPillar(preState, holderColumn);

        // E：满高普通柱，只在填充层带晶体，用来跨刀口连带打碎填充晶体。
        FillPillar(preState, eColumn);

        foreach (int layer in fill)
        {
            preState[layer, eColumn] = ShapeCell.Crystal(color);
        }

        int p = half - 1;
        int rotation = Wrap(p - column, parts);

        var auxiliaryColumns = new[] { holderColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（塔下缺口已填充成晶体）",
                preState,
                AngleWorkStepKind.PreState)
        };

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的前态不稳定（一次下落后会变形），后续碎晶验证无法进行。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        var rotated = ShapeOps.Rotate(preState, rotation);

        ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

        CutResult realCut = ShapePhysics.Cut(rotated);

        if (!realCut.Success ||
            realCut.West is null ||
            realCut.East is null)
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        ShapeDocument rawCollapsed =
            ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破的结构性半破与 ShapePhysics.Cut 的结果对不上。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        int pRotated = Wrap(p, parts);

        ShapeDocument rawBack = ShapeOps.Rotate(rawWest, -rotation);

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        // 填充的晶体必须真的被切割打碎。
        bool fillGone = true;

        foreach (int layer in fill)
        {
            if (rawWest[layer, pRotated].Type == CellType.Crystal)
            {
                fillGone = false;
            }
        }

        if (!fillGone)
        {
            return Incomplete(
                column,
                kind,
                "切割没有把填充的晶体全部打碎。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割：沿 n/2 刀口半破，打碎填充晶体与桥柱晶体"
                : $"切割：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，打碎填充晶体与桥柱晶体",
            rawBack,
            AngleWorkStepKind.Cut));

        if (!MatchesTargetColumn(
                collapsedBack,
                target,
                column,
                layers,
                out string? difference))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破后的目标列与目标不符：" + difference,
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "填充+暴破结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        steps.Add(new AngleWorkStep(
            "下落：碎晶后材料落位，目标列的缺口由切割造出",
            collapsedBack,
            AngleWorkStepKind.Collapse));

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            Construction = AngleConstruction.FillBreak,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        };
    }

    #endregion

    #region 前态

    private static ShapeDocument BuildPreState(
        ShapeDocument target,
        int layers,
        int column,
        int s,
        int d,
        AngleConstruction construction,
        out int bColumn,
        out int eColumn,
        out int nColumn)
    {
        int parts = target.PartCount;

        char color = CrystalColor(target, column);

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < target.LayerCount && layer < layers;
             layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        // 逆变换：把 d 层的普通格搬到晶体塔正下方 s 层，d 层空出来。
        preState[s, column] = target[d, column].Clone();
        preState[d, column] = ShapeCell.Empty();

        bColumn = Wrap(column + 1, parts);
        eColumn = Wrap(column + 2, parts);
        nColumn = Wrap(column - 1, parts);

        if (construction == AngleConstruction.G)
        {
            FillPillar(preState, bColumn);
            preState[d, bColumn] = ShapeCell.Crystal(color);

            if (s + 2 < layers)
            {
                // G 的 B 列：第 h 层晶体、第 t+1 层针、其他层普通格。
                preState[s + 2, bColumn] = ShapeCell.Pin();
            }

            FillPillar(preState, eColumn);
            preState[d, eColumn] = ShapeCell.Crystal(color);
        }
        else
        {
            // F：N | A | B | E。
            FillPillar(preState, nColumn);
            preState[s, nColumn] = ShapeCell.Pin();

            FillPillar(preState, bColumn);

            for (int layer = d;
                 layer <= s - 1;
                 layer++)
            {
                preState[layer, bColumn] = ShapeCell.Crystal(color);
            }

            FillPillar(preState, eColumn);
            preState[d, eColumn] = ShapeCell.Crystal(color);
        }

        return preState;
    }

    private static void FillPillar(
        ShapeDocument document,
        int column)
    {
        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            document[layer, column] = ShapeCell.Shape('C', 'u');
        }
    }

    private static char CrystalColor(
        ShapeDocument target,
        int column)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        return highest >= 0
            ? target[highest, column].ColorCode ?? 'u'
            : 'u';
    }

    #endregion

    #region 结构性半破

    /// <summary>
    /// 只做「打碎跨刀口晶体 + 取西半边」的结构性半破，<b>不做下落</b>。
    ///
    /// 用于把 Cut 与 Collapse 拆成两步展示；结果会用
    /// <see cref="ShapePhysics.Cut"/> + <see cref="ShapePhysics.Collapse"/> 核对。
    /// </summary>
    private static ShapeDocument CutWestBeforeCollapse(
        ShapeDocument document,
        int half)
    {
        int layers = document.LayerCount;
        int parts = document.PartCount;

        var cells = new ShapeCell[layers, parts];

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                cells[layer, part] = document[layer, part].Clone();
            }
        }

        var visited = new bool[layers, parts];

        var component = new List<(int Layer, int Part)>();

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                if (visited[layer, part] ||
                    cells[layer, part].Type != CellType.Crystal)
                {
                    continue;
                }

                component.Clear();

                bool west = false;
                bool east = false;

                var queue = new Queue<(int Layer, int Part)>();

                queue.Enqueue((layer, part));
                visited[layer, part] = true;

                while (queue.Count > 0)
                {
                    (int currentLayer, int currentPart) = queue.Dequeue();

                    component.Add((currentLayer, currentPart));

                    if (currentPart < half)
                    {
                        west = true;
                    }
                    else
                    {
                        east = true;
                    }

                    int left = (currentPart - 1 + parts) % parts;
                    int right = (currentPart + 1) % parts;

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        left,
                        layers,
                        parts);

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer,
                        right,
                        layers,
                        parts);

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer - 1,
                        currentPart,
                        layers,
                        parts);

                    Enqueue(
                        cells,
                        visited,
                        queue,
                        currentLayer + 1,
                        currentPart,
                        layers,
                        parts);
                }

                if (west && east)
                {
                    foreach ((int layerIndex, int partIndex) in component)
                    {
                        cells[layerIndex, partIndex] = ShapeCell.Empty();
                    }
                }
            }
        }

        var result = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < half;
                 part++)
            {
                result[layer, part] = cells[layer, part];
            }
        }

        return result;
    }

    private static void Enqueue(
        ShapeCell[,] cells,
        bool[,] visited,
        Queue<(int Layer, int Part)> queue,
        int layer,
        int part,
        int layers,
        int parts)
    {
        if (layer < 0 ||
            layer >= layers ||
            part < 0 ||
            part >= parts ||
            visited[layer, part] ||
            cells[layer, part].Type != CellType.Crystal)
        {
            return;
        }

        visited[layer, part] = true;

        queue.Enqueue((layer, part));
    }

    #endregion

    #region 校验与返回

    /// <summary>
    /// 给工序单打上分档标记，并按「角自己的顶针步数」重算 rank。
    ///
    /// <para>
    /// <paramref name="attempt"/> 为 true 时表示这只是「尝试过的分档」，
    /// 不会覆盖 Construction 的失败语义；两种情况下 rank 都只数 Pin 步。
    /// </para>
    /// </summary>
    private static AngleWorkOrder Label(
        AngleWorkOrder order,
        AngleAlgorithm algorithm,
        bool attempt)
    {
        _ = attempt;

        int rank = 0;

        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind == AngleWorkStepKind.Pin)
            {
                rank++;
            }
        }

        return order with
        {
            Algorithm = algorithm,
            Rank = rank
        };
    }

    /// <summary>追加一条限制说明（不改变其它字段）。</summary>
    private static AngleWorkOrder AddLimitation(
        AngleWorkOrder order,
        string text)
    {
        var limitations = new List<string>(order.Limitations);

        if (!limitations.Contains(text))
        {
            limitations.Add(text);
        }

        return order with { Limitations = limitations };
    }

    /// <summary>
    /// 不合法角：五项条件不过 ⇒ 不可制作（§6 第一行）。
    /// 这是本生成器唯一输出 <see cref="NotProduciblePrefix"/> 的入口。
    /// </summary>
    private static AngleWorkOrder NotProducible(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        ColumnCheck check)
    {
        string reason = string.Join("；", check.Failures);

        var steps = new List<AngleWorkStep>
        {
            new(
                "不可制作：角不满足五项条件——" + reason,
                null,
                AngleWorkStepKind.Note)
        };

        var limitations = new List<string>
        {
            "五项条件（论文定理 1）：针有直接支撑、晶体以下的针只在底部连续针段中、" +
            "非最高晶体有上盖、晶体下的洞有普通资源、晶体之间的洞有两枚普通资源。",
            "这不是「未覆盖」：当前列本身不合法，任何算法都做不出来。"
        };

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            NotProduciblePrefix + reason)
        {
            IsComplete = false,
            Construction = AngleConstruction.None,
            Algorithm = AngleAlgorithm.None,
            Rank = 0,
            Limitations = limitations
        };
    }

    private static List<string> BaseLimitations(
        string verificationText)
    {
        return new List<string>
        {
            "前态只画目标列与辅助列，其余象限留空；它们不参与本订单的验证。",
            "碎晶角必须「先有晶体塔、后造缺口」：目的地的缺口在结晶之后才出现，" +
            "所以本工序不含末步结晶（全局结晶会把缺口填平）。",
            verificationText
        };
    }

    private static AngleWorkStep CrystallizeNote()
    {
        return new AngleWorkStep(
            "补晶体：本角不需要末步结晶——Crystallize 是全局操作，" +
            "会把刚做出来的缺口重新填平；前态里的晶体塔应由构造 A 的逐层结晶预先做出。",
            null,
            AngleWorkStepKind.Note);
    }

    private static AngleWorkOrder AppendFailure(
        AngleWorkOrder order,
        string extra)
    {
        var steps = new List<AngleWorkStep>(order.Steps);

        steps.Add(new AngleWorkStep(
            "未覆盖（尚未实现）：" + extra,
            null,
            AngleWorkStepKind.Note));

        var limitations = new List<string>(order.Limitations);

        if (!limitations.Contains(extra))
        {
            limitations.Add(extra);
        }

        /*
         * Note 始终只有一个「未覆盖：」前缀：
         * 既有的失败说明可能已经带前缀，这里先剥掉再统一加一次。
         */
        string previous = order.Note is null
            ? string.Empty
            : StripUncoveredPrefix(order.Note);

        string body = previous.Length == 0
            ? extra
            : previous + "；" + extra;

        string note = UncoveredPrefix + body;

        return new AngleWorkOrder(
            order.Column,
            order.Kind,
            steps,
            note)
        {
            IsComplete = false,
            Construction = order.Construction,
            Rotation = order.Rotation,
            AuxiliaryColumns = order.AuxiliaryColumns,
            Algorithm = order.Algorithm,
            Rank = order.Rank,
            SubRecipe = order.SubRecipe,
            Limitations = limitations
        };
    }

    private static string StripUncoveredPrefix(
        string note)
    {
        return note.StartsWith(UncoveredPrefix, StringComparison.Ordinal)
            ? note[UncoveredPrefix.Length..]
            : note;
    }

    private static bool SameLayers(
        List<int> left,
        List<int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0;
             index < left.Count;
             index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTargetColumn(
        ShapeDocument actual,
        ShapeDocument target,
        int column,
        int layers,
        out string? difference)
    {
        for (int layer = 0;
             layer < layers;
             layer++)
        {
            ShapeCell expected = layer < target.LayerCount
                ? target[layer, column]
                : ShapeCell.Empty();

            ShapeCell got = actual[layer, column];

            if (!ShapeOps.SameCell(expected, got))
            {
                difference =
                    $"第 {layer + 1} 层期望 {Describe(expected)}，实际 {Describe(got)}；" +
                    $"实际列 = {ShapeSignature.ColumnText(actual, column)}";

                return false;
            }
        }

        difference = null;

        return true;
    }

    private static bool MatchesColumn(
        ShapeDocument actual,
        ShapeDocument expected,
        int column,
        int layers,
        out string? difference)
    {
        for (int layer = 0;
             layer < layers;
             layer++)
        {
            ShapeCell want = expected[layer, column];
            ShapeCell got = actual[layer, column];

            if (!ShapeOps.SameCell(want, got))
            {
                difference =
                    $"第 {layer + 1} 层期望 {Describe(want)}，实际 {Describe(got)}；" +
                    $"实际列 = {ShapeSignature.ColumnText(actual, column)}";

                return false;
            }
        }

        difference = null;

        return true;
    }

    private static string Describe(
        ShapeCell cell)
    {
        return cell.Type switch
        {
            CellType.Empty => "空",
            CellType.Pin => "顶针",
            CellType.Crystal => $"晶体({cell.ColorCode})",
            _ => $"普通格({cell.ShapeCode},{cell.ColorCode})"
        };
    }

    private static AngleWorkOrder Incomplete(
        int column,
        AngleBuildKind kind,
        string reason,
        List<string>? limitations = null,
        List<AngleWorkStep>? steps = null,
        AngleConstruction construction = AngleConstruction.None,
        int rotation = 0,
        IReadOnlyList<int>? auxiliaryColumns = null,
        AngleWorkStep? diagnostic = null)
    {
        var resultSteps = steps is null
            ? new List<AngleWorkStep>()
            : new List<AngleWorkStep>(steps);

        if (diagnostic is not null)
        {
            resultSteps.Add(diagnostic);
        }

        resultSteps.Add(new AngleWorkStep(
            "未覆盖（尚未实现）：" + reason,
            null,
            AngleWorkStepKind.Note));

        var resultLimitations = limitations is null
            ? new List<string>()
            : new List<string>(limitations);

        if (!resultLimitations.Contains(reason))
        {
            resultLimitations.Add(reason);
        }

        if (!resultLimitations.Contains(UncoveredDisclaimer))
        {
            resultLimitations.Add(UncoveredDisclaimer);
        }

        return new AngleWorkOrder(
            column,
            kind,
            resultSteps,
            UncoveredPrefix + reason)
        {
            IsComplete = false,
            Construction = construction,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns ?? Array.Empty<int>(),
            Limitations = resultLimitations
        };
    }

    private static int Wrap(
        int value,
        int size)
    {
        return ((value % size) + size) % size;
    }

    #endregion
}

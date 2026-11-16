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

    /// <summary>
    /// 下落（<b>已废弃</b>）：保留枚举值只为兼容 UI。
    /// 生成的步骤链不再输出这个 Kind——切割后的一次下落并进
    /// <see cref="Cut"/> 步的结果，纯下落（半边候选 2）并进 <see cref="Target"/> 步。
    /// </summary>
    Collapse,

    /// <summary>顶针：一次顶针完成批量下落（构造 H）。</summary>
    Pin,

    /// <summary>
    /// 轮间交换（§15.3 / 构造 C 支柱交接）：多刀半破在相邻两刀之间，
    /// 把桥梁柱的针层与保险丝柱的晶体层换到下一块的锚层。
    /// </summary>
    Exchange,

    /// <summary>构造 C：支柱交接——装新支撑柱、拆旧柱与辅助列。</summary>
    Support,

    /// <summary>构造 C：逐层追加无晶体尾部（堆叠一层）。</summary>
    Stack,

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

    /// <summary>
    /// 构造 D：定向碎晶——只沿刀口删掉桥柱/填充的晶体段，保留目标列与托柱。
    /// 与 <see cref="FillBreak"/> 的区别是<b>不把缺口填满再炸</b>：缺口所在的
    /// 目标格先被抬升一层（§7.5 规则二），填充晶体才与目标晶体脱开、可以单独炸。
    /// </summary>
    D,

    /// <summary>填充+暴破：把塔下缺口填成晶体，再用切割打碎这一段晶体。</summary>
    FillBreak,

    /// <summary>
    /// 半边工序：n = 4、五项 ✔ 但 S ✘ 的合法角，与相邻象限一起做。
    /// 半边 = 连续 n/2 列；候选见 <see cref="HalfCandidate"/>，
    /// 两列的逐格回放与目标定义见 <see cref="HalfWorkOrder"/>。
    /// </summary>
    Half
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
/// <see cref="Note"/> 为 null 表示工序<b>已逐步验证通过</b>；非 null 时有两种情况：
/// </para>
/// <list type="bullet">
/// <item><see cref="AngleWorkOrderBuilder.NotProduciblePrefix"/>：五项条件不过，
/// 角本身不合法 ⇒ <b>不可制作</b>；</item>
/// <item><see cref="AngleWorkOrderBuilder.UncoveredPrefix"/>：角合法，但生成器
/// 还没覆盖这个形态 ⇒ <b>未覆盖</b>（不代表不可制作）；</item>
/// <item><see cref="AngleWorkOrderBuilder.NonPaperConstructionPrefix"/>：
/// 工序已回放通过（<see cref="IsComplete"/> 为 true），但它用的是论文之外的
/// 兜底构造（目前只有填充+暴破）——这是<b>标注</b>，不是失败。</item>
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

    /// <summary>
    /// Q1 子配方（docs §19.4 / §5.1）：顶针路线（构造 H）的工序单里，
    /// 目标列 A 若带洞，必须挂「从紧列开始、一层展开」的子配方：
    /// <code>
    /// 紧列（洞填临时晶体）+ 托柱 + 保险丝
    ///   → 一刀半破（真实 ShapePhysics.Cut）
    ///   → 零件 A（Q1）
    /// </code>
    /// 用 <see cref="RecipeKind.Cut"/> 表达，<see cref="Replay"/>.Verify 直接用真实 Cut
    /// 重算。<see cref="SubRecipe"/> 仍然是整份前态的交换链装配；两者互补：
    /// 前者证明整份前态装得出来，后者说明 Q1 这一列本身怎么造。
    /// </summary>
    public Recipe? Q1SubRecipe { get; init; }

    /// <summary>
    /// 半边工序（docs/half-side-work-order.md）：n = 4、五项 ✔ 但 S ✘ 的合法角
    /// 不能单角做，必须与相邻象限一起做。
    ///
    /// <para>
    /// 非 null 表示这份角工序走的是「两列一起做」的半边路径：
    /// <see cref="Steps"/> 是目标角的单列视角（前态 → 一刀 → 目标），
    /// <see cref="HalfWorkOrder.Steps"/> 才是两列的完整核对（两列逐格等于目标半边）。
    /// </para>
    /// <para>
    /// 候选 1（<see cref="HalfCandidate.Algo8Self"/>）、候选 2
    /// （<see cref="HalfCandidate.Algo8Uplift"/>）、候选 3
    /// （<see cref="HalfCandidate.TempCrystalFuse"/>）按论文顺序尝试；候选 1 / 2 的
    /// 两列都是带子配方的 S 列（<see cref="HalfWorkOrder.TargetColumnsHaveSubRecipe"/>），
    /// 候选 3 的环伙伴 / 保险丝仍是要求紧列的辅助列。回放没通过时
    /// <see cref="IsComplete"/> 仍为 false，<see cref="Note"/> 以「未覆盖：」开头。
    /// </para>
    /// </summary>
    public HalfWorkOrder? Half { get; init; }

    /// <summary>
    /// 验收条件七（docs §6 第 7 条 / §17.7）：<b>前态目标列（带洞时）是否附带子配方</b>。
    ///
    /// <para>
    /// 辅助列一律按紧列生成，有洞就整份候选作废；目标列允许有洞，
    /// 但它必须能由「从紧列开始的子配方」得到（半破碎晶 / 顶针得到的零件，
    /// 见 §5.1「子配方至多一层」）。
    /// </para>
    /// <para>
    /// 生成器在装出前态时会把目标列本身（含洞）作为已验构造的产物，
    /// 因此所有成功工序都置 <c>true</c>；外部伪造 / 尚未给出子配方的候选
    /// 默认 <c>false</c>，<see cref="HasIllegalPreState"/> 会把它判成非法。
    /// <see cref="SubRecipe"/> 非空时同样算「有子配方」。
    /// </para>
    /// </summary>
    public bool TargetColumnHasSubRecipe { get; init; }

    /// <summary>
    /// 验收条件一（docs §6 第 1 条 / TODO.md §7.7）：
    /// <b>前态的每一列都必须通过五项条件</b>。
    ///
    /// <para>
    /// 前态若有非法列，就说明这个「起点」根本做不出来，
    /// 后续的 Cut / Pin 回放即使逐格对上也毫无意义
    /// （<c>.T.T.c</c> 的旧碎晶前态 <c>cTcT.c</c> 就是这样漏过去的）。
    /// </para>
    /// </summary>
    /// <param name="preStateIndex">第几条前态步（0 起）；不存在非法列时为 -1。</param>
    /// <param name="column">首个非法列（象限索引）；不存在非法列时为 -1。</param>
    /// <returns>存在非法列时返回 true。</returns>
    public bool HasIllegalPreState(
        out int preStateIndex,
        out int column)
    {
        int index = 0;

        bool targetColumnHasSubRecipe =
            TargetColumnHasSubRecipe || SubRecipe is not null;

        /*
         * 半边候选 1 / 2 的前态是「两根 S 列」：两列都能按单角工序单独做出来，
         * 所以两列都算「带子配方」，允许带洞（docs §6 第 7 条）。
         * 候选 3 的环伙伴 / 保险丝是真正的辅助列，不在这个豁免里。
         */
        var subRecipeColumns = new List<int>();

        if (targetColumnHasSubRecipe)
        {
            subRecipeColumns.Add(Column);
        }

        if (Half is { TargetColumnsHaveSubRecipe: true })
        {
            subRecipeColumns.AddRange(Half.Target.Columns);
        }

        foreach (AngleWorkStep step in Steps)
        {
            if (step.Kind != AngleWorkStepKind.PreState ||
                step.Shape is null)
            {
                continue;
            }

            if (!ShapeSignature.AllColumnsLegal(step.Shape, out column))
            {
                preStateIndex = index;

                return true;
            }

            /*
             * 验收条件七（docs §6 第 7 条 / §17.7）：
             * 辅助列有洞 ⇒ 拒绝；带子配方的目标列（含半边的两根 S 列）允许有洞。
             */
            if (AngleWorkOrderBuilder.HasIllegalColumnHoles(
                    step.Shape,
                    subRecipeColumns,
                    out column))
            {
                preStateIndex = index;

                return true;
            }

            index++;
        }

        preStateIndex = -1;
        column = -1;

        return false;
    }
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
public static partial class AngleWorkOrderBuilder
{
    /// <summary>未覆盖说明的统一前缀。</summary>
    public const string UncoveredPrefix = "未覆盖：";

    /// <summary>
    /// 不可制作说明的统一前缀。<b>只用于存在不合法角</b>（五项条件不过），
    /// 任何「还没实现」的情形都不允许写成不可制作。
    /// </summary>
    public const string NotProduciblePrefix = "不可制作：";

    /// <summary>
    /// 已完成、但用的是论文之外的兜底构造时的标注前缀（T7 / docs §5.2）。
    /// 带此前缀的工序 <see cref="AngleWorkOrder.IsComplete"/> 仍为 true——
    /// 标注不是失败，只是如实说明它不属于论文构造 A–H。
    /// </summary>
    public const string NonPaperConstructionPrefix = "非论文构造：";

    /// <summary>
    /// Q1 子配方缺失时的统一说明（docs §19.4 第 3 条）：
    /// <b>不伪造</b>一层紧列子配方——保持现状并把原因写进
    /// <see cref="AngleWorkOrder.Limitations"/>。
    /// </summary>
    public const string Q1SubRecipeMissingText =
        "Q1 子配方未找到（一层紧列不可行）：前态 A 的洞无法用「紧列 + 半破」做出来，" +
        "保持如实标注，不伪造子配方。";

    /// <summary>
    /// 未覆盖的含义说明：避免 UI 把它误读成「不可制作」。
    /// 唯一能否定的判据是「存在不合法角」，那由分析层单独给出。
    /// </summary>
    public const string UncoveredDisclaimer =
        "未覆盖只说明角工序生成器还没覆盖这个形态，不代表不可制作。";

    /// <summary>
    /// 前态非法时的统一失败原因（docs §6 第 1 条 / TODO.md §7.7）。
    /// 候选前态的任意一列不过五项条件 ⇒ 该候选作废，最终 Note 以
    /// <see cref="UncoveredPrefix"/> 开头并写明本原因。
    /// </summary>
    public const string IllegalPreStateReason = "前态存在非法列";

    /// <summary>
    /// 临时验证开关（docs §11 / §15 的清理项）：<c>true</c> 时
    /// <see cref="TryBuildConstructedH"/> 只走前两段——§20.3 构造式与 §13.6 七步，
    /// 跳过「带下降通道扫描」与「下移一格扫描」两段。
    ///
    /// <para>
    /// 用途：确认后两段是否还有存在价值。实测（2026-11-16，§13.6 七步落地后）：
    /// 开关打开时 3–6 层失败 0、7 层 1、8 层 10、9 层 61（共 72 例，见
    /// <c>docs/angle-work-order-uncovered-h.md</c>），所以后两段仍保留；
    /// 等这些形态归零后再连同本开关一起删除。环境变量
    /// <c>SHAPE_CONSTRUCT_H_STRICT_TWO_STAGE=1</c> 也可以打开它，
    /// 方便直接跑既有测试而不改代码。
    /// </para>
    /// </summary>
    public static bool ConstructHStrictTwoStage { get; set; } =
        string.Equals(
            Environment.GetEnvironmentVariable(
                "SHAPE_CONSTRUCT_H_STRICT_TWO_STAGE"),
            "1",
            StringComparison.Ordinal);

    /// <summary>
    /// 验收条件七（docs §6 第 7 条 / §17.7）：逐个象限检查前态列的「洞」。
    ///
    /// <code>
    /// foreach 列 c:
    ///     若该列有空洞:
    ///         若 c 是辅助列（不是目标列）        → 拒绝
    ///         否则若该列没有附带子配方            → 拒绝
    /// </code>
    ///
    /// <para>
    /// 「有空洞」按 <see cref="ShapeSignature.IsTightColumn"/> 判定：
    /// 材料之间夹着空层即算洞；完全为空的象限与末尾空层都不算洞。
    /// </para>
    /// <para>
    /// <b>辅助列与目标列的区别</b>：<paramref name="targetColumn"/> 是被求解的那个角
    /// 所在象限，其余非空象限都是辅助列。辅助列必须是紧列（论文构造 A/B/D/G/H 的明文：
    /// 连续接地、满高、没有空层）；目标列允许带洞，但必须由子配方证明可造
    /// （<paramref name="targetColumnHasSubRecipe"/>）。
    /// </para>
    /// </summary>
    /// <param name="preState">候选前态。</param>
    /// <param name="targetColumn">目标列（被求解的角）所在象限。</param>
    /// <param name="targetColumnHasSubRecipe">目标列带洞时是否附带「从紧列开始」的子配方。</param>
    /// <param name="badColumn">首个违反第 7 条的象限；通过时为 -1。</param>
    /// <returns>违反验收条件七时返回 true。</returns>
    public static bool HasIllegalColumnHoles(
        ShapeDocument preState,
        int targetColumn,
        bool targetColumnHasSubRecipe,
        out int badColumn)
    {
        return HasIllegalColumnHoles(
            preState,
            targetColumnHasSubRecipe
                ? new[] { targetColumn }
                : Array.Empty<int>(),
            out badColumn);
    }

    /// <summary>
    /// 验收条件七的通用形式：<paramref name="columnsWithSubRecipe"/> 里的列
    /// 视为「带子配方的目标列」（允许带洞），其余非空列一律要求紧列。
    ///
    /// <para>
    /// 半边候选 1 / 2 的两根列都是 S 列（各自有单角子配方），所以两列都在
    /// 豁免集合里；候选 3 只豁免 Q1，环伙伴 / 保险丝仍按辅助列要求紧列。
    /// </para>
    /// </summary>
    /// <param name="preState">候选前态。</param>
    /// <param name="columnsWithSubRecipe">允许带洞的列（带子配方的目标列）。</param>
    /// <param name="badColumn">首个违反第 7 条的象限；通过时为 -1。</param>
    /// <returns>违反验收条件七时返回 true。</returns>
    public static bool HasIllegalColumnHoles(
        ShapeDocument preState,
        IReadOnlyCollection<int> columnsWithSubRecipe,
        out int badColumn)
    {
        for (int part = 0; part < preState.PartCount; part++)
        {
            // 完全为空、或材料连续接地的列都合法。
            if (ShapeSignature.IsTightColumn(preState, part))
            {
                continue;
            }

            bool hasSubRecipe = false;

            foreach (int candidate in columnsWithSubRecipe)
            {
                if (candidate == part)
                {
                    hasSubRecipe = true;
                    break;
                }
            }

            /*
             * 非紧列只有在「带子配方的目标列」集合里才允许；
             * 其余任何非空列都是辅助列，论文要求一律紧列。
             */
            if (!hasSubRecipe)
            {
                badColumn = part;
                return true;
            }
        }

        badColumn = -1;
        return false;
    }

    /// <summary>
    /// 检查一份候选前态：任意一列不过五项条件、或违反 §6 第 7 条的洞规则，
    /// 就应当作废。
    ///
    /// <para>
    /// 目标列本身不合法时（只有 <c>enforceFiveConditions: false</c> 的合成语料
    /// 会走到这里，用来单独验证 Cut/Pin/G 的回放语义），不在这一层拦截：
    /// 那种语料的目标列都做不出来，要求它的前态合法没有意义。
    /// </para>
    /// <para>
    /// <paramref name="targetColumnHasSubRecipe"/> 由候选生成器给出：凡是允许
    /// 目标列带洞的构造（算法 5/6 的塔、构造 D/H 的反变换、构造 C 的零件），
    /// 都会把目标列作为已验子构造的产物 ⇒ 传 <c>true</c>；
    /// 没有子配方的候选传 <c>false</c>，会被这里的闸门拒掉。
    /// </para>
    /// </summary>
    private static bool IsRejectedPreState(
        ShapeDocument target,
        int column,
        ShapeDocument preState,
        out int badColumn,
        bool targetColumnHasSubRecipe = true)
    {
        badColumn = -1;

        if (!ShapeSignature.IsLegalColumn(target, column))
        {
            return false;
        }

        if (!ShapeSignature.AllColumnsLegal(preState, out badColumn))
        {
            return true;
        }

        return HasIllegalColumnHoles(
            preState,
            column,
            targetColumnHasSubRecipe,
            out badColumn);
    }

    /// <summary>把「前态存在非法列」写成带列位置的失败原因。</summary>
    private static string DescribeIllegalPreState(
        ShapeDocument preState,
        int badColumn)
    {
        string text = ShapeSignature.ColumnText(preState, badColumn);

        return ShapeSignature.CheckColumn(text).IsLegal
            ? $"{IllegalPreStateReason}（第 {badColumn + 1} 象限的前态列 {text} " +
              "违反 §6 第 7 条：辅助列必须是紧列，目标列有洞必须带子配方）"
            : $"{IllegalPreStateReason}（第 {badColumn + 1} 象限的前态列 {text} " +
              "不过五项条件）";
    }

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
         * 三、n = 4 的判定收尾（docs §17.3 / §6 第 6 条）：
         * S 判据（五项 ∧ (D∨F)）不成立 ⇒ 这个角不可制作。
         * 不能再落到「未覆盖」（那是「还没实现」的语义）。
         *
         * 例外（docs/half-side-work-order.md §4.1）：
         * 五项 ✔ 但 S ✘ 的合法角不能单角做，却可能「两列一起做」——
         * 先试半边工序的候选 3（临时晶体 + 保险丝 + 一刀）。
         * 候选命中并回放通过 ⇒ 返回角工序 + Half 字段；
         * 候选不适用或回放没通过 ⇒ 仍按原语义报 NotProducible / 未覆盖。
         */
        if (parts == 4 &&
            enforceFiveConditions &&
            !PillarSCriterion.IsPillarColumn(target, column))
        {
            AngleWorkOrder? halfOrder = TryBuildHalfOrder(
                target,
                column,
                cap,
                kind);

            if (halfOrder is not null)
            {
                if (halfOrder.IsComplete)
                {
                    return halfOrder;
                }

                /*
                 * 三个候选（1 → 2 → 3）全部没通过：按本条要求统一报「不可制作」，
                 * 不再把「候选形态命中但回放失败」写成「未覆盖」。
                 * 候选 3 的最后一条诊断只保留在 Limitations 里。
                 */
                return NotProducibleBySCriterion(
                    target,
                    column,
                    kind,
                    halfOrder.Note);
            }

            return NotProducibleBySCriterion(target, column, kind);
        }

        /*
         * 三·补、核心 / 尾部拆分（docs §17.5 第 1 条）：
         * 最高晶体以上的 T/P 尾部不参与碎晶，先按「核心」走现有流程，
         * 最后用构造 C 逐层接回尾部。
         */
        if (parts == 4)
        {
            AngleWorkOrder? tailOrder = TryBuildTailOrder(
                target,
                column,
                cap,
                kind,
                enforceFiveConditions);

            if (tailOrder is not null)
            {
                return ApplyIsolatedColumnRule(
                    tailOrder,
                    target,
                    column);
            }
        }

        /*
         * 三·补二、多刀半破（§15）：目标列有一个以上「紧贴晶体下方的缺口」时，
         * 每个缺口一枚临时晶体块，K 非空就继续切，刀数 == |K|。
         * 它比构造 D 的单刀 / 两刀更一般，所以在分档之前先试；
         * 连续缺口、底针、以及缺口下方不是普通格的形态不在此列。
         */
        if (parts == 4)
        {
            AngleWorkOrder? multiCut = TryBuildMultiCutHalfBreak(
                target,
                column,
                layers,
                parts,
                kind);

            if (multiCut is not null &&
                multiCut.IsComplete &&
                !HasCyclicPreState(multiCut, target) &&
                !multiCut.HasIllegalPreState(out _, out _))
            {
                return ApplyIsolatedColumnRule(
                    Label(multiCut, AngleAlgorithm.Algorithm4, attempt: false),
                    target,
                    column);
            }
        }

        /*
         * 四、分档（§6）：先按论文选分支，不要先试「填充+暴破」。
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

        string? tierRejectionReason = null;

        if (tierOrder is not null && tierOrder.IsComplete)
        {
            /*
             * 最后一道循环闸门：任何分档构造若拿「目标列本身」当前态，
             * 验收等式就平凡成立。这里统一再挡一次，避免某个新构造
             * 绕过各自的候选检查。
             */
            if (HasCyclicPreState(tierOrder, target))
            {
                tierRejectionReason = CyclicPreStateReason;
                tierOrder = null;
            }
            else if (ShapeSignature.IsLegalColumn(target, column) &&
                     tierOrder.HasIllegalPreState(
                         out int badPreStateIndex,
                         out int badPreStateColumn))
            {
                /*
                 * 验收条件一 / 七（§6 第 1、7 条）：前态每一列都要过五项条件，
                 * 辅助列还必须是紧列。前态里有非法列 ⇒ 这份候选作废，
                 * 即使 Cut/Pin 回放平凡通过。
                 */
                tierRejectionReason =
                    $"{IllegalPreStateReason}（第 {badPreStateIndex + 1} 条前态的" +
                    $"第 {badPreStateColumn + 1} 象限不合法：五项条件或 §6 第 7 条）";
                tierOrder = null;
            }
            else
            {
                /*
                 * 档内链（算法 1 → 2 → 3）会自己标出真正成功的那一档，
                 * 这里只给没打标的实现补上分档标记。
                 */
                return ApplyIsolatedColumnRule(
                    tierOrder.Algorithm == AngleAlgorithm.None
                        ? Label(tierOrder, algorithm, attempt: false)
                        : tierOrder,
                    target,
                    column);
            }
        }

        /*
         * 三·补、路线 F（n = 4、底格非针、D 不成立、F 成立）：
         * 先试【构造 G】——§15 的四象限单孔环下落（环伙伴 B + 保险丝柱 B′ + 一刀），
         * 它能把「长段（≥3）恢复」锁成恰好一层；
         * 再试 §11 的通用实现：规范计划 θ（lift / fill / support / bridge / cuts）
         * 全部由扫描得出、<b>只生成一次</b>，切割排程只有「一刀」与「逐块从上到下」
         * 两种固定形态；其恢复手段是【构造 D 的碎晶等价路线】。
         * 只有这两条都没通过回放时，才退回下面的兜底链（§2 第 2 条约定）。
         */
        if (algorithm == AngleAlgorithm.Algorithm4)
        {
            AngleWorkOrder? constructG = TryBuildConstructGOrder(
                target,
                column,
                cap,
                kind);

            if (constructG is not null &&
                constructG.IsComplete &&
                !HasCyclicPreState(constructG, target) &&
                !constructG.HasIllegalPreState(out _, out _))
            {
                return ApplyIsolatedColumnRule(constructG, target, column);
            }

            AngleWorkOrder? generic = TryBuildGenericFourQuadrantOrder(
                target,
                column,
                cap,
                kind);

            if (generic is not null &&
                generic.IsComplete &&
                !HasCyclicPreState(generic, target) &&
                !generic.HasIllegalPreState(out _, out _))
            {
                return ApplyIsolatedColumnRule(generic, target, column);
            }
        }

        /*
         * 四、算法 4 的「递归提升 + 构造 G 环下落」直接由兜底链承载
         * （填充+暴破的抬升形态 + 单轮/多轮构造 G），所以这里不再重复跑一遍。
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

        if (legacy.IsComplete &&
            ShapeSignature.IsLegalColumn(target, column) &&
            legacy.HasIllegalPreState(
                out int legacyPreStateIndex,
                out int legacyPreStateColumn))
        {
            legacy = AppendFailure(
                legacy,
                $"{IllegalPreStateReason}（第 {legacyPreStateIndex + 1} 条前态的" +
                $"第 {legacyPreStateColumn + 1} 象限不合法：五项条件或 §6 第 7 条）");
        }

        if (algorithm == AngleAlgorithm.Algorithm4)
        {
            return ApplyIsolatedColumnRule(
                Label(legacy, AngleAlgorithm.Algorithm4, attempt: false),
                target,
                column);
        }

        string tierReason = tierRejectionReason ?? (tierOrder?.Note is null
            ? "该形态没有找到回放验证通过的列级步骤"
            : StripUncoveredPrefix(tierOrder.Note));

        if (legacy.IsComplete)
        {
            /*
             * 分档构造没覆盖、兜底链成功：如实标成 Fallback，
             * 并在限制说明里写清「实际用的是兜底构造」。
             */
            return ApplyIsolatedColumnRule(
                AddLimitation(
                    Label(legacy, AngleAlgorithm.Fallback, attempt: false),
                    $"论文分档（{DescribeAlgorithm(algorithm)}）在该形态上未通过回放验证，" +
                    $"改用兜底构造（{legacy.Construction}）：{tierReason}"),
                target,
                column);
        }

        /*
         * 五、分档与兜底都没通过：保留已验证前缀，并区分「未覆盖」与「不可制作」。
         */
        return AppendFailure(
            Label(legacy, algorithm, attempt: true),
            $"论文分档（{DescribeAlgorithm(algorithm)}）未覆盖：{tierReason}");
    }

    /// <summary>
    /// §14.4 的产物规则：角工序做完后，若目标列满足<b>定理 4（孤立列判据）</b>，
    /// 就可以把托柱拆掉、降到孤立列（更省材料）；不满足则停在 S 形态，
    /// 并在 <see cref="AngleWorkOrder.Limitations"/> 里写明原因。
    ///
    /// <para>
    /// <b>实现方式</b>：只在「施工里真的出现过托柱」（有 <see cref="AngleWorkStepKind.Support"/>
    /// 步或非空辅助列）的工序单上追加一步「构造 C：拆掉托柱」，并用真实
    /// <see cref="ShapeOps.IsStable"/> 检验孤立列自己能站住；检验不过就保留托柱。
    /// Simple / 无晶体档本来就没有托柱（工序单只有逐层计划），只补一条「该列是孤立列」的说明。
    /// </para>
    /// <para>
    /// <b>实测（≤6 层 2125 个 S 列）</b>：其中 1054 列本身满足定理 4（例如 <c>cT.</c>——
    /// 紧列但带晶体，仍要碎晶/顶针才能做出来）；有托柱的 604 例都真的追加了「拆托柱」步，
    /// 并逐例用真实 <see cref="ShapeOps.IsStable"/> 验证。剩下的 S 列带洞（构造 G / D 的典型），
    /// 定理 4 不成立，保留托柱并写明原因。Simple / 无晶体档本来就没有托柱。
    /// </para>
    /// </summary>
    private static AngleWorkOrder ApplyIsolatedColumnRule(
        AngleWorkOrder order,
        ShapeDocument target,
        int column)
    {
        if (!order.IsComplete || order.Half is not null)
        {
            return order;
        }

        var limitations = new List<string>(order.Limitations);

        bool hasPillar =
            order.AuxiliaryColumns.Count > 0 ||
            order.Steps.Any(step => step.Kind == AngleWorkStepKind.Support);

        bool isolated = IsolatedColumn.IsIsolatedColumn(target, column);

        if (!isolated)
        {
            limitations.Add(
                "保留托柱：该列不是孤立列（定理 4 不成立），停在 S 形态。");

            return order with { Limitations = limitations };
        }

        if (!hasPillar)
        {
            limitations.Add(
                "该列是孤立列（定理 4 成立）：按孤立列施工，不需要托柱。");

            return order with { Limitations = limitations };
        }

        /*
         * 有托柱且定理 4 成立：真的拆掉托柱。产物只保留目标列，
         * 并检验它自己能稳定站住（构造 C 交接 + 真实下落）。
         * 目标图的其它象限是固定场景，不能在这一步里清掉。
         */
        if (HasOtherColumn(target, column))
        {
            limitations.Add(
                "该列是孤立列（定理 4 成立），但目标图还有其它象限；" +
                "托柱由末步 Target 统一清除，不单独拆。");

            return order with { Limitations = limitations };
        }

        ShapeDocument isolatedShape = ShapeOps.Project(target, column, 1);

        if (!ShapeOps.IsStable(isolatedShape) ||
            ShapeSignature.ColumnText(isolatedShape, column) !=
                ShapeSignature.ColumnText(target, column))
        {
            limitations.Add(
                "保留托柱：定理 4 判真，但拆托柱后的孤立列回放不稳定。");

            return order with { Limitations = limitations };
        }

        var steps = new List<AngleWorkStep>(order.Steps);

        int targetIndex = steps.FindLastIndex(
            step => step.Kind == AngleWorkStepKind.Target);

        steps.Insert(
            targetIndex < 0 ? steps.Count : targetIndex,
            new AngleWorkStep(
                "构造 C：拆掉托柱，降到孤立列（定理 4 成立、真实下落稳定）",
                isolatedShape,
                AngleWorkStepKind.Support));

        limitations.Add(
            "拆托柱：该列满足定理 4，最后用构造 C 交接把托柱拆掉，降到孤立列。");

        return order with
        {
            Steps = steps,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 兜底链：原有的「构造 D（定向碎晶）→ 构造 G → 填充+暴破 → 构造 H / 多轮 G / F」
    /// 顺序只作为论文分档的回退。
    ///
    /// <para>
    /// T7（docs §5.2）：<see cref="AngleConstruction.FillBreak"/> 不是论文构造，
    /// 因此先试论文里的 <see cref="AngleConstruction.D"/>（只抬升 + 定向炸晶，
    /// 不把缺口整段填满），D 不行再试 <see cref="AngleConstruction.G"/>；
    /// 两者都不行才保留 FillBreak，并在 <see cref="AngleWorkOrder.Note"/> 里标注
    /// 「非论文构造」。
    /// </para>
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
        /*
         * 构造 D（定向碎晶）不再单独保留一个「搜索兜底」：它的抬升填法就是
         * FillPlanMode.LiftOnly 的固定计划，而路线 F 的规范计划（§11）与构造 G
         * 已经覆盖了 n = 4 的全部可达形态（≤8 层穷举 0 例落到这里）。
         * 兜底链直接从构造 G 开始，下一档是固定计划的填充+暴破。
         */
        if (parts == 4)
        {
            /*
             * 构造 G（四象限单孔环下落）：单层缺口、晶体塔至少三连晶、
             * 且放得下 G 的支撑环时，一次 Cut + 落定就能造出缺口。
             */
            AngleWorkOrder? constructG = TryBuildLegacyConstructG(
                target,
                column,
                layers,
                parts,
                half,
                kind);

            if (constructG is not null && constructG.IsComplete)
            {
                return constructG;
            }
        }

        AngleWorkOrder fillBreakOrder = TryBuildFillBreak(
            target,
            column,
            layers,
            parts,
            half,
            kind);

        if (fillBreakOrder.IsComplete)
        {
            return AnnotateNonPaperConstruction(fillBreakOrder, parts == 4);
        }

        AngleWorkOrder fallbackOrder = BuildLegacy(
            target,
            column,
            cap);

        if (fallbackOrder.IsComplete)
        {
            return fallbackOrder.Construction == AngleConstruction.FillBreak
                ? AnnotateNonPaperConstruction(fallbackOrder, parts == 4)
                : fallbackOrder;
        }

        return AppendFailure(
            fallbackOrder,
            "首选填充+暴破也未通过：" +
            StripUncoveredPrefix(fillBreakOrder.Note ?? "未知原因"));
    }

    /// <summary>
    /// 兜底链里的构造 G 尝试（docs §15）：只处理「单层缺口 + 晶体塔 ≥ 3 连晶 +
    /// t+2 在画布内」的形态，其余返回 null 交给后面的兜底。
    /// </summary>
    private static AngleWorkOrder? TryBuildLegacyConstructG(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        if (highest < 0)
        {
            return null;
        }

        int t = highest;

        while (t > 0 &&
               target[t - 1, column].Type == CellType.Crystal)
        {
            t--;
        }

        if (t <= 0)
        {
            return null;
        }

        int s = t - 1;

        if (target[s, column].Type != CellType.Empty)
        {
            return null;
        }

        int d = -1;

        for (int layer = s - 1; layer >= 0; layer--)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                d = layer;
                break;
            }
        }

        if (d < 0 || target[d, column].Type != CellType.Shape)
        {
            return null;
        }

        bool singleLayerGap = d == s - 1;
        bool gTowerOk = highest - t + 1 >= 3;
        bool gPinRoom = s + 2 < layers;

        if (!singleLayerGap || !gTowerOk || !gPinRoom)
        {
            return null;
        }

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

    /// <summary>
    /// 给保留下来的 <see cref="AngleConstruction.FillBreak"/> 工序加「非论文构造」标注
    /// （T7 / docs §5.2）。
    /// </summary>
    private static AngleWorkOrder AnnotateNonPaperConstruction(
        AngleWorkOrder order,
        bool triedDirectionalAndLoopDown)
    {
        string description = triedDirectionalAndLoopDown
            ? "填充+暴破不是论文里的构造（A–H），" +
              "该形态上构造 D 与构造 G 都没通过真实回放，只能保留本兜底路线。"
            : "填充+暴破不是论文里的构造（A–H）；" +
              "该形态不走 n = 4 的构造 D / G 判据，只能保留本兜底路线。";

        string annotation = NonPaperConstructionPrefix + description;

        var limitations = new List<string>(order.Limitations);

        limitations.Add(annotation);

        return order with
        {
            Note = annotation,
            Limitations = limitations
        };
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
                /*
                 * n ≥ 6：算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回）。
                 * 三条算法都输出列级步骤链 PreState → Cut → Collapse → Support → Target。
                 */
                return TryBuildSixPlusOrder(
                    target,
                    column,
                    layers,
                    parts,
                    half,
                    kind);

            case AngleAlgorithm.Algorithm5:
            {
                /*
                 * 算法 5（底格是针）：先试构造性构造 H（docs §18.4）。
                 * 底针位置用临时晶体顶住（Pin 会把它补成针），其余按 I/J 算。
                 */
                AngleWorkOrder? constructedH = TryBuildConstructedH(
                    target,
                    column,
                    layers,
                    parts,
                    kind,
                    AngleAlgorithm.Algorithm5,
                    out string? constructedHFailure);

                if (constructedH is not null)
                {
                    return constructedH;
                }

                /*
                 * 算法 5 的正解（TODO.md §7.2 的 P.T.c）：底针位置用临时晶体
                 * 顶住（前态 ccTc.），一次 Pin 把底层晶体换成顶针、同时让
                 * 引爆柱超限碎晶，再由构造 C 换回满高支撑。先试这条塔式构造，
                 * 失败才退回通用的构造 H（多层缺口的批量下落）。
                 */
                AngleWorkOrder? towerStyle = TryBuildTowerStyleOrder(
                    target,
                    column,
                    layers,
                    parts,
                    kind,
                    allowBottomPin: true,
                    AngleAlgorithm.Algorithm5);

                if (towerStyle is not null)
                {
                    return towerStyle;
                }

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
                    : ConstructHUncovered(column, kind, constructedHFailure);
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

    /// <summary>
    /// 核心 / 尾部拆分（docs §17.5 第 1 条 / §5.3）：把目标列拆成
    /// 「核心（<b>整图</b>最高晶体及以下）」与「尾部（以上）」。
    ///
    /// <para>
    /// <b><c>Hc</c> 的取法（§5.3 ①）</b>：<c>Hc</c> 是<b>整图</b>（所有象限）最高晶体
    /// 所在层，不是目标列自己的最高晶体——这样「<c>Hc</c> 以上没有晶体」由扫描
    /// 自动满足，也不需要额外的「尾部无晶体」判据（§5.3 的说明）。
    /// </para>
    /// <para>
    /// 核心直接复用现有分档流程：核心列没有尾部，因此不会递归；
    /// 核心工序完成后再用构造 C 接回尾部：
    /// </para>
    /// <list type="number">
    /// <item>先装一根满高支撑柱（尾部可能包含空层，靠侧向粘连站住）；</item>
    /// <item>从低到高逐层追加尾部材料，每层都要求稳定；</item>
    /// <item>最后清掉辅助象限，得到完整目标角。</item>
    /// </list>
    /// <para>
    /// 返回 null 表示「没有尾部」或核心本身没做出来，交由原来的流程处理。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildTailOrder(
        ShapeDocument target,
        int column,
        int cap,
        AngleBuildKind kind,
        bool enforceFiveConditions)
    {
        int highest = HighestCrystalLayerOfShape(target);

        if (highest < 0 || highest >= target.LayerCount - 1)
        {
            return null;
        }

        bool hasTail = false;

        for (int layer = highest + 1; layer < target.LayerCount; layer++)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                hasTail = true;
                break;
            }
        }

        if (!hasTail)
        {
            return null;
        }

        ShapeDocument core = target.Clone();

        for (int layer = highest + 1; layer < target.LayerCount; layer++)
        {
            core[layer, column] = ShapeCell.Empty();
        }

        AngleWorkOrder coreOrder = Build(
            core,
            column,
            cap,
            enforceFiveConditions);

        if (coreOrder.IsComplete is false)
        {
            return null;
        }

        ShapeDocument? baseShape = coreOrder.Steps
            .LastOrDefault(step => step.Kind == AngleWorkStepKind.Target)
            ?.Shape;

        ShapeDocument current = baseShape is null
            ? BuildCoreShape(core, column, cap, target.PartCount)
            : baseShape.Clone();

        int supportColumn = Wrap(column + 1, target.PartCount);
        int otherSupport = Wrap(column - 1, target.PartCount);

        if (!HasOtherColumn(current, column))
        {
            ShapeDocument? withSupport = TryAddSupportPillar(
                current,
                column,
                supportColumn,
                otherSupport);

            if (withSupport is null)
            {
                return null;
            }

            current = withSupport;
        }

        var steps = new List<AngleWorkStep>(coreOrder.Steps);

        if (steps.Count > 0 &&
            steps[^1].Kind == AngleWorkStepKind.Target)
        {
            steps.RemoveAt(steps.Count - 1);
        }

        if (!HasOtherColumn(current, column))
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            $"构造 C：装满高支撑柱（第 {supportColumn + 1} 象限），" +
            "为尾部的悬空层提供侧向支撑",
            current.Clone(),
            AngleWorkStepKind.Support));

        for (int layer = highest + 1; layer < target.LayerCount; layer++)
        {
            if (target[layer, column].Type == CellType.Empty)
            {
                continue;
            }

            ShapeDocument next = current.Clone();

            next[layer, column] = target[layer, column].Clone();

            if (!ShapeOps.IsStable(next))
            {
                return null;
            }

            current = next;

            steps.Add(new AngleWorkStep(
                $"构造 C：接回尾部第 {layer + 1} 层" +
                $"（{Describe(target[layer, column])}）",
                current.Clone(),
                AngleWorkStepKind.Stack));
        }

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到完整目标角",
            target,
            AngleWorkStepKind.Target));

        var limitations = new List<string>(coreOrder.Limitations);

        limitations.Add(
            $"核心 / 尾部拆分（§5.3）：Hc = 第 {highest + 1} 层（<b>整图</b>最高晶体），" +
            $"核心 = 第 1–{highest + 1} 层，" +
            $"尾部 = 第 {highest + 2}–{target.LayerCount} 层；" +
            "尾部不参与碎晶，最后由构造 C 逐层接回。");

        int rank = 0;

        foreach (AngleWorkStep step in steps)
        {
            if (step.Kind == AngleWorkStepKind.Pin)
            {
                rank++;
            }
        }

        return AttachQ1SubRecipe(new AngleWorkOrder(
            column,
            kind,
            steps,
            // 核心已经完成：把它「非论文构造」之类的标注原样带到整份工序单。
            coreOrder.Note)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = coreOrder.Construction,
            Algorithm = coreOrder.Algorithm,
            Rotation = coreOrder.Rotation,
            AuxiliaryColumns = coreOrder.AuxiliaryColumns,
            SubRecipe = coreOrder.SubRecipe,
            Rank = rank,
            Limitations = limitations
        });
    }

    /// <summary>
    /// 整图最高晶体所在层（docs §5.3 ① 的 <c>Hc</c>，0 起；没有晶体返回 -1）。
    ///
    /// <para>
    /// 注意与 <see cref="AngleClassifier.HighestCrystalLayer"/> 的区别：那个只扫
    /// <b>一列</b>，这里的 <c>Hc</c> 要扫<b>所有象限</b>——否则当别的象限有更高的
    /// 晶体时，核心 / 尾部的分界线会取低，尾部里就会混进晶体（§5.3 明确要求
    /// 「<c>Hc</c> 以上没有晶体」）。
    /// </para>
    /// </summary>
    private static int HighestCrystalLayerOfShape(
        ShapeDocument document)
    {
        int highest = -1;

        for (int part = 0; part < document.PartCount; part++)
        {
            int layer = AngleClassifier.HighestCrystalLayer(document, part);

            if (layer > highest)
            {
                highest = layer;
            }
        }

        return highest;
    }

    /// <summary>把核心目标列写进一张新图（其余象限留空）。</summary>
    private static ShapeDocument BuildCoreShape(
        ShapeDocument core,
        int column,
        int cap,
        int parts)
    {
        var document = new ShapeDocument(cap, parts);

        for (int layer = 0;
             layer < Math.Min(cap, core.LayerCount);
             layer++)
        {
            document[layer, column] = core[layer, column].Clone();
        }

        return document;
    }

    /// <summary>除目标列之外是否还有其它非空格（已有支撑柱）。</summary>
    private static bool HasOtherColumn(
        ShapeDocument document,
        int column)
    {
        for (int part = 0; part < document.PartCount; part++)
        {
            if (part == column)
            {
                continue;
            }

            for (int layer = 0; layer < document.LayerCount; layer++)
            {
                if (document[layer, part].Type != CellType.Empty)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 给核心目标角加一根满高普通柱（构造 C 的「装新柱」），
    /// 两邻位依次尝试，要求稳定且每列合法。
    /// </summary>
    private static ShapeDocument? TryAddSupportPillar(
        ShapeDocument baseShape,
        int column,
        int first,
        int second)
    {
        foreach (int candidate in new[] { first, second })
        {
            ShapeDocument copy = baseShape.Clone();

            FillPillar(copy, candidate);

            if (!ShapeOps.IsStable(copy) ||
                !ShapeSignature.AllColumnsLegal(copy, out _))
            {
                continue;
            }

            return copy;
        }

        return null;
    }

    /// <summary>
    /// n = 4 的 S 判据不成立：角合法但不可制作（docs §17.3）。
    ///
    /// <para>
    /// 与五项条件失败一样使用 <see cref="NotProduciblePrefix"/>；
    /// 这不是「未覆盖」——D 与 F 都不成立时，论文定理 3 已判否。
    /// </para>
    /// </summary>
    private static AngleWorkOrder NotProducibleBySCriterion(
        ShapeDocument target,
        int column,
        AngleBuildKind kind,
        string? halfFailure = null)
    {
        string columnText = ShapeSignature.ColumnText(target, column);

        string reason =
            $"n = 4 的 S 判据不成立（D 与 F 都不成立）：列 {columnText} 虽然通过五项条件，" +
            "但论文定理 3 判它不可制作。";

        var limitations = new List<string>
        {
            "S 判据（n = 4）：五项条件 ∧ (D∨F)，见论文定理 3。",
            "这不是「未覆盖」：D 与 F 都不成立时，任何算法都做不出这个角。"
        };

        if (halfFailure is not null)
        {
            limitations.Add(
                "半边候选（1 → 2 → 3）全部没通过；最后一条诊断：" +
                halfFailure.Replace(
                    UncoveredPrefix,
                    string.Empty,
                    StringComparison.Ordinal));
        }

        return new AngleWorkOrder(
            column,
            kind,
            new[]
            {
                new AngleWorkStep(
                    "不可制作：" + reason,
                    null,
                    AngleWorkStepKind.Note)
            },
            NotProduciblePrefix + reason)
        {
            IsComplete = false,
            Construction = AngleConstruction.None,
            Algorithm = AngleAlgorithm.None,
            Rank = 0,
            Limitations = limitations
        };
    }

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
            TargetColumnHasSubRecipe = true,
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
            TargetColumnHasSubRecipe = true,
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
    /// <summary>
    /// 算法 6：先按 §20.3 构造式 / 塔式 / 通用计划生成；全部失败且目标底层是
    /// 普通格时，再做一次<b>底块提升</b>构造 H（docs §21.3 ① / §20.5）。
    ///
    /// <para>
    /// 底块提升放在所有既有候选<b>之后</b>：既有候选已经覆盖的形态（<c>T.T.c</c>、
    /// <c>T.T..c</c> 等）保持原输出不变，只有它们都回放失败、且底层又是普通格时
    /// 才用这条推导。实测（≤9 层穷举）只有 9 层的 <c>T..cT.T.c</c>、<c>T.T.cccTc</c>
    /// 与 8 层的 <c>T.T..ccc</c> 会走到这里；前两者是 docs §21.3 的缺口，
    /// 最后一个原先由兜底构造 G 完成、现在改由算法 6 的 H 完成（成功数不变）。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildAlgorithm6(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind)
    {
        AngleWorkOrder? core = TryBuildAlgorithm6Core(
            target,
            column,
            layers,
            parts,
            kind);

        return core;
    }

    private static AngleWorkOrder? TryBuildAlgorithm6Core(
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

        /*
         * 构造 H 的构造性入口（docs §18.4）：先按 I/J 直接生成前态 A/B/E/N，
         * 再用真实 Pin 回放验收。它覆盖「D ✔ + 多个短晶体段」这一族
         * （.T.cTc / .T.cTT.c / .T.cT.Tc / .T.T.T.cT.Tc），
         * 不再走 support × block × fuse × 列编码的枚举。
         */
        AngleWorkOrder? constructedH = TryBuildConstructedH(
            target,
            column,
            layers,
            parts,
            kind,
            AngleAlgorithm.Algorithm6,
            out string? constructedHFailure);

        if (constructedH is not null)
        {
            return constructedH;
        }

        // ── 第一步：底块提升 + 正规化（晶体下移一层，普通格留原位）──
        string? shiftedTower = BuildShiftedTowerColumn(
            target,
            column,
            layers);

        if (shiftedTower is null)
        {
            /*
             * 先试 §11 通用计划：它一次就能覆盖「晶体下移会与普通格重叠」的
             * 形态（TT..Tc 一族），而且回放很快；
             * 通用计划不成立时再压缩缺口、走塔式构造（.T.cTc 一族）。
             */
            AngleWorkOrder? alternative = TryAlternativeAlgorithm6(
                target,
                column,
                layers,
                kind);

            if (alternative is not null)
            {
                return alternative;
            }

            string? compressed = CompressGapBelowCrystal(
                target,
                column,
                layers);

            if (compressed is not null)
            {
                var seeds = new List<string>();

                string? lifted = LiftBottomSolid(compressed);

                // 底块提升后的形态优先（算法 6 的「抬升最下连续普通块」）：
                // T..Tc 一族在抬升后的塔上就能一次 Pin 成功。
                if (lifted is not null)
                {
                    seeds.Add(lifted);
                }

                if (!seeds.Contains(compressed, StringComparer.Ordinal))
                {
                    seeds.Add(compressed);
                }

                foreach (string seed in seeds)
                {
                    AngleWorkOrder? compressedOrder = TryBuildTowerStyleOrder(
                        target,
                        column,
                        layers,
                        parts,
                        kind,
                        allowBottomPin: false,
                        AngleAlgorithm.Algorithm6,
                        extraTowerColumn: seed);

                    if (compressedOrder is not null)
                    {
                        return compressedOrder;
                    }
                }
            }

            return null;
        }

        /*
         * 用户塔式构造优先（TODO.md §7.2 的 T.T.c、§7.7 的 .T.T.c）：
         * 底块要提升（正规塔是 T.Tc.、底层普通格要抬到第 2 层，
         * 否则 Pin 会在第 1 层补出一枚目标里没有的顶针），或者多个普通块
         * 被缺口隔开（.T.Tc.，通用构造 H 的单针支撑柱撑不住）时，
         * 直接用从目标列生成的塔 X 做一次 Pin + 构造 C，不再走填充+暴破。
         */
        if (ShouldUseTowerStyleH(shiftedTower))
        {
            AngleWorkOrder? towerStyle = TryBuildTowerStyleOrder(
                target,
                column,
                layers,
                parts,
                kind,
                allowBottomPin: false,
                AngleAlgorithm.Algorithm6);

            if (towerStyle is not null)
            {
                return towerStyle;
            }
        }

        string? normalizedTower = shiftedTower[0] ==
                                  ShapeSignature.EmptyCode
            ? shiftedTower
            : null;

        if (normalizedTower is null)
        {
            AngleWorkOrder? alternativeTower = TryAlternativeAlgorithm6(
                target,
                column,
                layers,
                kind);

            return alternativeTower ?? ConstructHUncovered(column, kind, constructedHFailure);
        }

        /*
         * 构造 H 已在函数开头按 I/J 构造性生成并回放验证过；走到这里说明它没覆盖
         * 该形态。算法 6 还剩两条<b>确定性</b>构造，按顺序试，不做候选枚举：
         * 两刀级联（构造 D）与 §11 通用计划（提升 + 定向碎晶）。
         */
        if (!CanUseConstructH(normalizedTower))
        {
            AngleWorkOrder? twoCut = TryBuildTwoCutCascade(
                target,
                column,
                layers,
                parts,
                kind);

            if (twoCut is not null)
            {
                return twoCut;
            }
        }

        AngleWorkOrder? genericOrder = TryAlternativeAlgorithm6(
            target,
            column,
            layers,
            kind);

        if (genericOrder is not null)
        {
            return genericOrder;
        }

        return ConstructHUncovered(column, kind, constructedHFailure);
    }

    /// <summary>
    /// 构造 H 的构造性生成失败时的「未覆盖」单：<b>不回退枚举搜索</b>，
    /// 只如实写明缺哪条规则（docs §18.4）。
    /// </summary>
    private static AngleWorkOrder ConstructHUncovered(
        int column,
        AngleBuildKind kind,
        string? reason)
    {
        return Incomplete(
            column,
            kind,
            "构造 H（按 I/J 构造性生成）未覆盖：" +
            (reason ?? "前态没有通过真实 Pin 回放"),
            BaseLimitations(
                "构造 H 只按 docs §18.4 的 I/J 规则直接生成 A/B/E/N，" +
                "不做 support × block × fuse 的候选枚举；生成或回放验证失败即报未覆盖。"),
            construction: AngleConstruction.H);
    }

    /// <summary>
    /// 算法 6 的后备路线：§11 通用计划（提升 + 定向碎晶），带真实回放与假阳性闸门。
    /// </summary>
    private static AngleWorkOrder? TryAlternativeAlgorithm6(
        ShapeDocument target,
        int column,
        int layers,
        AngleBuildKind kind)
    {
        AngleWorkOrder? generic = TryBuildGenericFourQuadrantOrder(
            target,
            column,
            layers,
            kind);

        if (generic is not null &&
            generic.IsComplete &&
            !HasCyclicPreState(generic, target) &&
            !generic.HasIllegalPreState(out _, out _))
        {
            return generic;
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
    /// 把最高晶体正下方的第一个缺口「压掉」：缺口以上的整段材料下移一层。
    ///
    /// <para>
    /// 例：<c>.T.cTc</c>（第 3 层是空）⇒ <c>.TcTc.</c>。
    /// 这样像 <c>c</c>、<c>T</c> 这种「晶体—普通格」交替的列，
    /// 就不会在「晶体下移一层」时撞上正下方的普通格（<see cref="BuildShiftedTowerColumn"/>
    /// 会因重叠返回 null），仍能生成一份可顶针的前态。
    /// 缺口位置由扫描得出，不写死层号。
    /// </para>
    /// </summary>
    private static string? CompressGapBelowCrystal(
        ShapeDocument target,
        int column,
        int layers)
    {
        int crystal = AngleClassifier.HighestCrystalLayer(target, column);

        if (crystal < 1 || layers > target.LayerCount)
        {
            return null;
        }

        var cells = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell cell = layer < target.LayerCount
                ? target[layer, column]
                : ShapeCell.Empty();

            cells[layer] = cell.Type switch
            {
                CellType.Crystal => ShapeSignature.CrystalCode,
                CellType.Shape => ShapeSignature.SolidCode,
                CellType.Pin => ShapeSignature.PinCode,
                _ => ShapeSignature.EmptyCode
            };

            if (cell.Type == CellType.Pin)
            {
                // 底针角走算法 5，不在这里改。
                return null;
            }
        }

        int gap = -1;

        for (int layer = crystal - 1; layer >= 1; layer--)
        {
            if (cells[layer] == ShapeSignature.EmptyCode &&
                cells[layer + 1] != ShapeSignature.EmptyCode)
            {
                gap = layer;
                break;
            }
        }

        if (gap < 0)
        {
            return null;
        }

        for (int layer = gap; layer < crystal; layer++)
        {
            cells[layer] = cells[layer + 1];
        }

        cells[crystal] = ShapeSignature.EmptyCode;

        return new string(cells);
    }

    /// <summary>
    /// 只做「晶体下移一层、普通格留在原位」，<b>不</b>检查底层是否为空。
    ///
    /// <para>
    /// 底块提升的形态（<c>T.T.c</c> 的正规塔 <c>T.Tc.</c>）底层本来就有普通格，
    /// 需要先拿到这个中间形态再把底块抬上去；底层是否为空的判定留给调用方。
    /// </para>
    /// </summary>
    private static string? BuildShiftedTowerColumn(
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

        return new string(cells);
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

    #region 用户塔式构造 H（§7.2：T.T.c / P.T.c）

    /// <summary>
    /// 是否该先试「用户塔式构造 H」。
    ///
    /// <para>
    /// 两类形态需要塔式的「连续针 + 托柱帽」支撑柱，而不是通用构造 H 的
    /// 「单针 + 普通格」：
    /// </para>
    /// <list type="number">
    /// <item><b>底块要提升</b>：正规塔底层还有普通格（<c>T.T.c</c> 的
    /// <c>T.Tc.</c>），不抬升的话 Pin 会在第 1 层补出一枚目标里没有的顶针；</item>
    /// <item><b>多个普通块被缺口隔开</b>：<c>.T.T.c</c> 的 <c>.T.Tc.</c> 有两个
    /// 普通块（第 2、4 层），通用构造 H 的单针支撑柱撑不住，必须用塔式的
    /// 连续针柱（见 TODO.md §7.7）。用 <see cref="CanUseConstructH"/> 排除
    /// <c>.TT.c</c> 那种「两枚普通格直接叠在晶体下」的形态——它按文档
    /// 应退回算法 2 + 构造 D。
    /// </item>
    /// </list>
    /// </summary>
    private static bool ShouldUseTowerStyleH(
        string normalizedTower)
    {
        if (LiftBottomSolid(normalizedTower) is not null)
        {
            return true;
        }

        return CountSolids(normalizedTower) >= 2 &&
               CanUseConstructH(normalizedTower);
    }

    /// <summary>列里普通格的数量。</summary>
    private static int CountSolids(
        string column)
    {
        int count = 0;

        foreach (char code in column)
        {
            if (code == ShapeSignature.SolidCode)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 把底层的连续普通格整体抬升一格（上面一格必须为空）。
    ///
    /// <para>
    /// 这是「底块提升」的具体做法：<c>T.Tc.</c> ⇒ <c>.TTc.</c>。
    /// 抬升后前态底层为空，Pin 不会在角列上补顶针。
    /// </para>
    /// </summary>
    private static string? LiftBottomSolid(
        string towerColumn)
    {
        if (towerColumn.Length < 2 ||
            towerColumn[0] != ShapeSignature.SolidCode)
        {
            return null;
        }

        int run = 0;

        while (run < towerColumn.Length &&
               towerColumn[run] == ShapeSignature.SolidCode)
        {
            run++;
        }

        if (run >= towerColumn.Length ||
            towerColumn[run] != ShapeSignature.EmptyCode)
        {
            return null;
        }

        char[] cells = towerColumn.ToCharArray();

        for (int index = run; index >= 1; index--)
        {
            cells[index] = ShapeSignature.SolidCode;
        }

        cells[0] = ShapeSignature.EmptyCode;

        return new string(cells);
    }

    /// <summary>
    /// 底针角（<c>P.T.c</c>）的前态角列：底针位置用临时晶体顶住。
    ///
    /// <para>
    /// 目标第 1 层的顶针由临时晶体顶替（Pin 会把底层的非空格补成顶针），
    /// 第 2 层再垫一枚晶体；最高晶体下移一层抵消 Pin 的抬升。
    /// <c>P.T.c</c> ⇒ <c>ccTc.</c>，与 §7.2 用户给的塔完全一致。
    /// </para>
    /// </summary>
    private static string? BuildPinHeldTowerColumn(
        ShapeDocument target,
        int column,
        int layers)
    {
        var cells = new char[layers];

        Array.Fill(cells, ShapeSignature.EmptyCode);

        bool sawPin = false;

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
                    cells[layer] = ShapeSignature.SolidCode;
                    break;

                case CellType.Pin:
                    // 只有底部的针能用临时晶体顶替；更高处的针不是这条路线。
                    if (layer != 0)
                    {
                        return null;
                    }

                    cells[layer] = ShapeSignature.CrystalCode;
                    sawPin = true;
                    break;

                case CellType.Crystal:
                    if (layer == 0 ||
                        cells[layer - 1] != ShapeSignature.EmptyCode)
                    {
                        return null;
                    }

                    cells[layer - 1] = ShapeSignature.CrystalCode;
                    break;
            }
        }

        if (!sawPin ||
            cells[0] != ShapeSignature.CrystalCode)
        {
            return null;
        }

        // 底针位置用临时晶体顶住：第 2 层若为空，也垫一枚晶体。
        if (cells.Length > 1 &&
            cells[1] == ShapeSignature.EmptyCode)
        {
            cells[1] = ShapeSignature.CrystalCode;
        }

        return new string(cells);
    }

    /// <summary>
    /// 底针 + 多层缺口的前态角列：底针位置用临时晶体顶住，最高普通格放在
    /// 「晶体下方第二个空段」的顶端，顶部晶体下移一格。
    ///
    /// <para>
    /// 结构规则（全部由扫描得出）：设 <c>t</c> = 晶体下方最高的普通格，
    /// <c>c</c> = 最高晶体。取 <c>preT = max(t, c − 2)</c>：
    /// </para>
    /// <list type="number">
    /// <item>第 1–preT 层填成临时晶体（Pin 后变成底针 + 被引爆柱整簇删掉的临时段）；</item>
    /// <item>第 preT+1 层放那枚普通格；</item>
    /// <item>第 c 层放晶体（Pin 后正落到目标晶体层 c+1）。</item>
    /// </list>
    /// <para>
    /// 实测：<c>P.T.c</c> ⇒ <c>ccTc.</c>；<c>P.T..c</c> 与 <c>P..T.c</c> ⇒ <c>cccTc.</c>。
    /// 这不是查表——<c>t</c>、<c>c</c> 都是扫描值。
    /// </para>
    /// </summary>
    private static string? BuildPinHeldGapTowerColumn(
        ShapeDocument target,
        int column,
        int layers)
    {
        if (target[0, column].Type != CellType.Pin)
        {
            return null;
        }

        int crystal = AngleClassifier.HighestCrystalLayer(target, column);

        if (crystal < 3 || crystal >= layers)
        {
            return null;
        }

        int solid = -1;

        for (int layer = 1; layer < crystal; layer++)
        {
            switch (target[layer, column].Type)
            {
                case CellType.Empty:
                    break;

                case CellType.Shape:
                    if (solid >= 0)
                    {
                        return null;
                    }

                    solid = layer;
                    break;

                default:
                    return null;
            }
        }

        if (solid < 0)
        {
            return null;
        }

        int preT = Math.Max(solid, crystal - 2);

        if (preT >= crystal - 1)
        {
            return null;
        }

        for (int layer = crystal + 1; layer < target.LayerCount; layer++)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                return null;
            }
        }

        var cells = new char[layers];

        Array.Fill(cells, ShapeSignature.EmptyCode);

        for (int layer = 0; layer < preT; layer++)
        {
            cells[layer] = ShapeSignature.CrystalCode;
        }

        cells[preT] = ShapeSignature.SolidCode;
        cells[crystal - 1] = ShapeSignature.CrystalCode;

        return new string(cells);
    }

    /// <summary>
    /// 塔式构造 C 的托针柱（N）：针位由「要落下的格 + 下落路径」直接算出。
    ///
    /// <para>
    /// 塔列 <c>A</c> 在 Pin 后整体抬一层，普通格从 <c>s = 塔列层号 + 1</c> 落到目标层。
    /// 构造 E（docs §5 规则一）：针不参与横向粘连，所以每条下落路径经过的层都放针；
    /// 路径的并集正好是 <c>[0, topSolid + 1]</c>（<c>topSolid</c> 是塔列最高普通格）。
    /// 针段之上留一枚普通格当托帽（盖在最高落点上方），其余为空。
    /// </para>
    /// <para>
    /// 没有候选枚举：针数 = <c>topSolid + 1</c>，托帽 = 下一层，全部由沿列扫描得出；
    /// 塔列高度不够放下托帽时返回 null（调用方按「未覆盖」处理）。
    /// </para>
    /// </summary>
    private static string? BuildTowerSupportColumn(
        string towerColumn)
    {
        int topSolid = LastSolidInText(towerColumn);

        if (topSolid < 0 || topSolid + 1 >= towerColumn.Length)
        {
            return null;
        }

        return new string(ShapeSignature.PinCode, topSolid + 1) +
               ShapeSignature.SolidCode +
               new string(
                   ShapeSignature.EmptyCode,
                   towerColumn.Length - topSolid - 2);
    }

    /// <summary>
    /// 塔式构造 C 的落块柱（B）：把塔列的普通格送回目标层的那根辅助柱。
    ///
    /// <para>
    /// B 的普通格与塔列的最高普通格对齐（<c>0..topSolid</c>），Pin 后整列落成
    /// <c>P + 普通格 0..topSolid</c>，正好把塔列的普通格托在目标层；
    /// 其中<b>一层换成晶体</b>当桥锚：它与引爆柱 E 同层相邻，Pin 时随 E 整簇碎掉。
    /// </para>
    /// <para>
    /// 桥锚层由目标列扫描得出（与构造 H 的锚层同一规则：临时晶体块要接进引爆簇）：
    /// </para>
    /// <list type="bullet">
    /// <item>目标底层是普通格 ⇒ 桥锚在第 2 层，让 B 的第 1 层保持实心，
    /// 托住塔列要落到地面的那枚普通格；</item>
    /// <item>否则（底层为空 / 针）⇒ 桥锚在第 1 层，B 从桥锚起连成紧列。</item>
    /// </list>
    /// </summary>
    private static string? BuildTowerBridgeColumn(
        ShapeDocument target,
        int column,
        string towerColumn)
    {
        int topSolid = LastSolidInText(towerColumn);

        if (topSolid < 0)
        {
            return null;
        }

        int anchor = target[0, column].Type == CellType.Shape ? 1 : 0;

        if (anchor > topSolid)
        {
            return null;
        }

        var cells = new char[towerColumn.Length];

        Array.Fill(cells, ShapeSignature.EmptyCode);

        for (int layer = 0; layer <= topSolid; layer++)
        {
            cells[layer] = layer == anchor
                ? ShapeSignature.CrystalCode
                : ShapeSignature.SolidCode;
        }

        return new string(cells);
    }

    /// <summary>列文本里最高普通格所在的层（0 起）；没有时返回 -1。</summary>
    private static int LastSolidInText(
        string column)
    {
        int topSolid = -1;

        for (int layer = 0; layer < column.Length; layer++)
        {
            if (column[layer] == ShapeSignature.SolidCode)
            {
                topSolid = layer;
            }
        }

        return topSolid;
    }

    /// <summary>
    /// 塔式构造 C（TODO.md §7.2 / 论文构造 H 的塔式形态）：从目标列直接生成四列塔
    /// 「角列 A + 落块柱 B + 引爆柱 E + 托针柱 N」。
    ///
    /// <para>
    /// 角列按形态取「正规化」「底块提升」或「底针埋晶」几个<b>固定</b>形态；
    /// N/B/E 全部由扫描直接构造（不再枚举 support × block × arrangement）：
    /// </para>
    /// <list type="bullet">
    /// <item>N 的针位 = 塔列最高普通格以下的全部层（构造 E 的下落路径），
    /// 上面一枚普通格当托帽；</item>
    /// <item>B 的普通格与塔列普通格对齐，桥锚层由「目标底层是否为普通格」直接确定；</item>
    /// <item>E 是满高纯晶体柱；角色排布固定为 A | B | E | N（B = +1、E = +2、N = −1）。</item>
    /// </list>
    /// <para>
    /// 塔装配好后只做一次真实 <see cref="ShapePhysics.Pin"/>，再用构造 C 换支撑柱。
    /// 整条链用真实物理回放验收：塔稳定、每列过五项条件、Pin 后目标列逐格
    /// 等于目标、交换链能 rank 0 装配、支柱交接后仍稳定；失败即返回 null，
    /// 由调用方报「未覆盖：缺哪条规则」，不再回退候选搜索。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildTowerStyleOrder(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        bool allowBottomPin,
        AngleAlgorithm algorithm,
        string? extraTowerColumn = null)
    {
        if (parts != 4)
        {
            return null;
        }

        var towerColumns = new List<string>();

        if (allowBottomPin &&
            target[0, column].Type == CellType.Pin &&
            target.LayerCount > 1 &&
            target[1, column].Type == CellType.Empty)
        {
            // 「底针位置用临时晶体顶住」只针对第 2 层为空、需要垫晶体的形态
            // （P.T.c ⇒ ccTc.）；底针上方已经是普通格的形态仍走通用构造 H。
            string? pinHeld = BuildPinHeldTowerColumn(
                target,
                column,
                layers);

            string? gapPinHeld = BuildPinHeldGapTowerColumn(
                target,
                column,
                layers);

            if (gapPinHeld is not null &&
                !string.Equals(
                    gapPinHeld,
                    pinHeld,
                    StringComparison.Ordinal))
            {
                /*
                 * 多层缺口：旧 pinHeld 塔的第 2 层晶体与顶部晶体之间
                 * 普通资源不够，五项条件本来就过不了；直接把 gap 塔放第一位，
                 * 少试一份必然失败的角列。
                 */
                towerColumns.Add(gapPinHeld);
            }
            else if (pinHeld is not null)
            {
                towerColumns.Add(pinHeld);
            }
        }

        string? shifted = BuildShiftedTowerColumn(
            target,
            column,
            layers);

        if (shifted is not null)
        {
            // 底块提升后的形态优先（T.T.c 的正解 .TTc.）。
            string? lifted = LiftBottomSolid(shifted);

            if (lifted is not null)
            {
                towerColumns.Add(lifted);
            }

            // 正规化塔仍作为后备；底层非空（T.T.c）时它不能直接用。
            if (shifted[0] == ShapeSignature.EmptyCode)
            {
                towerColumns.Add(shifted);
            }
        }

        if (extraTowerColumn is not null &&
            !towerColumns.Contains(extraTowerColumn, StringComparer.Ordinal))
        {
            towerColumns.Add(extraTowerColumn);
        }

        if (towerColumns.Count == 0)
        {
            return null;
        }

        var limitations = BaseLimitations(
            algorithm == AngleAlgorithm.Algorithm5
                ? "算法 5：底针位置用临时晶体顶住（ccTc.）→ 一次 Pin（构造 H）→ " +
                  "构造 C 换回满高支撑。"
                : "算法 6：底块提升（正规化塔 .TTc.）→ 一次 Pin（构造 H）→ " +
                  "构造 C 换回满高支撑。");

        limitations.Add(
            "塔 X 用交换链装配（rank 0），角工序的 rank 只统计这一次顶针。");

        limitations.Add(
            "塔里没有造洞用的 Cut：缺口由 Pin 的抬升与引爆柱整簇碎晶造出，" +
            "落块柱只负责把普通格送回目标层。");

        limitations.Add(
            "托针柱 N 的针位 = 最高普通格以下的全部层 [0, topSolid]，" +
            "托帽在 topSolid + 1；落块柱 B 的桥锚层由目标底层是否为普通格直接确定。" +
            "角色排布固定为 A | B | E | N（落块柱 +1、引爆柱 +2、托针柱 −1），" +
            "不再枚举 support × block × arrangement 候选。");

        // 固定排布：A = 目标列，B = +1，E = +2，N = −1（环顺序）。
        int blockPosition = Wrap(column + 1, parts);
        int fusePosition = Wrap(column + 2, parts);
        int supportPosition = Wrap(column + 3, parts);

        var seenTowers = new HashSet<string>(StringComparer.Ordinal);

        foreach (string towerColumn in towerColumns)
        {
            if (!seenTowers.Add(towerColumn))
            {
                continue;
            }

            string? supportText = BuildTowerSupportColumn(towerColumn);
            string? blockText = BuildTowerBridgeColumn(
                target,
                column,
                towerColumn);

            if (supportText is null || blockText is null)
            {
                continue;
            }

            AngleWorkOrder? order = BuildTowerStyleOrder(
                target,
                column,
                layers,
                parts,
                kind,
                algorithm,
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

        return null;
    }

    /// <summary>按固定四列布局装出塔 X，并用真实物理回放整条链。</summary>
    private static AngleWorkOrder? BuildTowerStyleOrder(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        AngleAlgorithm algorithm,
        string towerColumn,
        string supportText,
        string blockText,
        int supportPosition,
        int blockPosition,
        int fusePosition,
        List<string> limitations)
    {
        var tower = new ShapeDocument(layers, parts);

        // 晶体颜色必须沿用目标列：Pin 保留颜色。
        char crystalColor = CrystalColor(target, column);

        WriteColumn(tower, column, towerColumn, crystalColor);
        WriteColumn(tower, supportPosition, supportText, crystalColor);
        WriteColumn(tower, blockPosition, blockText, crystalColor);
        WriteColumn(
            tower,
            fusePosition,
            new string(ShapeSignature.CrystalCode, layers),
            crystalColor);

        /*
         * 辅助列按构造直接生成，已经是紧列（只有末尾空层）：
         * 托针柱 = 底部针段 + 一枚托帽；落块柱 = 0..topSolid 的实心列，
         * 其中桥锚层换成晶体。这里不再做「填洞」后处理。
         */

        // 验收条件一 / 七：前态每列合法，辅助列无洞。
        if (IsRejectedPreState(target, column, tower, out _))
        {
            return null;
        }

        // §7.1：前态的目标列不能等于目标本身。
        if (ShapeSignature.ColumnText(tower, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            return null;
        }

        // 先用最便宜的真实 Pin 筛：Pin 后目标列不逐格等于目标就换候选。
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

        /*
         * 塔的稳定 / 合法性检查放在 Pin 之后：绝大多数候选在 Pin 这一步就被
         * 筛掉，先跑 Collapse 会白花时间；通过 Pin 的候选再补这两条硬闸门，
         * 语义完全相同（不稳定的塔仍然会被拒绝）。
         */
        if (!ShapeOps.IsStable(tower) ||
            !ShapeSignature.AllColumnsLegal(tower, out _))
        {
            return null;
        }

        // 造塔子配方只允许交换链（rank 0），并端到端校验。
        if (!ExchangeChain.TryBuild(tower, out Recipe? subRecipe) ||
            subRecipe is null ||
            subRecipe.Rank != 0 ||
            !Replay.Verify(subRecipe, tower, out _))
        {
            return null;
        }

        // 构造 C（支柱交接）：装新支撑柱、拆旧柱与辅助列。
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

        string preStateTitle = algorithm == AngleAlgorithm.Algorithm5
            ? $"算法 5：底针位置用临时晶体顶住（角列 {towerColumn}），" +
              "另配托针柱/落块柱/引爆柱"
            : $"算法 6：底块提升后的正规化塔 X（角列 {towerColumn}），" +
              "另配托针柱/落块柱/引爆柱";

        var steps = new List<AngleWorkStep>
        {
            new(
                preStateTitle + "（子配方：交换链装配，rank 0）",
                tower,
                AngleWorkStepKind.PreState),

            new(
                "顶针：一次 Pin 把塔整体抬一层——角列底部的临时晶体变成顶针，" +
                "引爆柱顶端超限整簇碎裂，落块柱带着普通格落回目标层",
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
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.C,
            Algorithm = algorithm,
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

    #endregion

    #region 两刀定向碎晶（§7.4：.TT.c）

    /// <summary>
    /// <c>.TT.c</c> 的两刀配方（TODO.md §7.4）：
    /// 算法 2（把缺口填成临时晶体）+ 构造 D ×2（定向碎晶）+ 构造 E（托柱该层换顶针）
    /// + 构造 C（把托柱换成满高柱）。
    ///
    /// <para><b>前态</b>：目标列把缺口下方那枚普通格抬到缺口里（§7.5 规则二），
    /// 让出的位置填成临时晶体；第 1 层也补一枚临时晶体。托柱是满高普通柱，
    /// 唯独在缺口那一层换成顶针（规则一：顶针不参与横向粘连）。</para>
    /// <para><b>第一刀</b>：桥柱第 1 层放晶体，与角列第 1 层的临时晶体连成
    /// 跨刀口的晶体簇；切开后只删掉这枚临时晶体。</para>
    /// <para><b>第二刀</b>：桥柱改到缺口层放晶体，再切一刀删掉缺口层的临时
    /// 晶体；缺口上方那枚普通格失去侧向支撑（托柱同层是顶针），落回目标层。</para>
    /// <para><b>构造 C</b>：把托柱换成满高普通柱，得到目标 S 形态。</para>
    /// <para>
    /// 每一刀都用真实 <see cref="ShapePhysics.Cut"/> 回放；第二刀后目标列
    /// 必须逐格等于目标，托柱的顶针必须仍在原位。任何一步不成立就返回 null，
    /// 由上层退回兜底链。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildTwoCutCascade(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind)
    {
        if (parts != 4 ||
            layers < 3 ||
            target.LayerCount != layers)
        {
            return null;
        }

        int crystalLayer = AngleClassifier.HighestCrystalLayer(
            target,
            column);

        // 只处理「单晶塔，晶体就在顶层，缺口紧贴晶体下沿，
        // 缺口下方叠着普通格」的形态；其它形态留给算法 4/6 的塔式构造。
        if (crystalLayer != layers - 1 ||
            target[crystalLayer - 1, column].Type != CellType.Empty ||
            target[crystalLayer - 2, column].Type != CellType.Shape ||
            target[0, column].Type != CellType.Empty)
        {
            return null;
        }

        for (int layer = 0; layer < layers; layer++)
        {
            if (layer != crystalLayer &&
                target[layer, column].Type == CellType.Crystal)
            {
                return null;
            }

            if (target[layer, column].Type == CellType.Pin)
            {
                return null;
            }
        }

        int gapLayer = crystalLayer - 1;

        char color = CrystalColor(target, column);

        // 前态角列：目标列 + 缺口下方普通格抬升一格 + 两枚临时晶体。
        var scaffold = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            scaffold[layer] = target[layer, column].Type switch
            {
                CellType.Crystal => ShapeSignature.CrystalCode,
                CellType.Shape => ShapeSignature.SolidCode,
                _ => ShapeSignature.EmptyCode
            };
        }

        scaffold[gapLayer - 1] = ShapeSignature.CrystalCode;
        scaffold[gapLayer] = ShapeSignature.SolidCode;
        scaffold[0] = ShapeSignature.CrystalCode;

        // 托柱：满高普通柱，缺口那一层换成顶针（构造 E）。
        var holder = new char[layers];

        Array.Fill(holder, ShapeSignature.SolidCode);

        holder[gapLayer] = ShapeSignature.PinCode;

        // 第一刀的桥柱：第 1 层放晶体。
        string firstBridge =
            ShapeSignature.CrystalCode +
            new string(ShapeSignature.SolidCode, layers - 1);

        int holderColumn = Wrap(column + 1, parts);
        int bridgeColumn = Wrap(column - 1, parts);

        // 顺转到「目标列在第 1 象限、桥柱在第 4 象限」，
        // 跨刀口晶体簇才会被 Cut 整块删除。
        int rotation = Wrap(-column, parts);

        var limitations = BaseLimitations(
            "两刀定向碎晶（算法 2 + 构造 D×2 + 构造 E + 构造 C）：" +
            "前态把缺口下方的普通格抬到缺口里（§7.5 规则二），让出的位置填成临时晶体；" +
            "托柱在缺口那一层换成顶针（规则一：顶针不参与横向粘连）。");

        limitations.Add(
            "第一刀只删掉角列第 1 层的临时晶体；第二刀只删掉缺口层的临时晶体，" +
            "缺口上方的普通格失去侧向支撑后落回目标层。");

        limitations.Add(
            "两刀都保留「目标列 + 托柱」，最后用构造 C 把托柱换成满高普通柱。");

        // ---------- 第一步：第一刀 ----------

        var preState = new ShapeDocument(layers, parts);

        WriteColumn(preState, column, new string(scaffold), color);
        WriteColumn(preState, holderColumn, new string(holder), color);
        WriteColumn(preState, bridgeColumn, firstBridge, color);

        /*
         * 验收条件一（§6 第 1 条）：两刀配方有两条前态，两条都要每列合法。
         * 任一条有非法列就整份候选作废，由上层退回其它路线。
         */
        if (IsRejectedPreState(target, column, preState, out _))
        {
            return null;
        }

        if (!ShapeOps.IsStable(preState) ||
            ShapeSignature.ColumnText(preState, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            return null;
        }

        CutResult cut1 = ShapePhysics.Cut(
            ShapeOps.Rotate(preState, rotation));

        if (!cut1.Success || cut1.West is null)
        {
            return null;
        }

        ShapeDocument afterFirst = ShapeOps.Rotate(
            cut1.West,
            -rotation);

        // 第一刀必须只删掉第 1 层的临时晶体：目标晶体与托柱的顶针都要在。
        if (afterFirst[0, column].Type == CellType.Crystal ||
            afterFirst[crystalLayer, column].Type != CellType.Crystal ||
            afterFirst[gapLayer, holderColumn].Type != CellType.Pin)
        {
            return null;
        }

        var steps = new List<AngleWorkStep>
        {
            new(
                "第一步前态：scaffold（缺口下方的普通格抬升一层）+ " +
                "托柱（缺口层是顶针）+ 桥柱（第 1 层晶体）",
                preState,
                AngleWorkStepKind.PreState),

            new(
                "第一刀：刀口穿过桥柱与第 1 层临时晶体，删掉这枚临时晶体；" +
                "目标列与托柱保留",
                afterFirst,
                AngleWorkStepKind.Cut)
        };

        // ---------- 第二步：重新装桥柱 + 第二刀 ----------

        var secondStage = afterFirst.Clone();

        var secondBridge = new char[layers];

        Array.Fill(secondBridge, ShapeSignature.SolidCode);

        secondBridge[gapLayer - 1] = ShapeSignature.CrystalCode;

        WriteColumn(secondStage, bridgeColumn, new string(secondBridge), color);

        // 第二刀的前态同样要过五项条件（验收条件一）。
        if (IsRejectedPreState(target, column, secondStage, out _))
        {
            return null;
        }

        if (!ShapeOps.IsStable(secondStage))
        {
            return null;
        }

        CutResult cut2 = ShapePhysics.Cut(
            ShapeOps.Rotate(secondStage, rotation));

        if (!cut2.Success || cut2.West is null)
        {
            return null;
        }

        ShapeDocument afterSecond = ShapeOps.Rotate(
            cut2.West,
            -rotation);

        if (!MatchesTargetColumn(
                afterSecond,
                target,
                column,
                layers,
                out _) ||
            !ShapeOps.IsStable(afterSecond))
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            $"第二步前态：重新装桥柱（第 {gapLayer} 层放晶体），其余保持第一刀后的形态",
            secondStage,
            AngleWorkStepKind.PreState));

        steps.Add(new AngleWorkStep(
            "第二刀：刀口打碎缺口层的临时晶体，缺口上方的普通格失去侧向支撑、" +
            "落回目标层",
            afterSecond,
            AngleWorkStepKind.Cut));

        // ---------- 第三步：构造 C ----------

        var handover = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            handover[layer, column] = afterSecond[layer, column].Clone();
        }

        FillPillar(handover, holderColumn);

        if (!ShapeOps.IsStable(handover) ||
            !MatchesTargetColumn(
                handover,
                target,
                column,
                layers,
                out _))
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            "构造 C：把托柱换成满高普通柱，拆掉桥柱，得到目标角（S 形态）",
            handover,
            AngleWorkStepKind.Support));

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
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.D,
            Algorithm = AngleAlgorithm.Algorithm2,
            Rotation = rotation,
            AuxiliaryColumns = new[]
            {
                holderColumn,
                bridgeColumn
            },
            Rank = 0,
            Limitations = limitations
        };
    }

    #endregion

    #region 多刀半破（§15：任意刀数，刀数 = 临时晶体块数）

    /// <summary>
    /// §15 的多刀半破：把目标列的每个内部缺口变成一枚临时晶体块，
    /// <b>每刀正好删掉一个块</b>，由「剩余块集合 K」决定刀数，不枚举轮数。
    ///
    /// <para><b>前态</b>：对目标列每个「正下方是普通格」的内部缺口 g，
    /// 把 <c>g−1</c> 的普通格抬进缺口（<c>A[g] = T</c>），让出的
    /// <c>g−1</c> 填临时晶体。于是 <c>TT.T.c</c> 得到 <c>TcTcTc</c>、
    /// <c>TT.T.T.c</c> 得到 <c>TcTcTcTc</c>、<c>cTT.T.c</c> 得到 <c>cTcTcTc</c>；
    /// 原生晶体仍然坐在普通格上（P1）。</para>
    /// <para><b>每刀</b>：在 <c>column+1</c> 装「满高普通格 + 块顶上一层针」的桥梁柱，
    /// 在 <c>column−1</c> 装「块下方普通格 + 块层晶体」的保险丝柱；真实
    /// <see cref="ShapePhysics.Cut"/> 只碎掉「目标列的临时晶体块 + 保险丝晶体」，
    /// 落定后目标列向上推进一格。</para>
    /// <para><b>轮间</b>：下一块的锚层不同，桥梁柱的针层与保险丝晶体层都要重装。
    /// 这一步用新增的 <see cref="AngleWorkStepKind.Exchange"/> 步表达
    /// （构造 C 的支柱交接：先装新柱，再让新柱接管，最后拆旧柱）。</para>
    /// <para><b>终止</b>：初始 K 就是临时晶体连通块集合；每轮从 K 里删掉
    /// 当前最上面的块，并断言该块层不再有晶体、<c>|K|</c> 严格减 1。
    /// 刀数 == |K|，同一块不会被切第二次。</para>
    /// </summary>
    private static AngleWorkOrder? TryBuildMultiCutHalfBreak(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind)
    {
        if (parts != 4 ||
            layers < 4 ||
            target.LayerCount != layers)
        {
            return null;
        }

        string text = ShapeSignature.ColumnText(target, column);

        if (text.Length != layers)
        {
            return null;
        }

        /*
         * 只接「底部连续两根非空」的形态（TT.T.c / TT.T.T.c / cTT.T.c）：
         * 底格是空或第 2 层是空（T.T.c / .T.T.c / .TT.c）的形态仍走构造 H / G。
         */
        if (text[0] == ShapeSignature.EmptyCode ||
            text[1] == ShapeSignature.EmptyCode)
        {
            return null;
        }

        /*
         * 找「正下方紧贴缺口」的最高晶体段，把它当作本次多刀半破的工作段；
         * 它上面的 T / c 是尾部（例如 TT.T.cTc 的 T6、c7），保持原样即可。
         */
        int topBottom = -1;
        int topLayer = -1;

        for (int layer = layers - 1; layer >= 1; layer--)
        {
            if (text[layer] != ShapeSignature.CrystalCode ||
                text[layer - 1] == ShapeSignature.CrystalCode)
            {
                continue;
            }

            if (text[layer - 1] == ShapeSignature.EmptyCode)
            {
                topBottom = layer;
                topLayer = layer;

                while (topLayer + 1 < layers &&
                       text[topLayer + 1] == ShapeSignature.CrystalCode)
                {
                    topLayer++;
                }

                break;
            }
        }

        if (topBottom < 3)
        {
            return null;
        }

        for (int layer = 0; layer < layers; layer++)
        {
            if (text[layer] == ShapeSignature.PinCode)
            {
                return null;
            }

            if (text[layer] != ShapeSignature.CrystalCode)
            {
                continue;
            }

            int bottom = layer;

            while (bottom > 0 &&
                   text[bottom - 1] == ShapeSignature.CrystalCode)
            {
                bottom--;
            }

            /*
             * 只接「地面晶体段 + 工作段」两种原生晶体：
             * 中间还有别的晶体段的形态（9 层的 T..cT.T.c / T.T.cccTc、
             * TT.T.cTc 的尾部晶体）留给构造 H / D，避免抢走既有路线。
             */
            if (bottom != 0 && bottom != topBottom)
            {
                return null;
            }

            if (bottom != topBottom &&
                bottom > 0 &&
                text[bottom - 1] == ShapeSignature.EmptyCode)
            {
                return null;
            }
        }

        /*
         * 逐空段抬升：空段 [a, b] 正下方必须是普通格；把它抬到空段顶端 b，
         * a−1 … b−1 整段填临时晶体。这样原生晶体坐在普通格上（P1），
         * 每一段临时晶体就是一个「块」，块数就是刀数。
         */
        // 只接「单层缺口」：连续两层空（T..c 一族）留给构造 H / D，
        // 它们的落点区间重叠，不适合按缺口一块一刀。
        for (int layer = 1; layer < topLayer; layer++)
        {
            if (text[layer] == ShapeSignature.EmptyCode &&
                text[layer - 1] == ShapeSignature.EmptyCode)
            {
                return null;
            }
        }

        var pre = text.ToCharArray();
        int segments = 0;

        for (int layer = 1; layer < topLayer; layer++)
        {
            if (pre[layer] != ShapeSignature.EmptyCode ||
                pre[layer - 1] != ShapeSignature.SolidCode)
            {
                continue;
            }

            int hi = layer;

            while (hi + 1 < topLayer &&
                   pre[hi + 1] == ShapeSignature.EmptyCode)
            {
                hi++;
            }

            pre[hi] = ShapeSignature.SolidCode;

            for (int fill = layer - 1; fill < hi; fill++)
            {
                pre[fill] = ShapeSignature.CrystalCode;
            }

            segments++;
        }

        // 至少两个单层缺口才是「多刀」；单缺口仍走构造 H / G。
        if (segments < 2)
        {
            return null;
        }

        /*
         * P1：每个原生晶体段只检查段底——段底必须坐在普通格上；
         * 临时晶体也不能紧贴原生晶体段（纵向融合会误伤目标晶体）。
         */
        for (int layer = 0; layer < layers; layer++)
        {
            if (text[layer] != ShapeSignature.CrystalCode || layer == 0)
            {
                continue;
            }

            if (text[layer - 1] == ShapeSignature.CrystalCode)
            {
                continue;
            }

            if (pre[layer] != ShapeSignature.CrystalCode ||
                pre[layer - 1] != ShapeSignature.SolidCode)
            {
                return null;
            }

            if (layer - 1 >= 0 &&
                pre[layer - 1] == ShapeSignature.CrystalCode)
            {
                return null;
            }

            if (layer + 1 < layers &&
                text[layer + 1] != ShapeSignature.CrystalCode &&
                pre[layer + 1] == ShapeSignature.CrystalCode)
            {
                return null;
            }
        }

        if (!ShapeSignature.IsTightColumn(new string(pre)) ||
            !ShapeSignature.CheckColumn(new string(pre)).IsLegal)
        {
            return null;
        }

        // K：临时晶体连通块（纵向连续的一组临时晶体算一块）。
        var blocks = new List<(int Bottom, int Top)>();

        for (int layer = 0; layer < layers; layer++)
        {
            if (pre[layer] != ShapeSignature.CrystalCode ||
                text[layer] == ShapeSignature.CrystalCode)
            {
                continue;
            }

            int top = layer;

            while (top + 1 < layers &&
                   pre[top + 1] == ShapeSignature.CrystalCode &&
                   text[top + 1] != ShapeSignature.CrystalCode)
            {
                top++;
            }

            blocks.Add((layer, top));
            layer = top;
        }

        if (blocks.Count == 0)
        {
            return null;
        }

        // 按 §15.2：从最上面的块开始，逐块一刀。
        blocks.Sort((left, right) => right.Top.CompareTo(left.Top));

        char color = CrystalColor(target, column);
        int bridgeColumn = Wrap(column + 1, parts);
        int fuseColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);

        var steps = new List<AngleWorkStep>();
        ShapeDocument current = new(layers, parts);

        WriteColumn(current, column, new string(pre), color);

        var remaining = new List<(int Bottom, int Top)>(blocks);
        int knife = 0;

        while (remaining.Count > 0)
        {
            (int blockBottom, int blockTop) = remaining[0];
            remaining.RemoveAt(0);

            // 桥梁柱：满高普通格，块顶上一层放针。
            var bridge = new char[layers];
            Array.Fill(bridge, ShapeSignature.SolidCode);

            int pinLayer = Math.Min(blockTop + 1, layers - 1);
            bridge[pinLayer] = ShapeSignature.PinCode;

            // 保险丝柱：块下方普通格 + 块层晶体，其余空。
            var fuse = new char[layers];
            Array.Fill(fuse, ShapeSignature.EmptyCode);

            for (int layer = 0; layer < blockBottom; layer++)
            {
                fuse[layer] = ShapeSignature.SolidCode;
            }

            for (int layer = blockBottom; layer <= blockTop; layer++)
            {
                fuse[layer] = ShapeSignature.CrystalCode;
            }

            WriteColumn(current, bridgeColumn, new string(bridge), color);
            WriteColumn(current, fuseColumn, new string(fuse), color);

            if (!ShapeSignature.AllColumnsLegal(current, out _) ||
                !ShapeSignature.IsTightColumn(current, bridgeColumn) ||
                !ShapeSignature.IsTightColumn(current, fuseColumn) ||
                !ShapeOps.IsStable(current))
            {
                return null;
            }

            if (knife == 0)
            {
                steps.Add(new AngleWorkStep(
                    $"多刀半破第 1 刀前态：目标列 = {new string(pre)}" +
                    $"（每个缺口下方普通格抬进缺口、让出层填临时晶体）；" +
                    $"K = {blocks.Count} 块，本轮切最上面的块 L{blockBottom + 1}–L{blockTop + 1}",
                    current.Clone(),
                    AngleWorkStepKind.PreState));
            }
            else
            {
                steps.Add(new AngleWorkStep(
                    $"轮间交换（构造 C 支柱交接）：桥梁柱改到第 {pinLayer + 1} 层放针、" +
                    $"保险丝柱改到 L{blockBottom + 1}–L{blockTop + 1} 放晶体，" +
                    $"接管下一块 L{blockBottom + 1}–L{blockTop + 1}",
                    current.Clone(),
                    AngleWorkStepKind.Exchange));
            }

            CutResult cut = ShapePhysics.Cut(
                ShapeOps.Rotate(current, rotation));

            if (!cut.Success || cut.West is null)
            {
                return null;
            }

            ShapeDocument afterCut = ShapeOps.Rotate(
                cut.West,
                -rotation);

            string afterText = ShapeSignature.ColumnText(afterCut, column);

            // 该块必须被删掉；K 严格减 1。
            for (int layer = blockBottom; layer <= blockTop; layer++)
            {
                if (afterText[layer] == ShapeSignature.CrystalCode)
                {
                    return null;
                }
            }

            if (remaining.Count > 0 &&
                CountTemporaryCrystals(afterText, text) >=
                CountTemporaryCrystals(new string(pre), text))
            {
                return null;
            }

            if (!ShapeOps.IsStable(afterCut))
            {
                return null;
            }

            current = afterCut;
            knife++;

            steps.Add(new AngleWorkStep(
                $"第 {knife} 刀（真实 Cut，切完落下）：删掉 L{blockBottom + 1}–L{blockTop + 1} " +
                "的临时晶体块；目标列推进到 " + afterText,
                current.Clone(),
                AngleWorkStepKind.Cut));
        }

        if (ShapeSignature.ColumnText(current, column) != text)
        {
            return null;
        }

        steps.Add(new AngleWorkStep(
            "清除辅助象限，得到目标角",
            target,
            AngleWorkStepKind.Target));

        var limitations = BaseLimitations(
            "多刀半破（§15）：K = 临时晶体连通块集合，刀数 == |K|；" +
            "每刀只删最上面的一个块，轮间用构造 C 交换桥梁柱 / 保险丝柱。");

        limitations.Add(
            $"K = {blocks.Count} 块（层号 1 起：" +
            string.Join("、", blocks.Select(block =>
                $"L{block.Bottom + 1}–L{block.Top + 1}")) +
            "），实际刀数 = " + knife + "；未枚举轮数，K 清空即终止。");

        limitations.Add(
            "初始前态 P1：每颗原生晶体段的段底都坐在普通格上；" +
            "后续轮由上一刀的真实落定结果得到，目标列里目标本身要求的晶下间隙" +
            "（如 TT.T.c 第二刀前态 TcTT.c 的 c@6）与桥梁柱横向粘连、整体稳定，" +
            "且同一块不会被切第二次。");

        return new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.D,
            Algorithm = AngleAlgorithm.Algorithm4,
            Rotation = rotation,
            AuxiliaryColumns = new[] { bridgeColumn, fuseColumn },
            Rank = 0,
            Limitations = limitations
        };
    }

    /// <summary>与目标列逐层比对，数出前态里多出来的临时晶体枚数。</summary>
    private static int CountTemporaryCrystals(
        string column,
        string target)
    {
        int count = 0;

        for (int layer = 0; layer < column.Length; layer++)
        {
            if (column[layer] != ShapeSignature.CrystalCode)
            {
                continue;
            }

            if (layer >= target.Length ||
                target[layer] != ShapeSignature.CrystalCode)
            {
                count++;
            }
        }

        return count;
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
                IsComplete = true,
                TargetColumnHasSubRecipe = true
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
        FillPlanMode mode = FillPlanMode.All,
        bool withHandover = false)
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

        if (withHandover)
        {
            limitations.Add(
                "n ≥ 6 的列级步骤链：PreState → Cut → Collapse → Support → Target。" +
                "n ≥ 6 时 S ⟺ 五项条件（文档 §1.3），本档不使用 n = 4 的 D/F 判据。");

            limitations.Add(
                "Support = 构造 C：先把满高新支撑柱装上，再拆掉托柱/桥柱与辅助材料，" +
                "交接前后目标列逐格不变。");
        }

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

        AngleWorkOrder? illegalPreStateFailure = null;

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
                limitations,
                withHandover);

            if (attempt.IsComplete)
            {
                return attempt;
            }

            /*
             * 记下「前态存在非法列」的候选：即使回放验证还留下更长的
             * 已验证前缀（例如某一段 Cut 通过、另一段缺口对不上），
             * 最终 Note 也必须如实指出这个假阳性来源（TODO.md §7.7）。
             */
            if (attempt.Note is not null &&
                attempt.Note.Contains(
                    IllegalPreStateReason,
                    StringComparison.Ordinal) &&
                illegalPreStateFailure is null)
            {
                illegalPreStateFailure = attempt;
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

        if (illegalPreStateFailure is not null &&
            (bestFailure.Note is null ||
             !bestFailure.Note.Contains(
                 IllegalPreStateReason,
                 StringComparison.Ordinal)))
        {
            bestFailure = AppendFailure(
                bestFailure,
                StripUncoveredPrefix(illegalPreStateFailure.Note!));
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
        List<string> limitations,
        bool withHandover)
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

        /*
         * 验收条件一（§6 第 1 条）：候选前态的每一列都要过五项条件。
         * 不过就整份候选作废，不进入 Cut/Collapse 的回放验证。
         */
        if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
        {
            return Incomplete(
                column,
                kind,
                DescribeIllegalPreState(preState, badPreStateColumn),
                limitations,
                construction: AngleConstruction.FillBreak,
                rotation: rotation,
                auxiliaryColumns: auxiliaryColumns,
                diagnostic: new AngleWorkStep(
                    "非法前态（已作废）：" +
                    ShapeSignature.ColumnText(preState, badPreStateColumn),
                    preState,
                    AngleWorkStepKind.Note));
        }

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

        ShapeDocument collapsedBack =
            ShapeOps.Rotate(rawCollapsed, -rotation);

        /*
         * 验收（§6 第 3 条）：落定后的中间态必须稳定。
         */
        if (!ShapeOps.IsStable(collapsedBack))
        {
            return Incomplete(
                column,
                kind,
                "填充+暴破落定后的中间态不稳定（还会继续落），不能作为停稳的落点。",
                limitations,
                steps,
                AngleConstruction.FillBreak,
                rotation,
                auxiliaryColumns);
        }

        // 「切 + 落」合成一个 Cut 步：Shape 用落定后的真实结果。
        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割（碎晶并落定）：沿 n/2 刀口半破，打碎填充晶体与桥柱晶体，" +
                  "材料落位后缺口由切割造出"
                : $"切割（碎晶并落定）：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "打碎填充晶体与桥柱晶体，材料落位后缺口由切割造出",
            collapsedBack,
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

        if (withHandover)
        {
            /*
             * 构造 C（支柱交接）：n ≥ 6 的列级步骤链要求把辅助列的拆除写成一步。
             * 先装一根满高新支撑柱，再拆掉托柱 / 桥柱与其它辅助材料；
             * 交接前后目标列逐格不变，交接后的图形必须稳定。
             */
            ShapeDocument? handover = BuildSupportHandover(
                collapsedBack,
                target,
                column,
                layers,
                parts,
                out int supportColumn);

            if (handover is null)
            {
                return Incomplete(
                    column,
                    kind,
                    "构造 C 交接失败：相邻两列都撑不住目标列。",
                    limitations,
                    steps,
                    AngleConstruction.FillBreak,
                    rotation,
                    auxiliaryColumns);
            }

            steps.Add(new AngleWorkStep(
                $"构造 C：装第 {supportColumn + 1} 象限的满高新支撑柱，" +
                "拆掉托柱/桥柱与辅助材料，目标列逐格保持",
                handover,
                AngleWorkStepKind.Support));
        }

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
            TargetColumnHasSubRecipe = true,
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

        // 验收条件一（§6 第 1 条）：前态每一列都要过五项条件。
        if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
        {
            return Incomplete(
                column,
                kind,
                DescribeIllegalPreState(preState, badPreStateColumn),
                limitations,
                construction: AngleConstruction.FillBreak,
                rotation: rotation,
                auxiliaryColumns: auxiliaryColumns,
                diagnostic: new AngleWorkStep(
                    "非法前态（已作废）：" +
                    ShapeSignature.ColumnText(preState, badPreStateColumn),
                    preState,
                    AngleWorkStepKind.Note));
        }

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

        // 「切 + 落」合成一个 Cut 步：满高托柱把目标格侧向托住，落定后逐格等于目标。
        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割（碎晶并落定）：沿 n/2 刀口半破，保留目标列与满高托柱，" +
                  "丢弃东侧辅助柱；目标列每一格都由同层托柱侧向托住，不会下落"
                : $"切割（碎晶并落定）：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "保留目标列与满高托柱，丢弃东侧辅助柱；目标列每一格都由同层托柱侧向托住，不会下落",
            collapsedBack,
            AngleWorkStepKind.Cut));

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
            TargetColumnHasSubRecipe = true,
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

    #region n ≥ 6（算法 1 → 算法 2 → 算法 3：构造 F 送回）

    /// <summary>
    /// n ≥ 6 的碎晶角（§2 分档：<c>NeedsCrystalBreak 且 n ≥ 6</c>）：
    /// <b>算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回）</b>。
    ///
    /// <para>
    /// n ≥ 6 时 <b>S ⟺ 五项条件</b>（文档 §1.3），所以这里<b>不</b>套用 n = 4 的 D/F 判据：
    /// 只要五项条件通过、下面三条算法里有一条用真实物理回放通过，就认为做得出。
    /// </para>
    /// <para>
    /// 三条算法输出同一种<b>列级步骤链</b>
    /// <c>PreState → Cut → Collapse → Support → Target</c>：
    /// </para>
    /// <list type="number">
    /// <item><b>PreState</b>：按 §5 四条规则构造前态——辅助列的布局、针位、晶体通道
    /// 都写在步骤标题里，其余象限留空；</item>
    /// <item><b>Cut</b>：旋转到半破轴，真实 <see cref="ShapePhysics.Cut"/>，只打碎辅助列的晶体；</item>
    /// <item><b>Collapse</b>：真实 <see cref="ShapePhysics.Collapse"/>，目标列逐格对齐目标；</item>
    /// <item><b>Support</b>：构造 C——装一根满高新支撑柱、拆掉旧柱与辅助列；</item>
    /// <item><b>Target</b>：清掉辅助象限，得到目标角。</item>
    /// </list>
    /// <para>
    /// 算法 1/2 复用「填充 + 暴破」的两种填法（抬升填 / 直接填），但额外补上
    /// 构造 C 的交接步；算法 3 是 n ≥ 6 专用的构造 F。三条都没过才让调用方退兜底链。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildSixPlusOrder(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        // ── 算法 1（提升）：§5 规则二——缺口下方的普通格抬到缺口顶端，让出的位置填成晶体 ──
        AngleWorkOrder lift = TryBuildFillBreak(
            target,
            column,
            layers,
            parts,
            half,
            kind,
            FillPlanMode.LiftOnly,
            withHandover: true);

        if (lift.IsComplete)
        {
            return Label(lift, AngleAlgorithm.Algorithm1, attempt: true);
        }

        // ── 算法 2（正规核心）：把晶体塔下的缺口直接填成紧核心 ──
        AngleWorkOrder direct = TryBuildFillBreak(
            target,
            column,
            layers,
            parts,
            half,
            kind,
            FillPlanMode.DirectOnly,
            withHandover: true);

        if (direct.IsComplete)
        {
            return Label(direct, AngleAlgorithm.Algorithm2, attempt: true);
        }

        // ── 算法 3（构造 F 送回）：N | A | B | E 四列，一次受控下落到 d 层 ──
        AngleWorkOrder f = TryBuildConstructF(
            target,
            column,
            layers,
            parts,
            half,
            kind);

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
                "未知原因") +
            "；算法 1 的提升填法：" +
            StripUncoveredPrefix(lift.Note ?? "未知原因"));
    }

    /// <summary>
    /// 扫描构造 F 的规范 <c>(s, d)</c> 下落窗口。<b>全部由扫描得出</b>（§10）：
    ///
    /// <list type="bullet">
    /// <item><c>t</c>：某个晶体段的底端（<c>target[t]</c> 是晶体、<c>target[t−1]</c> 不是）；</item>
    /// <item><c>s = t − 1</c>：晶体段正下方那一层，<b>必须是空位</b>（构造 F 的针槽层）；</item>
    /// <item><c>d</c>：<c>s</c> 以下最高的非空层，<b>必须是普通格</b>（构造 F 只送回普通格）。</item>
    /// </list>
    ///
    /// <para>
    /// 从最高晶体段往下扫描，返回<b>最高</b>的合法窗口（规范窗口）；
    /// 没有窗口时返回 null。这里不做「逐窗口回放筛选」。
    /// 层号全部来自扫描，没有「第几层」常量，也没有按 n × 层数的表（§10）。
    /// </para>
    /// </summary>
    private static (int S, int D)? FindCanonicalDropWindow(
        ShapeDocument target,
        int column)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        for (int t = highest; t >= 1; t--)
        {
            if (target[t, column].Type != CellType.Crystal ||
                target[t - 1, column].Type == CellType.Crystal)
            {
                continue;
            }

            int s = t - 1;

            if (target[s, column].Type != CellType.Empty)
            {
                continue;
            }

            int d = -1;

            for (int layer = s - 1; layer >= 0; layer--)
            {
                if (target[layer, column].Type != CellType.Empty)
                {
                    d = layer;

                    break;
                }
            }

            if (d < 0 || target[d, column].Type != CellType.Shape)
            {
                continue;
            }

            return (s, d);
        }

        return null;
    }

    /// <summary>
    /// 算法 3（n ≥ 6）：构造 F 受控下落（文档 §3 的 F 原语）。
    ///
    /// <para><b>四列布局</b>（沿环四个连续位置，其余象限留空，三根柱子从地面做到画布高度）：</para>
    /// <code>
    /// 针槽柱 N | 目标 A | 辅助柱 B   | 碎晶桥柱 E
    /// 第 s 层针 | 目标列 | 第 d..s−1 层晶体 | 第 d 层晶体
    /// 其余 T   |        | 其余 T          | 其余 T
    /// </code>
    /// <para><b>为什么这样摆</b>（§5 四条规则）：</para>
    /// <list type="number">
    /// <item><b>规则二</b>：先把 d 层那枚普通格<b>抬到</b> s 层（晶体段正下方的缺口顶端），
    /// d 层让出落点。于是 B 的填充晶体通道与目标晶体塔隔着这一层位移、不会纵向融合，
    /// 切开时只删 B、不误伤 A 的晶体塔。</item>
    /// <item><b>规则一</b>：N 的<b>第 s 层换成顶针</b>——顶针不参与横向粘连，s 层那格
    /// 不与 N 粘住；同时针把 N 分成「下段接地」「上段托住晶体塔」两截，
    /// A 的晶体塔靠同层侧向粘连挂在 N 上，不会跟着下落。</item>
    /// <item><b>规则三</b>：半破轴取 <c>p = half − 2</c>，保留侧正好是 <c>N | A | B</c>
    /// 三列（n ≥ 6 的半边容得下三列），E 落在刀口另一侧。</item>
    /// <item><b>规则四</b>：前态的目标列是「d 层普通格抬到 s 层」的形态，与目标列不同。</item>
    /// </list>
    /// <para><b>落点</b>：切开后 B 的晶体通道整段碎掉，A[s] 与 B[s] 的两枚普通格粘成
    /// 双格一起下落；B 的第 d−1 层（0 起）普通格当刹车，双格正好停在 d 层，
    /// 也就是目标列里那枚普通格原来的位置。</para>
    /// <para>
    /// <c>s</c> 与 <c>d</c> 一律来自 <see cref="FindCanonicalDropWindow"/> 的扫描结果，
    /// 没有任何「第几层」常量，也没有按 n × 层数的表（§10）。
    /// </para>
    /// </summary>
    private static AngleWorkOrder TryBuildConstructF(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        if (parts < 6 || half < 3)
        {
            return Incomplete(
                column,
                kind,
                "构造 F 至少要 6 个象限：要留出 N | A | B | E 四个连续位置，" +
                "半破的保留侧还要容得下 N | A | B 三列。",
                construction: AngleConstruction.F);
        }

        var limitations = BaseLimitations(
            "构造 F（n ≥ 6）：沿环四个连续位置摆「针槽柱 N | 目标 A | 辅助柱 B | " +
            "碎晶桥柱 E」，其余象限留空；Cut 沿半破轴 p = half − 2 切开，" +
            "保留侧恰好是 N | A | B。");

        limitations.Add(
            "N 的第 s 层是顶针（顶针不参与横向粘连），B 的第 d..s−1 层是碎晶用的晶体通道，" +
            "E 只有第 d 层一枚晶体；三根柱子都从地面做到画布高度。");

        limitations.Add(
            "切开后 B 的晶体通道整段碎掉，A[s] 与 B[s] 粘成双格一起下落，" +
            "B 的第 d−1 层普通格当刹车，双格停在 d 层；最后构造 C 交接回普通柱。");

        limitations.Add(
            "n ≥ 6 时 S ⟺ 五项条件（文档 §1.3），本档不使用 n = 4 的 D/F 判据。");

        const string noWindow =
            "该列没有可用的 (s, d) 下落窗口：每个晶体段底端的正下方都要是空位，" +
            "且 s 以下最高的非空层必须是普通格。";

        /*
         * 规范窗口：扫描出的最高合法窗口。构造 F 只按这一个窗口装前态，
         * 不再「逐个窗口回放筛选」——窗口严格由扫描给出，失败就如实报未覆盖。
         */
        (int S, int D)? window = FindCanonicalDropWindow(target, column);

        if (window is null)
        {
            return Incomplete(
                column,
                kind,
                noWindow,
                limitations,
                construction: AngleConstruction.F);
        }

        (int s, int d) = window.Value;

        AngleWorkOrder? candidate = TryConstructFCandidate(
            target,
            column,
            layers,
            parts,
            half,
            kind,
            s,
            d,
            limitations);

        return candidate ?? Incomplete(
            column,
            kind,
            "构造 F 的最高下落窗口（第 " + (s + 1) + " 层 → 第 " + (d + 1) + " 层）" +
            "装出的前态不成立（象限布局冲突），无法回放。",
            limitations,
            construction: AngleConstruction.F);
    }

    /// <summary>
    /// 按给定的 <c>(s, d)</c> 装出一份构造 F 的前态，再用真实物理把
    /// <c>PreState → Cut → Collapse → Support → Target</c> 整条链回放并逐格核对。
    /// 任何一条验收不过就返回带 Note 的未完成单（可能是 null 之外的空候选）。
    /// </summary>
    private static AngleWorkOrder? TryConstructFCandidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind,
        int s,
        int d,
        List<string> limitations)
    {
        char color = CrystalColor(target, column);

        int nColumn = Wrap(column - 1, parts);
        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);

        if (nColumn == column || bColumn == column || eColumn == column ||
            eColumn == nColumn || eColumn == bColumn || nColumn == bColumn)
        {
            return null;
        }

        // ── 前态：按 §5 的规则逐列摆出来（不是「照抄目标 + 加柱子」）──

        var preState = new ShapeDocument(layers, parts);

        int copyLayers = Math.Min(layers, target.LayerCount);

        for (int layer = 0; layer < copyLayers; layer++)
        {
            preState[layer, column] = target[layer, column].Clone();
        }

        /*
         * 规则二（抬升）：把 d 层那枚普通格搬到 s 层（晶体段正下方），d 层让出落点。
         * 这一步同时保证规则四：前态的目标列不再等于目标列。
         */
        preState[s, column] = target[d, column].Clone();
        preState[d, column] = ShapeCell.Empty();

        // 针槽柱 N：第 s 层为针，其余普通格（规则一：顶针不参与横向粘连）。
        FillPillar(preState, nColumn);
        preState[s, nColumn] = ShapeCell.Pin();

        // 辅助柱 B：第 d..s−1 层为晶体通道，其余普通格。
        FillPillar(preState, bColumn);

        for (int layer = d; layer <= s - 1; layer++)
        {
            preState[layer, bColumn] = ShapeCell.Crystal(color);
        }

        // 碎晶桥柱 E：只有第 d 层一枚晶体，其余普通格。
        FillPillar(preState, eColumn);
        preState[d, eColumn] = ShapeCell.Crystal(color);

        var auxiliaryColumns = new[] { nColumn, bColumn, eColumn };

        // ── 验收一：前态每一列都过五项条件 ──

        if (IsRejectedPreState(target, column, preState, out int badColumn))
        {
            return Incomplete(
                column,
                kind,
                DescribeIllegalPreState(preState, badColumn),
                limitations,
                construction: AngleConstruction.F,
                auxiliaryColumns: auxiliaryColumns,
                diagnostic: new AngleWorkStep(
                    "非法前态（已作废）：" +
                    ShapeSignature.ColumnText(preState, badColumn),
                    preState,
                    AngleWorkStepKind.Note));
        }

        // ── 验收二：前态的目标列 ≠ 目标列（§5 规则四 / §7.1 循环闸门）──

        if (IsCyclicPreState(preState, target, column, hasLift: true) ||
            preState[s, column].Type == CellType.Empty ||
            preState[d, column].Type != CellType.Empty)
        {
            return null;
        }

        string preStateTitle =
            $"构造 F 的前态：环形四连位 N（第 {s + 1} 层顶针）| A | " +
            $"B（第 {d + 1}–{s} 层晶体通道）| E（第 {d + 1} 层晶体），其余象限留空；" +
            $"目标列把第 {d + 1} 层的普通格抬到第 {s + 1} 层、让出落点";

        var steps = new List<AngleWorkStep>
        {
            new(preStateTitle, preState, AngleWorkStepKind.PreState)
        };

        // ── 验收三（前态）：前态稳定 ──

        if (!ShapeOps.IsStable(preState))
        {
            return Incomplete(
                column,
                kind,
                "构造 F 的前态不稳定（一次下落后会变形），后续碎晶/下落验证无法进行。",
                limitations,
                steps,
                AngleConstruction.F,
                auxiliaryColumns: auxiliaryColumns);
        }

        // ── 半破轴（规则三）：p = half − 2，保留侧 N | A | B，E 在刀口另一侧 ──

        int p = half - 2;
        int rotation = Wrap(p - column, parts);
        int pRotated = Wrap(p, parts);
        int nRotated = Wrap(nColumn + rotation, parts);
        int bRotated = Wrap(bColumn + rotation, parts);
        int eRotated = Wrap(eColumn + rotation, parts);

        bool layoutOk =
            nRotated == Wrap(p - 1, parts) &&
            bRotated == Wrap(p + 1, parts) &&
            eRotated == half &&
            nRotated < half &&
            pRotated < half &&
            bRotated < half;

        if (!layoutOk)
        {
            return Incomplete(
                column,
                kind,
                "构造 F 的刀口布局不对：保留侧必须同时含 N | A | B（规则三）。",
                limitations,
                steps,
                AngleConstruction.F,
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
                "构造 F 的 ShapePhysics.Cut 失败：" +
                (realCut.Error ?? "未知原因"),
                limitations,
                steps,
                AngleConstruction.F,
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
                "构造 F 的结构性半破与 ShapePhysics.Cut 的结果对不上，不能保证 Cut 步是真实的。",
                limitations,
                steps,
                AngleConstruction.F,
                rotation,
                auxiliaryColumns);
        }

        // ── 验收四（Cut）：只打碎 B 的晶体通道与 E 的桥晶体，A 的晶体塔 / N 的针不能动 ──

        bool targetKept = true;

        for (int layer = 0; layer < layers; layer++)
        {
            if (!ShapeOps.SameCell(
                    rotated[layer, pRotated],
                    rawWest[layer, pRotated]))
            {
                targetKept = false;

                break;
            }
        }

        bool channelBuilt = true;
        bool channelGone = true;

        for (int layer = d; layer <= s - 1; layer++)
        {
            if (rotated[layer, bRotated].Type != CellType.Crystal)
            {
                channelBuilt = false;
            }

            if (rawWest[layer, bRotated].Type == CellType.Crystal)
            {
                channelGone = false;
            }
        }

        bool bridgeBuilt =
            rotated[d, eRotated].Type == CellType.Crystal;

        bool bridgeBroken =
            realCut.East is not null &&
            realCut.East[d, eRotated].Type != CellType.Crystal;

        bool pinKept =
            rotated[s, nRotated].Type == CellType.Pin &&
            rawWest[s, nRotated].Type == CellType.Pin;

        // s 层那两枚普通格的正下方必须真空出来，双格才落得下去。
        bool fellThrough =
            rawWest[s - 1, pRotated].Type == CellType.Empty &&
            rawWest[s - 1, bRotated].Type == CellType.Empty;

        if (!targetKept ||
            !channelBuilt ||
            !channelGone ||
            !bridgeBuilt ||
            !bridgeBroken ||
            !pinKept ||
            !fellThrough)
        {
            return Incomplete(
                column,
                kind,
                "构造 F 的切割没有只打碎 B 的晶体通道（或误伤了 A 的晶体塔 / N 的顶针），" +
                "因此不保留 Cut 步。",
                limitations,
                steps,
                AngleConstruction.F,
                rotation,
                auxiliaryColumns);
        }

        // ── 验收五：落定后逐格等于目标；中间态稳定 ──

        ShapeDocument collapsedBack = ShapeOps.Rotate(rawCollapsed, -rotation);

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
                "构造 F 的下落结果与目标列不符：" + difference,
                limitations,
                steps,
                AngleConstruction.F,
                rotation,
                auxiliaryColumns,
                new AngleWorkStep(
                    "构造 F 的下落结果（未通过验证，仅供参考）",
                    collapsedBack,
                    AngleWorkStepKind.Note));
        }

        if (!ShapeOps.IsStable(collapsedBack))
        {
            return Incomplete(
                column,
                kind,
                "构造 F 落定后的中间态不稳定（还会继续落），不能作为停稳的落点。",
                limitations,
                steps,
                AngleConstruction.F,
                rotation,
                auxiliaryColumns);
        }

        // 「切 + 落」合成一个 Cut 步：Shape 用落定后的真实结果。
        steps.Add(new AngleWorkStep(
            (rotation == 0
                ? "切割（碎晶并落定）：沿半破轴（保留侧 N | A | B）切一刀，"
                : $"切割（碎晶并落定）：先顺转 {rotation} 个象限，再沿半破轴（保留侧 N | A | B）切一刀，") +
            $"打碎 B 的晶体通道与 E 的桥晶体；A 第 {s + 1} 层的普通格与 B 的同层普通格" +
            $"粘成双格一起下落，被 B 第 {d} 层的普通格（刹车）托住，双格停在第 {d + 1} 层",
            collapsedBack,
            AngleWorkStepKind.Cut));

        // ── 验收六（Support）：构造 C 交接回普通柱，拆掉 N | B | E ──

        ShapeDocument? handover = BuildSupportHandover(
            collapsedBack,
            target,
            column,
            layers,
            parts,
            out int supportColumn);

        if (handover is null)
        {
            return Incomplete(
                column,
                kind,
                "构造 C 交接失败：相邻两列都撑不住目标列。",
                limitations,
                steps,
                AngleConstruction.F,
                rotation,
                auxiliaryColumns);
        }

        steps.Add(new AngleWorkStep(
            $"构造 C：装第 {supportColumn + 1} 象限的满高新支撑柱，" +
            "拆掉 N | B | E 三根辅助柱，目标列逐格保持",
            handover,
            AngleWorkStepKind.Support));

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
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.F,
            Algorithm = AngleAlgorithm.Algorithm3,
            Rotation = rotation,
            AuxiliaryColumns = auxiliaryColumns,
            Rank = 0,
            Limitations = limitations
        };
    }

    /// <summary>
    /// 构造 C（支柱交接）：只保留目标列，装一根满高新支撑柱，拆掉旧柱与全部辅助列。
    ///
    /// <para>
    /// 优先挑「现在还不是满高普通柱」的那一侧——那才是真正的「装新柱、拆旧柱」；
    /// 两侧都已经是满高柱时退而求其次。交接后的图形必须稳定、目标列逐格不变。
    /// </para>
    /// </summary>
    private static ShapeDocument? BuildSupportHandover(
        ShapeDocument collapsed,
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        out int supportColumn)
    {
        var candidates = new[]
        {
            Wrap(column - 1, parts),
            Wrap(column + 1, parts)
        };

        foreach (bool requireNewPillar in new[] { true, false })
        {
            foreach (int candidate in candidates)
            {
                if (requireNewPillar &&
                    IsFullPillar(collapsed, candidate))
                {
                    continue;
                }

                var handover = new ShapeDocument(layers, parts);

                for (int layer = 0; layer < layers; layer++)
                {
                    handover[layer, column] =
                        collapsed[layer, column].Clone();
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
        }

        supportColumn = -1;

        return null;
    }

    /// <summary>某一列是否整列都是普通格（满高普通柱）。</summary>
    private static bool IsFullPillar(
        ShapeDocument document,
        int column)
    {
        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            if (document[layer, column].Type != CellType.Shape)
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region 构造 G / F（切割路径）

    /// <summary>
    /// 构造 G（单层）或 F（n ≥ 6）的切割路径：前态 → Cut（碎晶并落定）→ Target。
    /// </summary>
    /// <summary>
    /// 把目标列里<b>与某个空层连通的那一段空洞</b>填成同层临时晶体，得到紧列。
    ///
    /// <para>
    /// 只填 <paramref name="anchor"/> 所在的连续空段（上下都被材料夹住的那一段），
    /// 不碰目标自己的其它洞：G/F 的刀口只经过闸门所在的那一段临时晶体，
    /// 远处的洞（例如最底晶体下方的「普通资源洞」）填了也切不掉，只会粘在原生晶体上。
    /// </para>
    /// <para>
    /// 颜色取目标列最高晶体的颜色；填出来的临时晶体必须能被同一刀/顶针碎掉，
    /// 否则候选会在真实回放里被逐格对比淘汰。
    /// </para>
    /// </summary>
    private static ShapeDocument FillTargetHoleRun(
        ShapeDocument document,
        ShapeDocument target,
        int column,
        int anchor)
    {
        var result = new ShapeDocument(document.LayerCount, document.PartCount);

        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            for (int part = 0; part < document.PartCount; part++)
            {
                result[layer, part] = document[layer, part].Clone();
            }
        }

        if (anchor < 0 ||
            anchor >= document.LayerCount ||
            document[anchor, column].Type != CellType.Empty)
        {
            return result;
        }

        int bottom = anchor;
        int top = anchor;

        while (bottom > 0 &&
               document[bottom - 1, column].Type == CellType.Empty)
        {
            bottom--;
        }

        while (top + 1 < document.LayerCount &&
               document[top + 1, column].Type == CellType.Empty)
        {
            top++;
        }

        char color = CrystalColor(target, column);

        for (int layer = bottom; layer <= top; layer++)
        {
            result[layer, column] = ShapeCell.Crystal(color);
        }

        return result;
    }

    /// <summary>
    /// 判断「把目标列的洞填成临时晶体」后，这一刀是否仍逐格得到目标：
    /// 前态每列合法、稳定，真实 Cut 的西半边再转回后目标列等于目标。
    /// </summary>
    private static bool TightPreStateWorks(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int rotation,
        ShapeDocument preState)
    {
        if (!ShapeSignature.AllColumnsLegal(preState, out _) ||
            !ShapeSignature.IsTightColumn(preState, column) ||
            !ShapeOps.IsStable(preState) ||
            ShapeSignature.ColumnText(preState, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            return false;
        }

        for (int part = 0; part < parts; part++)
        {
            if (part != column &&
                !ShapeSignature.IsTightColumn(preState, part))
            {
                return false;
            }
        }

        ShapeDocument rotated = ShapeOps.Rotate(preState, rotation);

        ShapeDocument rawWest = CutWestBeforeCollapse(rotated, half);

        CutResult realCut = ShapePhysics.Cut(rotated);

        if (!realCut.Success || realCut.West is null)
        {
            return false;
        }

        ShapeDocument rawCollapsed = ShapePhysics.Collapse(rawWest).Document!;

        if (!ShapeOps.Same(rawCollapsed, realCut.West))
        {
            return false;
        }

        ShapeDocument back = ShapeOps.Rotate(rawCollapsed, -rotation);

        return MatchesTargetColumn(
            back,
            target,
            column,
            layers,
            out _);
    }

    /// <summary>
    /// 把目标列里剩余的洞（最底晶体以下、或晶体之间）也填紧：
    /// 把洞正下方最近的普通格<b>抬到洞里</b>，它让出的那一层填同色临时晶体，
    /// 临时晶体放在环缝另一侧的保险丝列，切割时跨环缝碎掉；普通格随后落回原层，
    /// 洞就造出来了。
    ///
    /// <para>
    /// 典型例子：<c>T.cccT..Tccc</c>（洞在第 2 层）→ 把地面普通格抬到第 2 层、
    /// 地面填临时晶体，得到 <c>cTcccTccTccc</c>；一刀之后普通格落回地面，目标列的
    /// 第 2 层重新变成洞。抬升的必须是普通格（不是目标晶体/针），否则放弃填紧。
    /// </para>
    /// </summary>
    /// <returns>填紧后的列；无法安全抬升时返回 null。</returns>
    private static string? LiftTargetHoles(
        string preTarget,
        string targetColumn,
        out List<int> fuseLayers,
        out List<int> holeLayers)
    {
        fuseLayers = new List<int>();
        holeLayers = new List<int>();

        char[] result = preTarget.ToCharArray();

        /*
         * 从高到低处理剩余的目标洞（晶体塔下的「普通资源洞」、晶体之间的洞等）：
         * 只有「洞正下方最近的非空格是普通格，且它在目标里也是普通格」才抬升。
         * 若源格是晶体 / 针，放弃填紧（不能为了填洞搬走目标原生材料）。
         */
        for (int layer = preTarget.Length - 1; layer >= 0; layer--)
        {
            if (result[layer] != ShapeSignature.EmptyCode ||
                targetColumn[layer] != ShapeSignature.EmptyCode)
            {
                continue;
            }

            bool materialAbove = false;

            for (int above = layer + 1; above < preTarget.Length; above++)
            {
                if (result[above] != ShapeSignature.EmptyCode)
                {
                    materialAbove = true;
                    break;
                }
            }

            if (!materialAbove)
            {
                continue;
            }

            int source = -1;

            for (int below = layer - 1; below >= 0; below--)
            {
                if (result[below] != ShapeSignature.EmptyCode)
                {
                    source = below;
                    break;
                }
            }

            if (source < 0 ||
                result[source] != ShapeSignature.SolidCode ||
                targetColumn[source] != ShapeSignature.SolidCode)
            {
                return null;
            }

            result[layer] = result[source];
            result[source] = ShapeSignature.CrystalCode;
            fuseLayers.Add(source);
            holeLayers.Add(layer);
        }

        return new string(result);
    }

    /// <summary>
    /// 构造 G（单层）或 F（n ≥ 6）的切割路径：前态 → Cut（碎晶并落定）→ Target。
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
        if (construction != AngleConstruction.G)
        {
            return BuildCutPathCandidate(
                target,
                column,
                layers,
                parts,
                half,
                s,
                d,
                kind,
                construction,
                twoAuxiliaryColumns: false);
        }

        AngleWorkOrder twoAux = BuildCutPathCandidate(
            target,
            column,
            layers,
            parts,
            half,
            s,
            d,
            kind,
            construction,
            twoAuxiliaryColumns: true);

        if (twoAux.IsComplete)
        {
            return AddLimitation(
                twoAux,
                "辅助列最少化：只保留支撑列 B 与保险丝列 N（c + 中间普通格 + 闸门晶体），" +
                "第三列整列为空；真实 Cut + 落定逐格核对通过。");
        }

        AngleWorkOrder threeAux = BuildCutPathCandidate(
            target,
            column,
            layers,
            parts,
            half,
            s,
            d,
            kind,
            construction,
            twoAuxiliaryColumns: false);

        if (threeAux.IsComplete)
        {
            return AddLimitation(
                threeAux,
                "两列辅助（支撑 + 保险丝）在该形态上回放不通过" +
                "（闸门晶体需要中间桥柱才能与目标列跨刀口同簇），" +
                "因此退回三列辅助：支撑 + 满高桥柱 + 保险丝。");
        }

        return AppendFailure(
            twoAux,
            "三列辅助也未通过：" +
            StripUncoveredPrefix(threeAux.Note ?? "未知原因"));
    }

    private static AngleWorkOrder BuildCutPathCandidate(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        int s,
        int d,
        AngleBuildKind kind,
        AngleConstruction construction,
        bool twoAuxiliaryColumns)
    {
        /*
         * 目标列的洞先在「前态」里填成同层临时晶体（紧列），
         * 再让这一刀把临时晶体与闸门/保险丝一起碎掉。
         *
         * 这一步只作为候选：填紧后必须真实 Cut + Collapse 仍逐格等于目标列，
         * 才采用；否则保留原前态（兼容其它形态）。
         */
        int p = half - 2;
        int rotation = Wrap(p - column, parts);

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
            twoAuxiliaryColumns,
            out int bColumn,
            out int eColumn,
            out int nColumn);

        /*
         * 辅助列最少化：G 优先「支撑 B + 保险丝 N」两列（E 留空）；
         * 回放不通过时退回「支撑 B + 满高桥柱 E」三列（N 留空）。
         * F 的 E 是跨刀口的桥柱，必须保留，因此仍是三列。
         */
        var auxiliaryColumns = construction == AngleConstruction.G
            ? (twoAuxiliaryColumns
                ? new[] { bColumn, nColumn }
                : new[] { bColumn, eColumn })
            : new[] { nColumn, bColumn, eColumn };

        bool filledTargetHoles = false;

        if (ShapeSignature.IsLegalColumn(target, column))
        {
            ShapeDocument tightPreState = FillTargetHoleRun(
                preState,
                target,
                column,
                d);

            char crystalColor = CrystalColor(target, column);

            // 最底晶体以下的洞：抬升普通格、让出的层放临时晶体（保险丝层）。
            string? lifted = LiftTargetHoles(
                ShapeSignature.ColumnText(tightPreState, column),
                ShapeSignature.ColumnText(target, column),
                out List<int> lowerFuseLayers,
                out List<int> lowerHoleLayers);

            if (lifted is not null)
            {
                WriteColumn(tightPreState, column, lifted, crystalColor);

                if (lowerHoleLayers.Count > 0)
                {
                    /*
                     * 下方洞的临时晶体放在「环缝另一侧」的保险丝列，
                     * 目标列与它跨 3–0 环缝横向相连，一刀即碎。
                     *
                     * B 列的底部整段放针：针不与目标列的普通格横向粘连，
                     * 抬升的普通格才能落回；B 上方的普通格仍托住目标列的原生晶体。
                     */
                    int maxHole = lowerHoleLayers.Max();
                    int maxSource = lowerFuseLayers.Max();

                    for (int layer = 0; layer <= maxHole; layer++)
                    {
                        tightPreState[layer, bColumn] = ShapeCell.Pin();
                    }

                    if (construction == AngleConstruction.G &&
                        twoAuxiliaryColumns)
                    {
                        /*
                         * G 的保险丝列：0..max(d, 洞层, 让出层) 全是普通格（紧列），
                         * 让出层与闸门层 d 各放一颗晶体。整列在切割时被丢弃，
                         * 不再需要顶针（顶针会在紧列里造洞），
                         * 形态即用户实测的「c + 中间 T + 顶层 c」。
                         */
                        int supportTop = Math.Max(d, Math.Max(maxHole, maxSource));

                        for (int layer = 0;
                             layer <= supportTop && layer < layers;
                             layer++)
                        {
                            tightPreState[layer, nColumn] = ShapeCell.Shape('C', 'u');
                        }

                        foreach (int fuse in lowerFuseLayers)
                        {
                            tightPreState[fuse, nColumn] =
                                ShapeCell.Crystal(crystalColor);
                        }

                        tightPreState[d, nColumn] =
                            ShapeCell.Crystal(crystalColor);
                    }
                    else
                    {
                        int supportTop = Math.Max(maxHole, maxSource);

                        for (int layer = 0; layer <= supportTop; layer++)
                        {
                            tightPreState[layer, nColumn] = ShapeCell.Shape('C', 'u');
                        }

                        foreach (int fuse in lowerFuseLayers)
                        {
                            tightPreState[fuse, nColumn] =
                                ShapeCell.Crystal(crystalColor);
                        }

                        foreach (int hole in lowerHoleLayers)
                        {
                            tightPreState[hole, nColumn] = ShapeCell.Pin();
                        }
                    }
                }
            }

            bool tightWorks = TightPreStateWorks(
                target,
                column,
                layers,
                parts,
                half,
                rotation,
                tightPreState);

            if (!ShapeOps.Same(tightPreState, preState) &&
                tightWorks)
            {
                preState = tightPreState;
                filledTargetHoles = true;
            }
        }

        /*
         * 验收条件一（§6 第 1 条）：G/F 的候选前态也必须每一列都过五项条件。
         */
        if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
        {
            return Incomplete(
                column,
                kind,
                DescribeIllegalPreState(preState, badPreStateColumn),
                limitations,
                construction: construction,
                rotation: rotation,
                auxiliaryColumns: auxiliaryColumns,
                diagnostic: new AngleWorkStep(
                    "非法前态（已作废）：" +
                    ShapeSignature.ColumnText(preState, badPreStateColumn),
                    preState,
                    AngleWorkStepKind.Note));
        }

        if (filledTargetHoles)
        {
            limitations.Add(
                "目标列的空洞已填成同层临时晶体（前态是紧列）；" +
                "这一刀把临时晶体与闸门 / 保险丝同簇碎掉，普通格只降一层。");
        }

        var steps = new List<AngleWorkStep>
        {
            new(
                filledTargetHoles
                    ? "碎晶前的角（带辅助象限；目标列的洞已填成临时晶体）"
                    : "碎晶前的角（带辅助象限）",
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

        /*
         * 目标列的原生晶体必须一颗不少；填洞用的临时晶体必须被这一刀碎干净。
         * 不能再用「前态里的每颗晶体都要保留」——前态现在可能含临时晶体。
         */
        bool targetCrystalsKept = true;
        bool temporaryCrystalsGone = true;

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            bool targetCrystal = target[layer, column].Type == CellType.Crystal;
            bool beforeCrystal = rotated[layer, p].Type == CellType.Crystal;
            bool afterCrystal = rawWest[layer, p].Type == CellType.Crystal;

            if (targetCrystal && !afterCrystal)
            {
                targetCrystalsKept = false;
            }

            if (!targetCrystal && beforeCrystal && afterCrystal)
            {
                temporaryCrystalsGone = false;
            }
        }

        if (!hadBridgeCrystals ||
            !bridgeCrystalsGone ||
            !targetCrystalsKept ||
            !temporaryCrystalsGone)
        {
            return Incomplete(
                column,
                kind,
                "切割没有按预期打碎桥柱晶体（或误伤了目标列晶体、" +
                "临时晶体没有碎干净），因此不保留 Cut 步。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
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

        if (!ShapeOps.IsStable(collapsedBack))
        {
            return Incomplete(
                column,
                kind,
                "切割（碎晶并落定）后的中间态不稳定（还会继续落），不能作为停稳的落点。",
                limitations,
                steps,
                construction,
                rotation,
                auxiliaryColumns);
        }

        /*
         * 「切 + 落」是同一个操作节点的两步物理：合成一个 Cut 步，
         * Shape 用真实 Cut 的最终态（已落定），不再单独输出 Collapse 步。
         */
        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割（碎晶并落定）：沿 n/2 刀口半破，打碎桥柱晶体后材料落位，" +
                  "缺口移到晶体塔正下方"
                : $"切割（碎晶并落定）：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "打碎桥柱晶体后材料落位，缺口移到晶体塔正下方",
            collapsedBack,
            AngleWorkStepKind.Cut));

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
            TargetColumnHasSubRecipe = true,
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

        /*
         * 目标列的洞也填成同层临时晶体（紧列）：临时晶体与 B 的晶体桥、
         * E 的满高晶体柱连成一簇，顶针时 E 顶端超限碎裂会把它一起带走，
         * 顶针结果必须与原前态逐格相同才采用。
         */
        bool filledTargetHoles = false;

        if (ShapeSignature.IsLegalColumn(target, column))
        {
            ShapeDocument tightPreState = FillTargetHoleRun(
                preState,
                target,
                column,
                d - 1);

            if (!ShapeOps.Same(tightPreState, preState) &&
                ShapeSignature.AllColumnsLegal(tightPreState, out _) &&
                ShapeOps.IsStable(tightPreState))
            {
                PhysicsResult normalPin = ShapePhysics.Pin(preState);
                PhysicsResult tightPin = ShapePhysics.Pin(tightPreState);

                if (normalPin.Success &&
                    tightPin.Success &&
                    normalPin.Document is not null &&
                    tightPin.Document is not null &&
                    ShapeSignature.ColumnText(tightPin.Document, column) ==
                    ShapeSignature.ColumnText(normalPin.Document, column))
                {
                    preState = tightPreState;
                    filledTargetHoles = true;
                }
            }
        }

        // 验收条件一（§6 第 1 条）：构造 H 的候选前态也必须每列合法。
        if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
        {
            return Incomplete(
                column,
                kind,
                DescribeIllegalPreState(preState, badPreStateColumn),
                limitations,
                construction: AngleConstruction.H,
                rotation: 0,
                auxiliaryColumns: auxiliaryColumns,
                diagnostic: new AngleWorkStep(
                    "非法前态（已作废）：" +
                    ShapeSignature.ColumnText(preState, badPreStateColumn),
                    preState,
                    AngleWorkStepKind.Note));
        }

        if (filledTargetHoles)
        {
            limitations.Add(
                "目标列的空洞已填成同层临时晶体（前态是紧列）；" +
                "它与 B 的晶体桥、E 的满高晶体柱同簇，顶针时一起碎掉。");
        }

        var steps = new List<AngleWorkStep>
        {
            new(
                filledTargetHoles
                    ? "碎晶前的角（带辅助象限，按顶针后层号对齐；目标列的洞已填成临时晶体）"
                    : "碎晶前的角（带辅助象限，按顶针后层号对齐）",
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

        return AttachQ1SubRecipe(new AngleWorkOrder(
            column,
            kind,
            steps,
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.H,
            Rotation = 0,
            AuxiliaryColumns = auxiliaryColumns,
            Limitations = limitations
        });
    }

    #endregion

    #region 构造 H（构造性生成：按 I/J 算，不枚举）

    /// <summary>
    /// 构造性构造 H（docs §18.4）：由目标列<b>扫描</b>出四列 <c>A | B | E | N</c>，
    /// 再用真实 <see cref="ShapePhysics.Pin"/> 回放验收。
    /// <b>不枚举候选、不靠搜索凑</b>：失败就返回 null，由调用方如实报未覆盖。
    ///
    /// <para><b>两种确定性形态（先后各试一次，不枚举参数）</b>：</para>
    /// <list type="number">
    /// <item><b>带下降通道</b>：每段晶体（连续 <c>c</c>）的正下方为空 ⇒ 这个空段是
    /// 「通道」：通道最下面一格普通格是源（落点 <c>d</c>），通道顶端是源层
    /// <c>s = 空段顶</c>；把源格提到前态 <c>s−1</c> 层，顶针后沿通道落回 d。
    /// 快通道保证目标列的原生晶体与临时晶体之间隔着普通格，碎晶不会误伤。</item>
    /// <item><b>无下降通道</b>（A = 目标列下移一层）：Pin 之后每格本来就在目标层，
    /// 只需要碎掉临时晶体。</item>
    /// </list>
    ///
    /// <para><b>底层规则</b>：Pin 只给「第 1 层非空」的列补针。</para>
    /// <list type="bullet">
    /// <item>目标底层为空 ⇒ 前态第 1 层必须为空：把最下方连续普通块整体上抬一层，
    /// 逐个记录下降（源层 <c>s = d+1</c>），顶针后逐对落回；</item>
    /// <item>目标底层是针 ⇒ 第 1 层垫一枚临时晶体（Pin 把它补成针，晶体本身被炸掉）。</item>
    /// </list>
    ///
    /// <para><b>四列</b>：A = 目标列整体下移一层（源格放到前态 s−1 层）；
    /// B：前态第 z 层放晶体 ⇔ <c>z+1 ∈ I</c>，其余普通格；
    /// E：满高纯晶体柱（顶针时顶端超限整根碎掉，用来引爆）；
    /// N：前态第 z 层放针 ⇔ <c>z+1 ∈ J</c>，其余普通格。
    /// <c>I = ∪[d, s−1]</c> 并上每段临时晶体的最高层，<c>J = 所有源层 s</c>。</para>
    ///
    /// <para>
    /// 目标列的洞先填同层临时晶体（通道 / 紧列化），填不了的（会与原生晶体纵向
    /// 融合、或填了过不了五项条件）就撤掉；第 1 层始终保留为空/针。
    /// A 若仍带洞，用交换链子配方（<see cref="ExchangeChain"/> +
    /// <see cref="Replay"/>.Verify）证明可造，拿不到就如实报未覆盖。
    /// </para>
    /// </summary>
    #region 构造 H（§20.3 构造式：目标列 → 抬高后的列）

    /// <summary>
    /// 构造式构造 H（docs §20.3 / §20.5）：<b>由目标列 → 抬高后的列逐格比对</b>
    /// 推出降表、<c>I</c>、<c>J</c>，再生成 A | B | E | N，用真实
    /// <see cref="ShapePhysics.Pin"/> 验收。<b>不扫描「晶体正下方的空段」</b>。
    ///
    /// <para><b>推导规则（§20.3 ④）</b>：目标列的每个内部空段（上下都有材料）处理一次：</para>
    /// <list type="bullet">
    /// <item>空段上方的材料是<b>普通格</b> ⇒ 把它抬高一层（它就是「要降的格」），
    /// 空段连同它让出来的那一层用临时晶体填满——<b>只填让出来的段，不做「全填」</b>；</item>
    /// <item>空段上方的材料是<b>晶体</b> ⇒ 晶体下移一层（顶针会把它抬回去），
    /// 空出来的那一格留给下方空段抬高上来的普通格。</item>
    /// </list>
    /// <para>
    /// 两种处理顺序（从下往上 / 从上往下）都是确定性扫描，先后各试一次，
    /// <b>不做参数枚举</b>。普通格 0 起第 <c>k</c> 层给出降表 <c>k+2 → k+1</c>
    /// （§20.3 ①，层号用顶针后）；<c>I = ∪[d, s−1]</c> 并入临时晶体段最高层，
    /// <c>J = {s}</c>（§20.3 ②③）；B / N 直接按 I / J 生成（§20.3 ⑤）。
    /// </para>
    /// <para>
    /// 回放只作验收（§20.5 第 4 条）：不过就退回扫描路线，绝不扩大搜索。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildConstructedHPaper(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        AngleAlgorithm algorithm,
        out string? failureReason)
    {
        failureReason = null;

        if (parts != 4)
        {
            failureReason = "构造 H 需要四象限的环（A | B | E | N）。";
            return null;
        }

        if (layers < 2 || layers < target.LayerCount)
        {
            failureReason = "cap 装不下目标列的完整高度，构造 H 的前态放不下。";
            return null;
        }

        if (target[0, column].Type is not (CellType.Pin or CellType.Empty))
        {
            failureReason = "构造 H 的顶针会在第 1 层补针，目标底层必须是针或空。";
            return null;
        }

        var targetCodes = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            targetCodes[layer] = CodeOf(
                layer < target.LayerCount
                    ? target[layer, column]
                    : ShapeCell.Empty());
        }

        // 底针链（算法 5）走扫描路线：§20.3 的构造式只讨论普通格与晶体。
        for (int layer = 0; layer < layers; layer++)
        {
            if (targetCodes[layer] == ShapeSignature.PinCode)
            {
                failureReason = "目标列含顶针，§20.3 构造式只处理普通格 / 晶体。";
                return null;
            }
        }

        char color = CrystalColor(target, column);
        string? deriveFailure = null;

        foreach (bool topDown in new[] { false, true })
        {
            char[]? raised = DeriveRaisedColumn(
                targetCodes,
                layers,
                topDown,
                out bool[]? temporary,
                out string? why);

            if (raised is null || temporary is null)
            {
                deriveFailure = why;
                continue;
            }

            AngleWorkOrder? order = TryBuildConstructedHPaperCore(
                target,
                column,
                layers,
                parts,
                kind,
                algorithm,
                raised,
                temporary,
                color,
                topDown,
                out string? coreFailure);

            if (order is not null)
            {
                failureReason = null;
                return order;
            }

            deriveFailure = coreFailure;
        }

        failureReason = "按 §20.3 构造式推导抬高后的列失败：" + deriveFailure;
        return null;
    }

    /// <summary>
    /// §20.3 ④：由目标列推出「抬高后的列」A。返回 null 表示这份目标列不能按
    /// 构造式抬高（调用方退回扫描路线）。
    ///
    /// <para>
    /// <paramref name="topDown"/> 只决定内部空段的处理顺序：两种顺序都是确定性扫描，
    /// 不引入搜索参数。
    /// </para>
    /// </summary>
    private static char[]? DeriveRaisedColumn(
        char[] targetCodes,
        int layers,
        bool topDown,
        out bool[]? temporary,
        out string? failureReason)
    {
        temporary = null;
        failureReason = null;

        var raised = (char[])targetCodes.Clone();
        var temp = new bool[layers];

        var gaps = new List<(int Lo, int Hi)>();
        int index = 0;

        while (index < layers)
        {
            if (targetCodes[index] != ShapeSignature.EmptyCode)
            {
                index++;
                continue;
            }

            int lo = index;

            while (index < layers &&
                   targetCodes[index] == ShapeSignature.EmptyCode)
            {
                index++;
            }

            int hi = index - 1;

            if (lo > 0 &&
                hi + 1 < layers &&
                targetCodes[lo - 1] != ShapeSignature.EmptyCode &&
                targetCodes[hi + 1] != ShapeSignature.EmptyCode)
            {
                gaps.Add((lo, hi));
            }
        }

        gaps.Sort((left, right) => topDown
            ? right.Hi.CompareTo(left.Hi)
            : left.Lo.CompareTo(right.Lo));

        foreach ((int lo, int hi) in gaps)
        {
            char upper = targetCodes[hi + 1];

            if (upper == ShapeSignature.SolidCode)
            {
                /*
                 * §20.3 ④ 前半：把要降的普通格抬高一层，让出来的段填临时晶体。
                 * 抬高目标 = 空段上方的第一格再上一层；填晶体范围 = 空段 + 它让出的那一层。
                 */
                int destination = hi + 2;

                if (destination >= layers ||
                    raised[destination] != ShapeSignature.EmptyCode ||
                    raised[hi + 1] != ShapeSignature.SolidCode)
                {
                    failureReason =
                        $"第 {hi + 2} 层的普通格抬到第 {destination + 1} 层时没有空位。";
                    return null;
                }

                raised[destination] = ShapeSignature.SolidCode;
                raised[hi + 1] = ShapeSignature.EmptyCode;

                for (int layer = lo; layer <= hi + 1; layer++)
                {
                    raised[layer] = ShapeSignature.CrystalCode;
                    temp[layer] = true;
                }
            }
            else if (upper == ShapeSignature.CrystalCode)
            {
                /*
                 * §20.3 ④ 后半：晶体下移一层（顶针抬回原层）；空出来的那一格
                 * 留给下方空段抬高上来的普通格，填不上时在下面补临时晶体。
                 */
                if (raised[hi] != ShapeSignature.EmptyCode ||
                    raised[hi + 1] != ShapeSignature.CrystalCode)
                {
                    failureReason =
                        $"第 {hi + 2} 层的晶体下移时第 {hi + 1} 层已被占用。";
                    return null;
                }

                raised[hi] = ShapeSignature.CrystalCode;
                raised[hi + 1] = ShapeSignature.EmptyCode;
            }
            else
            {
                failureReason = "空段上方的材料不是普通格或晶体（§20.3 不处理顶针）。";
                return null;
            }
        }

        // 让出来的段若还剩内部空洞（上下都有材料），补成临时晶体。
        int top = layers - 1;

        while (top >= 0 && raised[top] == ShapeSignature.EmptyCode)
        {
            top--;
        }

        for (int layer = 1; layer < top; layer++)
        {
            if (raised[layer] == ShapeSignature.EmptyCode &&
                raised[layer - 1] != ShapeSignature.EmptyCode &&
                raised[layer + 1] != ShapeSignature.EmptyCode)
            {
                raised[layer] = ShapeSignature.CrystalCode;
                temp[layer] = true;
            }
        }

        // 目标底层为空 ⇒ 前态第 1 层必须为空（否则 Pin 会补出目标没有的针）。
        if (targetCodes[0] == ShapeSignature.EmptyCode &&
            raised[0] != ShapeSignature.EmptyCode)
        {
            failureReason = "前态第 1 层不为空，Pin 会补出目标没有的针。";
            return null;
        }

        // 临时晶体与原生晶体纵向相邻 ⇒ 碎晶会连带炸掉目标晶体：退回扫描路线。
        for (int layer = 0; layer < layers; layer++)
        {
            if (!temp[layer])
            {
                continue;
            }

            bool touchesNative =
                (layer > 0 &&
                 raised[layer - 1] == ShapeSignature.CrystalCode &&
                 !temp[layer - 1]) ||
                (layer + 1 < layers &&
                 raised[layer + 1] == ShapeSignature.CrystalCode &&
                 !temp[layer + 1]);

            if (touchesNative)
            {
                failureReason =
                    $"第 {layer + 1} 层的临时晶体与原生晶体纵向相邻，碎晶会误伤目标晶体。";
                return null;
            }
        }

        temporary = temp;
        return raised;
    }

    /// <summary>§20.3 ⑤：按 I / J 生成 A | B | E | N，并用真实 Pin 验收。</summary>
    private static AngleWorkOrder? TryBuildConstructedHPaperCore(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        AngleAlgorithm algorithm,
        char[] raised,
        bool[] temporary,
        char color,
        bool topDown,
        out string? failureReason,
        IEnumerable<int>? overrideILayers = null,
        IEnumerable<int>? overrideJLayers = null,
        string? summary = null,
        IReadOnlyList<string>? derivationLimitations = null)
    {        failureReason = null;

        var iLayers = new HashSet<int>();
        var jLayers = new HashSet<int>();
        var descents = new List<string>();

        for (int layer = 0; layer < layers; layer++)
        {
            if (raised[layer] != ShapeSignature.SolidCode)
            {
                continue;
            }

            // §20.3 ①：普通格 0 起第 k 层顶针后在 k+1 层，降到 k ⇒ 降表 k+2 → k+1。
            descents.Add($"{layer + 2}→{layer + 1}");
            iLayers.Add(layer);
            jLayers.Add(layer + 1);
        }

        // §20.3 ②：每段临时晶体的最高层并入 I（B 的晶体桥在那里与它咬合）。
        for (int layer = 0; layer < layers; layer++)
        {
            if (!temporary[layer] ||
                (layer > 0 && temporary[layer - 1]))
            {
                continue;
            }

            int segmentTop = layer;

            while (segmentTop + 1 < layers && temporary[segmentTop + 1])
            {
                segmentTop++;
            }

            iLayers.Add(segmentTop + 1);
            jLayers.Add(segmentTop + 2);
        }

        /*
         * 算法 6 的「底块提升」变体（§20.3 ①/② 的补充推导）直接把降表算好，
         * 用 I / J 覆盖上面按「晶体正下方空段」推出来的集合，不再二次推导。
         */
        if (overrideILayers is not null)
        {
            iLayers = new HashSet<int>(overrideILayers);
            jLayers = overrideJLayers is null
                ? new HashSet<int>()
                : new HashSet<int>(overrideJLayers);
            descents.Clear();
        }

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            preState[layer, column] = CellFromCode(raised[layer], color);
        }

        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);
        int nColumn = Wrap(column - 1, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            // B：z+1 ∈ I → 晶体，否则普通格（整列紧列）。
            preState[layer, bColumn] = iLayers.Contains(layer + 1)
                ? ShapeCell.Crystal(color)
                : ShapeCell.Shape('C', 'u');

            // E：满高纯晶体柱。
            preState[layer, eColumn] = ShapeCell.Crystal(color);

            // N：z+1 ∈ J → 针，否则普通格（整列紧列）。
            preState[layer, nColumn] = jLayers.Contains(layer + 1)
                ? ShapeCell.Pin()
                : ShapeCell.Shape('C', 'u');
        }

        if (ShapeSignature.ColumnText(preState, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            failureReason = "§20.3 推出的 A 与目标列相同（循环）。";
            return null;
        }

        // A 有洞 ⇒ 交换链子配方（rank 0）证明整份前态可装配。
        bool tightTargetColumn =
            ShapeSignature.IsTightColumn(preState, column);
        Recipe? subRecipe = null;

        if (!tightTargetColumn &&
            (!ExchangeChain.TryBuild(preState, out subRecipe) ||
             subRecipe is null ||
             subRecipe.Rank != 0 ||
             !Replay.Verify(subRecipe, preState, out _)))
        {
            failureReason =
                "§20.3 推出的 A 带洞，交换链子配方没能装配出这份前态。";
            return null;
        }

        var limitations = BaseLimitations(
            summary ??
            "构造 H（§20.3 构造式，由目标列 → 抬高后的列逐格比对得出）：" +
            "A = 目标列把要降的普通格抬高一层、只把让出来的段填临时晶体；" +
            "B 按 z+1 ∈ I 放晶体、E 是满高纯晶体柱、N 按 z+1 ∈ J 放针；" +
            "一次 Pin 同时引爆并整批落下。");

        if (derivationLimitations is not null)
        {
            limitations.AddRange(derivationLimitations);
        }
        else
        {
            limitations.Add(
                "降表（§20.3 ①，层号用顶针后）：" +
                (descents.Count > 0 ? string.Join("、", descents) : "无") +
                $"，I = {{{string.Join(",", iLayers.Order().Select(layer => layer + 1))}}}" +
                $"，J = {{{string.Join(",", jLayers.Order().Select(layer => layer + 1))}}}。");

            limitations.Add(
                "A 的推导顺序：" +
                (topDown ? "从上往下的空段" : "从下往上的空段") +
                $"，A = {ShapeSignature.ColumnText(preState, column)}。");
        }

        // §19.1：A 里晶体正下方为空 = 有晶下间隙；目标对应位置有材料时可避免。
        for (int layer = 1; layer < layers; layer++)
        {
            if (preState[layer, column].Type != CellType.Crystal ||
                preState[layer - 1, column].Type != CellType.Empty)
            {
                continue;
            }

            bool targetHasSupport =
                layer - 1 < target.LayerCount &&
                target[layer - 1, column].Type != CellType.Empty;

            limitations.Add(targetHasSupport
                ? "§19.1：A 第 " + (layer + 1) + " 层晶体下方本可贴住（目标对应位置有材料），" +
                  "但构造式没能填上，记为待改进。"
                : "此处的晶下间隙是目标本身要求的，支撑材料会留残料（§19.1 / §19.3）。");
        }

        var auxiliaryColumns = new[] { nColumn, bColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（构造 H：A | 晶体桥柱 B | 满高纯晶体柱 E | 针槽柱 N）",
                preState,
                AngleWorkStepKind.PreState)
        };

        // P1 硬约束（docs §1 / §R1）：原生晶体正下方必须是普通格。
        if (HasNativeCrystalGap(preState, column, temporary, out int paperGapLayer))
        {
            failureReason =
                $"A 第 {paperGapLayer + 1} 层的原生晶体下方为空（P1 不满足），作废这条前态。";
            return null;
        }

        // 验收条件一 / 七：前态每列五项条件 + 辅助列紧列。
        if (IsRejectedPreState(target, column, preState, out int badColumn))
        {
            failureReason = DescribeIllegalPreState(preState, badColumn);
            return null;
        }

        if (!ShapeOps.IsStable(preState))
        {
            failureReason = "§20.3 构造式的前态不稳定。";
            return null;
        }

        PhysicsResult pinResult = ShapePhysics.Pin(preState);

        if (!pinResult.Success || pinResult.Document is null)
        {
            failureReason = "ShapePhysics.Pin 失败：" +
                            (pinResult.Error ?? "未知原因");
            return null;
        }

        ShapeDocument pinned = pinResult.Document;

        if (!MatchesTargetColumn(
                pinned,
                target,
                column,
                layers,
                out string? difference))
        {
            failureReason = "§20.3 构造式一次顶针后的目标列与目标不符：" + difference;
            return null;
        }

        for (int layer = 0; layer < layers; layer++)
        {
            if (pinned[layer, bColumn].Type == CellType.Crystal)
            {
                failureReason = "顶针后晶体桥没有全部删除，构造 H 的前提不成立。";
                return null;
            }
        }

        steps.Add(new AngleWorkStep(
            "顶针：一次 Pin 把整体抬一层，E 顶端超限整簇碎裂并连带删掉 B 的晶体桥，" +
            "源普通格沿通道落到各自的目标层",
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
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.H,
            Algorithm = algorithm,
            Rotation = 0,
            AuxiliaryColumns = auxiliaryColumns,
            SubRecipe = subRecipe,
            Rank = 1,
            Limitations = limitations
        };
    }

    #endregion

    private static AngleWorkOrder? TryBuildConstructedH(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        AngleAlgorithm algorithm,
        out string? failureReason)
    {
        /*
         * ① §20.3 构造式：由「目标列 → 抬高后的列」逐格比对推出降表 / I / J。
         *    这是既有形态的固定写法，保留它可以让老语料的输出与工序单不变。
         */
        AngleWorkOrder? paper = TryBuildConstructedHPaper(
            target,
            column,
            layers,
            parts,
            kind,
            algorithm,
            out string? paperFailure);

        if (paper is not null)
        {
            failureReason = null;
            return AttachQ1SubRecipe(paper);
        }

        /*
         * ② §13.6 / §14 的通用构造：逐格扫描生成前态 A（晶体放 f−1、普通格按跨越的
         *    空层数放 f+k、空段只填「下方有格且上方有格」的连续空层），再由 A 与
         *    目标逐层比对推出降表 / I / J，最后按 I / J 生成 B / E / N。
         *    P1（每颗原生晶体下方非空）是硬约束；失败不退回枚举。
         */
        AngleWorkOrder? general = TryBuildConstructedHGeneral(
            target,
            column,
            layers,
            parts,
            kind,
            algorithm,
            out string? generalFailure);

        if (general is not null)
        {
            failureReason = null;
            return AttachQ1SubRecipe(general);
        }

        /*
         * 验证开关：只保留前两段（§20.3 构造式 → §13.6 通用前态）。
         * 后两段与两段是互斥的候选，不是前置条件；打开开关就能量出
         * 「两段够不够用」，够用则把后两段整段删除。
         */
        if (ConstructHStrictTwoStage)
        {
            failureReason =
                $"两段开关（§20.3 → §13.6）：§20.3 构造式：{paperFailure}；" +
                $"§13.6 通用构造：{generalFailure}；" +
                "已跳过「带下降通道扫描」与「下移一格扫描」两段。";
            return null;
        }

        /*
         * ③ 确定性扫描形态①：先把「晶体正下方空段」的最低普通格提到空段顶端
         *    （A 里形成可碎掉的下降通道；原生晶体与临时晶体被普通格隔开）。
         */
        AngleWorkOrder? lifted = TryBuildConstructedHCore(
            target,
            column,
            layers,
            parts,
            kind,
            algorithm,
            liftGapSources: true,
            out string? liftedFailure);

        if (lifted is not null)
        {
            failureReason = null;
            return AttachQ1SubRecipe(lifted);
        }

        /*
         * ④ 确定性扫描形态②：直接取 A = 目标列下移一层（Pin 之后每格正好落在目标层）。
         *    前态必须满足 P1（底针角除外），否则在验收里如实作废。
         */
        AngleWorkOrder? plain = TryBuildConstructedHCore(
            target,
            column,
            layers,
            parts,
            kind,
            algorithm,
            liftGapSources: false,
            out string? plainFailure);

        failureReason = plain is not null
            ? null
            : $"§20.3 构造式：{paperFailure}；§13.6 通用构造：{generalFailure}；" +
              $"带下降通道：{liftedFailure}；" +
              $"下移一格：{plainFailure}";

        return plain is null ? null : AttachQ1SubRecipe(plain);
    }
    /// <summary>
    /// §13.6 / §14 的构造式构造 H：<b>严格按 §13.6 的 7 步</b>生成前态 A、降表与 I / J，
    /// 再按 I / J 生成 B | E | N，最后用真实 <see cref="ShapePhysics.Pin"/> 验收。
    ///
    /// <para>
    /// 第 1 步定基准层 f0；第 2 步「最低晶体以下不动、及以上整体下移一格」（N2）；
    /// 第 3 步按目标 L1 是否为针决定 A[0] 非空 / 为空（N1）；
    /// 第 4 步填内部空段并把空段上方的格抬一格，P1 自检只查<b>初始前态</b>（N3）；
    /// 第 5 步只声明降 1 层；第 6 步按 I / J 生成 B / E / N；
    /// 第 7 步真实 Pin 验收，失败先做一次固定调整（临时段下扩一格），
    /// 再退到两种结构性读法，仍失败才返回 Uncovered——<b>不枚举、不退 FillBreak</b>。
    /// </para>
    /// </summary>
    private static AngleWorkOrder? TryBuildConstructedHGeneral(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        AngleAlgorithm algorithm,
        out string? failureReason)
    {
        failureReason = null;

        if (parts != 4)
        {
            failureReason = "构造 H 需要四象限的环（A | B | E | N）。";
            return null;
        }

        if (layers < 2 || layers < target.LayerCount)
        {
            failureReason = "cap 装不下目标列的完整高度，构造 H 的前态放不下。";
            return null;
        }

        var targetCodes = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            targetCodes[layer] = CodeOf(
                layer < target.LayerCount
                    ? target[layer, column]
                    : ShapeCell.Empty());
        }

        // N1（§16.5）：目标第 1 层的针由顶针补出来，A 里不放针；
        // 其余层的针不在构造 H 里（构造 H 的顶针只补第 1 层）。
        for (int layer = 0; layer < layers; layer++)
        {
            if (targetCodes[layer] != ShapeSignature.PinCode)
            {
                continue;
            }

            if (layer != 0)
            {
                failureReason = "目标列第 " + (layer + 1) +
                                " 层含顶针，构造 H 的顶针只能补出第 1 层的针。";
                return null;
            }
        }

        HPreState? plan = BuildPreStateH(
            targetCodes,
            layers,
            out string? buildFailure);

        if (plan is null)
        {
            failureReason = buildFailure;
            return null;
        }

        (HashSet<int> iLayers, HashSet<int> jLayers) = BuildDescents(
            plan,
            targetCodes,
            layers,
            extendTemporary: false,
            out string? descentFailure);

        if (descentFailure is not null)
        {
            failureReason = descentFailure;
            return null;
        }

        (char[] bCodes, char[] eCodes, char[] nCodes) = BuildAuxColumns(
            iLayers,
            jLayers,
            layers);

        char color = CrystalColor(target, column);

        var derivationLimitations = new List<string>
        {
            "构造 H（§13.6 七步，不枚举）：最低晶体以下不动、及以上整体下移一格；" +
            "目标 L1 是针时 A[0] 放临时晶体（顶针补针），否则 A[0] 留空；" +
            "「下方有格且上方有格」的连续空段填临时晶体，并把空段上方的格抬一格。",
            "A = " + new string(plan.A) +
            "；降表（源层 s → 落点 d，层号用顶针后）：" +
            (plan.Descents.Count > 0
                ? string.Join("、", plan.Descents.Select(pair => $"{pair.S}→{pair.D}"))
                : "无") + "。",
            "I = {" + string.Join(",", iLayers.Order()) + "}（B 的晶体桥层，1 起），" +
            "J = {" + string.Join(",", jLayers.Order()) + "}（N 的针槽层，1 起）；" +
            "B = " + new string(bCodes) +
            "，E = " + new string(eCodes) +
            "，N = " + new string(nCodes) + "。"
        };

        string? coreFailure = null;

        AngleWorkOrder? order = TryBuildConstructedHPaperCore(
            target,
            column,
            layers,
            parts,
            kind,
            algorithm,
            plan.A,
            plan.Temporary,
            color,
            topDown: false,
            out coreFailure,
            overrideILayers: iLayers,
            overrideJLayers: jLayers,
            summary: "构造 H（§13.6 七步）：最低晶体以下不动、及以上整体下移一格；" +
                     "空段填临时晶体、上方格抬一格；再按降表推 I / J 生成 " +
                     "B / E / N；一次 Pin 同时引爆并整批落下。",
            derivationLimitations: derivationLimitations);

        if (order is null)
        {
            /*
             * 第 7 步：回放失败 ⇒ 回第 4 步做一次确定性调整——把每段临时晶体
             * 向下扩一格（等价于填充段多覆盖一个让位层），再走第 5–7 步。
             * 只有「不扩」与「扩一格」两种固定读法，不是候选枚举。
             */
            (HashSet<int> extendedI, HashSet<int> extendedJ) = BuildDescents(
                plan,
                targetCodes,
                layers,
                extendTemporary: true,
                out string? extendedFailure);

            if (extendedFailure is null)
            {
                order = TryBuildConstructedHPaperCore(
                    target,
                    column,
                    layers,
                    parts,
                    kind,
                    algorithm,
                    plan.A,
                    plan.Temporary,
                    color,
                    topDown: false,
                    out string? extendedOrderFailure,
                    overrideILayers: extendedI,
                    overrideJLayers: extendedJ,
                    summary: "构造 H（§13.6 七步 + 临时段下扩一格）：A 不变，" +
                             "把每段临时晶体向下扩一格后重推 I / J，一次 Pin 引爆并整批落下。",
                    derivationLimitations: derivationLimitations);

                if (order is null)
                {
                    coreFailure = coreFailure + "；临时段下扩一格：" + extendedOrderFailure;
                }
            }
        }

        if (order is null)
        {
            /*
             * 结构性降表：深落差的 B 桥（落点上下两层）与相邻源层之间的针槽，
             * 由 A 的逐格位置直接推出。它是固定的结构性读法，不是候选枚举；
             * 前面两种读法都回放失败时才用，用来覆盖「落差 ≥ 2 层的坍落」。
             */
            (HashSet<int> structuralI, HashSet<int> structuralJ) = BuildDescentsStructural(
                plan,
                targetCodes,
                layers);

            order = TryBuildConstructedHPaperCore(
                target,
                column,
                layers,
                parts,
                kind,
                algorithm,
                plan.A,
                plan.Temporary,
                color,
                topDown: false,
                out string? structuralFailure,
                overrideILayers: structuralI,
                overrideJLayers: structuralJ,
                summary: "构造 H（§13.6 七步 + 结构性降表）：A 不变，" +
                         "深落差的 B 桥铺到落点上下两层、相邻源层补针槽，" +
                         "一次 Pin 引爆并整批落下。",
                derivationLimitations: derivationLimitations);

            if (order is null)
            {
                coreFailure = coreFailure + "；结构性降表：" + structuralFailure;
            }
        }

        if (order is null)
        {
            /*
             * 临时段针槽读法：N 在每段临时晶体的**每一层**都放针（不再只在源层下方放），
             * B 只保留「最低落点 / 深落差」的桥。它对应「临时段由针槽通道逐层拆开」
             * 这一种固定结构，不是候选枚举；前三种读法都失败时才用。
             */
            (HashSet<int> tempPinI, HashSet<int> tempPinJ) = BuildDescentsTemporaryPins(
                plan,
                targetCodes,
                layers);

            order = TryBuildConstructedHPaperCore(
                target,
                column,
                layers,
                parts,
                kind,
                algorithm,
                plan.A,
                plan.Temporary,
                color,
                topDown: false,
                out string? tempPinFailure,
                overrideILayers: tempPinI,
                overrideJLayers: tempPinJ,
                summary: "构造 H（§13.6 七步 + 临时段针槽）：A 不变，" +
                         "临时晶体段的每一层都放针、B 只保留落点桥，" +
                         "一次 Pin 引爆并整批落下。",
                derivationLimitations: derivationLimitations);

            if (order is null)
            {
                coreFailure = coreFailure + "；临时段针槽：" + tempPinFailure;
            }
        }
        if (order is null)
        {
            failureReason = coreFailure ?? "§13.6 通用前态没有通过真实 Pin 回放。";
            return null;
        }

        failureReason = null;
        return order;
    }

    /// <summary>§13.6 七步生成的前态 A 与降表（层号 1 起）。</summary>
    private sealed record HPreState(
        char[] A,
        bool[] Temporary,
        int[] Origin,
        IReadOnlyList<(int S, int D)> Descents,
        IReadOnlyList<int> TemporaryTops);

    /// <summary>
    /// §14.1 <c>BuildPreStateH(target, cap) → (A, descents)</c>，<b>严格按 §13.6 的 7 步</b>：
    ///
    /// <code>
    /// 第 1 步 f0 = 最低晶体所在层
    /// 第 2 步 f &lt; f0 ⇒ A[f] = t[f]；f ≥ f0 ⇒ A[f-1] = t[f]（f-1 &lt; 1 ⇒ 换碎晶路线）
    /// 第 3 步 目标 L1 是 P ⇒ A[0] 非空；否则 A[0] 必须为空（放到 L1 的格抬到 L2）
    /// 第 4 步 「下方有格、上方有格」的连续空段填临时晶体；
    ///         被填充段上方的那个格抬一格（让出的格并入填充段）；
    ///         上方是晶体时改为把空段下方的普通格抬到空段顶端（不移动晶体）。
    ///         最后只对<b>初始前态</b>做 P1 自检（N3：中间态允许晶下间隙）
    /// 第 5 步 降表：普通格在 A 第 a 层 ⇒ 顶针后 a+1；比目标层低就只声明降 1 层
    /// 第 6 步 B[z] ⇔ z 层是降表落点（d−1）或临时段顶；N[z] ⇔ z 层是源层 s−1；E 全晶体
    /// 第 7 步 由调用方跑真实 Pin 验收；失败则把填充段向下扩一格再走 5–7 步
    /// </code>
    ///
    /// <para><b>不枚举</b>：全程只有线性扫描与一次固定调整（临时段下扩一格）。</para>
    /// </summary>
    private static HPreState? BuildPreStateH(
        char[] targetCodes,
        int layers,
        out string? failureReason)
    {
        failureReason = null;

        var a = new char[layers];
        var temporary = new bool[layers];
        var origin = new int[layers];

        Array.Fill(a, ShapeSignature.EmptyCode);
        Array.Fill(origin, -1);

        char At(int layer) => a[layer - 1];
        void Put(int layer, char code) => a[layer - 1] = code;

        // ── 第 1 步：f0 = 最低晶体所在层 ──
        int firstCrystal = 0;

        for (int f = 1; f <= layers; f++)
        {
            if (targetCodes[f - 1] == ShapeSignature.CrystalCode)
            {
                firstCrystal = f;
                break;
            }
        }

        if (firstCrystal == 0)
        {
            failureReason = "目标列没有晶体，不走构造 H（构造 A / C）。";
            return null;
        }

        // ── 第 2 步：最低晶体以下不动，最低晶体及以上整体下移一格 ──
        for (int f = 1; f <= layers; f++)
        {
            char code = targetCodes[f - 1];

            if (code == ShapeSignature.EmptyCode)
            {
                continue;
            }

            if (code == ShapeSignature.PinCode)
            {
                // 第 1 层的针由顶针补出来（N1），其余针不在构造 H 里。
                if (f == 1)
                {
                    continue;
                }

                failureReason =
                    $"第 {f} 层的针不在第 1 层：构造 H 的顶针只能补出第 1 层的针。";
                return null;
            }

            int slot = f < firstCrystal
                ? f
                : f - 1;

            if (slot < 1)
            {
                failureReason =
                    $"第 {f} 层的目标格下移到第 {f - 1} 层后低于地面，换碎晶路线。";
                return null;
            }

            if (At(slot) != ShapeSignature.EmptyCode &&
                At(slot) != code)
            {
                failureReason =
                    $"第 {f} 层的目标格与第 {slot} 层已经放好的格冲突。";
                return null;
            }

            Put(slot, code);
            origin[slot - 1] = f;
        }

        // ── 第 3 步：第 1 层的针 ──
        if (targetCodes[0] == ShapeSignature.PinCode)
        {
            // 目标 L1 是针 ⇒ A[0] 必须非空，顶针会在 L1 补出这枚针。
            if (At(1) == ShapeSignature.EmptyCode)
            {
                Put(1, ShapeSignature.CrystalCode);
                temporary[0] = true;
            }
        }
        else if (At(1) != ShapeSignature.EmptyCode)
        {
            // 目标 L1 不是针 ⇒ A[0] 必须为空，否则顶针凭空补针。
            int slot = 2;

            while (slot <= layers &&
                   At(slot) != ShapeSignature.EmptyCode)
            {
                slot++;
            }

            if (slot > layers)
            {
                failureReason = "第 1 层的格抬到第 2 层时没有空位。";
                return null;
            }

            Put(slot, At(1));
            origin[slot - 1] = origin[0];
            Put(1, ShapeSignature.EmptyCode);
            origin[0] = -1;
        }

        // ── 第 4 步：临时晶体填充 + 抬升（自下而上，扫到稳定） ──
        for (int pass = 0; pass <= layers * 2; pass++)
        {
            int low = 0;
            int high = 0;

            for (int start = 2; start <= layers - 1; start++)
            {
                if (At(start) != ShapeSignature.EmptyCode ||
                    At(start - 1) == ShapeSignature.EmptyCode)
                {
                    continue;
                }

                int end = start;

                while (end + 1 <= layers &&
                       At(end + 1) == ShapeSignature.EmptyCode)
                {
                    end++;
                }

                if (end + 1 > layers ||
                    At(end + 1) == ShapeSignature.EmptyCode)
                {
                    continue;
                }

                low = start;
                high = end;
                break;
            }

            if (low == 0)
            {
                break;
            }

            char above = At(high + 1);

            if (above == ShapeSignature.SolidCode)
            {
                /*
                 * 被填充段上方的普通格抬一格；它让出来的那一格并入填充段。
                 * （P.T..c 的 T@3 → A@4，顶针后 5，再坍落到 3。）
                 */
                if (high + 2 > layers ||
                    At(high + 2) != ShapeSignature.EmptyCode)
                {
                    /*
                     * 上方格抬不动（再上一层已被占用）⇒ 固定退化读法：
                     * 只把空段填成临时晶体，不抬普通格。是否成立仍由第 7 步的
                     * 真实 Pin 验收，不在这里猜。
                     */
                    for (int layer = low; layer <= high; layer++)
                    {
                        Put(layer, ShapeSignature.CrystalCode);
                        temporary[layer - 1] = true;
                        origin[layer - 1] = -1;
                    }

                    continue;
                }

                Put(high + 2, ShapeSignature.SolidCode);
                origin[high + 1] = origin[high];
                Put(high + 1, ShapeSignature.EmptyCode);
                origin[high] = -1;

                for (int layer = low; layer <= high + 1; layer++)
                {
                    Put(layer, ShapeSignature.CrystalCode);
                    temporary[layer - 1] = true;
                    origin[layer - 1] = -1;
                }
            }
            else if (above == ShapeSignature.CrystalCode)
            {
                /*
                 * 空段上方是原生晶体：晶体不能动（它必须留在 f−1），
                 * 改为把空段下方的普通格抬到空段顶端，让出的段填临时晶体。
                 */
                if (At(low - 1) != ShapeSignature.SolidCode)
                {
                    failureReason =
                        $"第 {low - 1} 层不是普通格，抬不动（空段上方是晶体）。";
                    return null;
                }

                Put(high, ShapeSignature.SolidCode);
                origin[high - 1] = origin[low - 2];
                Put(low - 1, ShapeSignature.EmptyCode);
                origin[low - 2] = -1;

                if (low - 2 >= 1 &&
                    At(low - 2) != ShapeSignature.EmptyCode)
                {
                    for (int layer = low - 1; layer <= high - 1; layer++)
                    {
                        if (At(layer) != ShapeSignature.EmptyCode)
                        {
                            break;
                        }

                        Put(layer, ShapeSignature.CrystalCode);
                        temporary[layer - 1] = true;
                        origin[layer - 1] = -1;
                    }
                }
            }
            else
            {
                failureReason =
                    $"第 {high + 1} 层是针，构造 H 的空段填充不处理针。";
                return null;
            }
        }

        /*
         * P1 自检（N3）：只检查初始前态——每颗原生晶体直接坐在普通格上。
         * 不满足 ⇒ 把下方最近的普通格抬到它正下方（可跨多层）；
         * 抬不动 ⇒ 换碎晶路线。多刀的中间态不在这里检查。
         */
        for (int pass = 0; pass <= layers; pass++)
        {
            bool repaired = false;

            for (int crystal = 2; crystal <= layers; crystal++)
            {
                if (At(crystal) != ShapeSignature.CrystalCode ||
                    temporary[crystal - 1] ||
                    At(crystal - 1) != ShapeSignature.EmptyCode)
                {
                    continue;
                }

                int donor = crystal - 2;

                while (donor >= 1 &&
                       At(donor) != ShapeSignature.SolidCode)
                {
                    donor--;
                }

                if (donor < 1)
                {
                    failureReason =
                        $"A 第 {crystal} 层的原生晶体下方找不到可抬升的普通格" +
                        "（P1 抬不动，换碎晶路线）。";
                    return null;
                }

                Put(crystal - 1, ShapeSignature.SolidCode);
                origin[crystal - 2] = origin[donor - 1];
                Put(donor, ShapeSignature.EmptyCode);
                origin[donor - 1] = -1;
                repaired = true;
            }

            if (!repaired)
            {
                break;
            }
        }

        for (int layer = 2; layer <= layers; layer++)
        {
            if (At(layer) == ShapeSignature.CrystalCode &&
                !temporary[layer - 1] &&
                At(layer - 1) == ShapeSignature.EmptyCode)
            {
                failureReason =
                    $"A 第 {layer} 层的原生晶体下方为空（初始前态 P1 不满足）。";
                return null;
            }
        }

        if (!ShapeSignature.CheckColumn(new string(a)).IsLegal)
        {
            failureReason = "A 过不了五项条件：" + new string(a);
            return null;
        }

        // ── 第 5 步：降表（层号一律用顶针后 = A 的层号 + 1） ──
        var descents = new List<(int S, int D)>();

        for (int layer = 1; layer <= layers; layer++)
        {
            if (At(layer) != ShapeSignature.SolidCode ||
                origin[layer - 1] < 0)
            {
                continue;
            }

            int pinned = layer + 1;
            int targetLayer = origin[layer - 1];

            if (pinned == targetLayer)
            {
                continue;
            }

            if (pinned < targetLayer)
            {
                failureReason =
                    $"A 第 {layer} 层的普通格顶针后只到第 {pinned} 层，" +
                    $"高于目标第 {targetLayer} 层，顶针无法把它送下去。";
                return null;
            }

            // 只声明降 1 层，更深的落差由自由坍落完成。
            descents.Add((pinned, pinned - 1));
        }

        var temporaryTops = new List<int>();

        for (int layer = 1; layer <= layers; layer++)
        {
            if (!temporary[layer - 1] ||
                (layer > 1 && temporary[layer - 2]))
            {
                continue;
            }

            int top = layer;

            while (top + 1 <= layers && temporary[top])
            {
                top++;
            }

            temporaryTops.Add(top);
        }

        return new HPreState(
            a,
            temporary,
            origin,
            descents,
            temporaryTops);
    }

    /// <summary>
    /// §14.2 <c>BuildDescents(A, target) → (I, J)</c>，与 §13.6 第 5–6 步一致：
    ///
    /// <code>
    /// J = { 源层 s }                      → N 的针放在 s−1 层
    /// I = { 落点 d } ∪ { 每段临时晶体的最上层 } → B 的晶体放在对应层
    /// </code>
    ///
    /// <para>
    /// 这里的 I / J 直接用<b>前态（顶针前）的 1 起层号</b>保存，正好等于
    /// <see cref="TryBuildConstructedHPaperCore"/> 的 override 口径：
    /// B 在 <c>z+1 ∈ I</c> 的层放晶体，N 在 <c>z+1 ∈ J</c> 的层放针。
    /// </para>
    /// <para>
    /// <paramref name="extendTemporary"/> 对应第 7 步的确定性调整：
    /// 第一次回放失败时，把每段临时晶体向下扩一格再走第 5–7 步。
    /// 两种读法是固定的，不构成候选枚举。
    /// </para>
    /// </summary>
    private static (HashSet<int> I, HashSet<int> J) BuildDescents(
        HPreState plan,
        char[] targetCodes,
        int layers,
        bool extendTemporary,
        out string? failureReason)
    {
        failureReason = null;

        var iLayers = new HashSet<int>();
        var jLayers = new HashSet<int>();

        foreach ((int source, int destination) in plan.Descents)
        {
            jLayers.Add(source - 1);
            iLayers.Add(destination - 1);
        }

        foreach (int top in plan.TemporaryTops)
        {
            iLayers.Add(top);

            if (extendTemporary && top - 1 >= 1)
            {
                iLayers.Add(top - 1);
            }
        }

        iLayers.RemoveWhere(layer => layer < 1 || layer > layers);
        jLayers.RemoveWhere(layer => layer < 1 || layer > layers);

        if (plan.Descents.Count == 0 &&
            plan.TemporaryTops.Count == 0)
        {
            failureReason =
                "A 与目标之间没有降表、也没有临时晶体段（构造 H 不适用）。";
            return (new HashSet<int>(), new HashSet<int>());
        }

        return (iLayers, jLayers);
    }

    /// <summary>
    /// §14.3 <c>BuildAuxColumns(I, J, cap) → (B, E, N)</c>。
    /// <c>B[z]</c> 在 <c>z+1 ∈ I</c> 时放晶体，否则普通格；<c>E</c> 满高纯晶体；
    /// <c>N[z]</c> 在 <c>z+1 ∈ J</c> 时放针，否则普通格。
    /// </summary>
    private static (char[] B, char[] E, char[] N) BuildAuxColumns(
        HashSet<int> iLayers,
        HashSet<int> jLayers,
        int layers)
    {
        var b = new char[layers];
        var e = new char[layers];
        var n = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            b[layer] = iLayers.Contains(layer + 1)
                ? ShapeSignature.CrystalCode
                : ShapeSignature.SolidCode;
            e[layer] = ShapeSignature.CrystalCode;
            n[layer] = jLayers.Contains(layer + 1)
                ? ShapeSignature.PinCode
                : ShapeSignature.SolidCode;
        }

        return (b, e, n);
    }

    /// <summary>
    /// 临时段针槽读法（固定结构，不是枚举）：在 §13.6 第 5 步的源层针槽之外，
    /// 给每段临时晶体的每一层都补针，B 只保留最低落点 / 深落差的桥。
    /// 用于前三种读法都回放失败、但临时段需要逐层拆开的形态。
    /// </summary>
    private static (HashSet<int> I, HashSet<int> J) BuildDescentsTemporaryPins(
        HPreState plan,
        char[] targetCodes,
        int layers)
    {
        var iLayers = new HashSet<int>();
        var jLayers = new HashSet<int>();
        int minDestination = int.MaxValue;
        var deepTargets = new List<int>();

        for (int layer = 1; layer <= layers; layer++)
        {
            if (plan.A[layer - 1] != ShapeSignature.SolidCode ||
                plan.Origin[layer - 1] < 0)
            {
                continue;
            }

            int targetLayer = plan.Origin[layer - 1];

            if (layer == targetLayer - 1)
            {
                continue;
            }

            jLayers.Add(layer);
            minDestination = Math.Min(minDestination, layer);

            if (layer - targetLayer >= 2)
            {
                deepTargets.Add(targetLayer);
            }
        }

        if (jLayers.Count == 0)
        {
            return (iLayers, jLayers);
        }

        if (targetCodes[0] == ShapeSignature.SolidCode)
        {
            iLayers.Add(minDestination);
        }
        else
        {
            for (int layer = 1; layer < minDestination; layer++)
            {
                iLayers.Add(layer);
            }
        }

        foreach (int targetLayer in deepTargets)
        {
            if (targetLayer - 1 >= 1)
            {
                iLayers.Add(targetLayer - 1);
            }

            iLayers.Add(targetLayer);
        }

        for (int layer = 1; layer <= layers; layer++)
        {
            if (plan.Temporary[layer - 1])
            {
                jLayers.Add(layer);
            }
        }

        iLayers.RemoveWhere(layer => layer < 1 || layer > layers);
        jLayers.RemoveWhere(layer => layer < 1 || layer > layers);
        return (iLayers, jLayers);
    }
    /// <summary>
    /// 结构性降表（固定读法，不是枚举）：在 §13.6 第 5 步的 <c>J = {s}</c> 之外，
    /// 再按「落差 ≥ 2 层的坍落」补两种桥：
    /// <list type="bullet">
    /// <item><b>B 的深落差桥</b>：落差 ≥ 2 的普通格在落点上下两层各放一枚晶体桥；</item>
    /// <item><b>相邻源层的针槽</b>：两个源层只隔一层时，把中间那层也放针，避免先落下的格挡住后落下的格；</item>
    /// <item><b>临时晶体段</b>：段内还没有桥、也没有针槽时，在段顶补一枚晶体桥。</item>
    /// </list>
    /// <para>这些都由 A 的逐格位置直接算出，只作为前两种读法都回放失败后的第三种固定读法。</para>
    /// </summary>
    private static (HashSet<int> I, HashSet<int> J) BuildDescentsStructural(
        HPreState plan,
        char[] targetCodes,
        int layers)
    {
        var iLayers = new HashSet<int>();
        var jLayers = new HashSet<int>();
        var sources = new List<int>();
        int minDestination = int.MaxValue;
        var deepTargets = new List<int>();

        for (int layer = 1; layer <= layers; layer++)
        {
            if (plan.A[layer - 1] != ShapeSignature.SolidCode ||
                plan.Origin[layer - 1] < 0)
            {
                continue;
            }

            int targetLayer = plan.Origin[layer - 1];

            if (layer == targetLayer - 1)
            {
                continue;
            }

            int pinned = layer + 1;
            sources.Add(pinned - 1);
            minDestination = Math.Min(minDestination, layer);

            if (layer - targetLayer >= 2)
            {
                deepTargets.Add(targetLayer);
            }
        }

        if (sources.Count == 0)
        {
            return (iLayers, jLayers);
        }

        foreach (int pinLayer in sources)
        {
            jLayers.Add(pinLayer);
        }

        if (targetCodes[0] == ShapeSignature.SolidCode)
        {
            iLayers.Add(minDestination);
        }
        else
        {
            for (int layer = 1; layer < minDestination; layer++)
            {
                iLayers.Add(layer);
            }
        }

        var ordered = sources.Order().ToList();

        for (int index = 1; index < ordered.Count; index++)
        {
            if (ordered[index] - ordered[index - 1] <= 2)
            {
                for (int layer = ordered[index - 1] + 1;
                     layer < ordered[index];
                     layer++)
                {
                    jLayers.Add(layer);
                }
            }
        }

        foreach (int targetLayer in deepTargets)
        {
            if (targetLayer - 1 >= 1)
            {
                iLayers.Add(targetLayer - 1);
            }

            if (targetLayer >= 1)
            {
                iLayers.Add(targetLayer);
            }
        }

        for (int layer = 1; layer <= layers; layer++)
        {
            if (!plan.Temporary[layer - 1] ||
                (layer > 1 && plan.Temporary[layer - 2]))
            {
                continue;
            }

            int top = layer;

            while (top + 1 <= layers && plan.Temporary[top])
            {
                top++;
            }

            int low = layer;
            int high = top;
            int maxSelected = 0;
            bool hasPin = false;

            for (int probe = low; probe <= high; probe++)
            {
                if (iLayers.Contains(probe))
                {
                    maxSelected = Math.Max(maxSelected, probe);
                }

                if (jLayers.Contains(probe))
                {
                    hasPin = true;
                }
            }

            bool needBridge =
                targetCodes[0] == ShapeSignature.SolidCode ||
                (high - low + 1 >= 2 &&
                 !hasPin &&
                 (maxSelected == 0 || high - maxSelected >= 2));

            if (needBridge)
            {
                iLayers.Add(high);
            }
        }

        iLayers.RemoveWhere(layer => layer < 1 || layer > layers);
        jLayers.RemoveWhere(layer => layer < 1 || layer > layers);
        return (iLayers, jLayers);
    }
    /// <summary>
    /// P1 审计（docs §1 / §R1，口径按 §16.5 N3）：前态里<b>原生晶体</b>
    /// （非临时晶体）正下方为空即违规。
    ///
    /// <para>
    /// <b>只对「初始前态」调用</b>（工序第一张图）：构造 H 的前态是单张图，
    /// 所以这里天然只查第一张；多刀半破的中间态不做 P1 检查
    /// （中间态允许目标本身要求的晶下间隙）。任何 H 候选都必须在真实 Pin 之前通过这一条。
    /// </para>
    /// </summary>
    private static bool HasNativeCrystalGap(
        ShapeDocument preState,
        int column,
        bool[] temporary,
        out int gapLayer)
    {
        for (int layer = 1; layer < preState.LayerCount; layer++)
        {
            if (preState[layer, column].Type != CellType.Crystal ||
                (layer < temporary.Length && temporary[layer]))
            {
                continue;
            }

            if (preState[layer - 1, column].Type == CellType.Empty)
            {
                gapLayer = layer;
                return true;
            }
        }

        gapLayer = -1;
        return false;
    }
    /// <summary>
    /// §19.4：给顶针路线（构造 H）的工序单挂 Q1 子配方——「紧列 → 半破 → 零件」，
    /// 子配方最多一层（§5.1）。找不到时如实写进 <see cref="AngleWorkOrder.Limitations"/>，
    /// <b>不伪造</b>。
    /// </summary>
    private static AngleWorkOrder AttachQ1SubRecipe(
        AngleWorkOrder order)
    {
        if (order.Construction != AngleConstruction.H ||
            order.Q1SubRecipe is not null)
        {
            return order;
        }

        ShapeDocument? preState = order.Steps
            .FirstOrDefault(step =>
                step.Kind == AngleWorkStepKind.PreState &&
                step.Shape is not null)
            ?.Shape;

        if (preState is null ||
            ShapeSignature.IsTightColumn(preState, order.Column))
        {
            return order;
        }

        var limitations = new List<string>(order.Limitations);

        if (TryBuildColumnPartSubRecipe(
                preState,
                order.Column,
                out Recipe? q1,
                out string? description))
        {
            limitations.Add("Q1 子配方（§19.4 / §5.1）：" + description);

            return order with
            {
                Q1SubRecipe = q1,
                Limitations = limitations
            };
        }

        limitations.Add(
            AngleWorkOrderBuilder.Q1SubRecipeMissingText +
            (description is null ? string.Empty : "（" + description + "）"));

        return order with { Limitations = limitations };
    }

    /// <summary>
    /// 造 Q1 的一层子配方（§19.4 / §5.1）：
    /// <code>
    /// 紧列（Q1 的洞填临时晶体）+ 托柱（下一象限满高普通柱）
    ///     + 保险丝（上一象限：在每段临时晶体的最低层放晶体，跨刀口）
    ///   → 一刀半破（真实 ShapePhysics.Cut）
    ///   → 零件 Q1（托柱残料在装配时丢弃）
    /// </code>
    /// 半破时，Q1 的临时晶体经<b>环形相邻</b>与保险丝的晶体连成跨刀口晶体块，
    /// 整块碎掉；Q1 剩下的材料横向粘在紧列托柱上，位置不变。
    /// </summary>
    private static bool TryBuildColumnPartSubRecipe(
        ShapeDocument preState,
        int column,
        out Recipe? recipe,
        out string? description)
    {
        recipe = null;
        description = null;

        int parts = preState.PartCount;
        int layers = preState.LayerCount;

        if (parts != 4)
        {
            description = "半破需要四象限的刀口。";
            return false;
        }

        string partText = ShapeSignature.ColumnText(preState, column);

        int top = layers - 1;

        while (top >= 0 && partText[top] == ShapeSignature.EmptyCode)
        {
            top--;
        }

        if (top < 0)
        {
            description = "Q1 是空列，不需要子配方。";
            return false;
        }

        var holes = new List<int>();

        for (int layer = 0; layer <= top; layer++)
        {
            if (partText[layer] == ShapeSignature.EmptyCode)
            {
                holes.Add(layer);
            }
        }

        if (holes.Count == 0)
        {
            description = "Q1 已是紧列。";
            return false;
        }

        // 临时晶体与原生晶体纵向相邻 ⇒ 半破会误伤目标晶体：一层紧列不可行。
        foreach (int hole in holes)
        {
            if ((hole > 0 && partText[hole - 1] == ShapeSignature.CrystalCode) ||
                (hole + 1 <= top && partText[hole + 1] == ShapeSignature.CrystalCode))
            {
                description = $"第 {hole + 1} 层的洞与原生晶体纵向相邻。";
                return false;
            }
        }

        char color = CrystalColor(preState, column);

        var tight = partText.ToCharArray();
        var support = new char[layers];
        var fuse = new char[layers];

        Array.Fill(support, ShapeSignature.EmptyCode);
        Array.Fill(fuse, ShapeSignature.EmptyCode);

        for (int layer = 0; layer <= top; layer++)
        {
            support[layer] = ShapeSignature.SolidCode;
        }

        int maxHole = holes[^1];

        for (int layer = 0; layer <= maxHole; layer++)
        {
            fuse[layer] = ShapeSignature.SolidCode;
        }

        for (int index = 0; index < holes.Count; index++)
        {
            tight[holes[index]] = ShapeSignature.CrystalCode;

            // 每段临时晶体的最低层放一颗保险丝晶体（跨刀口）。
            if (index == 0 || holes[index] != holes[index - 1] + 1)
            {
                fuse[holes[index]] = ShapeSignature.CrystalCode;
            }
        }

        int supportColumn = Wrap(column + 1, parts);
        int fuseColumn = Wrap(column - 1, parts);
        int rotation = Wrap(-column, parts);

        var cutPreState = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            if (layer > top)
            {
                cutPreState[layer, column] = ShapeCell.Empty();
            }
            else if (partText[layer] == ShapeSignature.EmptyCode)
            {
                // 洞填临时晶体：紧列起点。
                cutPreState[layer, column] = ShapeCell.Crystal(color);
            }
            else
            {
                cutPreState[layer, column] = preState[layer, column].Clone();
            }

            cutPreState[layer, supportColumn] = CellFromCode(support[layer], color);
            cutPreState[layer, fuseColumn] = CellFromCode(fuse[layer], color);
        }

        if (!ShapeOps.IsStable(cutPreState) ||
            !AllColumnsTight(cutPreState))
        {
            description = "紧列起点的托柱 / 保险丝布局不稳定或不紧。";
            return false;
        }

        CutResult cut = ShapePhysics.Cut(
            ShapeOps.Rotate(cutPreState, rotation));

        if (!cut.Success || cut.West is null)
        {
            description = "半破子配方的真实 Cut 失败：" +
                          (cut.Error ?? "未知原因");
            return false;
        }

        ShapeDocument halfBreak = ShapeOps.Rotate(cut.West, -rotation);

        if (ShapeSignature.ColumnText(halfBreak, column) != partText)
        {
            description = "半破后的 Q1 与零件不符：" +
                          ShapeSignature.ColumnText(halfBreak, column);
            return false;
        }

        var steps = new List<RecipeStep>
        {
            new(
                "Q1 紧列起点（洞填临时晶体）+ 托柱 + 保险丝",
                cutPreState),
            new("半破（真实 Cut）→ 零件 Q1", halfBreak)
        };

        recipe = Recipe.CreateCut(cutPreState, halfBreak, rotation, steps);
        description =
            $"紧列起点 {new string(tight)} → 半破 → 零件 {partText}" +
            $"（托柱第 {supportColumn + 1} 象限、保险丝第 {fuseColumn + 1} 象限，" +
            $"顺转 {rotation}）。";

        return true;
    }

    /// <summary>所有非空列都是紧列（Q1 子配方的起点要求，§5.1 / §6 第 7 条）。</summary>
    private static bool AllColumnsTight(
        ShapeDocument document)
    {
        for (int part = 0; part < document.PartCount; part++)
        {
            if (!ShapeSignature.IsTightColumn(document, part))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 构造性构造 H 的单次尝试。<paramref name="liftGapSources"/> 决定是否把
    /// 「晶体正下方空段」里的普通格提到空段顶端（形成可碎的下降通道）。
    /// </summary>
    private static AngleWorkOrder? TryBuildConstructedHCore(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        AngleBuildKind kind,
        AngleAlgorithm algorithm,
        bool liftGapSources,
        out string? failureReason)
    {
        failureReason = null;

        if (parts != 4)
        {
            failureReason = "构造 H 需要四象限的环（A | B | E | N）。";
            return null;
        }

        if (layers < 2 || layers < target.LayerCount)
        {
            failureReason = "cap 装不下目标列的完整高度，构造 H 的前态放不下。";
            return null;
        }

        // 顶针会在前态第 1 层非空的位置补针：目标底层必须是针或空。
        if (target[0, column].Type is not (CellType.Pin or CellType.Empty))
        {
            failureReason = "构造 H 的顶针会在第 1 层补针，目标底层必须是针或空。";
            return null;
        }

        char color = CrystalColor(target, column);

        var targetCodes = new char[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            targetCodes[layer] = CodeOf(
                layer < target.LayerCount
                    ? target[layer, column]
                    : ShapeCell.Empty());
        }

        // ── A：目标列整体下移一层（Pin 会把整体抬起一层，正号抵消）──

        var columnCells = new ShapeCell[layers];

        for (int layer = 0; layer < layers; layer++)
        {
            columnCells[layer] = layer + 1 < target.LayerCount
                ? target[layer + 1, column].Clone()
                : ShapeCell.Empty();
        }

        var descents = new List<(int D, int S)>();

        bool MoveSource(int d, int s)
        {
            if (d < 1 || s <= d || s > layers || d >= layers)
            {
                return false;
            }

            if (columnCells[d - 1].Type == CellType.Empty ||
                columnCells[s - 1].Type != CellType.Empty)
            {
                return false;
            }

            // 源格必须就是目标 d 层的材料（A 的下移保证它在 d−1 层）。
            if (CodeOf(columnCells[d - 1]) != targetCodes[d])
            {
                return false;
            }

            columnCells[s - 1] = columnCells[d - 1];
            columnCells[d - 1] = ShapeCell.Empty();
            descents.Add((d, s));

            return true;
        }

        /*
         * ⓪（可选）晶体正下方的空段：通道最下面的普通格是源，通道顶端是源层
         * s。把它提到 s 层，A 里就多出一段「可碎掉的通道」——目标列的原生晶体
         * 与临时晶体之间被这枚普通格隔开，碎晶不会连带炸掉目标晶体。
         */
        if (liftGapSources)
        {
            for (int bottom = 1; bottom < layers; bottom++)
            {
                if (targetCodes[bottom] != ShapeSignature.CrystalCode ||
                    targetCodes[bottom - 1] != ShapeSignature.EmptyCode)
                {
                    continue;
                }

                int lo = bottom - 1;

                while (lo > 0 && targetCodes[lo - 1] == ShapeSignature.EmptyCode)
                {
                    lo--;
                }

                int d = lo - 1;

                if (d < 1 || targetCodes[d] != ShapeSignature.SolidCode)
                {
                    continue;
                }

                MoveSource(d, bottom - 1);
            }
        }

        /*
         * ① 目标底层为空 ⇒ 前态第 1 层必须为空：Pin 只给「第 1 层非空」的列补针，
         * 而目标第 1 层是空的，所以压在第 1 层的最下方连续普通块要整体上抬一层，
         * 顶针后再按「从下到上」逐对落回（每一对都压在前一对刚补上的位置）。
         */
        if (targetCodes[0] != ShapeSignature.PinCode &&
            columnCells[0].Type != CellType.Empty)
        {
            int run = 1;

            while (run + 1 < layers &&
                   targetCodes[run + 1] == ShapeSignature.SolidCode)
            {
                run++;
            }

            bool lifted = targetCodes[1] == ShapeSignature.SolidCode;

            for (int d = run; lifted && d >= 1; d--)
            {
                lifted = MoveSource(d, d + 1);
            }

            if (!lifted)
            {
                failureReason =
                    "目标底层为空，但第 1 层的普通块上方没有空位可抬，" +
                    "前态第 1 层无法清空（Pin 会补出多余的针）。";
                return null;
            }
        }

        // ── 目标列的洞填成临时晶体（通道 + 紧列化）：第 1 层除外 ──

        var temporary = new bool[layers];

        void FillRun(int lo, int hi)
        {
            for (int layer = Math.Max(lo, 0); layer <= hi && layer < layers; layer++)
            {
                if (columnCells[layer].Type != CellType.Empty)
                {
                    continue;
                }

                columnCells[layer] = ShapeCell.Crystal(color);
                temporary[layer] = true;
            }
        }

        foreach ((int d, int s) in descents)
        {
            int anchor = d - 1;

            if (columnCells[anchor].Type != CellType.Empty)
            {
                continue;
            }

            int lo = anchor;
            int hi = anchor;

            while (lo > 0 && columnCells[lo - 1].Type == CellType.Empty)
            {
                lo--;
            }

            while (hi + 1 < layers && columnCells[hi + 1].Type == CellType.Empty)
            {
                hi++;
            }

            // 目标底层不是针 ⇒ 第 1 层必须保持为空。
            FillRun(
                targetCodes[0] == ShapeSignature.PinCode ? lo : Math.Max(lo, 1),
                hi);
        }

        /*
         * 目标底层是针、但前态第 1 层仍为空：Pin 只会给「第 1 层非空」的列补针，
         * 所以第 1 层必须垫临时晶体（Pin 把它补成针，晶体本身抬到第 2 层）。
         * 它必须与 B 的晶体桥同层相连才会被引爆柱带走 ⇒ 层 1 并入 I。
         */
        if (targetCodes[0] == ShapeSignature.PinCode &&
            columnCells[0].Type == CellType.Empty)
        {
            FillRun(0, 0);
        }

        // 紧列化：把「底到顶」之间的洞也填成临时晶体（末尾的空层不算洞）。
        int top = layers - 1;

        while (top >= 0 && columnCells[top].Type == CellType.Empty)
        {
            top--;
        }

        for (int layer = targetCodes[0] == ShapeSignature.PinCode ? 0 : 1;
             layer <= top;
             layer++)
        {
            if (columnCells[layer].Type == CellType.Empty)
            {
                FillRun(layer, layer);
            }
        }

        /*
         * 紧列化只是「让 A 可造」的增强项：临时晶体本身也要过五项条件
         * （条件四：晶体下方有洞时，下方还必须有一枚普通格）。填完若不合法，
         * 就整体退回「不填洞」的形态——A 带洞，后面用交换链子配方证明可造。
         */
        if (!IsLegalColumnCells(columnCells))
        {
            for (int layer = 0; layer < layers; layer++)
            {
                if (!temporary[layer])
                {
                    continue;
                }

                columnCells[layer] = ShapeCell.Empty();
                temporary[layer] = false;
            }

            if (!IsLegalColumnCells(columnCells))
            {
                failureReason =
                    "前态 A 不论是否填临时晶体都过不了五项条件" +
                    $"（A = {ColumnCodesText(columnCells)}）。";
                return null;
            }
        }

        // ── I / J ──

        var iLayers = new HashSet<int>();
        var jLayers = new HashSet<int>();

        /*
         * 每段临时晶体取一个「锚层」（段的最高层，顶针后层号 = 最高下标 + 1）：
         * B 在那一层放晶体，临时晶体才能与 B 的晶体桥同层相连、被引爆柱带走。
         * 同时检查临时晶体没有和原生晶体纵向融合（否则碎晶会连带炸掉目标晶体）。
         */
        for (int layer = 0; layer < layers; layer++)
        {
            if (!temporary[layer] || (layer > 0 && temporary[layer - 1]))
            {
                continue;
            }

            int segmentTop = layer;

            while (segmentTop + 1 < layers && temporary[segmentTop + 1])
            {
                segmentTop++;
            }

            int below = layer - 1;
            int above = segmentTop + 1;

            /*
             * 临时晶体段与原生晶体纵向相邻 ⇒ 碎这段会连带炸掉目标晶体：
             * 把这个临时段整体撤掉（A 相应多一个洞，由子配方证明可造）。
             */
            if ((below >= 0 && columnCells[below].Type == CellType.Crystal) ||
                (above < layers && columnCells[above].Type == CellType.Crystal))
            {
                for (int index = layer; index <= segmentTop; index++)
                {
                    columnCells[index] = ShapeCell.Empty();
                    temporary[index] = false;
                }

                continue;
            }

            iLayers.Add(segmentTop + 1);
        }

        // 撤掉部分临时晶体后仍要过五项条件；不过就整体退回「不填洞」的形态。
        if (!IsLegalColumnCells(columnCells))
        {
            for (int layer = 0; layer < layers; layer++)
            {
                if (!temporary[layer])
                {
                    continue;
                }

                columnCells[layer] = ShapeCell.Empty();
                temporary[layer] = false;
            }

            if (!IsLegalColumnCells(columnCells))
            {
                failureReason =
                    "前态 A 不论是否填临时晶体都过不了五项条件" +
                    $"（A = {ColumnCodesText(columnCells)}）。";
                return null;
            }
        }

        foreach ((int d, int s) in descents)
        {
            jLayers.Add(s);
        }

        foreach ((int d, int s) in descents)
        {
            for (int layer = d; layer <= s - 1; layer++)
            {
                iLayers.Add(layer);
            }
        }

        var preState = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            preState[layer, column] = columnCells[layer].Clone();
        }

        int bColumn = Wrap(column + 1, parts);
        int eColumn = Wrap(column + 2, parts);
        int nColumn = Wrap(column - 1, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            // B：z+1 ∈ I → 晶体，否则普通格（整列紧列）。
            preState[layer, bColumn] = iLayers.Contains(layer + 1)
                ? ShapeCell.Crystal(color)
                : ShapeCell.Shape('C', 'u');

            // E：满高纯晶体柱。
            preState[layer, eColumn] = ShapeCell.Crystal(color);

            // N：z+1 ∈ J → 针，否则普通格（整列紧列）。
            preState[layer, nColumn] = jLayers.Contains(layer + 1)
                ? ShapeCell.Pin()
                : ShapeCell.Shape('C', 'u');
        }

        if (ShapeSignature.ColumnText(preState, column) ==
            ShapeSignature.ColumnText(target, column))
        {
            failureReason = "构造 H 的前态目标列与目标列相同（循环）。";
            return null;
        }

        // A 若带洞（第 1 层必须为空），用交换链子配方证明可造。
        bool tightTargetColumn = ShapeSignature.IsTightColumn(preState, column);
        Recipe? subRecipe = null;

        var limitations = BaseLimitations(
            "构造 H（按 I/J 构造性生成）：A = 目标列下移一层，B 按 z+1 ∈ I 放晶体，" +
            "E 是满高纯晶体柱，N 按 z+1 ∈ J 放针；一次 Pin 同时引爆并整批落下。");

        limitations.Add(
            $"下降表（扫描得出）：{string.Join("、", descents.Select(pair => $"{pair.S}→{pair.D}"))}；" +
            $"I = {{{string.Join(",", iLayers.Order())}}}，J = {{{string.Join(",", jLayers.Order())}}}。");

        var auxiliaryColumns = new[] { nColumn, bColumn, eColumn };

        var steps = new List<AngleWorkStep>
        {
            new(
                "碎晶前的角（构造 H：A | 晶体桥柱 B | 满高纯晶体柱 E | 针槽柱 N）",
                preState,
                AngleWorkStepKind.PreState)
        };

        // P1 硬约束（docs §1 / §R1）：原生晶体正下方必须是普通格。
        if (HasNativeCrystalGap(preState, column, temporary, out int plainGapLayer))
        {
            failureReason =
                $"A 第 {plainGapLayer + 1} 层的原生晶体下方为空（P1 不满足），作废这条前态。";
            return null;
        }

        // 验收条件一 / 七：前态每列五项条件 + 辅助列紧列。
        if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
        {
            failureReason = DescribeIllegalPreState(preState, badPreStateColumn);
            return null;
        }

        if (!ShapeOps.IsStable(preState))
        {
            failureReason = "构造 H 的前态不稳定（一次下落后会变形）。";
            return null;
        }

        // A 带洞 ⇒ 必须是「可造」的：附带交换链子配方（rank 0），并端到端校验。
        if (!tightTargetColumn)
        {
            if (!ExchangeChain.TryBuild(preState, out subRecipe) ||
                subRecipe is null ||
                subRecipe.Rank != 0 ||
                !Replay.Verify(subRecipe, preState, out _))
            {
                failureReason =
                    "前态 A 的第 1 层必须为空（否则 Pin 补针），A 因此带洞" +
                    $"（A = {ShapeSignature.ColumnText(preState, column)}）；" +
                    "交换链子配方没能装配出这份前态。";
                return null;
            }

            limitations.Add(
                "前态 A 的第 1 层为空且带洞，附带交换链子配方（rank 0）证明可造。");
        }

        // ── 一次顶针：真实回放 ──

        PhysicsResult pinResult = ShapePhysics.Pin(preState);

        if (!pinResult.Success || pinResult.Document is null)
        {
            failureReason = "ShapePhysics.Pin 失败：" + (pinResult.Error ?? "未知原因");
            return null;
        }

        ShapeDocument pinned = pinResult.Document;

        if (!MatchesTargetColumn(
                pinned,
                target,
                column,
                layers,
                out string? difference))
        {
            failureReason = "一次顶针后的目标列与目标不符：" + difference;
            return null;
        }

        // 结构性核对：B 的晶体桥必须已被 E 的整柱破碎连带删除。
        for (int layer = 0; layer < layers; layer++)
        {
            if (pinned[layer, bColumn].Type == CellType.Crystal)
            {
                failureReason = "顶针后晶体桥没有全部删除，构造 H 的前提不成立。";
                return null;
            }
        }

        steps.Add(new AngleWorkStep(
            "顶针：一次 Pin 把整体抬一层，E 顶端超限整簇碎裂并连带删掉 B 的晶体桥，" +
            "源普通格沿通道落到各自的目标层",
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
            TargetColumnHasSubRecipe = true,
            Construction = AngleConstruction.H,
            Algorithm = algorithm,
            Rotation = 0,
            AuxiliaryColumns = auxiliaryColumns,
            SubRecipe = subRecipe,
            Rank = 1,
            Limitations = limitations
        };
    }

    /// <summary>把一格映射回列字符串用的短代码（只为扫描下降表，不参与构造）。</summary>
    private static char CodeOf(
        ShapeCell cell)
    {
        return cell.Type switch
        {
            CellType.Crystal => ShapeSignature.CrystalCode,
            CellType.Pin => ShapeSignature.PinCode,
            CellType.Shape => ShapeSignature.SolidCode,
            _ => ShapeSignature.EmptyCode
        };
    }

    /// <summary>把一列格子写成短代码列文本（扫描 / 诊断用）。</summary>
    private static string ColumnCodesText(
        IReadOnlyList<ShapeCell> cells)
    {
        var chars = new char[cells.Count];

        for (int index = 0; index < cells.Count; index++)
        {
            chars[index] = CodeOf(cells[index]);
        }

        return new string(chars);
    }

    /// <summary>一列格子的五项条件判定（扫描 / 诊断用）。</summary>
    private static bool IsLegalColumnCells(
        IReadOnlyList<ShapeCell> cells)
    {
        return ShapeSignature.CheckColumn(ColumnCodesText(cells)).IsLegal;
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

            // 验收条件一（§6 第 1 条）：第 k 轮的前态也必须每列合法。
            if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
            {
                return Incomplete(
                    column,
                    kind,
                    $"第 {round + 1} 轮" +
                    DescribeIllegalPreState(preState, badPreStateColumn),
                    limitations,
                    steps,
                    AngleConstruction.G,
                    rotation,
                    auxiliaryColumns,
                    new AngleWorkStep(
                        $"第 {round + 1} 轮非法前态（已作废）：" +
                        ShapeSignature.ColumnText(preState, badPreStateColumn),
                        preState,
                        AngleWorkStepKind.Note));
            }

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

            // 每一刀 = 一次 Cut + 一次落定，合成一个 Cut 步，各带该刀落定后的结果。
            steps.Add(new AngleWorkStep(
                rounds == 1
                    ? (rotation == 0
                        ? "切割（碎晶并落定）：沿 n/2 刀口半破，打碎桥柱晶体，材料落位"
                        : $"切割（碎晶并落定）：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                          "打碎桥柱晶体，材料落位")
                    : $"第 {round + 1}/{rounds} 轮切割（碎晶并落定）：打碎桥柱晶体，" +
                      "源普通格下降一层",
                collapsedBack,
                AngleWorkStepKind.Cut));

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
            TargetColumnHasSubRecipe = true,
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

        // 验收条件一（§6 第 1 条）：兜底填充+暴破的前态也必须每列合法。
        if (IsRejectedPreState(target, column, preState, out int badPreStateColumn))
        {
            return Incomplete(
                column,
                kind,
                DescribeIllegalPreState(preState, badPreStateColumn),
                limitations,
                construction: AngleConstruction.FillBreak,
                rotation: rotation,
                auxiliaryColumns: auxiliaryColumns,
                diagnostic: new AngleWorkStep(
                    "非法前态（已作废）：" +
                    ShapeSignature.ColumnText(preState, badPreStateColumn),
                    preState,
                    AngleWorkStepKind.Note));
        }

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

        // 「切 + 落」合成一个 Cut 步：Shape 用落定后的真实结果。
        steps.Add(new AngleWorkStep(
            rotation == 0
                ? "切割（碎晶并落定）：沿 n/2 刀口半破，打碎填充晶体与桥柱晶体，" +
                  "材料落位后缺口由切割造出"
                : $"切割（碎晶并落定）：先顺转 {rotation} 个象限，再沿 n/2 刀口半破，" +
                  "打碎填充晶体与桥柱晶体，材料落位后缺口由切割造出",
            collapsedBack,
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
            TargetColumnHasSubRecipe = true,
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
        bool twoAuxiliaryColumns,
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
                preState[s + 2, bColumn] = ShapeCell.Pin();
            }

            if (twoAuxiliaryColumns)
            {
                /*
                 * 两列辅助（支撑 B + 保险丝 N）：第三列 E 留空。
                 * N 是「底部晶体 + 中间普通格 + 闸门层晶体」的紧列：
                 * 闸门晶体与目标列的临时晶体跨 3–0 环缝横向相连，一刀同碎。
                 * 旧实现把闸门晶体放在 E、N 只放顶针；整根 N 在切割时被丢弃，
                 * 顶针与 E 都是纯冗余，实测（12 / 14 层双闸门）删掉后回放逐格不变。
                 */
                for (int layer = 0; layer <= d && layer < layers; layer++)
                {
                    preState[layer, nColumn] = ShapeCell.Shape('C', 'u');
                }

                preState[d, nColumn] = ShapeCell.Crystal(color);
            }
            else
            {
                // 三列辅助（回退）：闸门晶体放在满高桥柱 E 上，N 先留空。
                FillPillar(preState, eColumn);
                preState[d, eColumn] = ShapeCell.Crystal(color);
            }
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

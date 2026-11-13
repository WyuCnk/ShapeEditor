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
                return tailOrder;
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
                return tierOrder.Algorithm == AngleAlgorithm.None
                    ? Label(tierOrder, algorithm, attempt: false)
                    : tierOrder;
            }
        }

        /*
         * 三·补、路线 F（n = 4、底格非针、D 不成立、F 成立）：
         * 先试【构造 G】——§15 的四象限单孔环下落（环伙伴 B + 保险丝柱 B′ + 一刀），
         * 它能把「长段（≥3）恢复」锁成恰好一层；
         * 再试 §11 的通用实现：CanonicalPlan（六个参数全部由扫描得出）→
         * 有界变体 V1–V5 → 真实物理回放，其恢复手段是【构造 D 的碎晶等价路线】。
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
                return constructG;
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
                return generic;
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
            return Label(legacy, AngleAlgorithm.Algorithm4, attempt: false);
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
         * 构造 D（定向碎晶）：只允许「抬升填法」——缺口下方的普通格被抬到
         * 缺口顶端，让出的位置填成临时晶体，碎掉后普通格落回目标层。
         * 这条路线不把缺口整段填满，属于论文构造 D；回放不通过时才继续往下试。
         */
        if (parts == 4)
        {
            AngleWorkOrder directional = TryBuildDirectionalBreak(
                target,
                column,
                layers,
                parts,
                half,
                kind);

            if (directional.IsComplete)
            {
                return directional;
            }

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

    /// <summary>构造 D 的兜底尝试：只走「抬升填法」（不把缺口整段填满）。</summary>
    private static AngleWorkOrder TryBuildDirectionalBreak(
        ShapeDocument target,
        int column,
        int layers,
        int parts,
        int half,
        AngleBuildKind kind)
    {
        AngleWorkOrder order = TryBuildFillBreak(
            target,
            column,
            layers,
            parts,
            half,
            kind,
            FillPlanMode.LiftOnly);

        if (!order.IsComplete)
        {
            return order;
        }

        var limitations = new List<string>(order.Limitations);

        string description =
            "构造 D（定向碎晶）：缺口下方的普通格先抬升到缺口顶端（§7.5 规则二），" +
            "让出的位置填成临时晶体后单独炸掉——不把缺口整段填满；" +
            "真实 Cut + 落定逐格核对通过。";

        if (limitations.Count > 0)
        {
            limitations[0] = description;
        }
        else
        {
            limitations.Add(description);
        }

        limitations.Add(
            "辅助列最少化：只保留支撑（托柱）+ 爆破（桥柱）两列，第三象限整列为空。");

        return order with
        {
            Construction = AngleConstruction.D,
            Limitations = limitations
        };
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

    /// <summary>
    /// 核心 / 尾部拆分（docs §17.5 第 1 条）：把目标列拆成
    /// 「核心（最高晶体及以下）」与「尾部（以上）」。
    ///
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
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

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
            $"核心 / 尾部拆分：核心 = 第 1–{highest + 1} 层（最高晶体及以下），" +
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

        return new AngleWorkOrder(
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
        };
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
            return TryAlternativeAlgorithm6(
                target,
                column,
                layers,
                kind);
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
         * 不同层、而晶体还必须留在原位——一次顶针做不到。
         *
         * 这类形态不再交给「填充+暴破」糊过去：按 TODO.md §7.4 的实测配方，
         * 走「抬升一格（§7.5 规则二）+ 定点碎晶两刀（构造 D）+ 托柱该层换
         * 顶针（规则一/构造 E）+ 构造 C」。`.TT.c` 就是这条链。
         */
        /*
         * 晶体下方有两层以上缺口时，规范托柱可能够不着顶针后的晶体。
         * 只要不是「.T..c」这种单个普通格、单个晶体、缺口紧贴晶体的最简形态，
         * 就先试「全高度托柱 + 结构化落块柱」的塔式枚举；
         * 最简形态仍走规范塔 `..Tc.`，避免自子形态。
         */
        int leadingEmpty = 0;

        while (leadingEmpty < target.LayerCount &&
               target[leadingEmpty, column].Type == CellType.Empty)
        {
            leadingEmpty++;
        }

        int crystalCount = 0;

        for (int probe = 0; probe < target.LayerCount; probe++)
        {
            if (target[probe, column].Type == CellType.Crystal)
            {
                crystalCount++;
            }
        }

        bool simpleSingle =
            CountSolids(normalizedTower) <= 1 &&
            crystalCount <= 1 &&
            leadingEmpty <= 1;

        if (CountGapsBelowHighest(target, column) >= 2 &&
            !simpleSingle)
        {
            AngleWorkOrder? broadTower = TryBuildTowerStyleOrder(
                target,
                column,
                layers,
                parts,
                kind,
                allowBottomPin: false,
                AngleAlgorithm.Algorithm6,
                forceBroad: true);

            if (broadTower is not null)
            {
                return broadTower;
            }
        }

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

            return shiftedTower[0] != ShapeSignature.EmptyCode
                ? TryAlternativeAlgorithm6(target, column, layers, kind)
                : null;
        }

        /*
         * 塔列优先用「缺口错开层」的变体：普通格不留在缺口下方，而是紧贴
         * 晶体，由落块柱完成最后一层位移。这样塔的目标列不会再包含一个
         * 正在求解的子形态（例如 `.T..c` 的正规化塔 `.T.c.` 里含 `.T.c`），
         * 否则造塔本身又要多一次顶破。正规化塔仍作为后备候选。
         */
        foreach (string towerColumn in EnumerateTowerColumns(
                     target,
                     column,
                     layers,
                     normalizedTower))
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

        /*
         * 晶体下方有两层以上缺口时，规范托柱可能够不着顶针后的晶体；
         * 最后再用「全高度托柱 + 结构化落块柱」的塔式枚举试一次
         * （T.T..c / .TT..c 一族）。标准形态在上面的规范枚举里已经返回，
         * 不会走到这里。
         */
        if (CountGapsBelowHighest(target, column) >= 2)
        {
            AngleWorkOrder? broadTower = TryBuildTowerStyleOrder(
                target,
                column,
                layers,
                parts,
                kind,
                allowBottomPin: false,
                AngleAlgorithm.Algorithm6,
                forceBroad: true);

            if (broadTower is not null)
            {
                return broadTower;
            }
        }

        /*
         * 算法 6 的「底块提升」在普通格无法直接抬进缺口时（例如 T.T..c：
         * 最下方普通块上方还是空，构造 H 的托柱补不出来），交给 §11 的
         * 通用计划；只有这种底层非空的形态才走昂贵后备，底层已空的普通形态
         * 继续交给原有兜底链，避免把简单形态拖慢。
         */
        return shiftedTower[0] != ShapeSignature.EmptyCode
            ? TryAlternativeAlgorithm6(target, column, layers, kind)
            : null;
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
    /// 最高晶体下方（第 1 层到晶体下方一层）的空格数。
    /// 用来区分「单层缺口」与「多层缺口」这类结构谓词，层号全部由扫描得出。
    /// </summary>
    private static int CountGapsBelowHighest(
        ShapeDocument target,
        int column)
    {
        int highest = AngleClassifier.HighestCrystalLayer(target, column);

        if (highest < 0)
        {
            return 0;
        }

        int gaps = 0;

        for (int layer = 1; layer < highest; layer++)
        {
            if (target[layer, column].Type == CellType.Empty)
            {
                gaps++;
            }
        }

        return gaps;
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
    /// 塔列候选：优先「缺口错开层」的变体，其次压缩缺口的变体，
    /// 最后才是正规化塔列。全部由沿列扫描得出。
    /// </summary>
    private static IEnumerable<string> EnumerateTowerColumns(
        ShapeDocument target,
        int column,
        int layers,
        string normalizedTower)
    {
        string? offset = OffsetGapBelowCrystal(normalizedTower);

        if (offset is not null &&
            !string.Equals(offset, normalizedTower, StringComparison.Ordinal))
        {
            yield return offset;
        }

        yield return normalizedTower;

        string? compressed = CompressGapBelowCrystal(
            target,
            column,
            layers);

        if (compressed is not null &&
            !string.Equals(compressed, normalizedTower, StringComparison.Ordinal) &&
            !string.Equals(compressed, offset, StringComparison.Ordinal))
        {
            yield return compressed;
        }
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
        string? shifted = BuildShiftedTowerColumn(
            target,
            column,
            layers);

        // 塔列第 1 层必须为空：顶针会在前态底层非空的位置补针，
        // 补出来的针会顶到目标角第 1 层。
        return shifted is not null &&
               shifted[0] == ShapeSignature.EmptyCode
            ? shifted
            : null;
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
            TargetColumnHasSubRecipe = true,
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

    /// <summary>
    /// 把一列里的洞填成同层晶体（docs §5 规则六 / §6 第 7 条）：
    /// 辅助列必须是紧列；需要「下降层」时不要留洞，而在那一层放一颗晶体，
    /// 半破 / 引爆时与临时晶体一起碎掉（<c>half-side-work-order.md</c> §2.1）。
    /// 末尾的空层不算洞，保留。
    /// </summary>
    private static void TightenAuxiliaryColumn(
        ShapeDocument document,
        int column,
        char crystalColor)
    {
        int target = Wrap(column, document.PartCount);
        int top = -1;

        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            if (document[layer, target].Type != CellType.Empty)
            {
                top = layer;
            }
        }

        for (int layer = 0; layer <= top; layer++)
        {
            if (document[layer, target].Type == CellType.Empty)
            {
                document[layer, target] = ShapeCell.Crystal(crystalColor);
            }
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
    /// 塔式构造的托针柱（N）候选：底部连续顶针，再往上放普通格当「托柱帽」。
    ///
    /// <para>
    /// 规范值由目标列的普通格位置决定：5 层的 <c>.TT.c</c>、<c>T.T.c</c>、
    /// <c>P.T.c</c> 优先得到 <c>PPPT.</c>（三道针 + 一道托帽）。
    /// 晶体离普通格更远时（<c>T.T..c</c>、<c>P.T..c</c>）需要更高的托帽才能接住
    /// 顶针后的晶体，所以规范值之后按「高度从低到高、针数从多到少」补全
    /// 所有 <c>P…PT…T</c> 结构；这些都是由扫描出的高度拼出来的，不按层数打表。
    /// </para>
    /// </summary>
    private static IEnumerable<string> EnumerateTowerSupportColumns(
        ShapeDocument target,
        int column,
        int layers,
        bool broad)
    {
        int topSolid = LastSolidLayer(target, column, layers);

        if (topSolid < 0)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int pins = topSolid + 1;
        int height = Math.Min(topSolid + 2, layers);

        if (pins < height && pins <= layers)
        {
            string canonical =
                new string(ShapeSignature.PinCode, pins) +
                new string(ShapeSignature.SolidCode, height - pins) +
                new string(ShapeSignature.EmptyCode, layers - height);

            if (seen.Add(canonical))
            {
                yield return canonical;
            }
        }

        if (!broad)
        {
            yield break;
        }

        for (int candidateHeight = 2;
             candidateHeight <= layers;
             candidateHeight++)
        {
            for (int candidatePins = 1;
                 candidatePins < candidateHeight;
                 candidatePins++)
            {
                string text =
                    new string(ShapeSignature.PinCode, candidatePins) +
                    new string(
                        ShapeSignature.SolidCode,
                        candidateHeight - candidatePins) +
                    new string(
                        ShapeSignature.EmptyCode,
                        layers - candidateHeight);

                if (seen.Add(text))
                {
                    yield return text;
                }
            }
        }

        string full = new string(ShapeSignature.SolidCode, layers);

        if (seen.Add(full))
        {
            yield return full;
        }
    }

    /// <summary>
    /// 塔式构造的落块柱（B）：最高普通格以下的每一层，
    /// 目标列是普通格的写普通格，其余（缺口/底针位置）写晶体。
    ///
    /// <para>
    /// 落块柱的晶体与引爆柱连成一簇，Pin 后整簇碎掉；留下的普通格
    /// 与角列的普通格同层相邻，负责把下落中的材料托在目标层。
    /// 底针角还可以把底针位置写成普通格（第二个变体）。
    /// </para>
    /// <para>
    /// 最后一个变体把<b>目标普通格之间的缺口也填成普通格</b>（只有底层是晶体）。
    /// TODO.md §7.7 的 <c>.T.T.c</c> 用的就是它：缺口若填晶体，Pin 后会随引爆柱
    /// 整簇碎掉，桥柱的普通格失去下方支撑，会带着目标列的普通格一起下落
    /// （实测得到 <c>.TT..c</c>）；填普通格才能让落块柱在 Pin 后仍是一条
    /// 由托针柱托住的实心链。
    /// </para>
    /// </summary>
    private static IEnumerable<string> EnumerateTowerBridgeColumns(
        ShapeDocument target,
        int column,
        int layers,
        bool pinAsSolidFirst,
        bool broad)
    {
        int topSolid = LastSolidLayer(target, column, layers);

        if (topSolid < 0)
        {
            yield break;
        }

        var variants = pinAsSolidFirst
            ? new[] { true, false }
            : new[] { false, true };

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (bool pinAsSolid in variants)
        {
            var cells = new char[layers];

            Array.Fill(cells, ShapeSignature.EmptyCode);

            for (int layer = 0;
                 layer <= topSolid && layer < layers;
                 layer++)
            {
                cells[layer] = target[layer, column].Type switch
                {
                    CellType.Shape => ShapeSignature.SolidCode,
                    CellType.Pin => pinAsSolid
                        ? ShapeSignature.SolidCode
                        : ShapeSignature.CrystalCode,
                    CellType.Crystal => ShapeSignature.CrystalCode,
                    _ => ShapeSignature.CrystalCode
                };
            }

            string text = new string(cells);

            if (seen.Add(text))
            {
                yield return text;
            }
        }

        // 缺口也填普通格：底层仍是晶体（与引爆柱同簇），其余全实心。
        var gapFilled = new char[layers];

        Array.Fill(gapFilled, ShapeSignature.EmptyCode);

        gapFilled[0] = ShapeSignature.CrystalCode;

        for (int layer = 1;
             layer <= topSolid && layer < layers;
             layer++)
        {
            gapFilled[layer] = ShapeSignature.SolidCode;
        }

        string gapFilledText = new string(gapFilled);

        if (seen.Add(gapFilledText))
        {
            yield return gapFilledText;
        }

        if (!broad)
        {
            yield break;
        }

        /*
         * 结构化补全：落块柱的作用是「把普通格送到目标层」。
         * 除了目标列自身的普通格，还可以在任意一层加 1–2 枚普通格当刹车/垫块
         * （文档 §5 规则三：断点由「还没删的临时晶体」提供，位置必须由扫描确定）。
         * 这里枚举「底晶 + 0/1/2 枚普通格」的全部结构，层号全部来自循环变量，
         * 不按层数或角形打表；候选仍要过真实 Pin 回放。
         */
        string pillar = new string(ShapeSignature.SolidCode, layers);

        if (seen.Add(pillar))
        {
            yield return pillar;
        }

        string crystalPillar =
            ShapeSignature.CrystalCode +
            new string(ShapeSignature.SolidCode, layers - 1);

        if (seen.Add(crystalPillar))
        {
            yield return crystalPillar;
        }

        string bottomCrystalOnly =
            ShapeSignature.CrystalCode +
            new string(ShapeSignature.EmptyCode, layers - 1);

        if (seen.Add(bottomCrystalOnly))
        {
            yield return bottomCrystalOnly;
        }

        for (int first = 1; first < layers; first++)
        {
            var single = new char[layers];

            Array.Fill(single, ShapeSignature.EmptyCode);

            single[0] = ShapeSignature.CrystalCode;
            single[first] = ShapeSignature.SolidCode;

            string singleText = new(single);

            if (seen.Add(singleText))
            {
                yield return singleText;
            }

            for (int second = first + 1; second < layers; second++)
            {
                var pair = (char[])single.Clone();

                pair[second] = ShapeSignature.SolidCode;

                string pairText = new(pair);

                if (seen.Add(pairText))
                {
                    yield return pairText;
                }
            }
        }
    }

    /// <summary>目标列里最高普通格所在的层（0 起）；没有时返回 -1。</summary>
    private static int LastSolidLayer(
        ShapeDocument target,
        int column,
        int layers)
    {
        int topSolid = -1;
        int limit = Math.Min(layers, target.LayerCount);

        for (int layer = 0; layer < limit; layer++)
        {
            if (target[layer, column].Type == CellType.Shape)
            {
                topSolid = layer;
            }
        }

        return topSolid;
    }

    /// <summary>
    /// 用户塔式构造 H（TODO.md §7.2）：从目标列直接生成四列塔
    /// 「角列 + 托针柱 N + 落块柱 B + 引爆柱 E」。
    ///
    /// <para>
    /// 角列按形态取「正规化」「底块提升」或「底针埋晶」三个变体；
    /// N/B 由目标列的普通格位置生成；E 是满高纯晶体柱。塔装配好后
    /// 只做一次真实 <see cref="ShapePhysics.Pin"/>，再用构造 C 换支撑柱。
    /// 每一份候选都要求：塔稳定、每列过五项条件、Pin 后目标列逐格
    /// 等于目标、交换链能 rank 0 装配、支柱交接后仍稳定。
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
        string? extraTowerColumn = null,
        bool forceBroad = false)
    {
        if (parts != 4)
        {
            return null;
        }

        var towerColumns = new List<string>();
        bool broadPins = extraTowerColumn is not null || forceBroad;

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
                 * 省掉一整轮必然失败的枚举。
                 */
                towerColumns.Add(gapPinHeld);
                broadPins = true;
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

        string[] supportTexts = EnumerateTowerSupportColumns(
                target,
                column,
                layers,
                broadPins)
            .ToArray();

        string[] bridgeTexts = EnumerateTowerBridgeColumns(
                target,
                column,
                layers,
                pinAsSolidFirst:
                target[0, column].Type == CellType.Pin,
                broadPins)
            .ToArray();

        if (supportTexts.Length == 0 ||
            bridgeTexts.Length == 0)
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

        int left = Wrap(column + 1, parts);
        int right = Wrap(column - 1, parts);
        int far = Wrap(column + 2, parts);

        // 引爆柱 E 与落块柱 B 必须同层相邻（B 的晶体要靠 E 整簇引爆）。
        // 第一组是 §7.2 用户塔的排法：托针柱在左、落块柱在右、引爆柱在最右。
        var arrangements = new (int Support, int Block, int Fuse)[]
        {
            (right, left, far),
            (left, right, far),
            (right, far, left),
            (left, far, right)
        };

        var seenTowers = new HashSet<string>(StringComparer.Ordinal);
        long broadStart = broadPins ? Environment.TickCount64 : 0;

        foreach (string towerColumn in towerColumns)
        {
            if (!seenTowers.Add(towerColumn))
            {
                continue;
            }

            foreach ((int supportPosition, int blockPosition, int fusePosition)
                     in arrangements)
            {
                foreach (string supportText in supportTexts)
                {
                    foreach (string blockText in bridgeTexts)
                    {
                        /*
                         * 宽枚举只是「补漏」：规范候选已经全部试过才会走到这里。
                         * 给它一个很短的预算（20 ms），超时就退回兜底链，
                         * 避免个别形态把单列耗时拖到上百毫秒。
                         */
                        if (broadPins &&
                            Environment.TickCount64 - broadStart > 20)
                        {
                            return null;
                        }

                        AngleWorkOrder? order = TryTowerStyleCandidate(
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
                }
            }
        }

        return null;
    }

    /// <summary>把一份塔式候选装出来并用真实物理回放整条链。</summary>
    private static AngleWorkOrder? TryTowerStyleCandidate(
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
         * 辅助列一律紧列（docs §5 规则六 / §6 第 7 条）：
         * 候选里可能有带洞的落块柱（如 <c>P.T..c</c> 的 <c>cT.T..</c>），
         * 把洞换成同层晶体后再回放——洞变晶体不会新造材料，
         * 半破 / 引爆时与临时晶体一起碎掉。
         */
        // TightenAuxiliaryColumn(tower, supportPosition, crystalColor);
        TightenAuxiliaryColumn(tower, blockPosition, crystalColor);
        // TightenAuxiliaryColumn(tower, fusePosition, crystalColor);

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
    /// 枚举构造 F 可用的 <c>(s, d)</c> 下落窗口。<b>全部由扫描得出</b>（§10）：
    ///
    /// <list type="bullet">
    /// <item><c>t</c>：某个晶体段的底端（<c>target[t]</c> 是晶体、<c>target[t−1]</c> 不是）；</item>
    /// <item><c>s = t − 1</c>：晶体段正下方那一层，<b>必须是空位</b>（构造 F 的针槽层）；</item>
    /// <item><c>d</c>：<c>s</c> 以下最高的非空层，<b>必须是普通格</b>（构造 F 只送回普通格）。</item>
    /// </list>
    ///
    /// <para>
    /// 从最高晶体段往下试，优先「晶体塔正下方」这个主形态；下面那些晶体段
    /// （缺口开在它们的正下方，而不在最高晶体正下方）也照样是合法的下落窗口，
    /// 逐个尝试即可，不需要任何「第几层」的常量。
    /// </para>
    /// </summary>
    private static IEnumerable<(int S, int D)> EnumerateDropWindows(
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

            yield return (s, d);
        }
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
    /// <c>s</c> 与 <c>d</c> 一律来自 <see cref="EnumerateDropWindows"/> 的扫描结果，
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

        AngleWorkOrder? firstFailure = null;

        foreach ((int s, int d) in EnumerateDropWindows(target, column))
        {
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

            if (candidate is null)
            {
                continue;
            }

            if (candidate.IsComplete)
            {
                return candidate;
            }

            // 保留首个「走得更远」的失败单（含已验证前缀），便于 UI 展示。
            if (firstFailure is null ||
                candidate.Steps.Count > firstFailure.Steps.Count)
            {
                firstFailure = candidate;
            }
        }

        return firstFailure ?? Incomplete(
            column,
            kind,
            noWindow,
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

        return new AngleWorkOrder(
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

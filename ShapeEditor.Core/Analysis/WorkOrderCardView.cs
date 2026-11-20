using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 卡片种类（docs/work-order-card-hierarchy-plan.md §3）。
///
/// <para>
/// 三层卡片：角卡（一列怎么做出来）→ 半边卡（半边怎么拼出来）→ 交换卡（两个半边怎么拼成目标）。
/// 「半半边」不建卡，它只是半边卡内部的一个中间态（§3.5 R3）。
/// </para>
/// </summary>
public enum CardKind
{
    /// <summary>角卡：目标角 + 左右支撑柱（产物固定 Q1）。</summary>
    Corner,

    /// <summary>半边卡：k = n/2 列半边。</summary>
    Half,

    /// <summary>交换卡：两个半边 → 目标图形（可能含顶破 / 堆叠收尾）。</summary>
    Exchange
}

/// <summary>
/// 半边卡的三种子型（plan §3 Q3 + 2026-11-20 用户裁定）。
///
/// <list type="bullet">
/// <item><see cref="FromCorners"/>（A 类）：角工序做角 → 角交换成半半边 →
/// 半半边交换成半边，<b>不需要碎晶</b>；<see cref="WorkOrderCardView.Inputs"/> 是角卡产物。</item>
/// <item><see cref="FromTightColumns"/>（B 类·碎晶）：输入 = 该半边各列的
/// <b>紧列</b>（角卡产物 = 紧列 + 双柱），卡内 = 1..N 个真实「碎晶」步
/// （N = 半边工序里的真实 <c>Cut</c> 步数），输出 = 目标半边。</item>
/// <item><see cref="WholeHalf"/>（整半边）：候选 1 / 2，不需要切割、没有输入，
/// 前态是卡内私有的（§3.5 R2）。</item>
/// </list>
/// </summary>
public enum HalfMode
{
    /// <summary>A 类：从角卡产物交换而来的半边（不需要碎晶）。</summary>
    FromCorners,

    /// <summary>B 类·碎晶：输入各列紧列（角卡产物），卡内按真实 Cut 步数碎晶。</summary>
    FromTightColumns,

    /// <summary>整半边一起做（候选 1 / 2：Q 自身 / 联合逆提升，不切割）。</summary>
    WholeHalf
}

/// <summary>
/// 卡内的一步（plan §3）。
///
/// <para>
/// <see cref="Operation"/> 的取值：可见步骤只有 <c>碎晶 / 交换 / 顶破 / 堆叠</c>
/// （这四种必须用真实 <c>ShapePhysics</c> 回放逐格验证）；
/// <c>旋转 / 废料 / 纯支撑交接 / 前态 / 收尾 / 下落 / 弱落 / 结晶</c> 一律
/// <see cref="Hidden"/> = true，数据保留但不进可见步骤。
/// </para>
/// </summary>
public sealed class CardStep
{
    public CardStep(
        string operation,
        ShapeDocument output,
        bool hidden = false,
        string? detail = null)
    {
        Operation = operation;
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Hidden = hidden;
        Detail = detail;
    }

    /// <summary>步骤名：碎晶 / 交换 / 顶破 / 堆叠（可见）；旋转 / 废料等（隐藏）。</summary>
    public string Operation;

    /// <summary>这一步做完之后的图形（逐格快照）。</summary>
    public ShapeDocument Output;

    /// <summary>是否隐藏（旋转、废料、纯支撑交接等不进可见步骤）。</summary>
    public bool Hidden;

    /// <summary>补充说明（隐藏步骤写「为什么隐藏」，可见步骤写回放细节）。</summary>
    public string? Detail;

    /// <summary>
    /// 隐藏「旋转」步的顺时针旋转量（其它步骤恒为 0）。
    /// 回放校验靠它把这一步真实算出来。
    /// </summary>
    public int Rotation;

    /// <summary>
    /// 这一步的输入取自哪张卡输入：−1 = 上一步的产物（默认）；
    /// ≥ 0 = <see cref="WorkOrderCardView.Inputs"/> 里对应的那一张。
    /// 只有 A 类半边卡的「角产物 → 半边」需要它。
    /// </summary>
    public int InputIndex = -1;

    /// <summary>
    /// 「堆叠」步压上去的那一块（hybrid）；只有可见的堆叠步才带，
    /// 回放校验用它做真实 <see cref="ShapePhysics.Stack"/>。
    /// </summary>
    public ShapeDocument? Block;

    /// <summary>可见步骤（UI 只画这些）。</summary>
    public bool Visible => !Hidden;
}

/// <summary>
/// 一张工序卡（只读视图，从现有 <see cref="Recipe"/> / <see cref="AngleWorkOrder"/> /
/// <see cref="HalfWorkOrder"/> 投影，不改旧结构）。
/// </summary>
public sealed class WorkOrderCardView
{
    public WorkOrderCardView(
        CardKind kind,
        string title,
        ShapeDocument output)
    {
        Kind = kind;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>卡片种类。</summary>
    public CardKind Kind;

    /// <summary>卡片标题（「角1」「半边1」「交换」）。</summary>
    public string Title;

    /// <summary>半边卡的模式（A 类 / B 类）；其它卡为 null。</summary>
    public HalfMode? Mode;

    /// <summary>
    /// 上一层卡片的<b>产物</b>（§3.5 R1）；角卡恒为空。
    /// 校验没通过时这里会被清空（不画未验证的输入链）。
    /// </summary>
    public IReadOnlyList<ShapeDocument> Inputs = Array.Empty<ShapeDocument>();

    /// <summary>输入卡标题，与 <see cref="Inputs"/> 一一对应（只用于展示）。</summary>
    public IReadOnlyList<string> InputTitles = Array.Empty<string>();

    /// <summary>
    /// 「并半」步的输入列映射：<c>Inputs[i]</c> 的 Q1（紧列）要放到本卡的
    /// 第 <c>InputColumns[i]</c> 个象限。只有 <see cref="HalfMode.FromTightColumns"/>
    /// 的半边卡用它做真实回放（逐格核对输入紧列真的进了前态）。
    /// </summary>
    public IReadOnlyList<int> InputColumns = Array.Empty<int>();

    /// <summary>
    /// 角卡产物是否只是「该列在半边前态里的紧列 + 左右双柱」——
    /// 即这个角属于一个需要碎晶的半边，碎晶归半边卡，本卡没有碎晶步。
    /// </summary>
    public bool TightColumnProduct;

    /// <summary>卡内步骤（含隐藏步骤，按执行顺序）。</summary>
    public IReadOnlyList<CardStep> Steps = Array.Empty<CardStep>();

    /// <summary>卡片产物（角卡固定 Q1；半边卡是 k 列半边；交换卡是目标图形）。</summary>
    public ShapeDocument Output;

    /// <summary>说明：失败原因 / 子型依据 / A 类不可行的理由。</summary>
    public string? Note;

    /// <summary>真实回放是否通过（不通过时 <see cref="Inputs"/> 必为空）。</summary>
    public bool Verified;

    /// <summary>角卡 / 半边卡的原始列号（角卡的 <c>column</c>、半边卡的起始列），交换卡为 −1。</summary>
    public int Column = -1;

    /// <summary>可见步骤（Hidden 的跳过）。</summary>
    public IReadOnlyList<CardStep> VisibleSteps =>
        Steps.Where(step => !step.Hidden).ToArray();

    public override string ToString()
    {
        return $"{Title}（{Kind}）Verified={Verified}";
    }
}

/// <summary>
/// 一整棵卡片树：角卡 × n、半边卡 × 2、交换卡 × 1。
/// </summary>
public sealed class WorkOrderCardSet
{
    public WorkOrderCardSet(
        ShapeDocument target)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>整图目标。</summary>
    public ShapeDocument Target;

    /// <summary>角卡（只给非空角画卡，按列号升序）。</summary>
    public IReadOnlyList<WorkOrderCardView> CornerCards = Array.Empty<WorkOrderCardView>();

    /// <summary>半边卡（恒为 2 张：左半边 / 右半边）。</summary>
    public IReadOnlyList<WorkOrderCardView> HalfCards = Array.Empty<WorkOrderCardView>();

    /// <summary>交换卡（整图可解时 1 张，否则 null）。</summary>
    public WorkOrderCardView? ExchangeCard;

    /// <summary>整棵树层面的说明（例如顶针 / 堆叠收尾、整体不可解）。</summary>
    public string? Note;

    /// <summary>全部卡片，按「角 → 半边 → 交换」展开。</summary>
    public IReadOnlyList<WorkOrderCardView> Cards
    {
        get
        {
            var cards = new List<WorkOrderCardView>();

            cards.AddRange(CornerCards);
            cards.AddRange(HalfCards);

            if (ExchangeCard is not null)
            {
                cards.Add(ExchangeCard);
            }

            return cards;
        }
    }

    /// <summary>通过真实回放的卡片数。</summary>
    public int VerifiedCount => Cards.Count(card => card.Verified);
}

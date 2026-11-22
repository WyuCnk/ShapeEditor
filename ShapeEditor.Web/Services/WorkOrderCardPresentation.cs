using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Web.Models;

namespace ShapeEditor.Web.Services;

/// <summary>
/// 三层工序卡片的层（从外到内：交换 → 半边 → 角）。
/// </summary>
public enum WorkOrderCardLayerKind
{
    /// <summary>交换工序：最外层，输入是两张半边卡。</summary>
    Exchange,

    /// <summary>半边工序：两张半边卡。</summary>
    Half,

    /// <summary>角工序：每个非空角一张。</summary>
    Corner
}

/// <summary>
/// 工序卡片里的一张输入（上一层卡的产物 + 它的标题）。
/// </summary>
public sealed class WorkOrderCardInput
{
    /// <summary>输入卡标题（例如「角1」「半边1」）。</summary>
    public string Title { get; init; } = "";

    /// <summary>输入形状（1:1 竖式图，页面按 30px 单元格绘制）。</summary>
    public required ShapeDocument Shape { get; init; }
}

/// <summary>
/// 工序卡片里的一条可见步骤（<see cref="CardStep.Hidden"/> 为 true 的步骤不会进来）。
///
/// <para>
/// v4 半边卡（A 类）的可见步骤固定是「交换 → 拆柱 → 跌落」；
/// 角卡仍有「碎晶 / 顶破」等步骤；交换卡只有「交换」（无废料步）。
/// </para>
/// </summary>
public sealed class WorkOrderCardStep
{
    /// <summary>步骤名：碎晶 / 交换 / 拆柱 / 跌落 / 顶破 / 堆叠。</summary>
    public string Operation { get; init; } = "";

    /// <summary>页面节点样式类（复用 angle-node 的配色）。</summary>
    public string CssClass { get; init; } = "angle-kind-target";

    /// <summary>这一步做完之后的形状。</summary>
    public required ShapeDocument Shape { get; init; }
}

/// <summary>
/// 页面上的一张工序卡片。
/// </summary>
public sealed class WorkOrderCardItem
{
    /// <summary>卡片种类。</summary>
    public CardKind Kind { get; init; }

    /// <summary>种类文案：角卡 / 半边卡 / 交换卡。</summary>
    public string KindText { get; init; } = "";

    /// <summary>卡片标题（「角1」「半边1」「交换」）。</summary>
    public string Title { get; init; } = "";

    /// <summary>半边卡的模式文案（A 类 / B 类·碎晶（旧版）/ 整半边）；其它卡为空。</summary>
    public string ModeText { get; init; } = "";

    /// <summary>真实回放是否通过。</summary>
    public bool Verified { get; init; }

    /// <summary>说明：失败原因 / 子型依据；未验证时页面会显示。</summary>
    public string? Note { get; init; }

    /// <summary>
    /// 输入链。未验证时恒为空（Core 也会清空；这里再兜一层，
    /// 保证页面和导出都不会画出未验证的输入链）。
    /// </summary>
    public IReadOnlyList<WorkOrderCardInput> Inputs { get; init; } =
        Array.Empty<WorkOrderCardInput>();

    /// <summary>可见步骤，按执行顺序。</summary>
    public IReadOnlyList<WorkOrderCardStep> Steps { get; init; } =
        Array.Empty<WorkOrderCardStep>();

    /// <summary>
    /// 前态标题（Core 的 <see cref="WorkOrderCardView.PreStateTitle"/>）：
    /// v4 半边卡是「拆柱后（跌落前）」，角卡 / 整半边卡是「前态」。
    /// 没有前态时为空。
    /// </summary>
    public string PreStateTitle { get; init; } = "";

    /// <summary>
    /// 前态图形（Core 的 <see cref="WorkOrderCardView.PreState"/>）。
    ///
    /// <para>
    /// 页面 / 导出把它画在<b>输入之后、可见步骤之前</b>：半边卡的前态是
    /// 「拆柱后、跌落前」的中间态（4 列），角卡的前态是工序起点。
    /// 前态不是输入链，因此未验证的卡仍然按 Core 给的值展示；
    /// Core 没有给前态时为 null，页面与导出都不画这一块。
    /// </para>
    /// </summary>
    public ShapeDocument? PreState { get; init; }

    /// <summary>
    /// 旧版 B 类·碎晶半边卡（<see cref="HalfMode.FromTightColumns"/>）。
    ///
    /// <para>
    /// v4 已删除该路径，枚举值只用于兼容历史数据：卡片照常可渲染，
    /// 但页面 / 导出加「旧版」降级样式，不报错、不隐藏步骤。
    /// </para>
    /// </summary>
    public bool IsLegacyHalf { get; init; }

    /// <summary>卡片产物（角卡固定 Q1；半边卡是 k 列半边；交换卡是目标图形）。</summary>
    public required ShapeDocument Output { get; init; }

    /// <summary>输出说明，例如「Q1 目标角 + 双柱」「目标半边」「目标图形」。</summary>
    public string OutputCaption { get; init; } = "";

    /// <summary>角卡的小字标注：Q1 目标角 / Q2 右柱 / Q4 左柱；其它卡为空。</summary>
    public string ColumnCaption { get; init; } = "";
}

/// <summary>
/// 一层工序卡片。
/// </summary>
public sealed class WorkOrderCardLayer
{
    public WorkOrderCardLayerKind Kind { get; init; }

    /// <summary>层标题：交换工序 / 半边工序 / 角工序。</summary>
    public string Title { get; init; } = "";

    public IReadOnlyList<WorkOrderCardItem> Cards { get; init; } =
        Array.Empty<WorkOrderCardItem>();
}

/// <summary>
/// 页面与导出图共用的三层工序卡片视图。
///
/// <para>
/// 只做「只读投影」：数据全部来自 <see cref="WorkOrderCardSet"/>，
/// 页面和导出都消费同一个对象，避免两边各算一遍、也避免显示规则漂移。
/// </para>
/// <para>
/// 布局采用<b>平铺三层</b>（交换 → 半边 → 角），而不是把角卡嵌进半边卡：
/// 每张卡都要放 30px 单元格的 1:1 竖式图，平铺能让每张卡独占整行、不被父卡缩窄；
/// 同时导出画布可以直接按「层标题 + 卡片列表」排版，不需要额外的嵌套缩进。
/// </para>
/// </summary>
public sealed class WorkOrderCardPresentation
{
    /// <summary>从外到内的三层。</summary>
    public IReadOnlyList<WorkOrderCardLayer> Layers { get; init; } =
        Array.Empty<WorkOrderCardLayer>();

    /// <summary>整棵卡片树的说明（例如顶针 / 堆叠收尾、不可解原因）。</summary>
    public string? Note { get; init; }

    /// <summary>卡片总数。</summary>
    public int TotalCount { get; init; }

    /// <summary>通过真实回放的卡片数。</summary>
    public int VerifiedCount { get; init; }

    /// <summary>是否有卡片；为 false 时页面保留旧的「制作路径」渲染。</summary>
    public bool HasCards => TotalCount > 0;

    /// <summary>
    /// 把 Core 的卡片树投影成页面 / 导出共用视图。
    /// 没有卡片（例如 n = 2）时返回 null，调用方据此回退旧渲染。
    /// </summary>
    public static WorkOrderCardPresentation? Create(
        WorkOrderCardSet? set)
    {
        if (set is null || set.Cards.Count == 0)
        {
            return null;
        }

        var layers = new List<WorkOrderCardLayer>(3);

        AddLayer(
            layers,
            WorkOrderCardLayerKind.Exchange,
            "交换工序",
            set.ExchangeCard is null
                ? Array.Empty<WorkOrderCardView>()
                : new[] { set.ExchangeCard });

        AddLayer(
            layers,
            WorkOrderCardLayerKind.Half,
            "半边工序",
            set.HalfCards);

        AddLayer(
            layers,
            WorkOrderCardLayerKind.Corner,
            "角工序",
            set.CornerCards);

        return new WorkOrderCardPresentation
        {
            Layers = layers,
            Note = set.Note,
            TotalCount = set.Cards.Count,
            VerifiedCount = set.VerifiedCount
        };
    }

    /// <summary>
    /// 导出图用的卡片层：<b>直接投影已缓存的卡片项</b>，不重跑 Core、也不重新建卡。
    ///
    /// <para>
    /// 页面与导出消费同一个 <see cref="WorkOrderCardPresentation"/>，字段一一对应：
    /// 标题 / 种类 / 模式 / 验证 / Note / 输入 / 前态 / 可见步骤 / 输出 / 角卡小字。
    /// 未验证卡的输入链在 <see cref="ToItem"/> 里已经清空，因此导出同样不会画出未验证的输入。
    /// </para>
    /// </summary>
    public IReadOnlyList<AnalysisExportWorkOrderLayer> ToExportLayers()
    {
        var layers = new List<AnalysisExportWorkOrderLayer>(Layers.Count);

        foreach (WorkOrderCardLayer layer in Layers)
        {
            var exportLayer = new AnalysisExportWorkOrderLayer
            {
                Title = layer.Title,
                Count = layer.Cards.Count
            };

            foreach (WorkOrderCardItem card in layer.Cards)
            {
                var exportCard = new AnalysisExportWorkOrderCard
                {
                    Title = card.Title,
                    KindText = card.KindText,
                    ModeText = card.ModeText,
                    Verified = card.Verified,
                    Note = card.Note ?? "",
                    IsCorner = card.Kind == CardKind.Corner,
                    IsLegacyHalf = card.IsLegacyHalf,
                    OutputCaption = card.OutputCaption,
                    CornerCaption = card.ColumnCaption,
                    PreStateTitle = card.PreStateTitle,
                    PreState = card.PreState is null
                        ? null
                        : ShapeCanvasState.Create(card.PreState),
                    Output = ShapeCanvasState.Create(card.Output)
                };

                foreach (WorkOrderCardInput input in card.Inputs)
                {
                    exportCard.Inputs.Add(new AnalysisExportWorkOrderCardInput
                    {
                        Title = input.Title,
                        State = ShapeCanvasState.Create(input.Shape)
                    });
                }

                foreach (WorkOrderCardStep step in card.Steps)
                {
                    exportCard.Steps.Add(new AnalysisExportWorkOrderCardStep
                    {
                        Operation = step.Operation,
                        State = ShapeCanvasState.Create(step.Shape)
                    });
                }

                exportLayer.Cards.Add(exportCard);
            }

            layers.Add(exportLayer);
        }

        return layers;
    }

    private static void AddLayer(
        List<WorkOrderCardLayer> layers,
        WorkOrderCardLayerKind kind,
        string title,
        IReadOnlyList<WorkOrderCardView> cards)
    {
        if (cards.Count == 0)
        {
            return;
        }

        layers.Add(new WorkOrderCardLayer
        {
            Kind = kind,
            Title = title,
            Cards = cards.Select(ToItem).ToArray()
        });
    }

    private static WorkOrderCardItem ToItem(
        WorkOrderCardView card)
    {
        /*
         * 未验证的卡不画输入链：Core 已经清空 Inputs，这里再按 Verified 兜一层，
         * 以后即使 Core 行为变化，页面和导出也不会显示出未验证的输入。
         */
        IReadOnlyList<WorkOrderCardInput> inputs = card.Verified
            ? card.Inputs
                .Select((shape, index) => new WorkOrderCardInput
                {
                    Title = index < card.InputTitles.Count
                        ? card.InputTitles[index]
                        : "输入 " + (index + 1),
                    Shape = shape
                })
                .ToArray()
            : Array.Empty<WorkOrderCardInput>();

        IReadOnlyList<WorkOrderCardStep> steps = card.Steps
            .Where(step => !step.Hidden)
            .Select(step => new WorkOrderCardStep
            {
                Operation = step.Operation,
                CssClass = StepClass(step.Operation),
                Shape = step.Output
            })
            .ToArray();

        /*
         * 前态：Core 只在对得上的卡上填。标题留空时统一补「前态」，
         * 页面和导出共用这一份文案，避免两边各写一套默认值。
         */
        string preStateTitle = card.PreStateTitle ?? "";

        if (card.PreState is not null &&
            string.IsNullOrWhiteSpace(preStateTitle))
        {
            preStateTitle = "前态";
        }

        return new WorkOrderCardItem
        {
            Kind = card.Kind,
            KindText = KindText(card.Kind),
            Title = card.Title,
            ModeText = ModeText(card.Mode),
            Verified = card.Verified,
            Note = card.Note,
            Inputs = inputs,
            Steps = steps,
            PreStateTitle = preStateTitle,
            PreState = card.PreState,
            IsLegacyHalf = card.Mode == HalfMode.FromTightColumns,
            Output = card.Output,
            OutputCaption = OutputCaption(card.Kind),
            ColumnCaption = card.Kind == CardKind.Corner
                ? CornerCaption(card.Output.PartCount)
                : ""
        };
    }

    private static string KindText(
        CardKind kind) =>
        kind switch
        {
            CardKind.Corner => "角卡",
            CardKind.Half => "半边卡",
            CardKind.Exchange => "交换卡",
            _ => ""
        };

    private static string ModeText(
        HalfMode? mode) =>
        mode switch
        {
            HalfMode.FromCorners => "A 类 · 角产物交换",
            // v4 已不再产生 FromTightColumns；这里只给历史数据留可读文案。
            HalfMode.FromTightColumns => "B 类 · 碎晶（旧版）",
            HalfMode.WholeHalf => "整半边",
            _ => ""
        };

    private static string OutputCaption(
        CardKind kind) =>
        kind switch
        {
            CardKind.Corner => "Q1 目标角 + 双柱（已归一化）",
            CardKind.Half => "目标半边",
            CardKind.Exchange => "目标图形",
            _ => ""
        };

    /// <summary>
    /// 角卡的小字标注。n = 4 时正好是验收要求的
    /// 「Q1 目标角 / Q2 右柱 / Q4 左柱」；n &gt; 4 时左柱写 Qn。
    /// </summary>
    private static string CornerCaption(
        int parts) =>
        parts == 4
            ? "Q1 目标角 / Q2 右柱 / Q4 左柱"
            : $"Q1 目标角 / Q2 右柱 / Q{parts} 左柱";

    /// <summary>
    /// 可见步骤复用 angle-node 的配色：碎晶红、交换紫红、拆柱紫蓝、跌落蓝绿、
    /// 顶破青绿、堆叠橙；未知步骤用目标绿兜底。
    /// </summary>
    private static string StepClass(
        string operation) =>
        operation switch
        {
            "碎晶" => "angle-kind-cut",
            "交换" => "angle-kind-exchange",
            "拆柱" => "angle-kind-support",
            "跌落" => "angle-kind-weakdrop",
            "顶破" => "angle-kind-pin",
            "堆叠" => "angle-kind-stack",
            _ => "angle-kind-target"
        };
}

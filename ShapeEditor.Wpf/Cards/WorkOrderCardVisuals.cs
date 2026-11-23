using System.Windows.Media;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Presentation.Cards;
using ShapeEditor.Presentation.Styling;
using ShapeEditor.Wpf.Rendering;

namespace ShapeEditor.Wpf.Cards;

/// <summary>
/// 工序卡片里的一块固定尺寸竖式图预览。
///
/// <para>
/// 尺寸完全来自 <see cref="CardMetrics"/>：宽 = 象限数 × 30，高 = 层数 × 30，
/// 与 <see cref="ShapeGridDrawing"/> 的 1:1 基准一致，不做任何缩放。
/// 预览图与分析页旧的制作路径节点用同一套画法（短代码字形 c / S / P）。
/// </para>
/// </summary>
public sealed class ShapePreviewViewModel
{
    public ShapePreviewViewModel(
        ShapeDocument shape,
        ImageSource? preview)
    {
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        Preview = preview;
    }

    /// <summary>原始图形，导出图也直接用它重画。</summary>
    public ShapeDocument Shape { get; }

    /// <summary>按 30px 单元格绘制的可复用图像。</summary>
    public ImageSource? Preview { get; }

    public double Width => Shape.PartCount * CardMetrics.CellSize;

    public double Height => Shape.LayerCount * CardMetrics.CellSize;

    public bool HasPreview => Preview is not null;
}

/// <summary>
/// 工序卡片的一张输入：上一层卡片的产物 + 它的标题。
/// </summary>
public sealed class WorkOrderInputViewModel
{
    public WorkOrderInputViewModel(
        string title,
        ShapePreviewViewModel preview)
    {
        Title = title ?? "";
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
    }

    public string Title { get; }

    public ShapePreviewViewModel Preview { get; }
}

/// <summary>
/// 工序卡片的一条可见步骤：操作名 + 该步做完之后的图形。
///
/// <para>
/// 配色沿用 Web 的 <c>angle-kind-*</c>：碎晶红、交换紫红、拆柱紫蓝、
/// 跌落蓝绿、顶破青绿、堆叠橙；未知步骤用目标绿兜底。
/// </para>
/// </summary>
public sealed class WorkOrderStepViewModel
{
    public WorkOrderStepViewModel(
        string operation,
        string cssClass,
        ShapePreviewViewModel preview,
        bool showArrow)
    {
        Operation = operation ?? "";
        CssClass = cssClass ?? "";
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        ShowArrow = showArrow;

        (AccentBrush, FillBrush) = WorkOrderCardPalette.StepBrushes(CssClass);
    }

    /// <summary>步骤名：碎晶 / 交换 / 拆柱 / 跌落 / 顶破 / 堆叠。</summary>
    public string Operation { get; }

    /// <summary>Web 端沿用的样式类，导出图据此选颜色。</summary>
    public string CssClass { get; }

    public ShapePreviewViewModel Preview { get; }

    /// <summary>
    /// 是否在步骤节点之后画「→」：最后一步不画。
    /// 箭头画在节点之后，换行到下一行时不会出现「行首箭头」。
    /// </summary>
    public bool ShowArrow { get; }

    public Brush AccentBrush { get; }

    public Brush FillBrush { get; }

    /// <summary>步骤节点宽度：象限数 × 30 + 16（与旧制作路径节点一致）。</summary>
    public double Width => CardMetrics.NodeWidth(Preview.Shape.PartCount);

    /// <summary>步骤节点高度：标题区 50 + 层数 × 30。</summary>
    public double Height => CardMetrics.NodeHeight(Preview.Shape.LayerCount);
}

/// <summary>
/// 页面与导出图共用的一张工序卡片视图。
///
/// 只做投影：字段全部来自 <see cref="WorkOrderCardItem"/>，预览图在这里一次性构建；
/// 窗口与导出消费同一个对象，避免两边各画一套、也避免导出时重跑 Core。
/// </summary>
public sealed class WorkOrderCardViewModel
{
    public WorkOrderCardViewModel(
        WorkOrderCardItem card,
        IReadOnlyList<WorkOrderInputViewModel> inputs,
        IReadOnlyList<WorkOrderStepViewModel> steps,
        ShapePreviewViewModel? preState,
        ShapePreviewViewModel output)
    {
        Item = card ?? throw new ArgumentNullException(nameof(card));
        Inputs = inputs ?? Array.Empty<WorkOrderInputViewModel>();
        Steps = steps ?? Array.Empty<WorkOrderStepViewModel>();
        PreState = preState;
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>Presentation 的原始卡片项，便于导出逐字段核对。</summary>
    public WorkOrderCardItem Item { get; }

    public CardKind Kind => Item.Kind;

    /// <summary>卡片标题：「角1」「半边1」「交换」。</summary>
    public string Title => Item.Title;

    /// <summary>种类文案：角卡 / 半边卡 / 交换卡。</summary>
    public string KindText => Item.KindText;

    /// <summary>半边卡模式文案；其它卡为空。</summary>
    public string ModeText => Item.ModeText;

    public bool HasModeText => !string.IsNullOrWhiteSpace(ModeText);

    public bool Verified => Item.Verified;

    public string VerifiedText => Verified ? "已验证" : "未验证";

    /// <summary>未验证时才显示红色警示框。</summary>
    public bool ShowWarning => !Verified;

    public string Note => Item.Note ?? "";

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>输入链；未验证的卡在 Presentation 里已被清空。</summary>
    public IReadOnlyList<WorkOrderInputViewModel> Inputs { get; }

    public bool HasInputs => Inputs.Count > 0;

    /// <summary>前态（输入之后、可见步骤之前）；没有前态的卡为 null。</summary>
    public ShapePreviewViewModel? PreState { get; }

    public bool HasPreState => PreState is not null;

    /// <summary>前态标题：Core 的 PreStateTitle，空时由 Presentation 补「前态」。</summary>
    public string PreStateTitle => Item.PreStateTitle;

    /// <summary>前态小节标题：与 Web 一致，非默认标题时加「前态 · 」前缀。</summary>
    public string PreStateSectionTitle =>
        string.IsNullOrWhiteSpace(PreStateTitle) || PreStateTitle == "前态"
            ? "前态"
            : "前态 · " + PreStateTitle;

    /// <summary>可见步骤（Hidden 的步骤已在 Presentation 里剔除）。</summary>
    public IReadOnlyList<WorkOrderStepViewModel> Steps { get; }

    public bool HasSteps => Steps.Count > 0;

    public string StepsTitle => $"可见步骤（{Steps.Count} 步）";

    /// <summary>已验证但没有可见步骤时的小字说明（例如整半边卡）。</summary>
    public bool ShowEmptySteps => Verified && !HasSteps;

    public ShapePreviewViewModel Output { get; }

    public string OutputCaption => Item.OutputCaption;

    public bool HasOutputCaption => !string.IsNullOrWhiteSpace(OutputCaption);

    /// <summary>角卡小字：Q1 目标角 / Q2 右柱 / Q4 左柱；其它卡为空。</summary>
    public string CornerCaption => Item.ColumnCaption;

    public bool HasCornerCaption => !string.IsNullOrWhiteSpace(CornerCaption);

    public bool IsCorner => Kind == CardKind.Corner;

    /// <summary>旧版 B 类·碎晶半边卡（只兼容历史数据）。</summary>
    public bool IsLegacyHalf => Item.IsLegacyHalf;

    public bool HasLegacyNote => IsLegacyHalf;

    public string LegacyNote =>
        "旧版 B 类·碎晶半边卡（仅兼容历史数据）：按原步骤回放展示，新分析不会再生成这种卡。";
}

/// <summary>
/// 一层工序卡片：交换工序 / 半边工序 / 角工序。
/// </summary>
public sealed class WorkOrderCardLayerViewModel
{
    public WorkOrderCardLayerViewModel(
        WorkOrderCardLayer layer,
        IReadOnlyList<WorkOrderCardViewModel> cards)
    {
        Kind = layer.Kind;
        Title = layer.Title;
        Cards = cards ?? Array.Empty<WorkOrderCardViewModel>();
        AccentBrush = WorkOrderCardPalette.LayerBrush(Kind);
    }

    public WorkOrderCardLayerKind Kind { get; }

    public string Title { get; }

    /// <summary>层标题右侧的张数：「N 张卡」。</summary>
    public string CountText => $"{Cards.Count} 张卡";

    public IReadOnlyList<WorkOrderCardViewModel> Cards { get; }

    /// <summary>层左侧的强调色条。</summary>
    public Brush AccentBrush { get; }
}

/// <summary>
/// 三层工序卡片的窗口 / 导出共用视图（交换 → 半边 → 角）。
///
/// <para>
/// 构建时一次性把 Presentation 的 <see cref="WorkOrderCardItem"/> 投影成
/// 可绑定的视图模型，并缓存每块图形的 <see cref="DrawingImage"/>：
/// 窗口直接绑定这里的层级集合，导出图也遍历同一份集合，
/// 不会在导出时重新调用 <see cref="CardBuilder"/> 或重新绘制图形。
/// </para>
/// </summary>
public sealed class WorkOrderCardVisualSet
{
    private WorkOrderCardVisualSet(
        IReadOnlyList<WorkOrderCardLayerViewModel> layers,
        string? note,
        int totalCount,
        int verifiedCount)
    {
        Layers = layers;
        Note = note;
        TotalCount = totalCount;
        VerifiedCount = verifiedCount;
    }

    public IReadOnlyList<WorkOrderCardLayerViewModel> Layers { get; }

    /// <summary>整棵卡片树的说明（顶针 / 堆叠收尾、不可解原因）。</summary>
    public string? Note { get; }

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public int TotalCount { get; }

    public int VerifiedCount { get; }

    public bool HasCards => TotalCount > 0;

    /// <summary>与 Web 的 CardsSummary 同一份文案：总张数、分层张数与验证进度。</summary>
    public string Summary
    {
        get
        {
            if (!HasCards)
            {
                return "没有工序卡片。";
            }

            string layers = string.Join(
                "、",
                Layers.Select(layer => $"{layer.Title} {layer.Cards.Count} 张"));

            return $"共 {TotalCount} 张卡（{layers}）；" +
                   $"已验证 {VerifiedCount}/{TotalCount}。";
        }
    }

    /// <summary>
    /// 把 Presentation 的卡片树投影成窗口 / 导出共用视图。
    /// 同一块图形（例如半边输出同时是交换输入）只构建一次预览。
    /// </summary>
    public static WorkOrderCardVisualSet Create(
        WorkOrderCardPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var previews = new Dictionary<ShapeDocument, ShapePreviewViewModel>(
            ReferenceEqualityComparer.Instance);

        var layers = new List<WorkOrderCardLayerViewModel>(presentation.Layers.Count);

        foreach (WorkOrderCardLayer layer in presentation.Layers)
        {
            var cards = new List<WorkOrderCardViewModel>(layer.Cards.Count);

            foreach (WorkOrderCardItem item in layer.Cards)
            {
                var inputs = item.Inputs
                    .Select(input => new WorkOrderInputViewModel(
                        input.Title,
                        GetPreview(previews, input.Shape)))
                    .ToArray();

                var steps = item.Steps
                    .Select((step, index) => new WorkOrderStepViewModel(
                        step.Operation,
                        step.CssClass,
                        GetPreview(previews, step.Shape),
                        showArrow: index < item.Steps.Count - 1))
                    .ToArray();

                cards.Add(new WorkOrderCardViewModel(
                    item,
                    inputs,
                    steps,
                    item.PreState is null
                        ? null
                        : GetPreview(previews, item.PreState),
                    GetPreview(previews, item.Output)));
            }

            layers.Add(new WorkOrderCardLayerViewModel(layer, cards));
        }

        return new WorkOrderCardVisualSet(
            layers,
            presentation.Note,
            presentation.TotalCount,
            presentation.VerifiedCount);
    }

    private static ShapePreviewViewModel GetPreview(
        Dictionary<ShapeDocument, ShapePreviewViewModel> previews,
        ShapeDocument shape)
    {
        if (previews.TryGetValue(shape, out ShapePreviewViewModel? preview))
        {
            return preview;
        }

        var image = new DrawingImage(
            ShapeGridDrawing.Build(
                shape,
                CardMetrics.CellSize,
                CardMetrics.CellSize,
                shortCodeGlyphs: true));

        image.Freeze();

        preview = new ShapePreviewViewModel(shape, image);

        previews[shape] = preview;

        return preview;
    }
}

/// <summary>
/// 工序卡片用到的配色。集中在这里，窗口与导出图取同一份画刷。
/// </summary>
internal static class WorkOrderCardPalette
{
    private static readonly Brush ExchangeAccent = CreateBrush(0x51, 0x2B, 0xD4);

    private static readonly Brush HalfAccent = CreateBrush(0x5E, 0x35, 0xB1);

    private static readonly Brush CornerAccent = CreateBrush(0x45, 0x27, 0xA0);

    private static readonly Brush CutAccent = CreateBrush(0xC6, 0x28, 0x28);

    private static readonly Brush CutFill = CreateBrush(0xFF, 0xF1, 0xF1);

    private static readonly Brush ExchangeStepAccent = CreateBrush(0x8E, 0x24, 0xAA);

    private static readonly Brush ExchangeStepFill = CreateBrush(0xF7, 0xEB, 0xFA);

    private static readonly Brush SupportAccent = CreateBrush(0x3F, 0x51, 0xB5);

    private static readonly Brush SupportFill = CreateBrush(0xEE, 0xF1, 0xFC);

    private static readonly Brush DropAccent = CreateBrush(0x00, 0x69, 0x5C);

    private static readonly Brush DropFill = CreateBrush(0xE4, 0xF4, 0xF1);

    private static readonly Brush PinAccent = CreateBrush(0x0E, 0x7C, 0x86);

    private static readonly Brush PinFill = CreateBrush(0xF2, 0xFB, 0xFB);

    private static readonly Brush StackAccent = CreateBrush(0xB2, 0x6A, 0x00);

    private static readonly Brush StackFill = CreateBrush(0xFF, 0xF8, 0xEE);

    private static readonly Brush TargetAccent = CreateBrush(0x2E, 0x7D, 0x32);

    private static readonly Brush TargetFill = CreateBrush(0xF3, 0xFB, 0xF4);

    public static Brush LayerBrush(
        WorkOrderCardLayerKind kind) =>
        kind switch
        {
            WorkOrderCardLayerKind.Exchange => ExchangeAccent,
            WorkOrderCardLayerKind.Half => HalfAccent,
            _ => CornerAccent
        };

    /// <summary>按 Presentation 给的 angle-kind-* 样式类取步骤边框色与底色。</summary>
    public static (Brush Accent, Brush Fill) StepBrushes(
        string cssClass) =>
        cssClass switch
        {
            "angle-kind-cut" => (CutAccent, CutFill),
            "angle-kind-exchange" => (ExchangeStepAccent, ExchangeStepFill),
            "angle-kind-support" => (SupportAccent, SupportFill),
            "angle-kind-weakdrop" => (DropAccent, DropFill),
            "angle-kind-pin" => (PinAccent, PinFill),
            "angle-kind-stack" => (StackAccent, StackFill),
            _ => (TargetAccent, TargetFill)
        };

    private static Brush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));

        brush.Freeze();

        return brush;
    }
}

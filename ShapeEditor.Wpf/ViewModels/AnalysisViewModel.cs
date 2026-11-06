using System.Collections.ObjectModel;
using System.Windows.Media;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Wpf.Rendering;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 分析窗口的目标：一段短代码，以及它对应的原始尺寸与格式。
/// 工作区的素材可能来自超短代码，必须把尺寸一起传过去才能正确还原。
/// </summary>
public sealed record AnalysisTarget(
    string Code,
    int Layers,
    int Parts,
    ShortCodeFormat Format);

/// <summary>
/// 分析窗口的展示状态。
///
/// 只调用 Core 已有的分析器，不在这里重复判定逻辑；
/// 切换下落类型时整体重新分析一次。
/// 展示形态与 Web 分析页一致：板块可折叠、半边一轴一块、
/// 制作路径拆成「交换工序」与「角的工序」两个子区域。
/// </summary>
public sealed class AnalysisViewModel : ObservableObject
{
    /// <summary>
    /// 性质的固定展示顺序，与 Web 分析页保持一致。
    /// </summary>
    private static readonly ShapeProperty[] PropertyOrder =
    {
        ShapeProperty.ContainsCrystal,
        ShapeProperty.ContainsPin,
        ShapeProperty.CrystalAndEmptySameLayer,
        ShapeProperty.CrystalAndPinSameLayer,
        ShapeProperty.GapBelowShape,
        ShapeProperty.GapBelowCrystal,
        ShapeProperty.MixedCrystalColorsSameLayer,
        ShapeProperty.Stable,
        ShapeProperty.CycleSupported,
        ShapeProperty.Exchangeable
    };

    private readonly ShortCodeParser _parser = new();

    private readonly string _code;

    private readonly ShortCodeFormat _format;

    private CollapseMode _mode;

    private ShapeDocument? _document;

    private ShapeAnalysisResult? _analysis;

    private HalfStabilityResult? _half;

    private ProductionPathResult? _pathResult;

    private ProductionPathGraph? _pathGraph;

    private string _pathSummary = "";

    /// <summary>制作路径结论的小字补充（目前只有「未覆盖」用）。</summary>
    private string _pathOutcomeNote = "";

    private ShapeProperty _properties;

    private string _errorMessage = "";

    private ImageSource? _previewImage;

    private string _angleSummary = "";

    private string _stabilityText = "";

    private string _halfSummary = "";

    private string _halfCollapsedSummary = "";

    /// <summary>只显示合法方向；默认勾选，且只影响窗口显示，导出始终列出全部。</summary>
    private bool _onlyLegalDirections = true;

    // 分析板块的显隐开关，只影响窗口显示；与 Web 分析页一样默认展开。
    private bool _isPropertiesExpanded = true;

    private bool _isAnglesExpanded = true;

    private bool _isStabilityExpanded = true;

    private bool _isHalfExpanded = true;

    private bool _isPathExpanded = true;

    // 制作路径下两个子区域：交换工序默认显示；角的工序仍在设计中，默认隐藏。
    private bool _isPathExchangeExpanded = true;

    private bool _isPathAnglesExpanded;

    public AnalysisViewModel(
        string code,
        int layers = 0,
        int parts = 0,
        ShortCodeFormat format = ShortCodeFormat.Full,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        _code = (code ?? "").Trim();

        _format = format;

        _mode = mode;

        LoadDocument(layers, parts);
    }

    /// <summary>顶部显示的短代码，过长时由界面截断。</summary>
    public string Code => _code;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public bool HasDocument => _document is not null;

    public ShapeDocument? Document => _document;

    /// <summary>竖式图预览，按 30×30 单元格绘制后由界面等比缩放。</summary>
    public ImageSource? PreviewImage => _previewImage;

    /// <summary>竖式图在 1:1 基准下的宽度（象限数 × 30），供左栏只缩小不放大。</summary>
    public double PreviewNaturalWidth => _document is null
        ? 0
        : _document.PartCount * 30;

    /// <summary>竖式图在 1:1 基准下的高度（层数 × 30）。</summary>
    public double PreviewNaturalHeight => _document is null
        ? 0
        : _document.LayerCount * 30;

    public string DimensionText => _document is null
        ? ""
        : $"{_document.LayerCount} 层 × {_document.PartCount} 象限";

    public bool IsCycleMode
    {
        get => _mode == CollapseMode.IncludeCycles;
        set
        {
            if (value)
            {
                SetMode(CollapseMode.IncludeCycles);
            }
        }
    }

    public bool IsFloorMode
    {
        get => _mode == CollapseMode.FloorOnly;
        set
        {
            if (value)
            {
                SetMode(CollapseMode.FloorOnly);
            }
        }
    }

    /// <summary>角的统计摘要：折叠与展开时显示同一句话。</summary>
    public string AngleSummary => _angleSummary;

    public string StabilityText => _stabilityText;

    public string HalfSummary => _halfSummary;

    /// <summary>半边板块折叠时显示的摘要，措辞与 Web 分析页一致。</summary>
    public string HalfCollapsedSummary => _halfCollapsedSummary;

    /// <summary>
    /// 制作路径板块（含「交换工序」与「角的工序」两个子区域）是否显示。
    /// 只要解析出图形就显示，与 Web 的 @if (_half is not null) 对齐。
    /// </summary>
    public bool HasPathSection => _half is not null;

    /// <summary>
    /// 是否存在交换工序：只有图形可交换（半边分析存在合法方向）时才显示。
    /// </summary>
    public bool HasProductionPath =>
        _half is not null &&
        _half.IsExchangeable;

    /// <summary>
    /// 交换工序的结论文本，按三态显示：
    /// 可制作给方向；不可制作说明存在不合法角；未覆盖说明超出算法覆盖范围。
    /// 门控之外（没有合法交换方向）不显示结论。
    /// </summary>
    public string PathSummary => _pathSummary;

    /// <summary>未覆盖时的小字说明；其余状态为空。</summary>
    public string PathOutcomeNote => _pathOutcomeNote;

    public bool HasPathOutcomeNote =>
        !string.IsNullOrWhiteSpace(_pathOutcomeNote);

    /// <summary>
    /// 交换工序的分层节点图；不可制作或没有路径时为空。
    /// 窗口与导出图片共用同一份布局结果。
    /// </summary>
    public ProductionPathGraph? PathGraph => _pathGraph;

    public bool HasPathGraph => _pathGraph is not null;

    /// <summary>
    /// 全部交换方向（不截断），导出分析图时按轴分组使用。
    /// 奇数象限时为空。
    /// </summary>
    public IReadOnlyList<ExchangeDirection> ExchangeDirections =>
        _half?.Directions ?? Array.Empty<ExchangeDirection>();

    public bool HasHalfDirections =>
        _half is not null &&
        _half.Directions.Count > 0;

    /// <summary>
    /// 当前下落规则下的制作路径结果。
    ///
    /// 与页面展示、导出分析图共用同一份结果：在 <see cref="Analyze"/>
    /// 里随稳定性、半边一起重算，切换下落规则后不会残留旧路径。
    /// </summary>
    public ProductionPathResult? GetProductionPath()
    {
        return _pathResult;
    }

    public bool HasProperties => _properties != ShapeProperty.None;

    public string PropertyEmptyText => _properties == ShapeProperty.None
        ? "没有识别到任何性质。"
        : "";

    /// <summary>图形性质的中文标签，顺序固定。</summary>
    public ObservableCollection<string> PropertyNames { get; } = new();

    /// <summary>角的判定表格行。</summary>
    public ObservableCollection<AngleRowViewModel> Angles { get; } = new();

    /// <summary>
    /// 半边稳定性按轴分块：每个轴一张卡片，块内方向在界面里自动换行。
    /// 开关「只显示合法方向」只影响这里的可见方向，不影响导出。
    /// </summary>
    public ObservableCollection<HalfAxisGroupViewModel> HalfAxisGroups { get; } = new();

    #region 板块显隐开关

    public bool IsPropertiesExpanded
    {
        get => _isPropertiesExpanded;
        set => SetProperty(ref _isPropertiesExpanded, value);
    }

    public bool IsAnglesExpanded
    {
        get => _isAnglesExpanded;
        set => SetProperty(ref _isAnglesExpanded, value);
    }

    public bool IsStabilityExpanded
    {
        get => _isStabilityExpanded;
        set => SetProperty(ref _isStabilityExpanded, value);
    }

    public bool IsHalfExpanded
    {
        get => _isHalfExpanded;
        set => SetProperty(ref _isHalfExpanded, value);
    }

    public bool IsPathExpanded
    {
        get => _isPathExpanded;
        set => SetProperty(ref _isPathExpanded, value);
    }

    public bool IsPathExchangeExpanded
    {
        get => _isPathExchangeExpanded;
        set => SetProperty(ref _isPathExchangeExpanded, value);
    }

    public bool IsPathAnglesExpanded
    {
        get => _isPathAnglesExpanded;
        set => SetProperty(ref _isPathAnglesExpanded, value);
    }

    #endregion

    /// <summary>只显示合法方向；默认勾选，切回时恢复全部方向。</summary>
    public bool OnlyLegalDirections
    {
        get => _onlyLegalDirections;
        set
        {
            if (SetProperty(ref _onlyLegalDirections, value))
            {
                RebuildHalfGroups();
            }
        }
    }

    /// <summary>
    /// 解析短代码并生成初始分析结果。
    /// 传入的尺寸优先，但不会小于短代码自身的宽度与层数。
    /// </summary>
    private void LoadDocument(
        int layers,
        int parts)
    {
        if (string.IsNullOrWhiteSpace(_code))
        {
            Fail("没有可分析的短代码。");

            return;
        }

        if (!ShortCodeParser.TryInferDimensions(
                _code,
                _format,
                out int inferredLayers,
                out int inferredParts,
                out string? error))
        {
            Fail(error ?? "短代码尺寸无法推断。");

            return;
        }

        int layerCount = Math.Max(
            layers > 0 ? layers : inferredLayers,
            inferredLayers);

        int partCount = Math.Max(
            parts > 0 ? parts : inferredParts,
            inferredParts);

        ParseResult result = _parser.Parse(
            _code,
            layerCount,
            partCount,
            _format);

        if (!result.Success || result.Document is null)
        {
            Fail(result.Error ?? "短代码解析失败。");

            return;
        }

        _document = result.Document;

        _previewImage = new DrawingImage(
            ShapeGridDrawing.Build(_document, 30, 30));

        ErrorMessage = "";

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(PreviewNaturalWidth));
        OnPropertyChanged(nameof(PreviewNaturalHeight));
        OnPropertyChanged(nameof(DimensionText));

        Analyze();
    }

    private void SetMode(
        CollapseMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        _mode = mode;

        OnPropertyChanged(nameof(IsCycleMode));
        OnPropertyChanged(nameof(IsFloorMode));

        Analyze();
    }

    /// <summary>
    /// 按当前下落类型重新运行三个分析器，并刷新全部展示集合。
    /// </summary>
    private void Analyze()
    {
        if (_document is null)
        {
            return;
        }

        _analysis = ShapeAngleAnalyzer.Analyze(_document, _mode);

        _half = HalfStabilityAnalyzer.Analyze(_document, _mode);

        /*
         * 下落规则变了，制作路径必须跟稳定性、半边一起重算；
         * 这里立即搜索一次，窗口展示与导出分析图使用同一份结果。
         */
        UpdateProductionPath();

        _properties = ShapePropertyAnalyzer.Analyze(
            _document,
            _analysis.IsStable,
            _half.IsExchangeable,
            _mode);

        PropertyNames.Clear();

        foreach (ShapeProperty property in PropertyOrder)
        {
            if ((_properties & property) == property)
            {
                PropertyNames.Add(ShapePropertyAnalyzer.NameOf(property));
            }
        }

        Angles.Clear();

        foreach (AngleResult angle in _analysis.Angles)
        {
            Angles.Add(new AngleRowViewModel(angle));
        }

        _angleSummary =
            $"共 {_analysis.Angles.Count} 个角，{_analysis.LegalCount} 个合法" +
            (_analysis.IsFullyLegal ? "（全部合法）" : "");

        _stabilityText = _analysis.IsStable
            ? "稳定：按当前规则执行一次下落后，图形与原来完全一致。"
            : "不稳定：执行一次下落后图形会发生变化。";

        if (_half is null)
        {
            _halfSummary = "需要在偶数象限的图形上分析。";

            _halfCollapsedSummary = _halfSummary;
        }
        else
        {
            _halfSummary = _half.IsExchangeable
                ? $"可交换：共 {_half.DirectionCount} 个交换方向，其中 {_half.LegalDirections.Count} 个合法。"
                : $"不可交换：共 {_half.DirectionCount} 个交换方向，没有合法的。";

            // 折叠时只保留统计摘要，与 Web 分析页的折叠态一致。
            _halfCollapsedSummary =
                $"共 {_half.DirectionCount} 个交换方向，其中 {_half.LegalDirections.Count} 个合法。";
        }

        RebuildHalfGroups();

        OnPropertyChanged(nameof(AngleSummary));
        OnPropertyChanged(nameof(StabilityText));
        OnPropertyChanged(nameof(HalfSummary));
        OnPropertyChanged(nameof(HalfCollapsedSummary));
        OnPropertyChanged(nameof(PathSummary));
        OnPropertyChanged(nameof(PathOutcomeNote));
        OnPropertyChanged(nameof(HasPathOutcomeNote));
        OnPropertyChanged(nameof(PathGraph));
        OnPropertyChanged(nameof(HasPathGraph));
        OnPropertyChanged(nameof(HasPathSection));
        OnPropertyChanged(nameof(HasProductionPath));
        OnPropertyChanged(nameof(HasHalfDirections));
        OnPropertyChanged(nameof(HasProperties));
        OnPropertyChanged(nameof(PropertyEmptyText));
    }

    /// <summary>
    /// 按轴重建半边卡片。开关只影响窗口显示；
    /// 导出图片直接使用 <see cref="ExchangeDirections"/> 的全部方向。
    /// </summary>
    private void RebuildHalfGroups()
    {
        HalfAxisGroups.Clear();

        if (_half is null)
        {
            return;
        }

        foreach (IGrouping<int, ExchangeDirection> group in
                 _half.Directions
                     .GroupBy(direction => direction.Axis)
                     .OrderBy(group => group.Key))
        {
            List<ExchangeDirection> visible = group
                .Where(direction => !_onlyLegalDirections || direction.IsLegal)
                .ToList();

            HalfAxisGroups.Add(new HalfAxisGroupViewModel(group.Key, visible));
        }
    }

    /// <summary>
    /// 按当前下落规则搜索制作路径，并生成界面展示用的结论与分层节点图。
    /// </summary>
    private void UpdateProductionPath()
    {
        _pathGraph = null;

        _pathSummary = "";

        _pathOutcomeNote = "";

        _pathResult = _document is null
            ? null
            : ProductionPathParser.Parse(
                _document,
                _mode);

        /*
         * 门控：只有存在合法交换方向时才显示交换工序结论；
         * 其余情况保持空白，避免在整块隐藏后仍残留「不可制作」。
         */
        if (_pathResult is null ||
            _half is null ||
            !_half.IsExchangeable)
        {
            return;
        }

        if (!_pathResult.IsProducible ||
            _pathResult.Path is null)
        {
            if (_pathResult.Outcome == PipelineOutcome.Uncovered)
            {
                _pathSummary =
                    "暂不支持解析：该图形的制作路径超出当前算法覆盖范围。";

                _pathOutcomeNote = "这并不代表不可制作。";
            }
            else
            {
                _pathSummary = "不可制作：存在不合法角。";
            }

            return;
        }

        ProductionPath path = _pathResult.Path;

        _pathSummary =
            $"可制作：方向 {path.Direction}（{(path.DirectionLegal ? "合法方向" : "不合法方向")}）。";

        /*
         * 分层节点图与窗口、导出共用：按 Node.Row 分层，
         * 节点尺寸与工作区一致，预览固定 30px 单元格不缩放。
         * 行数由 Core 决定（顶针 / 堆叠 / 块会追加新层）。
         */
        _pathGraph = ProductionPathGraph.Create(path);
    }

    /// <summary>
    /// 解析失败时清空所有结果，只保留错误信息。
    /// </summary>
    private void Fail(
        string message)
    {
        _document = null;

        _analysis = null;

        _half = null;

        _pathResult = null;

        _pathGraph = null;

        _pathSummary = "";

        _pathOutcomeNote = "";

        _properties = ShapeProperty.None;

        _previewImage = null;

        PropertyNames.Clear();

        Angles.Clear();

        HalfAxisGroups.Clear();

        _angleSummary = "";

        _stabilityText = "";

        _halfSummary = "";

        _halfCollapsedSummary = "";

        ErrorMessage = message;

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(PreviewNaturalWidth));
        OnPropertyChanged(nameof(PreviewNaturalHeight));
        OnPropertyChanged(nameof(DimensionText));
        OnPropertyChanged(nameof(AngleSummary));
        OnPropertyChanged(nameof(StabilityText));
        OnPropertyChanged(nameof(HalfSummary));
        OnPropertyChanged(nameof(HalfCollapsedSummary));
        OnPropertyChanged(nameof(PathSummary));
        OnPropertyChanged(nameof(PathOutcomeNote));
        OnPropertyChanged(nameof(HasPathOutcomeNote));
        OnPropertyChanged(nameof(PathGraph));
        OnPropertyChanged(nameof(HasPathGraph));
        OnPropertyChanged(nameof(HasPathSection));
        OnPropertyChanged(nameof(HasProductionPath));
        OnPropertyChanged(nameof(HasHalfDirections));
        OnPropertyChanged(nameof(HasProperties));
        OnPropertyChanged(nameof(PropertyEmptyText));
    }
}

/// <summary>
/// 半边稳定性的一个轴块：轴号 + 该轴当前可见的交换方向。
/// </summary>
public sealed class HalfAxisGroupViewModel
{
    public HalfAxisGroupViewModel(
        int axis,
        IReadOnlyList<ExchangeDirection> directions)
    {
        Title = $"轴 {axis + 1}";

        Directions = directions;
    }

    public string Title { get; }

    public IReadOnlyList<ExchangeDirection> Directions { get; }

    public bool HasDirections => Directions.Count > 0;

    public string EmptyText => "没有合法的交换方向。";
}

/// <summary>
/// 角的合法性表格的一行。
/// </summary>
public sealed class AngleRowViewModel
{
    public AngleRowViewModel(
        AngleResult angle)
    {
        AngleText = $"角 {angle.Part + 1}";

        IsLegal = angle.IsLegal;

        LegalityText = angle.IsLegal ? "合法" : "非法";

        ManufacturabilityText = angle.IsLegal ? "可制作" : "不可制作";

        NoteText = angle.IsLegal
            ? "—"
            : string.Join("；", angle.Failures);
    }

    public string AngleText { get; }

    public bool IsLegal { get; }

    public string LegalityText { get; }

    public string ManufacturabilityText { get; }

    public string NoteText { get; }
}

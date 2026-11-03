using System.Collections.ObjectModel;
using System.Windows.Media;
using ShapeEditor.Core.Analysis;
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

    private ShapeProperty _properties;

    private string _errorMessage = "";

    private ImageSource? _previewImage;

    private string _angleSummary = "";

    private string _stabilityText = "";

    private string _halfSummary = "";

    private string _halfNote = "";

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

    public string AngleSummary => _angleSummary;

    public string StabilityText => _stabilityText;

    public string HalfSummary => _halfSummary;

    public string HalfNote => _halfNote;

    public bool HasLegalDirections => LegalDirections.Count > 0;

    public bool HasProperties => _properties != ShapeProperty.None;

    public string PropertyEmptyText => _properties == ShapeProperty.None
        ? "没有识别到任何性质。"
        : "";

    /// <summary>图形性质的中文标签，顺序固定。</summary>
    public ObservableCollection<string> PropertyNames { get; } = new();

    /// <summary>角的判定表格行。</summary>
    public ObservableCollection<AngleRowViewModel> Angles { get; } = new();

    /// <summary>合法交换方向，最多保留前 60 个。</summary>
    public ObservableCollection<string> LegalDirections { get; } = new();

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

        LegalDirections.Clear();

        _halfNote = "";

        if (_half is null)
        {
            _halfSummary = "需要在偶数象限的图形上分析。";
        }
        else
        {
            _halfSummary = _half.IsExchangeable
                ? $"可交换：共 {_half.DirectionCount} 个交换方向，其中 {_half.LegalDirections.Count} 个合法。"
                : $"不可交换：共 {_half.DirectionCount} 个交换方向，没有合法的。";

            foreach (string direction in _half.LegalDirections.Take(60))
            {
                LegalDirections.Add(direction);
            }

            if (_half.LegalDirections.Count > 60)
            {
                _halfNote = "合法方向较多，仅显示前 60 个。";
            }
        }

        OnPropertyChanged(nameof(AngleSummary));
        OnPropertyChanged(nameof(StabilityText));
        OnPropertyChanged(nameof(HalfSummary));
        OnPropertyChanged(nameof(HalfNote));
        OnPropertyChanged(nameof(HasLegalDirections));
        OnPropertyChanged(nameof(HasProperties));
        OnPropertyChanged(nameof(PropertyEmptyText));
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

        _properties = ShapeProperty.None;

        _previewImage = null;

        PropertyNames.Clear();

        Angles.Clear();

        LegalDirections.Clear();

        _angleSummary = "";

        _stabilityText = "";

        _halfSummary = "";

        _halfNote = "";

        ErrorMessage = message;

        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(DimensionText));
        OnPropertyChanged(nameof(AngleSummary));
        OnPropertyChanged(nameof(StabilityText));
        OnPropertyChanged(nameof(HalfSummary));
        OnPropertyChanged(nameof(HalfNote));
        OnPropertyChanged(nameof(HasLegalDirections));
        OnPropertyChanged(nameof(HasProperties));
        OnPropertyChanged(nameof(PropertyEmptyText));
    }
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

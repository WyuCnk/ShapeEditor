using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Maui;

/// <summary>
/// 图形分析页。
///
/// 只调用 Core 已有的分析器，不在界面里重复判定逻辑；
/// 切换下落规则时整体重新分析一次。布局是单栏 ScrollView，
/// 从上到下：竖式图、图形性质、角的合法性、稳定性、半边稳定性、
/// 制作路径、角的判定规则。
///
/// 与 Web 分析页对齐的交互：
/// 每个板块都有独立的显示 / 隐藏开关；角的合法性折叠后仍显示角数摘要；
/// 半边稳定性一轴一卡、只显示合法方向的开关只影响页面；
/// 制作路径分成「交换工序」（分层 DAG）与「角的工序」（占位）两个独立开关。
/// </summary>
public partial class AnalysisPage : ContentPage
{
    /// <summary>分析页主题色，与工作区工具栏保持一致。</summary>
    private static readonly Color Accent = Color.FromArgb("#512BD4");

    private static readonly Color Text = Color.FromArgb("#333333");

    private static readonly Color Muted = Color.FromArgb("#6B6B76");

    private static readonly Color Danger = Color.FromArgb("#D64545");

    private static readonly Color Legal = Color.FromArgb("#2E9E5B");

    private static readonly Color BadRowBackground = Color.FromArgb("#FFF5F5");

    private static readonly Color AxisCardBackground = Color.FromArgb("#FBFAFF");

    private static readonly Color AxisCardStroke = Color.FromArgb("#E6E2F2");

    private static readonly Color ModeIdleBackground = Color.FromArgb("#EFEAF9");

    /// <summary>每个轴最多显示多少条交换方向，避免大图形把界面撑爆。</summary>
    private const int MaxDirectionsPerAxis = 60;

    /// <summary>整页最多显示多少条交换方向。</summary>
    private const int MaxDirectionRows = 240;

    /// <summary>顶部竖式图的高度上限。</summary>
    private const double PreviewMaxHeight = 320;

    /// <summary>节点图容器的兜底高度区间，与 Web 一致。</summary>
    private const double GraphViewportMinHeight = 220;

    private const double GraphViewportMaxHeight = 560;

    /// <summary>
    /// 性质的固定展示顺序，与 Web / WPF 分析页保持一致。
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

    /// <summary>板块标题右侧的开关按钮 → 被它控制的内容区。</summary>
    private readonly Dictionary<Button, VisualElement> _sectionBodies = new();

    private ShapeDocument? _document;

    private HalfStabilityResult? _half;

    private CollapseMode _mode = CollapseMode.IncludeCycles;

    private string _code = "";

    /// <summary>顶部竖式图当前的图形与可用栏宽。</summary>
    private ShapeDocument? _previewDocument;

    private double _previewAvailableWidth;

    /// <summary>只显示合法方向：默认开启，仅影响页面显示，不影响分析结果。</summary>
    private bool _onlyLegalDirections = true;

    /// <summary>重算 / 重绘守卫，避免在刷新界面时再次触发分析。</summary>
    private bool _rendering;

    public AnalysisPage()
    {
        InitializeComponent();

        RegisterSection(PropertyToggleButton, PropertyBody);

        RegisterSection(AngleToggleButton, AngleBody);

        RegisterSection(StabilityToggleButton, StabilityBody);

        RegisterSection(HalfToggleButton, HalfBody);

        RegisterSection(PathToggleButton, PathBody);

        RegisterSection(PathExchangeToggleButton, PathExchangeBody);

        RegisterSection(PathAnglesToggleButton, PathAnglesBody);

        RegisterSection(RulesToggleButton, RulesBody);

        UpdateModeButtons();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 目标图形由工作区 / 批量页写入 AppPending，取走即清空
        AnalysisTarget? target = AppPending.TakeAnalysis();

        if (target is not null)
        {
            LoadTarget(target);

            return;
        }

        if (_document is null)
        {
            ShowEmptyState();
        }
    }

    /// <summary>
    /// 解析待分析图形。尺寸优先用素材自带的，但不会小于短代码自身推断出的尺寸。
    /// </summary>
    private void LoadTarget(
        AnalysisTarget target)
    {
        _code = (target.Code ?? "").Trim();

        CodeLabel.Text = string.IsNullOrWhiteSpace(_code) ? "—" : _code;

        if (string.IsNullOrWhiteSpace(_code))
        {
            Fail("没有可分析的短代码。");

            return;
        }

        if (!ShortCodeParser.TryInferDimensions(
                _code,
                target.Format,
                out int inferredLayers,
                out int inferredParts,
                out string? inferError))
        {
            Fail(inferError ?? "短代码尺寸无法推断。");

            return;
        }

        int layers = Math.Max(
            target.Layers > 0 ? target.Layers : inferredLayers,
            inferredLayers);

        int parts = Math.Max(
            target.Parts > 0 ? target.Parts : inferredParts,
            inferredParts);

        ParseResult result = _parser.Parse(
            _code,
            layers,
            parts,
            target.Format);

        if (!result.Success || result.Document is null)
        {
            Fail(result.Error ?? "短代码解析失败。");

            return;
        }

        _document = result.Document;

        ClearMessage();

        Recompute();
    }

    #region 板块开关

    /// <summary>
    /// 登记一个板块开关。默认文案按内容区当前的可见状态设置，
    /// 后续由 <see cref="OnSectionToggleClicked"/> 只改可见性与文案。
    /// </summary>
    private void RegisterSection(
        Button button,
        VisualElement body)
    {
        _sectionBodies[button] = body;

        button.Text = body.IsVisible ? "隐藏" : "显示";
    }

    private void OnSectionToggleClicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button ||
            !_sectionBodies.TryGetValue(button, out VisualElement? body))
        {
            return;
        }

        body.IsVisible = !body.IsVisible;

        button.Text = body.IsVisible ? "隐藏" : "显示";
    }

    #endregion

    #region 下落规则

    private void OnModeClicked(
        object? sender,
        EventArgs e)
    {
        CollapseMode mode = ReferenceEquals(sender, FloorModeButton)
            ? CollapseMode.FloorOnly
            : CollapseMode.IncludeCycles;

        if (_mode == mode)
        {
            return;
        }

        _mode = mode;

        UpdateModeButtons();

        Recompute();
    }

    private void UpdateModeButtons()
    {
        SetModeButton(CycleModeButton, _mode == CollapseMode.IncludeCycles);

        SetModeButton(FloorModeButton, _mode == CollapseMode.FloorOnly);
    }

    private static void SetModeButton(
        Button button,
        bool active)
    {
        button.BackgroundColor = active ? Accent : ModeIdleBackground;

        button.TextColor = active ? Colors.White : Accent;
    }

    #endregion

    #region 导航

    private async void OnBackClicked(
        object? sender,
        EventArgs e)
    {
        if (await AppNavigation.GoBackAsync())
        {
            return;
        }

        // 没有可弹出的页面时退回编辑器标签页
        if (!await AppNavigation.GoToEditorAsync())
        {
            await DisplayAlertAsync("图形分析", "无法返回，请使用系统返回键。", "好");
        }
    }

    #endregion

    #region 分析

    /// <summary>
    /// 重算并重绘全部板块。
    /// </summary>
    private void Recompute()
    {
        if (_document is null || _rendering)
        {
            return;
        }

        _rendering = true;

        try
        {
            RecomputeCore(_document);

            EmptyLabel.IsVisible = false;

            ErrorLabel.IsVisible = false;

            ContentPanel.IsVisible = true;
        }
        catch (Exception exception)
        {
            Fail("分析失败：" + exception.Message);
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RecomputeCore(
        ShapeDocument document)
    {
        ClearSections();

        ShapeAnalysisResult analysis = ShapeAngleAnalyzer.Analyze(
            document,
            _mode);

        HalfStabilityResult half = HalfStabilityAnalyzer.Analyze(
            document,
            _mode);

        ShapeProperty properties = ShapePropertyAnalyzer.Analyze(
            document,
            analysis.IsStable,
            half.IsExchangeable,
            _mode);

        ProductionPathResult path = ProductionPathParser.Parse(
            document,
            _mode);

        _half = half;

        RenderPreview(document);

        RenderProperties(properties);

        RenderAngles(analysis);

        RenderStability(analysis);

        RenderHalfStability(document, half);

        RenderProductionPath(path, half);
    }

    private void ClearSections()
    {
        PropertyList.Clear();

        AngleList.Clear();

        AxisList.Clear();

        PropertyEmptyLabel.IsVisible = false;

        HalfNoteLabel.IsVisible = false;

        PathGraphView.Drawable = null;

        PathGraphBox.IsVisible = false;

        PathGraphNote.IsVisible = false;

        PathSummaryLabel.Text = "";

        PathOutcomeNoteLabel.Text = "";

        PathOutcomeNoteLabel.IsVisible = false;
    }

    private void RenderPreview(
        ShapeDocument document)
    {
        _previewDocument = document;

        UpdatePreviewLayout();
    }

    private void OnPreviewCardSizeChanged(
        object? sender,
        EventArgs e)
    {
        // SectionCard 左右内边距合计 20；用卡片宽度而不是内部 Stack 的宽度，
        // 避免预览先按估算宽度撑大内部布局后拿不到真实栏宽。
        double width = PreviewCard.Width - 20;

        // 只有栏宽变化才需要重算比例，高度变化（子元素换行等）不重复重绘。
        if (width <= 1 || Math.Abs(width - _previewAvailableWidth) < 0.5)
        {
            return;
        }

        _previewAvailableWidth = width;

        UpdatePreviewLayout();
    }

    /// <summary>
    /// 顶部竖式图：以 30 像素单元格为 1:1 基准，等比缩小到栏宽，
    /// 高度不超过 320；缩放比例上限为 1，小图形保持原尺寸。
    /// </summary>
    private void UpdatePreviewLayout()
    {
        if (_previewDocument is not ShapeDocument document)
        {
            return;
        }

        double availableWidth = _previewAvailableWidth > 1
            ? _previewAvailableWidth
            : 320;

        double naturalWidth = document.PartCount * WorkspaceLayout.CellSize;

        double naturalHeight = document.LayerCount * WorkspaceLayout.CellSize;

        if (naturalWidth <= 0 || naturalHeight <= 0)
        {
            return;
        }

        double scale = Math.Min(
            1,
            Math.Min(
                availableWidth / naturalWidth,
                PreviewMaxHeight / naturalHeight));

        if (scale <= 0)
        {
            return;
        }

        PreviewView.WidthRequest = naturalWidth * scale;

        PreviewView.HeightRequest = naturalHeight * scale;

        PreviewView.Drawable = new AnalysisPreviewDrawable(
            document,
            WorkspaceLayout.CellSize * scale);

        PreviewView.Invalidate();

        DimensionLabel.Text = $"{document.LayerCount} 层 × {document.PartCount} 象限";
    }

    private void RenderProperties(
        ShapeProperty properties)
    {
        bool any = false;

        foreach (ShapeProperty property in PropertyOrder)
        {
            if ((properties & property) != property)
            {
                continue;
            }

            PropertyList.Add(BuildPropertyChip(
                ShapePropertyAnalyzer.NameOf(property)));

            any = true;
        }

        PropertyEmptyLabel.IsVisible = !any;
    }

    private void RenderAngles(
        ShapeAnalysisResult analysis)
    {
        // 折叠与展开显示同一句话；折叠时板块里只保留这句摘要。
        AngleSummaryLabel.Text =
            $"共 {analysis.Angles.Count} 个角，{analysis.LegalCount} 个合法" +
            (analysis.IsFullyLegal ? "（全部合法）" : "");

        foreach (AngleResult angle in analysis.Angles)
        {
            AngleList.Add(BuildAngleRow(angle));
        }
    }

    private void RenderStability(
        ShapeAnalysisResult analysis)
    {
        StabilityLabel.Text = analysis.IsStable
            ? "稳定：按当前规则执行一次下落后，图形与原来完全一致。"
            : "不稳定：执行一次下落后图形会发生变化。";
    }

    /// <summary>
    /// 半边稳定性：每个轴一张卡片，卡片内方向自动换行；
    /// 整块区域跟随页面纵向滚动，不出现横向滚动。
    /// </summary>
    private void RenderHalfStability(
        ShapeDocument document,
        HalfStabilityResult half)
    {
        AxisList.Clear();

        // 奇数象限或象限数不足 4 时，Core 返回空结果
        if (document.PartCount % 2 != 0 || document.PartCount / 2 < 2)
        {
            HalfSummaryLabel.Text = "需要在偶数象限的图形上分析（至少 4 个象限）。";

            return;
        }

        HalfSummaryLabel.Text = half.IsExchangeable
            ? $"可交换：共 {half.DirectionCount} 个交换方向，其中 {half.LegalDirections.Count} 个合法。"
            : $"不可交换：共 {half.DirectionCount} 个交换方向，没有合法的。";

        if (half.Directions.Count == 0)
        {
            return;
        }

        int shown = 0;

        bool truncated = false;

        foreach (IGrouping<int, ExchangeDirection> group in
                 half.Directions.GroupBy(direction => direction.Axis)
                     .OrderBy(group => group.Key))
        {
            if (shown >= MaxDirectionRows)
            {
                truncated = true;

                break;
            }

            AxisList.Add(BuildAxisCard(group, ref shown, ref truncated));

            if (truncated)
            {
                break;
            }
        }

        HalfNoteLabel.IsVisible = truncated;

        HalfNoteLabel.Text = truncated
            ? $"交换方向较多，仅显示前 {shown} 条。"
            : "";
    }

    /// <summary>
    /// 一个轴的交换方向卡片：轴标题 + 可自动换行的方向标签。
    /// 合法 / 不合法只靠文字颜色区分（绿 / 红）。
    /// </summary>
    private View BuildAxisCard(
        IGrouping<int, ExchangeDirection> group,
        ref int shown,
        ref bool truncated)
    {
        var card = new Border
        {
            BackgroundColor = AxisCardBackground,
            Stroke = AxisCardStroke,
            StrokeThickness = 1,
            Padding = new Thickness(10),
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 10
            }
        };

        var layout = new VerticalStackLayout
        {
            Spacing = 6
        };

        layout.Add(new Label
        {
            Text = $"轴 {group.Key + 1}",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Accent
        });

        var flex = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            Direction = FlexDirection.Row,
            AlignItems = FlexAlignItems.Start,
            JustifyContent = FlexJustify.Start
        };

        int shownInAxis = 0;

        bool any = false;

        foreach (ExchangeDirection direction in group)
        {
            if (shown >= MaxDirectionRows || shownInAxis >= MaxDirectionsPerAxis)
            {
                truncated = true;

                break;
            }

            // 开关只影响页面显示；摘要与交换工序的判断始终使用完整结果。
            if (_onlyLegalDirections && !direction.IsLegal)
            {
                continue;
            }

            flex.Add(new Label
            {
                Text = direction.Display,
                FontSize = 12,
                TextColor = direction.IsLegal ? Legal : Danger,
                WidthRequest = 168,
                LineBreakMode = LineBreakMode.CharacterWrap,
                Margin = new Thickness(0, 0, 12, 4)
            });

            any = true;

            shown++;

            shownInAxis++;
        }

        if (any)
        {
            layout.Add(flex);
        }
        else
        {
            layout.Add(new Label
            {
                Text = _onlyLegalDirections
                    ? "没有合法的交换方向。"
                    : "没有交换方向。",
                FontSize = 11,
                TextColor = Muted
            });
        }

        card.Content = layout;

        return card;
    }

    private void OnOnlyLegalDirectionsToggled(
        object? sender,
        ToggledEventArgs e)
    {
        _onlyLegalDirections = e.Value;

        if (_document is null || _half is null)
        {
            return;
        }

        // 只重绘半边板块，不重跑分析。
        RenderHalfStability(_document, _half);
    }

    /// <summary>
    /// 制作路径：交换工序只在可交换时出现；角的工序始终占位。
    /// 两块各自有独立开关，外层「制作路径」总开关由板块开关统一管理。
    /// </summary>
    private void RenderProductionPath(
        ProductionPathResult path,
        HalfStabilityResult half)
    {
        PathExchangeSection.IsVisible = half.IsExchangeable;

        if (!half.IsExchangeable)
        {
            return;
        }

        if (!path.IsProducible || path.Path is null)
        {
            /*
             * 三态措辞：「不可制作」只在存在不合法角时使用；
             * 求解器没有找到路径但也没有否定时是「未覆盖」，
             * 必须明确告诉用户这不代表不可制作。
             */
            if (path.Outcome == PipelineOutcome.Uncovered)
            {
                PathSummaryLabel.Text =
                    "暂不支持解析：该图形的制作路径超出当前算法覆盖范围。";

                PathOutcomeNoteLabel.Text = "这并不代表不可制作。";

                PathOutcomeNoteLabel.IsVisible = true;
            }
            else
            {
                PathSummaryLabel.Text = "不可制作：存在不合法角。";
            }

            return;
        }

        PathSummaryLabel.Text =
            $"可制作：方向 {path.Path.Direction}" +
            $"（{(path.Path.DirectionLegal ? "合法方向" : "不合法方向")}）。";

        ProductionPathGraphLayout? graph = ProductionPathGraphLayout.Create(path.Path);

        if (graph is null)
        {
            return;
        }

        PathGraphView.WidthRequest = graph.Width;

        PathGraphView.HeightRequest = graph.Height;

        PathGraphView.Drawable = new ProductionPathGraphDrawable(graph);

        PathGraphView.Invalidate();

        // 节点少时按内容自适应，节点多时固定高度并纵横滚动。
        PathGraphScroll.HeightRequest = Math.Clamp(
            Math.Ceiling(graph.Height),
            GraphViewportMinHeight,
            GraphViewportMaxHeight);

        PathGraphBox.IsVisible = true;

        PathGraphNote.IsVisible = true;
    }

    #endregion

    #region 表格行

    private static View BuildPropertyChip(
        string name)
    {
        return new Border
        {
            BackgroundColor = ModeIdleBackground,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(9, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Content = new Label
            {
                Text = name,
                FontSize = 12,
                TextColor = Accent
            }
        };
    }

    private static View BuildAngleRow(
        AngleResult angle)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(70)));

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(90)));

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        grid.Add(Cell($"角 {angle.Part + 1}"), 0, 0);

        grid.Add(Cell(
            angle.IsLegal ? "合法" : "非法",
            angle.IsLegal ? Legal : Danger), 1, 0);

        grid.Add(Cell(
            angle.IsLegal ? "可制作" : "不可制作",
            Muted), 2, 0);

        // 备注写 Failures 拼接；合法角没有失败项
        grid.Add(new Label
        {
            Text = angle.Failures.Count == 0
                ? "—"
                : string.Join("；", angle.Failures),
            FontSize = 11,
            TextColor = Muted,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalTextAlignment = TextAlignment.Center
        }, 3, 0);

        return new Border
        {
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(0, 4),
            BackgroundColor = angle.IsLegal
                ? Colors.Transparent
                : BadRowBackground,
            Content = grid
        };
    }

    private static Label Cell(
        string text,
        Color? color = null)
    {
        return new Label
        {
            Text = text,
            FontSize = 12,
            TextColor = color ?? Text,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center
        };
    }

    #endregion

    #region 状态

    private void ClearMessage()
    {
        ErrorLabel.Text = "";

        ErrorLabel.IsVisible = false;

        EmptyLabel.IsVisible = false;
    }

    private void ShowEmptyState()
    {
        _document = null;

        _half = null;

        _previewDocument = null;

        PreviewView.Drawable = null;

        ContentPanel.IsVisible = false;

        ErrorLabel.IsVisible = false;

        EmptyLabel.IsVisible = true;

        CodeLabel.Text = "—";
    }

    /// <summary>
    /// 解析失败时清空结果，只保留错误信息。
    /// </summary>
    private void Fail(
        string message)
    {
        _document = null;

        _half = null;

        _previewDocument = null;

        PreviewView.Drawable = null;

        ContentPanel.IsVisible = false;

        EmptyLabel.IsVisible = false;

        ErrorLabel.Text = message;

        ErrorLabel.IsVisible = true;

        CodeLabel.Text = string.IsNullOrWhiteSpace(_code) ? "—" : _code;
    }

    #endregion
}

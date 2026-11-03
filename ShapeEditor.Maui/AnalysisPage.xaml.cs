using ShapeEditor.Core.Analysis;
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
/// </summary>
public partial class AnalysisPage : ContentPage
{
    /// <summary>分析页主题色，与工作区工具栏保持一致。</summary>
    private static readonly Color Accent = Color.FromArgb("#512BD4");

    private static readonly Color Text = Color.FromArgb("#333333");

    private static readonly Color Muted = Color.FromArgb("#6B6B76");

    private static readonly Color Danger = Color.FromArgb("#A00000");

    private static readonly Color Legal = Color.FromArgb("#2E7D32");

    private static readonly Color BadRowBackground = Color.FromArgb("#FFF5F5");

    private static readonly Color StepBackground = Color.FromArgb("#FAFAFA");

    private static readonly Color StepBorder = Color.FromArgb("#EEEAF7");

    private static readonly Color ModeIdleBackground = Color.FromArgb("#EFEAF9");

    /// <summary>每个轴最多显示多少条交换方向，避免大图形把界面撑爆。</summary>
    private const int MaxDirectionsPerAxis = 60;

    /// <summary>整页最多显示多少条交换方向。</summary>
    private const int MaxDirectionRows = 240;

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

    private readonly ShortCodeExporter _exporter = new();

    private ShapeDocument? _document;

    private CollapseMode _mode = CollapseMode.IncludeCycles;

    private string _code = "";

    /// <summary>重算 / 重绘守卫，避免在刷新界面时再次触发分析。</summary>
    private bool _rendering;

    public AnalysisPage()
    {
        InitializeComponent();

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

        RenderPreview(document);

        RenderProperties(properties);

        RenderAngles(analysis);

        RenderStability(analysis);

        RenderHalfStability(document, half);

        RenderProductionPath(path);
    }

    private void ClearSections()
    {
        PropertyList.Clear();

        AngleList.Clear();

        AxisList.Clear();

        PathList.Clear();

        PropertyEmptyLabel.IsVisible = false;

        HalfNoteLabel.IsVisible = false;
    }

    private void RenderPreview(
        ShapeDocument document)
    {
        double height = document.LayerCount * WorkspaceLayout.CellSize;

        PreviewView.WidthRequest = document.PartCount * WorkspaceLayout.CellSize;

        PreviewView.HeightRequest = Math.Max(WorkspaceLayout.CellSize, height);

        PreviewView.Drawable = new WorkspaceCellDrawable(document);

        PreviewScroll.HeightRequest = Math.Max(WorkspaceLayout.CellSize, height);

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

    private void RenderHalfStability(
        ShapeDocument document,
        HalfStabilityResult half)
    {
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

            AxisList.Add(BuildAxisTable(group, ref shown, ref truncated));

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
    /// 一个轴的交换方向表格：轴标题 + 表头 + 若干行。
    /// </summary>
    private View BuildAxisTable(
        IGrouping<int, ExchangeDirection> group,
        ref int shown,
        ref bool truncated)
    {
        var layout = new VerticalStackLayout
        {
            Spacing = 2
        };

        layout.Add(new Label
        {
            Text = $"轴 {group.Key + 1}",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Muted
        });

        var header = new Grid
        {
            ColumnSpacing = 8
        };

        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        header.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));

        header.Add(new Label
        {
            Text = "交换方向",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Muted
        }, 0, 0);

        header.Add(new Label
        {
            Text = "合法",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Muted
        }, 1, 0);

        layout.Add(header);

        int shownInAxis = 0;

        foreach (ExchangeDirection direction in group)
        {
            if (shown >= MaxDirectionRows || shownInAxis >= MaxDirectionsPerAxis)
            {
                truncated = true;

                break;
            }

            layout.Add(BuildDirectionRow(direction));

            shown++;

            shownInAxis++;
        }

        return layout;
    }

    private void RenderProductionPath(
        ProductionPathResult path)
    {
        if (!path.IsProducible || path.Path is null)
        {
            PathSummaryLabel.Text = "不可制作：" +
                (string.IsNullOrWhiteSpace(path.Error) ? "无法解析。" : path.Error);

            return;
        }

        PathSummaryLabel.Text =
            $"可制作：方向 {path.Path.Direction}" +
            $"（{(path.Path.DirectionLegal ? "合法方向" : "不合法方向")}）。";

        foreach (ProductionStep step in path.Path.Steps)
        {
            PathList.Add(BuildPathStepRow(step));
        }
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

    private static View BuildDirectionRow(
        ExchangeDirection direction)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));

        grid.Add(new Label
        {
            Text = direction.Display,
            FontSize = 11,
            TextColor = Text,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center
        }, 0, 0);

        grid.Add(Cell(
            direction.IsLegal ? "是" : "否",
            direction.IsLegal ? Legal : Danger), 1, 0);

        return new Border
        {
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(0, 2),
            BackgroundColor = direction.IsLegal
                ? Colors.Transparent
                : BadRowBackground,
            Content = grid
        };
    }

    private View BuildPathStepRow(
        ProductionStep step)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        grid.Add(new Label
        {
            Text = step.Title,
            FontSize = 12,
            TextColor = Text,
            VerticalTextAlignment = TextAlignment.Center
        }, 0, 0);

        if (step.Shape is not null)
        {
            grid.Add(new Label
            {
                Text = _exporter.Export(step.Shape, omitTopEmptyLayers: true),
                FontSize = 11,
                TextColor = Muted,
                LineBreakMode = LineBreakMode.TailTruncation,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center,
                MaximumWidthRequest = 220
            }, 1, 0);
        }

        return new Border
        {
            BackgroundColor = StepBackground,
            Stroke = StepBorder,
            StrokeThickness = 1,
            Padding = new Thickness(8, 4),
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

        ContentPanel.IsVisible = false;

        EmptyLabel.IsVisible = false;

        ErrorLabel.Text = message;

        ErrorLabel.IsVisible = true;

        CodeLabel.Text = string.IsNullOrWhiteSpace(_code) ? "—" : _code;
    }

    #endregion
}

using System.Windows;
using System.Windows.Media;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;
using ShapeEditor.Wpf.Rendering;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 画布上的一个节点。
///
/// 卡片只保留标题、删除按钮、竖式图预览和摘要文字，
/// 所有操作都移到工作区工具条。
/// </summary>
public sealed class WorkspaceNodeViewModel : ObservableObject
{
    private bool _isSelected;

    public WorkspaceNodeViewModel(
        WorkspaceNode node,
        ShapeDocument? document,
        int fallbackParts,
        bool isSelected)
    {
        Node = node;

        Document = document;

        Size size = WorkspaceLayout.SizeFor(
            node,
            document,
            fallbackParts);

        NodeWidth = size.Width;

        NodeHeight = size.Height;

        PreviewWidth = Math.Max(
            1,
            NodeWidth - WorkspaceLayout.HorizontalPadding);

        PreviewHeight = Math.Max(
            1,
            NodeHeight - WorkspaceLayout.HeaderHeight);

        Bounds = WorkspaceLayout.BoundsFor(node, size);

        Thumbnail = document is null
            ? null
            : new DrawingImage(
                ShapeGridDrawing.Build(
                    document,
                    WorkspaceLayout.CellSize,
                    WorkspaceLayout.CellSize));

        _isSelected = isSelected;
    }

    public WorkspaceNode Node { get; }

    public ShapeDocument? Document { get; }

    public string Id => Node.Id;

    /// <summary>30×30 单元格的竖式图，保持原始尺寸，不压缩。</summary>
    public ImageSource? Thumbnail { get; }

    /// <summary>节点卡片宽度。</summary>
    public double NodeWidth { get; }

    /// <summary>节点卡片高度。</summary>
    public double NodeHeight { get; }

    /// <summary>预览区宽度（象限数 × 30）。</summary>
    public double PreviewWidth { get; }

    /// <summary>预览区高度（层数 × 30）。</summary>
    public double PreviewHeight { get; }

    public Rect Bounds { get; }

    public double Left => Bounds.Left;

    public double Top => Bounds.Top;

    public Point Center => WorkspaceLayout.CenterOf(Bounds);

    public bool HasPreview => Document is not null;

    public string PlaceholderText => Node.Kind == "ref"
        ? "选择输入"
        : "空节点";

    /// <summary>节点里显示的短代码文本（c/S/P/-）。</summary>
    public string CodeText { get; set; } = "";

    public string CodeSummary { get; set; } = "";

    /// <summary>摘要文字；没有图形时给出占位说明。</summary>
    public string SummaryText => string.IsNullOrWhiteSpace(CodeSummary)
        ? (Node.Kind == "ref" ? "等待输入" : "没有图形")
        : CodeSummary;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Title => Node.Kind == "ref"
        ? "堆叠输入"
        : Node.IsResult
            ? Node.Label
            : "素材节点";

    public string Header => $"#{Node.Seq} · {Title}";

    public bool IsRef => Node.Kind == "ref";

    public bool IsResult => Node.IsResult;

    public bool HasShape => Node.HasShape;
}

/// <summary>
/// 一条连线。
///
/// 线段从源节点边界连到目标节点边界，箭头单独画在节点之上，
/// 连线上不再显示任何文字标签。
/// </summary>
public sealed class WorkspaceEdgeViewModel
{
    public WorkspaceEdgeViewModel(
        WorkspaceEdge edge,
        WorkspaceNodeViewModel from,
        WorkspaceNodeViewModel to)
    {
        Edge = edge;

        Point fromCenter = from.Center;

        Point toCenter = to.Center;

        Point start = WorkspaceLayout.BoundaryToward(
            from.Bounds,
            toCenter);

        Point entry = WorkspaceLayout.BoundaryToward(
            to.Bounds,
            fromCenter);

        /*
         * 箭头尖端贴在目标节点边框外 1px，
         * 线段也停在这里，避免被卡片边框盖住。
         */
        Point tip = WorkspaceLayout.OffsetOutward(
            to.Bounds,
            entry,
            1);

        var line = new LineGeometry(start, tip);

        line.Freeze();

        LineGeometry = line;

        double angle = Math.Atan2(
            tip.Y - start.Y,
            tip.X - start.X);

        const double head = 10;

        const double halfWidth = 5.5;

        double cos = Math.Cos(angle);

        double sin = Math.Sin(angle);

        var arrow = new PathGeometry();

        var figure = new PathFigure
        {
            StartPoint = tip,
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new LineSegment(
            new Point(
                tip.X - head * cos + halfWidth * sin,
                tip.Y - head * sin - halfWidth * cos),
            true));

        figure.Segments.Add(new LineSegment(
            new Point(
                tip.X - head * cos - halfWidth * sin,
                tip.Y - head * sin + halfWidth * cos),
            true));

        arrow.Figures.Add(figure);

        arrow.Freeze();

        ArrowGeometry = arrow;
    }

    public WorkspaceEdge Edge { get; }

    /// <summary>不带箭头的线段。</summary>
    public Geometry LineGeometry { get; }

    /// <summary>按连线方向旋转后的实心三角形。</summary>
    public Geometry ArrowGeometry { get; }
}

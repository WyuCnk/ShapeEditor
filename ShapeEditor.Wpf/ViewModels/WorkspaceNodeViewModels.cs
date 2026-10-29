using System.Windows;
using System.Windows.Media;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;
using ShapeEditor.Wpf.Rendering;
using ShapeEditor.Wpf.Services;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 画布布局常量，与 Web 版保持一致。
/// </summary>
public static class WorkspaceLayout
{
    public const double CellWidth = 250;

    public const double CellHeight = 400;

    public const double NodeWidth = 210;

    public const double NodeHeight = 260;

    public const double Padding = 24;

    public static double NodeLeft(
        WorkspaceNode node) => Padding + node.Col * CellWidth;

    public static double NodeTop(
        WorkspaceNode node) => Padding + node.Row * CellHeight;

    public static double CenterX(
        WorkspaceNode node) => NodeLeft(node) + NodeWidth / 2;

    public static double Bottom(
        WorkspaceNode node) => NodeTop(node) + NodeHeight;
}

/// <summary>
/// 画布上的一个节点。
/// </summary>
public sealed class WorkspaceNodeViewModel : ObservableObject
{
    private bool _isSelected;

    public WorkspaceNodeViewModel(
        WorkspaceNode node,
        ImageSource? thumbnail,
        bool isSelected)
    {
        Node = node;
        Thumbnail = thumbnail;
        _isSelected = isSelected;
    }

    public WorkspaceNode Node { get; }

    public string Id => Node.Id;

    public ImageSource? Thumbnail { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public double Left => WorkspaceLayout.NodeLeft(Node);

    public double Top => WorkspaceLayout.NodeTop(Node);

    public string Title => Node.Kind == "ref"
        ? "堆叠输入"
        : Node.IsResult
            ? Node.Label
            : "素材节点";

    public string Header => $"#{Node.Seq} · {Title}";

    public bool IsRef => Node.Kind == "ref";

    public bool IsResult => Node.IsResult;

    public string RefText { get; set; } = "";

    public bool HasShape => Node.HasShape;

    public Style? Tag => null;
}

/// <summary>
/// 一条连线：直线 + 标签。
/// </summary>
public sealed class WorkspaceEdgeViewModel
{
    public WorkspaceEdgeViewModel(
        WorkspaceEdge edge,
        WorkspaceNode from,
        WorkspaceNode to,
        int outIndex,
        int outCount,
        int inIndex,
        int inCount)
    {
        Edge = edge;

        double x1 = WorkspaceLayout.CenterX(from) + Spread(outIndex, outCount, 30);
        double y1 = WorkspaceLayout.Bottom(from);

        double x2 = WorkspaceLayout.CenterX(to) + Spread(inIndex, inCount, 30);
        double y2 = WorkspaceLayout.NodeTop(to);

        double tipX = x2;
        double tipY = y2 - 4;

        var line = new PathGeometry();

        var lineFigure = new PathFigure
        {
            StartPoint = new Point(x1, y1)
        };

        lineFigure.Segments.Add(new LineSegment(new Point(tipX, tipY), true));

        line.Figures.Add(lineFigure);

        line.Freeze();

        double angle = Math.Atan2(tipY - y1, tipX - x1);
        const double head = 10;

        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        var arrow = new PathGeometry();

        var arrowFigure = new PathFigure
        {
            StartPoint = new Point(tipX, tipY),
            IsClosed = true
        };

        arrowFigure.Segments.Add(new LineSegment(
            new Point(
                tipX - head * cos + head * 0.55 * sin,
                tipY - head * sin - head * 0.55 * cos),
            true));

        arrowFigure.Segments.Add(new LineSegment(
            new Point(
                tipX - head * cos - head * 0.55 * sin,
                tipY - head * sin + head * 0.55 * cos),
            true));

        arrow.Figures.Add(arrowFigure);

        arrow.Freeze();

        var geometry = new GeometryGroup();

        geometry.Children.Add(line);
        geometry.Children.Add(arrow);

        geometry.Freeze();

        Geometry = geometry;

        double t = Math.Clamp(edge.LabelT, 0.1, 0.9);

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));

        double nx = -dy / length;
        double ny = dx / length;

        if (ny > 0)
        {
            nx = -nx;
            ny = -ny;
        }

        if (Math.Abs(dx) < 8)
        {
            nx = 1;
            ny = 0;
        }

        double offset = Math.Abs(dx) < 8 ? 42 : 18;

        LabelX = x1 + dx * t + nx * offset;
        LabelY = y1 + dy * t + ny * offset;
    }

    public WorkspaceEdge Edge { get; }

    public Geometry Geometry { get; }

    public string Label => Edge.Label;

    public double LabelX { get; }

    public double LabelY { get; }

    public double LabelLeft => LabelX - 6;

    public double LabelTop => LabelY - 10;

    private static double Spread(
        int index,
        int count,
        double step)
    {
        return count <= 1
            ? 0
            : Math.Round((index - (count - 1) / 2.0) * step);
    }
}

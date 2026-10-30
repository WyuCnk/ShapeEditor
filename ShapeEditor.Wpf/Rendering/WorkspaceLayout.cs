using System.Windows;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 工作区画布布局。
///
/// 节点尺寸完全由内容决定：
/// 宽度 = 象限数 × 30 + 16，高度 = 50 + 层数 × 30，不再封顶。
/// 旧数据没有 X/Y 时，仍按 Row/Col 换算到旧版网格位置。
/// </summary>
public static class WorkspaceLayout
{
    /// <summary>单元格边长（逻辑像素）。</summary>
    public const double CellSize = 30;

    /// <summary>标题区高度；与上下边框合计为 50。</summary>
    public const double HeaderHeight = 50;

    /// <summary>左右留白合计：每边 8。</summary>
    public const double HorizontalPadding = 16;

    /// <summary>画布与导出图片的边距。</summary>
    public const double Padding = 24;

    /// <summary>旧版按行/列排布时的列宽。</summary>
    public const double CellWidth = 250;

    /// <summary>旧版按行/列排布时的行高。</summary>
    public const double CellHeight = 400;

    /// <summary>
    /// 节点实际使用的象限数：优先取解析后的文档尺寸。
    /// </summary>
    public static int ResolvePartCount(
        WorkspaceNode node,
        ShapeDocument? document,
        int fallbackParts)
    {
        if (document is not null)
        {
            return Math.Max(1, document.PartCount);
        }

        if (node.Parts > 0)
        {
            return node.Parts;
        }

        return Math.Max(1, fallbackParts);
    }

    /// <summary>
    /// 节点实际使用的层数：优先取解析后的文档尺寸。
    /// </summary>
    public static int ResolveLayerCount(
        WorkspaceNode node,
        ShapeDocument? document)
    {
        if (document is not null)
        {
            return Math.Max(1, document.LayerCount);
        }

        return node.Layers > 0 ? node.Layers : 1;
    }

    /// <summary>
    /// 按内容计算节点尺寸。
    /// </summary>
    public static Size SizeFor(
        WorkspaceNode node,
        ShapeDocument? document,
        int fallbackParts)
    {
        int parts = ResolvePartCount(node, document, fallbackParts);

        int layers = ResolveLayerCount(node, document);

        return new Size(
            parts * CellSize + HorizontalPadding,
            HeaderHeight + layers * CellSize);
    }

    /// <summary>
    /// 节点左上角坐标：有 X/Y 用 X/Y，否则沿用 Row/Col。
    /// </summary>
    public static Rect BoundsFor(
        WorkspaceNode node,
        Size size)
    {
        double left = node.X ?? (Padding + node.Col * CellWidth);

        double top = node.Y ?? (Padding + node.Row * CellHeight);

        return new Rect(left, top, size.Width, size.Height);
    }

    public static Point CenterOf(
        Rect bounds)
    {
        return new Point(
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2);
    }

    /// <summary>
    /// 从矩形中心朝目标点作射线，返回与矩形边界的交点。
    /// 用于让连线贴住节点的实际宽高，而不是固定锚点。
    /// </summary>
    public static Point BoundaryToward(
        Rect bounds,
        Point target)
    {
        Point center = CenterOf(bounds);

        double dx = target.X - center.X;

        double dy = target.Y - center.Y;

        if (Math.Abs(dx) < 0.001 &&
            Math.Abs(dy) < 0.001)
        {
            return center;
        }

        double scaleX = Math.Abs(dx) < 0.001
            ? double.PositiveInfinity
            : bounds.Width / 2 / Math.Abs(dx);

        double scaleY = Math.Abs(dy) < 0.001
            ? double.PositiveInfinity
            : bounds.Height / 2 / Math.Abs(dy);

        double scale = Math.Min(scaleX, scaleY);

        return new Point(
            center.X + dx * scale,
            center.Y + dy * scale);
    }

    /// <summary>
    /// 把边界点沿远离中心的方向外移指定距离。
    /// </summary>
    public static Point OffsetOutward(
        Rect bounds,
        Point boundary,
        double distance)
    {
        Point center = CenterOf(bounds);

        double dx = boundary.X - center.X;

        double dy = boundary.Y - center.Y;

        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
        {
            return boundary;
        }

        return new Point(
            boundary.X + dx / length * distance,
            boundary.Y + dy / length * distance);
    }
}

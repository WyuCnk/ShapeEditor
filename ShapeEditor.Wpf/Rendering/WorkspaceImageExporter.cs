using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 把路径图导出成 PNG：节点高度按图形比例自适应，行高取该行最高节点。
/// </summary>
public static class WorkspaceImageExporter
{
    private const double ColWidth = 250;
    private const double NodeWidth = 210;
    private const double Padding = 24;
    private const double RowGap = 90;
    private const double HeaderHeight = 46;
    private const double MaxImageWidth = NodeWidth - 24;
    private const double MaxImageHeight = 170;

    private static readonly Brush EdgeBrush = new SolidColorBrush(Color.FromRgb(0x51, 0x2B, 0xD4));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x76));
    private static readonly Brush RefBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x7B, 0xBF));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x27, 0xA0));

    public static void Export(
        string filePath,
        IReadOnlyList<WorkspaceNode> nodes,
        IReadOnlyList<WorkspaceEdge> edges,
        Func<WorkspaceNode, ShapeDocument?> documentOf)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var drawings = new Dictionary<string, DrawingGroup>();
        var sizes = new Dictionary<string, Size>();
        var heights = new Dictionary<string, double>();

        foreach (WorkspaceNode node in nodes)
        {
            ShapeDocument? document = documentOf(node);

            if (document is null)
            {
                heights[node.Id] = 120;

                continue;
            }

            DrawingGroup drawing = ShapeGridDrawing.Build(document, 24, 24);

            Rect bounds = drawing.Bounds;

            double scale = Math.Min(
                Math.Min(MaxImageWidth / Math.Max(1, bounds.Width), MaxImageHeight / Math.Max(1, bounds.Height)),
                1);

            // ShapeGridDrawing.Build 返回的 DrawingGroup 是冻结的，
            // 不能直接改 Transform，包一层再缩放
            var scaled = new DrawingGroup
            {
                Transform = new ScaleTransform(scale, scale)
            };

            scaled.Children.Add(drawing);

            drawings[node.Id] = scaled;
            sizes[node.Id] = new Size(bounds.Width * scale, bounds.Height * scale);
            heights[node.Id] = HeaderHeight + bounds.Height * scale + 14;
        }

        int maxRow = nodes.Max(node => node.Row);

        var rowHeights = new double[maxRow + 1];

        for (int row = 0; row <= maxRow; row++)
        {
            rowHeights[row] = nodes
                .Where(node => node.Row == row)
                .Select(node => heights[node.Id])
                .DefaultIfEmpty(120)
                .Max();
        }

        var rowY = new double[maxRow + 1];

        for (int row = 1; row <= maxRow; row++)
        {
            rowY[row] = rowY[row - 1] + rowHeights[row - 1] + RowGap;
        }

        int maxCol = nodes.Max(node => node.Col);

        double width = Padding * 2 + (maxCol + 1) * ColWidth - (ColWidth - NodeWidth);
        double height = rowY[maxRow] + rowHeights[maxRow] + Padding;

        var visual = new DrawingVisual();

        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            DrawEdges(context, nodes, edges, rowY, heights);

            foreach (WorkspaceNode node in nodes)
            {
                DrawNode(context, node, rowY[node.Row], heights[node.Id], drawings, sizes);
            }
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = File.Create(filePath);

        encoder.Save(stream);
    }

    private static void DrawEdges(
        DrawingContext context,
        IReadOnlyList<WorkspaceNode> nodes,
        IReadOnlyList<WorkspaceEdge> edges,
        double[] rowY,
        Dictionary<string, double> heights)
    {
        var byId = nodes.ToDictionary(node => node.Id);

        var outIndex = new Dictionary<string, int>();
        var outCount = edges.GroupBy(edge => edge.FromId).ToDictionary(group => group.Key, group => group.Count());
        var inIndex = new Dictionary<string, int>();
        var inCount = edges.GroupBy(edge => edge.ToId).ToDictionary(group => group.Key, group => group.Count());

        foreach (WorkspaceEdge edge in edges)
        {
            if (!byId.TryGetValue(edge.FromId, out WorkspaceNode? from) ||
                !byId.TryGetValue(edge.ToId, out WorkspaceNode? to))
            {
                continue;
            }

            int oi = outIndex.TryGetValue(edge.FromId, out int o) ? o : 0;
            outIndex[edge.FromId] = oi + 1;

            int ii = inIndex.TryGetValue(edge.ToId, out int i) ? i : 0;
            inIndex[edge.ToId] = ii + 1;

            double x1 = Padding + from.Col * ColWidth + NodeWidth / 2 +
                        Spread(oi, outCount[edge.FromId], 30);
            double y1 = rowY[from.Row] + heights[from.Id];

            double x2 = Padding + to.Col * ColWidth + NodeWidth / 2 +
                        Spread(ii, inCount[edge.ToId], 30);
            double y2 = rowY[to.Row];

            var pen = new Pen(EdgeBrush, 2);

            context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2 - 4));

            DrawArrow(context, x1, y1, x2, y2 - 4);

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

            double lx = x1 + dx * t + nx * offset;
            double ly = y1 + dy * t + ny * offset;

            FormattedText text = CreateText(edge.Label, 12, LabelBrush);

            var rect = new Rect(
                lx - text.Width / 2 - 6,
                ly - text.Height / 2 - 2,
                text.Width + 12,
                text.Height + 4);

            context.DrawRectangle(Brushes.White, null, rect);
            context.DrawText(text, new Point(rect.Left + 6, rect.Top + 2));
        }
    }

    private static void DrawArrow(
        DrawingContext context,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        double angle = Math.Atan2(y2 - y1, x2 - x1);
        const double size = 10;

        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        var geometry = new StreamGeometry();

        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(new Point(x2, y2), true, true);

            geometryContext.LineTo(
                new Point(
                    x2 - size * cos + size * 0.55 * sin,
                    y2 - size * sin - size * 0.55 * cos),
                true,
                false);

            geometryContext.LineTo(
                new Point(
                    x2 - size * cos - size * 0.55 * sin,
                    y2 - size * sin + size * 0.55 * cos),
                true,
                false);
        }

        geometry.Freeze();

        context.DrawGeometry(EdgeBrush, null, geometry);
    }

    private static void DrawNode(
        DrawingContext context,
        WorkspaceNode node,
        double y,
        double height,
        Dictionary<string, DrawingGroup> drawings,
        Dictionary<string, Size> sizes)
    {
        double x = Padding + node.Col * ColWidth;

        var border = new Pen(
            new SolidColorBrush(node.IsResult
                ? Color.FromRgb(0x51, 0x2B, 0xD4)
                : node.Kind == "ref"
                    ? Color.FromRgb(0xB9, 0xA9, 0xEE)
                    : Color.FromRgb(0xD0, 0xD0, 0xD0)),
            node.IsResult ? 2 : 1.2);

        Brush background = node.IsResult
            ? new SolidColorBrush(Color.FromRgb(0xFA, 0xF8, 0xFF))
            : Brushes.White;

        context.DrawRoundedRectangle(background, border, new Rect(x, y, NodeWidth, height), 12, 12);

        FormattedText title = CreateText($"#{node.Seq} · {NodeTitle(node)}", 12, TextBrush);

        context.DrawText(title, new Point(x + 10, y + 10));

        if (drawings.TryGetValue(node.Id, out DrawingGroup? drawing) &&
            sizes.TryGetValue(node.Id, out Size size))
        {
            context.PushTransform(new TranslateTransform(
                x + (NodeWidth - size.Width) / 2,
                y + HeaderHeight));

            context.DrawDrawing(drawing);

            context.Pop();
        }
    }

    private static string NodeTitle(
        WorkspaceNode node)
    {
        if (node.Kind == "ref")
        {
            return "堆叠输入";
        }

        return node.IsResult ? node.Label : "素材节点";
    }

    private static FormattedText CreateText(
        string text,
        double size,
        Brush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            size,
            brush,
            1.0);
    }

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

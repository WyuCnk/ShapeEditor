using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 把路径图导出成 PNG。
///
/// 节点尺寸与画布一致：宽度 = 象限数 × 30 + 16，高度 = 50 + 层数 × 30。
/// 预览使用固定 30×30 单元格，按 1 倍尺寸绘制 c/S/P 字母；
/// 连线不画标签，箭头在节点之后绘制，避免被卡片遮住。
/// </summary>
public static class WorkspaceImageExporter
{
    private const double CellSize = 30;

    private const double HeaderHeight = 50;

    private const double Padding = 24;

    private static readonly Brush EdgeBrush = CreateBrush(0x51, 0x2B, 0xD4);

    private static readonly Brush TextBrush = CreateBrush(0x6B, 0x6B, 0x76);

    private static readonly Brush SummaryBrush = CreateBrush(0x8B, 0x8B, 0x96);

    private static readonly Brush LetterBrush = CreateBrush(0x33, 0x33, 0x3D);

    private static readonly Brush PlaceholderBrush = CreateBrush(0xB0, 0xB0, 0xBA);

    private static readonly Pen EdgePen = CreatePen(EdgeBrush, 2);

    private static readonly Pen GridPen = CreatePen(CreateBrush(0xC8, 0xC8, 0xD0), 1);

    public static void Export(
        string filePath,
        IReadOnlyList<WorkspaceNode> nodes,
        IReadOnlyList<WorkspaceEdge> edges,
        Func<WorkspaceNode, ShapeDocument?> documentOf,
        int fallbackParts = 4)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var documents = new Dictionary<string, ShapeDocument?>();

        var layouts = new Dictionary<string, Rect>();

        double minLeft = double.MaxValue;

        double minTop = double.MaxValue;

        double maxRight = double.MinValue;

        double maxBottom = double.MinValue;

        foreach (WorkspaceNode node in nodes)
        {
            ShapeDocument? document = documentOf(node);

            documents[node.Id] = document;

            Size size = WorkspaceLayout.SizeFor(node, document, fallbackParts);

            Rect bounds = WorkspaceLayout.BoundsFor(node, size);

            layouts[node.Id] = bounds;

            minLeft = Math.Min(minLeft, bounds.Left);
            minTop = Math.Min(minTop, bounds.Top);
            maxRight = Math.Max(maxRight, bounds.Right);
            maxBottom = Math.Max(maxBottom, bounds.Bottom);
        }

        /*
         * 画布 = 所有节点包围盒 + 四周 24px 边距。
         */
        double width = Math.Max(1, maxRight - minLeft) + Padding * 2;

        double height = Math.Max(1, maxBottom - minTop) + Padding * 2;

        double offsetX = Padding - minLeft;

        double offsetY = Padding - minTop;

        var shifted = new Dictionary<string, Rect>();

        foreach (KeyValuePair<string, Rect> entry in layouts)
        {
            Rect rect = entry.Value;

            shifted[entry.Key] = new Rect(
                rect.Left + offsetX,
                rect.Top + offsetY,
                rect.Width,
                rect.Height);
        }

        var visual = new DrawingVisual();

        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, width, height));

            // 连线在节点之下。
            DrawEdges(context, edges, shifted);

            foreach (WorkspaceNode node in nodes)
            {
                DrawNode(
                    context,
                    node,
                    shifted[node.Id],
                    documents[node.Id]);
            }

            // 箭头在节点之后，尖端贴住目标边框，不会被卡片遮住。
            DrawArrows(context, edges, shifted);
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

    #region 连线与箭头

    private static void DrawEdges(
        DrawingContext context,
        IReadOnlyList<WorkspaceEdge> edges,
        IReadOnlyDictionary<string, Rect> layouts)
    {
        foreach ((Rect from, Rect to) in ResolveEdges(edges, layouts))
        {
            Point start = WorkspaceLayout.BoundaryToward(
                from,
                WorkspaceLayout.CenterOf(to));

            Point entry = WorkspaceLayout.BoundaryToward(
                to,
                WorkspaceLayout.CenterOf(from));

            Point tip = WorkspaceLayout.OffsetOutward(to, entry, 1);

            context.DrawLine(EdgePen, start, tip);
        }
    }

    private static void DrawArrows(
        DrawingContext context,
        IReadOnlyList<WorkspaceEdge> edges,
        IReadOnlyDictionary<string, Rect> layouts)
    {
        foreach ((Rect from, Rect to) in ResolveEdges(edges, layouts))
        {
            Point start = WorkspaceLayout.BoundaryToward(
                from,
                WorkspaceLayout.CenterOf(to));

            Point entry = WorkspaceLayout.BoundaryToward(
                to,
                WorkspaceLayout.CenterOf(from));

            Point tip = WorkspaceLayout.OffsetOutward(to, entry, 1);

            DrawArrow(context, start, tip);
        }
    }

    private static IEnumerable<(Rect From, Rect To)> ResolveEdges(
        IReadOnlyList<WorkspaceEdge> edges,
        IReadOnlyDictionary<string, Rect> layouts)
    {
        foreach (WorkspaceEdge edge in edges)
        {
            if (!layouts.TryGetValue(edge.FromId, out Rect from) ||
                !layouts.TryGetValue(edge.ToId, out Rect to))
            {
                continue;
            }

            yield return (from, to);
        }
    }

    private static void DrawArrow(
        DrawingContext context,
        Point start,
        Point tip)
    {
        double angle = Math.Atan2(
            tip.Y - start.Y,
            tip.X - start.X);

        const double head = 10;

        const double halfWidth = 5.5;

        double cos = Math.Cos(angle);

        double sin = Math.Sin(angle);

        var geometry = new StreamGeometry();

        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(tip, true, true);

            geometryContext.LineTo(
                new Point(
                    tip.X - head * cos + halfWidth * sin,
                    tip.Y - head * sin - halfWidth * cos),
                true,
                false);

            geometryContext.LineTo(
                new Point(
                    tip.X - head * cos - halfWidth * sin,
                    tip.Y - head * sin + halfWidth * cos),
                true,
                false);
        }

        geometry.Freeze();

        context.DrawGeometry(EdgeBrush, null, geometry);
    }

    #endregion

    #region 节点

    private static void DrawNode(
        DrawingContext context,
        WorkspaceNode node,
        Rect rect,
        ShapeDocument? document)
    {
        var borderColor = node.IsResult
            ? Color.FromRgb(0x51, 0x2B, 0xD4)
            : node.Kind == "ref"
                ? Color.FromRgb(0xB9, 0xA9, 0xEE)
                : Color.FromRgb(0xD0, 0xD0, 0xD0);

        var border = new Pen(
            CreateBrush(borderColor),
            node.IsResult ? 2 : 1);

        border.Freeze();

        Brush background = node.IsResult
            ? CreateBrush(0xFA, 0xF8, 0xFF)
            : Brushes.White;

        context.DrawRoundedRectangle(
            background,
            border,
            rect,
            12,
            12);

        FormattedText title = CreateText(
            $"#{node.Seq} · {NodeTitle(node)}",
            12,
            TextBrush,
            bold: true);

        title.MaxTextWidth = Math.Max(1, rect.Width - 20);

        title.MaxLineCount = 1;

        title.Trimming = TextTrimming.CharacterEllipsis;

        context.DrawText(title, new Point(rect.Left + 10, rect.Top + 8));

        if (document is null)
        {
            FormattedText empty = CreateText(
                node.Kind == "ref" ? "选择输入" : "空",
                12,
                PlaceholderBrush);

            empty.MaxTextWidth = Math.Max(1, rect.Width - 20);

            empty.MaxLineCount = 1;

            empty.Trimming = TextTrimming.CharacterEllipsis;

            context.DrawText(
                empty,
                new Point(
                    rect.Left + (rect.Width - Math.Min(empty.Width, rect.Width - 20)) / 2,
                    rect.Top + HeaderHeight + (rect.Height - HeaderHeight - empty.Height) / 2));

            return;
        }

        FormattedText summary = CreateText(
            ShapeText.Summary(document),
            10,
            SummaryBrush);

        summary.MaxTextWidth = Math.Max(1, rect.Width - 20);

        summary.MaxLineCount = 1;

        summary.Trimming = TextTrimming.CharacterEllipsis;

        context.DrawText(
            summary,
            new Point(rect.Left + 10, rect.Top + 28));

        DrawPreview(context, document, rect);
    }

    /// <summary>
    /// 固定 30×30 单元格，水平居中；字母只表示类型：
    /// c 晶体、S 普通形状、P 顶针，空位只画格线。
    /// </summary>
    private static void DrawPreview(
        DrawingContext context,
        ShapeDocument document,
        Rect nodeRect)
    {
        double gridWidth = document.PartCount * CellSize;

        double gridHeight = document.LayerCount * CellSize;

        double gridLeft = nodeRect.Left + (nodeRect.Width - gridWidth) / 2;

        double gridTop = nodeRect.Top + HeaderHeight;

        IReadOnlyList<string> lines = ShapeText.Lines(document);

        for (int visualRow = 0; visualRow < document.LayerCount; visualRow++)
        {
            string line = lines[visualRow];

            double cellTop = gridTop + visualRow * CellSize;

            for (int part = 0; part < document.PartCount; part++)
            {
                char letter = part < line.Length ? line[part] : '-';

                if (letter == '-')
                {
                    continue;
                }

                FormattedText text = CreateText(
                    letter.ToString(),
                    16,
                    LetterBrush,
                    bold: true,
                    family: "Consolas, Microsoft YaHei UI");

                context.DrawText(
                    text,
                    new Point(
                        gridLeft + part * CellSize + (CellSize - text.Width) / 2,
                        cellTop + (CellSize - text.Height) / 2));
            }
        }

        DrawGrid(
            context,
            document.PartCount,
            document.LayerCount,
            gridLeft,
            gridTop,
            gridWidth,
            gridHeight);
    }

    private static void DrawGrid(
        DrawingContext context,
        int columns,
        int rows,
        double left,
        double top,
        double width,
        double height)
    {
        /*
         * 外框向内缩半个像素，避免线条被卡片边界裁掉。
         */
        const double half = 0.5;

        double right = left + Math.Max(half, width - half);

        double bottom = top + Math.Max(half, height - half);

        for (int column = 0; column <= columns; column++)
        {
            double x = column == 0
                ? left + half
                : column == columns
                    ? right
                    : left + column * CellSize;

            context.DrawLine(
                GridPen,
                new Point(x, top + half),
                new Point(x, bottom));
        }

        for (int row = 0; row <= rows; row++)
        {
            double y = row == 0
                ? top + half
                : row == rows
                    ? bottom
                    : top + row * CellSize;

            context.DrawLine(
                GridPen,
                new Point(left + half, y),
                new Point(right, y));
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

    #endregion

    #region 绘制工具

    private static FormattedText CreateText(
        string text,
        double size,
        Brush brush,
        bool bold = false,
        string family = "Microsoft YaHei UI, Segoe UI")
    {
        var typeface = new Typeface(
            new FontFamily(family),
            FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            1.0);
    }

    private static Brush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        return CreateBrush(Color.FromRgb(red, green, blue));
    }

    private static Brush CreateBrush(
        Color color)
    {
        var brush = new SolidColorBrush(color);

        brush.Freeze();

        return brush;
    }

    private static Pen CreatePen(
        Brush brush,
        double thickness)
    {
        var pen = new Pen(brush, thickness);

        pen.Freeze();

        return pen;
    }

    #endregion
}

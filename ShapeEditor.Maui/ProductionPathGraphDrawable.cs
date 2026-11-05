using System.Text;
using ShapeEditor.Core.Models;
using GraphicsFont = Microsoft.Maui.Graphics.Font;

namespace ShapeEditor.Maui;

/// <summary>
/// 制作路径的分层 DAG 绘制。
///
/// 整张图只用一个 GraphicsView：先画箭头，再逐个画节点背景、标题、
/// 30 像素单元格的竖式图预览，最后补节点边框。节点数量增加时不会新增控件，
/// 滚动与重绘都由同一个画布承担。
///
/// 箭头规则：跨层从上层底边中点连到下层顶边中点；同层（交换 → 废料、
/// 目标 → 末端废料）从相对的侧边中点连到侧边中点。
/// 废料节点用灰色虚线边框、0.55 透明度的预览和灰色虚线箭头淡显。
/// </summary>
public sealed class ProductionPathGraphDrawable : IDrawable
{
    private static readonly Color CanvasBackground = Color.FromArgb("#FBFAFF");

    private static readonly Color NodeBackground = Colors.White;

    private static readonly Color TargetBackground = Color.FromArgb("#FAF8FF");

    private static readonly Color WasteBackground = Color.FromArgb("#FBFBFC");

    private static readonly Color PendingBackground = Color.FromArgb("#FCFCFD");

    private static readonly Color NodeStroke = Color.FromArgb("#E6E2F2");

    private static readonly Color TargetStroke = Color.FromArgb("#512BD4");

    private static readonly Color WasteStroke = Color.FromArgb("#C4C4CC");

    private static readonly Color PendingStroke = Color.FromArgb("#D9D4E8");

    private static readonly Color Accent = Color.FromArgb("#512BD4");

    private static readonly Color WasteLine = Color.FromArgb("#9A9AA5");

    private static readonly Color HeadText = Color.FromArgb("#4A3F72");

    private static readonly Color WasteHeadText = Color.FromArgb("#9A9AA5");

    private static readonly Color MutedText = Color.FromArgb("#8A8A95");

    /// <summary>废料箭头的虚线样式。</summary>
    private static readonly float[] WasteDash = { 5, 4 };

    /// <summary>废料 / 待接入节点的虚线边框样式。</summary>
    private static readonly float[] WasteBorderDash = { 4, 3 };

    private const float TitleFontSize = 10.5f;

    private const float TitleLineHeight = 13f;

    private const float TitlePadding = 8f;

    private const float TitleTop = 7f;

    private const float ArrowLength = 10f;

    private const float ArrowHalfWidth = 5f;

    private const float NodeCornerRadius = 10f;

    private readonly ProductionPathGraphLayout _layout;

    public ProductionPathGraphDrawable(
        ProductionPathGraphLayout layout)
    {
        _layout = layout;
    }

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        canvas.FillColor = CanvasBackground;

        canvas.FillRectangle(
            0,
            0,
            (float)_layout.Width,
            (float)_layout.Height);

        DrawEdges(canvas);

        foreach (ProductionPathNodeBox node in _layout.Nodes)
        {
            DrawNode(canvas, node);
        }
    }

    private void DrawEdges(
        ICanvas canvas)
    {
        foreach (ProductionPathEdge edge in _layout.Edges)
        {
            Color color = edge.IsWaste ? WasteLine : Accent;

            (Point start, Point end) = Anchors(edge.From, edge.To);

            canvas.SaveState();

            canvas.StrokeColor = color;

            canvas.StrokeSize = 2;

            canvas.StrokeDashPattern = edge.IsWaste ? WasteDash : null;

            canvas.DrawLine(
                (float)start.X,
                (float)start.Y,
                (float)end.X,
                (float)end.Y);

            canvas.RestoreState();

            DrawArrowHead(canvas, start, end, color);
        }
    }

    private static void DrawNode(
        ICanvas canvas,
        ProductionPathNodeBox node)
    {
        Rect bounds = node.Bounds;

        Color background = node.IsWaste
            ? WasteBackground
            : node.IsPending
                ? PendingBackground
                : node.IsTarget
                    ? TargetBackground
                    : NodeBackground;

        Color stroke = node.IsWaste
            ? WasteStroke
            : node.IsPending
                ? PendingStroke
                : node.IsTarget
                    ? TargetStroke
                    : NodeStroke;

        canvas.SaveState();

        canvas.FillColor = background;

        canvas.FillRoundedRectangle(
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height,
            NodeCornerRadius);

        DrawTitle(canvas, node);

        if (node.Node.Shape is ShapeDocument shape)
        {
            // 预览固定 30 像素单元格，节点宽度已经按象限数预留，不需要缩放。
            double previewWidth = shape.PartCount * ProductionPathGraphLayout.CellSize;

            float previewX = (float)(bounds.X + (bounds.Width - previewWidth) / 2);

            float previewY = (float)(bounds.Y + ProductionPathGraphLayout.HeaderHeight);

            canvas.SaveState();

            if (node.IsWaste)
            {
                canvas.Alpha = 0.55f;
            }

            // 预览底色固定白色，与工作区 / Web 的竖式图一致；
            // 目标节点的淡紫、废料节点的灰白只留在卡片外圈。
            canvas.FillColor = Colors.White;

            canvas.FillRectangle(
                previewX,
                previewY,
                (float)previewWidth,
                (float)(shape.LayerCount * ProductionPathGraphLayout.CellSize));

            WorkspaceCellDrawable.DrawDocument(
                canvas,
                shape,
                previewX,
                previewY,
                (float)ProductionPathGraphLayout.CellSize);

            canvas.RestoreState();
        }
        else if (node.IsPending)
        {
            float bodyHeight = (float)(bounds.Height - ProductionPathGraphLayout.HeaderHeight);

            canvas.Font = GraphicsFont.Default;

            canvas.FontSize = 11;

            canvas.FontColor = MutedText;

            canvas.DrawString(
                "待接入",
                (float)bounds.X,
                (float)(bounds.Y + ProductionPathGraphLayout.HeaderHeight),
                (float)bounds.Width,
                bodyHeight,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
        }

        // 边框最后画：压住预览的格线，让圆角与虚线保持完整。
        canvas.StrokeColor = stroke;

        canvas.StrokeSize = 1;

        if (node.IsWaste || node.IsPending)
        {
            canvas.StrokeDashPattern = WasteBorderDash;
        }

        canvas.DrawRoundedRectangle(
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height,
            NodeCornerRadius);

        canvas.StrokeDashPattern = null;

        canvas.RestoreState();
    }

    private static void DrawTitle(
        ICanvas canvas,
        ProductionPathNodeBox node)
    {
        string title = node.Node.Title;

        float maxWidth = (float)node.Width - TitlePadding * 2;

        if (string.IsNullOrWhiteSpace(title) || maxWidth <= 0)
        {
            return;
        }

        canvas.Font = GraphicsFont.DefaultBold;

        canvas.FontSize = TitleFontSize;

        canvas.FontColor = node.IsWaste ? WasteHeadText : HeadText;

        IReadOnlyList<string> lines = WrapTitle(title, maxWidth);

        float x = (float)node.X + TitlePadding;

        float y = (float)node.Y + TitleTop;

        for (int index = 0; index < lines.Count; index++)
        {
            // 第二行从空格处折行时去掉前导空格，标题左对齐才整齐。
            canvas.DrawString(
                index == 0 ? lines[index] : lines[index].TrimStart(),
                x,
                y,
                maxWidth,
                TitleLineHeight,
                HorizontalAlignment.Left,
                VerticalAlignment.Top);

            y += TitleLineHeight;
        }
    }

    /// <summary>
    /// 标题最多两行；放不下时在第二行末尾加省略号，避免压到下面的预览。
    /// 中文标题没有空格，按字符贪心比按词换行更稳。
    /// </summary>
    private static IReadOnlyList<string> WrapTitle(
        string title,
        float maxWidth)
    {
        if (Measure(title) <= maxWidth)
        {
            return new[] { title };
        }

        var first = new StringBuilder();

        int index = 0;

        while (index < title.Length)
        {
            string candidate = first.ToString() + title[index];

            if (first.Length > 0 && Measure(candidate) > maxWidth)
            {
                break;
            }

            first.Append(title[index]);

            index++;
        }

        string rest = title[index..];

        if (Measure(rest) <= maxWidth)
        {
            return new[] { first.ToString(), rest };
        }

        var second = new StringBuilder();

        while (index < title.Length)
        {
            string candidate = second.ToString() + title[index];

            if (second.Length > 0 && Measure(candidate) > maxWidth)
            {
                second.Append('…');

                break;
            }

            second.Append(title[index]);

            index++;
        }

        return new[] { first.ToString(), second.ToString() };
    }

    /// <summary>
    /// 估算一段标题的显示宽度。
    ///
    /// 不调用 <c>ICanvas.GetStringSize</c>：部分渲染后端不实现文字测量，
    /// 而节点标题基本是中文，按「中文占满字号、ASCII 占约 0.56 字号」估算足够稳定。
    /// </summary>
    private static float Measure(
        string text)
    {
        float width = 0;

        foreach (char character in text)
        {
            width += IsWide(character)
                ? TitleFontSize
                : TitleFontSize * 0.56f;
        }

        return width;
    }

    /// <summary>是否按全角宽度估算（CJK 与全角标点）。</summary>
    private static bool IsWide(
        char character)
    {
        return character >= 0x2E80;
    }

    /// <summary>
    /// 分层图的两个锚点：跨层用底边中点 → 顶边中点，
    /// 同层（交换 → 废料）用相对的侧边中点。
    /// </summary>
    private static (Point Start, Point End) Anchors(
        ProductionPathNodeBox from,
        ProductionPathNodeBox to)
    {
        Rect fromBounds = from.Bounds;

        Rect toBounds = to.Bounds;

        if (toBounds.Y >= fromBounds.Bottom - 0.5)
        {
            return (
                new Point(fromBounds.X + fromBounds.Width / 2, fromBounds.Bottom),
                new Point(toBounds.X + toBounds.Width / 2, toBounds.Y));
        }

        if (fromBounds.Y >= toBounds.Bottom - 0.5)
        {
            return (
                new Point(fromBounds.X + fromBounds.Width / 2, fromBounds.Y),
                new Point(toBounds.X + toBounds.Width / 2, toBounds.Bottom));
        }

        bool rightward = toBounds.X >= fromBounds.X;

        double fromY = fromBounds.Y + fromBounds.Height / 2;

        double toY = toBounds.Y + toBounds.Height / 2;

        return rightward
            ? (
                new Point(fromBounds.Right, fromY),
                new Point(toBounds.X, toY))
            : (
                new Point(fromBounds.X, fromY),
                new Point(toBounds.Right, toY));
    }

    private static void DrawArrowHead(
        ICanvas canvas,
        Point start,
        Point end,
        Color color)
    {
        double dx = end.X - start.X;

        double dy = end.Y - start.Y;

        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
        {
            return;
        }

        double ux = dx / length;

        double uy = dy / length;

        double baseX = end.X - ux * ArrowLength;

        double baseY = end.Y - uy * ArrowLength;

        var path = new PathF();

        path.MoveTo((float)end.X, (float)end.Y);

        path.LineTo(
            (float)(baseX - uy * ArrowHalfWidth),
            (float)(baseY + ux * ArrowHalfWidth));

        path.LineTo(
            (float)(baseX + uy * ArrowHalfWidth),
            (float)(baseY - ux * ArrowHalfWidth));

        path.Close();

        canvas.SaveState();

        canvas.FillColor = color;

        canvas.FillPath(path);

        canvas.RestoreState();
    }
}

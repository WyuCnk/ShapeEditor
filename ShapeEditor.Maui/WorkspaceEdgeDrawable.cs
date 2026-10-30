namespace ShapeEditor.Maui;

/// <summary>
/// 一条连线的两个端点（已经投影到节点边框外 1px）。
/// </summary>
public readonly record struct WorkspaceLink(
    float X1,
    float Y1,
    float X2,
    float Y2);

/// <summary>
/// 连线几何：锚点取「节点矩形与连心线的交点」，
/// 终点沿连线方向外延 1px，让箭头尖端贴住目标边框。
/// </summary>
public static class WorkspaceLinkGeometry
{
    /// <summary>端点外延距离。</summary>
    private const double Extension = 1;

    public static WorkspaceLink Create(
        Rect from,
        Rect to)
    {
        var fromCenter = new Point(
            from.X + from.Width / 2,
            from.Y + from.Height / 2);

        var toCenter = new Point(
            to.X + to.Width / 2,
            to.Y + to.Height / 2);

        double dx = toCenter.X - fromCenter.X;
        double dy = toCenter.Y - fromCenter.Y;

        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 0.001)
        {
            return new WorkspaceLink(
                (float)fromCenter.X,
                (float)fromCenter.Y,
                (float)toCenter.X,
                (float)toCenter.Y);
        }

        double ux = dx / length;
        double uy = dy / length;

        Point start = Intersect(from, fromCenter, ux, uy);

        Point end = Intersect(to, toCenter, -ux, -uy);

        // 两个端点都沿连线方向外延到节点外侧 1px，
        // 箭头尖端因此贴在目标边框上而不是被卡片盖住。
        return new WorkspaceLink(
            (float)(start.X + ux * Extension),
            (float)(start.Y + uy * Extension),
            (float)(end.X - ux * Extension),
            (float)(end.Y - uy * Extension));
    }

    /// <summary>
    /// 从矩形中心沿方向 (ux, uy) 射出，求与矩形边框的交点。
    /// </summary>
    private static Point Intersect(
        Rect rect,
        Point center,
        double ux,
        double uy)
    {
        double halfWidth = rect.Width / 2;
        double halfHeight = rect.Height / 2;

        double distanceX = double.PositiveInfinity;
        double distanceY = double.PositiveInfinity;

        if (Math.Abs(ux) > 1e-9)
        {
            distanceX = halfWidth / Math.Abs(ux);
        }

        if (Math.Abs(uy) > 1e-9)
        {
            distanceY = halfHeight / Math.Abs(uy);
        }

        double distance = Math.Min(distanceX, distanceY);

        if (double.IsInfinity(distance))
        {
            return center;
        }

        return new Point(
            center.X + ux * distance,
            center.Y + uy * distance);
    }
}

/// <summary>
/// 连线层：只画线段，位于节点卡片下方。
/// </summary>
public sealed class WorkspaceLineDrawable : IDrawable
{
    private static readonly Color Accent = Color.FromArgb("#512BD4");

    public List<WorkspaceLink> Links { get; } = new();

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        canvas.StrokeColor = Accent;
        canvas.StrokeSize = 2;

        foreach (WorkspaceLink link in Links)
        {
            canvas.DrawLine(link.X1, link.Y1, link.X2, link.Y2);
        }
    }
}

/// <summary>
/// 箭头层：只画三角形，位于节点卡片上方。
/// </summary>
public sealed class WorkspaceArrowDrawable : IDrawable
{
    private static readonly Color Accent = Color.FromArgb("#512BD4");

    private const float ArrowLength = 10;

    private const float ArrowHalfWidth = 5.5f;

    public List<WorkspaceLink> Links { get; } = new();

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        canvas.FillColor = Accent;

        foreach (WorkspaceLink link in Links)
        {
            DrawArrow(canvas, link);
        }
    }

    private static void DrawArrow(
        ICanvas canvas,
        WorkspaceLink link)
    {
        float angle = (float)Math.Atan2(link.Y2 - link.Y1, link.X2 - link.X1);

        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        float baseX = link.X2 - ArrowLength * cos;
        float baseY = link.Y2 - ArrowLength * sin;

        var path = new PathF();

        path.MoveTo(link.X2, link.Y2);

        path.LineTo(
            baseX + ArrowHalfWidth * sin,
            baseY - ArrowHalfWidth * cos);

        path.LineTo(
            baseX - ArrowHalfWidth * sin,
            baseY + ArrowHalfWidth * cos);

        path.Close();

        canvas.FillPath(path);
    }
}

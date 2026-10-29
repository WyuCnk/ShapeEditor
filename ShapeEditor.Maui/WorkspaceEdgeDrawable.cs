namespace ShapeEditor.Maui;

/// <summary>
/// 工作区画布的连线层：直线 + 箭头 + 标签。
/// </summary>
public sealed class WorkspaceEdgeDrawable : IDrawable
{
    private static readonly Color Accent = Color.FromArgb("#512BD4");

    public List<EdgeShape> Edges { get; } = new();

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        foreach (EdgeShape edge in Edges)
        {
            canvas.StrokeColor = Accent;
            canvas.StrokeSize = 2;

            canvas.DrawLine(edge.X1, edge.Y1, edge.X2, edge.Y2);

            DrawArrow(canvas, edge.X1, edge.Y1, edge.X2, edge.Y2);


        }
    }

    private static void DrawArrow(
        ICanvas canvas,
        float x1,
        float y1,
        float x2,
        float y2)
    {
        float angle = (float)Math.Atan2(y2 - y1, x2 - x1);
        const float size = 9;

        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        var path = new PathF();

        path.MoveTo(x2, y2);
        path.LineTo(x2 - size * cos + size * 0.55f * sin, y2 - size * sin - size * 0.55f * cos);
        path.LineTo(x2 - size * cos - size * 0.55f * sin, y2 - size * sin + size * 0.55f * cos);
        path.Close();

        canvas.FillColor = Accent;

        canvas.FillPath(path);
    }
}

/// <summary>
/// 一条线的几何与标签位置。
/// </summary>
public sealed record EdgeShape(
    float X1,
    float Y1,
    float X2,
    float Y2,
    string Label,
    float LabelX,
    float LabelY);

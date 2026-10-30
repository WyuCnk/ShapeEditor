using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;

namespace ShapeEditor.Maui;

/// <summary>
/// 节点卡片里的竖式图预览：固定 30×30 单元格，按实际尺寸绘制，不压缩、不滚动。
///
/// 字母表示类型：c 晶体 / S 形状 / P 顶针；空位只画格线。
/// </summary>
public sealed class WorkspaceCellDrawable : IDrawable
{
    private static readonly Color GridLine = Color.FromArgb("#E6E2F2");

    private readonly ShapeDocument _document;

    private readonly int _layers;

    private readonly int _parts;

    public WorkspaceCellDrawable(
        ShapeDocument document)
    {
        _document = document;
        _layers = document.LayerCount;
        _parts = document.PartCount;
    }

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        if (_layers <= 0 || _parts <= 0)
        {
            return;
        }

        float cell = (float)WorkspaceLayout.CellSize;

        for (int visualRow = 0; visualRow < _layers; visualRow++)
        {
            // 最高层画在最上面，和编辑器竖式图一致
            int layer = _layers - 1 - visualRow;

            for (int part = 0; part < _parts; part++)
            {
                float x = part * cell;
                float y = visualRow * cell;

                ShapeCell shape = _document[layer, part];

                if (shape.Type != CellType.Empty)
                {
                    canvas.FillColor = ColorFor(shape);

                    canvas.FillRectangle(
                        x + 1,
                        y + 1,
                        cell - 2,
                        cell - 2);

                    canvas.FontColor = Colors.White;
                    canvas.FontSize = 16;

                    canvas.DrawString(
                        LetterOf(shape),
                        x,
                        y,
                        cell,
                        cell,
                        HorizontalAlignment.Center,
                        VerticalAlignment.Center);
                }

                canvas.StrokeColor = GridLine;
                canvas.StrokeSize = 0.6f;

                canvas.DrawRectangle(x, y, cell, cell);
            }
        }
    }

    private static string LetterOf(
        ShapeCell shape)
    {
        return shape.Type switch
        {
            CellType.Crystal => "c",
            CellType.Pin => "P",
            _ => "S"
        };
    }

    private static Color ColorFor(
        ShapeCell shape)
    {
        if (shape.HasColor && shape.ColorCode is char code)
        {
            return Color.FromUint(ShapePalette.FromCode(code));
        }

        return Color.FromArgb("#9E9E9E");
    }
}

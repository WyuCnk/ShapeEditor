using ShapeEditor.Core.Models;

namespace ShapeEditor.Maui;

/// <summary>
/// 批量查看用的静态预览：把图形按格子等比画满，不做任何交互。
/// </summary>
public sealed class ShapePreviewDrawable : IDrawable
{
    private ShapeDocument? _document;

    public void SetDocument(
        ShapeDocument? document)
    {
        _document = document;
    }

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        if (_document is null || _document.LayerCount <= 0 || _document.PartCount <= 0)
        {
            return;
        }

        int layers = _document.LayerCount;
        int parts = _document.PartCount;

        float cell = Math.Min(dirtyRect.Width / parts, dirtyRect.Height / layers);

        if (cell <= 0)
        {
            return;
        }

        float offsetX = dirtyRect.Left + (dirtyRect.Width - cell * parts) / 2;
        float offsetY = dirtyRect.Top + (dirtyRect.Height - cell * layers) / 2;

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                ShapeCell shape = _document[layer, part];

                // 第 0 层在最下面，和编辑器保持一致
                int row = layers - 1 - layer;

                float x = offsetX + part * cell;
                float y = offsetY + row * cell;

                if (shape.Type != CellType.Empty)
                {
                    canvas.FillColor = ColorFor(shape);

                    canvas.FillRectangle(
                        x + cell * 0.08f,
                        y + cell * 0.08f,
                        cell * 0.84f,
                        cell * 0.84f);
                }

                canvas.StrokeColor = Color.FromArgb("#E6E2F2");
                canvas.StrokeSize = 0.6f;

                canvas.DrawRectangle(x, y, cell, cell);
            }
        }
    }

    private static Color ColorFor(
        ShapeCell shape)
    {
        return shape.Type switch
        {
            CellType.Crystal => Color.FromArgb("#FF2D55"),
            CellType.Shape when shape.HasColor => Color.FromArgb("#4A90D9"),
            _ => Color.FromArgb("#2B2B2B")
        };
    }
}

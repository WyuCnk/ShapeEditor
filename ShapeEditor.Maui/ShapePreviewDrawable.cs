using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;

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

                if (shape.Type == CellType.Empty)
                {
                    // 空位画短横线，和 c/S/P 同一套显示约定
                    if (cell >= 11)
                    {
                        canvas.FontColor = Color.FromArgb("#C9C4DA");
                        canvas.FontSize = (float)(cell * 0.45);

                        canvas.DrawString(
                            "-",
                            x,
                            y,
                            cell,
                            cell,
                            HorizontalAlignment.Center,
                            VerticalAlignment.Center);
                    }

                    canvas.StrokeColor = Color.FromArgb("#E6E2F2");
                    canvas.StrokeSize = 0.6f;

                    canvas.DrawRectangle(x, y, cell, cell);

                    continue;
                }

                if (shape.Type != CellType.Empty)
                {
                    canvas.FillColor = ColorFor(shape);

                    canvas.FillRectangle(
                        x + cell * 0.08f,
                        y + cell * 0.08f,
                        cell * 0.84f,
                        cell * 0.84f);

                    // 格子够大时用字母标注：C 晶体 / S 形状 / P 顶针
                    if (cell >= 11)
                    {
                        canvas.FontColor = Colors.White;
                        canvas.FontSize = (float)(cell * 0.52);

                        canvas.DrawString(
                            LetterOf(shape),
                            x,
                            y,
                            cell,
                            cell,
                            HorizontalAlignment.Center,
                            VerticalAlignment.Center);
                    }
                }

                canvas.StrokeColor = Color.FromArgb("#E6E2F2");
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

    /// <summary>
    /// 颜色表示部件颜色，字母表示类型：S 形状 / C 晶体 / P 顶针。
    /// 没有颜色时用统一的灰色。
    /// </summary>
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

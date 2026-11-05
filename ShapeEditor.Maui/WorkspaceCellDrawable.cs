using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;
using GraphicsFont = Microsoft.Maui.Graphics.Font;

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

    public WorkspaceCellDrawable(
        ShapeDocument document)
    {
        _document = document;
    }

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        DrawDocument(
            canvas,
            _document,
            0,
            0,
            (float)WorkspaceLayout.CellSize);
    }

    /// <summary>
    /// 在画布指定位置按给定单元格边长画一张竖式图。
    ///
    /// 分析页的顶部预览（等比缩小）和制作路径节点（固定 30）都调用这里，
    /// 保证字母（c / S / P）、颜色与格线三处完全一致。
    /// </summary>
    public static void DrawDocument(
        ICanvas canvas,
        ShapeDocument document,
        float originX,
        float originY,
        float cell)
    {
        int layers = document.LayerCount;

        int parts = document.PartCount;

        if (layers <= 0 || parts <= 0 || cell <= 0)
        {
            return;
        }

        // 调用方可能刚画过粗体标题，预览里的字母统一用常规字重。
        canvas.Font = GraphicsFont.Default;

        // 缩小时线宽、内缩与字号一起按比例收，避免小尺寸预览糊成一片。
        float inset = Math.Max(0.5f, cell * 0.033f);

        float fontSize = Math.Clamp(cell * 0.53f, 4f, 16f);

        // 顶部预览可能缩得很小，格子不足 7 时字母会溢出，干脆只保留颜色。
        bool showLetter = cell >= 7f;

        float stroke = Math.Max(0.4f, cell * 0.02f);

        for (int visualRow = 0; visualRow < layers; visualRow++)
        {
            // 最高层画在最上面，和编辑器竖式图一致
            int layer = layers - 1 - visualRow;

            for (int part = 0; part < parts; part++)
            {
                float x = originX + part * cell;

                float y = originY + visualRow * cell;

                ShapeCell shape = document[layer, part];

                if (shape.Type != CellType.Empty)
                {
                    canvas.FillColor = ColorFor(shape);

                    canvas.FillRectangle(
                        x + inset,
                        y + inset,
                        cell - inset * 2,
                        cell - inset * 2);

                    canvas.FontColor = Colors.White;
                    canvas.FontSize = fontSize;

                    if (showLetter)
                    {
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

                canvas.StrokeColor = GridLine;
                canvas.StrokeSize = stroke;

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

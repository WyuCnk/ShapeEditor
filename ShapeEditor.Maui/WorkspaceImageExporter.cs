using SkiaSharp;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区路径图的离屏导出：直接用 SkiaSharp 画，不截屏。
///
/// 节点尺寸与画布一致（宽 = 象限数 × 30 + 16，高 = 50 + 层数 × 30），
/// 图片尺寸 = 所有节点包围盒 + 24px 边距，按 1 倍导出。
/// 连线不带标签，箭头在最后绘制。
/// </summary>
public sealed class WorkspaceImageExporter
{
    private const int Margin = 24;

    private const int CellSize = 30;

    private const int MaxImageSide = 16_000;

    private static readonly SKColor Accent = new(0x51, 0x2B, 0xD4);

    private static readonly SKColor BorderColor = new(0xD0, 0xD0, 0xD0);

    private static readonly SKColor ResultFill = new(0xFA, 0xF8, 0xFF);

    private static readonly SKColor Muted = new(0x6B, 0x6B, 0x76);

    private static readonly SKColor GridColor = new(0xE6, 0xE2, 0xF2);

    private static readonly SKColor EmptyCellFill = new(0x9E, 0x9E, 0x9E);

    public async Task<string> ExportAsync(
        IReadOnlyList<WorkspaceNodeBox> boxes,
        IReadOnlyList<WorkspaceEdge> edges,
        Func<WorkspaceNode, ShapeDocument?> documentOf)
    {
        if (boxes.Count == 0)
        {
            throw new InvalidOperationException("画布是空的。");
        }

        double left = boxes.Min(box => box.Bounds.X);
        double top = boxes.Min(box => box.Bounds.Y);
        double right = boxes.Max(box => box.Bounds.Right);
        double bottom = boxes.Max(box => box.Bounds.Bottom);

        int imageWidth;
        int imageHeight;

        try
        {
            imageWidth = checked((int)Math.Ceiling(right - left) + Margin * 2);
            imageHeight = checked((int)Math.Ceiling(bottom - top) + Margin * 2);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("图片尺寸过大，无法生成。");
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new InvalidOperationException("画布尺寸无效。");
        }

        if (imageWidth > MaxImageSide || imageHeight > MaxImageSide)
        {
            throw new InvalidOperationException(
                $"图片尺寸过大，当前最大边长为 {MaxImageSide} 像素。");
        }

        // 把包围盒左上角移到 24px 边距的位置
        float offsetX = (float)(Margin - left);
        float offsetY = (float)(Margin - top);

        SKTypeface typeface = await LoadTypefaceAsync();

        using var bitmap = new SKBitmap(
            imageWidth,
            imageHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        var lookup = boxes.ToDictionary(box => box.Node.Id);

        var links = new List<WorkspaceLink>();

        // 先画连线，节点会盖住线头，和画布层次一致
        using (var linePaint = new SKPaint
        {
            Color = Accent,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        })
        {
            foreach (WorkspaceEdge edge in edges)
            {
                if (!lookup.TryGetValue(edge.FromId, out WorkspaceNodeBox? from) ||
                    !lookup.TryGetValue(edge.ToId, out WorkspaceNodeBox? to))
                {
                    continue;
                }

                WorkspaceLink link = WorkspaceLinkGeometry.Create(
                    from.Bounds,
                    to.Bounds);

                links.Add(link);

                canvas.DrawLine(
                    link.X1 + offsetX,
                    link.Y1 + offsetY,
                    link.X2 + offsetX,
                    link.Y2 + offsetY,
                    linePaint);
            }
        }

        foreach (WorkspaceNodeBox box in boxes)
        {
            DrawNode(
                canvas,
                box,
                documentOf(box.Node),
                offsetX,
                offsetY,
                typeface);
        }

        // 箭头最后绘制，保证压在连线与节点之上
        using (var arrowPaint = new SKPaint
        {
            Color = Accent,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            foreach (WorkspaceLink link in links)
            {
                DrawArrow(canvas, link, offsetX, offsetY, arrowPaint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);

        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        string path = Path.Combine(
            FileSystem.CacheDirectory,
            $"shape-path-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using FileStream stream = File.Create(path);

        data.SaveTo(stream);

        return path;
    }

    #region 节点

    private static void DrawNode(
        SKCanvas canvas,
        WorkspaceNodeBox box,
        ShapeDocument? document,
        float offsetX,
        float offsetY,
        SKTypeface typeface)
    {
        WorkspaceNode node = box.Node;

        float x = (float)box.Bounds.X + offsetX;
        float y = (float)box.Bounds.Y + offsetY;
        float width = (float)box.Bounds.Width;
        float height = (float)box.Bounds.Height;

        var rect = new SKRect(x, y, x + width, y + height);

        using (var fill = new SKPaint
        {
            Color = node.IsResult ? ResultFill : SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        })
        {
            canvas.DrawRect(rect, fill);
        }

        using (var border = new SKPaint
        {
            Color = BorderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        })
        {
            canvas.DrawRect(rect, border);
        }

        canvas.Save();

        canvas.ClipRect(rect);

        using var textFont = new SKFont(typeface, 10f);

        using var textPaint = new SKPaint
        {
            Color = Muted,
            IsAntialias = true
        };

        DrawText(
            canvas,
            $"#{node.Seq} {WorkspaceLayout.TitleOf(node)}",
            x + (float)WorkspaceLayout.HorizontalPadding,
            y + 18,
            textFont,
            textPaint);

        int layers = WorkspaceLayout.LayersOf(node, document);
        int parts = WorkspaceLayout.PartsOf(node, document);

        if (layers > 0 && parts > 0)
        {
            DrawPreview(
                canvas,
                document,
                x + (float)WorkspaceLayout.HorizontalPadding,
                y + (float)WorkspaceLayout.HeaderHeight,
                layers,
                parts,
                typeface);
        }

        string summary = document is null
            ? "空"
            : ShapeText.Summary(document);

        DrawText(
            canvas,
            summary,
            x + (float)WorkspaceLayout.HorizontalPadding,
            y + height - 8,
            textFont,
            textPaint);

        canvas.Restore();
    }

    private static void DrawPreview(
        SKCanvas canvas,
        ShapeDocument? document,
        float left,
        float top,
        int layers,
        int parts,
        SKTypeface typeface)
    {
        using var cellFill = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        using var letterFont = new SKFont(typeface, 16f);

        using var letterPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        for (int visualRow = 0; visualRow < layers; visualRow++)
        {
            int layer = layers - 1 - visualRow;

            for (int part = 0; part < parts; part++)
            {
                if (document is null)
                {
                    break;
                }

                ShapeCell shape = document[layer, part];

                if (shape.Type == CellType.Empty)
                {
                    continue;
                }

                float cellLeft = left + part * CellSize;
                float cellTop = top + visualRow * CellSize;

                cellFill.Color = ColorOf(shape);

                canvas.DrawRect(
                    new SKRect(
                        cellLeft + 1,
                        cellTop + 1,
                        cellLeft + CellSize - 1,
                        cellTop + CellSize - 1),
                    cellFill);

                DrawCenteredLetter(
                    canvas,
                    LetterOf(shape),
                    cellLeft + CellSize / 2f,
                    cellTop + CellSize / 2f,
                    letterFont,
                    letterPaint);
            }
        }

        using var gridPaint = new SKPaint
        {
            Color = GridColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.6f,
            IsAntialias = false
        };

        float gridWidth = parts * CellSize;
        float gridHeight = layers * CellSize;

        for (int column = 0; column <= parts; column++)
        {
            float gridX = left + column * CellSize;

            canvas.DrawLine(gridX, top, gridX, top + gridHeight, gridPaint);
        }

        for (int row = 0; row <= layers; row++)
        {
            float gridY = top + row * CellSize;

            canvas.DrawLine(left, gridY, left + gridWidth, gridY, gridPaint);
        }
    }

    #endregion

    #region 箭头

    private static void DrawArrow(
        SKCanvas canvas,
        WorkspaceLink link,
        float offsetX,
        float offsetY,
        SKPaint paint)
    {
        float x1 = link.X1 + offsetX;
        float y1 = link.Y1 + offsetY;
        float x2 = link.X2 + offsetX;
        float y2 = link.Y2 + offsetY;

        float angle = (float)Math.Atan2(y2 - y1, x2 - x1);

        float cos = (float)Math.Cos(angle);
        float sin = (float)Math.Sin(angle);

        const float length = 10;
        const float halfWidth = 5.5f;

        float baseX = x2 - length * cos;
        float baseY = y2 - length * sin;

        using var builder = new SKPathBuilder();

        builder.MoveTo(x2, y2);

        builder.LineTo(
            baseX + halfWidth * sin,
            baseY - halfWidth * cos);

        builder.LineTo(
            baseX - halfWidth * sin,
            baseY + halfWidth * cos);

        builder.Close();

        using SKPath path = builder.Detach();

        canvas.DrawPath(path, paint);
    }

    #endregion

    #region 文本与颜色

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKFont font,
        SKPaint paint)
    {
        using SKTextBlob? blob = SKTextBlob.Create(text, font);

        if (blob is not null)
        {
            canvas.DrawText(blob, x, baseline, paint);
        }
    }

    private static void DrawCenteredLetter(
        SKCanvas canvas,
        string letter,
        float centerX,
        float centerY,
        SKFont font,
        SKPaint paint)
    {
        float textWidth = font.MeasureText(letter, paint);

        SKFontMetrics metrics = font.Metrics;

        float baseline = centerY - (metrics.Ascent + metrics.Descent) / 2f;

        DrawText(
            canvas,
            letter,
            centerX - textWidth / 2f,
            baseline,
            font,
            paint);
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

    private static SKColor ColorOf(
        ShapeCell shape)
    {
        uint argb = shape.HasColor && shape.ColorCode is char code
            ? ShapePalette.FromCode(code)
            : 0xFF9E9E9E;

        return ToSKColor(argb);
    }

    private static SKColor ToSKColor(
        uint argb)
    {
        return new SKColor(
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb,
            (byte)(argb >> 24));
    }

    private static async Task<SKTypeface> LoadTypefaceAsync()
    {
        await using Stream stream =
            await FileSystem.OpenAppPackageFileAsync("NotoSansSC-Regular.ttf");

        SKTypeface? typeface = SKTypeface.FromStream(stream);

        return typeface
            ?? throw new InvalidOperationException("无法加载中文字体。");
    }

    #endregion
}

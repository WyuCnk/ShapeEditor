using SkiaSharp;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Maui;

public sealed class ShapeImageExporter
{
    #region 常量

    private const int CellSize = 128;
    private const int GridLineWidth = 2;
    private const int OuterPadding = 24;

    private const int TitleHeight = 54;
    private const int CodeLineHeight = 44;
    private const int BottomPadding = 24;

    private const int MaxImageSide = 16_000;

    #endregion

    #region 公共方法

    /// <summary>
    /// 将当前竖式图导出为 PNG。
    ///
    /// 图形矩阵从最高层绘制到最低层，
    /// 也就是与屏幕上的竖式图顺序一致。
    /// </summary>
    public async Task<string> ExportPng(
        ShapeDocument document,
        string shortCode,
        bool includeCode = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        SKTypeface chineseTypeface = await LoadChineseTypefaceAsync();
        int columns = document.PartCount;
        int rows = document.LayerCount;

        long cellCount =
            (long)rows * columns;

        if (cellCount > 10_000)
        {
            throw new InvalidOperationException(
                "图形过大，暂不支持导出图片。" +
                "请减少层数或象限数后再试。");
        }

        if (columns <= 0 || rows <= 0)
        {
            throw new InvalidOperationException(
                "图形的层数和象限数必须大于 0。");
        }

        int gridWidth;
        int gridHeight;
        int imageWidth;
        int imageHeight;

        try
        {
            gridWidth = checked(
                columns * CellSize +
                (columns + 1) * GridLineWidth);

            gridHeight = checked(
                rows * CellSize +
                (rows + 1) * GridLineWidth);

            imageWidth = checked(
                gridWidth +
                OuterPadding * 2);

            imageHeight = checked(
                gridHeight +
                OuterPadding * 2 +
                TitleHeight +
                (includeCode
                    ? CodeLineHeight
                    : 0) +
                BottomPadding);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(
                "图片尺寸过大，无法生成。");
        }

        if (imageWidth > MaxImageSide ||
            imageHeight > MaxImageSide)
        {
            throw new InvalidOperationException(
                $"图片尺寸过大，当前最大边长为 {MaxImageSide} 像素。");
        }

        using var bitmap = new SKBitmap(
            imageWidth,
            imageHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        using var titlePaint =
    CreateTextPaint(SKColors.Black);

        using var titleFont =
            CreateTextFont(
                chineseTypeface,
                30);

        using var codePaint =
            CreateTextPaint(SKColors.DarkSlateGray);

        using var codeFont =
            CreateTextFont(
                chineseTypeface,
                22);

        float titleX = OuterPadding;
        float titleY = OuterPadding + 34;

        DrawText(
            canvas,
            $"{rows}层 × {columns}象限",
            titleX,
            titleY,
            titleFont,
            titlePaint);

        int gridStartY =
            OuterPadding +
            TitleHeight +
            (includeCode ? CodeLineHeight : 0);

        if (includeCode)
        {
            DrawFittedText(
                canvas,
                shortCode,
                OuterPadding,
                OuterPadding + TitleHeight + 27,
                imageWidth - OuterPadding * 2,
                codeFont,
                codePaint);
        }

        DrawGrid(
            canvas,
            document,
            OuterPadding,
            gridStartY,
            chineseTypeface);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        string fileName =
            $"shape-{DateTime.Now:yyyyMMdd-HHmmss}.png";

        string filePath =
            Path.Combine(
                FileSystem.CacheDirectory,
                fileName);

        using FileStream stream = File.Open(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        data.SaveTo(stream);

        return filePath;
    }

    #endregion

    #region 矩阵绘制

    private static void DrawGrid(
        SKCanvas canvas,
        ShapeDocument document,
        int startX,
        int startY,
        SKTypeface typeface)
    {
        int rows = document.LayerCount;
        int columns = document.PartCount;

        for (int visualRow = 0;
             visualRow < rows;
             visualRow++)
        {
            int layer =
                rows - 1 - visualRow;

            for (int part = 0;
                 part < columns;
                 part++)
            {
                int x =
                    startX +
                    GridLineWidth +
                    part * CellSize +
                    part * GridLineWidth;

                int y =
                    startY +
                    GridLineWidth +
                    visualRow * CellSize +
                    visualRow * GridLineWidth;

                var cellRect = new SKRect(
                    x,
                    y,
                    x + CellSize,
                    y + CellSize);

                DrawCell(
                    canvas,
                    document[layer, part],
                    cellRect,
                    typeface);
            }
        }

        DrawGridLines(
            canvas,
            startX,
            startY,
            rows,
            columns);
    }

    private static void DrawGridLines(
        SKCanvas canvas,
        int startX,
        int startY,
        int rows,
        int columns)
    {
        int totalWidth =
            columns * CellSize +
            (columns + 1) * GridLineWidth;

        int totalHeight =
            rows * CellSize +
            (rows + 1) * GridLineWidth;

        using var paint = new SKPaint
        {
            Color = SKColors.Gray,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = GridLineWidth,
            IsAntialias = false
        };

        for (int column = 0;
             column <= columns;
             column++)
        {
            float x =
                startX +
                GridLineWidth / 2f +
                column * (CellSize + GridLineWidth);

            canvas.DrawLine(
                x,
                startY,
                x,
                startY + totalHeight,
                paint);
        }

        for (int row = 0;
             row <= rows;
             row++)
        {
            float y =
                startY +
                GridLineWidth / 2f +
                row * (CellSize + GridLineWidth);

            canvas.DrawLine(
                startX,
                y,
                startX + totalWidth,
                y,
                paint);
        }
    }

    #endregion

    #region 单元格绘制

    private static void DrawCell(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect,
        SKTypeface typeface)
    {
        switch (cell.Type)
        {
            case CellType.Empty:
                DrawEmptyCell(
                    canvas,
                    rect);

                break;

            case CellType.Pin:
                DrawPinCell(
                    canvas,
                    rect);

                break;

            case CellType.Crystal:
                DrawTextCell(
                    canvas,
                    cell,
                    rect,
                    "晶",
                    typeface);

                break;

            case CellType.Shape:
                DrawTextCell(
                    canvas,
                    cell,
                    rect,
                    "图",
                    typeface);

                break;
        }
    }

    private static void DrawEmptyCell(
        SKCanvas canvas,
        SKRect rect)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            rect,
            paint);
    }

    /// <summary>
    /// 顶针：白色 | 黑色 | 白色。
    /// </summary>
    private static void DrawPinCell(
        SKCanvas canvas,
        SKRect rect)
    {
        using var whitePaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            rect,
            whitePaint);

        float thirdWidth =
            rect.Width / 3f;

        var middleRect = new SKRect(
            rect.Left + thirdWidth,
            rect.Top,
            rect.Left + thirdWidth * 2f,
            rect.Bottom);

        using var blackPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            middleRect,
            blackPaint);
    }

    private static void DrawTextCell(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect,
        string text,
        SKTypeface typeface)
    {
        char colorCode =
            cell.ColorCode ?? 'u';

        canvas.Save();

        /*
         * 晶体使用缓存的 7 段色带；
         * 普通形状使用纯色背景。
         */
        if (cell.Type == CellType.Crystal)
        {
            DrawCrystalBands(
                canvas,
                cell,
                rect);
        }
        else
        {
            using var backgroundPaint = new SKPaint
            {
                Color = GetColor(colorCode),
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };

            canvas.DrawRect(
                rect,
                backgroundPaint);
        }

        /*
         * 普通形状和晶体统一使用浅色边框，
         * 与当前 ShapeGridDrawable 保持一致。
         */
        DrawCrystalBorder(
            canvas,
            rect,
            colorCode);

        SKColor textColor =
            colorCode == 'k'
                ? SKColors.White
                : SKColors.Black;

        using var textPaint =
            CreateTextPaint(textColor);

        using var textFont =
            CreateTextFont(
                typeface,
                52);

        float textWidth =
            textFont.MeasureText(
                text,
                textPaint);

        SKFontMetrics metrics =
            textFont.Metrics;

        float textX =
            rect.MidX -
            textWidth / 2f;

        float textY =
            rect.MidY -
            (metrics.Ascent + metrics.Descent) / 2f;

        DrawText(
            canvas,
            text,
            textX,
            textY,
            textFont,
            textPaint);

        canvas.Restore();
    }

    private static void DrawCrystalBands(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect)
    {
        char colorCode =
            cell.ColorCode ?? 'r';

        if (!CrystalGradientCache.TryGetValue(
                colorCode,
                out SKColor[]? colors))
        {
            colors =
                CrystalGradientCache['r'];
        }

        canvas.Save();

        canvas.ClipRect(rect);

        float bandWidth =
            rect.Width / colors.Length;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        for (int index = 0;
             index < colors.Length;
             index++)
        {
            float x =
                rect.Left +
                index * bandWidth;

            float width =
                index == colors.Length - 1
                    ? rect.Right - x
                    : bandWidth;

            paint.Color =
                colors[index];

            canvas.DrawRect(
                new SKRect(
                    x,
                    rect.Top,
                    x + width,
                    rect.Bottom),
                paint);
        }

        canvas.Restore();
    }

    private static void DrawCrystalBorder(
        SKCanvas canvas,
        SKRect rect,
        char colorCode)
    {
        SKColor borderColor =
            colorCode == 'w'
                ? SKColors.White
                : new SKColor(
                    255,
                    255,
                    255,
                    184);

        float inset =
            Math.Max(
                1f,
                rect.Width * 0.035f);

        using var borderPaint = new SKPaint
        {
            Color = borderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(
                1f,
                rect.Width * 0.025f),
            IsAntialias = true
        };

        canvas.DrawRect(
            new SKRect(
                rect.Left + inset,
                rect.Top + inset,
                rect.Right - inset,
                rect.Bottom - inset),
            borderPaint);
    }

    #endregion

    #region 画笔和颜色

    private static async Task<SKTypeface>
    LoadChineseTypefaceAsync()
    {
        await using Stream stream =
            await FileSystem.OpenAppPackageFileAsync(
                "NotoSansSC-Regular.ttf");

        SKTypeface? typeface =
            SKTypeface.FromStream(stream);

        return typeface
            ?? throw new InvalidOperationException(
                "无法加载中文字体。");
    }

    /// <summary>
    /// 每种颜色对应一组 7 段晶体色带。
    ///
    /// 顺序：
    /// 暗色 → 基础色 → 过渡色 → 高光
    ///       → 过渡色 → 基础色 → 暗色
    /// </summary>
    private static readonly Dictionary<
        char,
        SKColor[]> CrystalGradientCache =
        CreateCrystalGradientCache();

    private static Dictionary<
        char,
        SKColor[]> CreateCrystalGradientCache()
    {
        char[] colorCodes =
        {
        'r',
        'g',
        'b',
        'c',
        'm',
        'y',
        'w',
        'k',
        'u'
    };

        var result =
            new Dictionary<
                char,
                SKColor[]>();

        foreach (char colorCode in colorCodes)
        {
            SKColor baseColor =
                GetColor(colorCode);

            result[colorCode] =
                CreateCrystalGradientColors(
                    baseColor,
                    colorCode);
        }

        return result;
    }

    private static SKColor[] CreateCrystalGradientColors(
        SKColor baseColor,
        char colorCode)
    {
        float edgeFactor =
            colorCode == 'k'
                ? 0.72f
                : 0.82f;

        float shoulderAmount =
            colorCode == 'k'
                ? 0.16f
                : 0.12f;

        float highlightAmount =
            colorCode == 'k'
                ? 0.42f
                : 0.34f;

        SKColor edgeColor =
            DarkenColor(
                baseColor,
                edgeFactor);

        SKColor shoulderColor =
            LightenColor(
                baseColor,
                shoulderAmount);

        SKColor highlightColor =
            LightenColor(
                baseColor,
                highlightAmount);

        return new[]
        {
        edgeColor,
        baseColor,
        shoulderColor,
        highlightColor,
        shoulderColor,
        baseColor,
        edgeColor
    };
    }

    private static SKColor DarkenColor(
        SKColor color,
        float factor)
    {
        return new SKColor(
            (byte)(color.Red * factor),
            (byte)(color.Green * factor),
            (byte)(color.Blue * factor),
            color.Alpha);
    }

    private static SKColor LightenColor(
        SKColor color,
        float amount)
    {
        return new SKColor(
            (byte)(
                color.Red +
                (255 - color.Red) * amount),

            (byte)(
                color.Green +
                (255 - color.Green) * amount),

            (byte)(
                color.Blue +
                (255 - color.Blue) * amount),

            color.Alpha);
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        SKFont font,
        SKPaint paint)
    {
        using SKTextBlob? blob =
            SKTextBlob.Create(
                text,
                font);

        if (blob is not null)
        {
            canvas.DrawText(
                blob,
                x,
                y,
                paint);
        }
    }

    private async static void DrawFittedText(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        float maxWidth,
        SKFont font,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SKTypeface chineseTypeface = await LoadChineseTypefaceAsync();

        float textWidth =
            font.MeasureText(
                text,
                paint);

        if (textWidth <= maxWidth)
        {
            DrawText(
                canvas,
                text,
                x,
                baseline,
                font,
                paint);

            return;
        }

        float newSize =
            font.Size;

        while (newSize > 10)
        {
            newSize -= 1;

            using var smallerFont =
                CreateTextFont(
                    chineseTypeface,
                    newSize);

            if (smallerFont.MeasureText(
                    text,
                    paint) <= maxWidth)
            {
                DrawText(
                    canvas,
                    text,
                    x,
                    baseline,
                    smallerFont,
                    paint);

                return;
            }
        }

        using var minimumFont =
            CreateTextFont(
                chineseTypeface,
                10);

        DrawText(
            canvas,
            text,
            x,
            baseline,
            minimumFont,
            paint);
    }

    private static SKPaint CreateTextPaint(
        SKColor color)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true
        };
    }

    private static SKFont CreateTextFont(
    SKTypeface typeface,
    float textSize)
    {
        return new SKFont(
            typeface,
            textSize);
    }

    private static SKColor GetColor(
        char code)
    {
        return code switch
        {
            'r' => SKColor.Parse("#FF3864"),
            'g' => SKColor.Parse("#83FF38"),
            'b' => SKColor.Parse("#65B7FF"),
            'c' => SKColor.Parse("#35E6C1"),
            'm' => SKColor.Parse("#D85CFF"),
            'y' => SKColor.Parse("#FFB83E"),

            // 与屏幕版 ShapeGridDrawable 保持一致
            'w' => SKColor.Parse("#D8D8D8"),

            'k' => SKColor.Parse("#35353C"),
            'u' => SKColor.Parse("#A9BAC8"),
            _ => SKColors.White
        };
    }

    #endregion
}
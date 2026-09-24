using Microsoft.Maui.Graphics;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Maui;

public sealed class ShapeGridDrawable : IDrawable
{
    #region 内部类型

    /// <summary>
    /// 当前矩阵在画布中的布局信息。
    /// </summary>
    private readonly record struct GridLayout(
        float CellSize,
        float GridWidth,
        float GridHeight,
        float StartX,
        float StartY);

    #endregion

    #region 字段和状态

    private ShapeDocument? _document;

    private int? _selectedLayer;
    private int? _selectedPart;

    private float _zoom = 1f;
    private float _panX;
    private float _panY;

    #endregion

    #region 公共属性

    public float Zoom => _zoom;

    public float PanX => _panX;

    public float PanY => _panY;

    #endregion

    #region 文档和选中状态

    /// <summary>
    /// 设置当前要绘制的图形文档。
    /// </summary>
    public void SetDocument(
        ShapeDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// 设置当前选中的单元格。
    ///
    /// layer 0 是最低层。
    /// part 0 是该层的第一个象限。
    /// </summary>
    public void SetSelectedCell(
        int? layer,
        int? part)
    {
        _selectedLayer = layer;
        _selectedPart = part;
    }

    #endregion

    #region 缩放和平移

    /// <summary>
    /// 将视图恢复到默认状态。
    /// </summary>
    public void ResetView()
    {
        _zoom = 1f;
        _panX = 0;
        _panY = 0;
    }

    /// <summary>
    /// 应用缩放和平移。
    /// </summary>
    public void ApplyTransform(
        float zoom,
        float panX,
        float panY)
    {
        _zoom = Math.Clamp(
            zoom,
            0.35f,
            5f);

        _panX = panX;
        _panY = panY;
    }

    #endregion

    #region 主绘制

    public void Draw(
        ICanvas canvas,
        RectF dirtyRect)
    {
        // 先清空画布背景
        bool isDark =
    Application.Current?.RequestedTheme ==
    AppTheme.Dark;

        canvas.FillColor =
            isDark
                ? Color.FromArgb("#121212")
                : Colors.White;

        canvas.FillRectangle(
            dirtyRect);

        if (_document is null)
        {
            return;
        }

        int layers = _document.LayerCount;
        int parts = _document.PartCount;

        if (layers <= 0 || parts <= 0)
        {
            return;
        }

        GridLayout layout =
            CalculateLayout(
                dirtyRect.Width,
                dirtyRect.Height,
                layers,
                parts);

        DrawCells(
            canvas,
            layout,
            layers,
            parts);

        DrawGridLines(
            canvas,
            layout,
            layers,
            parts);

        DrawSelectedCell(
            canvas,
            layout,
            layers,
            parts);
    }

    #endregion

    #region 单元格绘制

    private void DrawCells(
        ICanvas canvas,
        GridLayout layout,
        int layers,
        int parts)
    {
        /*
         * 视觉行从上到下排列。
         *
         * layer 0 是最低层，
         * 所以最高层位于最上方，
         * layer 0 位于最下方。
         */
        for (int visualRow = 0;
             visualRow < layers;
             visualRow++)
        {
            int layer =
                layers - 1 - visualRow;

            for (int part = 0;
                 part < parts;
                 part++)
            {
                float x =
                    layout.StartX +
                    part * layout.CellSize;

                float y =
                    layout.StartY +
                    visualRow * layout.CellSize;

                var cellRect = new RectF(
                    x,
                    y,
                    layout.CellSize,
                    layout.CellSize);

                DrawCell(
                    canvas,
                    _document![layer, part],
                    cellRect);
            }
        }
    }

    private void DrawGridLines(
        ICanvas canvas,
        GridLayout layout,
        int layers,
        int parts)
    {
        canvas.StrokeColor =
            Color.FromArgb("#9E9E9E");

        canvas.StrokeSize = 1;

        // 竖线
        for (int column = 0;
             column <= parts;
             column++)
        {
            float x =
                layout.StartX +
                column * layout.CellSize;

            canvas.DrawLine(
                x,
                layout.StartY,
                x,
                layout.StartY + layout.GridHeight);
        }

        // 横线
        for (int row = 0;
             row <= layers;
             row++)
        {
            float y =
                layout.StartY +
                row * layout.CellSize;

            canvas.DrawLine(
                layout.StartX,
                y,
                layout.StartX + layout.GridWidth,
                y);
        }
    }

    private void DrawSelectedCell(
        ICanvas canvas,
        GridLayout layout,
        int layers,
        int parts)
    {
        if (_selectedLayer is not int selectedLayer ||
            _selectedPart is not int selectedPart)
        {
            return;
        }

        if (selectedLayer < 0 ||
            selectedLayer >= layers ||
            selectedPart < 0 ||
            selectedPart >= parts)
        {
            return;
        }

        float selectedX =
            layout.StartX +
            selectedPart * layout.CellSize;

        int selectedVisualRow =
            layers - 1 - selectedLayer;

        float selectedY =
            layout.StartY +
            selectedVisualRow * layout.CellSize;

        float borderWidth =
            Math.Min(
                3f,
                layout.CellSize / 4f);

        if (borderWidth <= 0)
        {
            return;
        }

        canvas.StrokeColor =
            Color.FromArgb("#1976D2");

        canvas.StrokeSize = borderWidth;

        canvas.DrawRectangle(
            selectedX + borderWidth / 2,
            selectedY + borderWidth / 2,
            layout.CellSize - borderWidth,
            layout.CellSize - borderWidth);
    }

    private static void DrawCell(
        ICanvas canvas,
        ShapeCell cell,
        RectF rect)
    {
        switch (cell.Type)
        {
            case CellType.Empty:
                DrawEmpty(
                    canvas,
                    rect);

                break;

            case CellType.Pin:
                DrawPin(
                    canvas,
                    rect);

                break;

            case CellType.Crystal:
                DrawTextCell(
                    canvas,
                    cell,
                    rect,
                    "晶");

                break;

            case CellType.Shape:
                DrawTextCell(
                    canvas,
                    cell,
                    rect,
                    "图");

                break;
        }
    }

    private static void DrawEmpty(
    ICanvas canvas,
    RectF rect)
    {
        canvas.SaveState();

        bool isDark =
    Application.Current?.RequestedTheme ==
    AppTheme.Dark;

        canvas.FillColor =
            isDark
                ? Color.FromArgb("#121212")
                : Colors.White;

        canvas.FillRectangle(
            rect);

        canvas.RestoreState();
    }

    /// <summary>
    /// 绘制顶针。
    ///
    /// 单元格从左到右均分为三列：
    ///
    /// 白色 | 黑色 | 白色
    /// </summary>
    private static void DrawPin(
    ICanvas canvas,
    RectF rect)
    {
        canvas.SaveState();

        bool isDark =
    Application.Current?.RequestedTheme ==
    AppTheme.Dark;

        canvas.FillColor =
            isDark
                ? Color.FromArgb("#121212")
                : Colors.White;

        canvas.FillRectangle(
            rect);

        float thirdWidth =
            rect.Width / 3f;

        var middleRect = new RectF(
            rect.X + thirdWidth,
            rect.Y,
            thirdWidth,
            rect.Height);

        canvas.FillColor = Colors.Black;
        canvas.FillRectangle(middleRect);

        canvas.RestoreState();
    }

    private static void DrawTextCell(
    ICanvas canvas,
    ShapeCell cell,
    RectF rect,
    string text)
    {
        char colorCode =
            cell.ColorCode ?? 'u';

        canvas.SaveState();

        if (cell.Type == CellType.Crystal)
        {
            /*
             * 晶体使用缓存的 7 段色带，
             * 不再创建 LinearGradientPaint。
             */
            DrawCrystalGradient(
                canvas,
                cell,
                rect);
        }
        else
        {
            /*
             * 普通形状使用纯色背景。
             */
            canvas.FillColor =
                GetColor(colorCode);

            canvas.FillRectangle(rect);
        }

        /*
         * 普通形状也暂时使用晶体的浅色边框，
         * 对比黑色边框更加柔和。
         */
        if (cell.Type is CellType.Shape
            or CellType.Crystal)
        {
            DrawCrystalBorder(
                canvas,
                rect,
                colorCode);
        }

        canvas.FontColor =
            colorCode == 'k'
                ? Colors.White
                : Colors.Black;

        canvas.FontSize =
            Math.Max(
                12f,
                rect.Height * 0.42f);

        canvas.Font =
            Microsoft.Maui.Graphics.Font.DefaultBold;

        canvas.DrawString(
            text,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            HorizontalAlignment.Center,
            VerticalAlignment.Center,
            TextFlow.ClipBounds);

        canvas.RestoreState();
    }

    private static void DrawShapeBorder(
    ICanvas canvas,
    RectF rect)
    {
        float inset =
            Math.Max(
                1f,
                rect.Width * 0.05f);

        canvas.StrokeColor =
            Color.FromArgb("#202020");

        canvas.StrokeSize =
            Math.Max(
                1.5f,
                rect.Width * 0.035f);

        canvas.DrawRectangle(
            rect.X + inset,
            rect.Y + inset,
            rect.Width - inset * 2f,
            rect.Height - inset * 2f);
    }

    private static void DrawCrystalBorder(
    ICanvas canvas,
    RectF rect,
    char colorCode)
    {
        /*
         * 白色晶体使用更亮的边框；
         * 其他颜色晶体使用半透明白色边框。
         */
        Color borderColor =
            colorCode == 'w'
                ? Color.FromArgb("#FFFFFF")
                : Color.FromRgba(
                    1f,
                    1f,
                    1f,
                    0.72f);

        float inset =
            Math.Max(
                1f,
                rect.Width * 0.035f);

        canvas.StrokeColor =
            borderColor;

        canvas.StrokeSize =
            Math.Max(
                1f,
                rect.Width * 0.025f);

        canvas.DrawRectangle(
            rect.X + inset,
            rect.Y + inset,
            rect.Width - inset * 2f,
            rect.Height - inset * 2f);
    }

    #endregion

    #region 命中检测

    /// <summary>
    /// 根据触摸点获取对应的 layer 和 part。
    ///
    /// 屏幕最上方是最高层，
    /// 屏幕最下方是 layer 0。
    /// </summary>
    public bool TryHitTest(
        PointF point,
        float width,
        float height,
        out int layer,
        out int part)
    {
        layer = -1;
        part = -1;

        if (_document is null)
        {
            return false;
        }

        int layers = _document.LayerCount;
        int parts = _document.PartCount;

        if (layers <= 0 || parts <= 0)
        {
            return false;
        }

        GridLayout layout =
            CalculateLayout(
                width,
                height,
                layers,
                parts);

        bool insideGrid =
            point.X >= layout.StartX &&
            point.X < layout.StartX + layout.GridWidth &&
            point.Y >= layout.StartY &&
            point.Y < layout.StartY + layout.GridHeight;

        if (!insideGrid)
        {
            return false;
        }

        int visualColumn =
            (int)(
                (point.X - layout.StartX) /
                layout.CellSize);

        int visualRow =
            (int)(
                (point.Y - layout.StartY) /
                layout.CellSize);

        if (visualColumn < 0 ||
            visualColumn >= parts ||
            visualRow < 0 ||
            visualRow >= layers)
        {
            return false;
        }

        part = visualColumn;

        // 视觉行需要翻转为数据层号
        layer =
            layers - 1 - visualRow;

        return true;
    }

    #endregion

    #region 几何计算

    /// <summary>
    /// 计算矩阵在画布中的位置和尺寸。
    ///
    /// 绘制和命中检测共用此方法，
    /// 确保缩放和平移后两者位置完全一致。
    /// </summary>
    private GridLayout CalculateLayout(
        float width,
        float height,
        int layers,
        int parts)
    {
        if (width <= 0 ||
            height <= 0 ||
            layers <= 0 ||
            parts <= 0)
        {
            return new GridLayout(
                0,
                0,
                0,
                0,
                0);
        }

        float baseCellWidth =
            width / parts;

        float baseCellHeight =
            height / layers;

        float baseCellSize =
            Math.Min(
                baseCellWidth,
                baseCellHeight);

        float cellSize =
            baseCellSize * _zoom;

        float gridWidth =
            cellSize * parts;

        float gridHeight =
            cellSize * layers;

        float startX =
            (width - gridWidth) / 2f +
            _panX;

        float startY =
            (height - gridHeight) / 2f +
            _panY;

        return new GridLayout(
            cellSize,
            gridWidth,
            gridHeight,
            startX,
            startY);
    }

    #endregion

    #region 颜色

    /*
     * 每种颜色对应一组从左到右的晶体渐变色。
     *
     * 例如红色晶体：
     *
     * 深红 → 红 → 亮红 → 高光 → 亮红 → 红 → 深红
     *
     * 这些颜色只在程序启动时计算一次。
     * 绘制单元格时直接读取，不再创建 LinearGradientPaint。
     */
    private static readonly Dictionary<
        char,
        Color[]> CrystalGradientCache =
        CreateCrystalGradientCache();

    private static Dictionary<
        char,
        Color[]> CreateCrystalGradientCache()
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
                Color[]>();

        foreach (char colorCode in colorCodes)
        {
            Color baseColor =
                GetColor(colorCode);

            result[colorCode] =
                CreateCrystalGradientColors(
                    baseColor,
                    colorCode);
        }

        return result;
    }

    private static Color[] CreateCrystalGradientColors(
        Color baseColor,
        char colorCode)
    {
        /*
         * 黑色晶体适当提高中心高光，
         * 否则黑色背景上的玻璃效果不明显。
         */
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

        Color edgeColor =
            DarkenColor(
                baseColor,
                edgeFactor);

        Color shoulderColor =
            LightenColor(
                baseColor,
                shoulderAmount);

        Color highlightColor =
            LightenColor(
                baseColor,
                highlightAmount);

        /*
         * 7 段色带。
         *
         * 从左到右：
         * 边缘暗色
         * 基础色
         * 过渡色
         * 中央高光
         * 过渡色
         * 基础色
         * 边缘暗色
         */
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

    private static void DrawCrystalGradient(
    ICanvas canvas,
    ShapeCell cell,
    RectF rect)
    {
        char colorCode =
            cell.ColorCode ?? 'r';

        if (!CrystalGradientCache.TryGetValue(
                colorCode,
                out Color[]? colors))
        {
            colors =
                CrystalGradientCache['r'];
        }

        canvas.SaveState();

        /*
         * 限制色带只能绘制在当前单元格内。
         */
        canvas.ClipRectangle(rect);

        float bandWidth =
            rect.Width / colors.Length;

        /*
         * 用 7 个普通色块模拟：
         *
         * 暗 → 基础 → 过渡 → 亮 → 过渡 → 基础 → 暗
         *
         * 不使用 LinearGradientPaint，
         * 避免 Android 模拟器图形后端卡死。
         */
        for (int index = 0;
             index < colors.Length;
             index++)
        {
            float x =
                rect.X + index * bandWidth;

            float width =
                index == colors.Length - 1
                    ? rect.Right - x
                    : bandWidth;

            canvas.FillColor =
                colors[index];

            canvas.FillRectangle(
                new RectF(
                    x,
                    rect.Y,
                    width,
                    rect.Height));
        }

        canvas.RestoreState();
    }

    private static Color DarkenColor(
        Color color,
        float factor)
    {
        return Color.FromRgba(
            color.Red * factor,
            color.Green * factor,
            color.Blue * factor,
            color.Alpha);
    }

    private static Color LightenColor(
        Color color,
        float amount)
    {
        return Color.FromRgba(
            color.Red +
                (1f - color.Red) * amount,

            color.Green +
                (1f - color.Green) * amount,

            color.Blue +
                (1f - color.Blue) * amount,

            color.Alpha);
    }

    private static Color GetColor(
        char code)
    {
        return code switch
        {
            'r' => Color.FromArgb("#FF3864"),
            'g' => Color.FromArgb("#83FF38"),
            'b' => Color.FromArgb("#65B7FF"),
            'c' => Color.FromArgb("#35E6C1"),
            'm' => Color.FromArgb("#D85CFF"),
            'y' => Color.FromArgb("#FFB83E"),
            'w' => Color.FromArgb("#D8D8D8"),
            'k' => Color.FromArgb("#35353C"),
            'u' => Color.FromArgb("#A9BAC8"),
            _ => Colors.White
        };
    }

    #endregion
}
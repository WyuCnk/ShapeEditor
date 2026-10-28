using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 画刷、画笔与字体的缓存。
///
/// 所有 Freezable 都冻结，避免 WPF 在渲染线程上做多余的保护性拷贝。
/// </summary>
public sealed class ShapeTheme
{
    public static ShapeTheme Instance { get; } = new();

    private static readonly Typeface ShapeTypeface = new(
        new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    private readonly Dictionary<char, SolidColorBrush> _shapeBrushes = new();

    private readonly Dictionary<char, SolidColorBrush[]> _crystalBrushes = new();

    private readonly Dictionary<(char Text, bool Light, double Size), FormattedText> _texts = new();

    private ShapeTheme()
    {
        EmptyBrush = CreateBrush(ShapePalette.Empty);

        PinBrush = CreateBrush(0xFF000000);

        CellBorderPen = CreatePen(
            Color.FromArgb(184, 255, 255, 255),
            1);

        GridPen = CreatePen(
            ShapePaletteHex.ToColor(GridMetrics.GridLineColor),
            1);

        SelectionPen = CreatePen(
            ShapePaletteHex.ToColor(GridMetrics.SelectionColor),
            2);

        DarkTextBrush = CreateBrush(0xFF000000);

        LightTextBrush = CreateBrush(0xFFFFFFFF);
    }

    /// <summary>
    /// 空白单元格底色。
    /// </summary>
    public SolidColorBrush EmptyBrush { get; }

    /// <summary>
    /// 顶针中间的竖条。
    /// </summary>
    public SolidColorBrush PinBrush { get; }

    /// <summary>
    /// 单元格内侧白色描边。
    /// </summary>
    public Pen CellBorderPen { get; }

    /// <summary>
    /// 网格线。
    /// </summary>
    public Pen GridPen { get; }

    /// <summary>
    /// 选中框。线宽随单元格尺寸变化，绘制时另建 Pen。
    /// </summary>
    public Pen SelectionPen { get; }

    public SolidColorBrush DarkTextBrush { get; }

    public SolidColorBrush LightTextBrush { get; }

    public Typeface Typeface => ShapeTypeface;

    /// <summary>
    /// 图形或晶体的填充色。
    /// </summary>
    public SolidColorBrush GetShapeBrush(
        char? colorCode)
    {
        char key = colorCode ?? 'u';

        if (_shapeBrushes.TryGetValue(
                key,
                out SolidColorBrush? brush))
        {
            return brush;
        }

        brush = CreateBrush(ShapePalette.FromCode(key));

        _shapeBrushes[key] = brush;

        return brush;
    }

    /// <summary>
    /// 晶体单元格的七段条带颜色。
    /// </summary>
    public SolidColorBrush[] GetCrystalBrushes(
        char? colorCode)
    {
        char key = colorCode ?? 'u';

        if (_crystalBrushes.TryGetValue(
                key,
                out SolidColorBrush[]? brushes))
        {
            return brushes;
        }

        uint[] bands = CrystalBands.Get(key);

        brushes = new SolidColorBrush[bands.Length];

        for (int index = 0;
             index < bands.Length;
             index++)
        {
            brushes[index] = CreateBrush(bands[index]);
        }

        _crystalBrushes[key] = brushes;

        return brushes;
    }

    /// <summary>
    /// 单元格文字（图 / 晶），按字号与前景色缓存。
    /// </summary>
    public FormattedText GetText(
        string text,
        bool light,
        double fontSize)
    {
        char key = text[0];

        if (_texts.TryGetValue(
                (key, light, fontSize),
                out FormattedText? formatted))
        {
            return formatted;
        }

        formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            ShapeTypeface,
            fontSize,
            light ? LightTextBrush : DarkTextBrush,
            1.0);

        _texts[(key, light, fontSize)] = formatted;

        return formatted;
    }

    public static SolidColorBrush CreateBrush(
        uint argb)
    {
        return CreateBrush(ShapePaletteHex.ToColor(argb));
    }

    public static SolidColorBrush CreateBrush(
        Color color)
    {
        var brush = new SolidColorBrush(color);

        brush.Freeze();

        return brush;
    }

    public static Pen CreatePen(
        Color color,
        double thickness)
    {
        var pen = new Pen(CreateBrush(color), thickness);

        pen.Freeze();

        return pen;
    }

    /// <summary>
    /// 顶针中间的黑色竖条。
    /// </summary>
    public static Rect GetPinBar(
        double x,
        double y,
        double width,
        double height)
    {
        double barWidth = width * GridMetrics.PinBarRatio;

        return new Rect(
            x + (width - barWidth) / 2,
            y,
            barWidth,
            height);
    }

    /// <summary>
    /// 单元格类型对应的文字。
    /// </summary>
    public static string GetCellText(
        CellType type)
    {
        return type == CellType.Crystal ? "晶" : "图";
    }

    /// <summary>
    /// 只有图形和晶体需要画字。
    /// </summary>
    public static bool HasText(
        CellType type)
    {
        return type is CellType.Shape or CellType.Crystal;
    }
}

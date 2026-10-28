namespace ShapeEditor.Core.Theme;

/// <summary>
/// 竖式图的绘制尺寸与线宽。
///
/// 数值来自 Web 端 shape-grid.js，WPF 与 MAUI 都以这里为唯一来源，
/// 保证三端画出来的格子比例一致。
/// 单位是逻辑像素（WPF 里是 DIP）。
/// </summary>
public static class GridMetrics
{
    /// <summary>
    /// 基准单元格宽度。所有单元格按这个尺寸绘制后再整体缩放。
    /// </summary>
    public const double CellWidth = 100;

    /// <summary>
    /// 基准单元格高度。
    /// </summary>
    public const double CellHeight = 76;

    /// <summary>
    /// 适应屏幕时四周留白。
    /// </summary>
    public const double FitMargin = 12;

    /// <summary>
    /// 适应屏幕时放大的上限，避免小图形被拉得过大。
    /// </summary>
    public const double MaxFitScale = 1.45;

    public const double MinZoom = 0.35;

    public const double MaxZoom = 5;

    /// <summary>
    /// 网格线颜色 0xAARRGGBB。
    /// </summary>
    public const uint GridLineColor = 0xFF9E9E9E;

    /// <summary>
    /// 选中单元格的描边颜色。
    /// </summary>
    public const uint SelectionColor = 0xFF1976D2;

    /// <summary>
    /// 单元格内侧白色描边的透明度。
    /// </summary>
    public const double CellBorderOpacity = 0.72;

    /// <summary>
    /// 顶针中间的黑色竖条占宽度的比例。
    /// </summary>
    public const double PinBarRatio = 1.0 / 3.0;

    /// <summary>
    /// 导出图片时每一格的基准尺寸。
    /// </summary>
    public const double ExportCellWidth = 128;

    public const double ExportCellHeight = 96;

    /// <summary>
    /// 导出图片的最大边长，超过就拒绝导出。
    /// </summary>
    public const int MaxExportSide = 16000;

    /// <summary>
    /// 图 / 晶 文字的字号：min(宽,高) 的 46%，限制在 14 到 72 之间。
    /// </summary>
    public static double GetFontSize(
        double width,
        double height)
    {
        return Math.Clamp(
            Math.Min(width, height) * 0.46,
            14,
            72);
    }

    /// <summary>
    /// 单元格内侧白色描边的宽度。
    /// </summary>
    public static double GetCellBorderWidth(
        double width,
        double height)
    {
        return Math.Max(
            1,
            Math.Min(
                4,
                Math.Min(width, height) * 0.025));
    }

    /// <summary>
    /// 单元格内侧白色描边的内缩距离。
    /// </summary>
    public static double GetCellBorderInset(
        double width,
        double height)
    {
        return Math.Max(
            2,
            Math.Min(width, height) * 0.035);
    }

    /// <summary>
    /// 选中框的线宽与内缩。
    /// </summary>
    public static double GetSelectionWidth(
        double width,
        double height)
    {
        return Math.Max(
            2,
            Math.Min(
                5,
                Math.Min(width, height) * 0.035));
    }

    public static double GetSelectionInset(
        double width,
        double height)
    {
        return Math.Max(
            2,
            Math.Min(width, height) * 0.035);
    }

    /// <summary>
    /// 计算适应屏幕的缩放比例，与 Web 端公式一致。
    /// </summary>
    public static double GetFitScale(
        double viewWidth,
        double viewHeight,
        int columns,
        int rows)
    {
        if (viewWidth <= 0 ||
            viewHeight <= 0 ||
            columns <= 0 ||
            rows <= 0)
        {
            return 1;
        }

        double scale = Math.Min(
            (viewWidth - FitMargin * 2) / (columns * CellWidth),
            (viewHeight - FitMargin * 2) / (rows * CellHeight));

        return Math.Clamp(
            scale,
            0.05,
            MaxFitScale);
    }
}

using System.Collections.Concurrent;

namespace ShapeEditor.Core.Theme;

/// <summary>
/// 晶体单元格的竖条带配色。
///
/// 七段算法：边缘压暗，中间向白色混合。
/// Web、MAUI、WPF 三端共用这一份实现。
///
/// 黑色晶体是特例：底色太暗，高光需要更强，
/// 否则玻璃效果看不出来。
/// </summary>
public static class CrystalBands
{
    public const int BandCount = 7;

    private static readonly ConcurrentDictionary<char, uint[]> Cache = new();

    /// <summary>
    /// 按颜色代码取七段条带色，顺序为：
    /// 边缘、基础、过渡、高光、过渡、基础、边缘。
    /// </summary>
    public static uint[] Get(
        char colorCode)
    {
        return Cache.GetOrAdd(
            colorCode,
            Create);
    }

    private static uint[] Create(
        char colorCode)
    {
        uint baseColor = ShapePalette.FromCode(colorCode);

        byte a = (byte)(baseColor >> 24);
        byte r = (byte)(baseColor >> 16);
        byte g = (byte)(baseColor >> 8);
        byte b = (byte)baseColor;

        /*
         * 黑色晶体提高高光强度，其余颜色用标准系数。
         */
        double edgeFactor = colorCode == 'k' ? 0.72 : 0.82;

        double shoulderAmount = colorCode == 'k' ? 0.16 : 0.12;

        double highlightAmount = colorCode == 'k' ? 0.42 : 0.34;

        uint edge = Pack(
            a,
            (byte)(r * edgeFactor),
            (byte)(g * edgeFactor),
            (byte)(b * edgeFactor));

        uint shoulder = MixWithWhite(a, r, g, b, shoulderAmount);

        uint highlight = MixWithWhite(a, r, g, b, highlightAmount);

        return new[]
        {
            edge,
            baseColor,
            shoulder,
            highlight,
            shoulder,
            baseColor,
            edge
        };
    }

    private static uint MixWithWhite(
        byte a,
        byte r,
        byte g,
        byte b,
        double amount)
    {
        return Pack(
            a,
            (byte)(r + (255 - r) * amount),
            (byte)(g + (255 - g) * amount),
            (byte)(b + (255 - b) * amount));
    }

    private static uint Pack(
        byte a,
        byte r,
        byte g,
        byte b)
    {
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}

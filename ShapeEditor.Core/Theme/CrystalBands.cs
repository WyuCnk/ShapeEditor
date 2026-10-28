using System.Collections.Concurrent;

namespace ShapeEditor.Core.Theme;

/// <summary>
/// 晶体单元格的竖条带配色。
///
/// Web 端 shape-grid.js 里是同样的七段算法：
/// 边缘压暗到 82%，中间向白色混合 12% 和 34%。
/// 这里抽到 Core，供 MAUI 与 WPF 共用；Web 仍是独立的 JS 实现，
/// 修改时两边都要同步。
/// </summary>
public static class CrystalBands
{
    public const int BandCount = 7;

    private static readonly ConcurrentDictionary<uint, uint[]> Cache = new();

    public static uint[] Get(
        uint baseColor)
    {
        return Cache.GetOrAdd(
            baseColor,
            Create);
    }

    private static uint[] Create(
        uint baseColor)
    {
        byte a = (byte)(baseColor >> 24);
        byte r = (byte)(baseColor >> 16);
        byte g = (byte)(baseColor >> 8);
        byte b = (byte)baseColor;

        uint edge = Pack(a, (byte)(r * 0.82), (byte)(g * 0.82), (byte)(b * 0.82));
        uint shoulder = MixWithWhite(a, r, g, b, 0.12);
        uint highlight = MixWithWhite(a, r, g, b, 0.34);

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

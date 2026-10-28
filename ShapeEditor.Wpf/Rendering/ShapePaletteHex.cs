using System.Windows.Media;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// Core 里的颜色用 0xAARRGGBB 表示，这里转换成 WPF 的 Color。
/// </summary>
internal static class ShapePaletteHex
{
    public static Color ToColor(
        uint argb)
    {
        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }
}

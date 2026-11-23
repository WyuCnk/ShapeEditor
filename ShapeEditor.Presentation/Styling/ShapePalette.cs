using ShapeEditor.Core.Models;
using CoreBands = ShapeEditor.Core.Theme.CrystalBands;
using CorePalette = ShapeEditor.Core.Theme.ShapePalette;

namespace ShapeEditor.Presentation.Styling;

/// <summary>
/// 三端共用的调色板：颜色码 → ARGB / <c>#RRGGBB</c> / 中文名 / 晶体条带。
///
/// <para>
/// 真正的色值仍以 Core 的 <c>ShapePalette</c> 与 <c>CrystalBands</c> 为唯一来源
/// （Core 不新增、不修改 API），这里只把三端各自重复的「颜色码 → 展示值」换算
/// 集中到一处：
/// </para>
/// <list type="bullet">
/// <item>WPF 用 <see cref="Argb(char?)"/> 建画刷、<see cref="CrystalBands"/> 建条带画刷；</item>
/// <item>MAUI 用同样的两项建 <c>Color</c> / <c>SKColor</c>；</item>
/// <item>Web 用 <see cref="Hex"/> 填 <c>ShapeThemeData</c> 之外的自有映射与文案。</item>
/// </list>
/// <para>
/// <c>#RRGGBB</c> 的格式与 Core 的 <c>ShapeThemeData</c> 完全一致（大写、无 alpha），
/// 保证 Web 端颜色字符串与迁移前逐字相同。
/// </para>
/// </summary>
public static class ShapePalette
{
    /// <summary>空白底色（0xAARRGGBB）。</summary>
    public const uint Empty = CorePalette.Empty;

    /// <summary>未着色 / 未知颜色码的兜底色码。</summary>
    public const char FallbackCode = 'u';

    /// <summary>三端支持的全部颜色码（顺序与 Core 的 <c>ShapeColor.All</c> 一致）。</summary>
    public static IReadOnlyList<char> Codes => ShapeColor.All;

    /// <summary>
    /// 颜色码对应的 0xAARRGGBB 色值；<paramref name="code"/> 为 null 时按未着色处理，
    /// 未知颜色码回退成空白底色（与 Core 行为一致）。
    /// </summary>
    public static uint Argb(
        char? code)
    {
        return CorePalette.FromCode(code ?? FallbackCode);
    }

    /// <summary>
    /// 颜色码对应的 <c>#RRGGBB</c> 字符串；web 端颜色映射用。
    /// </summary>
    public static string Hex(
        char? code)
    {
        return ToHex(Argb(code));
    }

    /// <summary>
    /// 0xAARRGGBB → <c>#RRGGBB</c>：大写十六进制、丢弃 alpha，
    /// 与 Core 的 <c>ShapeThemeData</c> 保持同一格式。
    /// </summary>
    public static string ToHex(
        uint argb)
    {
        return $"#{(byte)(argb >> 16):X2}{(byte)(argb >> 8):X2}{(byte)argb:X2}";
    }

    /// <summary>颜色码对应的七段晶体条带色（边缘 → 基础 → 过渡 → 高光 → …）。</summary>
    public static uint[] CrystalBands(
        char? code)
    {
        return CoreBands.Get(code ?? FallbackCode);
    }

    /// <summary>按钮上显示的颜色简称（红 / 绿 / 蓝 / …）。</summary>
    public static string ShortName(
        char code)
    {
        return CorePalette.GetShortName(code);
    }

    /// <summary>颜色全名（红色 / 绿色 / …）；未知颜色码返回「无颜色」。</summary>
    public static string Name(
        char? code)
    {
        return ShapeColor.GetName(code);
    }

    /// <summary>深色底上是否需要白色文字（目前只有黑色 k）。</summary>
    public static bool NeedsLightText(
        char? code)
    {
        return CorePalette.NeedsLightText(code);
    }
}

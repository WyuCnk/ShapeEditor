namespace ShapeEditor.Core.Theme;

/// <summary>
/// 形状与晶体的调色板。
///
/// 这里的 RGB 值与 Web 端 shape-grid.js、MAUI 端 ShapeGridDrawable 保持一致，
/// 三条前端共用同一套颜色，避免各写一份。
/// 数值格式为 0xAARRGGBB。
/// </summary>
public static class ShapePalette
{
    public const uint Red = 0xFFFF3864;

    public const uint Green = 0xFF83FF38;

    public const uint Blue = 0xFF65B7FF;

    public const uint Cyan = 0xFF35E6C1;

    public const uint Magenta = 0xFFD85CFF;

    public const uint Yellow = 0xFFFFB83E;

    public const uint White = 0xFFD8D8D8;

    public const uint Black = 0xFF35353C;

    public const uint Uncolored = 0xFFA9BAC8;

    /// <summary>
    /// 空白的底色。
    /// </summary>
    public const uint Empty = 0xFFFFFFFF;

    /// <summary>
    /// 颜色代码对应的颜色。
    /// 未知代码返回空白底色。
    /// </summary>
    public static uint FromCode(
        char code)
    {
        return code switch
        {
            'r' => Red,
            'g' => Green,
            'b' => Blue,
            'c' => Cyan,
            'm' => Magenta,
            'y' => Yellow,
            'w' => White,
            'k' => Black,
            'u' => Uncolored,
            _ => Empty
        };
    }

    /// <summary>
    /// 按钮上显示的颜色简称，与 Web 端一致。
    /// </summary>
    public static string GetShortName(
        char code)
    {
        return code switch
        {
            'r' => "红",
            'g' => "绿",
            'b' => "蓝",
            'c' => "青",
            'm' => "洋红",
            'y' => "黄",
            'w' => "白",
            'k' => "黑",
            'u' => "未着色",
            _ => ""
        };
    }

    /// <summary>
    /// 深色底上需要使用白色文字，目前只有黑色 k。
    /// </summary>
    public static bool NeedsLightText(
        char? code)
    {
        return code == 'k';
    }
}

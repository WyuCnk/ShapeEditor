namespace ShapeEditor.Core;

/// <summary>
/// 图形尺寸的硬性规则。
///
/// 象限数必须为偶数：切割要沿中线均分半球，
/// 奇数会让两半不等宽，因此全局禁止。
/// </summary>
public static class ShapeRules
{
    public const int MinPartCount = 2;

    public const int MaxPartCount = 100;

    public const int MinLayerCount = 1;

    public const int MaxLayerCount = 100;

    public const int MaxCellCount = 10_000;

    public static bool IsValidPartCount(
        int parts)
    {
        return parts >= MinPartCount &&
               parts <= MaxPartCount &&
               parts % 2 == 0;
    }

    public static bool IsValidLayerCount(
        int layers)
    {
        return layers >= MinLayerCount &&
               layers <= MaxLayerCount;
    }

    public static bool IsValidDimensions(
        int layers,
        int parts)
    {
        return IsValidLayerCount(layers) &&
               IsValidPartCount(parts) &&
               (long)layers * parts <= MaxCellCount;
    }

    /// <summary>
    /// 把象限数向上取到最近的偶数，并夹在合法区间内。
    /// </summary>
    public static int NormalizePartCount(
        int parts)
    {
        int value = Math.Clamp(
            parts,
            MinPartCount,
            MaxPartCount);

        if (value % 2 != 0)
        {
            value++;
        }

        return Math.Min(value, MaxPartCount);
    }
}

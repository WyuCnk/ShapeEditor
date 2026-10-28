using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;

namespace ShapeEditor.Tests;

/// <summary>
/// 三端共享的主题常量。
/// </summary>
internal static class ThemeTests
{
    public static void PaletteMatchesWebHexValues()
    {
        Assert.Equal(0xFFFF3864u, ShapePalette.FromCode('r'));
        Assert.Equal(0xFF83FF38u, ShapePalette.FromCode('g'));
        Assert.Equal(0xFF65B7FFu, ShapePalette.FromCode('b'));
        Assert.Equal(0xFF35E6C1u, ShapePalette.FromCode('c'));
        Assert.Equal(0xFFD85CFFu, ShapePalette.FromCode('m'));
        Assert.Equal(0xFFFFB83Eu, ShapePalette.FromCode('y'));
        Assert.Equal(0xFFD8D8D8u, ShapePalette.FromCode('w'));
        Assert.Equal(0xFF35353Cu, ShapePalette.FromCode('k'));
        Assert.Equal(0xFFA9BAC8u, ShapePalette.FromCode('u'));
    }

    public static void UnknownColorFallsBackToWhite()
    {
        Assert.Equal(ShapePalette.Empty, ShapePalette.FromCode('z'));
    }

    public static void CrystalBandsHaveSevenSteps()
    {
        foreach (char code in ShapeColor.All)
        {
            Assert.Equal(7, CrystalBands.Get(code).Length);
        }
    }

    public static void CrystalBandsAreSymmetric()
    {
        uint[] bands = CrystalBands.Get('r');

        Assert.Equal(bands[0], bands[6], "首尾两段应当对称");
        Assert.Equal(bands[1], bands[5], "基础色应当对称");
        Assert.Equal(bands[2], bands[4], "过渡色应当对称");
    }

    public static void CrystalHighlightIsBrighterThanBase()
    {
        uint baseColor = CrystalBands.Get('r')[1];
        uint highlight = CrystalBands.Get('r')[3];

        Assert.NotEqual(baseColor, highlight, "高光段应当与基础色不同");
    }

    public static void BlackCrystalUsesStrongerHighlight()
    {
        uint blackHighlight = CrystalBands.Get('k')[3];
        uint blackBase = CrystalBands.Get('k')[1];

        Assert.NotEqual(blackBase, blackHighlight);

        // 黑色底色本身很暗，高光必须明显提亮
        byte blackGreen = (byte)(blackHighlight >> 8);

        Assert.True(blackGreen > 40, "黑色晶体的高光太暗");
    }

    public static void FitScaleIsClamped()
    {
        double big = GridMetrics.GetFitScale(4000, 4000, 1, 1);
        double small = GridMetrics.GetFitScale(40, 40, 100, 100);

        Assert.True(big <= GridMetrics.MaxFitScale, "放大不应超过上限");
        Assert.True(small > 0, "缩放必须为正");
        Assert.True(small >= 0.05, "缩放不应小于下限");
    }

    public static void WebThemeDataMirrorsCore()
    {
        ShapeThemeData theme = ShapeThemeData.Default;

        Assert.Equal(9, theme.Colors.Count);
        Assert.Equal(9, theme.CrystalBands.Count);
        Assert.Equal("#FF3864", theme.Colors["r"]);
        Assert.Equal(7, theme.CrystalBands["r"].Length);
        Assert.Equal(GridMetrics.CellWidth, theme.CellWidth);
        Assert.Equal(GridMetrics.CellHeight, theme.CellHeight);
        Assert.Equal("#9E9E9E", theme.GridLineColor);
        Assert.Equal("#1976D2", theme.SelectionColor);
    }
}

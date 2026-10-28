using System.Windows.Media;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;
using ShapeEditor.Wpf.Rendering;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 形状画笔选项（空 / 顶 / 晶 / 图）。
/// </summary>
public sealed class ShapeBrushOption : ObservableObject
{
    private bool _isActive;

    public ShapeBrushOption(
        CellType type,
        string label)
    {
        Type = type;
        Label = label;
    }

    public CellType Type { get; }

    public string Label { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

/// <summary>
/// 颜色画笔选项。
/// </summary>
public sealed class ColorBrushOption : ObservableObject
{
    private bool _isActive;

    public ColorBrushOption(
        char code)
    {
        Code = code;

        Label = ShapePalette.GetShortName(code);

        uint argb = ShapePalette.FromCode(code);

        Swatch = ShapeTheme.CreateBrush(argb);

        TextBrush = ShapePalette.NeedsLightText(code)
            ? ShapeTheme.CreateBrush(0xFFFFFFFF)
            : ShapeTheme.CreateBrush(0xFF000000);
    }

    public char Code { get; }

    public string Label { get; }

    /// <summary>
    /// 色块背景。
    /// </summary>
    public Brush Swatch { get; }

    /// <summary>
    /// 色块上的文字颜色。
    /// </summary>
    public Brush TextBrush { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

namespace ShapeEditor.Core.Models;

public sealed class ShapeCell
{
    public CellType Type { get; private set; }

    /// <summary>
    /// 普通形状字符，例如 C、R、G、F。
    /// 晶体使用 c，顶针使用 P，空使用 -。
    /// </summary>
    public char ShapeCode { get; private set; }

    /// <summary>
    /// 颜色代码：r/g/b/c/m/y/w/k/u。
    /// 空和顶针没有颜色，使用 null。
    /// </summary>
    public char? ColorCode { get; private set; }

    private ShapeCell(
        CellType type,
        char shapeCode,
        char? colorCode)
    {
        Type = type;
        ShapeCode = shapeCode;
        ColorCode = colorCode;
    }

    public static ShapeCell Empty()
    {
        return new ShapeCell(
            CellType.Empty,
            '-',
            null);
    }

    public static ShapeCell Pin()
    {
        return new ShapeCell(
            CellType.Pin,
            'P',
            null);
    }

    public static ShapeCell Crystal(char color)
    {
        return new ShapeCell(
            CellType.Crystal,
            'c',
            color);
    }

    public static ShapeCell Shape(
        char shapeCode,
        char color)
    {
        if (!IsNormalShapeCode(shapeCode))
        {
            shapeCode = 'C';
        }

        return new ShapeCell(
            CellType.Shape,
            shapeCode,
            color);
    }

    public ShapeCell Clone()
    {
        return new ShapeCell(
            Type,
            ShapeCode,
            ColorCode);
    }

    public void SetColor(char color)
    {
        if (Type is CellType.Shape or CellType.Crystal)
        {
            ColorCode = color;
        }
    }

    public string ToShortCode()
    {
        return Type switch
        {
            CellType.Empty => "--",
            CellType.Pin => "P-",
            CellType.Crystal => $"c{ColorCode ?? 'u'}",
            CellType.Shape => $"{GetExportShapeCode()}{ColorCode ?? 'u'}",
            _ => "--"
        };
    }

    public string ToDisplayText()
    {
        return Type switch
        {
            CellType.Empty => "",
            CellType.Pin => "",
            CellType.Crystal => "晶",
            CellType.Shape => "图",
            _ => ""
        };
    }

    public bool HasColor =>
        Type is CellType.Shape or CellType.Crystal;

    public static bool IsNormalShapeCode(char code)
    {
        return code is >= 'A' and <= 'Z'
            && code != 'P';
    }

    private char GetExportShapeCode()
    {
        return IsNormalShapeCode(ShapeCode)
            ? ShapeCode
            : 'C';
    }
}
using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Services;

public sealed class ShapeEditorService
{
    #region 基础操作

    public void Clear(
        ShapeDocument document)
    {
        document.Clear();
    }

    #endregion

    #region 普通类型编辑

    public void SetType(
        ShapeDocument document,
        int layer,
        int part,
        CellType type)
    {
        ShapeCell oldCell =
            document[layer, part];

        ShapeCell newCell =
            type switch
            {
                CellType.Empty =>
                    ShapeCell.Empty(),

                CellType.Pin =>
                    ShapeCell.Pin(),

                CellType.Crystal =>
                    ShapeCell.Crystal(
                        GetCrystalColor(oldCell)),

                CellType.Shape =>
                    ShapeCell.Shape(
                        oldCell.Type == CellType.Shape
                            ? oldCell.ShapeCode
                            : 'C',
                        GetShapeColor(oldCell)),

                _ =>
                    ShapeCell.Empty()
            };

        document[layer, part] =
            newCell;
    }

    #endregion

    #region 形状画笔

    public void ApplyShapeBrush(
        ShapeDocument document,
        int layer,
        int part,
        CellType brushType)
    {
        ShapeCell oldCell =
            document[layer, part];

        ShapeCell newCell =
            brushType switch
            {
                CellType.Empty =>
                    ShapeCell.Empty(),

                CellType.Pin =>
                    ShapeCell.Pin(),

                CellType.Crystal =>
                    ShapeCell.Crystal(
                        GetCrystalColor(oldCell)),

                CellType.Shape =>
                    ShapeCell.Shape(
                        'C',
                        GetShapeColor(oldCell)),

                _ =>
                    ShapeCell.Empty()
            };

        document[layer, part] =
            newCell;
    }

    #endregion

    #region 颜色画笔

    public bool ApplyColorBrush(
        ShapeDocument document,
        int layer,
        int part,
        char color)
    {
        ShapeCell cell =
            document[layer, part];

        if (!cell.HasColor)
        {
            return false;
        }

        if (!ShapeColor.IsValid(color))
        {
            return false;
        }

        if (cell.ColorCode == color)
        {
            return false;
        }

        cell.SetColor(color);

        return true;
    }

    #endregion

    #region 默认颜色

    /// <summary>
    /// 普通形状的颜色：
    ///
    /// 已有图形或晶体：保留原颜色；
    /// 空或顶针：默认未着色 u。
    /// </summary>
    private static char GetShapeColor(
        ShapeCell oldCell)
    {
        if ((oldCell.Type is CellType.Shape
             or CellType.Crystal) &&
            oldCell.ColorCode is char color)
        {
            return color;
        }

        return 'u';
    }

    /// <summary>
    /// 晶体的颜色：
    ///
    /// 已有图形或晶体：保留原颜色；
    /// 空或顶针：默认红色 r。
    /// </summary>
    private static char GetCrystalColor(
        ShapeCell oldCell)
    {
        if ((oldCell.Type is CellType.Shape
             or CellType.Crystal) &&
            oldCell.ColorCode is char color)
        {
            return color;
        }

        return 'r';
    }

    #endregion
}
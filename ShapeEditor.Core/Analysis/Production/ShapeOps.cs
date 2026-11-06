using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 图形的基本操作与比较工具。
///
/// 图形比较一律按「层数 + 象限数 + 逐格类型/形状码/颜色码」判断，
/// 不走短代码字符串，避免顶部空层省略带来的歧义。
/// </summary>
public static class ShapeOps
{
    /// <summary>
    /// 两个图形是否逐格完全相同（尺寸也必须相同）。
    /// </summary>
    public static bool Same(
        ShapeDocument? left,
        ShapeDocument? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.LayerCount != right.LayerCount ||
            left.PartCount != right.PartCount)
        {
            return false;
        }

        for (int layer = 0;
             layer < left.LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < left.PartCount;
                 part++)
            {
                if (!SameCell(left[layer, part], right[layer, part]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool SameCell(
        ShapeCell left,
        ShapeCell right)
    {
        return left.Type == right.Type &&
               left.ShapeCode == right.ShapeCode &&
               left.ColorCode == right.ColorCode;
    }

    /// <summary>
    /// 执行一次下落，结果与原来逐格相同即为稳定。
    /// </summary>
    public static bool IsStable(
        ShapeDocument document,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        ShapeDocument? collapsed =
            ShapePhysics.Collapse(document, mode).Document;

        return Same(document, collapsed);
    }

    /// <summary>
    /// 旋转 k 个象限（k 为正顺时针，负为逆时针）。
    /// 返回新图形，不修改入参。
    /// </summary>
    public static ShapeDocument Rotate(
        ShapeDocument document,
        int k)
    {
        ShapeDocument copy = document.Clone();

        int steps = ((k % document.PartCount) +
                     document.PartCount) %
                    document.PartCount;

        for (int index = 0;
             index < steps;
             index++)
        {
            copy.MoveRightCircular();
        }

        return copy;
    }

    /// <summary>
    /// 只保留从 from 开始的 count 个连续象限（环形），其余清空。
    /// 象限数与层数保持不变。
    /// </summary>
    public static ShapeDocument Project(
        ShapeDocument source,
        int from,
        int count)
    {
        var result = new ShapeDocument(
            source.LayerCount,
            source.PartCount);

        for (int index = 0;
             index < count;
             index++)
        {
            int column = Wrap(from + index, source.PartCount);

            for (int layer = 0;
                 layer < source.LayerCount;
                 layer++)
            {
                result[layer, column] =
                    source[layer, column].Clone();
            }
        }

        return result;
    }

    /// <summary>
    /// 截断到指定层数（调用 Truncate 的物理语义：先碎超出上限的晶体）。
    /// </summary>
    public static ShapeDocument Truncate(
        ShapeDocument document,
        int layerCount)
    {
        return ShapePhysics
            .Limit(document, layerCount)
            .Document!;
    }

    /// <summary>
    /// 把整列（含所有层）设为满层普通块（柱子）。
    /// </summary>
    public static void SetPillar(
        ShapeDocument document,
        int column)
    {
        int target = Wrap(column, document.PartCount);

        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            document[layer, target] =
                ShapeCell.Shape('C', 'u');
        }
    }

    public static int Wrap(
        int value,
        int size)
    {
        return ((value % size) + size) % size;
    }
}

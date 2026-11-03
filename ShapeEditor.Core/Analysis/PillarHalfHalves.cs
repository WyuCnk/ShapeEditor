using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 四个带柱子的半半边（规格 §1）。
///
/// 每个半半边只有一根柱子（满层 <c>Cu</c> 普通形状），柱位为：
/// P1 @ (a-1)、P2 @ (a+half)、P3 @ (a+half-1)、P4 @ a，均对象限数 n 取模。
/// 柱子所在列都在本片内容区间之外，因此不会覆盖内容。
/// </summary>
internal static class PillarHalfHalves
{
    /// <summary>片编号：1–4。</summary>
    public const int First = 1;

    public const int Second = 2;

    public const int Third = 3;

    public const int Fourth = 4;

    /// <summary>
    /// 按规格 §1 构造第 <paramref name="piece"/> 片（未做 §2 的预旋转）。
    /// </summary>
    public static ShapeDocument BuildPiece(
        ShapeDocument source,
        int axis,
        int leftSplit,
        int rightSplit,
        int piece)
    {
        int parts = source.PartCount;
        int half = parts / 2;

        (int start, int count, int pillar) = piece switch
        {
            First => (axis, leftSplit, axis - 1),
            Second => (axis + leftSplit, half - leftSplit, axis + half),
            Third => (axis + half, rightSplit, axis + half - 1),
            Fourth => (axis + half + rightSplit, half - rightSplit, axis),
            _ => throw new ArgumentOutOfRangeException(nameof(piece))
        };

        var shape = new ShapeDocument(
            source.LayerCount,
            parts);

        for (int layer = 0;
             layer < source.LayerCount;
             layer++)
        {
            // 只保留本片内容；其余列保持为空。
            for (int offset = 0;
                 offset < count;
                 offset++)
            {
                int column = Mod(start + offset, parts);

                shape[layer, column] = source[layer, column].Clone();
            }

            // 柱子补满所有层，作为固定边界。
            shape[layer, Mod(pillar, parts)] = ShapeCell.Shape('C', 'u');
        }

        return shape;
    }

    /// <summary>
    /// 顺时针旋转 <paramref name="steps"/> 个象限；负值表示逆时针。
    /// 顺转等价于编辑区的「右移」。
    /// </summary>
    public static ShapeDocument RotateRight(
        ShapeDocument source,
        int steps)
    {
        int parts = source.PartCount;

        int normalized = Mod(steps, parts);

        ShapeDocument result = source.Clone();

        for (int index = 0;
             index < normalized;
             index++)
        {
            result.MoveRightCircular();
        }

        return result;
    }

    private static int Mod(
        int value,
        int modulus)
    {
        return ((value % modulus) + modulus) % modulus;
    }
}

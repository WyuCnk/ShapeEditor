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
    /// 交换方向的显示文本。
    ///
    /// 象限数 ≤ 9 时保持紧凑写法，例如 <c>(12+3)+(4+56)</c>；
    /// 象限数 &gt; 9 时相邻数字会产生歧义（1、2 与 12），
    /// 整串改用逗号分隔，例如 <c>(1,2,3,4)+(5,6,7,8)</c>。
    /// 判定按整个图形的象限数统一执行，两个半边与左右切点写法一致。
    /// </summary>
    /// <param name="axis">轴（半边起始列，0 起）。</param>
    /// <param name="half">半边包含的象限数（总象限数的一半）。</param>
    /// <param name="leftSplit">左切点（1 到 half−1）。</param>
    /// <param name="rightSplit">右切点（1 到 half−1）。</param>
    public static string FormatDirection(
        int axis,
        int half,
        int leftSplit,
        int rightSplit)
    {
        int parts = half * 2;

        string separator = parts > 9 ? "," : "";

        string leftText =
            Numbers(axis, leftSplit, parts, separator) +
            "+" +
            Numbers(axis + leftSplit, half - leftSplit, parts, separator);

        string rightText =
            Numbers(axis + half, rightSplit, parts, separator) +
            "+" +
            Numbers(axis + half + rightSplit, half - rightSplit, parts, separator);

        return $"({leftText})+({rightText})";

        static string Numbers(
            int start,
            int count,
            int parts,
            string separator)
        {
            return string.Join(
                separator,
                Enumerable.Range(0, count)
                    .Select(index => ((start + index) % parts + 1).ToString()));
        }
    }

    /// <summary>
    /// 把有符号的顺时针旋转量规范成「较短方向」：
    /// 顺转 k 与逆转 n−k 取较小者；相等（k = n/2，反转）时按顺时针处理。
    ///
    /// 返回可直接写进 <see cref="ProductionStep"/> 的方向、数量与标题。
    /// </summary>
    public static (RotationDirection Direction, int Count, string Title) DescribeRotation(
        int parts,
        int clockwiseSteps)
    {
        int normalized = Mod(clockwiseSteps, parts);

        if (normalized == 0)
        {
            return (
                RotationDirection.None,
                0,
                "旋转结果（无旋转）");
        }

        int counterClockwise = parts - normalized;

        // 相等即反转：按顺时针旋转 n/2 个象限处理。
        bool clockwise = normalized <= counterClockwise;

        int count = clockwise ? normalized : counterClockwise;

        string directionText = clockwise ? "顺时针" : "逆时针";

        return (
            clockwise ? RotationDirection.Clockwise : RotationDirection.CounterClockwise,
            count,
            $"旋转结果（{directionText}旋转 {count} 个象限）");
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

using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 一个交换方向的判定结果。
/// </summary>
public sealed record ExchangeDirection(
    int Axis,
    int LeftSplit,
    int RightSplit,
    string Display,
    bool IsLegal);

/// <summary>
/// 半边稳定性分析结果。
/// </summary>
public sealed record HalfStabilityResult(
    int PartCount,
    int DirectionCount,
    IReadOnlyList<string> LegalDirections,
    IReadOnlyList<ExchangeDirection> Directions)
{
    /// <summary>只要存在一个合法交换方向就称图形可交换。</summary>
    public bool IsExchangeable => LegalDirections.Count > 0;
}

/// <summary>
/// 半边稳定性：
///
/// 交换方向 = 轴（n/2 个）× 左切点（n/2−1）× 右切点（n/2−1）。
/// 某方向合法 ⇔ 两个半边稳定，且该方向上的四个带柱子半半边（规格 §1）都稳定。
///
/// 每个带柱子的半半边只有一根柱子：P1 @(a−1)、P2 @(a+half)、
/// P3 @(a+half−1)、P4 @a。稳定性判定直接用未旋转的片即可：
/// 旋转只是列的循环移位，不改变下落结果；真正需要预旋转的是
/// <see cref="ProductionPathParser"/> 里的交换步骤（规格 §2）。
/// </summary>
public static class HalfStabilityAnalyzer
{
    public static HalfStabilityResult Analyze(
        ShapeDocument document,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        int parts = document.PartCount;
        int half = parts / 2;

        if (parts % 2 != 0 || half < 2)
        {
            return new HalfStabilityResult(
                parts,
                0,
                Array.Empty<string>(),
                Array.Empty<ExchangeDirection>());
        }

        var legal = new List<string>();

        var directionList = new List<ExchangeDirection>();

        for (int axis = 0; axis < half; axis++)
        {
            int[] left = Contiguous(axis, half, parts);
            int[] right = Contiguous(axis + half, half, parts);

            // 两个半边本身也必须稳定，否则 Cut 会把它们塌陷改写。
            bool halvesStable =
                IsStable(document, left, mode) &&
                IsStable(document, right, mode);

            for (int leftSplit = 1; leftSplit < half; leftSplit++)
            {
                for (int rightSplit = 1; rightSplit < half; rightSplit++)
                {
                    bool legalDirection =
                        halvesStable &&
                        SubStable(document, axis, leftSplit, rightSplit, PillarHalfHalves.First, mode) &&
                        SubStable(document, axis, leftSplit, rightSplit, PillarHalfHalves.Second, mode) &&
                        SubStable(document, axis, leftSplit, rightSplit, PillarHalfHalves.Third, mode) &&
                        SubStable(document, axis, leftSplit, rightSplit, PillarHalfHalves.Fourth, mode);

                    string text = Display(left, leftSplit, right, rightSplit);

                    directionList.Add(new ExchangeDirection(
                        axis,
                        leftSplit,
                        rightSplit,
                        text,
                        legalDirection));

                    if (legalDirection)
                    {
                        legal.Add(text);
                    }
                }
            }
        }

        int directions = half * (half - 1) * (half - 1);

        return new HalfStabilityResult(
            parts,
            directions,
            legal,
            directionList);
    }

    /// <summary>
    /// 第 <paramref name="piece"/> 片带柱子的半半边是否稳定（未旋转）。
    /// </summary>
    private static bool SubStable(
        ShapeDocument document,
        int axis,
        int leftSplit,
        int rightSplit,
        int piece,
        CollapseMode mode)
    {
        ShapeDocument shape = PillarHalfHalves.BuildPiece(
            document,
            axis,
            leftSplit,
            rightSplit,
            piece);

        return IsUnchanged(shape, mode);
    }

    /// <summary>只保留指定列的半边是否稳定。</summary>
    private static bool IsStable(
        ShapeDocument document,
        int[] columns,
        CollapseMode mode)
    {
        int layers = document.LayerCount;
        int parts = document.PartCount;

        var shape = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            foreach (int column in columns)
            {
                shape[layer, column] = document[layer, column].Clone();
            }
        }

        return IsUnchanged(shape, mode);
    }

    private static bool IsUnchanged(
        ShapeDocument shape,
        CollapseMode mode)
    {
        ShapeDocument? collapsed = ShapePhysics.Collapse(shape, mode).Document;

        if (collapsed is null)
        {
            return false;
        }

        var exporter = new ShortCodeExporter();

        return exporter.Export(shape) == exporter.Export(collapsed);
    }

    private static int[] Contiguous(
        int start,
        int count,
        int parts)
    {
        var result = new int[count];

        for (int index = 0; index < count; index++)
        {
            result[index] = (start + index) % parts;
        }

        return result;
    }

    private static string Display(
        int[] left,
        int leftSplit,
        int[] right,
        int rightSplit)
    {
        string leftText = string.Join("", left.Take(leftSplit).Select(Number)) +
                          "+" +
                          string.Join("", left.Skip(leftSplit).Select(Number));

        string rightText = string.Join("", right.Take(rightSplit).Select(Number)) +
                           "+" +
                           string.Join("", right.Skip(rightSplit).Select(Number));

        return $"({leftText})+({rightText})";

        static string Number(int index) => (index + 1).ToString();
    }
}

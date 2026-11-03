using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

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

        // 稳定性在旋转下不变；一次分析内的重复半半边/半边只塌陷一次。
        var cache = new StabilityCache(mode);

        for (int axis = 0; axis < half; axis++)
        {
            int[] left = Contiguous(axis, half, parts);
            int[] right = Contiguous(axis + half, half, parts);

            // 两个半边本身也必须稳定，否则 Cut 会把它们塌陷改写。
            // 半边没有柱子，起始列分别是轴与轴 + half。
            bool halvesStable =
                IsStable(document, left, axis, cache) &&
                IsStable(document, right, axis + half, cache);

            /*
             * P1/P2 只依赖 (轴, 左切点)，P3/P4 只依赖 (轴, 右切点)：
             * 先各算一次，后面的双重循环只查表，不再重复构造同一片。
             * 传 1 的切点参数不会影响对应片的构造（各片只读自己那一段）。
             */
            var leftPiecesStable = new bool[half];
            var rightPiecesStable = new bool[half];

            if (halvesStable)
            {
                for (int leftSplit = 1; leftSplit < half; leftSplit++)
                {
                    leftPiecesStable[leftSplit] =
                        SubStable(document, axis, leftSplit, 1, PillarHalfHalves.First, cache) &&
                        SubStable(document, axis, leftSplit, 1, PillarHalfHalves.Second, cache);
                }

                for (int rightSplit = 1; rightSplit < half; rightSplit++)
                {
                    rightPiecesStable[rightSplit] =
                        SubStable(document, axis, 1, rightSplit, PillarHalfHalves.Third, cache) &&
                        SubStable(document, axis, 1, rightSplit, PillarHalfHalves.Fourth, cache);
                }
            }

            for (int leftSplit = 1; leftSplit < half; leftSplit++)
            {
                for (int rightSplit = 1; rightSplit < half; rightSplit++)
                {
                    // 四片里任一不稳定就跳过该方向，不进入交换阶段。
                    bool legalDirection =
                        halvesStable &&
                        leftPiecesStable[leftSplit] &&
                        rightPiecesStable[rightSplit];

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
    ///
    /// 起始列：P1 = 轴、P2 = 轴 + 左切点、P3 = 轴 + half、P4 = 轴 + half + 右切点，
    /// 与 <see cref="PillarHalfHalves.BuildPiece"/> 的内容区间一致。
    /// </summary>
    private static bool SubStable(
        ShapeDocument document,
        int axis,
        int leftSplit,
        int rightSplit,
        int piece,
        StabilityCache cache)
    {
        int half = document.PartCount / 2;

        int contentStart = piece switch
        {
            PillarHalfHalves.First => axis,
            PillarHalfHalves.Second => axis + leftSplit,
            PillarHalfHalves.Third => axis + half,
            PillarHalfHalves.Fourth => axis + half + rightSplit,
            _ => throw new ArgumentOutOfRangeException(nameof(piece))
        };

        ShapeDocument shape = PillarHalfHalves.BuildPiece(
            document,
            axis,
            leftSplit,
            rightSplit,
            piece);

        return cache.IsStable(shape, contentStart);
    }

    /// <summary>只保留指定列的半边是否稳定。</summary>
    private static bool IsStable(
        ShapeDocument document,
        int[] columns,
        int contentStart,
        StabilityCache cache)
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

        return cache.IsStable(shape, contentStart);
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

using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 制作路径的一步。
/// </summary>
public sealed record ProductionStep(
    string Title,
    ShapeDocument? Shape);

/// <summary>
/// 一条制作路径。
/// </summary>
public sealed record ProductionPath(
    int Axis,
    int LeftSplit,
    int RightSplit,
    string Direction,
    bool DirectionLegal,
    IReadOnlyList<ProductionStep> Steps);

/// <summary>
/// 制作路径解析结果。
/// </summary>
public sealed record ProductionPathResult(
    bool IsProducible,
    ProductionPath? Path,
    string? Error);

/// <summary>
/// 制作路径解析。
///
/// 工序：四个带柱子的半半边 → 交换得到两个半边 → 交换得到图形 → 顶针与堆叠。
/// 交换的两个输出中任意一个等于期望图形即视为该步成功。
/// </summary>
public static class ProductionPathParser
{
    public static ProductionPathResult Parse(
        ShapeDocument target,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        int parts = target.PartCount;

        if (parts % 2 != 0)
        {
            return new ProductionPathResult(false, null, "象限数必须为偶数。");
        }

        int half = parts / 2;

        if (half < 2)
        {
            return new ProductionPathResult(false, null, "象限数太少，无法解析制作路径。");
        }

        HalfStabilityResult halfResult = HalfStabilityAnalyzer.Analyze(target, mode);

        var exporter = new ShortCodeExporter();

        string targetCode = exporter.Export(target);

        for (int axis = 0; axis < half; axis++)
        {
            int[] left = Columns(axis, half, parts);
            int[] right = Columns(axis + half, half, parts);

            for (int leftSplit = 1; leftSplit < half; leftSplit++)
            {
                for (int rightSplit = 1; rightSplit < half; rightSplit++)
                {
                    var leftHalf = Slice(target, left);
                    var rightHalf = Slice(target, right);

                    var left1 = PillarShape(target, left, leftSplit, true, axis, parts);
                    var left2 = PillarShape(target, left, leftSplit, false, axis, parts);
                    var right1 = PillarShape(target, right, rightSplit, true, axis + half, parts);
                    var right2 = PillarShape(target, right, rightSplit, false, axis + half, parts);

                    if (!ExchangeMatches(left1, left2, leftHalf, exporter))
                    {
                        continue;
                    }

                    if (!ExchangeMatches(right1, right2, rightHalf, exporter))
                    {
                        continue;
                    }

                    if (!ExchangeMatches(leftHalf, rightHalf, target, exporter))
                    {
                        continue;
                    }

                    string direction = Display(left, leftSplit, right, rightSplit);

                    var steps = new List<ProductionStep>
                    {
                        new ProductionStep("制作左侧两个带柱子的半半边", left1),
                        new ProductionStep("制作右侧两个带柱子的半半边", right1),
                        new ProductionStep("交换得到左半边", leftHalf),
                        new ProductionStep("交换得到右半边", rightHalf),
                        new ProductionStep("交换得到目标图形", target),
                        new ProductionStep("顶针与堆叠（待接入）", null)
                    };

                    return new ProductionPathResult(
                        true,
                        new ProductionPath(
                            axis,
                            leftSplit,
                            rightSplit,
                            direction,
                            halfResult.LegalDirections.Contains(direction),
                            steps),
                        null);
                }
            }
        }

        return new ProductionPathResult(false, null, "没有找到可行的制作路径。");
    }

    private static bool ExchangeMatches(
        ShapeDocument first,
        ShapeDocument second,
        ShapeDocument expected,
        ShortCodeExporter exporter)
    {
        var result = ShapeExchange.Exchange(first, second);

        if (result.First is null || result.Second is null)
        {
            return false;
        }

        string expectedCode = exporter.Export(expected);

        return exporter.Export(result.First) == expectedCode ||
               exporter.Export(result.Second) == expectedCode;
    }

    /// <summary>只保留指定列的图形。</summary>
    private static ShapeDocument Slice(
        ShapeDocument source,
        int[] columns)
    {
        var shape = new ShapeDocument(source.LayerCount, source.PartCount);

        for (int layer = 0; layer < source.LayerCount; layer++)
        {
            foreach (int column in columns)
            {
                shape[layer, column] = source[layer, column].Clone();
            }
        }

        return shape;
    }

    /// <summary>
    /// 构造带柱子的半半边：first=true 时包含左柱和前 split 列，否则是后段与右柱。
    /// </summary>
    private static ShapeDocument PillarShape(
        ShapeDocument source,
        int[] columns,
        int split,
        bool first,
        int pillarAnchor,
        int parts)
    {
        int layers = source.LayerCount;
        int half = columns.Length;

        var shape = new ShapeDocument(layers, parts);

        int leftPillar = (pillarAnchor - 1 + parts) % parts;
        int rightPillar = (pillarAnchor + half) % parts;

        int start = first ? 0 : split;
        int count = first ? split : half - split;

        for (int index = 0; index < count; index++)
        {
            int column = columns[start + index];

            for (int layer = 0; layer < layers; layer++)
            {
                shape[layer, column] = source[layer, column].Clone();
            }
        }

        int pillar = first ? leftPillar : rightPillar;

        for (int layer = 0; layer < layers; layer++)
        {
            shape[layer, pillar] = ShapeCell.Shape('C', 'u');
        }

        return shape;
    }

    private static int[] Columns(
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

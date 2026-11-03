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
/// 每个方向要求两个半边稳定，且两侧在各自切点处的两个「带柱子子图形」也稳定。
/// 柱子为满层普通形状，只作固定边界。
/// </summary>
public static class HalfStabilityAnalyzer
{
    public static HalfStabilityResult Analyze(
        ShapeDocument document,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        int parts = document.PartCount;
        int layers = document.LayerCount;
        int half = parts / 2;

        if (half < 2 || parts % 2 != 0)
        {
            return new HalfStabilityResult(parts, 0, Array.Empty<string>(), Array.Empty<ExchangeDirection>());
        }

        var halves = new bool[parts / 2, 2];
        var subShapes = new Dictionary<string, bool>();

        var legal = new List<string>();

        var directionList = new List<ExchangeDirection>();

        for (int axis = 0; axis < parts / 2; axis++)
        {
            int[] left = Contiguous(axis, half, parts);
            int[] right = Contiguous(axis + half, half, parts);

            halves[axis, 0] = IsStable(document, left, mode);
            halves[axis, 1] = IsStable(document, right, mode);

            for (int i = 1; i < half; i++)
            {
                for (int j = 1; j < half; j++)
                {
                    bool legalDirection =
                        halves[axis, 0] &&
                        halves[axis, 1] &&
                        SubStable(document, axis, left, i, true, mode) &&
                        SubStable(document, axis, left, i, false, mode) &&
                        SubStable(document, axis + half, right, j, true, mode) &&
                        SubStable(document, axis + half, right, j, false, mode);

                    string text = Display(left, i, right, j);

                    directionList.Add(new ExchangeDirection(axis, i, j, text, legalDirection));

                    if (legalDirection)
                    {
                        legal.Add(text);
                    }
                }
            }
        }

        int directions = (parts / 2) * (half - 1) * (half - 1);

        return new HalfStabilityResult(parts, directions, legal, directionList);
    }

    /// <summary>
    /// 带柱子的子图形是否稳定。前半段包含左柱，后半段包含右柱。
    /// </summary>
    private static bool SubStable(
        ShapeDocument document,
        int axis,
        int[] halfColumns,
        int split,
        bool firstHalf,
        CollapseMode mode)
    {
        int parts = document.PartCount;
        int layers = document.LayerCount;
        int half = halfColumns.Length;

        int[] columns;

        if (firstHalf)
        {
            columns = new int[split + 1];
            columns[0] = (axis - 1 + parts) % parts;

            for (int index = 0; index < split; index++)
            {
                columns[index + 1] = halfColumns[index];
            }
        }
        else
        {
            int rest = half - split;
            columns = new int[rest + 1];

            for (int index = 0; index < rest; index++)
            {
                columns[index] = halfColumns[split + index];
            }

            columns[rest] = (axis + half) % parts;
        }

        var shape = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            for (int index = 0; index < columns.Length; index++)
            {
                int column = columns[index];

                bool isPillar = index == 0 || index == columns.Length - 1;

                shape[layer, column] = isPillar
                    ? ShapeCell.Shape('C', 'u')
                    : document[layer, column];
            }
        }

        return IsUnchanged(shape, mode);
    }

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
                shape[layer, column] = document[layer, column];
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

        var exporter = new Services.ShortCodeExporter();

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

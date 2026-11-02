namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 单个「角」（竖式图的一列）的判定结果。
/// </summary>
public sealed record AngleResult(
    int Part,
    bool IsLegal,
    IReadOnlyList<string> Failures);

/// <summary>
/// 整个图形的分析结果。
/// </summary>
public sealed record ShapeAnalysisResult(
    int PartCount,
    int LayerCount,
    IReadOnlyList<AngleResult> Angles)
{
    public int LegalCount => Angles.Count(angle => angle.IsLegal);

    public bool IsFullyLegal => LegalCount == Angles.Count;
}

/// <summary>
/// 角合法性判定。
///
/// 角 = 竖式图的一列。四条规则：
/// 一、顶针下方相邻非空（顶针在最底层时合法）；
/// 二、晶体下方存在顶针时，该顶针要么在最底层，要么下方全是顶针；
/// 三、同列两层晶体之间若存在空层，则中间至少两个非空格，且下方晶体正上方非空；
/// 四、最低晶体要么在最底层，要么下方有普通形状，要么下方全是顶针。
/// </summary>
public static class ShapeAngleAnalyzer
{
    public const string RuleOne = "规则一：顶针下方相邻格为空。";

    public const string RuleTwo = "规则二：晶体下方的顶针没有顶到底。";

    public const string RuleThree = "规则三：两层晶体之间的非空格少于两个，或下方晶体上方为空。";

    public const string RuleFour = "规则四：最低晶体下方既不是普通形状，也不是连续的顶针。";

    public static ShapeAnalysisResult Analyze(
        Models.ShapeDocument document)
    {
        var angles = new List<AngleResult>();

        for (int part = 0; part < document.PartCount; part++)
        {
            angles.Add(AnalyzeAngle(document, part));
        }

        return new ShapeAnalysisResult(
            document.PartCount,
            document.LayerCount,
            angles);
    }

    public static AngleResult AnalyzeAngle(
        Models.ShapeDocument document,
        int part)
    {
        var failures = new List<string>();

        int layers = document.LayerCount;

        bool IsEmpty(int layer) =>
            layer < 0 ||
            layer >= layers ||
            document[layer, part].Type == Models.CellType.Empty;

        bool IsPin(int layer) =>
            layer >= 0 &&
            layer < layers &&
            document[layer, part].Type == Models.CellType.Pin;

        bool IsCrystal(int layer) =>
            layer >= 0 &&
            layer < layers &&
            document[layer, part].Type == Models.CellType.Crystal;

        bool IsShape(int layer) =>
            layer >= 0 &&
            layer < layers &&
            document[layer, part].Type == Models.CellType.Shape;

        // 规则一：顶针下方相邻必须非空（最底层豁免）
        for (int layer = 0; layer < layers; layer++)
        {
            if (!IsPin(layer) || layer == 0)
            {
                continue;
            }

            if (IsEmpty(layer - 1))
            {
                failures.Add($"第 {layer + 1} 层顶针下方为空（{RuleOne}）");
            }
        }

        // 规则二：晶体下方若有顶针，该顶针必须顶到底
        for (int layer = 0; layer < layers; layer++)
        {
            if (!IsCrystal(layer))
            {
                continue;
            }

            for (int below = 0; below < layer; below++)
            {
                if (!IsPin(below) || below == 0)
                {
                    continue;
                }

                bool allPins = true;

                for (int deeper = 0; deeper < below; deeper++)
                {
                    if (!IsPin(deeper))
                    {
                        allPins = false;
                        break;
                    }
                }

                if (!allPins)
                {
                    failures.Add(
                        $"第 {layer + 1} 层晶体下方第 {below + 1} 层顶针未顶到底（{RuleTwo}）");
                }
            }
        }

        // 规则三：两层晶体之间有空洞时，中间至少两个非空格，且下方晶体上方非空
        for (int lower = 0; lower < layers; lower++)
        {
            if (!IsCrystal(lower))
            {
                continue;
            }

            for (int upper = lower + 1; upper < layers; upper++)
            {
                if (!IsCrystal(upper))
                {
                    continue;
                }

                bool hasGap = false;
                int filled = 0;

                for (int middle = lower + 1; middle < upper; middle++)
                {
                    if (IsEmpty(middle))
                    {
                        hasGap = true;
                    }
                    else
                    {
                        filled++;
                    }
                }

                if (!hasGap)
                {
                    continue;
                }

                if (filled < 2)
                {
                    failures.Add(
                        $"第 {lower + 1} 层与第 {upper + 1} 层晶体之间只有 {filled} 个非空格（{RuleThree}）");

                    continue;
                }

                if (IsEmpty(lower + 1))
                {
                    failures.Add(
                        $"第 {lower + 1} 层晶体正上方为空（{RuleThree}）");
                }
            }
        }

        // 规则四：最低晶体要么在最底层，要么下方有普通形状，要么下方全是顶针
        int lowestCrystal = -1;

        for (int layer = 0; layer < layers; layer++)
        {
            if (IsCrystal(layer))
            {
                lowestCrystal = layer;
                break;
            }
        }

        if (lowestCrystal > 0)
        {
            bool hasShape = false;
            bool allPins = true;

            for (int below = 0; below < lowestCrystal; below++)
            {
                if (IsShape(below))
                {
                    hasShape = true;
                }

                if (!IsPin(below))
                {
                    allPins = false;
                }
            }

            if (!hasShape && !allPins)
            {
                failures.Add(
                    $"第 {lowestCrystal + 1} 层晶体下方既无普通形状也非连续顶针（{RuleFour}）");
            }
        }

        return new AngleResult(part, failures.Count == 0, failures);
    }
}

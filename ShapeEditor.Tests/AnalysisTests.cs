using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 角合法性判定。
/// </summary>
internal static class AnalysisTests
{
    public static void LegalAngleWithPinOnBottom()
    {
        var document = new ShapeDocument(3, 2);

        document[0, 0] = ShapeCell.Pin();
        document[1, 0] = ShapeCell.Shape('C', 'u');
        document[2, 0] = ShapeCell.Shape('C', 'u');

        document[0, 1] = ShapeCell.Shape('C', 'u');
        document[1, 1] = ShapeCell.Shape('C', 'u');
        document[2, 1] = ShapeCell.Shape('C', 'u');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.True(angle.IsLegal);
    }

    public static void PinWithEmptyBelowIsIllegal()
    {
        var document = new ShapeDocument(3, 1);

        document[1, 0] = ShapeCell.Pin();
        document[2, 0] = ShapeCell.Shape('C', 'u');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.False(angle.IsLegal);
    }

    public static void PinUnderCrystalMustReachBottom()
    {
        var document = new ShapeDocument(4, 1);

        document[0, 0] = ShapeCell.Shape('C', 'u');
        document[1, 0] = ShapeCell.Pin();
        document[2, 0] = ShapeCell.Shape('C', 'u');
        document[3, 0] = ShapeCell.Crystal('r');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.False(angle.IsLegal);
    }

    public static void CrystalGapNeedsTwoFilledCells()
    {
        var document = new ShapeDocument(5, 1);

        document[0, 0] = ShapeCell.Shape('C', 'u');
        document[1, 0] = ShapeCell.Crystal('r');
        document[2, 0] = ShapeCell.Empty();
        document[3, 0] = ShapeCell.Shape('C', 'u');
        document[4, 0] = ShapeCell.Crystal('r');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.False(angle.IsLegal);
    }

    public static void CrystalGapWithTwoFilledCellsIsLegal()
    {
        var document = new ShapeDocument(6, 1);

        document[0, 0] = ShapeCell.Shape('C', 'u');
        document[1, 0] = ShapeCell.Crystal('r');
        document[2, 0] = ShapeCell.Shape('C', 'u');
        document[3, 0] = ShapeCell.Shape('C', 'u');
        document[4, 0] = ShapeCell.Empty();
        document[5, 0] = ShapeCell.Crystal('r');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.True(angle.IsLegal);
    }

    public static void LowestCrystalNeedsSupport()
    {
        var document = new ShapeDocument(3, 1);

        document[0, 0] = ShapeCell.Empty();
        document[1, 0] = ShapeCell.Empty();
        document[2, 0] = ShapeCell.Crystal('r');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.False(angle.IsLegal);
    }

    public static void LowestCrystalOnPinsIsLegal()
    {
        var document = new ShapeDocument(3, 1);

        document[0, 0] = ShapeCell.Pin();
        document[1, 0] = ShapeCell.Pin();
        document[2, 0] = ShapeCell.Crystal('r');

        AngleResult angle = ShapeAngleAnalyzer.AnalyzeAngle(document, 0);

        Assert.True(angle.IsLegal);
    }

    public static void FilledColumnIsStable()
    {
        var document = new ShapeDocument(3, 2);

        for (int layer = 0; layer < 3; layer++)
        {
            for (int part = 0; part < 2; part++)
            {
                document[layer, part] = ShapeCell.Shape('C', 'u');
            }
        }

        Assert.True(ShapeAngleAnalyzer.IsStable(document, ShapeEditor.Core.Physics.CollapseMode.IncludeCycles));
        Assert.True(ShapeAngleAnalyzer.IsStable(document, ShapeEditor.Core.Physics.CollapseMode.FloorOnly));
    }

    public static void FloatingCrystalIsUnstable()
    {
        var document = new ShapeDocument(3, 2);

        document[2, 0] = ShapeCell.Crystal('r');

        Assert.False(ShapeAngleAnalyzer.IsStable(document, ShapeEditor.Core.Physics.CollapseMode.FloorOnly));
    }
    public static void HalfStabilityOnSolidBlock()
    {
        var document = new ShapeDocument(2, 4);

        for (int layer = 0; layer < 2; layer++)
        {
            for (int part = 0; part < 4; part++)
            {
                document[layer, part] = ShapeCell.Shape('C', 'u');
            }
        }

        HalfStabilityResult result = HalfStabilityAnalyzer.Analyze(
            document,
            ShapeEditor.Core.Physics.CollapseMode.FloorOnly);

        Assert.Equal(2, result.DirectionCount);
        Assert.True(result.IsExchangeable);
    }

    public static void HalfStabilityRejectsFloatingShape()
    {
        var document = new ShapeDocument(3, 4);

        document[2, 0] = ShapeCell.Shape('C', 'u');

        HalfStabilityResult result = HalfStabilityAnalyzer.Analyze(
            document,
            ShapeEditor.Core.Physics.CollapseMode.FloorOnly);

        Assert.False(result.IsExchangeable);
    }
    public static void PropertiesDetectGaps()
    {
        var document = new ShapeDocument(4, 2);

        document[0, 0] = ShapeCell.Shape('C', 'u');
        document[1, 0] = ShapeCell.Empty();
        document[2, 0] = ShapeCell.Shape('C', 'u');
        document[3, 0] = ShapeCell.Crystal('r');

        document[0, 1] = ShapeCell.Pin();
        document[1, 1] = ShapeCell.Shape('C', 'u');
        document[2, 1] = ShapeCell.Shape('C', 'u');
        document[3, 1] = ShapeCell.Crystal('b');

        ShapeProperty properties = ShapePropertyAnalyzer.Analyze(document, false, false);

        Assert.True((properties & ShapeProperty.ContainsCrystal) != 0);
        Assert.True((properties & ShapeProperty.ContainsPin) != 0);
        Assert.True((properties & ShapeProperty.GapBelowShape) != 0);
        Assert.True((properties & ShapeProperty.MixedCrystalColorsSameLayer) != 0);
    }

    public static void PropertiesDetectCrystalGap()
    {
        var document = new ShapeDocument(3, 2);

        document[0, 0] = ShapeCell.Shape('C', 'u');
        document[1, 0] = ShapeCell.Empty();
        document[2, 0] = ShapeCell.Crystal('r');

        document[0, 1] = ShapeCell.Shape('C', 'u');
        document[1, 1] = ShapeCell.Shape('C', 'u');
        document[2, 1] = ShapeCell.Shape('C', 'u');

        ShapeProperty properties = ShapePropertyAnalyzer.Analyze(document, false, false);

        Assert.True((properties & ShapeProperty.GapBelowCrystal) != 0);
        Assert.True((properties & ShapeProperty.Stable) == 0);
    }
    public static void PropertiesMarkCycleSupport()
    {
        var parser = new ShapeEditor.Core.Services.ShortCodeParser();

        var parsed = parser.Parse(
            "------Cu:--------:----Cucr:----P-cr:----Cucr",
            5,
            4);

        Assert.True(parsed.Success);

        ShapeProperty withCycles = ShapePropertyAnalyzer.Analyze(
            parsed.Document!,
            false,
            false,
            ShapeEditor.Core.Physics.CollapseMode.IncludeCycles);

        Assert.True((withCycles & ShapeProperty.CycleSupported) != 0);

        ShapeProperty floorOnly = ShapePropertyAnalyzer.Analyze(
            parsed.Document!,
            false,
            false,
            ShapeEditor.Core.Physics.CollapseMode.FloorOnly);

        Assert.True((floorOnly & ShapeProperty.CycleSupported) == 0);
    }
    public static void ExchangeKeepsPartCount()
    {
        var first = new ShapeDocument(2, 4);
        var second = new ShapeDocument(2, 4);

        for (int layer = 0; layer < 2; layer++)
        {
            for (int part = 0; part < 4; part++)
            {
                first[layer, part] = ShapeCell.Shape('C', 'u');
                second[layer, part] = ShapeCell.Crystal('r');
            }
        }

        var result = ShapeExchange.Exchange(first, second);

        Assert.True(result.Error is null);
        Assert.Equal(4, result.First!.PartCount);
        Assert.Equal(4, result.Second!.PartCount);
    }

    public static void ExchangeRejectsDifferentPartCount()
    {
        var first = new ShapeDocument(2, 4);
        var second = new ShapeDocument(2, 6);

        var result = ShapeExchange.Exchange(first, second);

        Assert.True(result.Error is not null);
    }
    public static void ProductionPathSmokeTest()
    {
        var document = new ShapeDocument(2, 4);

        for (int layer = 0; layer < 2; layer++)
        {
            for (int part = 0; part < 4; part++)
            {
                document[layer, part] = ShapeCell.Shape('C', 'u');
            }
        }

        ProductionPathResult result = ProductionPathParser.Parse(document);

        System.Console.WriteLine(
            "producible=" + result.IsProducible +
            " error=" + (result.Error ?? "-") +
            " direction=" + (result.Path?.Direction ?? "-"));

        Assert.True(result.IsProducible || result.Error is not null);
    }

    /// <summary>
    /// 规格 §6.1：6 象限单层目标，方向 (12+3)+(4+56) 可以构造出目标。
    ///
    /// 单层全晶体目标对任意切点都成立，而规格 §5 的遍历从 ls = rs = 1 开始，
    /// 因此解析器返回的是第一个可行方向 (1+23)+(4+56)；
    /// §6.1 记录的方向同样是合法方向（见下面的断言）。
    /// </summary>
    public static void ProductionPathSixQuadrants()
    {
        var parser = new ShortCodeParser();
        ParseResult parsed = parser.Parse("CrCgCbCcCmCy", 1, 6);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ShapeDocument target = parsed.Document!;

        ProductionPathResult result = ProductionPathParser.Parse(target);

        Assert.True(result.IsProducible, result.Error ?? "6 象限样例应可制作");
        Assert.True(result.Path is not null);

        ProductionPath path = result.Path!;

        Assert.Equal(1, path.LeftSplit);
        Assert.Equal(1, path.RightSplit);
        Assert.Equal("(1+23)+(4+56)", path.Direction);
        Assert.True(path.DirectionLegal, "解析出的方向应合法");

        var exporter = new ShortCodeExporter();

        // 两个半边与规格 §4 的自检公式一致。
        Assert.Equal("CrCgCbCu--Cu", exporter.Export(path.Steps[4].Shape!));
        Assert.Equal("Cu--CuCcCmCy", exporter.Export(path.Steps[5].Shape!));

        // 末端产物就是目标。
        Assert.Equal(
            exporter.Export(target),
            exporter.Export(path.Steps[6].Shape!));

        // §6.1 记录的方向也应被判为合法。
        HalfStabilityResult directions = HalfStabilityAnalyzer.Analyze(target);

        Assert.True(
            directions.LegalDirections.Contains("(12+3)+(4+56)"),
            "规格 §6.1 的方向应当是合法方向");
    }

    /// <summary>
    /// 规格 §6.2：8 象限单层目标，方向 (12+34)+(56+78) 可以构造出目标。
    /// 与 6 象限同理，§5 的遍历先返回第一个可行方向。
    /// </summary>
    public static void ProductionPathEightQuadrants()
    {
        var parser = new ShortCodeParser();
        ParseResult parsed = parser.Parse("CrCgCbCcCmCyCwCk", 1, 8);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ShapeDocument target = parsed.Document!;

        ProductionPathResult result = ProductionPathParser.Parse(target);

        Assert.True(result.IsProducible, result.Error ?? "8 象限样例应可制作");
        Assert.True(result.Path is not null);

        ProductionPath path = result.Path!;

        Assert.Equal(1, path.LeftSplit);
        Assert.Equal(1, path.RightSplit);
        Assert.Equal("(1+234)+(5+678)", path.Direction);
        Assert.True(path.DirectionLegal, "解析出的方向应合法");

        var exporter = new ShortCodeExporter();

        Assert.Equal("CrCgCbCcCu----Cu", exporter.Export(path.Steps[4].Shape!));
        Assert.Equal("Cu----CuCmCyCwCk", exporter.Export(path.Steps[5].Shape!));

        Assert.Equal(
            exporter.Export(target),
            exporter.Export(path.Steps[6].Shape!));

        HalfStabilityResult directions = HalfStabilityAnalyzer.Analyze(target);

        Assert.True(
            directions.LegalDirections.Contains("(12+34)+(56+78)"),
            "规格 §6.2 的方向应当是合法方向");
    }

    /// <summary>
    /// 规格 §6.3：n = 4 只有 ls = rs = 1 一个方向，仍然成立。
    /// </summary>
    public static void ProductionPathFourQuadrants()
    {
        var parser = new ShortCodeParser();
        ParseResult parsed = parser.Parse("CrCgCbCc", 1, 4);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ProductionPathResult result = ProductionPathParser.Parse(parsed.Document!);

        Assert.True(result.IsProducible, result.Error ?? "4 象限样例应可制作");
        Assert.True(result.Path is not null);

        Assert.Equal(1, result.Path!.LeftSplit);
        Assert.Equal(1, result.Path.RightSplit);
        Assert.Equal("(1+2)+(3+4)", result.Path.Direction);
    }

    /// <summary>
    /// 规格 §6.3：n = 2 时 half = 1，没有合法切点，不可制作。
    /// </summary>
    public static void ProductionPathTwoQuadrantsIsImpossible()
    {
        var document = new ShapeDocument(1, 2);

        document[0, 0] = ShapeCell.Crystal('r');
        document[0, 1] = ShapeCell.Crystal('g');

        ProductionPathResult result = ProductionPathParser.Parse(document);

        Assert.False(result.IsProducible, "n = 2 不应可制作");
        Assert.True(result.Path is null);
        Assert.True(result.Error is not null);
    }

    /// <summary>
    /// 用户样例（8 象限 9 层）：轴 0 的 9 个方向全部不合法。
    ///
    /// 修复前，第二片会把首列内容也覆盖成柱子（P2/P4），从而掩盖悬空结构，
    /// 轴 0 的 9 个方向全部被误判为合法；修复后每个方向都因 P1/P2 不稳定而失败。
    /// </summary>
    public static void UserSampleHasNoLegalAxisZeroDirection()
    {
        const string code =
            "Cu--------------:----------------:crCuCu----------:cr--Cu----------:" +
            "crCuCu----------:----------------:--crCuCu--------:--cr--Cu--------:" +
            "--crCuCu--------";

        var parser = new ShortCodeParser();
        ParseResult parsed = parser.Parse(code, 9, 8);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        HalfStabilityResult result = HalfStabilityAnalyzer.Analyze(
            parsed.Document!,
            ShapeEditor.Core.Physics.CollapseMode.IncludeCycles);

        int axisZeroDirections = result.Directions.Count(
            direction => direction.Axis == 0);

        int axisZeroLegal = result.Directions.Count(
            direction => direction.Axis == 0 && direction.IsLegal);

        Assert.Equal(9, axisZeroDirections);
        Assert.Equal(0, axisZeroLegal);
    }
    public static void AnalyzeReportsPerAngle()
    {
        var document = new ShapeDocument(2, 2);

        document[0, 0] = ShapeCell.Pin();
        document[1, 0] = ShapeCell.Shape('C', 'u');

        document[0, 1] = ShapeCell.Empty();
        document[1, 1] = ShapeCell.Shape('C', 'u');

        ShapeAnalysisResult result = ShapeAngleAnalyzer.Analyze(document);

        Assert.Equal(2, result.PartCount);
        Assert.Equal(2, result.LegalCount);
    }
}

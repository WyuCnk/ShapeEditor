using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;

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

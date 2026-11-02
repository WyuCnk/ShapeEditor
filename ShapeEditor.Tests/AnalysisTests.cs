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

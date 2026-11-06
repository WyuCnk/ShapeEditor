using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
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
        // 步骤里包含预旋转与交换后转回，按标题取「交换得到左/右半边」这一结果步。
        ProductionStep leftHalfStep = path.Steps.First(
            step => step.Title == "交换得到左半边");

        ProductionStep rightHalfStep = path.Steps.First(
            step => step.Title == "交换得到右半边");

        Assert.Equal("CrCgCbCu--Cu", exporter.Export(leftHalfStep.Shape!));
        Assert.Equal("Cu--CuCcCmCy", exporter.Export(rightHalfStep.Shape!));

        // 末端产物就是目标。
        ProductionStep targetStep = path.Steps.First(
            step => step.Kind == ProductionStepKind.Target);

        Assert.Equal(
            exporter.Export(target),
            exporter.Export(targetStep.Shape!));

        // 预旋转：P1/P2 顺转 half−ls = 2，P3/P4 逆转 rs = 1。
        Assert.Equal(
            2,
            path.Steps.Count(step =>
                step.Kind == ProductionStepKind.Rotate &&
                step.RotationDirection == RotationDirection.Clockwise &&
                step.RotationCount == 2));

        Assert.Equal(
            2,
            path.Steps.Count(step =>
                step.Kind == ProductionStepKind.Rotate &&
                step.RotationDirection == RotationDirection.CounterClockwise &&
                step.RotationCount == 1));

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

        ProductionStep leftHalfStep = path.Steps.First(
            step => step.Title == "交换得到左半边");

        ProductionStep rightHalfStep = path.Steps.First(
            step => step.Title == "交换得到右半边");

        Assert.Equal("CrCgCbCcCu----Cu", exporter.Export(leftHalfStep.Shape!));
        Assert.Equal("Cu----CuCmCyCwCk", exporter.Export(rightHalfStep.Shape!));

        ProductionStep targetStep = path.Steps.First(
            step => step.Kind == ProductionStepKind.Target);

        Assert.Equal(
            exporter.Export(target),
            exporter.Export(targetStep.Shape!));

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
    /// 图结构：4 象限目标应整理成 5 层、16 个节点、15 条边。
    ///
    /// 每层节点数：4（带柱半半边）、4（预旋转）、4（交换：2 个半边 + 2 个废料）、
    /// 2（转回后半边）、2（目标 + 废料）。废料标题固定「废料」且必须带图形。
    /// </summary>
    public static void ProductionGraphFourQuadrants()
    {
        var parser = new ShortCodeParser();
        ParseResult parsed = parser.Parse("CrCgCbCc", 1, 4);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ShapeDocument target = parsed.Document!;

        ProductionPathResult result = ProductionPathParser.Parse(target);

        Assert.True(result.IsProducible, result.Error ?? "4 象限样例应可制作");
        Assert.True(result.Path is not null);

        ProductionPath path = result.Path!;

        Assert.Equal(16, path.Nodes.Count);
        Assert.Equal(15, path.Edges.Count);

        Assert.Equal(4, path.Nodes.Count(node => node.Row == 1));
        Assert.Equal(4, path.Nodes.Count(node => node.Row == 2));
        Assert.Equal(4, path.Nodes.Count(node => node.Row == 3));
        Assert.Equal(2, path.Nodes.Count(node => node.Row == 4));
        Assert.Equal(2, path.Nodes.Count(node => node.Row == 5));

        Assert.Equal(
            2,
            path.Nodes.Count(node =>
                node.Row == 3 && node.Kind == ProductionNodeKind.Exchange));

        Assert.Equal(
            2,
            path.Nodes.Count(node =>
                node.Row == 3 && node.Kind == ProductionNodeKind.Waste));

        // 交换的两个输出都保留：废料标题固定，且带可绘制的图形。
        Assert.Equal(
            3,
            path.Nodes.Count(node => node.Kind == ProductionNodeKind.Waste));

        Assert.True(
            path.Nodes
                .Where(node => node.Kind == ProductionNodeKind.Waste)
                .All(node => node.Title == "废料"),
            "废料节点标题必须固定为「废料」");

        Assert.True(
            path.Nodes
                .Where(node => node.Kind == ProductionNodeKind.Waste)
                .All(node => node.Shape is not null),
            "废料节点也必须带图形");

        ProductionNode targetNode = path.Nodes.Single(
            node => node.Kind == ProductionNodeKind.Target);

        Assert.Equal(5, targetNode.Row);

        var exporter = new ShortCodeExporter();

        Assert.Equal(
            exporter.Export(target),
            exporter.Export(targetNode.Shape!));

        // 关键连接：P1/P2 → 左交换，P3/P4 → 右交换，
        // 交换分别输出转回后半边与废料，两个转回后半边再汇入目标。
        Assert.True(HasEdge(path, "rotated-p1", "exchange-left"));
        Assert.True(HasEdge(path, "rotated-p2", "exchange-left"));
        Assert.True(HasEdge(path, "rotated-p3", "exchange-right"));
        Assert.True(HasEdge(path, "rotated-p4", "exchange-right"));
        Assert.True(HasEdge(path, "exchange-left", "rotated-left"));
        Assert.True(HasEdge(path, "exchange-left", "waste-left"));
        Assert.True(HasEdge(path, "exchange-right", "rotated-right"));
        Assert.True(HasEdge(path, "exchange-right", "waste-right"));
        Assert.True(HasEdge(path, "rotated-left", "target"));
        Assert.True(HasEdge(path, "rotated-right", "target"));
        Assert.True(HasEdge(path, "target", "waste-final"));

        static bool HasEdge(
            ProductionPath path,
            string fromId,
            string toId)
        {
            return path.Edges.Any(edge =>
                edge.FromId == fromId &&
                edge.ToId == toId);
        }
    }

    /// <summary>
    /// n = 2 的可分判据（定理 5）：两列都必须通过孤立列判据（定理 4）。
    /// 规格 §7 的反例：底层 <c>T.</c>、顶层 <c>TT</c>，右列 <c>.T</c> 会失去左列支撑下落，
    /// 因此不可分；两列本身都通过五项条件，所以只能报「未覆盖」，不能报「不可制作」。
    /// </summary>
    public static void ProductionPathTwoQuadrantsIsImpossible()
    {
        var document = new ShapeDocument(2, 2);

        document[0, 0] = ShapeCell.Shape('C', 'u');
        document[1, 0] = ShapeCell.Shape('C', 'u');

        document[0, 1] = ShapeCell.Empty();
        document[1, 1] = ShapeCell.Shape('C', 'u');

        ProductionPathResult result = ProductionPathParser.Parse(document);

        Assert.False(result.IsProducible, "n = 2 的反例不应可制作");
        Assert.True(result.Path is null);
        Assert.True(result.Error is not null);
        Assert.Equal(PipelineOutcome.Uncovered, result.Outcome);
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

    /// <summary>
    /// 优化结果一致：n = 8 规格样例（单层 8 色晶体）。
    ///
    /// 数值来自优化前的实现（加入旋转归一化缓存之前）：
    /// 方向数 36，且 36 个方向全部合法；解析器返回第一个可行方向 (1+234)+(5+678)。
    /// 缓存只应缩短计算，不应改变任何判定。
    /// </summary>
    public static void OptimizedHalfStabilityMatchesRecordedBaseline()
    {
        var parser = new ShortCodeParser();
        ParseResult parsed = parser.Parse("CrCgCbCcCmCyCwCk", 1, 8);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ShapeDocument target = parsed.Document!;

        HalfStabilityResult analysis = HalfStabilityAnalyzer.Analyze(target);

        Assert.Equal(36, analysis.DirectionCount);
        Assert.Equal(36, analysis.Directions.Count);
        Assert.Equal(36, analysis.LegalDirections.Count);

        string[] expected =
        {
            "(1+234)+(5+678)",
            "(1+234)+(56+78)",
            "(1+234)+(567+8)",
            "(12+34)+(5+678)",
            "(12+34)+(56+78)",
            "(12+34)+(567+8)",
            "(123+4)+(5+678)",
            "(123+4)+(56+78)",
            "(123+4)+(567+8)",

            "(2+345)+(6+781)",
            "(2+345)+(67+81)",
            "(2+345)+(678+1)",
            "(23+45)+(6+781)",
            "(23+45)+(67+81)",
            "(23+45)+(678+1)",
            "(234+5)+(6+781)",
            "(234+5)+(67+81)",
            "(234+5)+(678+1)",

            "(3+456)+(7+812)",
            "(3+456)+(78+12)",
            "(3+456)+(781+2)",
            "(34+56)+(7+812)",
            "(34+56)+(78+12)",
            "(34+56)+(781+2)",
            "(345+6)+(7+812)",
            "(345+6)+(78+12)",
            "(345+6)+(781+2)",

            "(4+567)+(8+123)",
            "(4+567)+(81+23)",
            "(4+567)+(812+3)",
            "(45+67)+(8+123)",
            "(45+67)+(81+23)",
            "(45+67)+(812+3)",
            "(456+7)+(8+123)",
            "(456+7)+(81+23)",
            "(456+7)+(812+3)"
        };

        var actual = new HashSet<string>(
            analysis.LegalDirections,
            StringComparer.Ordinal);

        Assert.Equal(expected.Length, actual.Count);

        foreach (string direction in expected)
        {
            Assert.True(
                actual.Contains(direction),
                $"缺少合法方向 {direction}");
        }

        ProductionPathResult path = ProductionPathParser.Parse(target);

        Assert.True(path.IsProducible, path.Error ?? "8 象限样例应可制作");
        Assert.True(path.Path is not null);

        Assert.Equal("(1+234)+(5+678)", path.Path!.Direction);
        Assert.Equal(1, path.Path.LeftSplit);
        Assert.Equal(1, path.Path.RightSplit);
        Assert.True(path.Path.DirectionLegal);
    }

    /// <summary>
    /// n = 20 单层全晶体（可构造）：连续解析 3 次，单次平均耗时必须远低于
    /// 200ms。旋转归一化缓存把稳定性塌陷次数从 3260 次降到 19 次，
    /// 优化前实测约 38ms/次。
    /// </summary>
    public static void ProductionPathTwentyQuadrantsIsFast()
    {
        var parser = new ShortCodeParser();

        ParseResult parsed = parser.Parse(
            "CrCrCrCrCrCrCrCrCrCrCrCrCrCrCrCrCrCrCrCr",
            1,
            20);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ShapeDocument target = parsed.Document!;

        ProductionPathResult first = ProductionPathParser.Parse(target);

        Assert.True(first.IsProducible, first.Error ?? "n = 20 单层全晶体应可制作");

        // n > 9 时方向数字会产生歧义，整串必须用逗号分隔。
        Assert.True(
            first.Path!.Direction.Contains(','),
            $"n = 20 的方向应使用逗号分隔：{first.Path.Direction}");

        // 制作路径与半边稳定性表格必须使用同一套方向文本。
        Assert.True(
            first.Path.DirectionLegal,
            $"n = 20 的方向应与半边稳定性分析一致：{first.Path.Direction}");

        // 预旋转量与交换后转回都暴露在步骤里，且取较短方向（≤ n/2）。
        Assert.True(
            first.Path.Steps.Any(step =>
                step.Kind == ProductionStepKind.Rotate &&
                step.RotationDirection != RotationDirection.None &&
                step.RotationCount > 0 &&
                step.RotationCount <= target.PartCount / 2),
            "n = 20 的制作路径应包含合法旋转步骤");

        // 预热一次，避免首次 JIT 计入选时。
        ProductionPathParser.Parse(target);

        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int run = 0; run < 3; run++)
        {
            ProductionPathResult repeated = ProductionPathParser.Parse(target);

            Assert.True(
                repeated.IsProducible,
                repeated.Error ?? "n = 20 单层全晶体应可制作");
        }

        stopwatch.Stop();

        double average = stopwatch.Elapsed.TotalMilliseconds / 3;

        Assert.True(
            average < 200,
            $"n = 20 连续解析平均 {average:F2} ms，超过 200 ms");
    }
}

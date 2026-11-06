using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 结晶原语、角分类与角工序（构造 G/F）的测试。
/// </summary>
internal static class AngleWorkOrderTests
{
    private const string UserSampleCode =
        "Cu--------------:----------------:crCuCu----------:cr--Cu----------:" +
        "crCuCu----------:----------------:--crCuCu--------:--cr--Cu--------:" +
        "--crCuCu--------";

    #region 结晶

    /// <summary>
    /// 空格与顶针都变成指定颜色的晶体；普通形状、已有晶体（含颜色）不变。
    /// </summary>
    public static void CrystallizeFillsEmptyAndPin()
    {
        var document = new ShapeDocument(3, 4);

        document[0, 0] = ShapeCell.Empty();
        document[1, 0] = ShapeCell.Pin();
        document[2, 0] = ShapeCell.Shape('R', 'g');

        document[0, 1] = ShapeCell.Crystal('b');
        document[1, 1] = ShapeCell.Shape('C', 'u');
        document[2, 1] = ShapeCell.Empty();

        document[2, 3] = ShapeCell.Pin();

        PhysicsResult result = ShapePhysics.Crystallize(document, 'r');

        Assert.True(result.Success, result.Error ?? "结晶失败");

        ShapeDocument crystallized = result.Document!;

        Assert.Equal(3, crystallized.LayerCount);
        Assert.Equal(4, crystallized.PartCount);

        // 所有空格与顶针 -> 红色晶体
        foreach ((int layer, int part) in new[]
                 {
                     (0, 0), (1, 0), (2, 1), (2, 3)
                 })
        {
            Assert.Equal(
                CellType.Crystal,
                crystallized[layer, part].Type,
                $"({layer},{part}) 应变成晶体");

            Assert.Equal(
                'r',
                crystallized[layer, part].ColorCode,
                $"({layer},{part}) 的晶体颜色应为 r");
        }

        // 普通形状不变（类型、形状码、颜色）
        Assert.Equal(CellType.Shape, crystallized[2, 0].Type);
        Assert.Equal('R', crystallized[2, 0].ShapeCode);
        Assert.Equal('g', crystallized[2, 0].ColorCode);

        Assert.Equal(CellType.Shape, crystallized[1, 1].Type);
        Assert.Equal('C', crystallized[1, 1].ShapeCode);
        Assert.Equal('u', crystallized[1, 1].ColorCode);

        // 已有晶体不变：类型与颜色都保留原来的 b，而不是 r
        Assert.Equal(CellType.Crystal, crystallized[0, 1].Type);
        Assert.Equal('b', crystallized[0, 1].ColorCode);
    }

    /// <summary>
    /// 不修改入参；非法颜色返回失败。
    /// </summary>
    public static void CrystallizeIsPureAndValidatesColor()
    {
        var document = new ShapeDocument(2, 4);

        document[0, 0] = ShapeCell.Empty();
        document[1, 1] = ShapeCell.Pin();

        PhysicsResult result = ShapePhysics.Crystallize(document, 'g');

        Assert.True(result.Success, result.Error ?? "结晶失败");

        Assert.Equal(CellType.Empty, document[0, 0].Type);
        Assert.Equal(CellType.Pin, document[1, 1].Type);

        Assert.True(
            result.Document![0, 0].Type == CellType.Crystal,
            "输出应含晶体");

        PhysicsResult badColor = ShapePhysics.Crystallize(document, 'x');

        Assert.False(badColor.Success, "非法颜色应当失败");
        Assert.True(badColor.Error is not null);
    }

    #endregion

    #region 角分类

    /// <summary>
    /// 简单角：最高晶体以下全部非空；没有晶体；尾部留空都不影响分类。
    /// </summary>
    public static void ClassifySimpleAngles()
    {
        // Tcc：最高晶体在第 3 层，下面 T、c 都非空。
        Assert.Equal(
            AngleBuildKind.Simple,
            AngleClassifier.Classify(Column("Tcc", 4), 0),
            "Tcc 应为简单角");

        // c..：最高晶体在第 1 层，上方留空是尾部，不算缺口。
        Assert.Equal(
            AngleBuildKind.Simple,
            AngleClassifier.Classify(Column("c..", 4), 0),
            "c.. 应为简单角");

        // Pc：顶针在晶体下方，非空。
        Assert.Equal(
            AngleBuildKind.Simple,
            AngleClassifier.Classify(Column("Pc", 4), 0),
            "Pc 应为简单角");

        // 空列：没有晶体。
        Assert.Equal(
            AngleBuildKind.Simple,
            AngleClassifier.Classify(new ShapeDocument(4, 4), 2),
            "空列应为简单角");
    }

    /// <summary>
    /// 碎晶角：最高晶体下方存在空位。
    /// </summary>
    public static void ClassifyNeedsCrystalBreakAngles()
    {
        Assert.Equal(
            AngleBuildKind.NeedsCrystalBreak,
            AngleClassifier.Classify(Column("T.c", 4), 0),
            "T.c 应为碎晶角（晶体下方有空位）");

        Assert.Equal(
            AngleBuildKind.NeedsCrystalBreak,
            AngleClassifier.Classify(Column("cT.T.c", 6), 0),
            "cT.T.c 应为碎晶角");

        // 缺口在最高晶体正下方（用户样例的形态）。
        Assert.Equal(
            AngleBuildKind.NeedsCrystalBreak,
            AngleClassifier.Classify(Column("T.ccc", 6), 0),
            "T.ccc 应为碎晶角");
    }

    /// <summary>
    /// 用户的 8 象限 9 层样例：
    /// 第 1、2 列是碎晶角（晶体塔下方有空位），其余列没有晶体或最高晶体连续触地。
    /// </summary>
    public static void ClassifyUserSample()
    {
        ShapeDocument document = Parse(UserSampleCode, 9, 8);

        Assert.True(ShapeOps.IsStable(document), "用户样例应稳定");

        // 样例的列文本（底 -> 顶）：
        //   第 1 列 T.ccc....   —— 晶体塔 3..5 层，第 2 层空
        //   第 2 列 ..T.T.ccc  —— 晶体塔 7..9 层，第 6 层空
        //   第 3、4 列没有晶体；其余为空列。
        string[] expectedColumns =
        {
            "T.ccc....",
            "..T.T.ccc",
            "..TTT.T.T",
            "......TTT",
            ".........",
            ".........",
            ".........",
            "........."
        };

        AngleBuildKind[] expectedKinds =
        {
            AngleBuildKind.NeedsCrystalBreak,
            AngleBuildKind.NeedsCrystalBreak,
            AngleBuildKind.Simple,
            AngleBuildKind.Simple,
            AngleBuildKind.Simple,
            AngleBuildKind.Simple,
            AngleBuildKind.Simple,
            AngleBuildKind.Simple
        };

        for (int column = 0; column < 8; column++)
        {
            Assert.Equal(
                expectedColumns[column],
                ShapeSignature.ColumnText(document, column),
                $"第 {column} 列的列文本不符");

            Assert.Equal(
                expectedKinds[column],
                AngleClassifier.Classify(document, column),
                $"第 {column} 列的分类不符");
        }
    }

    #endregion

    #region 角工序：简单角

    /// <summary>
    /// 简单角只给一条说明，不造假步骤。
    /// </summary>
    public static void SimpleAngleHasOnlyNote()
    {
        ShapeDocument document = Parse(UserSampleCode, 9, 8);

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(document, 2, 9);

        Assert.Equal(AngleBuildKind.Simple, order.Kind);
        Assert.True(order.IsComplete, "简单角应直接完成");
        Assert.True(order.Note is null, "简单角不应有未实现说明");
        Assert.Equal(1, order.Steps.Count);
        Assert.Equal(AngleWorkStepKind.Note, order.Steps[0].Kind);
        Assert.True(order.Steps[0].Title.Contains("简单角"));
        Assert.True(order.Steps[0].Shape is null);
    }

    #endregion

    #region 角工序：构造 G（n = 4）

    /// <summary>
    /// 构造 G 的验证通过案例：目标列 <c>T.ccc</c>（晶体塔三连晶，正下方一层空、再下方是 T）。
    ///
    /// 前态是 <c>.Tccc</c> + 辅助列；Cut 打碎辅助列的晶体后，
    /// 晶体塔正下方的普通格落到第 1 层，缺口出现在第 2 层，正好得到目标列。
    /// </summary>
    public static void ConstructionGReplaysToTarget()
    {
        // 放在第 4 列（索引 3），顺便验证切割前的旋转。
        ShapeDocument target = FromColumns(4, 5, (3, "T.ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 3, 5);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.True(order.IsComplete, order.Note ?? "G 应验证通过");
        Assert.True(order.Note is null, order.Note ?? "G 不应有未实现说明");
        Assert.Equal(AngleConstruction.G, order.Construction);
        Assert.Equal(1, order.Rotation);

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Collapse,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        Assert.Equal(
            ".Tccc",
            ShapeSignature.ColumnText(preState, 3),
            "碎晶前的角应为 .Tccc");

        // 真实回放：旋转 -> Cut -> 转回。
        ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

        CutResult cut = ShapePhysics.Cut(rotated);

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

        Assert.Equal(
            "T.ccc",
            ShapeSignature.ColumnText(replayed, 3),
            "真实回放后的目标列应与目标一致");

        // Collapse 步的快照必须等于真实 Cut 的结果（Cut 自带一次下落）。
        Assert.True(
            ShapeOps.Same(
                replayed,
                Step(order, AngleWorkStepKind.Collapse).Shape),
            "Collapse 步应等于真实 Cut 出来的西半边");

        // Cut 步必须真的打碎了辅助列的晶体，且没有误伤目标列晶体。
        ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

        foreach (int aux in order.AuxiliaryColumns)
        {
            Assert.Equal(
                0,
                CountCrystals(cutStep, aux),
                $"辅助列 {aux} 的晶体应全部被打碎");
        }

        Assert.True(
            CountCrystals(cutStep, 3) == CountCrystals(preState, 3),
            "目标列的晶体塔在切割后必须完好");

        // 末步结晶会填掉缺口：这演示了为什么订单里不含末步 Crystallize。
        ShapeDocument overCrystallized = ShapePhysics
            .Crystallize(replayed, 'r')
            .Document!;

        Assert.Equal(
            CellType.Crystal,
            overCrystallized[1, 3].Type,
            "对下落结果再结晶会把第 2 层的缺口填成晶体");

        Assert.False(
            ShapeSignature.ColumnText(overCrystallized, 3) ==
            ShapeSignature.ColumnText(target, 3),
            "末步结晶会破坏目标列，因此不是工序的一步");

        Assert.True(
            order.Steps.Any(step =>
                step.Kind == AngleWorkStepKind.Note &&
                step.Title.Contains("结晶")),
            "订单应有说明解释为什么没有末步结晶");

        Assert.True(
            order.Limitations.Any(text => text.Contains("结晶")),
            "Limitations 应向 UI 说明结晶的前提");
    }

    /// <summary>
    /// 规格 §3 构造 G 的原始例子：<c>cT..Tccc → cT.T.ccc</c>。
    /// </summary>
    public static void ConstructionGMatchesSpecExample()
    {
        ShapeDocument target = FromColumns(4, 8, (0, "cT.T.ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8);

        Assert.True(order.IsComplete, order.Note ?? "规格 §3 的 G 例子应验证通过");
        Assert.Equal(AngleConstruction.G, order.Construction);
        Assert.Equal(0, order.Rotation);

        Assert.Equal(
            "cT..Tccc",
            ShapeSignature.ColumnText(
                Step(order, AngleWorkStepKind.PreState).Shape!,
                0),
            "规格 §3 的碎晶前态应为 cT..Tccc");

        ReplayAndVerifyColumn(order, target, 0);
    }

    #endregion

    #region 角工序：构造 F（n ≥ 6）

    /// <summary>
    /// 用户样例的两列碎晶角都用构造 F 验证通过。
    /// </summary>
    public static void ConstructionFHandlesUserSample()
    {
        ShapeDocument document = Parse(UserSampleCode, 9, 8);

        foreach (int column in new[] { 0, 1 })
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(document, column, 9);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(order.IsComplete, order.Note ?? $"第 {column} 列的 F 工序应验证通过");
            Assert.True(order.Note is null);
            Assert.Equal(AngleConstruction.F, order.Construction);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Collapse,
                AngleWorkStepKind.Note,
                AngleWorkStepKind.Target);

            ReplayAndVerifyColumn(order, document, column);
        }
    }

    /// <summary>
    /// 构造 F 的多层缺口案例（n = 6 与 n = 10）：目标列 <c>T..ccc</c>，
    /// 晶体塔在三连晶之上，缺口高两层，G 的单层下落做不了，F 一次降到第 1 层。
    /// </summary>
    public static void ConstructionFHandlesMultiLayerGap()
    {
        foreach (int parts in new[] { 6, 10 })
        {
            ShapeDocument target = FromColumns(parts, 6, (0, "T..ccc"));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"n = {parts} 的多层缺口 F 工序应验证通过");
            Assert.Equal(AngleConstruction.F, order.Construction);

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                "..Tccc",
                ShapeSignature.ColumnText(preState, 0),
                "碎晶前的角应把 T 抬到晶体塔正下方");

            ReplayAndVerifyColumn(order, target, 0);
        }
    }

    #endregion

    #region 角工序：未覆盖形态

    /// <summary>
    /// 构造 G 只能降一层：n = 4、缺口高两层的形态应明确返回未实现，
    /// 而不是伪造一条工序。
    /// </summary>
    public static void ConstructionGDoesNotFakeMultiLayerGap()
    {
        ShapeDocument target = FromColumns(4, 6, (0, "T..ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.False(order.IsComplete, "多层缺口不应被标成已完成");
        Assert.True(order.Note is not null, "未实现时必须给出 Note");
        Assert.True(order.Steps.Count > 0, "未实现时也要保留说明步骤");
        Assert.Equal(AngleWorkStepKind.Note, order.Steps[^1].Kind);
        Assert.True(
            order.Limitations.Any(text => text.Contains("多轮")),
            "限制说明应指出需要多轮 G");
    }

    /// <summary>
    /// 缺口不直接位于晶体塔下方（中间还夹着普通格）时，G/F 的逆变换不适用，
    /// 必须诚实报告未覆盖。
    /// </summary>
    public static void ConstructionRejectsGapNotUnderTower()
    {
        ShapeDocument target = FromColumns(4, 8, (0, "cT..Tccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8);

        Assert.False(order.IsComplete);
        Assert.True(order.Note is not null);
        Assert.True(order.Steps.Count > 0);
        Assert.True(
            order.Note!.Contains("正下方"),
            "Note 应说明缺口不在晶体塔正下方：" + order.Note);
    }

    /// <summary>
    /// 前态不稳定时（这里的目标列在第 1 层就有一枚悬空顶针），
    /// 订单必须止步于「前态 + 说明」，不能伪造 Cut/Collapse 步骤。
    /// </summary>
    public static void UnstablePrestateKeepsVerifiedPrefix()
    {
        ShapeDocument target = FromColumns(6, 6, (0, ".PT.c."));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

        Assert.False(order.IsComplete, "前态不稳定的订单不应标成完成");
        Assert.True(order.Note is not null, "必须给出未实现说明");
        Assert.True(
            order.Note!.Contains("前态不稳定"),
            "Note 应说明卡在前态：" + order.Note);

        Assert.True(order.Steps.Count > 0, "必须保留已验证的前若干步");

        Assert.Equal(
            AngleWorkStepKind.PreState,
            order.Steps[0].Kind,
            "第一条应是已构造出的前态");

        Assert.True(
            order.Steps.Any(step => step.Kind == AngleWorkStepKind.Note),
            "末尾应有说明步骤");

        Assert.False(
            order.Steps.Any(step =>
                step.Kind is AngleWorkStepKind.Cut or
                AngleWorkStepKind.Collapse or
                AngleWorkStepKind.Target),
            "验证失败后不能伪造后续步骤");
    }

    /// <summary>
    /// xfail：n = 4、缺口高两层的角 <c>T..ccc</c> 是合法的 S 列（D 条件成立），
    /// 但单层 G 做不了，需要多轮 G 或其他 n = 4 的 D 构造；当前尚未实现。
    ///
    /// 按「应当完成」来断言，因此现在是预期的 XFAIL。
    /// </summary>
    public static void FourQuadrantMultiLayerGapIsNotYetImplemented()
    {
        ShapeDocument target = FromColumns(4, 6, (0, "T..ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

        Assert.True(
            order.IsComplete,
            order.Note ?? "n = 4 的多层缺口应能由多轮 G 完成");

        Assert.True(order.Steps.Count > 0);
    }

    #endregion

    #region 回放工具

    /// <summary>
    /// 用真实 Cut 回放订单，断言目标列与目标一致；同时核对 Collapse 快照。
    /// </summary>
    private static void ReplayAndVerifyColumn(
        AngleWorkOrder order,
        ShapeDocument target,
        int column)
    {
        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

        CutResult cut = ShapePhysics.Cut(rotated);

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(replayed, column),
            $"第 {column} 列真实回放后应与目标一致");

        Assert.True(
            ShapeOps.Same(
                replayed,
                Step(order, AngleWorkStepKind.Collapse).Shape),
            "Collapse 步应等于真实 Cut 出来的西半边");

        ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

        foreach (int aux in order.AuxiliaryColumns)
        {
            Assert.Equal(
                0,
                CountCrystals(cutStep, aux),
                $"辅助列 {aux} 的晶体应全部被打碎");
        }

        Assert.Equal(
            CountCrystals(target, column),
            CountCrystals(cutStep, column),
            "目标列的晶体塔在切割后必须完好");
    }

    private static int CountCrystals(
        ShapeDocument document,
        int column)
    {
        int count = 0;

        for (int layer = 0;
             layer < document.LayerCount;
             layer++)
        {
            if (document[layer, column].Type == CellType.Crystal)
            {
                count++;
            }
        }

        return count;
    }

    private static AngleWorkStep Step(
        AngleWorkOrder order,
        AngleWorkStepKind kind)
    {
        return order.Steps.Single(step => step.Kind == kind);
    }

    private static void AssertStepKinds(
        AngleWorkOrder order,
        params AngleWorkStepKind[] expected)
    {
        Assert.Equal(
            expected.Length,
            order.Steps.Count,
            "步骤数量不符：" +
            string.Join(",", order.Steps.Select(step => step.Kind)));

        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(
                expected[index],
                order.Steps[index].Kind,
                $"第 {index} 步类型不符");
        }
    }

    #endregion

    #region 语料构造

    private static ShapeDocument Column(
        string text,
        int parts)
    {
        return FromColumns(parts, text.Length, (0, text));
    }

    private static ShapeDocument FromColumns(
        int parts,
        int layers,
        params (int Column, string Text)[] columns)
    {
        var document = new ShapeDocument(layers, parts);

        foreach ((int column, string text) in columns)
        {
            for (int layer = 0;
                 layer < layers;
                 layer++)
            {
                char code = layer < text.Length ? text[layer] : '.';

                document[layer, column] = code switch
                {
                    'c' => ShapeCell.Crystal('r'),
                    'P' => ShapeCell.Pin(),
                    'T' => ShapeCell.Shape('C', 'u'),
                    _ => ShapeCell.Empty()
                };
            }
        }

        return document;
    }

    private static ShapeDocument Parse(
        string code,
        int layers,
        int parts)
    {
        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        Assert.True(parsed.Success, parsed.Error ?? "短代码解析失败");

        return parsed.Document!;
    }

    #endregion
}

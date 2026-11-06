using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 结晶原语、角分类与角工序（构造 G/F/H、填充+暴破、多轮 G）的测试。
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
    /// 构造 G 的验证通过案例（填充+暴破做不了时的兜底）：目标列 <c>cT.ccc</c>。
    ///
    /// 缺口在第 3 层，上方是保留晶体、下方又是晶体，填充+暴破会把保留晶体一起打碎，
    /// 因此退回构造 G：前态是 <c>c.Tccc</c> + 辅助列；Cut 打碎辅助列的晶体后，
    /// 普通格落到第 2 层，缺口出现在第 3 层，正好得到目标列。
    /// </summary>
    public static void ConstructionGReplaysToTarget()
    {
        // 放在第 4 列（索引 3），顺便验证切割前的旋转。
        ShapeDocument target = FromColumns(4, 6, (3, "cT.ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 3, 6);

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
            "c.Tccc",
            ShapeSignature.ColumnText(preState, 3),
            "碎晶前的角应为 c.Tccc");

        // 真实回放：旋转 -> Cut -> 转回。
        ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

        CutResult cut = ShapePhysics.Cut(rotated);

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

        Assert.Equal(
            "cT.ccc",
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
            overCrystallized[2, 3].Type,
            "对下落结果再结晶会把第 3 层的缺口填成晶体");

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
    /// 用户样例的两列碎晶角：第 1 列走首选的填充+暴破，第 2 列（多个缺口、
    /// 缺口上方是保留晶体）填充+暴破做不了，退回构造 F。
    /// </summary>
    public static void ConstructionFHandlesUserSample()
    {
        ShapeDocument document = Parse(UserSampleCode, 9, 8);

        foreach ((int column, AngleConstruction expected) in new[]
                 {
                     (0, AngleConstruction.FillBreak),
                     (1, AngleConstruction.F)
                 })
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(document, column, 9);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"第 {column} 列的工序应验证通过");
            Assert.True(order.Note is null);
            Assert.Equal(expected, order.Construction);

            if (expected == AngleConstruction.FillBreak)
            {
                AssertStepKinds(
                    order,
                    AngleWorkStepKind.PreState,
                    AngleWorkStepKind.Cut,
                    AngleWorkStepKind.Collapse,
                    AngleWorkStepKind.Target);
            }
            else
            {
                AssertStepKinds(
                    order,
                    AngleWorkStepKind.PreState,
                    AngleWorkStepKind.Cut,
                    AngleWorkStepKind.Collapse,
                    AngleWorkStepKind.Note,
                    AngleWorkStepKind.Target);
            }

            ReplayAndVerifyColumn(order, document, column);
        }
    }

    /// <summary>
    /// 多层缺口案例（n = 6 与 n = 10）：目标列 <c>T..ccc</c>。
    ///
    /// 缺口高两层，但「填充+暴破」是首选：前态把 T 抬到缺口顶端、
    /// 底下两层填成晶体 <c>ccTccc</c>，一次 Cut 打碎底下两层晶体，
    /// T 落回第 1 层，缺口就由碎晶造出来。
    /// </summary>
    public static void FillBreakHandlesMultiLayerGap()
    {
        foreach (int parts in new[] { 6, 10 })
        {
            ShapeDocument target = FromColumns(parts, 6, (0, "T..ccc"));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"n = {parts} 的多层缺口填充+暴破应验证通过");
            Assert.Equal(AngleConstruction.FillBreak, order.Construction);

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                "ccTccc",
                ShapeSignature.ColumnText(preState, 0),
                "碎晶前的角应把 T 抬到缺口顶端并把底下两层填成晶体");

            ReplayAndVerifyColumn(order, target, 0);
        }
    }

    #endregion

    #region 角工序：未覆盖形态

    /// <summary>
    /// 填充+暴破会误伤保留晶体、构造 H 与多轮 G 也都做不了时，
    /// 必须明确返回未实现，而不是伪造一条工序。
    /// </summary>
    public static void ConstructionGDoesNotFakeMultiLayerGap()
    {
        // cT..ccc（cap = 6）：缺口上下都是保留晶体，下方只有一枚普通格且再下方是晶体，
        // 填充+暴破会把保留晶体一起打碎；底层是晶体使构造 H 不适用，
        // 晶体塔又不足三连晶，多轮 G 也不满足。
        ShapeDocument target = FromColumns(4, 6, (0, "cT..ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.False(order.IsComplete, "无法完成的角不应被标成已完成");
        Assert.True(order.Note is not null, "未实现时必须给出 Note");
        Assert.True(order.Steps.Count > 0, "未实现时也要保留说明步骤");
        Assert.Equal(AngleWorkStepKind.Note, order.Steps[^1].Kind);
        Assert.True(
            order.Note!.Contains("填充+暴破"),
            "Note 应如实说明首选的填充+暴破也未通过：" + order.Note);
        Assert.True(
            order.Limitations.Any(text => text.Contains("多轮")),
            "限制说明应指出多轮 G 也不适用");
        Assert.False(
            order.Steps.Any(step =>
                step.Kind is AngleWorkStepKind.Cut or
                AngleWorkStepKind.Collapse or
                AngleWorkStepKind.Target),
            "验证失败后不能伪造后续步骤");
    }

    /// <summary>
    /// 多轮构造 G 的兜底案例：n = 4、缺口高两层的角 <c>cT..ccc</c>。
    ///
    /// 填充+暴破会误伤缺口上下的保留晶体，因此退回多轮 G：每轮
    /// PreState → Cut → Collapse，逐层把普通格降到位。
    /// </summary>
    public static void FourQuadrantMultiLayerGapUsesRepeatedG()
    {
        ShapeDocument target = FromColumns(4, 8, (0, "cT..ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8);

        Assert.True(
            order.IsComplete,
            order.Note ?? "n = 4 的多层缺口应能由多轮 G 兜底完成");
        Assert.True(order.Note is null);
        Assert.Equal(AngleConstruction.G, order.Construction);

        // 缺口高两层 ⇒ 两轮；每轮 PreState/Cut/Collapse，最后 Note + Target。
        Assert.Equal(2, order.Steps.Count(step => step.Kind == AngleWorkStepKind.PreState));
        Assert.Equal(2, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut));
        Assert.Equal(2, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Collapse));
        Assert.Equal(8, order.Steps.Count);

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Collapse,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Collapse,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        // 逐轮核对：源普通格每次只降一层。
        Assert.Equal(
            "c..Tccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.PreState, 0).Shape!,
                0));

        Assert.Equal(
            "c.T.ccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.Collapse, 0).Shape!,
                0));

        Assert.Equal(
            "c.T.ccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.PreState, 1).Shape!,
                0));

        Assert.Equal(
            "cT..ccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.Collapse, 1).Shape!,
                0));

        VerifyCutRound(order, 0);
        VerifyCutRound(order, 1);

        Assert.True(
            ShapeOps.Same(
                target,
                NthStep(order, AngleWorkStepKind.Target, 0).Shape),
            "末步应清除辅助象限并得到目标角");
    }

    #endregion

    #region 角工序：构造 H（n = 4 一次顶针）

    /// <summary>
    /// 构造 H：四象限多层缺口（底层是空的）用<b>一次 Pin</b>完成整段下降。
    ///
    /// 填充+暴破会误伤保留晶体时退回构造 H。环上依次是 N | A | B | E：
    /// B 的晶体按顶针后层号对齐（前态 z 层晶体 ⇒ 顶针后 z+1 层），
    /// E 是满高纯晶体柱，N 在 s−1 层放针。
    /// </summary>
    public static void ConstructionHSinglePinBatchDrop()
    {
        foreach ((string text, string expectedPrestate, int cap) in new[]
                 {
                     (".T..c.", "..Tc..", 6),
                     (".T..c...", "..Tc....", 8)
                 })
        {
            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.True(order.IsComplete, order.Note ?? $"{text} 的构造 H 应验证通过");
            Assert.True(order.Note is null);
            Assert.Equal(AngleConstruction.H, order.Construction);
            Assert.Equal(0, order.Rotation);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Pin,
                AngleWorkStepKind.Note,
                AngleWorkStepKind.Target);

            Assert.Equal(
                1,
                order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                expectedPrestate,
                ShapeSignature.ColumnText(preState, 0),
                "构造 H 的前态应按顶针前线号排布");

            Assert.True(ShapeOps.IsStable(preState), "构造 H 的前态应稳定");

            // 辅助列按环顺序 N | A | B | E。
            Assert.Equal(3, order.AuxiliaryColumns.Count);

            int nColumn = order.AuxiliaryColumns[0];
            int bColumn = order.AuxiliaryColumns[1];
            int eColumn = order.AuxiliaryColumns[2];

            int d = ResolveDropLayer(target, 0);

            int s = ResolveTowerBottom(target, 0) - 1;

            Assert.Equal(
                CellType.Pin,
                preState[s - 1, nColumn].Type,
                "N 应在 s−1 层放针（顶针后位于 s 层）");

            for (int layer = d - 1;
                 layer <= s - 2;
                 layer++)
            {
                Assert.Equal(
                    CellType.Crystal,
                    preState[layer, bColumn].Type,
                    $"B 的晶体桥应对齐顶针后的 {layer + 1} 层");
            }

            for (int layer = 0;
                 layer < cap;
                 layer++)
            {
                Assert.Equal(
                    CellType.Crystal,
                    preState[layer, eColumn].Type,
                    "E 应是满高纯晶体柱");
            }

            // 真实 Pin 回放。
            PhysicsResult pin = ShapePhysics.Pin(preState);

            Assert.True(pin.Success, pin.Error ?? "顶针失败");

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(pin.Document!, 0),
                "一次顶针后的目标列应与目标一致");

            Assert.True(
                ShapeOps.Same(
                    pin.Document,
                    Step(order, AngleWorkStepKind.Pin).Shape),
                "Pin 步快照应等于真实 ShapePhysics.Pin 的结果");

            Assert.True(
                ShapeOps.Same(
                    target,
                    Step(order, AngleWorkStepKind.Target).Shape),
                "末步应得到目标角");
        }
    }

    #endregion

    #region 角工序：填充 + 暴破

    /// <summary>
    /// 缺口不在晶体塔正下方（塔底正下方是普通格）时，先把塔下缺口填成晶体，
    /// 再用切割打碎这一段晶体：PreState（含填充晶体）→ Cut → Collapse → Target。
    /// </summary>
    public static void ConstructionFillBreakHandlesGapNotUnderTower()
    {
        foreach ((string text, string expectedPrestate) in new[]
                 {
                     ("cT..Tccc", "cTccTccc"),
                     ("cT..TcccPT", "cTccTcccPT")
                 })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 的填充+暴破工序应验证通过");
            Assert.True(order.Note is null);
            Assert.Equal(AngleConstruction.FillBreak, order.Construction);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Collapse,
                AngleWorkStepKind.Target);

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                expectedPrestate,
                ShapeSignature.ColumnText(preState, 0),
                "前态应把塔下缺口填成晶体");

            ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

            // 填充的晶体（第 3、4 层）必须已被切碎。
            foreach (int layer in new[] { 2, 3 })
            {
                Assert.Equal(
                    CellType.Empty,
                    cutStep[layer, 0].Type,
                    $"第 {layer + 1} 层的填充晶体应被打碎");
            }

            // 目标原有的晶体（底层与晶体塔）必须完好。
            Assert.Equal(
                CountCrystals(target, 0),
                CountCrystals(cutStep, 0),
                "目标列的晶体塔在切割后必须完好");

            foreach (int aux in order.AuxiliaryColumns)
            {
                Assert.Equal(
                    0,
                    CountCrystals(cutStep, aux),
                    $"辅助列 {aux} 的晶体应全部被打碎");
            }

            // 真实回放：旋转 → Cut → 转回。
            ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

            CutResult cut = ShapePhysics.Cut(rotated);

            Assert.True(cut.Success, cut.Error ?? "切割失败");

            ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(replayed, 0),
                "填充+暴破后真实 Cut 的目标列应与目标一致");

            Assert.True(
                ShapeOps.Same(
                    replayed,
                    Step(order, AngleWorkStepKind.Collapse).Shape),
                "Collapse 步应等于真实 Cut 出来的西半边");
        }
    }

    /// <summary>
    /// 填充+暴破失败时只保留已验证前缀（PreState、Cut），并如实写 Note：
    /// <c>c.Tc....</c> 唯一的缺口上下都贴着保留晶体，下方只有一枚普通格且再下方
    /// 又是晶体，填充后会与保留晶体连成同一晶体簇，切割必然误伤，
    /// 因此必须报未实现。
    /// </summary>
    public static void FillBreakKeepsVerifiedPrefixOnMismatch()
    {
        ShapeDocument target = FromColumns(4, 8, (0, "c.Tc...."));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8);

        Assert.False(order.IsComplete, "会误伤保留晶体的填充+暴破不应标成完成");
        Assert.True(order.Note is not null);
        Assert.True(
            order.Note!.Contains("填充+暴破后的目标列与目标不符"),
            "Note 应说明卡在回放核对：" + order.Note);
        Assert.Equal(AngleConstruction.FillBreak, order.Construction);

        Assert.Equal(
            AngleWorkStepKind.PreState,
            order.Steps[0].Kind,
            "第一条应是已验证的前态");
        Assert.True(
            order.Steps.Any(step => step.Kind == AngleWorkStepKind.Cut),
            "真实 Cut 已验证，应保留 Cut 步");
        Assert.True(
            order.Steps.Any(step =>
                step.Kind == AngleWorkStepKind.Note &&
                step.Title.Contains("未通过验证")),
            "应保留回放结果的诊断步骤");
        Assert.Equal(AngleWorkStepKind.Note, order.Steps[^1].Kind);
        Assert.False(
            order.Steps.Any(step =>
                step.Kind is AngleWorkStepKind.Collapse or
                AngleWorkStepKind.Target),
            "验证失败后不能伪造 Collapse/Target 步");
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

    #endregion

    #region 角工序：填充 + 暴破（首选）

    /// <summary>
    /// 用户给的 cTT.cTcc 示意前态（8 层 × 4 象限；短代码允许省略尾部空象限）。
    /// </summary>
    private const string UserFillBreakPrestateCode =
        "crCu--Cu:CuCu--Cu:cgCu--cg:CuP-----:crCu----:CuCu----:crCu----:crCu----";

    /// <summary>
    /// 用户给的示意前态直接切割就得到 cTT.cTcc：跨刀口的是第 3 层那一段晶体，
    /// 打碎后第 4 层的 T 落到第 3 层，目标列逐格成形。
    /// </summary>
    public static void UserPrestateCutsToTargetColumn()
    {
        ShapeDocument preState = Parse(UserFillBreakPrestateCode, 8, 4);

        Assert.True(ShapeOps.IsStable(preState), "用户给的示意前态应稳定");

        Assert.Equal(
            "cTcTcTcc",
            ShapeSignature.ColumnText(preState, 0),
            "示意前态的第 1 列应是 cTcTcTcc");

        CutResult cut = ShapePhysics.Cut(preState);

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument west = cut.West!;

        AssertColumnCells(west, 0, "cTT.cTcc");

        Assert.Equal(
            "cTT.cTcc",
            ShapeSignature.ColumnText(west, 0),
            "用户前态切割后目标列应逐格等于 cTT.cTcc");

        // 跨刀口的桥柱残留在东半边，不进入目标角。
        Assert.Equal(
            "TT......",
            ShapeSignature.ColumnText(cut.East!, 3),
            "桥柱柱脚应留在东半边");
    }

    /// <summary>
    /// 用户的三个例子都用「填充 + 暴破」完成：
    /// T..ccc / T..cc 先把 T 抬到缺口顶端、底下两层填成晶体；
    /// cTT.cTcc 把缺口下方那枚 T 抬进缺口、缺口位置填成晶体。
    /// 一次 Cut 打碎填充段，Collapse 后逐格等于目标列。
    /// </summary>
    public static void FillBreakCoversUserExamples()
    {
        var cases = new (string Target, string PreState, int[] BrokenLayers)[]
        {
            ("T..ccc", "ccTccc", new[] { 0, 1 }),
            ("T..cc", "ccTcc", new[] { 0, 1 }),
            ("cTT.cTcc", "cTcTcTcc", new[] { 2 })
        };

        foreach ((string text, string expectedPrestate, int[] brokenLayers) in cases)
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 应能用填充+暴破完成");
            Assert.True(order.Note is null, $"{text} 不应有未实现说明");
            Assert.Equal(AngleConstruction.FillBreak, order.Construction);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Collapse,
                AngleWorkStepKind.Target);

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                expectedPrestate,
                ShapeSignature.ColumnText(preState, 0),
                $"{text} 的前态应把缺口填成晶体");

            Assert.True(ShapeOps.IsStable(preState), $"{text} 的前态应稳定");

            // 真实回放：旋转 → Cut → 转回，再逐格核对目标列。
            ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

            CutResult cut = ShapePhysics.Cut(rotated);

            Assert.True(cut.Success, cut.Error ?? "切割失败");

            ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

            AssertColumnCells(replayed, 0, text);

            Assert.True(
                ShapeOps.Same(
                    replayed,
                    Step(order, AngleWorkStepKind.Collapse).Shape),
                $"{text} 的 Collapse 步应等于真实 Cut 出来的西半边");

            // 填充的晶体必须真的被暴破打碎。
            ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

            foreach (int layer in brokenLayers)
            {
                Assert.Equal(
                    CellType.Empty,
                    cutStep[layer, 0].Type,
                    $"{text} 第 {layer + 1} 层的填充晶体应被打碎");
            }

            // 保留晶体没有被误伤。
            Assert.Equal(
                CountCrystals(target, 0),
                CountCrystals(cutStep, 0),
                $"{text} 的保留晶体在切割后必须完好");
        }
    }

    /// <summary>
    /// 优先级与回退：填充+暴破做得了的碎晶角走新方案；
    /// 做不了（会误伤保留晶体）的形状仍退回构造 G / H / F。
    /// </summary>
    public static void FillBreakIsPreferredOverLegacyConstructions()
    {
        // 单层缺口 T.ccc：原来走构造 G，现在优先填充+暴破。
        ShapeDocument preferredTarget = FromColumns(4, 5, (3, "T.ccc"));

        AngleWorkOrder preferred = AngleWorkOrderBuilder.Build(preferredTarget, 3, 5);

        Assert.True(preferred.IsComplete, preferred.Note ?? "填充+暴破应验证通过");
        Assert.Equal(AngleConstruction.FillBreak, preferred.Construction);

        // 缺口上下都是保留晶体：退回构造 G。
        Assert.Equal(
            AngleConstruction.G,
            BuildConstruction("cT.T.ccc", 4, 8));

        // 落点/针位只允许一次顶针：退回构造 H。
        Assert.Equal(
            AngleConstruction.H,
            BuildConstruction(".T..c.", 4, 6));

        // n ≥ 6 的受控下落：退回构造 F。
        Assert.Equal(
            AngleConstruction.F,
            BuildConstruction(".T..c..", 6, 7));
    }

    private static AngleConstruction BuildConstruction(
        string text,
        int parts,
        int cap)
    {
        ShapeDocument target = FromColumns(parts, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

        Assert.True(
            order.IsComplete,
            order.Note ?? $"{text} 应能由兜底构造完成");

        return order.Construction;
    }

    #endregion

    #region 角工序：cap 高于目标高度

    /// <summary>
    /// 目标只有 6/7/8 层、cap 更大时：前态与中间态要补空层，但目标列仍要逐格对上。
    /// 覆盖首选填充+暴破与三种兜底构造（多轮 G、构造 H、构造 F）。
    /// </summary>
    public static void ConstructionHandlesCapAboveTargetHeight()
    {
        var cases = new (string Text, int TargetLayers, int Parts, AngleConstruction Construction)[]
        {
            ("T..ccc", 6, 4, AngleConstruction.FillBreak),
            ("cT..ccc", 8, 4, AngleConstruction.G),
            (".T..c.", 6, 4, AngleConstruction.H),
            (".T..c..", 7, 6, AngleConstruction.F)
        };

        const int cap = 10;

        foreach ((string text, int targetLayers, int parts, AngleConstruction construction) in cases)
        {
            ShapeDocument target = FromColumns(parts, targetLayers, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text}（cap 高于目标）应验证通过");
            Assert.Equal(construction, order.Construction);

            AngleWorkStep lastOperational = order.Steps.Last(step =>
                step.Shape is not null &&
                step.Kind is AngleWorkStepKind.Collapse or
                    AngleWorkStepKind.Pin);

            Assert.Equal(
                text.PadRight(cap, '.'),
                ShapeSignature.ColumnText(lastOperational.Shape!, 0),
                $"{text}（cap 高于目标）的最终目标列应逐格对上");

            Assert.True(
                ShapeOps.Same(
                    target,
                    Step(order, AngleWorkStepKind.Target).Shape),
                "Target 步应等于原目标");
        }
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

    /// <summary>
    /// 逐格核对一列：<c>.</c> 空、<c>P</c> 顶针、<c>T</c> 普通格(C,u)、
    /// <c>c</c> 晶体(r)。
    /// </summary>
    private static void AssertColumnCells(
        ShapeDocument document,
        int column,
        string expected)
    {
        for (int layer = 0; layer < expected.Length; layer++)
        {
            ShapeCell cell = document[layer, column];

            switch (expected[layer])
            {
                case '.':
                    Assert.Equal(
                        CellType.Empty,
                        cell.Type,
                        $"第 {layer + 1} 层应为空");
                    break;

                case 'P':
                    Assert.Equal(
                        CellType.Pin,
                        cell.Type,
                        $"第 {layer + 1} 层应为顶针");
                    break;

                case 'T':
                    Assert.Equal(
                        CellType.Shape,
                        cell.Type,
                        $"第 {layer + 1} 层应为普通格");

                    Assert.Equal(
                        'C',
                        cell.ShapeCode,
                        $"第 {layer + 1} 层普通格的形状码应为 C");

                    Assert.Equal(
                        'u',
                        cell.ColorCode,
                        $"第 {layer + 1} 层普通格的颜色应为 u");
                    break;

                case 'c':
                    Assert.Equal(
                        CellType.Crystal,
                        cell.Type,
                        $"第 {layer + 1} 层应为晶体");

                    Assert.Equal(
                        'r',
                        cell.ColorCode,
                        $"第 {layer + 1} 层晶体的颜色应为 r");
                    break;

                default:
                    throw new ArgumentException(
                        "未知的列代码：" + expected[layer]);
            }
        }
    }

    private static AngleWorkStep Step(
        AngleWorkOrder order,
        AngleWorkStepKind kind)
    {
        return order.Steps.Single(step => step.Kind == kind);
    }

    private static AngleWorkStep NthStep(
        AngleWorkOrder order,
        AngleWorkStepKind kind,
        int occurrence)
    {
        int seen = 0;

        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind != kind)
            {
                continue;
            }

            if (seen == occurrence)
            {
                return step;
            }

            seen++;
        }

        throw new InvalidOperationException(
            $"找不到第 {occurrence + 1} 个 {kind} 步");
    }

    /// <summary>
    /// 多轮 G 的逐轮回放：前态稳定 → 真实 Cut → 西半边转回后与 Collapse 快照一致，
    /// 且辅助列晶体全部被打碎、目标列晶体不丢。
    /// </summary>
    private static void VerifyCutRound(
        AngleWorkOrder order,
        int round)
    {
        ShapeDocument preState =
            NthStep(order, AngleWorkStepKind.PreState, round).Shape!;

        ShapeDocument cutStep =
            NthStep(order, AngleWorkStepKind.Cut, round).Shape!;

        ShapeDocument collapse =
            NthStep(order, AngleWorkStepKind.Collapse, round).Shape!;

        Assert.True(
            ShapeOps.IsStable(preState),
            $"第 {round + 1} 轮前态应稳定");

        ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

        CutResult cut = ShapePhysics.Cut(rotated);

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

        Assert.True(
            ShapeOps.Same(replayed, collapse),
            $"第 {round + 1} 轮 Collapse 步应等于真实 Cut 出来的西半边");

        foreach (int aux in order.AuxiliaryColumns)
        {
            Assert.Equal(
                0,
                CountCrystals(cutStep, aux),
                $"第 {round + 1} 轮辅助列 {aux} 的晶体应全部被打碎");
        }

        Assert.Equal(
            CountCrystals(preState, order.Column),
            CountCrystals(cutStep, order.Column),
            $"第 {round + 1} 轮目标列的晶体塔必须完好");
    }

    /// <summary>最高晶体段的底端（0 起）。</summary>
    private static int ResolveTowerBottom(
        ShapeDocument target,
        int column)
    {
        int t = AngleClassifier.HighestCrystalLayer(target, column);

        while (t > 0 &&
               target[t - 1, column].Type == CellType.Crystal)
        {
            t--;
        }

        return t;
    }

    /// <summary>晶体塔正下方窗口里的最高非空层（0 起）；没有时返回 -1。</summary>
    private static int ResolveDropLayer(
        ShapeDocument target,
        int column)
    {
        int s = ResolveTowerBottom(target, column) - 1;

        for (int layer = s - 1;
             layer >= 0;
             layer--)
        {
            if (target[layer, column].Type != CellType.Empty)
            {
                return layer;
            }
        }

        return -1;
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

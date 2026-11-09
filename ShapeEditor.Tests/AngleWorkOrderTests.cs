using System.Diagnostics;
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

        // cT.ccc 在五项条件下不合法（晶体之间只有一个普通格），
        // 这里只是用合成列验证构造 G 的 Cut/Collapse 回放语义。
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            3,
            6,
            enforceFiveConditions: false);

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
    /// 算法 4 的长晶体段案例 <c>cT.T.ccc</c>（D 不成立、F 成立）：
    /// 走 §11 的通用实现（规范计划 + 变体 + 回放），不再用手写的构造 G 前态。
    ///
    /// <para>
    /// 规范计划给出：核心列 <c>cTccTccc</c>（把 T@3 抬到 4、让出的第 3 层填临时晶体）、
    /// 托柱 <c>TTT.TTPT</c>、桥柱第 4 层一颗晶体；一刀之后目标列逐格等于
    /// <c>cT.T.ccc</c>。
    /// </para>
    /// <para>
    /// <b>断言更新的原因</b>：旧实现让这一档落到兜底链的构造 G（前态 <c>cT..Tccc</c>）；
    /// 按 §2 第 2 条，路线 F 现在必须先走 §11 通用实现，而通用实现的构造原语是
    /// 定向碎晶（构造 D）。文档 §4.7 也注明「单格环下落（构造 G）」与「定向碎晶
    /// （构造 D）」可以互换，因此这里改为断言 D。
    /// </para>
    /// </summary>
    public static void ConstructionGMatchesSpecExample()
    {
        ShapeDocument target = FromColumns(4, 8, (0, "cT.T.ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8);

        Assert.True(order.IsComplete, order.Note ?? "规格 §3 的 G 例子应验证通过");
        Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm);
        Assert.Equal(AngleConstruction.D, order.Construction);
        Assert.Equal(0, order.Rotation);

        Assert.Equal(
            "cTccTccc",
            ShapeSignature.ColumnText(
                Step(order, AngleWorkStepKind.PreState).Shape!,
                0),
            "规范核心列应为 cTccTccc（T@3 抬到 4，第 3 层填临时晶体）");

        ReplayGenericAlgorithm4(order, target, 0);
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

            /*
             * n ≥ 6 的列级步骤链：PreState → Cut → Collapse → Support → Target。
             * Support = 构造 C（装新支撑柱、拆辅助列）；n = 4 的构造 G 仍然是
             * PreState → Cut → Collapse → Target（见 ConstructionGReplaysToTarget）。
             */
            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Collapse,
                AngleWorkStepKind.Support,
                AngleWorkStepKind.Target);

            ShapeDocument handover =
                Step(order, AngleWorkStepKind.Support).Shape!;

            Assert.True(
                ShapeOps.IsStable(handover),
                "构造 C 交接后的图形应稳定");

            Assert.Equal(
                ShapeSignature.ColumnText(document, column),
                ShapeSignature.ColumnText(handover, column),
                "构造 C 交接不得改动目标列");

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

        // 合成列（五项条件不过）：只验证「做不出来就诚实报未覆盖」。
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            6,
            enforceFiveConditions: false);

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

        // 合成列（五项条件不过）：只验证多轮构造 G 的逐轮回放。
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            8,
            enforceFiveConditions: false);

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
    /// D 成立（最低晶体下 ≥2 空）的形态由算法 6 接管（见算法 6 的测试），
    /// 所以这里用「D 成立但算法 6 造不出塔」的形态，让兜底链退回构造 H。
    /// 环上依次是 N | A | B | E：B 的晶体按顶针后层号对齐
    /// （前态 z 层晶体 ⇒ 顶针后 z+1 层），E 是满高纯晶体柱，N 在 s−1 层放针。
    /// </summary>
    public static void ConstructionHSinglePinBatchDrop()
    {
        foreach ((string text, string expectedPrestate, int cap) in new[]
                 {
                     (".T..cT", "..TcT.", 6),
                     (".T..cTT", "..TcTT.", 7)
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
    /// 缺口不在晶体塔正下方（塔底正下方是普通格）时的两条路线：
    /// <c>cT..Tccc</c> 的 D 不成立 ⇒ 路线 F ⇒ §11 通用实现（构造 D）；
    /// <c>cT..TcccPT</c> 带针 ⇒ 通用实现主动让位 ⇒ 仍由兜底链的填充+暴破完成。
    ///
    /// <para>
    /// <b>断言更新的原因</b>：第一个例子的底格是晶体（D 不成立），按 §2 第 2 条
    /// 必须先走 §11 通用实现；旧断言里的 Collapse 步也随之变成 Support 步。
    /// </para>
    /// </summary>
    public static void ConstructionFillBreakHandlesGapNotUnderTower()
    {
        foreach ((string text, string expectedPrestate) in new[]
                 {
                     ("cT..Tccc", "cTccTccc")
                 })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 的 §11 通用实现应验证通过");
            Assert.True(order.Note is null);
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm);
            Assert.Equal(AngleConstruction.D, order.Construction);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Support,
                AngleWorkStepKind.Target);

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                expectedPrestate,
                ShapeSignature.ColumnText(preState, 0),
                "规范核心列应把塔下缺口填成临时晶体");

            ReplayGenericAlgorithm4(order, target, 0);
        }

        /*
         * 带针的合成列（cT..TcccPT）：§11 的四列布局里没有针位，通用实现主动返回
         * 「未覆盖」，由兜底链的填充+暴破接住——这正是 §2 第 2 条的退化场景。
         */
        {
            const string text = "cT..TcccPT";
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
                "cTccTcccPT",
                ShapeSignature.ColumnText(preState, 0),
                "前态应把塔下缺口填成晶体");

            ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

            foreach (int layer in new[] { 2, 3 })
            {
                Assert.Equal(
                    CellType.Empty,
                    cutStep[layer, 0].Type,
                    $"第 {layer + 1} 层的填充晶体应被打碎");
            }

            Assert.Equal(
                CountCrystals(target, 0),
                CountCrystals(cutStep, 0),
                "目标列的晶体塔在切割后必须完好");

            ShapeDocument replayed = ShapeOps.Rotate(
                ShapePhysics.Cut(ShapeOps.Rotate(preState, order.Rotation)).West!,
                -order.Rotation);

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(replayed, 0),
                "填充+暴破后真实 Cut 的目标列应与目标一致");
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

        // 合成列（五项条件不过）：这里只验证兜底链保留已验证前缀的行为。
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            8,
            enforceFiveConditions: false);

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

        // 合成列（五项条件不过）：只验证「前态不稳定时保留已验证前缀」。
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            6,
            enforceFiveConditions: false);

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
    /// 用户给的三个例子：前两个（<c>T..ccc</c>、<c>T..cc</c>，D 成立）仍走填充+暴破；
    /// 第三个 <c>cTT.cTcc</c> 的 D 不成立（底格就是晶体）⇒ 路线 F ⇒ §11 通用实现，
    /// 断言随之改为构造 D（算法 4）。
    /// </summary>
    public static void FillBreakCoversUserExamples()
    {
        var cases = new (string Target, string PreState, int[] BrokenLayers)[]
        {
            ("T..ccc", "ccTccc", new[] { 0, 1 }),
            ("T..cc", "ccTcc", new[] { 0, 1 })
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

        /*
         * 第三个例子 cTT.cTcc 的 D 不成立（底格本身就是晶体）⇒ 路线 F，
         * 由 §11 通用实现（算法 4 / 构造 D）接管；核心列 cTcTcTcc 与旧的
         * 填充前态只差构造标注，但步骤链变成 PreState → Cut → Support → Target。
         */
        {
            const string text = "cTT.cTcc";
            const int cap = 8;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(order.IsComplete, order.Note ?? $"{text} 应由 §11 通用实现完成");
            Assert.True(order.Note is null, $"{text} 不应有未实现说明");
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm);
            Assert.Equal(AngleConstruction.D, order.Construction);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Support,
                AngleWorkStepKind.Target);

            Assert.Equal(
                "cTcTcTcc",
                ShapeSignature.ColumnText(
                    Step(order, AngleWorkStepKind.PreState).Shape!,
                    0),
                $"{text} 的规范核心列应为 cTcTcTcc");

            ReplayGenericAlgorithm4(order, target, 0);
        }
    }

    /// <summary>
    /// 优先级与回退：D 成立的角仍走填充+暴破；D 不成立的路线 F 按 §2 第 2 条
    /// 先走 §11 通用实现（构造 D），只有通用实现回放失败才退回兜底链（G / H / F）。
    ///
    /// <para>
    /// <b>断言更新的原因</b>：<c>T.ccc</c> 与 <c>cT.T.ccc</c> 都是路线 F，
    /// 现在由 §11 通用实现完成（构造 D），不再是兜底链的填充+暴破 / 构造 G。
    /// </para>
    /// </summary>
    public static void FillBreakIsPreferredOverLegacyConstructions()
    {
        // 单层缺口 T.ccc：D 不成立 ⇒ 路线 F ⇒ §11 通用实现（构造 D）。
        ShapeDocument preferredTarget = FromColumns(4, 5, (3, "T.ccc"));

        AngleWorkOrder preferred = AngleWorkOrderBuilder.Build(preferredTarget, 3, 5);

        Assert.True(preferred.IsComplete, preferred.Note ?? "算法 4 通用实现应验证通过");
        Assert.Equal(AngleAlgorithm.Algorithm4, preferred.Algorithm);
        Assert.Equal(AngleConstruction.D, preferred.Construction);

        // 缺口上下都是保留晶体：同样落在路线 F，由通用实现完成。
        Assert.Equal(
            AngleConstruction.D,
            BuildConstruction("cT.T.ccc", 4, 8));

        // 落点/针位只允许一次顶针：退回构造 H。
        // .T..cT 的 D 成立（最低晶体在 5 层、下面只有一个空），因此走算法 6 的兜底链。
        Assert.Equal(
            AngleConstruction.H,
            BuildConstruction(".T..cT", 4, 6));

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
    /// 覆盖填充+暴破、多轮 G、算法 6（.T..c. 的 D 成立）与构造 F。
    /// </summary>
    public static void ConstructionHandlesCapAboveTargetHeight()
    {
        var cases = new (string Text, int TargetLayers, int Parts, AngleConstruction Construction)[]
        {
            ("T..ccc", 6, 4, AngleConstruction.FillBreak),
            ("cT..ccc", 8, 4, AngleConstruction.G),
            (".T..c.", 6, 4, AngleConstruction.C),
            (".T..c..", 7, 6, AngleConstruction.F)
        };

        const int cap = 10;

        foreach ((string text, int targetLayers, int parts, AngleConstruction construction) in cases)
        {
            ShapeDocument target = FromColumns(parts, targetLayers, (0, text));

            // cT..ccc 是合成列（五项条件不过），保留它只验证多轮 G 的补层行为。
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                cap,
                enforceFiveConditions: false);

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

    #region 角工序：算法 6（n = 4、D 成立）

    /// <summary>
    /// 算法 6：n = 4、D 成立（最低晶体以下至少两空）的 S 形态
    /// <c>.T.c</c>、<c>..T.c</c>、<c>.T..c</c>。
    ///
    /// <para>
    /// 工序单 = 造塔 X（交换链子配方，rank 0）→ 顶针（一次 Pin）→ 构造 C
    /// （装新支撑柱、拆旧柱与辅助列）→ 目标角。造塔后整条链都用真实
    /// <see cref="ShapePhysics"/> 回放：塔稳定、每列过五项条件、交换链可装配、
    /// 顶针后目标列逐格等于目标、支柱交接后仍稳定。
    /// </para>
    /// </summary>
    public static void Algorithm6BuildsSideSupportedAngles()
    {
        foreach ((string text, int cap) in new[]
                 {
                     (".T.c", 4),
                     ("..T.c", 5),
                     (".T..c", 5)
                 })
        {
            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
            Assert.Equal(AngleConstruction.C, order.Construction);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 的算法 6 工序应验证通过");
            Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

            // 步骤链：造塔 → 顶针 → 构造 C → 目标角。
            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Pin,
                AngleWorkStepKind.Support,
                AngleWorkStepKind.Target);

            // rank 只统计角自己的顶针，子配方必须是 rank 0 的交换链。
            Assert.Equal(1, order.Rank);
            Assert.Equal(1, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

            ShapeDocument tower = order.Steps[0].Shape!;
            ShapeDocument pinned = order.Steps[1].Shape!;
            ShapeDocument handover = order.Steps[2].Shape!;

            // 造塔 X 本身：稳定、每列过五项条件、能用交换链装出来。
            Assert.True(ShapeOps.IsStable(tower), $"{text} 的塔 X 应稳定");
            Assert.True(
                ShapeSignature.AllColumnsLegal(tower, out _),
                $"{text} 的塔 X 每列都应过五项条件");
            Assert.True(
                ExchangeChain.TryBuild(tower, out Recipe? rebuilt),
                $"{text} 的塔 X 应能用交换链装配");

            Assert.True(order.SubRecipe is not null, "算法 6 必须带造塔子配方");
            Assert.Equal(RecipeKind.Exchange, order.SubRecipe!.Kind);
            Assert.Equal(0, order.SubRecipe.Rank);
            Assert.True(
                order.SubRecipe.Steps.Any(step => step.Title.Contains("交换")),
                "造塔子配方必须包含交换节点");
            Assert.True(
                Replay.Verify(order.SubRecipe, tower, out IReadOnlyList<string> errors),
                "造塔子配方应能端到端回放：" + string.Join(",", errors));
            Assert.True(
                ShapeOps.Same(order.SubRecipe.Shape, tower),
                "子配方产物必须等于前态（塔 X）");

            // 顶针步必须等于真实 ShapePhysics.Pin 的结果。
            Assert.True(
                ShapeOps.Same(ShapePhysics.Pin(tower).Document, pinned),
                "Pin 步快照应等于真实顶针结果");

            // 顶针后与构造 C 后，目标列都逐格等于目标。
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(pinned, 0),
                $"{text} 顶针后的目标列应逐格等于目标");
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(handover, 0),
                $"{text} 构造 C 交接后的目标列应逐格等于目标");

            Assert.True(
                ShapeOps.IsStable(handover),
                $"{text} 构造 C 交接后的图形应稳定");

            // 构造 C 只留下目标列与新支撑柱，旧柱/辅助列都已拆掉。
            int supportColumn = order.AuxiliaryColumns[0];

            Assert.Equal(3, supportColumn, "第 1 列的新支撑柱应落在第 4 象限");
            Assert.True(
                ShapeSignature.ColumnText(handover, supportColumn) ==
                new string('T', cap),
                "新支撑柱应是满高普通柱");

            for (int other = 0; other < 4; other++)
            {
                if (other == 0 || other == supportColumn)
                {
                    continue;
                }

                for (int layer = 0; layer < cap; layer++)
                {
                    Assert.Equal(
                        CellType.Empty,
                        handover[layer, other].Type,
                        $"拆掉辅助列后第 {other + 1} 象限第 {layer + 1} 层应为空");
                }
            }

            // 末步清除辅助柱，得到目标角。
            Assert.True(
                ShapeOps.Same(
                    target,
                    Step(order, AngleWorkStepKind.Target).Shape),
                "末步应得到目标角");

            // 目标列没有丢失任何一格（颜色/形状码也逐格一致）。
            AssertColumnCells(handover, 0, text);
        }
    }

    /// <summary>
    /// <c>.TcTc</c> 的 D 不成立、却属于 S ⇒ 路线 F：由 §11 通用实现接住
    /// （规范计划里没有提升，核心列就是 <c>cTcTc</c>，托柱是满高实心柱）。
    ///
    /// <para>
    /// §7.1 的循环判据仍然生效：前态 <c>cTcTc</c> 比目标多一枚临时晶体，
    /// 所以「前态 = 目标列 + 托柱」不会被当成完成。
    /// </para>
    /// <para>
    /// <b>断言更新的原因</b>：旧实现让这一档落到兜底链的填充+暴破
    /// （步骤含 Collapse、托柱放在第 4 列）；按 §2 第 2 条，路线 F 先走 §11
    /// 通用实现后，构造是定向碎晶（构造 D），步骤链变成
    /// PreState → Cut → Support → Target，托柱放在环上相邻的下一列（第 2 列）、
    /// 桥柱放在第 4 列。
    /// </para>
    /// </summary>
    public static void PillarHolderRemainsFallbackForNonDBranches()
    {
        const string text = ".TcTc";
        const int cap = 5;

        ShapeDocument target = FromColumns(4, cap, (0, text));

        Assert.False(
            PillarSCriterion.BottomResourceD(text),
            ".TcTc 的 D 不应成立");
        Assert.True(
            PillarSCriterion.IsPillarColumn(target, 0),
            ".TcTc 应属于 n = 4 的 S");

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

        Assert.True(order.IsComplete, order.Note ?? "算法 4 通用实现应验证通过");
        Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm);
        Assert.Equal(AngleConstruction.D, order.Construction);

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Support,
            AngleWorkStepKind.Target);

        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        Assert.Equal(
            "cTcTc",
            ShapeSignature.ColumnText(preState, 0),
            "前态应把底层缺口填成临时晶体（不能直接等于目标列）");
        Assert.NotEqual(
            text,
            ShapeSignature.ColumnText(preState, 0),
            "前态的目标列必须与目标不同，否则就是循环");
        Assert.True(ShapeOps.IsStable(preState), "满高托柱的前态应稳定");

        int holderColumn = order.AuxiliaryColumns[0];

        Assert.Equal(1, holderColumn, "第 1 列的托柱应放在环上相邻的第 2 列");

        for (int layer = 0; layer < cap; layer++)
        {
            Assert.Equal(
                CellType.Shape,
                preState[layer, holderColumn].Type,
                $"托柱第 {layer + 1} 层必须是实体（覆盖全高）");
        }

        Assert.Equal(
            PipelineOutcome.Producible,
            ProductionPipeline.Solve(preState, cap).Outcome,
            "满高托柱的前态应能被生产管线判定为 Producible");

        ReplayGenericAlgorithm4(order, target, 0);
    }

    #endregion

    #region §7 回归角（假阳性、托柱、子形态、耗时）

    /// <summary>
    /// TODO.md §7 的六个回归角：<c>.T.c</c>、<c>T.T.c</c>、<c>P.T.c</c>、
    /// <c>.TT.c</c>、<c>.T..c</c>、<c>.T.T.c</c>（§7.7）。
    ///
    /// <para>每个角都要满足：</para>
    /// <list type="number">
    /// <item><see cref="AngleWorkOrder.IsComplete"/> 为 true，最终列逐格等于目标；</item>
    /// <item>前态的目标列<b>不等于</b>目标本身，且前态至少含一处临时晶体
    /// （§7.1：前态 = 目标的「先做出来再切掉辅助柱」是循环，不算完成）；</item>
    /// <item>前态的每一列都过五项条件（§6 第 1 条 / §7.7）；</item>
    /// <item><c>.T..c</c> 的塔列不含 <c>.T.c</c> 子形态（缺口错开层，避免多一次顶破）。</item>
    /// </list>
    /// <para>
    /// 另外用 Stopwatch 断言 <c>.TT.c</c> 的单次 Build 耗时 &lt; 30 ms：
    /// 它以前会在构造 H 的 support × block × fuse × 列编码五重枚举里跑满整圈。
    /// </para>
    /// </summary>
    public static void RegressionCornersAreConstructible()
    {
        var corners = new (string Text, int Cap)[]
        {
            (".T.c", 4),
            ("T.T.c", 5),
            ("P.T.c", 5),
            (".TT.c", 5),
            (".T..c", 5),
            (".T.T.c", 6)
        };

        // 预热 JIT：计时只统计稳定态下的单次 Build。
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 4, (0, ".T.c")), 0, 4);
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 5, (0, ".TT.c")), 0, 5);
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 6, (0, ".T.T.c")), 0, 6);

        foreach ((string text, int cap) in corners)
        {
            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 应可制作");
            Assert.True(
                order.Note is null,
                order.Note ?? $"{text} 通过时不应有未覆盖说明");

            /*
             * 路线核对（TODO.md §7.2 / §7.4 / §7.7）：五个角走「塔 + 一次 Pin +
             * 构造 C」，`.TT.c` 走「两刀定向碎晶 + 构造 C」，都不再以碎晶造洞为主力。
             */
            (AngleAlgorithm Algorithm,
             AngleConstruction Construction,
             int Pins,
             int Cuts) route = text switch
            {
                ".T.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.C, 1, 0),
                "T.T.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.C, 1, 0),
                "P.T.c" => (AngleAlgorithm.Algorithm5, AngleConstruction.C, 1, 0),
                ".TT.c" => (AngleAlgorithm.Algorithm2, AngleConstruction.D, 0, 2),
                ".T..c" => (AngleAlgorithm.Algorithm6, AngleConstruction.C, 1, 0),
                ".T.T.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.C, 1, 0),
                _ => throw new InvalidOperationException("未登记的回归角：" + text)
            };

            Assert.Equal(route.Algorithm, order.Algorithm);
            Assert.Equal(route.Construction, order.Construction);
            Assert.Equal(
                route.Pins,
                order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));
            Assert.Equal(
                route.Cuts,
                order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut));

            // 最终列逐格等于目标（类型、形状码、颜色都一致）。
            AngleWorkStep targetStep = Step(order, AngleWorkStepKind.Target);

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(targetStep.Shape!, 0),
                $"{text} 的末步目标列应逐格等于目标");
            AssertColumnCells(targetStep.Shape!, 0, text);

            // 最后一条操作步（Pin/Cut/Collapse/Support）也必须已经等于目标。
            AngleWorkStep lastOperational = order.Steps.Last(step =>
                step.Shape is not null &&
                step.Kind is AngleWorkStepKind.Pin or
                    AngleWorkStepKind.Cut or
                    AngleWorkStepKind.Collapse or
                    AngleWorkStepKind.Support);

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(lastOperational.Shape!, 0),
                $"{text} 的最后一条操作步应逐格等于目标");

            // §7.1 必要条件：前态不能就是目标本身，且必须真的有临时材料。
            // 两刀配方有两条 PreState（第二刀前重新装桥柱），这里取第一条。
            ShapeDocument preState = order.Steps
                .First(step =>
                    step.Kind == AngleWorkStepKind.PreState &&
                    step.Shape is not null)
                .Shape!;
            string preColumn = ShapeSignature.ColumnText(preState, 0);

            Assert.NotEqual(
                text,
                preColumn,
                $"{text} 的前态不能等于目标本身，否则就是循环");
            Assert.True(
                CountTemporaryCrystals(preState, target) >= 1,
                $"{text} 的前态至少要有 1 颗目标里没有的临时晶体");
            Assert.True(ShapeOps.IsStable(preState), $"{text} 的前态应稳定");

            // §6 第 1 条：前态每一列都要过五项条件（`.TT.c` 有两条前态，全部检查）。
            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out int badPreColumn),
                $"{text} 的前态第 {badPreColumn + 1} 象限不是合法列");
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                $"{text} 的前态不应含非法列");

            // 步骤链用真实物理回放。
            if (route.Construction == AngleConstruction.C)
            {
                Assert.True(
                    order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin),
                    $"{text} 走「抬升/临时晶体 + Pin」路线，步骤链必须有 Pin 步");
                Assert.True(
                    ShapeOps.Same(
                        ShapePhysics.Pin(preState).Document,
                        Step(order, AngleWorkStepKind.Pin).Shape),
                    $"{text} 的 Pin 步应等于真实顶针结果");
                Assert.Equal(
                    text,
                    ShapeSignature.ColumnText(
                        Step(order, AngleWorkStepKind.Support).Shape!,
                        0),
                    $"{text} 的构造 C 交接后目标列应等于目标");
            }
            else if (route.Construction == AngleConstruction.D)
            {
                // §7.4：两刀定向碎晶，不是单刀填充+暴破，也不混入 Pin。
                Assert.False(
                    order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin),
                    $"{text} 的构造 D 配方不应混入 Pin 步");
                ReplayTwoCutCascade(order, target, 0);
            }
            else
            {
                ReplayAndVerifyColumn(order, target, 0);
            }

            /*
             * §7.1 第 3 处：`.T..c` 的塔列不能含正在求解的子形态 `.T.c`。
             * 正解是 `..Tc.`（普通格紧贴晶体），不是 `.T.c.`。
             */
            if (text == ".T..c")
            {
                Assert.False(
                    preColumn.Contains(".T.c", StringComparison.Ordinal),
                    $"`.T..c` 的塔列不应含 `.T.c` 子形态，实际为 {preColumn}");
            }
        }

        // §7.1 第 2 条：`.TT.c` 的耗时应在与 `.T.c` 同量级（留余量取 30 ms）。
        ShapeDocument expensive = FromColumns(4, 5, (0, ".TT.c"));

        _ = AngleWorkOrderBuilder.Build(expensive, 0, 5);

        Stopwatch stopwatch = Stopwatch.StartNew();

        AngleWorkOrder fast = AngleWorkOrderBuilder.Build(expensive, 0, 5);

        stopwatch.Stop();

        Assert.True(fast.IsComplete, fast.Note ?? ".TT.c 应可制作");
        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds < 30.0,
            $".TT.c 单次 Build 耗时 {stopwatch.Elapsed.TotalMilliseconds:F2} ms，" +
            "应在 30 ms 以内");
    }

    /// <summary>
    /// TODO.md §7.2 用户给出的两座正解塔（5 层 × 4 象限）：
    /// 原样解析后 X 稳定、可交换，且 <c>顶针(X)</c> 的目标列逐格等于目标。
    ///
    /// <para>
    /// 另外核对生成器真的按同一路线走：<c>T.T.c</c> 用算法 6 的底块提升塔
    /// （前态角列 <c>.TTc.</c>，逐列结构就是用户给的塔），<c>P.T.c</c> 用
    /// 算法 5 的底针埋晶塔（前态角列 <c>ccTc.</c>）；两者都有 Pin 步与构造 C。
    /// </para>
    /// </summary>
    public static void UserGivenTowersPinToTarget()
    {
        var towers = new (string Name, string Code, string Expected)[]
        {
            (
                "T.T.c",
                "--CucrP-:CucrcrP-:CuCucrP-:cr--crCu:----cr--",
                "T.T.c"),
            (
                "P.T.c",
                "crcrcrP-:crCucrP-:CuCucrP-:cr--crCu:----cr--",
                "P.T.c")
        };

        foreach ((string name, string code, string expected) in towers)
        {
            ShapeDocument tower = Parse(code, 5, 4);

            Assert.Equal(5, tower.LayerCount);
            Assert.Equal(4, tower.PartCount);

            Assert.True(ShapeOps.IsStable(tower), $"{name} 的塔 X 应稳定");
            Assert.True(Swappable.Test(tower), $"{name} 的塔 X 应可交换");

            PhysicsResult pin = ShapePhysics.Pin(tower);

            Assert.True(pin.Success, pin.Error ?? "顶针失败");

            Assert.Equal(
                expected,
                ShapeSignature.ColumnText(pin.Document!, 0),
                $"{name} 的 顶针(X) 第 1 列应逐格等于目标");

            // 顶针不改变晶体颜色：用塔自己角列的最高晶体作为期望颜色。
            int towerCrystalLayer = -1;

            for (int layer = 0; layer < tower.LayerCount; layer++)
            {
                if (tower[layer, 0].Type == CellType.Crystal)
                {
                    towerCrystalLayer = layer;
                }
            }

            Assert.True(towerCrystalLayer >= 0, $"{name} 的塔角列应有晶体");

            char crystalColor =
                tower[towerCrystalLayer, 0].ColorCode ?? 'u';

            // 逐格核对类型、普通格的形状/颜色，以及晶体的颜色。
            for (int layer = 0; layer < expected.Length; layer++)
            {
                ShapeCell cell = pin.Document![layer, 0];

                switch (expected[layer])
                {
                    case '.':
                        Assert.Equal(CellType.Empty, cell.Type);
                        break;

                    case 'T':
                        Assert.Equal(CellType.Shape, cell.Type);
                        Assert.Equal('C', cell.ShapeCode);
                        Assert.Equal('u', cell.ColorCode);
                        break;

                    case 'P':
                        Assert.Equal(CellType.Pin, cell.Type);
                        break;

                    case 'c':
                        Assert.Equal(CellType.Crystal, cell.Type);
                        Assert.Equal(
                            crystalColor,
                            cell.ColorCode,
                            $"{name} 顶针后的晶体应保留原颜色");
                        break;
                }
            }
        }

        // `T.T.c` 的塔就是用户给的那一座：逐列结构一致。
        ShapeDocument generatedTt = Step(
                AngleWorkOrderBuilder.Build(
                    FromColumns(4, 5, (0, "T.T.c")),
                    0,
                    5),
                AngleWorkStepKind.PreState)
            .Shape!;

        ShapeDocument userTt = Parse(
            "--CucrP-:CucrcrP-:CuCucrP-:cr--crCu:----cr--",
            5,
            4);

        for (int part = 0; part < 4; part++)
        {
            Assert.Equal(
                ShapeSignature.ColumnText(userTt, part),
                ShapeSignature.ColumnText(generatedTt, part),
                $"T.T.c 的造塔前态第 {part + 1} 象限应与用户给出的塔一致");
        }

        // 两条路线都要有 Pin 步与构造 C。
        foreach (string text in new[] { "T.T.c", "P.T.c" })
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns(4, 5, (0, text)),
                0,
                5);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Pin,
                AngleWorkStepKind.Support,
                AngleWorkStepKind.Target);

            Assert.True(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin),
                $"{text} 的塔式路线必须有 Pin 步");
        }
    }

    #endregion

    #region 角工序：未覆盖与不可制作

    /// <summary>
    /// 合法的非 S 列 <c>cT.T.c</c> 必须诚实返回「未覆盖」，Note 不能写成「不可制作」。
    ///
    /// 本生成器只报告自己的覆盖范围；「不可制作」只由五项条件判定，
    /// 所以所有合法但没覆盖的 Note 都以「未覆盖：」开头。
    /// </summary>
    public static void PillarHolderDoesNotClaimNonSCriterion()
    {
        ShapeDocument target = FromColumns(4, 6, (0, "cT.T.c"));

        Assert.True(
            ShapeSignature.IsLegalColumn(target, 0),
            "cT.T.c 应是合法列");

        Assert.False(
            PillarSCriterion.IsPillarColumn(target, 0),
            "cT.T.c 不应属于 n = 4 的 S");

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

        Assert.False(order.IsComplete, "不在 S 的合法列不能被硬说成已完成");
        Assert.True(order.Note is not null, "未完成时必须给出 Note");

        Assert.True(
            order.Note!.StartsWith(
                AngleWorkOrderBuilder.UncoveredPrefix,
                StringComparison.Ordinal),
            "Note 必须以「未覆盖：」开头：" + order.Note);

        Assert.False(
            order.Note.Contains("不可制作", StringComparison.Ordinal),
            "未覆盖的 Note 不能暗示不可制作：" + order.Note);

        Assert.True(
            order.Limitations.Contains(
                AngleWorkOrderBuilder.UncoveredDisclaimer),
            "限制说明应明确「未覆盖不代表不可制作」");

        Assert.Equal(AngleWorkStepKind.Note, order.Steps[^1].Kind);

        Assert.True(
            order.Steps[^1].Title.StartsWith("未覆盖", StringComparison.Ordinal),
            "末步标题应标成未覆盖：" + order.Steps[^1].Title);

        Assert.False(
            order.Steps.Any(step =>
                step.Kind is AngleWorkStepKind.Cut or
                AngleWorkStepKind.Collapse or
                AngleWorkStepKind.Target),
            "验证失败后不能伪造后续步骤");
    }

    /// <summary>
    /// 验收条件一（docs §6 第 1 条 / TODO.md §7.7）：<b>前态的每一列都必须
    /// 通过五项条件</b>。不通过时该候选作废，Note 必须写「前态存在非法列」。
    ///
    /// <para>
    /// 反例直接用 <c>.T.T.c</c> 的旧碎晶前态 <c>cTcT.c</c>
    /// （第 3 层与第 6 层是晶体，中间只有 1 枚普通格 ⇒ 条件五失败）。
    /// 修复后的 <c>.T.T.c</c> 走算法 6 的塔式路线，前态 <c>.T.Tc.</c> 每列合法；
    /// 而另一个仍只生成非法前态的合法角 <c>.T.cT.</c> 必须被如实拒成「未覆盖」。
    /// </para>
    /// </summary>
    public static void IllegalPreStateIsRejected()
    {
        // ── 反例：旧的碎晶前态 cTcT.c ──
        ShapeDocument oldPreState = FromColumns(4, 6, (0, "cTcT.c"));

        ColumnCheck check = ShapeSignature.CheckColumn(oldPreState, 0);

        Assert.False(check.IsLegal, "cTcT.c 应在五项条件下不合法");
        Assert.True(
            check.Failures.Any(failure =>
                failure.Contains("条件五", StringComparison.Ordinal)),
            "cTcT.c 违反的应是条件五（晶体之间的洞缺少两枚普通资源）：" +
            string.Join("；", check.Failures));

        ShapeDocument target = FromColumns(4, 6, (0, ".T.T.c"));

        /*
         * 即使一份工序单的其它步骤都「回放通过」，只要前态含非法列，
         * HasIllegalPreState 也必须把它判成假阳性。
         */
        var forged = new AngleWorkOrder(
            0,
            AngleBuildKind.NeedsCrystalBreak,
            new[]
            {
                new AngleWorkStep(
                    "旧碎晶前态 cTcT.c",
                    oldPreState,
                    AngleWorkStepKind.PreState),
                new AngleWorkStep(
                    "目标角 .T.T.c",
                    target,
                    AngleWorkStepKind.Target)
            },
            null)
        {
            IsComplete = true
        };

        Assert.True(
            forged.HasIllegalPreState(out int preStateIndex, out int badColumn),
            "前态 cTcT.c 的非法列必须被识别出来");
        Assert.Equal(0, preStateIndex, "非法列在第 1 条前态里");
        Assert.Equal(0, badColumn, "非法列是第 1 象限");

        // ── 修复后的 .T.T.c：前态是合法的塔 .T.Tc. ──
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6);

        Assert.True(order.IsComplete, order.Note ?? ".T.T.c 应可制作");
        Assert.False(
            order.HasIllegalPreState(out _, out _),
            "修复后的 .T.T.c 前态不应含非法列");

        foreach (AngleWorkStep step in order.Steps.Where(
                     step => step.Kind == AngleWorkStepKind.PreState))
        {
            bool legal = ShapeSignature.AllColumnsLegal(
                step.Shape!,
                out int badPreColumn);

            Assert.True(
                legal,
                legal
                    ? "前态列不合法"
                    : $"第 {badPreColumn + 1} 象限的前态列不合法：" +
                      ShapeSignature.ColumnText(step.Shape!, badPreColumn));
        }

        // ── 合法目标 .T.cT.：唯一碎晶前态 cT.cT. 非法 ⇒ 候选作废 ──
        ShapeDocument rejectedTarget = FromColumns(4, 6, (0, ".T.cT."));

        Assert.True(
            ShapeSignature.IsLegalColumn(rejectedTarget, 0),
            ".T.cT. 本身应是合法角");

        AngleWorkOrder rejected =
            AngleWorkOrderBuilder.Build(rejectedTarget, 0, 6);

        Assert.False(
            rejected.IsComplete,
            "唯一的碎晶前态非法时，工序不能标成完成");
        Assert.True(rejected.Note is not null, "未完成时必须给出 Note");
        Assert.True(
            rejected.Note!.StartsWith(
                AngleWorkOrderBuilder.UncoveredPrefix,
                StringComparison.Ordinal),
            "Note 必须以「未覆盖：」开头：" + rejected.Note);
        Assert.True(
            rejected.Note.Contains(
                AngleWorkOrderBuilder.IllegalPreStateReason,
                StringComparison.Ordinal),
            "Note 必须写明「前态存在非法列」：" + rejected.Note);

        Assert.False(
            rejected.Steps.Any(step =>
                step.Kind is AngleWorkStepKind.PreState or
                AngleWorkStepKind.Cut or
                AngleWorkStepKind.Collapse or
                AngleWorkStepKind.Pin or
                AngleWorkStepKind.Support or
                AngleWorkStepKind.Target),
            "作废的候选不能留下任何操作步");
    }

    /// <summary>
    /// 五项条件不过的角：默认 Build 必须报「不可制作」，而不是「未覆盖」。
    ///
    /// <c>cT.ccc</c> 只在两枚晶体之间放了一枚普通格（条件五要求两枚），
    /// <c>cT..ccc</c> 同理。旧语料里它们能被构造机器回放出来，只是因为
    /// 生成器当时不看合法性；真实角不合法就不存在可制作的前态。
    /// </summary>
    public static void IllegalAngleIsNotProducible()
    {
        foreach ((string text, int parts, int layers) in new[]
                 {
                     ("cT.ccc", 4, 6),
                     ("cT..ccc", 4, 8)
                 })
        {
            ShapeDocument target = FromColumns(parts, layers, (0, text));

            ColumnCheck check = ShapeSignature.CheckColumn(target, 0);

            Assert.False(check.IsLegal, $"{text} 应在五项条件下不合法");
            Assert.True(check.Failures.Count > 0, "不合法必须给出失败的条件");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

            Assert.False(order.IsComplete, $"{text} 不合法，不能标成可制作");
            Assert.True(order.Note is not null, "不可制作必须给出 Note");

            Assert.True(
                order.Note!.StartsWith(
                    AngleWorkOrderBuilder.NotProduciblePrefix,
                    StringComparison.Ordinal),
                "Note 必须以「不可制作：」开头：" + order.Note);

            Assert.False(
                order.Note.Contains("未覆盖", StringComparison.Ordinal),
                "不合法角不能写成未覆盖：" + order.Note);

            Assert.Equal(AngleAlgorithm.None, order.Algorithm);
            Assert.Equal(AngleConstruction.None, order.Construction);
            Assert.Equal(0, order.Rank);
            Assert.Equal(1, order.Steps.Count);
            Assert.Equal(AngleWorkStepKind.Note, order.Steps[0].Kind);

            Assert.True(
                order.Steps[0].Title.StartsWith("不可制作", StringComparison.Ordinal),
                "步骤标题应标成不可制作：" + order.Steps[0].Title);

            Assert.False(
                order.Steps.Any(step =>
                    step.Kind is AngleWorkStepKind.Cut or
                    AngleWorkStepKind.Collapse or
                    AngleWorkStepKind.Pin or
                    AngleWorkStepKind.Support or
                    AngleWorkStepKind.Target),
                "不可制作时不能伪造任何操作步骤");
        }
    }

    /// <summary>
    /// 算法 6 的标杆案例 <c>.T.c</c>（TODO.md §3.1.1）：
    /// 造塔 X（交换链子配方）→ 顶针 → 构造 C → 目标角，rank 只算这一次顶针。
    /// </summary>
    public static void Algorithm6BuildsDotTcFromTower()
    {
        const int cap = 4;

        ShapeDocument target = FromColumns(4, cap, (0, ".T.c"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
        Assert.Equal(AngleConstruction.C, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? ".T.c 应可制作");
        Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

        // 步骤链：造塔（子配方）→ 顶针 → 构造 C → 目标角。
        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Pin,
            AngleWorkStepKind.Support,
            AngleWorkStepKind.Target);

        Assert.Equal(1, order.Rank);
        Assert.Equal(1, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

        ShapeDocument tower = order.Steps[0].Shape!;

        /*
         * 塔 X 的逐列结构就是 §3.1.1 用户给的那一座（4 层 × 4 象限）。
         * 晶体颜色属于语料（测试用 r，用户样例用 g），这里只比较结构。
         */
        ShapeDocument specTower =
            Parse("--cgcgCu:CuCucgP-:cr--cgCu:----cg--", 4, 4);

        for (int part = 0; part < 4; part++)
        {
            Assert.Equal(
                ShapeSignature.ColumnText(specTower, part),
                ShapeSignature.ColumnText(tower, part),
                $"造塔 X 第 {part + 1} 象限的逐层结构应与 §3.1.1 的塔一致");
        }

        // 角列的晶体颜色必须沿用目标列（顶针保留颜色；塔里晶体已下移一层）。
        Assert.Equal(
            'r',
            tower[2, 0].ColorCode,
            "造塔 X 的角列晶体颜色应与目标列一致");

        Assert.True(ShapeOps.IsStable(tower), "塔 X 应稳定");
        Assert.True(
            ShapeSignature.AllColumnsLegal(tower, out _),
            "塔 X 每列都应过五项条件");

        // 造塔子配方嵌在工序单里：rank 0 的交换链，四片 + 两次交换可展开。
        Assert.True(order.SubRecipe is not null, "算法 6 必须带造塔子配方");
        Assert.Equal(RecipeKind.Exchange, order.SubRecipe!.Kind);
        Assert.Equal(0, order.SubRecipe.Rank);
        Assert.True(
            ShapeOps.Same(order.SubRecipe.Shape, tower),
            "子配方产物必须等于前态（塔 X）");

        Assert.True(
            order.SubRecipe.Exchange is not null,
            "子配方应带交换明细（四片 + 两次交换）");

        Assert.True(
            order.SubRecipe.Steps.Count(step => step.Title.Contains("交换")) >= 3,
            "子配方步骤链应含两次半边交换 + 末次交换");

        Assert.True(
            order.SubRecipe.Steps.Any(step => step.Title.Contains("零件 1（含柱）")) &&
            order.SubRecipe.Steps.Any(step => step.Title.Contains("零件 2（含柱）")),
            "子配方应能展开成四个带柱半半边（左 1/2、右 1/2）");

        Assert.True(
            Replay.Verify(order.SubRecipe, tower, out IReadOnlyList<string> subErrors),
            "造塔子配方应能端到端回放：" + string.Join(",", subErrors));

        // 角工序只统计自己的顶针：子配方 rank 0 + 角顶针 1 = 1。
        Assert.Equal(0, order.SubRecipe.Rank);
        Assert.Equal(1, order.Rank);

        // 顶针步必须等于真实 ShapePhysics.Pin 的结果。
        Assert.True(
            ShapeOps.Same(
                ShapePhysics.Pin(tower).Document,
                order.Steps[1].Shape),
            "Pin 步快照应等于真实 ShapePhysics.Pin");

        // 最终列逐格等于 .T.c（顶针后与构造 C 后都要成立）。
        AssertColumnCells(order.Steps[1].Shape!, 0, ".T.c");
        AssertColumnCells(order.Steps[2].Shape!, 0, ".T.c");

        Assert.Equal(
            ".T.c",
            ShapeSignature.ColumnText(order.Steps[1].Shape!, 0),
            "顶针后目标列应逐格等于 .T.c");

        Assert.Equal(
            ".T.c",
            ShapeSignature.ColumnText(order.Steps[2].Shape!, 0),
            "构造 C 后目标列应逐格等于 .T.c");

        // 构造 C：装新支撑柱、拆旧柱与辅助列。
        ShapeDocument handover = order.Steps[2].Shape!;

        Assert.True(ShapeOps.IsStable(handover), "构造 C 交接后的图形应稳定");

        int supportColumn = order.AuxiliaryColumns[0];

        Assert.Equal(3, supportColumn, "新支撑柱应落在第 4 象限");
        Assert.True(
            ShapeSignature.ColumnText(handover, supportColumn) == "TTTT",
            "新支撑柱应是满高普通柱");

        foreach (int other in new[] { 1, 2 })
        {
            for (int layer = 0; layer < cap; layer++)
            {
                Assert.Equal(
                    CellType.Empty,
                    handover[layer, other].Type,
                    $"拆掉辅助列后第 {other + 1} 象限第 {layer + 1} 层应为空");
            }
        }

        // 末步得到目标角。
        Assert.True(
            ShapeOps.Same(
                target,
                Step(order, AngleWorkStepKind.Target).Shape),
            "末步应得到目标角");
    }

    /// <summary>
    /// 第六个回归角 <c>.T.T.c</c>（TODO.md §7.7）：<b>不是</b>碎晶路线，
    /// 而是算法 6 的塔式路线——<c>BuildShiftedTowerColumn</c> 给出
    /// <c>.T.Tc.</c>（晶体下移一层、缺口封上），一次 Pin 回到 <c>.T.T.c</c>，
    /// 最后用构造 C 换支撑柱。
    ///
    /// <para>
    /// 用户给出的塔（6 层 × 4 象限）原样解析后必须：稳定、可交换、每列过五项
    /// 条件，且 <c>Pin(X)</c> 的第 1 列逐格等于 <c>.T.T.c</c>。
    /// </para>
    /// </summary>
    public static void Algorithm6BuildsDotDotTcFromTower()
    {
        const int cap = 6;
        const string text = ".T.T.c";

        const string towerCode =
            "--crcrP-:CuCucrP-:--CucrP-:CuCucrP-:cr--crCu:----cr--";

        ShapeDocument specTower = Parse(towerCode, cap, 4);

        Assert.Equal(6, specTower.LayerCount);
        Assert.Equal(4, specTower.PartCount);

        // 原样解析的塔 X：稳定、可交换、每列合法。
        Assert.True(ShapeOps.IsStable(specTower), "X 应稳定");
        Assert.True(Swappable.Test(specTower), "X 应可交换");
        Assert.True(
            ShapeSignature.AllColumnsLegal(specTower, out int badColumn),
            $"X 每列都应过五项条件；首个非法列 = {badColumn + 1}");

        // X 第 1 列 = .T.Tc.，顶针后 = .T.T.c。
        Assert.Equal(
            ".T.Tc.",
            ShapeSignature.ColumnText(specTower, 0),
            "X 第 1 列应为 .T.Tc.");

        PhysicsResult specPin = ShapePhysics.Pin(specTower);

        Assert.True(specPin.Success, specPin.Error ?? "顶针失败");
        AssertColumnCells(specPin.Document!, 0, text);

        Assert.Equal(
            text,
            ShapeSignature.ColumnText(specPin.Document!, 0),
            "顶针(X) 第 1 列应逐格等于 .T.T.c");

        // 生成器必须走算法 6 / 构造 C：PreState → Pin → Support → Target。
        ShapeDocument target = FromColumns(4, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
        Assert.Equal(AngleConstruction.C, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? ".T.T.c 应可制作");
        Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Pin,
            AngleWorkStepKind.Support,
            AngleWorkStepKind.Target);

        Assert.Equal(1, order.Rank);
        Assert.Equal(
            1,
            order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

        ShapeDocument tower = order.Steps[0].Shape!;
        ShapeDocument pinned = order.Steps[1].Shape!;
        ShapeDocument handover = order.Steps[2].Shape!;

        // 前态就是用户给的塔：四列逐格一致，且每列合法。
        for (int part = 0; part < 4; part++)
        {
            Assert.Equal(
                ShapeSignature.ColumnText(specTower, part),
                ShapeSignature.ColumnText(tower, part),
                $"造塔前态第 {part + 1} 象限应与用户塔一致");
        }

        Assert.True(
            ShapeSignature.AllColumnsLegal(tower, out _),
            "前态每列都应过五项条件");
        Assert.False(
            order.HasIllegalPreState(out _, out _),
            "算法 6 的前态不应含非法列");
        Assert.True(ShapeOps.IsStable(tower), "前态应稳定");

        // 角色排法与用户塔一致：托针柱在第 4 象限，落块柱在第 2 象限，
        // 引爆柱在第 3 象限。
        Assert.Equal(3, order.AuxiliaryColumns[0]);
        Assert.Equal(1, order.AuxiliaryColumns[1]);
        Assert.Equal(2, order.AuxiliaryColumns[2]);

        // 造塔子配方：rank 0 的交换链，能端到端回放。
        Assert.True(order.SubRecipe is not null, "算法 6 必须带造塔子配方");
        Assert.Equal(RecipeKind.Exchange, order.SubRecipe!.Kind);
        Assert.Equal(0, order.SubRecipe.Rank);
        Assert.True(
            ShapeOps.Same(order.SubRecipe.Shape, tower),
            "子配方产物必须等于前态（塔 X）");
        Assert.True(
            ExchangeChain.TryBuild(tower, out _),
            "塔 X 应能用交换链装配");
        Assert.True(
            Replay.Verify(
                order.SubRecipe,
                tower,
                out IReadOnlyList<string> subErrors),
            "造塔子配方应能端到端回放：" + string.Join(",", subErrors));

        // Pin 步等于真实顶针；顶针后与构造 C 后的目标列都逐格等于目标。
        Assert.True(
            ShapeOps.Same(ShapePhysics.Pin(tower).Document, pinned),
            "Pin 步快照应等于真实顶针结果");
        AssertColumnCells(pinned, 0, text);
        Assert.Equal(
            text,
            ShapeSignature.ColumnText(pinned, 0),
            "顶针后的目标列应逐格等于 .T.T.c");

        AssertColumnCells(handover, 0, text);
        Assert.Equal(
            text,
            ShapeSignature.ColumnText(handover, 0),
            "构造 C 交接后目标列应逐格等于 .T.T.c");
        Assert.True(ShapeOps.IsStable(handover), "构造 C 交接后应稳定");

        // 构造 C 只保留目标列与满高新支撑柱，旧辅助列全部拆掉。
        int supportColumn = order.AuxiliaryColumns[0];

        Assert.Equal(
            new string('T', cap),
            ShapeSignature.ColumnText(handover, supportColumn),
            "新支撑柱应是满高普通柱");

        for (int other = 0; other < 4; other++)
        {
            if (other == 0 || other == supportColumn)
            {
                continue;
            }

            for (int layer = 0; layer < cap; layer++)
            {
                Assert.Equal(
                    CellType.Empty,
                    handover[layer, other].Type,
                    $"拆掉辅助列后第 {other + 1} 象限第 {layer + 1} 层应为空");
            }
        }

        // 末步得到目标角。
        Assert.True(
            ShapeOps.Same(
                target,
                Step(order, AngleWorkStepKind.Target).Shape),
            "末步应得到目标角");
    }

    /// <summary>
    /// 算法 5：n = 4、底格是针 ⇒ 构造 H 一次性批量恢复。
    /// <c>PTT.c</c> 底格是针、缺口在晶体塔下，正好由构造 H 完成。
    /// </summary>
    public static void Algorithm5BuildsBottomPinAngle()
    {
        const int cap = 5;

        ShapeDocument target = FromColumns(4, cap, (0, "PTT.c"));

        Assert.Equal(CellType.Pin, target[0, 0].Type);

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.Equal(AngleAlgorithm.Algorithm5, order.Algorithm);
        Assert.Equal(AngleConstruction.H, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? "PTT.c 的构造 H 应验证通过");
        Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Pin,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        Assert.Equal(1, order.Rank);
        Assert.Equal(1, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        Assert.True(ShapeOps.IsStable(preState), "构造 H 的前态应稳定");

        // 真实 Pin 回放。
        Assert.True(
            ShapeOps.Same(
                ShapePhysics.Pin(preState).Document,
                Step(order, AngleWorkStepKind.Pin).Shape),
            "Pin 步快照应等于真实 ShapePhysics.Pin");

        Assert.Equal(
            "PTT.c",
            ShapeSignature.ColumnText(
                Step(order, AngleWorkStepKind.Pin).Shape!,
                0),
            "一次顶针后的目标列应逐格等于目标");

        Assert.True(
            ShapeOps.Same(
                target,
                Step(order, AngleWorkStepKind.Target).Shape),
            "末步应得到目标角");
    }

    /// <summary>
    /// §6 的 n ≥ 6 分档：算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回），
    /// 以及 n = 4 其余的算法 4（递归提升 + 构造 G 环下落）。
    /// </summary>
    public static void AlgorithmTiersSelectPaperBranch()
    {
        // 算法 1（提升）：缺口下方有普通格，先抬到缺口顶端再碎晶。
        AngleWorkOrder lift = AngleWorkOrderBuilder.Build(
            FromColumns(6, 6, (0, "T..ccc")),
            0,
            6);

        Assert.Equal(AngleAlgorithm.Algorithm1, lift.Algorithm);
        Assert.Equal(AngleConstruction.FillBreak, lift.Construction);
        Assert.True(lift.IsComplete, lift.Note ?? "算法 1 应验证通过");
        Assert.Equal(
            "ccTccc",
            ShapeSignature.ColumnText(
                Step(lift, AngleWorkStepKind.PreState).Shape!,
                0),
            "算法 1 的前态应把 T 抬到缺口顶端");

        // 算法 2（正规核心）：缺口只能直接填成紧核心（上下都挨着普通格）。
        AngleWorkOrder core = AngleWorkOrderBuilder.Build(
            FromColumns(6, 8, (0, "cT..Tccc")),
            0,
            8);

        Assert.Equal(AngleAlgorithm.Algorithm2, core.Algorithm);
        Assert.Equal(AngleConstruction.FillBreak, core.Construction);
        Assert.True(core.IsComplete, core.Note ?? "算法 2 应验证通过");
        Assert.Equal(
            "cTccTccc",
            ShapeSignature.ColumnText(
                Step(core, AngleWorkStepKind.PreState).Shape!,
                0),
            "算法 2 的前态应把缺口直接填成紧核心");

        // 算法 3（构造 F 送回）：一次受控下落到 d 层。
        AngleWorkOrder f = AngleWorkOrderBuilder.Build(
            FromColumns(6, 7, (0, ".T..c..")),
            0,
            7);

        Assert.Equal(AngleAlgorithm.Algorithm3, f.Algorithm);
        Assert.Equal(AngleConstruction.F, f.Construction);
        Assert.True(f.IsComplete, f.Note ?? "算法 3 应验证通过");

        // 算法 4（n = 4、D 不成立）：§11 通用实现（规范计划 + 变体 + 回放）。
        // 旧实现落到兜底链的构造 G；按 §2 第 2 条，路线 F 先走 §11 通用实现，
        // 构造原语是定向碎晶（构造 D），断言随之更新。
        AngleWorkOrder g = AngleWorkOrderBuilder.Build(
            FromColumns(4, 8, (0, "cT.T.ccc")),
            0,
            8);

        Assert.Equal(AngleAlgorithm.Algorithm4, g.Algorithm);
        Assert.Equal(AngleConstruction.D, g.Construction);
        Assert.True(g.IsComplete, g.Note ?? "算法 4 应验证通过");
    }

    #endregion

    #region 角工序：算法 4 的 §11 通用实现（六条验收语料）

    /// <summary>
    /// §11 通用实现（路线 F）的验收语料，逐格比对：
    /// <c>T.ccc</c>(5) / <c>cTT.cccT.T.ccc</c>(14) / <c>cT.T.T.T...ccc</c>(14) /
    /// <c>cT.T....T..ccc</c>(14) / <c>cTT..T.c</c>(8) 必须完成；
    /// <c>cT.T.T.c</c>(8) 是 §4.10 的反例（F 不成立）⇒ 必须判「未覆盖」而不是完成。
    ///
    /// <para>每个通过案例都断言：</para>
    /// <list type="bullet">
    /// <item><see cref="AngleWorkOrder.Algorithm"/> = 算法 4、<see cref="AngleWorkOrder.Construction"/> = 构造 D；</item>
    /// <item><see cref="AngleWorkOrder.IsComplete"/> 且不带 Note；</item>
    /// <item>每条前态的每一列过五项条件、前态稳定、前态目标列 ≠ 目标；</item>
    /// <item>每一步用真实 <see cref="ShapePhysics"/> 逐格回放（§11.4 六条）；</item>
    /// <item>最终列逐格等于目标；</item>
    /// <item>单角耗时 &lt; 50 ms（变体搜索有预算上限）。</item>
    /// </list>
    /// <para>
    /// 语料里的前态锚点（<c>T.ccc</c> 的 <c>cTccc</c>、<c>cTT..T.c</c> 的 <c>cTccTcTc</c>）
    /// 与文档 §4.7 / §4.9 的实测前态一致，用来防止「换个前态蒙过去」。
    /// </para>
    /// </summary>
    public static void Algorithm4GenericPlanCoversAcceptanceCorpus()
    {
        var cases = new (string Text, int Layers, string? PreStateAnchor)[]
        {
            ("T.ccc", 5, "cTccc"),
            ("cTT.cccT.T.ccc", 14, null),
            ("cT.T.T.T...ccc", 14, null),
            ("cT.T....T..ccc", 14, null),
            ("cTT..T.c", 8, "cTccTcTc")
        };

        // 预热 JIT：首调用包含 JIT，不能算进「单角耗时 < 50 ms」。
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 5, (0, "T.ccc")), 0, 5);

        foreach ((string text, int layers, string? preStateAnchor) in cases)
        {
            ShapeDocument target = FromColumns(4, layers, (0, text));

            AngleWorkOrder order;

            var watch = Stopwatch.StartNew();

            order = AngleWorkOrderBuilder.Build(target, 0, layers);

            watch.Stop();

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind, text);
            Assert.True(order.IsComplete, order.Note ?? $"{text} 未覆盖");
            Assert.True(order.Note is null, order.Note ?? $"{text} 通过时不应有 Note");
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm, text);
            Assert.Equal(AngleConstruction.D, order.Construction, text);
            Assert.Equal(0, order.Rank, text);
            Assert.True(
                watch.ElapsedMilliseconds < 50,
                $"{text} 单角耗时 {watch.ElapsedMilliseconds} ms，应 < 50 ms");

            // 结构谓词（§1.3 / §4.7–§4.9）：五项 ✔、D ✘、F ✔。
            Assert.True(ShapeSignature.CheckColumn(text).IsLegal, text + " 五项条件应通过");
            Assert.False(PillarSCriterion.BottomResourceD(text), text + " 的 D 应不成立");
            Assert.True(PillarSCriterion.RecursiveTerminalF(text), text + " 的 F 应成立");

            // §11.4 第 1–3 条：前态每列合法、稳定、目标列 ≠ 目标。
            // 逐块各一刀时会有多条前态，这里取第一条（规范核心列那条）。
            ShapeDocument preState = order.Steps
                .First(step =>
                    step.Kind == AngleWorkStepKind.PreState &&
                    step.Shape is not null)
                .Shape!;

            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out int badColumn),
                $"{text} 的前态第 {badColumn + 1} 象限不是合法列");
            Assert.True(ShapeOps.IsStable(preState), $"{text} 的前态应稳定");
            Assert.NotEqual(
                text,
                ShapeSignature.ColumnText(preState, 0),
                $"{text} 的前态目标列必须与目标不同");

            if (preStateAnchor is not null)
            {
                Assert.Equal(
                    preStateAnchor,
                    ShapeSignature.ColumnText(preState, 0),
                    $"{text} 的规范核心列应与文档实测前态一致");
            }

            // §11.4 第 4–5 条：每一刀逐格回放、交接与末步等于目标。
            ReplayGenericAlgorithm4(order, target, 0);

            AssertColumnCells(Step(order, AngleWorkStepKind.Target).Shape!, 0, text);
        }

        // §4.10 的反例：cT.T.T.c 的 D ✘、F ✘ ⇒ S ✘ ⇒ 只能报「未覆盖」。
        {
            const string counter = "cT.T.T.c";

            ShapeDocument target = FromColumns(4, 8, (0, counter));

            Assert.False(
                PillarSCriterion.BottomResourceD(counter),
                "cT.T.T.c 的 D 不应成立");
            Assert.False(
                PillarSCriterion.RecursiveTerminalF(counter),
                "cT.T.T.c 的 F 不应成立");
            Assert.False(
                PillarSCriterion.IsPillarColumn(target, 0),
                "cT.T.T.c 不属于 n = 4 的 S");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8);

            Assert.False(order.IsComplete, "F 不成立的反例必须报未覆盖，不能报完成");
            Assert.True(order.Note is not null, "必须给出未覆盖说明");
            Assert.True(
                order.Note!.StartsWith(
                    AngleWorkOrderBuilder.UncoveredPrefix,
                    StringComparison.Ordinal),
                "失败前缀必须是「未覆盖」而不是「不可制作」：" + order.Note);
            Assert.False(
                order.Steps.Any(step =>
                    step.Kind is AngleWorkStepKind.Cut or
                        AngleWorkStepKind.Collapse or
                        AngleWorkStepKind.Support or
                        AngleWorkStepKind.Target),
                "未覆盖时不能伪造后续步骤");
        }
    }

    #endregion

    #region 角工序：n ≥ 6（算法 1/2/3 + 构造 F）

    /// <summary>
    /// n ≥ 6 的碎晶角（文档 §9 第 1 条）：分档是
    /// <c>算法 1（提升）→ 算法 2（正规核心）→ 算法 3（构造 F 送回）</c>，
    /// 三条算法都输出列级步骤链
    /// <c>PreState → Cut → Collapse → Support → Target</c>。
    ///
    /// <para>
    /// 每个案例都按 §6 的六条验收逐条核对：前态每列五项条件、前态目标列 ≠ 目标列、
    /// 前态与中间态稳定、每一步真实物理回放、末步逐格等于目标。
    /// </para>
    /// </summary>
    public static void SixPlusCornersFollowAlgorithmTiers()
    {
        var cases = new (int Parts, int Layers, string Text, AngleAlgorithm Algorithm, AngleConstruction Construction)[]
        {
            // 算法 1（提升）：缺口在晶体塔下方，先把下方的普通格抬到缺口顶端。
            (6, 6, "T..c..", AngleAlgorithm.Algorithm1, AngleConstruction.FillBreak),

            // 算法 1（提升），缺口高两层。
            (6, 6, "T...c.", AngleAlgorithm.Algorithm1, AngleConstruction.FillBreak),

            // 算法 2（正规核心）：缺口直接填成紧核心。
            (6, 6, "cT..Tc", AngleAlgorithm.Algorithm2, AngleConstruction.FillBreak),

            // 算法 3（构造 F）：缺口就在晶体段正下方，一次受控下落到 d 层。
            (6, 6, ".T..c.", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 算法 3（构造 F），缺口在下方那个晶体段正下方（不在最高晶体下方）。
            (6, 6, ".T.cTc", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 算法 3（构造 F），n = 8。
            (8, 8, "..T.c...", AngleAlgorithm.Algorithm3, AngleConstruction.F)
        };

        foreach ((int parts, int layers, string text, AngleAlgorithm algorithm,
                  AngleConstruction construction) in cases)
        {
            ShapeDocument target = FromColumns(parts, layers, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

            string label = $"n={parts} 的角 {text}";

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind, label);

            Assert.True(
                order.IsComplete,
                label + " 应完成：" + (order.Note ?? "?"));

            Assert.True(order.Note is null, label + " 不应带 Note");
            Assert.Equal(algorithm, order.Algorithm, label + " 的分档不符");
            Assert.Equal(construction, order.Construction, label + " 的构造不符");
            Assert.Equal(0, order.Rank, label + " 没有顶针步，rank 应为 0");

            // ── 列级步骤链 ──
            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
                AngleWorkStepKind.Collapse,
                AngleWorkStepKind.Support,
                AngleWorkStepKind.Target);

            // ── 验收一：前态每一列都过五项条件 ──
            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.False(
                order.HasIllegalPreState(out int badPreState, out int badColumn),
                label + " 的前态有非法列：" +
                $"第 {badPreState + 1} 条前态第 {badColumn + 1} 象限");

            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out int firstBad),
                label + " 的前态第 " + (firstBad + 1) + " 象限不过五项条件");

            // ── 验收二：前态的目标列 ≠ 目标列（§5 规则四，禁止循环）──
            Assert.NotEqual(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(preState, 0),
                label + " 的前态目标列不能等于目标列");

            Assert.True(
                CountTemporaryCrystals(preState, target) > 0,
                label + " 的前态至少要有一处临时晶体（真的在施工）");

            // ── 验收三：前态与中间态稳定 ──
            Assert.True(ShapeOps.IsStable(preState), label + " 的前态应稳定");

            // ── 验收四：每一步都用真实 ShapePhysics 回放并逐格核对 ──
            ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

            CutResult cut = ShapePhysics.Cut(rotated);

            Assert.True(cut.Success, cut.Error ?? "切割失败");

            ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

            ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

            /*
             * Cut 步是「半破后、下落前」的快照：对它再做一次真实 Collapse，
             * 必须逐格等于真实 Cut 出来的西半边（也就是 Collapse 步）。
             */
            Assert.True(
                ShapeOps.Same(
                    replayed,
                    ShapePhysics.Collapse(cutStep).Document!),
                label + " 的 Cut 步用真实 Collapse 重放后应等于真实 Cut 的西半边");

            Assert.True(
                ShapeOps.Same(replayed, Step(order, AngleWorkStepKind.Collapse).Shape),
                label + " 的 Collapse 步应等于真实 Cut 出来的西半边");

            /*
             * 切割只能删掉辅助列/填充的晶体：目标列原有的晶体一颗都不能少。
             * （前态目标列里的临时填充晶体本来就该在这一刀里消失，所以不能拿
             * 整列文本去比，只能比保留晶体的数量——与 ReplayAndVerifyColumn 一致。）
             */
            Assert.Equal(
                CountCrystals(target, 0),
                CountCrystals(cutStep, 0),
                label + " 的目标列晶体塔在切割后必须完好");

            foreach (int auxiliary in order.AuxiliaryColumns)
            {
                Assert.Equal(
                    0,
                    CountCrystals(cutStep, auxiliary),
                    label + " 的辅助列 " + auxiliary + " 晶体应全部被打碎");
            }

            ShapeDocument collapsed = Step(order, AngleWorkStepKind.Collapse).Shape!;

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(collapsed, 0),
                label + " 的下落后目标列应逐格等于目标");

            Assert.True(
                ShapeOps.IsStable(collapsed),
                label + " 下落后的中间态应稳定");

            // ── 验收：构造 C 交接后仍然稳定、目标列不变 ──
            ShapeDocument handover = Step(order, AngleWorkStepKind.Support).Shape!;

            Assert.True(ShapeOps.IsStable(handover), label + " 交接后应稳定");

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(handover, 0),
                label + " 交接不得改动目标列");

            /*
             * 构造 C 之后只剩「目标列 + 一根满高新支撑柱」：辅助列要么被拆掉，
             * 要么就地换成那根新支撑柱（规则：先装新柱、再拆旧柱与辅助材料）。
             */
            string emptyColumn = new('.', layers);
            string fullPillar = new('T', layers);

            int[] remaining = Enumerable.Range(0, parts)
                .Where(part =>
                    ShapeSignature.ColumnText(handover, part) != emptyColumn)
                .ToArray();

            Assert.Equal(2, remaining.Length, label + " 交接后只应剩两列");
            Assert.True(remaining.Contains(0), label + " 交接后必须保留目标列");
            Assert.True(
                remaining.Any(part =>
                    part != 0 &&
                    ShapeSignature.ColumnText(handover, part) == fullPillar),
                label + " 交接后应装一根满高新支撑柱");

            // ── 验收五：最终逐格等于目标 ──
            Assert.True(
                ShapeOps.Same(target, Step(order, AngleWorkStepKind.Target).Shape),
                label + " 的末步应逐格等于目标");
        }

        // cap 高于目标层数：层号照样由扫描得出，分档不受影响。
        ShapeDocument shortTarget = FromColumns(6, 4, (0, "T..c"));

        AngleWorkOrder tall = AngleWorkOrderBuilder.Build(shortTarget, 0, 6);

        Assert.Equal(AngleAlgorithm.Algorithm1, tall.Algorithm);
        Assert.True(tall.IsComplete, tall.Note ?? "cap 高于目标层数也应完成");
        Assert.Equal(
            "T..c..",
            ShapeSignature.ColumnText(
                Step(tall, AngleWorkStepKind.Collapse).Shape!,
                0),
            "cap 高于目标层数时，多出来的层应留空");
        Assert.True(
            ShapeOps.Same(shortTarget, Step(tall, AngleWorkStepKind.Target).Shape),
            "cap 高于目标层数时，末步仍应逐格等于目标");
    }

    /// <summary>
    /// 构造 F 的四列布局（文档 §3）：沿环四个连续位置
    /// <c>针槽柱 N | 目标 A | 辅助柱 B | 碎晶桥柱 E</c>，其余象限空；
    /// N 的第 s 层是针、B 的第 d..s−1 层是晶体、E 只有第 d 层晶体、其余都是 T；
    /// 目标列把 d 层那枚普通格抬到 s 层。
    ///
    /// <para>
    /// <c>.T..c..</c>（n = 6，7 层）：最高晶体段只有第 5 层一枚晶体，
    /// 段底端 <c>t = 4</c>（0 起）⇒ <c>s = 3</c>；<c>s</c> 以下最高的非空层
    /// <c>d = 1</c>。这里的层号全部由扫描得出，测试只是把扫描结果固定下来。
    /// </para>
    /// </summary>
    public static void ConstructFLaysFourColumns()
    {
        const int parts = 6;
        const int layers = 7;

        ShapeDocument target = FromColumns(parts, layers, (0, ".T..c.."));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

        Assert.Equal(AngleAlgorithm.Algorithm3, order.Algorithm);
        Assert.Equal(AngleConstruction.F, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? "构造 F 应验证通过");

        // 四连位：N = 第 6 象限，B = 第 2 象限，E = 第 3 象限（目标列是第 1 象限）。
        Assert.Equal(3, order.AuxiliaryColumns.Count);
        Assert.Equal(5, order.AuxiliaryColumns[0]);
        Assert.Equal(1, order.AuxiliaryColumns[1]);
        Assert.Equal(2, order.AuxiliaryColumns[2]);

        // 半破轴：p = half − 2 = 1 ⇒ 顺转 1 个象限后目标列落在第 2 位。
        Assert.Equal(1, order.Rotation);

        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        // 目标列 A：d = 1 的普通格抬到 s = 3，第 2 层（0 起 1）让出。
        Assert.Equal("...Tc..", ShapeSignature.ColumnText(preState, 0));

        // N：第 s 层针，其余 T。
        Assert.Equal("TTTPTTT", ShapeSignature.ColumnText(preState, 5));

        // B：第 d..s−1 层晶体通道，其余 T。
        Assert.Equal("TccTTTT", ShapeSignature.ColumnText(preState, 1));

        // E：只有第 d 层一枚晶体，其余 T。
        Assert.Equal("TcTTTTT", ShapeSignature.ColumnText(preState, 2));

        // 其余象限留空。
        foreach (int part in new[] { 3, 4 })
        {
            Assert.Equal(
                new string('.', layers),
                ShapeSignature.ColumnText(preState, part),
                $"第 {part + 1} 象限应留空");
        }
    }

    /// <summary>
    /// n ≥ 6 的角遍历（6 层、字母表 {空, T, 晶体, 顶针}）：
    /// <b>每个合法的碎晶角都必须做得出</b>——这正是文档 §1.3 的
    /// 「n ≥ 6 时 S ⟺ 五项条件」，也是「没有按 n × 层数打表」的回归证据。
    /// 同时检查不合法列一律报「不可制作」而不是「未覆盖」。
    /// </summary>
    public static void SixPlusLegalColumnsAreAllConstructible()
    {
        var alphabet = new[] { '.', 'T', 'c', 'P' };

        const int parts = 6;
        const int layers = 6;

        var buffer = new char[layers];

        int legal = 0;
        int breakable = 0;

        for (int code = 0; code < 4096; code++)
        {
            int remaining = code;

            for (int layer = 0; layer < layers; layer++)
            {
                buffer[layer] = alphabet[remaining % alphabet.Length];
                remaining /= alphabet.Length;
            }

            string text = new(buffer);

            ShapeDocument target = FromColumns(parts, layers, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

            if (!ShapeSignature.IsLegalColumn(target, 0))
            {
                Assert.False(order.IsComplete, text + " 不合法，不应有工序");
                Assert.True(
                    order.Note is not null &&
                    order.Note.StartsWith(
                        AngleWorkOrderBuilder.NotProduciblePrefix,
                        StringComparison.Ordinal),
                    text + " 不合法，必须报「不可制作」：" + order.Note);

                continue;
            }

            legal++;

            if (order.Kind != AngleBuildKind.NeedsCrystalBreak)
            {
                Assert.True(order.IsComplete, text + " 的简单角应完成");

                continue;
            }

            breakable++;

            Assert.True(
                order.IsComplete,
                "n = 6 的合法碎晶角都应做得出（S ⟺ 五项条件）：" +
                text + " → " + (order.Note ?? "?"));

            Assert.True(
                ShapeOps.Same(target, Step(order, AngleWorkStepKind.Target).Shape),
                text + " 的末步应逐格等于目标");
        }

        Assert.True(legal > 1400, "n = 6、6 层的合法列应有上千个，实际 " + legal);
        Assert.True(breakable > 300, "碎晶角应有数百个，实际 " + breakable);
    }

    /// <summary>
    /// 失败语义必须区分：五项条件不过 ⇒「不可制作」；角合法但生成器画不下
    /// 前态 ⇒「未覆盖」。
    /// </summary>
    public static void SixPlusDistinguishesUncoveredFromNotProducible()
    {
        // 不可制作：两枚晶体之间的洞只有 1 枚普通格（条件五失败）。
        ShapeDocument illegal = FromColumns(6, 6, (0, "cT.c.."));

        AngleWorkOrder notProducible = AngleWorkOrderBuilder.Build(illegal, 0, 6);

        Assert.False(notProducible.IsComplete, "不合法角不应有工序");
        Assert.True(
            notProducible.Note is not null &&
            notProducible.Note.StartsWith(
                AngleWorkOrderBuilder.NotProduciblePrefix,
                StringComparison.Ordinal),
            "不合法角必须报「不可制作」：" + notProducible.Note);

        // 未覆盖：角本身合法（n ≥ 6 属于 S），但 cap 小于层数、前态画不下。
        ShapeDocument legal = FromColumns(6, 6, (0, "T..c.."));

        Assert.True(
            ShapeSignature.IsLegalColumn(legal, 0),
            "T..c.. 应是合法角");

        AngleWorkOrder uncovered = AngleWorkOrderBuilder.Build(legal, 0, 5);

        Assert.False(uncovered.IsComplete, "画不下前态时不应标成完成");
        Assert.True(
            uncovered.Note is not null &&
            uncovered.Note.StartsWith(
                AngleWorkOrderBuilder.UncoveredPrefix,
                StringComparison.Ordinal),
            "画不下前态必须报「未覆盖」：" + uncovered.Note);
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

    /// <summary>
    /// 两刀定向碎晶（§7.4）的逐刀回放：每一刀的 PreState 都用真实
    /// <see cref="ShapePhysics.Cut"/> 重算，Cut 步快照必须等于西半边；
    /// 第二刀后的目标列逐格等于目标，最后构造 C 换柱也保持目标列。
    /// </summary>
    private static void ReplayTwoCutCascade(
        AngleWorkOrder order,
        ShapeDocument target,
        int column)
    {
        AngleWorkStep? preState = null;

        int cutIndex = 0;

        for (int index = 0; index < order.Steps.Count; index++)
        {
            AngleWorkStep step = order.Steps[index];

            if (step.Kind == AngleWorkStepKind.PreState &&
                step.Shape is not null)
            {
                preState = step;
                continue;
            }

            if (step.Kind != AngleWorkStepKind.Cut ||
                step.Shape is null)
            {
                continue;
            }

            Assert.True(
                preState is not null,
                "每一刀前面都必须有一条前态");

            ShapeDocument rotated = ShapeOps.Rotate(
                preState!.Shape!,
                order.Rotation);

            CutResult cut = ShapePhysics.Cut(rotated);

            Assert.True(cut.Success, cut.Error ?? "切割失败");

            ShapeDocument replayed = ShapeOps.Rotate(
                cut.West!,
                -order.Rotation);

            Assert.True(
                ShapeOps.Same(replayed, step.Shape),
                $"第 {cutIndex + 1} 刀应等于真实 Cut 出来的西半边");

            Assert.True(
                ShapeOps.IsStable(preState.Shape!),
                $"第 {cutIndex + 1} 刀的前态应稳定");

            cutIndex++;
        }

        Assert.Equal(2, cutIndex);

        AngleWorkStep lastCut = order.Steps.Last(
            step => step.Kind == AngleWorkStepKind.Cut);

        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(lastCut.Shape!, column),
            "第二刀后的目标列应逐格等于目标");

        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(
                Step(order, AngleWorkStepKind.Support).Shape!,
                column),
            "构造 C 换柱后目标列仍应逐格等于目标");

        Assert.True(
            ShapeOps.Same(
                target,
                Step(order, AngleWorkStepKind.Target).Shape),
            "末步应得到目标角");
    }

    /// <summary>
    /// 算法 4（§11 通用实现）步骤链的回放：PreState →（Cut → PreState?）…→ Support → Target。
    ///
    /// <para>逐条核对 §11.4 的六条验收：</para>
    /// <list type="number">
    /// <item>每条前态的每一列都过五项条件；</item>
    /// <item>每条前态的目标列 ≠ 目标列；</item>
    /// <item>前态与每个中间态都稳定；</item>
    /// <item>每一刀都等于真实 <see cref="ShapePhysics.Cut"/> 的西半边（含 <c>Rotation</c>）；</item>
    /// <item>构造 C 交接后与末步都逐格等于目标；</item>
    /// <item>失败语义由调用方另行断言（这里只验证已完成订单）。</item>
    /// </list>
    /// </summary>
    private static void ReplayGenericAlgorithm4(
        AngleWorkOrder order,
        ShapeDocument target,
        int column)
    {
        AngleWorkStep? preState = null;
        int cuts = 0;

        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind == AngleWorkStepKind.PreState)
            {
                Assert.True(step.Shape is not null, "前态必须有图形");
                Assert.True(ShapeOps.IsStable(step.Shape!), "前态必须稳定");
                Assert.True(
                    ShapeSignature.AllColumnsLegal(step.Shape!, out int badColumn),
                    $"前态第 {badColumn + 1} 象限不是合法列");
                Assert.NotEqual(
                    ShapeSignature.ColumnText(target, column),
                    ShapeSignature.ColumnText(step.Shape!, column),
                    "前态的目标列必须与目标不同（否则就是循环）");

                preState = step;

                continue;
            }

            if (step.Kind != AngleWorkStepKind.Cut)
            {
                continue;
            }

            Assert.True(preState?.Shape is not null, "每一刀前面都要有一条前态");

            ShapeDocument replayed = ShapeOps.Rotate(
                ShapePhysics.Cut(
                    ShapeOps.Rotate(preState!.Shape!, order.Rotation)).West!,
                -order.Rotation);

            Assert.True(
                ShapeOps.Same(replayed, step.Shape),
                $"第 {cuts + 1} 刀应逐格等于真实 Cut 出来的西半边");

            Assert.True(ShapeOps.IsStable(step.Shape!), "切割后的中间态必须稳定");

            preState = step;
            cuts++;
        }

        Assert.True(cuts >= 1, "算法 4 至少有一刀");

        ShapeDocument handover = Step(order, AngleWorkStepKind.Support).Shape!;

        Assert.True(ShapeOps.IsStable(handover), "构造 C 交接后的图形应稳定");
        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(handover, column),
            "构造 C 交接后的目标列应逐格等于目标");

        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(preState!.Shape!, column),
            "最后一刀后的目标列应逐格等于目标");

        Assert.True(
            ShapeOps.Same(target, Step(order, AngleWorkStepKind.Target).Shape),
            "末步应得到目标角");
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
    /// 前态里「目标中没有」的晶体数量（同格比较）：即临时晶体数。
    /// </summary>
    private static int CountTemporaryCrystals(
        ShapeDocument preState,
        ShapeDocument target)
    {
        int count = 0;

        for (int part = 0; part < preState.PartCount; part++)
        {
            for (int layer = 0; layer < preState.LayerCount; layer++)
            {
                if (preState[layer, part].Type != CellType.Crystal)
                {
                    continue;
                }

                bool alsoInTarget =
                    part < target.PartCount &&
                    layer < target.LayerCount &&
                    target[layer, part].Type == CellType.Crystal;

                if (!alsoInTarget)
                {
                    count++;
                }
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

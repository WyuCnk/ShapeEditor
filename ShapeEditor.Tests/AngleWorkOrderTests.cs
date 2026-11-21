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

        /*
         * Cut 步现在是「切割 + 落定」的最终态（合成一步），
         * 必须逐格等于真实 ShapePhysics.Cut 的西半边。
         */
        Assert.True(
            ShapeOps.Same(
                replayed,
                Step(order, AngleWorkStepKind.Cut).Shape),
            "Cut 步应等于真实 Cut 出来的西半边（已落定）");

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
    /// 算法 4 的长晶体段案例 <c>cT.T.ccc</c>（D 不成立、F 成立）：走 <b>构造 G</b>（§15.9）。
    ///
    /// <para>
    /// §15 的实测前态：核心列 <c>cTccTccc</c>（把 T@3 抬到 4、让出的第 3 层填临时晶体）、
    /// 环伙伴 <c>TTTcTTPT</c>（第 6 层 T / 第 7 层针 / 第 8 层 T，闸门层放晶体而不是洞）、
    /// 保险丝柱 <c>TTTcTTTT</c>（第 4 层一颗晶体）；一刀之后目标列逐格等于 <c>cT.T.ccc</c>。
    /// </para>
    /// <para>
    /// <b>断言更新的原因</b>：构造 G 按 §15 实现之后成了路线 F 的首选恢复手段
    /// （单孔环下落把下落锁成恰好一层），碎晶等价路线（构造 D）降为回退；
    /// 因此这里从断言 D 改为断言 G，并逐条核对 §15.3 的四条不变量。
    /// </para>
    /// </summary>
    public static void ConstructionGMatchesSpecExample()
    {
        ShapeDocument target = FromColumns(4, 8, (0, "cT.T.ccc"));

        // 预热 JIT：首调用包含 JIT，不能算进「单角耗时 < 50 ms」。
        _ = AngleWorkOrderBuilder.Build(
            FromColumns(4, 8, (0, "T.ccc")),
            0,
            5,
            preferCrystalBreak: false);

        var watch = Stopwatch.StartNew();

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            8,
            preferCrystalBreak: false);

        watch.Stop();

        Assert.True(order.IsComplete, order.Note ?? "规格 §15.9 的 G 例子应验证通过");
        Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm);
        Assert.Equal(AngleConstruction.G, order.Construction);
        Assert.Equal(0, order.Rotation);
        Assert.Equal(0, order.Rank);
        Assert.True(
            watch.ElapsedMilliseconds < 50,
            $"cT.T.ccc 单角耗时 {watch.ElapsedMilliseconds} ms，应 < 50 ms");

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Support,
            AngleWorkStepKind.Target);

        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        Assert.Equal(
            "cTccTccc",
            ShapeSignature.ColumnText(preState, 0),
            "规范核心列应为 cTccTccc（T@3 抬到 4，第 3 层填临时晶体）");

        // §15.9 的实测辅助列：环伙伴第 7 层是针，最末辅助列是保险丝柱（第 4 层晶体）。
        Assert.Equal(
            "TTTcTTPT",
            ShapeSignature.ColumnText(preState, order.AuxiliaryColumns[0]),
            "环伙伴 B 应为紧列 TTTcTTPT（环内第 7 层放针、闸门层放晶体）");
        Assert.Equal(
            CellType.Crystal,
            preState[3, order.AuxiliaryColumns[^1]].Type,
            "保险丝柱 B′ 第 4 层应放晶体（与目标列第 4 层的临时晶体跨刀口相连）");

        // 辅助列最少化：第三列（满高普通柱）整列为空。
        Assert.True(
            ShapeSignature.ColumnText(preState, 2)
                .All(code => code == ShapeSignature.EmptyCode),
            "构造 G 的两列辅助之外，第三列应整列为空");

        ReplayGenericAlgorithm4(order, target, 0);

        // 不变量三：目标原生晶体一颗不少，临时晶体全部消失。
        Assert.Equal(
            4,
            CountCrystals(Step(order, AngleWorkStepKind.Cut).Shape!, 0),
            "切割后目标列只应剩 4 颗原生晶体（第 1、6、7、8 层）");
        Assert.Equal(
            6,
            CountCrystals(preState, 0),
            "前态目标列 = 4 颗原生晶体 + 第 3、4 层的 2 颗临时晶体");
        Assert.True(
            CountTemporaryCrystals(preState, target) > 0,
            "构造 G 的前态必须真有临时晶体（不是「先做出目标再切柱」）");
    }

    #endregion

    #region 角工序：构造 F（n ≥ 6）

    /// <summary>
    /// 用户样例的两列碎晶角：n ≥ 6 不再生成 FillBreak，两列都走
    /// 算法 1（提升）→ 算法 2（全填临时晶体）→ 构造 F 四列。
    /// </summary>
    public static void ConstructionFHandlesUserSample()
    {
        ShapeDocument document = Parse(UserSampleCode, 9, 8);

        foreach ((int column, AngleConstruction expected) in new[]
                 {
                     (0, AngleConstruction.F),
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
             * n ≥ 6 的列级步骤链：PreState → Cut（碎晶并落定）→ Support → Target。
             * Support = 构造 C（装新支撑柱、拆辅助列）；n = 4 的构造 G 是
             * PreState → Cut（碎晶并落定）→ Target（见 ConstructionGReplaysToTarget）。
             */
            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Cut,
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
    /// 缺口高两层：算法 1 把 T 抬到缺口顶端、算法 2 再把最高晶体以下的空位
    /// 全部填成临时晶体 <c>ccTccc</c>，构造 F 一刀打碎临时晶体，
    /// T 落回第 1 层，缺口就由碎晶造出来（不再走 FillBreak）。
    /// </summary>
    public static void ConstructFHandlesMultiLayerGap()
    {
        foreach (int parts in new[] { 6, 10 })
        {
            ShapeDocument target = FromColumns(parts, 6, (0, "T..ccc"));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6, preferCrystalBreak: false);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"n = {parts} 的多层缺口构造 F 应验证通过");
            Assert.Equal(AngleConstruction.F, order.Construction);
            Assert.Equal(AngleAlgorithm.Algorithm3, order.Algorithm);

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                "ccTccc",
                ShapeSignature.ColumnText(preState, 0),
                "算法 2 的正规核心应把 T 抬到缺口顶端并把最高晶体以下的空位填成临时晶体");

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

        // 缺口高两层 ⇒ 两轮；每轮 PreState/Cut（切 + 落定），最后 Note + Target。
        Assert.Equal(2, order.Steps.Count(step => step.Kind == AngleWorkStepKind.PreState));
        Assert.Equal(2, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut));
        Assert.Equal(0, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Collapse));
        Assert.Equal(6, order.Steps.Count);

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        // 逐轮核对：源普通格每次只降一层（Cut 步即落定后的结果）。
        Assert.Equal(
            "c..Tccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.PreState, 0).Shape!,
                0));

        Assert.Equal(
            "c.T.ccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.Cut, 0).Shape!,
                0));

        Assert.Equal(
            "c.T.ccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.PreState, 1).Shape!,
                0));

        Assert.Equal(
            "cT..ccc.",
            ShapeSignature.ColumnText(
                NthStep(order, AngleWorkStepKind.Cut, 1).Shape!,
                0));

        VerifyCutRound(order, 0);
        VerifyCutRound(order, 1);

        Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                "末步应清除辅助象限并得到目标角");
    }

    #endregion

    #region 角工序：T4 两列最少化与 T7 FillBreak 标注

    /// <summary>
    /// T4：构造 D 先试两列（支撑 + 爆破），第三象限整列为空。
    /// T7：D / G 都做不了时以前会保留 FillBreak；现在这些形态由构造性构造 H
    /// （docs §18.4）覆盖，≤6 层 S 列里 FillBreak 已归零（审计 CONSTR）。
    /// 「保留 FillBreak 必须带非论文标注」的规则仍由审计兜底。
    /// </summary>
    public static void DirectionalBreakPrefersTwoColumnsAndAnnotatesFillBreak()
    {
        /*
         * T4：通用构造 D 的两列布局。
         * 断言更新的原因：`T.T..c` 现在由 §13.6 的通用构造 H 覆盖（前态 A 按
         * 晶体 f−1、普通格跨越空层放置，P1 每颗晶体下方都有普通格），
         * 不再走构造 D 的两列辅助路线；T4 的两列最少化仍由 `cTT..T.c` 守住。
         */
        foreach (string text in new[] { "cTT..T.c" })
        {
            int cap = text.Length;
            ShapeDocument target = FromColumns(4, cap, (0, text));
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
            Assert.Equal(AngleConstruction.D, order.Construction, text);
            Assert.True(order.Note is null, text + " 通过时不应有 Note");
            Assert.Equal(2, order.AuxiliaryColumns.Count, text + " 应只用两列辅助");
            Assert.True(
                order.Limitations.Any(line => line.Contains("辅助列最少化")),
                text + " 的限制说明应写明辅助列最少化");

            // 前态第三象限（非辅助列）必须整列为空。
            ShapeDocument preState = NthStep(
                order,
                AngleWorkStepKind.PreState,
                0).Shape!;

            for (int part = 0; part < 4; part++)
            {
                if (order.AuxiliaryColumns.Contains(part) || part == 0)
                {
                    continue;
                }

                for (int layer = 0; layer < cap; layer++)
                {
                    Assert.Equal(
                        CellType.Empty,
                        preState[layer, part].Type,
                        text + $" 的第 {part + 1} 象限不应有材料");
                }
            }

            ReplayGenericAlgorithm4(order, target, 0);
        }

        // T4 的另一例 `T.T..c` 由 §13.6 通用构造 H 完成（一次 Pin）：
        // 前态 A 每颗原生晶体下方都是普通格（P1），末步等于目标。
        {
            const string text = "T.T..c";
            ShapeDocument target = FromColumns(4, text.Length, (0, text));
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, text.Length, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
            Assert.Equal(AngleConstruction.H, order.Construction, text);
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(Step(order, AngleWorkStepKind.Target).Shape!, 0),
                text + " 的末步目标列应等于目标");
        }

        /*
         * `T..T.c` 的 D 同样成立（算法 6），现在由 §0.4 论文四步直接完成：
         * 底块提升 → 正规化 → 构造 H 顶针 → 逐块弱落恢复，
         * Pin 只是中间态，最终由 WeakDrop 把底块放回目标层。
         */
        {
            ShapeDocument target = FromColumns(4, 6, (0, "T..T.c"));
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? "T..T.c 应可制作");
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
            Assert.Equal(AngleConstruction.H, order.Construction);
            Assert.True(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin),
                "T..T.c 的构造 H 应有 Pin 步");
            Assert.True(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.WeakDrop),
                "T..T.c 应有弱落恢复步");
            Assert.Equal(
                "T..T.c",
                ShapeSignature.ColumnText(
                    Step(order, AngleWorkStepKind.Target).Shape!,
                    0),
                "T..T.c 的末步目标列应等于目标");
        }

        // T7：底针 + 普通格可抬升 ⇒ 现在由构造 H（按 I/J 构造性生成）一次顶针完成。
        // 断言更新的原因：底针位置用临时晶体顶住后，I/J 的构造性 H 已能覆盖该形态，
        // 不再走 support×block×fuse 的塔式枚举（docs §18.4）。
        {
            ShapeDocument target = FromColumns(4, 4, (0, "PT.c"));
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 4, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? "PT.c 应可制作");
            Assert.Equal(AngleConstruction.H, order.Construction);
            Assert.True(order.Note is null, "PT.c 用构造 H，不应有 Note");
            Assert.Equal(3, order.AuxiliaryColumns.Count);
            AssertColumnCells(
                Step(order, AngleWorkStepKind.Target).Shape!,
                0,
                "PT.c");
        }

        /*
         * T7：缺口夹在针与普通格之间、D / G 都不行的两列。
         * 断言更新的原因（docs §18.4）：构造性构造 H 现在也能覆盖它们——
         * 第 1 层垫一枚临时晶体（Pin 把它补成针）、其余按 I/J 生成，
         * 一次顶针即得目标。因此 ≤6 层的 S 列里已经不再出现 FillBreak
         * （见审计 CONSTR）；「保留 FillBreak 时必须带非论文标注」的规则
         * 仍由 ConstructionDistributionAudit 兜底（n = 0 时该断言自然成立）。
         */
        foreach (string text in new[] { "P.Tc", "P.TcT" })
        {
            int cap = text.Length;
            ShapeDocument target = FromColumns(4, cap, (0, text));
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
            Assert.Equal(AngleConstruction.H, order.Construction, text);
            Assert.True(order.Note is null, text + " 由构造 H 完成时不应带 Note");
            AssertColumnCells(
                Step(order, AngleWorkStepKind.Target).Shape!,
                0,
                text);
        }
    }

    #endregion

    #region 角工序：构造 H（n = 4 一次顶针）

    /// <summary>
    /// 核心 / 尾部拆分（docs §17.5 第 1 条）：最高晶体以上的 T/P 不参与碎晶，
    /// 先按核心列走现有流程，最后由构造 C 逐层接回尾部。
    ///
    /// <para>
    /// <c>.T..cT</c> / <c>.T..cTT</c> 的核心都是 <c>.T..c</c>（算法 6 的塔式路线）；
    /// 尾部 T 通过 <see cref="AngleWorkStepKind.Stack"/> 步追加。每个前态都要合法、
    /// 稳定，且 Pin 步等于真实 <c>ShapePhysics.Pin</c>。
    /// </para>
    /// </summary>
    public static void CoreTailSplitAppendsTail()
    {
        foreach ((string text, string preStateText, string coreTargetText, int tailCount)
                 in new[]
                 {
                     (".T..cT", "..Tc..", ".T..c.", 1),
                     (".T..cTT", "..Tc...", ".T..c..", 2)
                 })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? $"{text} 应可制作");
            Assert.True(order.Note is null);
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);

            /*
             * 断言更新的原因：核心 `.T..c` 现在由构造性构造 H（按 I/J 生成 A/B/E/N
             * + 一次顶针）完成，不再走「塔 X + 构造 C 交接」的枚举路线（docs §18.4），
             * 因此核心只有一条 Pin 步、没有再换支撑柱，核心之后只留尾部接回用的
             * 「装满高支撑柱」一条 Support。
             */
            Assert.Equal(AngleConstruction.H, order.Construction);

            var expectedKinds = new List<AngleWorkStepKind>
            {
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Pin,
                AngleWorkStepKind.Note,
                // 尾部接回前先给悬空层装满高支撑柱。
                AngleWorkStepKind.Support
            };

            for (int index = 0; index < tailCount; index++)
            {
                expectedKinds.Add(AngleWorkStepKind.Stack);
            }

            expectedKinds.Add(AngleWorkStepKind.Target);

            AssertStepKinds(order, expectedKinds.ToArray());

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(
                preStateText,
                ShapeSignature.ColumnText(preState, 0),
                "核心塔列不应包含尾部材料");
            Assert.True(ShapeOps.IsStable(preState), "核心前态应稳定");
            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out _),
                "核心前态的每一列都应过五项条件");
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                "核心前态不应含非法列");

            Assert.True(
                ShapeOps.Same(
                    ShapePhysics.Pin(preState).Document,
                    Step(order, AngleWorkStepKind.Pin).Shape),
                "Pin 步应等于真实 ShapePhysics.Pin 的结果");

            Assert.Equal(
                coreTargetText,
                ShapeSignature.ColumnText(
                    Step(order, AngleWorkStepKind.Pin).Shape!,
                    0),
                "顶针后的核心列应与核心目标一致");

            Assert.Equal(
                tailCount,
                order.Steps.Count(step =>
                    step.Kind == AngleWorkStepKind.Stack),
                "尾部每有一层非空材料，就要有一条构造 C 追加步");

            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                "末步应得到完整目标角");
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(
                    Step(order, AngleWorkStepKind.Target).Shape!,
                    0),
                "完整目标列应逐格等于目标");
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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

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
         * 带尾部 PT 的合成列（cT..TcccPT）：最高晶体以上的 P/T 现在是「尾部」，
         * 由构造 C 逐层接回；核心 cT..Tccc 走 §11 通用实现（构造 D）。
         */
        {
            const string text = "cT..TcccPT";
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 的核心 + 尾部工序应验证通过");
            Assert.True(order.Note is null);
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm);
            Assert.Equal(AngleConstruction.D, order.Construction);

            Assert.True(
                order.Steps.Any(step =>
                    step.Kind == AngleWorkStepKind.Stack),
                "尾部 P/T 必须由构造 C 的逐层追加步接回");
            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                "末步应得到完整目标角");
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
    /// 前态不稳定时（这里的目标列在最高晶体上方有一枚悬空顶针，
    /// 算法 2 只填最高晶体以下的空位，辅助柱又粘不住顶针），
    /// 订单元必须止步于「前态 + 说明」，不能伪造 Cut/Collapse 步骤。
    /// </summary>
    public static void UnstablePrestateKeepsVerifiedPrefix()
    {
        ShapeDocument target = FromColumns(6, 7, (0, "T..c..P"));

        // 合成列（五项条件不过）：只验证「前态不稳定时保留已验证前缀」。
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            7,
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
    /// 用户给的三个例子：<c>T..ccc</c>、<c>T..cc</c> 与 <c>cTT.cTcc</c>。
    ///
    /// <para>
    /// <b>断言更新的原因（本轮纯构造改造）</b>：前两个的 D 成立 ⇒ 算法 6；
    /// 塔式构造 C 现在由「托针柱针位 = 最高普通格以下全部层、落块柱桥锚 = 目标底格」
    /// 直接生成，一次 Pin + 构造 C 就能得到目标列，因此比 §11 通用计划（构造 D）
    /// 先命中。第三个 <c>cTT.cTcc</c> 的 D 不成立，仍走 §11 通用实现（构造 D）。
    /// </para>
    /// </summary>
    public static void FillBreakCoversUserExamples()
    {
        foreach (string text in new[] { "T..ccc", "T..cc" })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                cap,
                preferCrystalBreak: false);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 应由算法 6 的塔式构造 C 完成");
            Assert.True(order.Note is null, $"{text} 不应有未实现说明");
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);

            // 断言更新的原因：这两个例子现在由 §0.4 的论文四步（算法 6）完成：
            // 底块提升 → 正规化 → 构造 H 一次顶针 → 逐块弱落恢复；
            // 步骤链因此多了一步 WeakDrop（恢复底块提升）。
            Assert.Equal(AngleConstruction.H, order.Construction);

            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Pin,
                AngleWorkStepKind.WeakDrop,
                AngleWorkStepKind.Note,
                AngleWorkStepKind.Target);

            // 每一步都用真实物理回放，最终列逐格等于目标。
            Assert.True(
                ShapeOps.Same(
                    ShapePhysics.Pin(
                        Step(order, AngleWorkStepKind.PreState).Shape!).Document,
                    Step(order, AngleWorkStepKind.Pin).Shape),
                $"{text} 的 Pin 步应等于真实顶针结果");

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(
                    Step(order, AngleWorkStepKind.Target).Shape!,
                    0),
                $"{text} 的末步目标列应逐格等于目标");
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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                cap,
                preferCrystalBreak: false);

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
        // 单层缺口 T.ccc：D 不成立 ⇒ 路线 F ⇒ 首选构造 G（§15 单孔环下落）。
        ShapeDocument preferredTarget = FromColumns(4, 5, (3, "T.ccc"));

        AngleWorkOrder preferred = AngleWorkOrderBuilder.Build(
            preferredTarget,
            3,
            5,
            preferCrystalBreak: false);

        Assert.True(preferred.IsComplete, preferred.Note ?? "算法 4 构造 G 应验证通过");
        Assert.Equal(AngleAlgorithm.Algorithm4, preferred.Algorithm);
        Assert.Equal(AngleConstruction.G, preferred.Construction);

        // 缺口上下都是保留晶体：同样落在路线 F，由构造 G 完成。
        Assert.Equal(
            AngleConstruction.G,
            BuildConstruction("cT.T.ccc", 4, 8, preferCrystalBreak: false));

        // 落点/针位只允许一次顶针：由构造 H 完成。
        // PTT.c 底格是针、缺口在晶体塔下，算法 5 的通用构造 H 正好适用。
        Assert.Equal(
            AngleConstruction.H,
            BuildConstruction("PTT.c", 4, 5, preferCrystalBreak: false));

        // n ≥ 6 的受控下落：退回构造 F。
        Assert.Equal(
            AngleConstruction.F,
            BuildConstruction(".T..c..", 6, 7));
    }

    private static AngleConstruction BuildConstruction(
        string text,
        int parts,
        int cap,
        bool preferCrystalBreak = true)
    {
        ShapeDocument target = FromColumns(parts, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            cap,
            preferCrystalBreak: preferCrystalBreak);

        Assert.True(
            order.IsComplete,
            order.Note ?? $"{text} 应能由兜底构造完成");

        return order.Construction;
    }

    #endregion

    #region 角工序：cap 高于目标高度

    /// <summary>
    /// 目标只有 6/7/8 层、cap 更大时：前态与中间态要补空层，但目标列仍要逐格对上。
    /// 覆盖构造 C（塔式一次顶针）、构造 G、构造 H 与构造 F。
    ///
    /// <para>
    /// <b>断言更新的原因（本轮纯构造改造）</b>：<c>T..ccc</c> 的 D 成立 ⇒ 算法 6；
    /// 塔式构造 C 的托针柱/落块柱现在由扫描直接生成，一次 Pin + 构造 C 即得目标，
    /// 因此不再落到 §11 通用计划（构造 D）。
    /// </para>
    /// </summary>
    public static void ConstructionHandlesCapAboveTargetHeight()
    {
        var cases = new (string Text, int TargetLayers, int Parts, AngleConstruction Construction)[]
        {
            ("T..ccc", 6, 4, AngleConstruction.H),
            ("cT..ccc", 8, 4, AngleConstruction.G),
            // `.T..c.` 现在由构造性构造 H 完成（docs §18.4），不再走塔 X + 构造 C。
            (".T..c.", 6, 4, AngleConstruction.H),
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
                step.Kind is AngleWorkStepKind.Cut or
                    AngleWorkStepKind.Pin or
                    AngleWorkStepKind.WeakDrop or
                    AngleWorkStepKind.Support);

            Assert.Equal(
                text.PadRight(cap, '.'),
                ShapeSignature.ColumnText(lastOperational.Shape!, 0),
                $"{text}（cap 高于目标）的最终目标列应逐格对上");

            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);

            /*
             * 断言更新的原因：这三个侧向支撑角现在由<b>构造性构造 H</b> 完成
             * （docs §18.4：按 I/J 直接生成 A | B | E | N，一次 Pin 批量下落），
             * 不再是「造塔 X + 交换链子配方 + 构造 C 交接」的枚举路线。
             */
            Assert.Equal(AngleConstruction.H, order.Construction);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 的算法 6 工序应验证通过");
            Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

            // 步骤链：前态 → 一次顶针 → 结晶说明 → 目标角（H 不需要再换支撑柱）。
            AssertStepKinds(
                order,
                AngleWorkStepKind.PreState,
                AngleWorkStepKind.Pin,
                AngleWorkStepKind.Note,
                AngleWorkStepKind.Target);

            // rank 只统计角自己的顶针。
            Assert.Equal(1, order.Rank);
            Assert.Equal(1, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

            ShapeDocument preState = order.Steps[0].Shape!;
            ShapeDocument pinned = order.Steps[1].Shape!;

            // 前态本身：稳定、每列过五项条件。
            Assert.True(ShapeOps.IsStable(preState), $"{text} 的前态应稳定");
            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out _),
                $"{text} 的前态每列都应过五项条件");
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                $"{text} 的前态不应含非法列");

            // 四列布局：A | 晶体桥柱 B | 满高纯晶体柱 E | 针槽柱 N。
            Assert.Equal(3, order.AuxiliaryColumns[0], $"{text} 的 N 列应在第 4 象限");
            Assert.Equal(1, order.AuxiliaryColumns[1], $"{text} 的 B 列应在第 2 象限");
            Assert.Equal(2, order.AuxiliaryColumns[2], $"{text} 的 E 列应在第 3 象限");

            Assert.True(
                ShapeSignature.ColumnText(preState, 2)
                    .All(code => code == ShapeSignature.CrystalCode),
                $"{text} 的 E 列应是满高纯晶体柱");

            // 目标列第 1 层必须为空（否则 Pin 会补出目标没有的针）。
            Assert.Equal(
                ShapeSignature.EmptyCode,
                ShapeSignature.ColumnText(preState, 0)[0],
                $"{text} 的前态第 1 层必须为空");

            // 顶针步必须等于真实 ShapePhysics.Pin 的结果。
            Assert.True(
                ShapeOps.Same(ShapePhysics.Pin(preState).Document, pinned),
                "Pin 步快照应等于真实顶针结果");

            // 顶针后目标列逐格等于目标。
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(pinned, 0),
                $"{text} 顶针后的目标列应逐格等于目标");

            // A 带洞时必须有交换链子配方（rank 0）证明可造，并端到端回放。
            Assert.True(order.SubRecipe is not null, $"{text} 的带洞前态必须带子配方");
            Assert.Equal(RecipeKind.Exchange, order.SubRecipe!.Kind);
            Assert.Equal(0, order.SubRecipe.Rank);
            Assert.True(
                Replay.Verify(order.SubRecipe, preState, out IReadOnlyList<string> errors),
                "前态子配方应能端到端回放：" + string.Join(",", errors));
            Assert.True(
                ShapeOps.Same(order.SubRecipe.Shape, preState),
                "子配方产物必须等于前态");

            // 末步清除辅助柱，得到目标角。
            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                "末步应得到目标角");

            // 目标列没有丢失任何一格（颜色/形状码也逐格一致）。
            AssertColumnCells(pinned, 0, text);
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

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                cap,
                preferCrystalBreak: false);

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.True(
                order.IsComplete,
                order.Note ?? $"{text} 应可制作");
            Assert.True(
                order.Note is null,
                order.Note ?? $"{text} 通过时不应有未覆盖说明");

            /*
             * 路线核对（TODO.md §7.2 / §7.4 / §7.7 / docs §18.4）：
             * `.T.c` / `P.T.c` / `.T..c` 现在由<b>构造性构造 H</b>（按 I/J 生成
             * A|B|E|N + 一次顶针）完成；`T.T.c` / `.T.T.c` 仍是「底块提升塔 +
             * 构造 C」；`.TT.c` 走「两刀定向碎晶 + 构造 C」。
             */
            (AngleAlgorithm Algorithm,
             AngleConstruction Construction,
             int Pins,
             int Cuts) route = text switch
            {
                ".T.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.H, 1, 0),
                "T.T.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.H, 1, 0),
                "P.T.c" => (AngleAlgorithm.Algorithm5, AngleConstruction.H, 1, 0),
                ".TT.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.H, 1, 0),
                ".T..c" => (AngleAlgorithm.Algorithm6, AngleConstruction.H, 1, 0),
                ".T.T.c" => (AngleAlgorithm.Algorithm6, AngleConstruction.H, 1, 0),
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

            // 最后一条操作步（Pin/WeakDrop/Cut/Collapse/Support）也必须已经等于目标。
            AngleWorkStep lastOperational = order.Steps.Last(step =>
                step.Shape is not null &&
                step.Kind is AngleWorkStepKind.Pin or
                    AngleWorkStepKind.WeakDrop or
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
            else if (route.Construction == AngleConstruction.H)
            {
                // 构造 H：一次 Pin 完成批量下降；若还有底块提升，再由
                // WeakDrop 按逆序恢复。最终目标列必须逐格等于目标。
                ShapeDocument pinInput = order.Steps
                    .TakeWhile(step => step.Kind != AngleWorkStepKind.Pin)
                    .Last(step => step.Shape is not null)
                    .Shape!;

                Assert.True(
                    ShapeOps.Same(
                        ShapePhysics.Pin(pinInput).Document,
                        Step(order, AngleWorkStepKind.Pin).Shape),
                    $"{text} 的 Pin 步应等于真实顶针结果");

                AngleWorkStep pinStep = Step(order, AngleWorkStepKind.Pin);
                int pinIndex = order.Steps
                    .ToList()
                    .FindIndex(step => step.Kind == AngleWorkStepKind.Pin);
                AngleWorkStep? finalRestore = order.Steps
                    .Skip(pinIndex + 1)
                    .LastOrDefault(step => step.Kind == AngleWorkStepKind.WeakDrop);

                if (finalRestore is not null)
                {
                    Assert.Equal(
                        text,
                        ShapeSignature.ColumnText(finalRestore.Shape!, 0),
                        $"{text} 的弱落恢复后目标列应等于目标");
                }
                else
                {
                    Assert.Equal(
                        text,
                        ShapeSignature.ColumnText(pinStep.Shape!, 0),
                        $"{text} 的 Pin 后目标列应等于目标");
                }

                Assert.False(
                    order.Steps.Any(step => step.Kind == AngleWorkStepKind.Support),
                    $"{text} 的构造 H 不需要构造 C 交接步");
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

        /*
         * 断言更新的原因（§0.4 四步）：`T.T.c` 现在由算法 6 的论文四步完成：
         * 底块提升 → 正规化 → 构造 H 顶针 → 按逆序逐块弱落。
         * Pin 只是中间态，最终由两次 WeakDrop 把两个底块放回目标层。
         */
        {
            AngleWorkOrder generated = AngleWorkOrderBuilder.Build(
                FromColumns(4, 5, (0, "T.T.c")),
                0,
                5,
                preferCrystalBreak: false);

            Assert.True(generated.IsComplete, generated.Note ?? "T.T.c 应可制作");
            Assert.Equal(AngleConstruction.H, generated.Construction);

            ShapeDocument generatedTt = Step(
                    generated,
                    AngleWorkStepKind.PreState)
                .Shape!;

            Assert.True(
                ShapeOps.IsStable(generatedTt),
                "T.T.c 的构造 H 前态应稳定");
            Assert.True(
                ShapeSignature.AllColumnsLegal(generatedTt, out _),
                "T.T.c 的构造 H 前态每列都应合法");
            Assert.Equal(
                2,
                generated.Steps.Count(step => step.Kind == AngleWorkStepKind.WeakDrop),
                "T.T.c 有两次底块提升，应有两次弱落恢复");
            Assert.Equal(
                "T.T.c",
                ShapeSignature.ColumnText(
                    Step(generated, AngleWorkStepKind.Target).Shape!,
                    0),
                "T.T.c 的末步目标列应等于目标");
        }

        /*
         * 断言更新的原因：`T.T.c` 走算法 6 的论文四步（构造 H + 逐块弱落），
         * `P.T.c` 的底针位置用临时晶体顶住后，构造性构造 H 已能按 I/J 一次顶针完成。
         */
        foreach ((string Text, AngleWorkStepKind[] Kinds) in new[]
                 {
                     ("T.T.c", new[]
                     {
                         AngleWorkStepKind.PreState,
                         AngleWorkStepKind.Pin,
                         AngleWorkStepKind.WeakDrop,
                         AngleWorkStepKind.WeakDrop,
                         AngleWorkStepKind.Note,
                         AngleWorkStepKind.Target
                     }),
                     ("P.T.c", new[]
                     {
                         AngleWorkStepKind.PreState,
                         AngleWorkStepKind.Pin,
                         AngleWorkStepKind.Note,
                         AngleWorkStepKind.Target
                     })
                 })
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns(4, 5, (0, Text)),
                0,
                5,
                preferCrystalBreak: false);

            AssertStepKinds(order, Kinds);

            Assert.True(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin),
                $"{Text} 的塔式路线必须有 Pin 步");
        }
    }

    #endregion

    #region 角工序：未覆盖与不可制作

    /// <summary>
    /// 合法的非 S 列 <c>cT.T.c</c> 必须判「不可制作」，不能报「未覆盖」。
    ///
    /// <para>
    /// n = 4 的 S 判据是五项 ∧ (D∨F)（论文定理 3）。D 与 F 都不成立时，
    /// 角虽然合法（五项通过），但已经确定做不出来——这是判定，不是覆盖缺口。
    /// 对应 docs §17.3 的修正：S ✘ ⇒ <see cref="AngleWorkOrderBuilder.NotProduciblePrefix"/>。
    /// </para>
    /// </summary>
    public static void PillarHolderDoesNotClaimNonSCriterion()
    {
        foreach (string text in new[] { "cT.T.c", "cT.T.T.c" })
        {
            ShapeDocument target = FromColumns(4, text.Length, (0, text));

            Assert.True(
                ShapeSignature.IsLegalColumn(target, 0),
                text + " 应是合法列");

            Assert.False(
                PillarSCriterion.IsPillarColumn(target, 0),
                text + " 不应属于 n = 4 的 S");

            Assert.False(
                PillarSCriterion.BottomResourceD(text),
                text + " 的 D 应不成立");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, text.Length, preferCrystalBreak: false);

            Assert.False(order.IsComplete, "不在 S 的合法列不能被硬说成已完成");
            Assert.True(order.Note is not null, "未完成时必须给出 Note");

            Assert.True(
                order.Note!.StartsWith(
                    AngleWorkOrderBuilder.NotProduciblePrefix,
                    StringComparison.Ordinal),
                "Note 必须以「不可制作：」开头：" + order.Note);

            Assert.False(
                order.Note.Contains(
                    AngleWorkOrderBuilder.UncoveredPrefix,
                    StringComparison.Ordinal),
                "S 判据不成立不能写成未覆盖：" + order.Note);

            Assert.False(
                order.Note.Contains("不可制作：未覆盖", StringComparison.Ordinal),
                "不可制作与未覆盖不能混用：" + order.Note);

            Assert.Equal(AngleAlgorithm.None, order.Algorithm);
            Assert.Equal(AngleConstruction.None, order.Construction);
            Assert.Equal(1, order.Steps.Count);
            Assert.Equal(AngleWorkStepKind.Note, order.Steps[0].Kind);
        }
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
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 6, preferCrystalBreak: false);

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

        // ── 合法目标 .T.cT.：核心 .T.c 做完后由构造 C 接回尾部 T ──
        ShapeDocument rejectedTarget = FromColumns(4, 6, (0, ".T.cT."));

        Assert.True(
            ShapeSignature.IsLegalColumn(rejectedTarget, 0),
            ".T.cT. 本身应是合法角");

        AngleWorkOrder rejected =
            AngleWorkOrderBuilder.Build(rejectedTarget, 0, 6);

        Assert.True(
            rejected.IsComplete,
            rejected.Note ?? ".T.cT. 的尾部应由构造 C 接回");
        Assert.False(
            rejected.HasIllegalPreState(out _, out _),
            "核心/尾部拆分后的前态不应含非法列");
        Assert.True(
            rejected.Steps.Any(step =>
                step.Kind == AngleWorkStepKind.Stack),
            "尾部必须由构造 C 的逐层追加步接回");
        Assert.True(
                AngleTestHelpers.TargetColumnMatches(rejected, rejectedTarget),
                "末步应得到完整目标角");
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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

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
    /// 算法 6 的标杆案例 <c>.T.c</c>（TODO.md §3.1.1 / docs §18.4）：
    /// 现在由<b>构造性构造 H</b> 完成——A = 目标列下移一层（<c>.Tc.</c>），
    /// B 按 <c>z+1 ∈ I</c> 放晶体、E 满高纯晶体柱、N 按 <c>z+1 ∈ J</c> 放针，
    /// 一次 Pin 整批落下；rank 只算这一次顶针。
    ///
    /// <para>
    /// <b>断言更新的原因</b>：旧实现走「造塔 X + 交换链子配方 + 构造 C 交接」的
    /// support × block × fuse 枚举；按 docs §18.4，构造 H 的前态必须按 I/J
    /// 构造性生成，不再枚举候选，所以该形态改由 H 直接完成（不需要构造 C 交接）。
    /// </para>
    /// </summary>
    public static void Algorithm6BuildsDotTcFromTower()
    {
        const int cap = 4;

        ShapeDocument target = FromColumns(4, cap, (0, ".T.c"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
        Assert.Equal(AngleConstruction.H, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? ".T.c 应可制作");
        Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

        // 步骤链：前态 → 一次顶针 → 结晶说明 → 目标角。
        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Pin,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        Assert.Equal(1, order.Rank);
        Assert.Equal(1, order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));

        ShapeDocument preState = order.Steps[0].Shape!;
        ShapeDocument pinned = order.Steps[1].Shape!;

        // 四列布局：A = .Tc.，B 是紧列、E 是满高纯晶体柱、N 是紧列（带针）。
        Assert.Equal(".Tc.", ShapeSignature.ColumnText(preState, 0));
        Assert.True(
            ShapeSignature.IsTightColumn(preState, 1),
            "B 列应是紧列：" + ShapeSignature.ColumnText(preState, 1));
        Assert.True(
            ShapeSignature.ColumnText(preState, 2)
                .All(code => code == ShapeSignature.CrystalCode),
            "E 列应是满高纯晶体柱");
        Assert.True(
            ShapeSignature.IsTightColumn(preState, 3),
            "N 列应是紧列：" + ShapeSignature.ColumnText(preState, 3));
        Assert.True(
            ShapeSignature.ColumnText(preState, 3)
                .Contains(ShapeSignature.PinCode),
            "N 列应有针（z+1 ∈ J）");

        // 角列的晶体颜色必须沿用目标列（顶针保留颜色）。
        Assert.Equal(
            'r',
            preState[2, 0].ColorCode,
            "前态 A 的晶体颜色应与目标列一致");

        Assert.True(ShapeOps.IsStable(preState), "前态应稳定");
        Assert.True(
            ShapeSignature.AllColumnsLegal(preState, out _),
            "前态每列都应过五项条件");

        // A 的第 1 层必须为空（否则 Pin 会补出目标没有的针），因此 A 带洞，
        // 必须附带交换链子配方（rank 0）证明可造并端到端回放。
        Assert.Equal(
            ShapeSignature.EmptyCode,
            ShapeSignature.ColumnText(preState, 0)[0],
            "A 的第 1 层必须为空");
        Assert.True(order.SubRecipe is not null, "构造 H 的带洞前态必须带子配方");
        Assert.Equal(RecipeKind.Exchange, order.SubRecipe!.Kind);
        Assert.Equal(0, order.SubRecipe.Rank);
        Assert.True(
            Replay.Verify(order.SubRecipe, preState, out IReadOnlyList<string> subErrors),
            "前态子配方应能端到端回放：" + string.Join(",", subErrors));
        Assert.True(
            ShapeOps.Same(order.SubRecipe.Shape, preState),
            "子配方产物必须等于前态");

        // 顶针步必须等于真实 ShapePhysics.Pin 的结果。
        Assert.True(
            ShapeOps.Same(
                ShapePhysics.Pin(preState).Document,
                pinned),
            "Pin 步快照应等于真实 ShapePhysics.Pin");

        // 最终列逐格等于 .T.c。
        AssertColumnCells(pinned, 0, ".T.c");
        Assert.Equal(
            ".T.c",
            ShapeSignature.ColumnText(pinned, 0),
            "顶针后目标列应逐格等于 .T.c");

        // 末步得到目标角。
        Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
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

        /*
         * 生成器一侧（2026-11-17 第三轮改动 2 / 改动 3）：`.T.T.c` 是
         * 「N 连续针段」族的四个回归之一，现在由 ① 构造 H 一次顶针完成：
         * A = .T.Tc.、B = cTTT..、E = 满高纯晶体柱、N = TPPPT.，
         * 一次 Pin 直接回到目标，不再有弱落步。
         */
        ShapeDocument target = FromColumns(4, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

        Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
        Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm);
        Assert.Equal(AngleConstruction.H, order.Construction);
        Assert.True(order.IsComplete, order.Note ?? ".T.T.c 应可制作");
        Assert.True(order.Note is null, order.Note ?? "通过时不应有未覆盖说明");

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Pin,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        Assert.Equal(1, order.Rank);
        Assert.Equal(
            1,
            order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin));
        Assert.Equal(
            0,
            order.Steps.Count(step => step.Kind == AngleWorkStepKind.WeakDrop));

        ShapeDocument pre = order.Steps[0].Shape!;
        ShapeDocument pinned = Step(order, AngleWorkStepKind.Pin).Shape!;

        // 前态每列合法、稳定；四列布局 A | B | E | N。
        Assert.True(
            ShapeSignature.AllColumnsLegal(pre, out _),
            "前态每列都应过五项条件");
        Assert.False(
            order.HasIllegalPreState(out _, out _),
            "算法 6 的前态不应含非法列");
        Assert.True(ShapeOps.IsStable(pre), "前态应稳定");
        Assert.Equal(3, order.AuxiliaryColumns[0]);
        Assert.Equal(1, order.AuxiliaryColumns[1]);
        Assert.Equal(2, order.AuxiliaryColumns[2]);
        Assert.Equal(".T.Tc.", ShapeSignature.ColumnText(pre, 0));
        Assert.Equal("cTTT..", ShapeSignature.ColumnText(pre, 1));
        Assert.Equal("TPPPT.", ShapeSignature.ColumnText(pre, 3));
        Assert.True(
            ShapeSignature.ColumnText(pre, 2)
                .All(code => code == ShapeSignature.CrystalCode),
            "E 列应是满高纯晶体柱");

        // 前态 A 的第 1 层必须为空（否则 Pin 会补出目标没有的针）。
        Assert.Equal(
            ShapeSignature.EmptyCode,
            ShapeSignature.ColumnText(pre, 0)[0],
            "前态 A 的第 1 层必须为空");

        // A 带洞 ⇒ 要么整份交换链子配方、要么 Q1 一层子配方证明可造。
        Assert.True(
            order.SubRecipe is not null || order.Q1SubRecipe is not null,
            "构造 H 的带洞前态必须带子配方");
        Assert.True(
            order.Q1SubRecipe is not null,
            ".T.Tc. 的洞需要 Q1 子配方证明可造");
        Assert.Equal(RecipeKind.Cut, order.Q1SubRecipe!.Kind);
        Assert.Equal(0, order.Q1SubRecipe.Rank);
        Assert.True(
            Replay.Verify(
                order.Q1SubRecipe,
                null,
                out IReadOnlyList<string> subErrors),
            "Q1 子配方应能端到端回放：" + string.Join(",", subErrors));

        // Pin 步等于真实顶针；顶针后目标列逐格等于目标。
        Assert.True(
            ShapeOps.Same(ShapePhysics.Pin(pre).Document, pinned),
            "Pin 步快照应等于真实顶针结果");
        AssertColumnCells(pinned, 0, text);
        Assert.Equal(
            text,
            ShapeSignature.ColumnText(pinned, 0),
            "顶针后的目标列应逐格等于 .T.T.c");

        // 末步得到目标角。
        Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
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

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

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
                AngleTestHelpers.TargetColumnMatches(order, target),
                "末步应得到目标角");
    }

    /// <summary>
    /// §6 的 n ≥ 6 分档：算法 1（提升）与算法 2（正规核心）现在都是
    /// <b>同一份构造 F 工序</b>的前两阶段——有提升窗口时标算法 3（构造 F 送回），
    /// 没有提升窗口时标算法 2（只破除正规核心的临时晶体）；
    /// n = 4 其余仍走算法 4（递归提升 + 构造 G 环下落）。
    ///
    /// <para>
    /// n ≥ 6 不再生成 <see cref="AngleConstruction.FillBreak"/>：这是
    /// docs/n6-corner-algorithm.md §4 的修正口径。
    /// </para>
    /// </summary>
    public static void AlgorithmTiersSelectPaperBranch()
    {
        // 算法 1 的提升阶段：缺口下方有普通格，先抬到缺口顶端、再全填临时晶体。
        AngleWorkOrder lift = AngleWorkOrderBuilder.Build(
            FromColumns(6, 6, (0, "T..ccc")),
            0,
            6);

        Assert.Equal(AngleAlgorithm.Algorithm3, lift.Algorithm);
        Assert.Equal(AngleConstruction.F, lift.Construction);
        Assert.True(lift.IsComplete, lift.Note ?? "带提升窗口的构造 F 应验证通过");
        Assert.Equal(
            "ccTccc",
            ShapeSignature.ColumnText(
                Step(lift, AngleWorkStepKind.PreState).Shape!,
                0),
            "算法 1 的前态应把 T 抬到缺口顶端、再把空位填成临时晶体");

        // 算法 2（正规核心）：没有提升窗口，只需要破除填出来的临时晶体。
        AngleWorkOrder core = AngleWorkOrderBuilder.Build(
            FromColumns(6, 8, (0, "cT..Tccc")),
            0,
            8);

        Assert.Equal(AngleAlgorithm.Algorithm2, core.Algorithm);
        Assert.Equal(AngleConstruction.F, core.Construction);
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

        // 算法 4（n = 4、D 不成立）：路线 F 的首选是构造 G（§15 单孔环下落），
        // 碎晶等价（构造 D）只在 G 不适用/回放失败时回退。
        AngleWorkOrder g = AngleWorkOrderBuilder.Build(
            FromColumns(4, 8, (0, "cT.T.ccc")),
            0,
            8);

        Assert.Equal(AngleAlgorithm.Algorithm4, g.Algorithm);
        Assert.Equal(AngleConstruction.G, g.Construction);
        Assert.True(g.IsComplete, g.Note ?? "算法 4 应验证通过");
    }

    #endregion

    #region 角工序：算法 4 的 §11 通用实现（六条验收语料）

    /// <summary>
    /// 路线 F 的验收语料，逐格比对：
    /// <c>T.ccc</c>(5) / <c>cTT.cccT.T.ccc</c>(14) / <c>cT.T.T.T...ccc</c>(14) /
    /// <c>cT.T....T..ccc</c>(14) 走<b>构造 G</b>（§15 单孔环下落）；
    /// <c>cTT..T.c</c>(8) 没有三连晶（G1 不成立）⇒ 回退碎晶等价路线（构造 D）；
    /// <c>cT.T.T.c</c>(8) 是 §4.10 的反例（F 不成立）⇒ 必须判「未覆盖」而不是完成。
    ///
    /// <para>每个通过案例都断言：</para>
    /// <list type="bullet">
    /// <item><see cref="AngleWorkOrder.Algorithm"/> = 算法 4、<see cref="AngleWorkOrder.Construction"/> = 期望的构造；</item>
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
        var cases = new (string Text, int Layers, string? PreStateAnchor, AngleConstruction Construction)[]
        {
            ("T.ccc", 5, "cTccc", AngleConstruction.G),
            ("cTT.cccT.T.ccc", 14, null, AngleConstruction.G),
            ("cT.T.T.T...ccc", 14, null, AngleConstruction.G),
            ("cT.T....T..ccc", 14, null, AngleConstruction.G),
            // G1 不成立（没有 ≥3 的晶体段）：回退构造 D 的碎晶等价路线。
            ("cTT..T.c", 8, "cTccTcTc", AngleConstruction.D)
        };

        // 预热 JIT：首调用包含 JIT，不能算进「单角耗时 < 50 ms」。
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 5, (0, "T.ccc")), 0, 5);

        foreach ((string text, int layers, string? preStateAnchor, AngleConstruction construction) in cases)
        {
            ShapeDocument target = FromColumns(4, layers, (0, text));

            AngleWorkOrder order;

            var watch = Stopwatch.StartNew();

            order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

            watch.Stop();

            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind, text);
            Assert.True(order.IsComplete, order.Note ?? $"{text} 未覆盖");
            Assert.True(order.Note is null, order.Note ?? $"{text} 通过时不应有 Note");
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm, text);
            Assert.Equal(construction, order.Construction, text);
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

        // §4.10 的反例：cT.T.T.c 的 D ✘、F ✘ ⇒ S ✘ ⇒ 判「不可制作」。
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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 8, preferCrystalBreak: false);

            Assert.False(order.IsComplete, "S 判据不成立的反例必须报不可制作");
            Assert.True(order.Note is not null, "必须给出不可制作说明");
            Assert.True(
                order.Note!.StartsWith(
                    AngleWorkOrderBuilder.NotProduciblePrefix,
                    StringComparison.Ordinal),
                "失败前缀必须是「不可制作」而不是「未覆盖」：" + order.Note);
            Assert.False(
                order.Steps.Any(step =>
                    step.Kind is AngleWorkStepKind.Cut or
                        AngleWorkStepKind.Collapse or
                        AngleWorkStepKind.Support or
                        AngleWorkStepKind.Target),
                "不可制作时不能伪造后续步骤");
        }
    }

    /// <summary>
    /// 构造 G（§15 四象限单孔环下落）作为路线 F 的标准恢复路线：验收语料逐条核对。
    ///
    /// <para>表格（目标 / 层数 / 是否走 G / 依据）：</para>
    /// <list type="bullet">
    /// <item><c>cT.T.ccc</c>(8) ⇒ G，§15.9 实测实例；</item>
    /// <item><c>T.ccc</c>(5) ⇒ G，§4.7 的长段恢复（三连晶）；</item>
    /// <item><c>cTT.cccT.T.ccc</c>(14) ⇒ G，§4.8；</item>
    /// <item><c>cT.T.T.T...ccc</c>(14) ⇒ G，§4.8.1 阶梯式抬升；</item>
    /// <item><c>cTT..T.c</c>(8) ⇒ <b>不</b>走 G（G1 不成立：没有 ≥3 的晶体段）⇒ 回退构造 D；</item>
    /// <item><c>cT.T.T.c</c>(8) ⇒ F 不成立 ⇒ 仍判「未覆盖」（见 <see cref="Algorithm4GenericPlanCoversAcceptanceCorpus"/>）。</item>
    /// </list>
    ///
    /// <para>
    /// 每个 G 案例都核对：<c>IsComplete</c>、步骤链 <c>PreState → Cut → Support → Target</c>、
    /// 每条前态每列合法且稳定、前态目标列 ≠ 目标、每一步用真实 <c>ShapePhysics</c> 逐格回放、
    /// 最终列逐格等于目标、前态四列与实测布局一致、耗时 &lt; 50 ms。
    /// </para>
    /// </summary>
    /// <summary>
    /// 任务一（docs §17.7）：7 个「仅辅助列有洞」的案例，把洞换成同层晶体后
    /// 仍然逐格回放等于目标，且所有辅助列都是紧列。
    ///
    /// <para>修复前的辅助列（洞用 <c>.</c> 表示）：</para>
    /// <list type="bullet">
    /// <item><c>T.ccc</c> ⇒ <c>.TTPT</c>；<c>T.ccc.</c> / <c>T.cccT</c> /
    /// <c>T.cccP</c> / <c>T.cccc</c> ⇒ <c>.TTPTT</c>；</item>
    /// <item><c>TT.ccc</c> ⇒ <c>T.TTPT</c>；<c>P.T..c</c> ⇒ <c>cT.T..</c>
    /// （这一例走算法 5 的落块柱，不是构造 G）。</item>
    /// </list>
    /// </summary>
    public static void AuxiliaryColumnHolesBecomeCrystals()
    {
        var cases = new (string Text, string ExpectedPillar, AngleConstruction Construction)[]
        {
            ("T.ccc", "cTTPT", AngleConstruction.G),
            ("T.ccc.", "cTTPTT", AngleConstruction.G),
            ("T.cccT", "cTTPTT", AngleConstruction.G),
            ("T.cccP", "cTTPTT", AngleConstruction.G),
            // 断言更新的原因：P.T..c 现在由构造性构造 H 完成（docs §18.4），
            // 前态第 2 象限是 B 列（按 z+1 ∈ I 放晶体，其余普通格），整列紧列。
            // 2026-11-16 再次更新：§13.6 的改正 2 让 B / N 不再满高——B 只铺到
            // max(I)+1（= 源层 4），再往上留空（满高的 B 会把源普通格锚在原地，
            // 顶针后落不到目标层）。所以这里从 TccTTT 变成 TccT..：
            // P1 普通格 / P2·P3 晶体桥（I={2,3}）/ P4 陪落普通格 / P5·P6 留空。
            ("P.T..c", "TccT..", AngleConstruction.H),
            ("TT.ccc", "TcTTPT", AngleConstruction.G),
            ("T.cccc", "cTTPTT", AngleConstruction.G)
        };

        foreach ((string text, string expectedPillar, AngleConstruction construction) in cases)
        {
            int layers = text.Length;
            ShapeDocument target = FromColumns(4, layers, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

            Assert.True(order.IsComplete, text + " 应完成：" + (order.Note ?? "?"));
            Assert.Equal(construction, order.Construction, text + " 的构造不符");
            Assert.False(
                order.HasIllegalPreState(out int preIndex, out int badColumn),
                $"{text} 的前态不应有非法列（第 {preIndex + 1} 条第 {badColumn + 1} 象限）");

            // 每条前态：所有非空辅助列都必须是紧列。
            foreach (AngleWorkStep step in order.Steps)
            {
                if (step.Kind != AngleWorkStepKind.PreState || step.Shape is null)
                {
                    continue;
                }

                for (int part = 0; part < step.Shape.PartCount; part++)
                {
                    if (part == order.Column)
                    {
                        continue;
                    }

                    string columnText = ShapeSignature.ColumnText(step.Shape, part);

                    if (columnText.All(code => code == ShapeSignature.EmptyCode))
                    {
                        continue;
                    }

                    Assert.True(
                        ShapeSignature.IsTightColumn(step.Shape, part),
                        $"{text} 的第 {part + 1} 象限辅助列不是紧列：{columnText}");
                }
            }

            // 辅助列的洞已换成同层晶体（首条前态的第 2 象限就是托柱/落块柱）。
            ShapeDocument firstPreState =
                NthStep(order, AngleWorkStepKind.PreState, 0).Shape!;

            Assert.Equal(
                expectedPillar,
                ShapeSignature.ColumnText(firstPreState, 1),
                text + " 的辅助列应为紧列");

            // 逐格回放：构造 G 走 Cut；算法 5 的塔走一次真实 Pin。
            if (order.Steps.Any(step => step.Kind == AngleWorkStepKind.Cut))
            {
                ShapeDocument rotated = ShapeOps.Rotate(firstPreState, order.Rotation);
                CutResult cut = ShapePhysics.Cut(rotated);

                Assert.True(cut.Success, cut.Error ?? text + " 的切割失败");

                ShapeDocument replayed =
                    ShapeOps.Rotate(cut.West!, -order.Rotation);

                Assert.True(
                    ShapeOps.Same(
                        replayed,
                        order.Steps.First(step =>
                            step.Kind == AngleWorkStepKind.Cut).Shape),
                    text + " 的 Cut 步应逐格等于真实 ShapePhysics.Cut 的西半边");
            }
            else
            {
                ShapeDocument pinned = ShapePhysics.Pin(firstPreState).Document!;

                Assert.True(
                    ShapeOps.Same(pinned, Step(order, AngleWorkStepKind.Pin).Shape),
                    text + " 的一次顶针应逐格等于真实 ShapePhysics.Pin");
            }

            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                text + " 的末步应逐格等于目标");
        }
    }

    /// <summary>
    /// 任务二（docs §6 第 7 条 / §17.7）：前态洞闸门必须区分辅助列与目标列。
    ///
    /// <list type="number">
    /// <item>辅助列有洞 ⇒ 拒绝（无论目标列是否带子配方）；</item>
    /// <item>目标列有洞、且带子配方（<c>.T.c</c> 的塔 <c>.Tc.</c>）⇒ 允许；</item>
    /// <item>目标列有洞、但没有子配方 ⇒ 拒绝。</item>
    /// </list>
    /// </summary>
    public static void PreStateHoleGateDistinguishesAuxiliaryAndTarget()
    {
        // ── 1. 辅助列有洞：拒绝 ──
        ShapeDocument auxiliaryHole = FromColumns(
            4,
            5,
            (0, "cTccc"),
            (1, ".TTPT"));

        Assert.True(
            AngleWorkOrderBuilder.HasIllegalColumnHoles(
                auxiliaryHole,
                0,
                targetColumnHasSubRecipe: true,
                out int badAux),
            "辅助列 .TTPT 有洞，应被拒绝");
        Assert.Equal(1, badAux, "有洞的辅助列是第 2 象限");

        var forgedAuxHole = new AngleWorkOrder(
            0,
            AngleBuildKind.NeedsCrystalBreak,
            new[]
            {
                new AngleWorkStep(
                    "旧前态：托柱留洞",
                    auxiliaryHole,
                    AngleWorkStepKind.PreState),
                new AngleWorkStep(
                    "目标角",
                    FromColumns(4, 5, (0, "T.ccc")),
                    AngleWorkStepKind.Target)
            },
            null)
        {
            IsComplete = true,
            TargetColumnHasSubRecipe = true
        };

        Assert.True(
            forgedAuxHole.HasIllegalPreState(out _, out int badForgedAux),
            "辅助列有洞的工序应被判成假阳性");
        Assert.Equal(1, badForgedAux, "被拒的应是第 2 象限（辅助列）");

        // ── 2. 目标列有洞、且带子配方：允许 ──
        ShapeDocument target = FromColumns(4, 4, (0, ".T.c"));
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 4, preferCrystalBreak: false);

        Assert.True(order.IsComplete, order.Note ?? ".T.c 应可制作");
        Assert.True(order.SubRecipe is not null, "算法 6 的目标列应带造塔子配方");

        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        Assert.False(
            ShapeSignature.IsTightColumn(preState, 0),
            "前态目标列 .Tc. 应带洞（这正是子配方要证明可造的情形）");
        Assert.Equal(
            ".Tc.",
            ShapeSignature.ColumnText(preState, 0),
            "前态目标列应是 .Tc.");

        Assert.False(
            order.HasIllegalPreState(out _, out int badTarget),
            "目标列有洞但带子配方，应被接受：" + badTarget);

        ShapeDocument targetHole = FromColumns(4, 4, (0, ".Tc."));

        Assert.False(
            AngleWorkOrderBuilder.HasIllegalColumnHoles(
                targetHole,
                0,
                targetColumnHasSubRecipe: true,
                out _),
            "目标列有洞且带子配方，应通过闸门");

        // ── 3. 目标列有洞、但没有子配方：拒绝 ──
        Assert.True(
            AngleWorkOrderBuilder.HasIllegalColumnHoles(
                targetHole,
                0,
                targetColumnHasSubRecipe: false,
                out int badNoSubRecipe),
            "目标列有洞但没有子配方，应被拒绝");
        Assert.Equal(0, badNoSubRecipe, "被拒的应是目标列");

        AngleWorkOrder stripped = order with
        {
            SubRecipe = null,
            TargetColumnHasSubRecipe = false
        };

        Assert.True(
            stripped.HasIllegalPreState(out _, out int badStripped),
            "剥掉子配方的工序应被判成假阳性");
        Assert.Equal(0, badStripped, "被拒的应是目标列（第 1 象限）");
    }

    /// <summary>
    /// 用户实测的双闸门案例（12 层）：<c>T.cccT.T.ccc</c>。
    ///
    /// <para>
    /// 旧输出前态目标列是 <c>T.cccT..Tccc</c>（第 7、8 层两个洞），
    /// 既没有填紧、也没有子配方 ⇒ 等于「凭空出现」，违反 docs §5 规则六 / §6 第 7 条。
    /// 现在：
    /// </para>
    /// <list type="number">
    /// <item>把第 7、8 层的间隙洞填成同层临时晶体；</item>
    /// <item>把最底晶体下的洞（第 2 层）用「地面普通格抬到第 2 层、地面填临时晶体」填紧；</item>
    /// <item>底层临时晶体与闸门晶体都用环缝另一侧的保险丝列一刀碎掉，普通格落回地面。</item>
    /// </list>
    /// <para>
    /// 前态因此是**全紧列** <c>cTcccTccTccc</c>，辅助列只有「支撑 + 保险丝」两列
    /// （第三列整列为空）；Cut 步已经是「碎晶并落定」的最终态，逐格等于
    /// 目标 <c>T.cccT.T.ccc</c>，不再输出 <c>.TcccT..Tccc</c> 这种「半破后、下落前」快照。
    /// </para>
    /// </summary>
    public static void DoubleGateGapFillsTargetColumn()
    {
        const string text = "T.cccT.T.ccc";
        const int layers = 12;

        ShapeDocument target = FromColumns(4, layers, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

        Assert.True(order.IsComplete, order.Note ?? $"{text} 应完成");
        Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm, text);
        Assert.Equal(AngleConstruction.G, order.Construction, text);
        Assert.Equal(0, order.Rank, text);
        Assert.True(order.SubRecipe is null, "该案例应靠紧前态，而不是造塔子配方");

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Cut,
            AngleWorkStepKind.Note,
            AngleWorkStepKind.Target);

        ShapeDocument preState = NthStep(order, AngleWorkStepKind.PreState, 0).Shape!;

        Assert.Equal(
            "cTcccTccTccc",
            ShapeSignature.ColumnText(preState, 0),
            "目标列的两个洞应填成同层临时晶体；最底晶体下的洞用「抬普通格 + 地面填晶体」填紧");

        // 验收：前态只有 3 列有料（目标列 + 支撑 + 保险丝），第三列整列为空。
        Assert.Equal(
            "cTcccTccTccc|PPTTTTTcTTPT|............|cTTTTTTc....",
            string.Join(
                "|",
                Enumerable.Range(0, 4).Select(part =>
                    ShapeSignature.ColumnText(preState, part))),
            "两列辅助：支撑 B + 保险丝 N（c + 中间普通格 + 闸门晶体），第三列留空");

        Assert.Equal(
            "1,3",
            string.Join(",", order.AuxiliaryColumns),
            "辅助列应只有支撑列与保险丝列");

        for (int part = 0; part < preState.PartCount; part++)
        {
            Assert.True(
                ShapeSignature.IsTightColumn(preState, part),
                $"前态第 {part + 1} 象限必须是紧列：" +
                ShapeSignature.ColumnText(preState, part));
        }

        Assert.False(
            order.HasIllegalPreState(out _, out int badColumn),
            "紧前态不应有非法列：" + badColumn);
        Assert.True(ShapeOps.IsStable(preState), "前态必须稳定");

        // 真实回放：Cut 自带一次下落，Cut 步就是最终态。
        CutResult cut = ShapePhysics.Cut(
            ShapeOps.Rotate(preState, order.Rotation));

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(
            cut.West!,
            -order.Rotation);

        ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

        Assert.True(
            ShapeOps.Same(replayed, cutStep),
            "Cut 步应等于真实 Cut 出来的西半边（已落定）");
        Assert.Equal(
            text,
            ShapeSignature.ColumnText(cutStep, 0),
            "碎晶并落定之后目标列应逐格等于目标（不再是半破后快照）");

        Assert.False(
            order.Steps.Any(step => step.Kind == AngleWorkStepKind.Collapse),
            "步骤链里不能再出现 Collapse 中间态");

        Assert.True(
            AngleTestHelpers.TargetColumnMatches(order, target),
            "末步应得到目标角");
    }

    public static void ConstructGIsStandardRouteForLongCrystalRuns()
    {
        /*
         * 闸门改晶体后，`cTT.cccT.T.ccc` 的双闸门前态（闸门 2、9）要求
         * 环伙伴同时放下方针（第 6 层）和闸门晶体（第 9 层）——
         * 针上方出现晶体 ⇒ 违反五项条件二，新构造 G 的候选因此作废；
         * 但等价的单发 G（PreState → Cut → Collapse → Note → Target）仍然成立。
         *
         * 前态目标列的两个间隙洞（第 9、10 层）填成同层临时晶体（紧列），
         * 第 4 层的晶体下洞用「抬普通格 + 让出层填晶体」填紧；
         * 底层临时晶体由环缝另一侧的 N 列保险丝一刀碎掉。
         * 这样前态不再「凭空出现」带洞的目标列（docs §5 规则六 / §6 第 7 条）。
         */
        var cases = new (string Text, int Layers, bool UsesG, bool NewStyleG, string PreStateColumns)[]
        {
            (
                "cT.T.ccc",
                8,
                true,
                true,
                "cTccTccc|TTTcTTPT|........|TTTcTTTT"),
            (
                "T.ccc",
                5,
                true,
                true,
                "cTccc|cTTPT|.....|cTTTT"),
            (
                "cTT.cccT.T.ccc",
                14,
                true,
                false,
                "cTcTcccTccTccc|PPPPTTTTTcTTPT|..............|TTcTTTTTTc...."),
            (
                "cT.T.T.T...ccc",
                14,
                true,
                true,
                "cTccTccTccTccc|TTTcTTcTTcTTPT|..............|TTTcTTcTTcTTTT"),
            (
                "cTT..T.c",
                8,
                false,
                false,
                /*
                 * T4（docs §5.2）：构造 D 先试两列（支撑 + 爆破），第三象限留空。
                 * 旧断言是四列布局（第三象限满高普通柱），现已按最少化降为 2 列。
                 */
                "cTccTcTc|TTTPPTPT|........|TTTTTcTT")
        };

        // 预热 JIT：首调用包含 JIT，不能算进「单角耗时 < 50 ms」。
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 5, (0, "T.ccc")), 0, 5);
        _ = AngleWorkOrderBuilder.Build(FromColumns(4, 8, (0, "cT.T.ccc")), 0, 8);

        foreach ((string text, int layers, bool usesG, bool newStyleG, string preStateColumns) in cases)
        {
            ShapeDocument target = FromColumns(4, layers, (0, text));

            var watch = Stopwatch.StartNew();

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

            watch.Stop();

            Assert.True(order.IsComplete, order.Note ?? $"{text} 应完成");
            Assert.True(order.Note is null, order.Note ?? $"{text} 通过时不应有 Note");
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm, text);
            Assert.Equal(
                usesG ? AngleConstruction.G : AngleConstruction.D,
                order.Construction,
                text + " 的构造不符（长段应优先构造 G，G1 不成立才回退构造 D）");
            Assert.Equal(0, order.Rank, text);
            Assert.True(
                watch.ElapsedMilliseconds < 50,
                $"{text} 单角耗时 {watch.ElapsedMilliseconds} ms，应 < 50 ms");

            if (usesG)
            {
                if (newStyleG)
                {
                    AssertStepKinds(
                        order,
                        AngleWorkStepKind.PreState,
                        AngleWorkStepKind.Cut,
                        AngleWorkStepKind.Support,
                        AngleWorkStepKind.Target);
                }
                else
                {
                    // 双闸门 + 下方针的环伙伴不能放闸门晶体（针上方出现晶体会
                    // 违反五项条件二），退回等价的单发 G：
                    // PreState → Cut（碎晶并落定）→ Note（不结晶）→ Target。
                    AssertStepKinds(
                        order,
                        AngleWorkStepKind.PreState,
                        AngleWorkStepKind.Cut,
                        AngleWorkStepKind.Note,
                        AngleWorkStepKind.Target);
                }
            }

            // 前态布局：G / D 都是「目标列 / 支撑 B / 第三列留空 / 爆破柱（桥柱或保险丝）」。
            // 逐块各一刀时有两条前态，这里取第一条（规范核心列那条）。
            ShapeDocument preState = NthStep(order, AngleWorkStepKind.PreState, 0).Shape!;

            string actualColumns = string.Join(
                "|",
                Enumerable.Range(0, 4).Select(part =>
                    ShapeSignature.ColumnText(preState, part)));

            Assert.Equal(preStateColumns, actualColumns, text + " 的前态布局不符");

            // §6 第 1–3 条：前态每列合法、稳定、目标列 ≠ 目标。
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                text + " 的前态存在非法列");
            Assert.True(ShapeOps.IsStable(preState), text + " 的前态应稳定");
            Assert.NotEqual(
                text,
                ShapeSignature.ColumnText(preState, 0),
                text + " 的前态目标列必须与目标不同");

            // §6 第 4–5 条：每一步真实回放、最终列逐格等于目标。
            if (usesG && !newStyleG)
            {
                ReplaySingleRoundG(order, target, 0);
            }
            else
            {
                ReplayGenericAlgorithm4(order, target, 0);
            }

            AssertColumnCells(Step(order, AngleWorkStepKind.Target).Shape!, 0, text);

            if (!usesG)
            {
                continue;
            }

            // 不变量三：切割只碎临时晶体与保险丝，目标列原生晶体一颗不少。
            ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

            Assert.Equal(
                text.Count(code => code == 'c'),
                CountCrystals(cutStep, 0),
                text + " 切割后目标列应只剩原生晶体");

            Assert.True(
                CountTemporaryCrystals(preState, target) > 0,
                text + " 的构造 G 前态必须真有临时晶体");

            Assert.True(
                order.Limitations.Any(line => line.Contains("构造 G")),
                text + " 的限制说明应写明用的是构造 G");
        }

        // cTT..T.c 不走 G 的**原因**是结构谓词 G1：没有 ≥3 的连续晶体段。
        int longestRun = 0;
        int run = 0;

        foreach (char code in "cTT..T.c")
        {
            run = code == 'c' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        Assert.Equal(1, longestRun, "cTT..T.c 没有三连晶 ⇒ 构造 G 不适用");
    }

    /// <summary>
    /// 覆盖率闸门（文档 §10「禁止打表」的机器检查）：4 象限、4–9 层、<c>{.,T,c}</c>
    /// 全枚举里，凡是**路线 F 且 F 成立**的合法列都必须完成，未覆盖数必须是 0。
    ///
    /// <para>
    /// 「F ✘」的形态（<c>cT.T.c</c> 一族：五项 ✔ 但不是 n = 4 的 S）不算在内——
    /// 它们本来就不该被路线 F 覆盖，与构造 G 的实现无关。
    /// </para>
    /// <para>
    /// 这个数字在实现构造 G 之前是 0，实现之后仍然是 0：G 是**首选**路线，
    /// 不适用时回退构造 D，所以覆盖率只增不减（见 TODO.md §7.9 的实测表）。
    /// </para>
    /// </summary>
    public static void RouteFFullyCoveredForFourToNineLayers()
    {
        var palette = new[] { '.', 'T', 'c' };
        var watch = Stopwatch.StartNew();

        for (int layers = 4; layers <= 9; layers++)
        {
            long total = (long)Math.Pow(3, layers);
            var buffer = new char[layers];
            int routeF = 0;

            for (long code = 0; code < total; code++)
            {
                long rest = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = palette[rest % 3];
                    rest /= 3;
                }

                string text = new(buffer);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.RecursiveTerminalF(text) ||
                    buffer[0] == 'P' ||
                    PillarSCriterion.BottomResourceD(text))
                {
                    continue;
                }

                ShapeDocument target = FromColumns(4, layers, (0, text));

                if (AngleClassifier.Classify(target, 0) != AngleBuildKind.NeedsCrystalBreak)
                {
                    continue;
                }

                routeF++;

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

                Assert.True(
                    order.IsComplete,
                    $"{layers} 层、路线 F 的 {text} 未覆盖：" + (order.Note ?? "?"));
                Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm, text);
            }

            Assert.True(routeF > 0, layers + " 层应存在「路线 F 且 F 成立」的合法列");
        }

        watch.Stop();

        Assert.True(
            watch.ElapsedMilliseconds < 60000,
            "4–9 层全枚举耗时 " + watch.ElapsedMilliseconds + " ms，超过测试预算");
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
            // 算法 1（提升）+ 算法 2（正规核心）→ 构造 F：缺口在晶体塔下方，
            // 先把下方的普通格抬到缺口顶端，再把最高晶体以下的空位全填成临时晶体。
            (6, 6, "T..c..", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 同上，缺口高两层。
            (6, 6, "T...c.", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 算法 2（正规核心）：没有提升窗口，只把缺口填成紧核对再碎晶。
            (6, 6, "cT..Tc", AngleAlgorithm.Algorithm2, AngleConstruction.F),

            // 算法 3（构造 F）：缺口就在晶体段正下方，一次受控下落到 d 层。
            (6, 6, ".T..c.", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 算法 3（构造 F），缺口在下方那个晶体段正下方（不在最高晶体下方）。
            (6, 6, ".T.cTc", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 算法 3（构造 F），n = 8。
            (8, 8, "..T.c...", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // 算法 3（构造 F）：两段晶体都在空位上方，初始前态必须是
            // ccTcTcTc（算法 1 提升 + 算法 2 全填临时晶体的正规核心，紧列、P1 ✔），
            // 一刀落回 .T.cTT.c。
            (6, 8, ".T.cTT.c", AngleAlgorithm.Algorithm3, AngleConstruction.F),

            // n = 6、9 层的 cTT.cTT.c：不再走 FillBreak，改走构造 F 四列；
            // col0 = cTcTcTcTc（紧列、P1 ✔），E 是满高纯晶体柱。
            (6, 9, "cTT.cTT.c", AngleAlgorithm.Algorithm3, AngleConstruction.F)
        };

        foreach ((int parts, int layers, string text, AngleAlgorithm algorithm,
                  AngleConstruction construction) in cases)
        {
            ShapeDocument target = FromColumns(parts, layers, (0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

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

            // ── P1：初始前态每颗晶体正下方非空（n ≥ 6 的硬闸门）──
            Assert.True(
                AngleWorkOrderBuilder.SatisfiesP1(preState),
                label + " 的初始前态必须满足 P1（每颗晶体坐在材料上）");

            if (text == ".T.cTT.c")
            {
                string column = ShapeSignature.ColumnText(preState, 0);

                Assert.Equal(
                    "ccTcTcTc",
                    column,
                    "n=6 的 .T.cTT.c：前态 col0 必须是算法 1 提升 + 算法 2 全填后的 ccTcTcTc");

                Assert.True(
                    ShapeSignature.IsTightColumn(column),
                    "前态 col0 必须是紧列：ccTcTcTc");

                /*
                 * 算法 2 填出的临时晶体：L1（目标也是空）以及两次提升让出的
                 * L2 / L6（被提升的普通格在 L3 / L7）。L3、L7 是提升后的普通格，
                 * 不能改成临时晶体——它们与原生晶体 L4 / L8 竖直相邻，
                 * 改成晶体后会在 Cut 里被整簇打碎（实测 Cut 结果 .T..TT..）。
                 */
                foreach (int layer in new[] { 0, 1, 5 })
                {
                    Assert.Equal(
                        CellType.Crystal,
                        preState[layer, 0].Type,
                        $"第 {layer + 1} 层应是临时晶体");
                }

                Assert.Equal(CellType.Shape, preState[2, 0].Type);
                Assert.Equal(CellType.Shape, preState[6, 0].Type);
            }

            if (text == "cTT.cTT.c")
            {
                Assert.Equal(
                    "cTcTcTcTc",
                    ShapeSignature.ColumnText(preState, 0),
                    "n=6 的 cTT.cTT.c 前态 col0 应是算法 1 + 算法 2 的紧列");

                Assert.True(
                    ShapeSignature.IsTightColumn(preState, 0),
                    "前态 col0 必须是紧列：cTcTcTcTc");

                Assert.NotEqual(AngleConstruction.FillBreak, order.Construction);

                // 四连位 N | A | B | E：N 在 s 层放针，B 放晶体通道，E 满高纯晶体。
                Assert.Equal(
                    "TTTPTTTPT",
                    ShapeSignature.ColumnText(preState, 5),
                    "N 应在两次提升的源层（第 4、8 层）放顶针");

                Assert.Equal(
                    "TTcTTTcTT",
                    ShapeSignature.ColumnText(preState, 1),
                    "B 应在第 d..s−1 层放晶体通道（第 3、7 层）");

                Assert.Equal(
                    "ccccccccc",
                    ShapeSignature.ColumnText(preState, 2),
                    "E 必须是满高纯晶体柱");

                foreach (int part in new[] { 0, 1, 2, 5 })
                {
                    Assert.True(
                        ShapeSignature.ColumnText(preState, part)
                            .Any(code => code != ShapeSignature.EmptyCode),
                        $"第 {part + 1} 象限必须有材料");
                }
            }

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

            // Cut 步已经是「切割 + 落定」的最终态。
            Assert.True(
                ShapeOps.Same(replayed, cutStep),
                label + " 的 Cut 步应等于真实 Cut 出来的西半边（已落定）");

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

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(cutStep, 0),
                label + " 的落定后目标列应逐格等于目标");

            Assert.True(
                ShapeOps.IsStable(cutStep),
                label + " 落定后的中间态应稳定");

            Assert.False(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Collapse),
                label + " 的步骤链不应再出现 Collapse 中间态");

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
                AngleTestHelpers.TargetColumnMatches(order, target),
                label + " 的末步应逐格等于目标");
        }

        // cap 高于目标层数：层号照样由扫描得出，分档不受影响。
        ShapeDocument shortTarget = FromColumns(6, 4, (0, "T..c"));

        AngleWorkOrder tall = AngleWorkOrderBuilder.Build(shortTarget, 0, 6);

        Assert.Equal(AngleAlgorithm.Algorithm3, tall.Algorithm);
        Assert.True(tall.IsComplete, tall.Note ?? "cap 高于目标层数也应完成");
        Assert.Equal(
            "T..c..",
            ShapeSignature.ColumnText(
                Step(tall, AngleWorkStepKind.Cut).Shape!,
                0),
            "cap 高于目标层数时，多出来的层应留空");
        Assert.True(
            AngleTestHelpers.TargetColumnMatches(tall, shortTarget),
            "cap 高于目标层数时，末步仍应逐格等于目标");
    }

    /// <summary>
    /// 构造 F 的四列布局（docs/n6-corner-algorithm.md §2 / §4）：沿环四个连续位置
    /// <c>针槽柱 N | 目标 A | 辅助柱 B | 碎晶桥柱 E</c>，其余象限空，四列都有材料；
    /// N 的第 s 层是针、B 的第 d..s−1 层是晶体（外加临时块锚点）、
    /// E 是满高纯晶体柱，其余是 T；目标列先按算法 1 抬升、再按算法 2 全填空位。
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

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

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

        /*
         * 目标列 A：d = 1 的普通格抬到 s = 3；算法 2 再把最高晶体（第 5 层）
         * 以下的空位（第 1、2、3 层）全填成临时晶体。
         */
        Assert.Equal("cccTc..", ShapeSignature.ColumnText(preState, 0));
        Assert.True(ShapeSignature.IsTightColumn(preState, 0), "前态目标列应是紧列");

        // N：第 s 层针，其余 T。
        Assert.Equal("TTTPTTT", ShapeSignature.ColumnText(preState, 5));

        // B：第 d..s−1 层晶体通道，其余 T（临时块顶 = 2 已在通道里）。
        Assert.Equal("TccTTTT", ShapeSignature.ColumnText(preState, 1));

        // E：满高纯晶体柱（§4 禁止把辅助列留空）。
        Assert.Equal("ccccccc", ShapeSignature.ColumnText(preState, 2));

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

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

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
                AngleTestHelpers.TargetColumnMatches(order, target),
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

    #region 角工序：核心 / 尾部拆分与族 B/C 回归

    /// <summary>
    /// docs §17 族 A：最高晶体上方带 T/P 尾部。核心（最高晶体及以下）先按现有
    /// 流程做完，尾部由构造 C 逐层接回；前态不得含非法列，最终逐格等于目标。
    /// </summary>
    public static void CoreTailCoversFamilyA()
    {
        foreach (string text in new[]
                 {
                     ".T.cT", ".T.cP", "..T.cT", "P.T.cT", ".T.c.T", ".T.ccP"
                 })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            Assert.True(
                ShapeSignature.IsLegalColumn(target, 0),
                text + " 应是合法列");
            Assert.True(
                PillarSCriterion.IsPillarColumn(target, 0),
                text + " 应属于 n = 4 的 S");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
            Assert.True(order.Note is null, text + " 通过时不应有 Note");
            Assert.True(
                order.Steps.Any(step =>
                    step.Kind == AngleWorkStepKind.Stack),
                text + " 的尾部必须由构造 C 的逐层追加步接回");
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                text + " 的前态不应含非法列");

            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                text + " 的末步目标列应逐格等于目标");
        }
    }

    /// <summary>
    /// T3（docs §5.3）：12 个尾部角全部走「核心 + 尾部两步法」。
    ///
    /// <para>每一份工序单都要满足：</para>
    /// <list type="number">
    /// <item>核心 = 最高晶体及以下（前态的目标列不得含尾部材料）；</item>
    /// <item>先有构造 C 的「延长托柱」步，再有逐格 <see cref="AngleWorkStepKind.Stack"/>；</item>
    /// <item>每条 Stack 只加目标尾部的一格，加完后图形稳定；</item>
    /// <item>空层跳过、非空尾部层每层恰好一条 Stack；</item>
    /// <item>收尾 <see cref="AngleWorkStepKind.Target"/> 逐格等于目标。</item>
    /// </list>
    /// </summary>
    public static void TailTwoStepAcceptsPaperCorpus()
    {
        string[] corpus =
        {
            "T.cT", "T.cP", ".TcT", "T.c.T", ".T.cTT", "T.cTP",
            ".T.cPP", "T.ccT", ".T.ccP", ".T.cT.", ".T.cP.", "T.cT.T"
        };

        var problems = new List<string>();
        var rows = new List<string>();

        foreach (string text in corpus)
        {
            int cap = text.Length;
            ShapeDocument target = FromColumns(4, cap, (0, text));

            if (!PillarSCriterion.IsPillarColumn(target, 0))
            {
                problems.Add(text + "：不在 n = 4 的 S，无法按角工序制作");
                continue;
            }

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            if (!order.IsComplete)
            {
                problems.Add(text + "：未完成（" + (order.Note ?? "无 Note") + "）");
                continue;
            }

            if (order.Note is not null)
            {
                problems.Add(text + "：通过却带 Note：" + order.Note);
            }

            int highest = AngleClassifier.HighestCrystalLayer(target, 0);

            string coreText = text[..(highest + 1)];

            var tailLayers = new List<int>();

            for (int layer = highest + 1; layer < cap; layer++)
            {
                if (target[layer, 0].Type != CellType.Empty)
                {
                    tailLayers.Add(layer);
                }
            }

            if (tailLayers.Count == 0)
            {
                problems.Add(text + "：最高晶体以上没有尾部材料");
                continue;
            }

            AngleWorkStep? preState = order.Steps.FirstOrDefault(
                step => step.Kind == AngleWorkStepKind.PreState &&
                        step.Shape is not null);

            if (preState?.Shape is null)
            {
                problems.Add(text + "：没有前态");
                continue;
            }

            if (!ShapeOps.IsStable(preState.Shape))
            {
                problems.Add(text + "：核心前态不稳定");
            }

            if (ShapeSignature.ColumnText(preState.Shape, 0) == text)
            {
                problems.Add(text + "：前态目标列等于目标（循环）");
            }

            /*
             * 前态只覆盖核心：最高晶体以上的层必须整列为空，
             * 尾部材料只能由后面的 Stack 步一格一格接上来。
             */
            for (int layer = highest + 1; layer < cap; layer++)
            {
                if (preState.Shape[layer, 0].Type != CellType.Empty)
                {
                    problems.Add(
                        text + $"：前态第 {layer + 1} 层带着尾部材料，尾部应留给 Stack");
                }
            }

            var kinds = order.Steps
                .Select(step => step.Kind)
                .ToList();

            int firstStack = kinds.IndexOf(AngleWorkStepKind.Stack);
            int lastSupport = kinds.LastIndexOf(AngleWorkStepKind.Support);

            if (firstStack < 0)
            {
                problems.Add(text + "：没有逐格 Stack 接尾部");
                continue;
            }

            if (lastSupport < 0)
            {
                problems.Add(text + "：Stack 前没有构造 C 的「延长托柱」Support 步");
                continue;
            }

            if (lastSupport > firstStack)
            {
                problems.Add(text + "：延长托柱的 Support 步出现在 Stack 之后");
                continue;
            }

            // 支撑柱必须真的覆盖到目标高度（构造 C：先延长托柱再接尾部）。
            ShapeDocument supportState = order.Steps[lastSupport].Shape!;

            string supportTargetColumn = ShapeSignature.ColumnText(supportState, 0);

            if (supportTargetColumn[..(highest + 1)] != coreText)
            {
                problems.Add(
                    text + "：延长托柱时目标列应已等于核心 " + coreText + "，实际 " +
                    supportTargetColumn);
            }

            if (!HasFullHeightColumn(supportState, 0, cap))
            {
                problems.Add(text + "：延长后的托柱没有覆盖到目标高度");
            }

            if (!ShapeOps.IsStable(supportState))
            {
                problems.Add(text + "：延长托柱后不稳定");
            }

            ShapeDocument previous = supportState;
            int stackIndex = 0;
            var stackedLayers = new List<int>();

            for (int index = lastSupport + 1; index < order.Steps.Count; index++)
            {
                AngleWorkStep step = order.Steps[index];

                if (step.Kind == AngleWorkStepKind.Stack)
                {
                    ShapeDocument? current = step.Shape;

                    if (current is null)
                    {
                        problems.Add(text + "：Stack 步没有图形");
                        break;
                    }

                    int layer = FindSingleChangedLayer(previous, current);

                    if (layer < 0)
                    {
                        problems.Add(
                            text + "：第 " + (stackIndex + 1) + " 条 Stack 不是「只加一格」");
                        break;
                    }

                    if (!tailLayers.Contains(layer))
                    {
                        problems.Add(
                            text + $"：Stack 加在第 {layer + 1} 层，但那一层不是目标尾部");
                    }

                    if (!ShapeOps.SameCell(current[layer, 0], target[layer, 0]))
                    {
                        problems.Add(
                            text + $"：Stack 第 {layer + 1} 层的格子与目标不一致");
                    }

                    if (!ShapeOps.IsStable(current))
                    {
                        problems.Add(
                            text + $"：接到第 {layer + 1} 层后不稳定");
                    }

                    stackedLayers.Add(layer);
                    previous = current;
                    stackIndex++;
                }
                else if (step.Shape is not null)
                {
                    previous = step.Shape;
                }
            }

            if (stackIndex != tailLayers.Count)
            {
                problems.Add(
                    text + $"：Stack 数量 {stackIndex} 与尾部非空层数 {tailLayers.Count} 不一致");
            }

            if (stackedLayers.Count != stackedLayers.Distinct().Count())
            {
                problems.Add(text + "：同一尾部层被接到两次");
            }

            rows.Add(
                $"{text}：构造={order.Construction}，核心={coreText}，" +
                $"尾部层={string.Join(",", tailLayers.Select(layer => layer + 1))}，" +
                $"步骤={string.Join("→", kinds)}，Stack={stackIndex}");

            if (!AngleTestHelpers.TargetColumnMatches(order, target))
            {
                problems.Add(text + "：末步的目标列没有逐格等于目标");
            }

            if (order.HasIllegalPreState(out _, out _))
            {
                problems.Add(text + "：前态含非法列");
            }
        }

        Console.WriteLine(
            $"  TAILCORPUS cases={corpus.Length} problems={problems.Count}");

        foreach (string row in rows)
        {
            Console.WriteLine("    " + row);
        }

        foreach (string problem in problems)
        {
            Console.WriteLine("    " + problem);
        }

        Assert.True(
            problems.Count == 0,
            $"12 个尾部角的两步法有 {problems.Count} 个问题：" +
            string.Join(" | ", problems));
    }

    /// <summary>除目标列外是否存在一根从地面到 <paramref name="cap"/> 的满高柱。</summary>
    private static bool HasFullHeightColumn(
        ShapeDocument document,
        int column,
        int cap)
    {
        for (int part = 0; part < document.PartCount; part++)
        {
            if (part == column)
            {
                continue;
            }

            bool full = true;

            for (int layer = 0; layer < cap; layer++)
            {
                if (document[layer, part].Type != CellType.Shape)
                {
                    full = false;
                    break;
                }
            }

            if (full)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>两张图恰好只有目标列的一层不同时返回该层号，否则返回 -1。</summary>
    private static int FindSingleChangedLayer(
        ShapeDocument before,
        ShapeDocument after)
    {
        if (before.PartCount != after.PartCount ||
            before.LayerCount != after.LayerCount)
        {
            return -1;
        }

        int changed = -1;

        for (int part = 0; part < before.PartCount; part++)
        {
            for (int layer = 0; layer < before.LayerCount; layer++)
            {
                if (ShapeOps.SameCell(before[layer, part], after[layer, part]))
                {
                    continue;
                }

                if (part != 0 || changed >= 0)
                {
                    return -1;
                }

                changed = layer;

                if (after[layer, part].Type == CellType.Empty)
                {
                    return -1;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// docs §17 族 B：底格有材料的两个代表。
    /// <c>T.T..c</c> 的底块提升由 §11 通用计划完成；
    /// <c>P.T..c</c> / <c>P..T.c</c> 底格是针 ⇒ 算法 5，用「底针埋晶 + 加高托柱」
    /// 的一次 Pin 完成。
    /// </summary>
    public static void FamilyBIsConstructible()
    {
        foreach ((string text, AngleAlgorithm algorithm) in new[]
                 {
                     // 断言更新的原因：T.T..c 现在由 §13.6 通用构造 H 直接完成
                     // （P1：晶体下方抬普通格），不再退回算法 4。
                     ("T.T..c", AngleAlgorithm.Algorithm6),
                     ("P.T..c", AngleAlgorithm.Algorithm5),
                     ("P..T.c", AngleAlgorithm.Algorithm5)
                 })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns(4, cap, (0, text));

            Assert.True(
                PillarSCriterion.IsPillarColumn(target, 0),
                text + " 应属于 n = 4 的 S");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
            Assert.True(order.Note is null);
            Assert.Equal(algorithm, order.Algorithm);
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                text + " 的前态不应含非法列");

            Assert.True(
                AngleTestHelpers.TargetColumnMatches(order, target),
                text + " 的末步应逐格等于目标");
        }
    }

    /// <summary>
    /// docs §17 族 C：<c>.T.cTc</c>。
    /// 最高晶体正下方的缺口先被压缩，再用一次 Pin 让临时晶体消失、普通格落回，
    /// 不再走会「删错层」的填充+暴破。
    /// </summary>
    public static void FamilyCColumnIsConstructible()
    {
        const string text = ".T.cTc";
        int cap = text.Length;

        ShapeDocument target = FromColumns(4, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

        Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
        Assert.True(order.Note is null);
        Assert.False(
            order.HasIllegalPreState(out _, out _),
            text + " 的前态不应含非法列");
        Assert.Equal(
            text,
            ShapeSignature.ColumnText(
                Step(order, AngleWorkStepKind.Target).Shape!,
                0),
            text + " 的末步目标列应逐格等于目标");
    }

    #endregion

    #region 半边工序（v4：逆提升 → 各自做角 → 交换 → 拆柱 → 跌落）

    /// <summary>
    /// 经典例 <c>cT.T.cPT | TTTT.ccc</c>：Build 走 v4，外层与 Half 的步骤链、
    /// 中间态、逐格回放都必须成立。
    /// </summary>
    public static void HalfSideV4BuildsAnchor()
    {
        const int cap = 8;

        ShapeDocument target = FromColumns(
            4,
            cap,
            (0, "cT.T.cPT"),
            (1, "TTTT.ccc"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            cap,
            preferCrystalBreak: false);

        Assert.True(order.IsComplete, order.Note ?? "cT.T.cPT 的半边应可做");
        Assert.Equal(AngleConstruction.Half, order.Construction);
        Assert.Equal(AngleAlgorithm.None, order.Algorithm);
        Assert.Equal(0, order.Rank);
        Assert.Equal(0, order.Rotation);

        AssertStepKinds(
            order,
            AngleWorkStepKind.PreState,
            AngleWorkStepKind.Target);

        HalfWorkOrder half = order.Half
            ?? throw new InvalidOperationException("半边工序必须带 Half 字段");

        Assert.True(half.IsComplete, half.Note ?? "半边应验证通过");
        Assert.Equal(HalfCandidate.LiftCornerDrop, half.Candidate);
        Assert.Equal("交换 + 拆柱 + 跌落", half.OperationText);
        Assert.Equal(0, half.Target.PrimaryColumn);
        Assert.Equal(2, half.Target.Columns.Count);
        Assert.Equal(0, half.Target.Columns[0]);
        Assert.Equal(1, half.Target.Columns[1]);

        Assert.Equal(4, half.Steps.Count);
        Assert.Equal(AngleWorkStepKind.PreState, half.Steps[0].Kind);
        Assert.Equal(AngleWorkStepKind.Exchange, half.Steps[1].Kind);
        Assert.Equal(AngleWorkStepKind.Support, half.Steps[2].Kind);
        Assert.Equal(AngleWorkStepKind.Target, half.Steps[^1].Kind);
        Assert.False(
            half.Steps.Any(step => step.Kind == AngleWorkStepKind.Cut),
            "v4 半边里不能再有 Cut 步");

        // ① 逆提升：两列都抬一格，变成角工序能做的 S 列。
        ShapeDocument lifted = half.Steps[0].Shape!;
        Assert.Equal("cT..TcPT", ShapeSignature.ColumnText(lifted, 0));
        Assert.Equal("TTT.Tccc", ShapeSignature.ColumnText(lifted, 1));
        Assert.True(
            PillarSCriterion.IsPillarColumn(lifted, 0) &&
            PillarSCriterion.IsPillarColumn(lifted, 1),
            "逆提升后两列都必须属于 S");
        Assert.False(
            AngleWorkOrderBuilder.P1Violation(lifted, out _, out _),
            "逆提升后的前态必须满足 P1（每颗晶体坐在材料上）");
        Assert.Equal(2, half.LiftNotes.Count);

        // ② 两张角卡产物（目标列 + 左右柱，归一化 Q1）
        Assert.Equal(2, half.CornerProducts.Count);
        Assert.Equal(2, half.CornerOrders.Count);
        Assert.True(half.CornerOrders[0].IsComplete);
        Assert.True(half.CornerOrders[1].IsComplete);
        Assert.Equal("cT..TcPT", ShapeSignature.ColumnText(half.CornerProducts[0], 0));
        Assert.Equal("TTT.Tccc", ShapeSignature.ColumnText(half.CornerProducts[1], 0));

        // ③ 交换：两根支撑柱各来自一张角卡，中间两列是逆提升后的两列。
        ShapeDocument exchanged = half.ExchangedShape!;
        Assert.Equal("cT..TcPT", ShapeSignature.ColumnText(exchanged, 1));
        Assert.Equal("TTT.Tccc", ShapeSignature.ColumnText(exchanged, 2));
        Assert.True(IsPillarOnly(exchanged, 0), "交换后第 1 象限应是支撑柱");
        Assert.True(IsPillarOnly(exchanged, 3), "交换后第 4 象限应是支撑柱");

        // ④ 拆柱：只清空这两根柱。
        ShapeDocument stripped = half.StrippedShape!;
        Assert.True(IsEmptyColumn(stripped, 0) && IsEmptyColumn(stripped, 3));
        Assert.Equal("cT..TcPT", ShapeSignature.ColumnText(stripped, 1));
        Assert.Equal("TTT.Tccc", ShapeSignature.ColumnText(stripped, 2));

        // ⑤ 跌落：一次真实 Collapse，两列逐格回到目标。
        ShapeDocument preSettle = half.PreSettleShape!;
        ShapeDocument settled = ShapePhysics.Collapse(preSettle).Document!;

        Assert.True(
            ShapeOps.Same(settled, half.Steps[^1].Shape),
            "Target 步必须等于真实 Collapse 的落定结果");
        Assert.Equal("cT.T.cPT", ShapeSignature.ColumnText(settled, 0));
        Assert.Equal("TTTT.ccc", ShapeSignature.ColumnText(settled, 1));
        Assert.True(
            ShapeOps.Same(
                half.Steps[^1].Shape,
                ShapeOps.Project(target, 0, 2)),
            "半边目标必须逐格等于目标的那两列");

        Assert.True(
            half.Limitations.Any(line => line.Contains("v4", StringComparison.Ordinal)),
            "限制说明应写明这是 v4 半边工序");
    }

    /// <summary>回归 2–5：同族的四个目标半边都必须通过 v4。</summary>
    public static void HalfSideV4CoversRegressionFamily()
    {
        var cases = new (string U, string V)[]
        {
            ("cT.T..cPT", "TTTT..ccc"),
            ("cT..T.cPT", "TTTTT.ccc"),
            ("cT.T.cPPT", "TTTT.cccc"),
            ("T.cT.T.cPT", "PPTTTT.ccc")
        };

        foreach ((string u, string v) in cases)
        {
            HalfWorkOrder half = AssertV4Pass(u, v);

            Assert.Equal(2, half.LiftNotes.Count);
            Assert.Equal(2, half.CornerProducts.Count);
            Assert.Equal(
                ShapeSignature.ColumnText(half.Steps[^1].Shape!, 0),
                u.TrimEnd('.'));
            Assert.Equal(
                ShapeSignature.ColumnText(half.Steps[^1].Shape!, 1),
                v.TrimEnd('.'));
        }
    }

    /// <summary>
    /// 用户反例 A（前态，真实 Cut 后得到目标半边）：v4 必须复现同一个半边。
    /// </summary>
    public static void HalfSideV4ReproducesCounterexampleA()
    {
        const int layers = 13;

        ShapeDocument preState = Parse(
            "crcrCu--:CucrCu--:CucrCu--:--crCu--:crcrCu--:crCuCu--:CuCuCu--:" +
            "crCuCu--:crcrcr--:CuCu----:crcr----:P-cr----:Cucr----",
            layers,
            4);

        CutResult cut = ShapePhysics.Cut(preState);
        Assert.True(cut.Success, cut.Error ?? "反例 A 的真实 Cut 应成功");

        HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(cut.West!, 0, layers);

        Assert.True(half is not null, "反例 A 的目标半边应能被 v4 复现");
        Assert.Equal(HalfCandidate.LiftCornerDrop, half!.Candidate);
        Assert.True(
            ShapeOps.Same(
                half.Steps[^1].Shape,
                ShapeOps.Project(cut.West!, 0, 2)),
            "反例 A 的 v4 产物应逐格等于真实 Cut 出来的半边");
        Assert.Equal(
            "cTT.ccT.T.cPT",
            ShapeSignature.ColumnText(half.Steps[^1].Shape!, 0));
        Assert.Equal(
            "cccccTTTT.ccc",
            ShapeSignature.ColumnText(half.Steps[^1].Shape!, 1));
    }

    /// <summary>
    /// 用户反例 B（抬升块上方的晶下间隙不在针环下方）：v4 必须复现同一个半边。
    /// </summary>
    public static void HalfSideV4ReproducesCounterexampleB()
    {
        const int layers = 12;

        ShapeDocument preState = Parse(
            "crCuCu--:CuCuCu--:crCuCu--:crcrcr--:CuCuCu--:crcrCu--:P-crCu--:" +
            "CucrCu--:CuCuCu--:Cucrcr--:P-Cu----:Cucr----",
            layers,
            4);

        CutResult cut = ShapePhysics.Cut(preState);
        Assert.True(cut.Success, cut.Error ?? "反例 B 的真实 Cut 应成功");

        HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(cut.West!, 0, layers);

        Assert.True(half is not null, "反例 B 的目标半边应能被 v4 复现");
        Assert.Equal(HalfCandidate.LiftCornerDrop, half!.Candidate);
        Assert.True(
            ShapeOps.Same(
                half.Steps[^1].Shape,
                ShapeOps.Project(cut.West!, 0, 2)),
            "反例 B 的 v4 产物应逐格等于真实 Cut 出来的半边");
        Assert.Equal(
            "cT.T.cPTTTPT",
            ShapeSignature.ColumnText(half.Steps[^1].Shape!, 0));
        Assert.Equal(
            "TTTT.cccTT.c",
            ShapeSignature.ColumnText(half.Steps[^1].Shape!, 1));
    }

    /// <summary>
    /// 第 6 条：五项 ✔、S ✘，但两列拼起来拆柱跌落 ≠ 目标 ⇒ NotProducible
    /// （不是「未覆盖」，也不回退碎晶 / 候选）。
    /// </summary>
    public static void HalfSideV4RejectsSixthRegression()
    {
        const int layers = 10;

        ShapeDocument target = FromColumns(
            4,
            layers,
            (0, "cT.T.cT.T.c"),
            (1, "TTTTTTTTTT"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            layers,
            preferCrystalBreak: false);

        Assert.False(order.IsComplete, "cT.T.cT.T.c 配实心邻列应判不可制作");
        Assert.True(order.Note is not null, "不可制作必须写 Note");
        Assert.True(
            order.Note!.StartsWith(
                AngleWorkOrderBuilder.NotProduciblePrefix,
                StringComparison.Ordinal),
            "失败必须是 NotProducible，不能是未覆盖：" + order.Note);
        Assert.True(
            order.Note.Contains("拆柱跌落与目标不符", StringComparison.Ordinal),
            "失败原因应指出是第 ⑤ 步跌落不符：" + order.Note);
        Assert.True(order.Half is null, "不可制作时不应伪造完整的 Half");

        Assert.True(
            AngleWorkOrderBuilder.BuildHalf(target, 0, layers) is null,
            "BuildHalf 对不可制作的半边应返回 null");
    }

    /// <summary>非法列（五项 ✘）不走半边：仍然报不可制作。</summary>
    public static void HalfSideV4DoesNotClaimIllegalColumns()
    {
        const int layers = 8;

        ShapeDocument target = FromColumns(4, layers, (0, ".c"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            layers,
            preferCrystalBreak: false);

        Assert.False(order.IsComplete);
        Assert.True(order.Half is null, "非法列不能伪造 Half");
        Assert.True(
            order.Note!.StartsWith(
                AngleWorkOrderBuilder.NotProduciblePrefix,
                StringComparison.Ordinal),
            "非法列应报不可制作：" + order.Note);
    }

    /// <summary>半边从第 3 象限起（环形）：v4 的产物要对齐到原始列。</summary>
    public static void HalfSideV4AlignsRightHalf()
    {
        const int cap = 8;

        ShapeDocument target = FromColumns(
            4,
            cap,
            (2, "cT.T.cPT"),
            (3, "TTTT.ccc"));

        HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(target, 2, cap);

        Assert.True(half is not null, "第 3、4 象限的半边应可做");
        Assert.Equal(2, half!.Target.Columns[0]);
        Assert.Equal(3, half.Target.Columns[1]);
        Assert.True(
            ShapeOps.Same(
                half.Steps[^1].Shape,
                ShapeOps.Project(target, 2, 2)),
            "右半边应逐格等于目标第 3、4 象限");

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            2,
            cap,
            preferCrystalBreak: false);

        Assert.True(order.IsComplete, order.Note ?? "第 3 象限的角应可做");
        Assert.Equal(HalfCandidate.LiftCornerDrop, order.Half!.Candidate);
    }

    /// <summary>
    /// 单列目标（第二列恒为空）就是「229 个非 S 合法角」的形态：
    /// v4 判据要求两列一起跌落，单列没有第二列支撑 ⇒ NotProducible。
    /// 这条把「单列目标不在 v4 的适用域」写成可执行规格。
    /// </summary>
    public static void HalfSideV4SingleColumnTargetIsNotProducible()
    {
        const int layers = 8;

        ShapeDocument target = FromColumns(4, layers, (0, "cT.T.cPT"));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            layers,
            preferCrystalBreak: false);

        Assert.False(order.IsComplete, "单列目标不能报半边可得");
        Assert.True(
            order.Note!.StartsWith(
                AngleWorkOrderBuilder.NotProduciblePrefix,
                StringComparison.Ordinal),
            "单列目标应报 NotProducible：" + order.Note);
    }

    /// <summary>① 逆提升后的前态：两列合法、S ✔、P1 ✔，且逐格方向正确。</summary>
    public static void HalfSideV4PreStateIsLiftedAndP1Clean()
    {
        HalfWorkOrder half = AssertV4Pass("cT.T.cPPT", "TTTT.cccc");

        ShapeDocument lifted = half.Steps[0].Shape!;

        Assert.True(
            ShapeSignature.AllColumnsLegal(lifted, out int bad),
            $"逆提升后的前态第 {bad + 1} 象限不合法");
        Assert.False(
            AngleWorkOrderBuilder.P1Violation(lifted, out _, out _),
            "逆提升后的前态应满足 P1");
        Assert.True(
            ShapeOps.IsStable(half.CornerProducts[0]) &&
            ShapeOps.IsStable(half.CornerProducts[1]),
            "两张角产物（目标列 + 左右柱）应各自稳定");
        Assert.Equal(
            "cT..TcPPT",
            ShapeSignature.ColumnText(lifted, 0),
            "u 的普通格应被抬到晶体正下方");
    }

    /// <summary>③ 交换产物必须恰好是「柱 + u + v + 柱」：两根柱各来自一张角卡。</summary>
    public static void HalfSideV4ExchangeHasTwoSupportPillars()
    {
        HalfWorkOrder half = AssertV4Pass("cT..T.cPT", "TTTTT.ccc");
        ShapeDocument exchanged = half.ExchangedShape!;

        Assert.True(IsPillarOnly(exchanged, 0));
        Assert.True(IsPillarOnly(exchanged, 3));

        Assert.Equal(
            ShapeSignature.ColumnText(half.Steps[0].Shape!, 0),
            ShapeSignature.ColumnText(exchanged, 1),
            "交换后第 2 象限应是逆提升后的 u");
        Assert.Equal(
            ShapeSignature.ColumnText(half.Steps[0].Shape!, 1),
            ShapeSignature.ColumnText(exchanged, 2),
            "交换后第 3 象限应是逆提升后的 v");
        Assert.Equal(2, half.RemovedPillarColumns.Count);
        Assert.Equal(0, half.RemovedPillarColumns[0]);
        Assert.Equal(3, half.RemovedPillarColumns[1]);
    }

    /// <summary>n = 6 的 k 列半边仍走论文候选 1（Q 自身）。</summary>
    public static void HalfSideAlgo8SelfBuildsStableHalf()
    {
        const int parts = 6;
        const int layers = 8;

        ShapeDocument target = FromColumns(
            parts,
            layers,
            (0, "cTcccccc"),
            (1, "cTcccccc"),
            (2, "cTcccccc"));

        HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(target, 0, layers);

        Assert.True(half is not null, "n = 6 的三列 S 半边应可得");
        Assert.Equal(HalfCandidate.Algo8Self, half!.Candidate);
        Assert.Equal(2, half.Steps.Count);
        Assert.Equal(AngleWorkStepKind.PreState, half.Steps[0].Kind);
        Assert.Equal(AngleWorkStepKind.Target, half.Steps[^1].Kind);
        Assert.True(ShapeOps.Same(half.Steps[0].Shape, half.Steps[^1].Shape));
    }

    /// <summary>n = 6 / 8 的 k 列半边：列号按环顺序，产物逐格等于目标那一段。</summary>
    public static void HalfSideGeneralizesToKColumns()
    {
        var cases = new (int Parts, int Layers, int Start)[]
        {
            (6, 8, 2),
            (8, 8, 5)
        };

        foreach ((int parts, int layers, int start) in cases)
        {
            int width = parts / 2;
            var columns = new List<(int Column, string Text)>();

            for (int index = 0; index < width; index++)
            {
                columns.Add(((start + index) % parts, "cTcccccc"));
            }

            ShapeDocument target = FromColumns(parts, layers, columns.ToArray());

            HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(
                target,
                start,
                layers);

            Assert.True(half is not null, $"n = {parts} 的 {width} 列半边应可得");
            Assert.Equal(start, half!.Target.PrimaryColumn);
            Assert.Equal(width, half.Target.Columns.Count);

            for (int index = 0; index < width; index++)
            {
                Assert.Equal(
                    (start + index) % parts,
                    half.Target.Columns[index],
                    $"n = {parts} 的半边应覆盖从第 {start + 1} 象限起的连续 {width} 列");
            }

            Assert.True(
                ShapeOps.Same(
                    half.Steps[^1].Shape,
                    ShapeOps.Project(target, start, width)),
                $"n = {parts} 的半边目标应逐格等于目标那一段");
            Assert.Equal(0, half.Rank);
            Assert.True(half.TargetColumnsHaveSubRecipe);
        }
    }

    /// <summary>k 列半边不做假阳性：不稳定 / 有非法列时返回 null。</summary>
    public static void HalfSideKColumnsDoesNotFakeUnstableHalf()
    {
        const int parts = 6;
        const int layers = 3;

        ShapeDocument unstable = FromColumns(
            parts,
            layers,
            (0, ".T"),
            (1, "T"));

        Assert.True(ShapeSignature.AllColumnsLegal(unstable, out _));
        Assert.False(ShapeOps.IsStable(unstable));
        Assert.True(
            AngleWorkOrderBuilder.BuildHalf(unstable, 0, layers) is null,
            "不稳定的 k 列半边不能报可得");

        ShapeDocument illegal = FromColumns(
            parts,
            8,
            (0, "cT.T.cPT"),
            (1, ".c"));

        Assert.False(ShapeSignature.IsLegalColumn(illegal, 1));
        Assert.True(
            AngleWorkOrderBuilder.BuildHalf(illegal, 0, 8) is null,
            "半边里有非法列时必须拒绝");
    }

    /// <summary>v4 的失败一律 NotProducible（不写「未覆盖」、不伪造 Target 步）。</summary>
    public static void HalfSideV4NotesAreNotUncovered()
    {
        var cases = new (string U, string V)[]
        {
            ("cT.T.cT.T.c", "TTTTTTTTTT"),
            ("cT.T.cT.T.c", "TTTT.ccc"),
            ("cT.T.cT.T.c", "........")
        };

        foreach ((string u, string v) in cases)
        {
            int layers = Math.Max(u.Length, v.Length);

            ShapeDocument target = FromColumns(
                4,
                layers,
                (0, u),
                (1, v));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                layers,
                preferCrystalBreak: false);

            if (order.IsComplete)
            {
                continue;
            }

            Assert.True(
                order.Note!.StartsWith(
                    AngleWorkOrderBuilder.NotProduciblePrefix,
                    StringComparison.Ordinal),
                $"{u}|{v} 的失败必须写 NotProducible：" + order.Note);
            Assert.False(
                order.Note.Contains(
                    AngleWorkOrderBuilder.UncoveredPrefix,
                    StringComparison.Ordinal),
                "不应再出现「未覆盖」：" + order.Note);
            Assert.False(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Target),
                "未完成的工序不应伪造 Target 步");
        }
    }

    /// <summary>
    /// 五步链的中间态顺序：前态（逆提升）→ 交换 → 拆柱 → 目标，
    /// 与命令行 <c>--half</c> 的打印顺序一致。
    /// </summary>
    public static void HalfSideV4WireFormatMatchesCliSequence()
    {
        HalfWorkOrder half = AssertV4Pass("cT.T.cPT", "TTTT.ccc");

        Assert.True(half.LiftedHalf is not null, "① 逆提升前态必须有快照");
        Assert.True(half.ExchangedShape is not null, "③ 交换必须有快照");
        Assert.True(half.StrippedShape is not null, "④ 拆柱必须有快照");
        Assert.True(half.PreSettleShape is not null, "⑤ 跌落前必须有快照");
        Assert.True(half.CornerProducts.Count == 2, "② 两张角卡产物必须都在");

        // ④ 拆柱的产物必须能由 ⑤ 一次 Collapse 得到目标（不再补第二次结算）。
        ShapeDocument settled = ShapePhysics.Collapse(half.PreSettleShape!).Document!;

        Assert.True(ShapeOps.Same(settled, half.Steps[^1].Shape));
        Assert.True(
            ShapeOps.IsStable(settled),
            "⑤ 之后的一次落定必须稳定（不会再多落一次）");
    }

    /// <summary>跑一例 v4 并断言通过，返回 Half。</summary>
    private static HalfWorkOrder AssertV4Pass(
        string u,
        string v)
    {
        int layers = Math.Max(u.Length, v.Length);

        ShapeDocument target = FromColumns(
            4,
            layers,
            (0, u),
            (1, v));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            target,
            0,
            layers,
            preferCrystalBreak: false);

        Assert.True(order.IsComplete, order.Note ?? (u + "|" + v + " 应可做"));
        Assert.True(order.Half is not null, "应带 Half 字段");

        HalfWorkOrder half = order.Half!;

        Assert.True(half.IsComplete, half.Note ?? "半边应完整");
        Assert.Equal(HalfCandidate.LiftCornerDrop, half.Candidate);
        Assert.True(
            ShapeOps.Same(
                half.Steps[^1].Shape,
                ShapeOps.Project(target, 0, 2)),
            u + "|" + v + " 的半边目标应逐格等于目标那两列");

        return half;
    }

    /// <summary>某列是否只有普通格（支撑柱）。</summary>
    private static bool IsPillarOnly(
        ShapeDocument shape,
        int column)
    {
        bool any = false;

        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            CellType type = shape[layer, column].Type;

            if (type == CellType.Empty)
            {
                continue;
            }

            if (type != CellType.Shape)
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    /// <summary>某列是否整列为空。</summary>
    private static bool IsEmptyColumn(
        ShapeDocument shape,
        int column)
    {
        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            if (shape[layer, column].Type != CellType.Empty)
            {
                return false;
            }
        }

        return true;
    }

    #endregion


    #region 回放工具

    /// <summary>
    /// 用真实 Cut 回放订单，断言目标列与目标一致；Cut 步就是「切割 + 落定」的最终态。
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

        ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

        Assert.True(
            ShapeOps.Same(replayed, cutStep),
            "Cut 步应等于真实 Cut 出来的西半边（已落定）");

        Assert.False(
            order.Steps.Any(step => step.Kind == AngleWorkStepKind.Collapse),
            "步骤链不应再出现 Collapse 中间态");

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
                AngleTestHelpers.TargetColumnMatches(order, target),
                "末步应得到目标角");
    }

    /// <summary>
    /// 单轮构造 G 的逐格回放：<c>PreState → Cut（碎晶并落定）→ Target</c>。
    ///
    /// <para>
    /// Cut 步已经是「切割 + 落定」的最终态，所以直接与真实
    /// <c>ShapePhysics.Cut</c> 的西半边逐格比较，不再核对单独的 Collapse 步。
    /// </para>
    /// </summary>
    private static void ReplaySingleRoundG(
        AngleWorkOrder order,
        ShapeDocument target,
        int column)
    {
        ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

        Assert.True(ShapeOps.IsStable(preState), "单轮 G 的前态应稳定");
        Assert.True(
            ShapeSignature.AllColumnsLegal(preState, out int badColumn),
            $"单轮 G 的前态第 {badColumn + 1} 象限不是合法列");

        ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

        CutResult cut = ShapePhysics.Cut(rotated);

        Assert.True(cut.Success, cut.Error ?? "单轮 G 的切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

        ShapeDocument cutStep = Step(order, AngleWorkStepKind.Cut).Shape!;

        Assert.True(
            ShapeOps.Same(replayed, cutStep),
            "单轮 G 的 Cut 步应等于真实 Cut 出来的西半边（已落定）");

        Assert.True(ShapeOps.IsStable(cutStep), "切割并落定后的中间态必须稳定");

        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(cutStep, column),
            "单轮 G 切割并落定后目标列应逐格等于目标");

        Assert.False(
            order.Steps.Any(step => step.Kind == AngleWorkStepKind.Collapse),
            "步骤链不应再出现 Collapse 中间态");

        Assert.True(
            AngleTestHelpers.TargetColumnMatches(order, target),
            "单轮 G 的末步应得到目标角");
    }

    /// <summary>
    /// 算法 4（§11 通用实现）步骤链的回放：PreState →（Cut → PreState?）…→ Support → Target。
    ///
    /// <para>逐条核对 §11.4 的六条验收：</para>
    /// <list type="number">
    /// <item>每条前态的每一列都过五项条件；</item>
    /// <item>每条前态的目标列 ≠ 目标列；</item>
    /// <item>前态与每个中间态都稳定；</item>
    /// <item>每一刀都等于真实 <see cref="ShapePhysics.Cut"/> 的西半边（含 <c>Rotation</c>；
    /// 单发 G 的 Cut 步允许是「下落前」快照，再 Collapse 一次核对）；</item>
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

            /*
             * Cut 步现在是「切割 + 落定」的最终态：直接与真实
             * ShapePhysics.Cut 的西半边逐格比较，不再接受「下落前」快照。
             */
            Assert.True(
                ShapeOps.Same(replayed, step.Shape),
                $"第 {cuts + 1} 刀应逐格等于真实 Cut 出来的西半边（已落定）");

            Assert.True(ShapeOps.IsStable(step.Shape!), "切割并落定后的中间态必须稳定");

            preState = step;
            cuts++;
        }

        Assert.True(cuts >= 1, "算法 4 至少有一刀");

        AngleWorkStep? support = order.Steps.LastOrDefault(step =>
            step.Kind == AngleWorkStepKind.Support && step.Shape is not null);

        ShapeDocument handover = support?.Shape
            ?? order.Steps.Last(step =>
                step.Kind == AngleWorkStepKind.Cut && step.Shape is not null).Shape!;

        Assert.True(ShapeOps.IsStable(handover), "构造 C 交接后的图形应稳定");
        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(handover, column),
            "构造 C 交接后的目标列应逐格等于目标");

        Assert.Equal(
            ShapeSignature.ColumnText(target, column),
            ShapeSignature.ColumnText(handover, column),
            "最后一刀/下落之后的目标列应逐格等于目标");

        Assert.True(
            AngleTestHelpers.TargetColumnMatches(order, target),
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
    /// 多轮 G 的逐轮回放：前态稳定 → 真实 Cut → 西半边转回后与 Cut 步一致，
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

        Assert.True(
            ShapeOps.IsStable(preState),
            $"第 {round + 1} 轮前态应稳定");

        ShapeDocument rotated = ShapeOps.Rotate(preState, order.Rotation);

        CutResult cut = ShapePhysics.Cut(rotated);

        Assert.True(cut.Success, cut.Error ?? "切割失败");

        ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

        Assert.True(
            ShapeOps.Same(replayed, cutStep),
            $"第 {round + 1} 轮 Cut 步应等于真实 Cut 出来的西半边（已落定）");

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

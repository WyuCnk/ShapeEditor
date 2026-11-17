using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 任务书（docs/prompt-for-main-agent.md，2026-11-17 角工序第三轮）的四项改动：
///
/// <list type="number">
/// <item>改动 1：<c>t[1]=='T'</c> 的路由短路——不再尝试构造 H；</item>
/// <item>改动 2：「N 连续针段」族的四个回归前态逐格通过；</item>
/// <item>改动 3：策略优先级（H → G/D → 算法 6 四步 → 多刀）落到分档里；</item>
/// <item>改动 4：仍产生 <see cref="AngleWorkStepKind.WeakDrop"/> 时，工序标题必须写全 §0.9.2 的五项。</item>
/// </list>
///
/// <para>
/// 另外把残余 47 列（docs/residual-47-weakdrop.md §1）也拉一遍：其中有多少列
/// 已经改走一次顶针、剩下哪些仍保留弱落，测试里如实打印，不做特判。
/// </para>
/// </summary>
internal static class PinFamilyRegressionTests
{
    private const int Parts = 4;

    /// <summary>
    /// 改动 2 的四个回归前态（docs §7.1 / 任务书表格）。
    ///
    /// <para>
    /// 每个前态都必须：A / B / E / N 逐格等于表格；四列过五项条件；
    /// 初始前态稳定、每颗原生晶体下方都是普通格（P1）；
    /// 真实 <see cref="ShapePhysics.Pin"/> 一次回到目标列；步骤链里没有弱落。
    /// </para>
    /// </summary>
    public static void FourRegressionsReplay()
    {
        var cases = new (string Target, string A, string B, string E, string N)[]
        {
            (".TT.c", ".TTc.", "cTT..", "ccccc", "TPPT."),
            (".T.T.c", ".T.Tc.", "cTTT..", "cccccc", "TPPPT."),
            (".TTT.c", ".TTTc.", "cTTT..", "cccccc", "TPPPT."),
            (".TT.Tc", ".TTTc.", "cTTT..", "cccccc", "TPPTT.")
        };

        foreach ((string text, string a, string b, string e, string n) in cases)
        {
            ShapeDocument target = FromColumns(Parts, text.Length, (0, text));
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length);

            Assert.True(order.IsComplete, text + " 应可制作：" + (order.Note ?? "?"));
            Assert.True(order.Note is null, text + " 不应有未覆盖说明");
            Assert.Equal(AngleConstruction.H, order.Construction, text + " 应走构造 H");
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm, text + " 应属于算法 6 档");
            Assert.Equal(1, order.Rank, text + " 应只有一次顶针");
            Assert.Equal(
                0,
                order.Steps.Count(step => step.Kind == AngleWorkStepKind.WeakDrop),
                text + " 的一次顶针前态不应再有弱落步");

            ShapeDocument preState = Step(order, AngleWorkStepKind.PreState).Shape!;

            Assert.Equal(a, ShapeSignature.ColumnText(preState, 0), text + " 的 A");
            Assert.Equal(b, ShapeSignature.ColumnText(preState, 1), text + " 的 B");
            Assert.Equal(e, ShapeSignature.ColumnText(preState, 2), text + " 的 E");
            Assert.Equal(n, ShapeSignature.ColumnText(preState, 3), text + " 的 N");

            Assert.True(ShapeOps.IsStable(preState), text + " 的前态应稳定");
            Assert.True(
                ShapeSignature.AllColumnsLegal(preState, out int badColumn),
                text + $" 的第 {badColumn + 1} 象限不过五项条件");
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                text + " 的前态不应含非法列");

            // P1：初始前态里每颗原生晶体都要坐在材料上。
            for (int part = 0; part < preState.PartCount; part++)
            {
                string column = ShapeSignature.ColumnText(preState, part);

                for (int layer = 1; layer < column.Length; layer++)
                {
                    Assert.False(
                        column[layer] == ShapeSignature.CrystalCode &&
                        column[layer - 1] == ShapeSignature.EmptyCode,
                        text + $" 的第 {part + 1} 象限第 {layer + 1} 层晶体下方为空（P1）");
                }
            }

            PhysicsResult pinned = ShapePhysics.Pin(preState);

            Assert.True(pinned.Success, text + " 的真实 Pin 失败：" + pinned.Error);
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(pinned.Document!, 0),
                text + " 顶针后应逐格等于目标");

            AngleWorkStep pinStep = Step(order, AngleWorkStepKind.Pin);

            Assert.True(
                ShapeOps.Same(pinned.Document, pinStep.Shape),
                text + " 的 Pin 步快照应等于真实顶针结果");

            Assert.True(
                ShapeOps.Same(
                    target,
                    Step(order, AngleWorkStepKind.Target).Shape),
                text + " 的末步应逐格等于目标");

            Console.WriteLine(
                "  PINFAMILY " + text + " A=" + a + " B=" + b +
                " E=" + e + " N=" + n + " weak=0");
        }
    }

    /// <summary>
    /// 改动 1：<c>t[1]=='T'</c> 的列<b>不尝试</b>构造 H。
    ///
    /// <para>
    /// 用 <see cref="AngleWorkOrderDiagnostics"/> 的计数器断言：3–6 层全部
    /// <c>t[1]=='T'</c> 的 S 列在生成过程中，「第一层是普通格的构造 H 尝试」次数为 0；
    /// 同时先跑一条非 T 列证明计数器确实会记录构造 H 尝试（避免永远为 0 的假通过）。
    /// </para>
    /// </summary>
    public static void RouteShortCircuitSkipsConstructH()
    {
        AngleWorkOrderDiagnostics.Reset();

        // 反证：`.T..T..c` 的第一层是空，构造 H 应被真的尝试过。
        _ = AngleWorkOrderBuilder.Build(
            FromColumns(Parts, 8, (0, ".T..T..c")),
            0,
            8);

        Assert.True(
            AngleWorkOrderDiagnostics.ConstructHAttempts > 0,
            "计数器应记录到非 T 列的构造 H 尝试");
        Assert.Equal(
            0,
            AngleWorkOrderDiagnostics.ConstructHOnSolidFirstLayer,
            "非 T 列不应被记成「第一层是普通格的构造 H 尝试」");

        AngleWorkOrderDiagnostics.Reset();

        int solidFirstLayerColumns = 0;

        for (int layers = 3; layers <= 6; layers++)
        {
            int combinations = (int)Math.Pow(4, layers);
            var buffer = new char[layers];

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = (remaining % 4) switch
                    {
                        0 => ShapeSignature.EmptyCode,
                        1 => ShapeSignature.SolidCode,
                        2 => ShapeSignature.PinCode,
                        _ => ShapeSignature.CrystalCode
                    };

                    remaining /= 4;
                }

                string text = new(buffer);

                if (text[0] != ShapeSignature.SolidCode ||
                    !ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                solidFirstLayerColumns++;

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                    FromColumns(Parts, layers, (0, text)),
                    0,
                    layers);

                Assert.True(
                    order.IsComplete,
                    text + " 应仍可制作：" + (order.Note ?? "?"));

                // 「构造 H 失败后回退」的日志不得再出现在 t[1]=='T' 的列上。
                string log = (order.Note ?? string.Empty) + "\n" +
                             string.Join("\n", order.Limitations);

                Assert.False(
                    log.Contains("构造 H（一次顶针）也未通过", StringComparison.Ordinal),
                    text + " 不应出现「构造 H 失败后回退」的日志：" + log);

                Assert.False(
                    log.Contains("§0.11 九步构造 H：", StringComparison.Ordinal),
                    text + " 不应尝试 §0.11 九步构造 H：" + log);
            }
        }

        Assert.True(
            solidFirstLayerColumns > 0,
            "3–6 层应存在 t[1]=='T' 的 S 列");

        Console.WriteLine(
            "  SHORTCIRCUIT t1T=" + solidFirstLayerColumns +
            " constructHAttempts=" + AngleWorkOrderDiagnostics.ConstructHAttempts +
            " solidFirstLayerAttempts=" +
            AngleWorkOrderDiagnostics.ConstructHOnSolidFirstLayer);

        Assert.Equal(
            0,
            AngleWorkOrderDiagnostics.ConstructHOnSolidFirstLayer,
            "t[1]=='T' 的列不得再尝试构造 H（改动 1 的短路）");
    }

    /// <summary>
    /// 改动 4：仍产生弱落时，<b>每一项</b>的标题都要写全 §0.9.2 的五项：
    /// PreState（含辅助列 S′）→ Support（撤托柱）→ Collapse（哪一格、落在哪一层的
    /// 什么材料之上、跨几层）→ Support（装回托柱）→ PreState（拆掉 S′）。
    /// </summary>
    public static void WeakDropStepsWriteFiveParts()
    {
        foreach ((string text, int cap) in new[] { ("T.T.c", 5), ("T..T.c", 6) })
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns(Parts, cap, (0, text)),
                0,
                cap);

            Assert.True(order.IsComplete, text + " 应可制作：" + (order.Note ?? "?"));
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm, text);

            AngleWorkStep[] weakDrops = order.Steps
                .Where(step => step.Kind == AngleWorkStepKind.WeakDrop)
                .ToArray();

            Assert.True(weakDrops.Length > 0, text + " 应至少有一处弱落");

            foreach (AngleWorkStep step in weakDrops)
            {
                string title = step.Title;

                string[] required =
                {
                    "弱落（构造 E）",
                    "[PreState]",
                    "辅助列 S′",
                    "连续接地",
                    "为针",
                    "其余普通格",
                    "[Support] 撤掉原托柱",
                    "[Collapse]",
                    "落到第",
                    "落在",
                    "之上，跨",
                    "[Support] 装回满高托柱",
                    "[PreState] 拆掉 S′"
                };

                foreach (string part in required)
                {
                    Assert.True(
                        title.Contains(part, StringComparison.Ordinal),
                        text + " 的弱落标题缺少「" + part + "」：" + title);
                }

                // 必须写明「跨了几层」的具体层数。
                Assert.True(
                    title.Contains(" 层；", StringComparison.Ordinal) ||
                    title.Contains(" 层。", StringComparison.Ordinal),
                    text + " 的弱落标题没有写跨几层：" + title);

                Console.WriteLine("  WEAKDROP " + text + "：" + title);
            }
        }
    }

    /// <summary>
    /// 残余 47 列（docs/residual-47-weakdrop.md §1）的回归：
    /// 逐列生成工序、打印 D / F 与是否仍含弱落；能一次顶针的列不得再出现弱落。
    ///
    /// <para>
    /// 目前先要求 ≥ 45 列改走一次顶针（4 个文档样例全部在内），
    /// 剩下的列如实保留弱落并在输出里列出——不为数字去掉正确步骤。
    /// </para>
    /// </summary>
    public static void Residual47ColumnsUseOnePin()
    {
        var weak = new List<string>();
        int complete = 0;

        foreach (string text in Residual47)
        {
            bool d = PillarSCriterion.BottomResourceD(text);
            bool f = PillarSCriterion.RecursiveTerminalF(text);

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns(Parts, text.Length, (0, text)),
                0,
                text.Length);

            Assert.True(order.IsComplete, text + " 应可制作：" + (order.Note ?? "?"));

            complete++;

            if (order.Steps.Any(step => step.Kind == AngleWorkStepKind.WeakDrop))
            {
                weak.Add(text);
            }

            Console.WriteLine(
                "  RESIDUAL " + text.PadRight(8) +
                " D=" + d + " F=" + f +
                " weak=" + (weak.Contains(text) ? 1 : 0) +
                " algorithm=" + order.Algorithm +
                " construction=" + order.Construction);
        }

        Console.WriteLine(
            "  RESIDUAL47 complete=" + complete +
            " onePin=" + (complete - weak.Count) +
            " weak=" + weak.Count +
            "（" + string.Join(" ", weak) + "）");

        Assert.Equal(47, complete, "47 列都应完成");
        Assert.True(
            complete - weak.Count >= 45,
            "至少 45 列应改走一次顶针，实际 " + (complete - weak.Count));
    }

    /// <summary>docs/residual-47-weakdrop.md §1 的 47 列。</summary>
    private static readonly string[] Residual47 =
    {
        ".TT.c",
        ".T.T.c", ".TT..c", ".TT.c.", ".TT.cc", ".TT.cP", ".TT.cT", ".TT.Tc",
        ".TTT.c",
        ".T..T.c", ".T.T..c", ".T.T.c.", ".T.T.cc", ".T.T.cP", ".T.T.cT",
        ".TT...c", ".TT..c.", ".TT..cc", ".TT..cP", ".TT..cT", ".TT..Tc",
        ".TT.c..", ".TT.c.T", ".TT.cc.", ".TT.ccc", ".TT.ccP", ".TT.ccT",
        ".TT.cP.", ".TT.cPP", ".TT.cPT", ".TT.cT.", ".TT.cTc", ".TT.cTP",
        ".TT.cTT", ".TT.T.c", ".TT.Tc.", ".TT.Tcc", ".TT.TcP", ".TT.TcT",
        ".TT.TTc", ".TTT..c", ".TTT.c.", ".TTT.cc", ".TTT.cP", ".TTT.cT",
        ".TTT.Tc", ".TTTT.c"
    };

    private static ShapeDocument FromColumns(
        int parts,
        int layers,
        params (int Column, string Text)[] columns)
    {
        var document = new ShapeDocument(layers, parts);

        foreach ((int column, string text) in columns)
        {
            for (int layer = 0; layer < layers; layer++)
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

    private static AngleWorkStep Step(
        AngleWorkOrder order,
        AngleWorkStepKind kind)
    {
        AngleWorkStep? step = order.Steps.FirstOrDefault(
            candidate => candidate.Kind == kind && candidate.Shape is not null);

        Assert.True(step is not null, kind + " 步缺失");
        return step!;
    }
}

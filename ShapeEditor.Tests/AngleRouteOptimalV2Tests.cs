using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// O-v2 碎晶路线（docs/angle-route-optimal-v2.md）的回归：
/// 四个基线前态逐格复现、T.T.c 的两刀与五步交换支撑柱、收尾统一形态、指标 M1–M7。
/// </summary>
internal static class AngleRouteOptimalV2Tests
{
    private const int Parts = 4;

    /// <summary>
    /// O-v2 §2.1 / §7 的五个基线：A / S / F 逐格等于文档表，刀数符合，末步目标列逐格等于目标。
    ///
    /// <para>
    /// <c>T.T.c</c> 与 <c>T.T.T.c</c> <b>没有一刀解</b>（§7）：它们的临时晶体是<b>多个不连续块</b>，
    /// 必须按 §三「K = 临时晶体块集合，while K ≠ ∅ 逐块切」——所以刀数 = 块数。
    /// </para>
    /// </summary>
    public static void FourCrystalBreakBaselines()
    {
        var baselines = new (string Target, string A, string S, string F, int Cuts)[]
        {
            ("T.c", "cTc", "TPT", "c..", 1),
            ("T...c", "cccTc", "TTTPT", "TTc..", 1),
            (".TT.cTT.c", "cTcTcTcTc", "TTTPTTTPT", "cTcTTTc..", 1),
            ("T.T.c", "cTcTc", "TTTPT", "TTc..", 2),
            ("T.T.T.c", "cTcTcTc", "TTTTTPT", "TTTTc..", 3)
        };

        foreach ((string text, string a, string s, string f, int cuts) in baselines)
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns((0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.True(order.IsComplete, text + " 应完成：" + (order.Note ?? "?"));
            Assert.Equal(
                AngleConstruction.CrystalBreak,
                order.Construction,
                text + " 应走 O-v2 碎晶路线");
            Assert.Equal(
                AngleRoute.CrystalBreak,
                order.EffectiveRoute,
                text + " 的路线应标为碎晶");

            AngleWorkStep preState = order.Steps.First(
                step => step.Kind == AngleWorkStepKind.PreState &&
                        step.Shape is not null);

            ShapeDocument expected = FromColumns(
                (0, a),
                (1, s),
                (2, string.Empty),
                (3, f));

            Assert.True(
                ShapeOps.Same(preState.Shape, expected),
                text + " 的前态四列应逐格等于 O-v2 表：期望 " +
                Describe(expected) + "，实际 " + Describe(preState.Shape!));

            Assert.Equal(
                cuts,
                order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut),
                text + " 的刀数");

            Assert.Equal(
                text,
                order.TargetColumnText,
                text + " 的末步目标列应逐格等于目标");
        }

        // T.T.c：第一刀后必须是 cTT.c，第二刀 + 换柱后才是 T.T.c。
        {
            const string text = "T.T.c";

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns((0, text)),
                0,
                text.Length);

            AngleWorkStep firstCut = order.Steps.First(
                step => step.Kind == AngleWorkStepKind.Cut);

            Assert.Equal(
                "cTT.c",
                ShapeSignature.ColumnText(firstCut.Shape!, 0),
                "T.T.c 的第一刀后应为 cTT.c");

            Assert.Equal(
                "T.T.c",
                order.TargetColumnText,
                "T.T.c 的完整工序后应为 T.T.c");
        }
    }

    /// <summary>
    /// §0.12.2 五步交换支撑柱：<b>每一刀之前</b>一轮，每一步都有完整四列短代码，
    /// 并用真实 <see cref="ShapeOps.Rotate"/> / <see cref="ShapePhysics.Cut"/> 回放。
    ///
    /// <para>
    /// 工序结构固定为 <c>刀 ①②③④ 刀 ⑤ ①②③④ 刀 ⑤ 收尾</c>：
    /// 前三步（旋转 / 装新柱 / 接管）与第四步（拆旧柱并装下一刀保险丝）在刀前，
    /// 第五步（逆旋转）在刀后。多刀时这个结构按刀数重复，<b>每一轮都必须完整</b>。
    /// </para>
    /// </summary>
    public static void SupportExchangeHasFiveSteps()
    {
        var exporter = new ShortCodeExporter();

        // T.T.c：两刀、一轮交换；② / ④ 与用户给出的参考序列逐字一致。
        AssertExchangeRounds(
            "T.T.c",
            2,
            exporter,
            "CucrCu--:P-CuCu--:CuCuCu--:Cu--P---:CucrCu--",
            "Cucrcr--:P-Cu----:CuCu----:Cu------:Cucr----");

        // T.T.T.c：三刀、两轮交换（§7：没有一刀解，必须逐块切）。
        AssertExchangeRounds("T.T.T.c", 3, exporter, null, null);

        /*
         * 用户给出的 T.T.c 完整序列（docs §0.12.2 的短代码示例 / 任务书第三节）：
         *
         *   第 1 刀：crCu--Cu:CuCu--Cu:crCu--cr:CuP-----:crCu----  → cTT.c
         *   换柱① ：crCu--Cu:CuCu--P-:CuCu--Cu:--P---Cu:crCu--Cu
         *   旋转  ：CucrCu--:P-CuCu--:CuCuCu--:Cu--P---:CucrCu--
         *   换柱② ：Cucrcr--:P-Cu----:CuCu----:Cu------:Cucr----
         *   第 2 刀：CuCu----:P-------:CuCu----:Cu------:Cucr----  → T.T.c
         *
         * 实现按 §0.12.2 的文字顺序（① 先旋转、② 再装新柱），所以：
         *   ② = 用户「旋转」、④ = 用户「换柱②」、第 2 刀 = 用户「第 2 刀」；
         *   用户「换柱①」= ② 逆旋转回原象限（同一块材料，只是显示坐标系不同）。
         */
        {
            const string text = "T.T.c";

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns((0, text)),
                0,
                text.Length);

            List<int> cuts = order.Steps
                .Select((step, index) => (step, index))
                .Where(entry => entry.step.Kind == AngleWorkStepKind.Cut)
                .Select(entry => entry.index)
                .ToList();

            AngleWorkStep preState = order.Steps.First(
                step => step.Kind == AngleWorkStepKind.PreState);

            Assert.Equal(
                "crCu--Cu:CuCu--Cu:crCu--cr:CuP-----:crCu----",
                exporter.Export(preState.Shape!, omitTopEmptyLayers: false),
                "T.T.c 的「第 1 刀」前态应与用户序列逐字一致");

            Assert.Equal(
                "crCu--Cu:CuCu--P-:CuCu--Cu:--P---Cu:crCu--Cu",
                exporter.Export(
                    ShapeOps.Rotate(order.Steps[cuts[0] + 2].Shape!, -1),
                    omitTopEmptyLayers: false),
                "T.T.c 的「换柱①」= ② 逆旋转回原象限，应与用户序列逐字一致");

            Assert.Equal(
                "CuCu----:P-------:CuCu----:Cu------:Cucr----",
                exporter.Export(order.Steps[cuts[1]].Shape!, omitTopEmptyLayers: false),
                "T.T.c 的「第 2 刀」应与用户序列逐字一致");
        }
    }

    /// <summary>
    /// 核对 <paramref name="text"/> 的多刀工序：刀数、每轮五步交换、短代码、真实回放。
    /// </summary>
    private static void AssertExchangeRounds(
        string text,
        int expectedCuts,
        ShortCodeExporter exporter,
        string? expectedNewSupport,
        string? expectedFuse)
    {
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            FromColumns((0, text)),
            0,
            text.Length);

        Assert.True(order.IsComplete, text + " 应完成：" + (order.Note ?? "?"));

        List<int> cuts = order.Steps
            .Select((step, index) => (step, index))
            .Where(entry => entry.step.Kind == AngleWorkStepKind.Cut)
            .Select(entry => entry.index)
            .ToList();

        Assert.Equal(expectedCuts, cuts.Count, text + " 的刀数");

        /*
         * 换柱轮次 = 第 2 刀起每一刀一轮：
         *   刀 ①②③④ 刀 ⑤ ①②③④ 刀 ⑤
         * 所以第 j 刀（j ≥ 2）之前正好四步，之后正好第五步（逆旋转）。
         */
        for (int cutIndex = 1; cutIndex < cuts.Count; cutIndex++)
        {
            int cut = cuts[cutIndex];

            // ①②③④ 在本刀之前。
            List<AngleWorkStep> exchange = order.Steps
                .Skip(cut - 4)
                .Take(4)
                .ToList();

            Assert.Equal(4, exchange.Count, text + $" 第 {cutIndex + 1} 刀前必须是四步（①②③④）");

            Assert.Equal(AngleWorkStepKind.Exchange, exchange[0].Kind, "① 应是 Exchange");
            Assert.Equal(AngleWorkStepKind.Support, exchange[1].Kind, "② 应是 Support");
            Assert.Equal(AngleWorkStepKind.Exchange, exchange[2].Kind, "③ 应是 Exchange");
            Assert.Equal(AngleWorkStepKind.Support, exchange[3].Kind, "④ 应是 Support");

            Assert.True(
                exchange.All(step => step.Shape is not null),
                text + " 交换步的每一步都必须有图形");

            foreach (AngleWorkStep step in exchange)
            {
                string code = exporter.Export(
                    step.Shape!,
                    omitTopEmptyLayers: false);

                int expectedCells = step.Shape!.LayerCount * 4 * 2;

                Assert.Equal(
                    expectedCells + step.Shape.LayerCount - 1,
                    code.Length,
                    text + " 交换步必须带完整四列短代码：" + code);
            }

            // ① 真实旋转：本步 = 上一份状态顺转 1 个象限。
            Assert.True(
                ShapeOps.Same(
                    exchange[0].Shape,
                    ShapeOps.Rotate(order.Steps[cut - 5].Shape!, 1)),
                text + $" 第 {cutIndex + 1} 刀前的 ① 应是真实 Rotate(上一状态, +1)");

            // ③ 与 ② 是同一份图形（新柱接管，图形不变）。
            Assert.True(
                ShapeOps.Same(exchange[1].Shape, exchange[2].Shape),
                text + $" 第 {cutIndex + 1} 刀前的 ③（接管）应与 ② 同一份图形");

            if (expectedNewSupport is not null)
            {
                Assert.Equal(
                    expectedNewSupport,
                    exporter.Export(exchange[1].Shape!, omitTopEmptyLayers: false),
                    text + " 的 ②（装新支撑柱）应对应参考序列的「旋转」");
            }

            if (expectedFuse is not null)
            {
                Assert.Equal(
                    expectedFuse,
                    exporter.Export(exchange[3].Shape!, omitTopEmptyLayers: false),
                    text + " 的 ④（拆旧柱 / 装保险丝）应对应参考序列的「再换」");
            }

            // 刀前第四步的最后一份图形就是刀口输入：真实 Cut 必须等于工序里的 Cut 步。
            CutResult replay = ShapePhysics.Cut(
                ShapeOps.Rotate(exchange[3].Shape!, order.Rotation));

            Assert.True(
                replay.Success && replay.West is not null,
                text + $" 第 {cutIndex + 1} 刀的真实 Cut 失败");

            Assert.True(
                ShapeOps.Same(
                    ShapeOps.Rotate(replay.West!, -order.Rotation),
                    order.Steps[cut].Shape),
                text + $" 第 {cutIndex + 1} 刀的 Cut 步应等于真实物理回放");

            // ⑤ 逆旋转在本刀之后：= 本刀结果逆旋转 1 个象限。
            AngleWorkStep inverse = order.Steps[cut + 1];

            Assert.Equal(
                AngleWorkStepKind.Exchange,
                inverse.Kind,
                text + $" 第 {cutIndex + 1} 刀的第五步应是 Exchange（逆旋转）");

            Assert.True(
                ShapeOps.Same(
                    inverse.Shape,
                    ShapeOps.Rotate(order.Steps[cut].Shape!, -1)),
                text + $" 第 {cutIndex + 1} 刀的第五步应是真实 Rotate(刀结果, -1)");
        }

        // 末步目标列逐格等于目标。
        Assert.Equal(
            text,
            ShapeSignature.ColumnText(
                order.Steps.Last(step =>
                    step.Kind == AngleWorkStepKind.Target).Shape!,
                order.Column),
            text + " 的末步目标列应逐格等于目标");

        // 多刀碎晶：弱落必须是 0（M1 权重最高，docs/angle-route-optimal-v2.md §7.1）。
        Assert.Equal(0, order.Metrics.WeakDrops, text + " 的多刀碎晶不应出现弱落");
    }

    /// <summary>
    /// 碎晶前态的 P1 与抬升上界（docs §0.12.3 / 任务书第五节）：
    /// <list type="number">
    /// <item>P1：每颗<b>原生晶体</b>正下方<b>相邻</b>必须是普通格；</item>
    /// <item>抬升上界：A 是紧列，每枚普通格上方都还有材料 ⇒ 它已经「抬到不能再抬」；</item>
    /// <item>临时晶体块只在最高晶体以下，且每段都至少一格。</item>
    /// </list>
    /// 扫描 3–6 层全部 S 列中走碎晶路线的列。
    /// </summary>
    public static void CrystalBreakPreStateSatisfiesP1AndMaximalLift()
    {
        var alphabet = new[]
        {
            ShapeSignature.EmptyCode,
            ShapeSignature.SolidCode,
            ShapeSignature.PinCode,
            ShapeSignature.CrystalCode
        };

        int prestates = 0;
        int p1Violations = 0;
        int liftViolations = 0;
        var cases = new List<string>();

        for (int layers = 3; layers <= 6; layers++)
        {
            int combinations = (int)Math.Pow(alphabet.Length, layers);
            var buffer = new char[layers];

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = alphabet[remaining % alphabet.Length];
                    remaining /= alphabet.Length;
                }

                string text = new(buffer);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                    FromColumns((0, text)),
                    0,
                    layers);

                if (!order.IsComplete ||
                    order.Construction != AngleConstruction.CrystalBreak)
                {
                    continue;
                }

                AngleWorkStep? initial = order.Steps.FirstOrDefault(
                    step => step.Kind == AngleWorkStepKind.PreState &&
                            step.Shape is not null);

                if (initial?.Shape is null)
                {
                    continue;
                }

                prestates++;

                string a = ShapeSignature.ColumnText(initial.Shape, 0);

                // ① P1：原生晶体正下方相邻不能是空、也不能是**临时晶体**
                //（原生晶体段内部的上下相叠是目标本身要求的，允许）。
                for (int layer = 1; layer < layers; layer++)
                {
                    if (text[layer] != ShapeSignature.CrystalCode)
                    {
                        continue;
                    }

                    char below = a[layer - 1];

                    bool sitsOnNativeCrystal =
                        below == ShapeSignature.CrystalCode &&
                        text[layer - 1] == ShapeSignature.CrystalCode;

                    if (a[layer] != ShapeSignature.CrystalCode ||
                        (below != ShapeSignature.SolidCode && !sitsOnNativeCrystal))
                    {
                        p1Violations++;

                        if (cases.Count < 10)
                        {
                            cases.Add("P1 " + text + " A=" + a);
                        }
                    }
                }

                // ② 抬升上界：A 必须是紧列（无洞）⇒ 每枚普通格上方仍有材料。
                if (!ShapeSignature.IsTightColumn(a))
                {
                    liftViolations++;

                    if (cases.Count < 10)
                    {
                        cases.Add("LIFT " + text + " A=" + a);
                    }

                    continue;
                }

                for (int layer = 0; layer + 1 < layers; layer++)
                {
                    if (a[layer] != ShapeSignature.SolidCode)
                    {
                        continue;
                    }

                    if (a[layer + 1] == ShapeSignature.EmptyCode &&
                        a.TrimEnd(ShapeSignature.EmptyCode).Length > layer + 1)
                    {
                        liftViolations++;

                        if (cases.Count < 10)
                        {
                            cases.Add("LIFT " + text + " A=" + a);
                        }

                        break;
                    }
                }
            }
        }

        Console.WriteLine(
            $"  OV2P1 prestates={prestates} p1Violations={p1Violations} " +
            $"liftViolations={liftViolations}");

        Assert.True(prestates > 0, "应至少扫到一条碎晶前态");
        Assert.Equal(0, p1Violations, "碎晶前态的 P1 违规必须为 0：" + string.Join(" | ", cases));
        Assert.Equal(0, liftViolations, "碎晶前态的抬升不是「尽量高」：" + string.Join(" | ", cases));
    }

    /// <summary>
    /// §5 / §0.12.1 收尾统一形态：Target = 左柱 + 目标列 + 右柱（n = 4），
    /// n = 2 与半边工序保持原行为。
    /// </summary>
    public static void TargetShapeIsUnified()
    {
        foreach (string text in new[] { "T.c", "T...c", ".TT.cTT.c", "T.T.c", "T.T.T.c", ".T.c" })
        {
            int cap = text.Length;

            ShapeDocument target = FromColumns((0, text));

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap);

            Assert.True(order.IsComplete, text + " 应完成");

            AngleWorkStep targetStep = order.Steps.Last(
                step => step.Kind == AngleWorkStepKind.Target);

            Assert.True(targetStep.Shape is not null, text + " 缺 Target 步");

            ShapeDocument shape = targetStep.Shape!;
            int height = ShapeSignature.ColumnText(target, 0).TrimEnd('.').Length;

            int left = 3;
            int right = 1;

            Assert.Equal(
                ShapeSignature.ColumnText(target, 0),
                ShapeSignature.ColumnText(shape, 0),
                text + " 的 Target 目标列应逐格等于目标");

            for (int layer = 0; layer < height; layer++)
            {
                Assert.Equal(
                    CellType.Shape,
                    shape[layer, left].Type,
                    text + $" 的左柱第 {layer + 1} 层应为普通格");
                Assert.Equal(
                    CellType.Shape,
                    shape[layer, right].Type,
                    text + $" 的右柱第 {layer + 1} 层应为普通格");
            }

            Assert.True(
                ShapeSignature.IsTightColumn(shape, left) &&
                ShapeSignature.IsTightColumn(shape, right),
                text + " 的两侧柱必须是连续接地的紧列");

            Assert.True(
                ShapeSignature.CheckColumn(shape, left).IsLegal &&
                ShapeSignature.CheckColumn(shape, right).IsLegal,
                text + " 的两侧柱必须过五项条件");

            Assert.True(ShapeOps.IsStable(shape), text + " 的统一收尾形态应稳定");
        }

        // n = 2：两侧是同一列，不能放两根柱；保持原行为（Target 不加柱）。
        {
            var twoParts = new ShapeDocument(3, 2);
            twoParts[0, 0] = ShapeCell.Shape('C', 'u');
            twoParts[2, 0] = ShapeCell.Crystal('r');

            AngleWorkOrder narrow = AngleWorkOrderBuilder.Build(twoParts, 0, 3);

            if (narrow.IsComplete)
            {
                Assert.Equal(
                    "T.c",
                    narrow.TargetColumnText,
                    "n = 2 的 Target 目标列应保持原行为");
            }
        }

        // 半边工序不受影响：Target 仍是两列半边目标（不加两侧柱）。
        {
            const string text = "cT.T.cPT";

            AngleWorkOrder half = AngleWorkOrderBuilder.Build(
                FromColumns((0, text)),
                0,
                8);

            Assert.Equal(AngleConstruction.Half, half.Construction);
            Assert.True(half.Half is not null, "应走半边工序");
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(
                    half.Steps.Last(step =>
                        step.Kind == AngleWorkStepKind.Target).Shape!,
                    0),
                "半边工序的 Target 目标列应保持原行为");
        }
    }

    /// <summary>
    /// 指标 M1–M7：碎晶路线的 M1（弱落）必须为 0，且不差于旧路线。
    ///
    /// <para>
    /// <c>T.T.T.c</c> 是 §7 的多刀样例：旧路线是算法 6 + 2 次弱落，新路线是 3 刀碎晶，
    /// <b>M1 从 2 降到 0</b>（M3 从 0 升到 3，按 docs/work-order-metrics.md 的字典序是净胜）。
    /// </para>
    /// </summary>
    public static void MetricsRecordedAndNotWorse()
    {
        foreach (string text in new[] { "T.c", "T...c", ".TT.cTT.c", "T.T.c", "T.T.T.c" })
        {
            ShapeDocument target = FromColumns((0, text));

            AngleWorkOrder current = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length);

            AngleWorkOrder old = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length,
                preferCrystalBreak: false);

            Assert.Equal(0, current.Metrics.WeakDrops, text + " 的 M1 应为 0");
            Assert.Equal(2, current.Metrics.AuxiliaryColumns, text + " 的 M4 应为 2");
            Assert.True(current.Metrics.Materials > 0, text + " 的 M7 应记录材料数");
            Assert.True(current.Metrics.Steps > 0, text + " 的 M6 应记录步数");

            if (old.IsComplete)
            {
                Assert.True(
                    current.Metrics.WeakDrops <= old.Metrics.WeakDrops,
                    text + " 的新路线 M1 不得比旧路线差");
            }

            if (text == "T.T.T.c")
            {
                // §7：旧路线 = 算法 6 + 2 次弱落；新路线 = 3 刀碎晶 + 0 弱落。
                Assert.Equal(3, current.Metrics.Cuts, "T.T.T.c 应是三刀");
                Assert.Equal(0, current.Metrics.WeakDrops, "T.T.T.c 的弱落应为 0");
                Assert.Equal(AngleRoute.CrystalBreak, current.EffectiveRoute, "T.T.T.c 应走碎晶");
                Assert.Equal(2, old.Metrics.WeakDrops, "T.T.T.c 的旧路线应是 2 次弱落");
            }

            Console.WriteLine(
                $"  OV2 {text} route={current.EffectiveRoute} " +
                $"bytes=" + string.Join(
                    "/",
                    current.Metrics.ToVector()) +
                " old=" + string.Join(
                    "/",
                    old.Metrics.ToVector()));
        }
    }

    private static ShapeDocument FromColumns(params (int Column, string Text)[] columns)
    {
        int layers = columns.Max(entry => entry.Text.Length);

        var document = new ShapeDocument(layers, Parts);

        foreach ((int column, string text) in columns)
        {
            for (int layer = 0; layer < layers; layer++)
            {
                char code = layer < text.Length
                    ? text[layer]
                    : ShapeSignature.EmptyCode;

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

    private static string Describe(ShapeDocument document)
    {
        return string.Join(
            " | ",
            Enumerable.Range(0, document.PartCount)
                .Select(part => ShapeSignature.ColumnText(document, part)));
    }
}

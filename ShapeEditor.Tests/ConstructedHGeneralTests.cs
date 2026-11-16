using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 构造 H 的 §13.6 / §14 通用前态验收：
/// P1 审计、§R9 三个样例、≤8 层全部构造 H 的语料。
/// </summary>
internal static class ConstructedHGeneralTests
{
    private const int Parts = 4;

    private static readonly char[] Alphabet =
    {
        ShapeSignature.EmptyCode,
        ShapeSignature.SolidCode,
        ShapeSignature.PinCode,
        ShapeSignature.CrystalCode
    };

    /// <summary>
    /// P1 审计（docs §1 / §R1，口径按 §16.5 N3 放宽）：
    /// <b>只检查工序的第一张图（初始前态）</b>——初始前态里
    /// 「原生晶体正下方是空」的例数必须为 0；多刀之间的中间态不做 P1 检查。
    ///
    /// <para>
    /// 口径：≤6 层的全部 S 列，每份工序只看第一条
    /// <see cref="AngleWorkStepKind.PreState"/>；任何一列出现 <c>c</c> 上方紧贴
    /// <c>.</c>（含临时晶体）都算违规。构造 H 的临时晶体只填「下方有格」的空段，
    /// 所以它们也不会悬空。
    /// </para>
    /// </summary>
    public static void P1AuditIsClean()
    {
        int prestates = 0;
        int violations = 0;
        int allPrestates = 0;
        int allViolations = 0;
        var violationCases = new List<string>();

        for (int layers = 3; layers <= 6; layers++)
        {
            int combinations = 1 << (2 * layers);
            var buffer = new char[layers];

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = Alphabet[remaining % Alphabet.Length];
                    remaining /= Alphabet.Length;
                }

                string text = new(buffer);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                ShapeDocument target = BuildColumn(text);
                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                // N3：只取第一张图（初始前态），多刀中间态跳过。
                AngleWorkStep? initial = order.Steps.FirstOrDefault(
                    step => step.Kind == AngleWorkStepKind.PreState &&
                            step.Shape is not null);

                if (initial?.Shape is null)
                {
                    continue;
                }

                allPrestates++;

                if (order.Construction == AngleConstruction.H)
                {
                    prestates++;
                }

                for (int part = 0; part < initial.Shape.PartCount; part++)
                {
                    string column = ShapeSignature.ColumnText(initial.Shape, part);

                    for (int layer = 1; layer < column.Length; layer++)
                    {
                        if (column[layer] != ShapeSignature.CrystalCode ||
                            column[layer - 1] != ShapeSignature.EmptyCode)
                        {
                            continue;
                        }

                        allViolations++;

                        if (order.Construction == AngleConstruction.H)
                        {
                            violations++;
                            violationCases.Add(
                                text + "（" + order.Construction + "/" + order.Algorithm +
                                "）：第 " + (part + 1) + " 象限 " + column +
                                "；" + string.Join("；", order.Limitations));
                        }
                    }
                }
            }
        }

        Console.WriteLine(
            $"  P1AUDIT prestates={prestates} violations={violations}（只查初始前态）");
        Console.WriteLine(
            $"  P1ALL prestates={allPrestates} violations={allViolations}" +
            "（只查初始前态；碎晶路线允许目标本身要求的晶下间隙）");

        if (violationCases.Count > 0)
        {
            Console.WriteLine(
                "  P1VIOLATIONS " + string.Join(" | ", violationCases.Take(10)));
        }

        Assert.True(prestates > 0, "P1 审计应至少扫到一条前态");
        Assert.Equal(0, violations, "P1 违规必须为 0：" + string.Join(" | ", violationCases.Take(10)));

        // 特别断言 §R8 的 T...cT.T.c：每颗原生晶体下方都必须非空。
        AssertP1Case("T...cT.T.c");
        AssertP1Case("T..cT.T.c");
        AssertP1Case("T.T.cccTc");
    }

    /// <summary>
    /// §R9 的三个实测样例：前态每颗晶体下方非空（P1），一次 Pin 后目标列逐格等于目标；
    /// 同时打印 A / B / E / N，便于与 docs 的样例对照。
    /// </summary>
    public static void ThreePaperSamplesUseGeneralH()
    {
        var cases = new (string Text, string ExpectedA)[]
        {
            ("T...cT.T.c", "..TcTccTc."),
            ("T..cT.T.c", ".TcTccTc."),
            // ③ 的 docs 前态是半破路线（TccTcccTc）；这里走等价的顶针路线，
            // 只要 P1 成立且 Pin 后逐格等于目标即可。
            ("T.T.cccTc", ".TTcccTc.")
        };

        foreach ((string text, string expectedA) in cases)
        {
            ShapeDocument target = BuildColumn(text);
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, text.Length);

            Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");
            Assert.Equal(AngleConstruction.H, order.Construction, text);
            Assert.Equal(AngleAlgorithm.Algorithm6, order.Algorithm, text);
            Assert.Equal(1, order.Rank, text + " 应只有一次顶针");

            ShapeDocument preState = Step(
                order,
                AngleWorkStepKind.PreState).Shape!;

            string a = ShapeSignature.ColumnText(preState, order.Column);

            Assert.Equal(expectedA, a, text + " 的 A 应与 §13.6 的逐格推导一致");

            for (int layer = 1; layer < a.Length; layer++)
            {
                Assert.False(
                    a[layer] == ShapeSignature.CrystalCode &&
                    a[layer - 1] == ShapeSignature.EmptyCode,
                    text + $" 的 A 第 {layer + 1} 层晶体下方为空（P1 不满足）");
            }

            // 四列逐列打印（B / E / N 是辅助列，顺序为 +1 / +2 / -1）。
            int bColumn = (order.Column + 1) % Parts;
            int eColumn = (order.Column + 2) % Parts;
            int nColumn = (order.Column + 3) % Parts;

            Console.WriteLine(
                $"  R9 {text} A={a} " +
                $"B={ShapeSignature.ColumnText(preState, bColumn)} " +
                $"E={ShapeSignature.ColumnText(preState, eColumn)} " +
                $"N={ShapeSignature.ColumnText(preState, nColumn)}");

            PhysicsResult pin = ShapePhysics.Pin(preState);

            Assert.True(pin.Success, text + " 的真实 Pin 失败：" + pin.Error);
            Assert.Equal(
                text,
                ShapeSignature.ColumnText(pin.Document!, order.Column),
                text + " 的 Pin 后目标列应逐格等于目标");

            AngleWorkStep pinStep = Step(order, AngleWorkStepKind.Pin);

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(pinStep.Shape!, order.Column),
                text + " 的 Pin 步快照应逐格等于目标");

            AngleWorkStep targetStep = Step(order, AngleWorkStepKind.Target);

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(targetStep.Shape!, order.Column),
                text + " 的末步应逐格等于目标");
        }
    }

    /// <summary>
    /// ≤8 层全部「实际走构造 H」的语料：逐条核验 P1、前态每列合法、
    /// 末步逐格等于目标，并打印每层数量。
    /// </summary>
    public static void HCorpusUnderEightLayers()
    {
        int total = 0;

        for (int layers = 3; layers <= 8; layers++)
        {
            int combinations = 1 << (2 * layers);
            var buffer = new char[layers];
            int h = 0;
            var problems = new List<string>();

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = Alphabet[remaining % Alphabet.Length];
                    remaining /= Alphabet.Length;
                }

                string text = new(buffer);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                ShapeDocument target = BuildColumn(text);
                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                if (!order.IsComplete ||
                    order.Construction != AngleConstruction.H)
                {
                    continue;
                }

                h++;

                ShapeDocument preState = Step(
                    order,
                    AngleWorkStepKind.PreState).Shape!;

                if (order.HasIllegalPreState(out int preIndex, out int badColumn))
                {
                    problems.Add(
                        text + $"：第 {preIndex + 1} 条前态第 {badColumn + 1} 象限非法");
                    continue;
                }

                for (int part = 0; part < preState.PartCount; part++)
                {
                    string column = ShapeSignature.ColumnText(preState, part);

                    for (int layer = 1; layer < column.Length; layer++)
                    {
                        if (column[layer] == ShapeSignature.CrystalCode &&
                            column[layer - 1] == ShapeSignature.EmptyCode)
                        {
                            problems.Add(
                                text + $"：第 {part + 1} 象限 P1 违规 " + column);
                        }
                    }
                }

                AngleWorkStep? targetStep = order.Steps
                    .LastOrDefault(step => step.Kind == AngleWorkStepKind.Target);

                if (targetStep?.Shape is null ||
                    !ShapeOps.Same(targetStep.Shape, target))
                {
                    problems.Add(text + "：末步没有逐格等于目标");
                }
            }

            total += h;
            Console.WriteLine($"  HCORPUS layers={layers} h={h} problems={problems.Count}");

            Assert.True(
                problems.Count == 0,
                $"{layers} 层构造 H 语料出现问题：" +
                string.Join(" | ", problems.Take(10)));
        }

        Console.WriteLine($"  HCORPUS total={total}");

        Assert.True(total > 0, "≤8 层应有构造 H 的语料");
    }

    private static void AssertP1Case(string text)
    {
        ShapeDocument target = BuildColumn(text);
        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, text.Length);

        ShapeDocument preState = Step(
            order,
            AngleWorkStepKind.PreState).Shape!;

        string a = ShapeSignature.ColumnText(preState, order.Column);
        var gaps = new List<int>();

        for (int layer = 1; layer < a.Length; layer++)
        {
            if (a[layer] == ShapeSignature.CrystalCode &&
                a[layer - 1] == ShapeSignature.EmptyCode)
            {
                gaps.Add(layer + 1);
            }
        }

        Assert.Equal(0, gaps.Count, text + " 的 P1 违规层：" + string.Join(",", gaps));
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

    private static ShapeDocument BuildColumn(string text)
    {
        var document = new ShapeDocument(text.Length, Parts);

        for (int layer = 0; layer < text.Length; layer++)
        {
            document[layer, 0] = text[layer] switch
            {
                'c' => ShapeCell.Crystal('r'),
                'P' => ShapeCell.Pin(),
                'T' => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }

        return document;
    }
}

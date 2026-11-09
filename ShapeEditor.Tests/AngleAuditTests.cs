using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// n = 4 的穷举审计（docs §17）：长度 3–6、字母表 <c>. T P c</c>，
/// 共 5440 列（64 + 256 + 1024 + 4096）；docs 里的「5460」把长度 1–2 的
/// 20 列也算进去了，而 2126 / 2125 / 19 三个数字只对应长度 3–6。
///
/// <para>
/// 审计口径与 docs §17.1 一致：
/// </para>
/// <code>
/// 合法列            2126
///   其中 S          2125
///   其中合法非 S        1    ← cT.T.c
/// S 列工序生成成功   2125    （目标：19 个失败 → 0）
/// </code>
/// <para>
/// 除计数外还逐项核对：S 列不得出现假阳性（前态每列合法、前态目标列 ≠ 目标、
/// 末步逐格等于目标），单列耗时 &lt; 50 ms。
/// </para>
/// </summary>
internal static class AngleAuditTests
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
    /// 全量审计：5460 列。失败时把每一列的失败原因写进断言消息。
    /// </summary>
    public static void ExhaustiveFourQuadrantAudit()
    {
        // 预热 JIT：首调用包含 JIT，不能算进「单列耗时」。
        foreach (string warmup in new[]
                 {
                     "T.c", ".T.c", "T.T.c", ".T.cT", "T.T..c",
                     "P.T..c", "P..T.c", ".T.cTc", ".TT..c", "cT.T.c",
                     ".T..cc", "..T..c", "TT..Tc", "T..cTc", "T...Tc",
                     "T..TcT", "T..TcP", "T.T.Tc", "T..Tc.", "T..Tcc"
                 })
        {
            _ = AngleWorkOrderBuilder.Build(
                BuildColumn(warmup),
                0,
                warmup.Length);
        }

        var failures = new List<string>();
        var falsePositives = new List<string>();
        var legalNonS = new List<string>();

        int legal = 0;
        int s = 0;
        int success = 0;
        int total = 0;
        long worstTicks = 0;
        string worstColumn = string.Empty;

        for (int layers = 3; layers <= 6; layers++)
        {
            int combinations = 1;

            for (int index = 0; index < layers; index++)
            {
                combinations *= Alphabet.Length;
            }

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

                total++;

                ShapeDocument target = BuildColumn(text);

                bool isLegal = ShapeSignature.IsLegalColumn(target, 0);

                AngleWorkOrder order;

                var watch = Stopwatch.StartNew();

                order = AngleWorkOrderBuilder.Build(target, 0, layers);

                watch.Stop();

                if (watch.ElapsedTicks > worstTicks)
                {
                    worstTicks = watch.ElapsedTicks;
                    worstColumn = text;
                }

                if (!isLegal)
                {
                    if (order.IsComplete)
                    {
                        falsePositives.Add(text + "：不合法却报完成");
                    }
                    else if (order.Note is null ||
                             !order.Note.StartsWith(
                                 AngleWorkOrderBuilder.NotProduciblePrefix,
                                 StringComparison.Ordinal))
                    {
                        failures.Add(text + "：不合法但 Note 不是「不可制作」：" + order.Note);
                    }

                    continue;
                }

                legal++;

                if (!PillarSCriterion.IsPillarColumn(target, 0))
                {
                    legalNonS.Add(text);

                    if (order.IsComplete)
                    {
                        falsePositives.Add(text + "：非 S 却报完成");
                    }
                    else if (order.Note is null ||
                             !order.Note.StartsWith(
                                 AngleWorkOrderBuilder.NotProduciblePrefix,
                                 StringComparison.Ordinal))
                    {
                        failures.Add(text + "：合法非 S 应报「不可制作」，实际：" + order.Note);
                    }

                    continue;
                }

                s++;

                if (!order.IsComplete)
                {
                    failures.Add(text + "：" + (order.Note ?? "未给出原因"));
                    continue;
                }

                success++;

                // 假阳性闸门：前态每列合法、前态目标列 ≠ 目标、末步等于目标。
                if (order.HasIllegalPreState(out int preStateIndex, out int badColumn))
                {
                    falsePositives.Add(
                        text + $"：第 {preStateIndex + 1} 条前态第 {badColumn + 1} 象限不合法");
                }

                foreach (AngleWorkStep step in order.Steps)
                {
                    if (step.Kind != AngleWorkStepKind.PreState ||
                        step.Shape is null)
                    {
                        continue;
                    }

                    if (ShapeSignature.ColumnText(step.Shape, 0) == text)
                    {
                        falsePositives.Add(text + "：前态目标列等于目标（循环）");
                    }
                }

                AngleWorkStep? targetStep = order.Steps
                    .LastOrDefault(step => step.Kind == AngleWorkStepKind.Target);

                if (order.Kind == AngleBuildKind.NeedsCrystalBreak &&
                    (targetStep?.Shape is null ||
                     !ShapeOps.Same(targetStep.Shape, target)))
                {
                    falsePositives.Add(text + "：末步没有逐格等于目标");
                }
            }
        }

        double worstMs = worstTicks * 1000.0 / Stopwatch.Frequency;

        Console.WriteLine(
            $"  AUDIT total={total} legal={legal} s={s} success={success} " +
            $"legalNonS={legalNonS.Count} failures={failures.Count} " +
            $"falsePositives={falsePositives.Count} worst={worstMs:F2}ms");

        if (legalNonS.Count > 0)
        {
            Console.WriteLine("  合法非 S：" + string.Join(" ", legalNonS));
        }

        if (failures.Count > 0)
        {
            Console.WriteLine(
                "  FAILCOLS " + string.Join(
                    " ",
                    failures.Select(line => line.Split('：')[0])));
            Console.WriteLine("  FAILNOTES " + string.Join(" | ", failures));
        }

        if (falsePositives.Count > 0)
        {
            Console.WriteLine("  假阳性列表：" + string.Join(" | ", falsePositives));
        }

        Console.WriteLine(
            $"  最慢单列：{worstColumn} = {worstMs:F2} ms（要求 < 50 ms）");

        Assert.Equal(5440, total, "审计列数应为 5440（长度 3–6 的所有列：64+256+1024+4096）");
        Assert.Equal(2126, legal, "合法列数应为 2126");
        Assert.Equal(2125, s, "S 列数应为 2125");
        Assert.True(
            legalNonS.Count == 1 && legalNonS[0] == "cT.T.c",
            "长度 ≤6 的合法非 S 只有 cT.T.c，实际：" + string.Join(" ", legalNonS));
        Assert.True(
            failures.Count == 0,
            $"S 列生成失败 {failures.Count} 个（目标 0）：" + string.Join(" | ", failures));
        Assert.True(
            falsePositives.Count == 0,
            $"出现 {falsePositives.Count} 个假阳性：" + string.Join(" | ", falsePositives));
        Assert.True(worstMs < 50.0, $"最慢单列 {worstMs:F2} ms，应 < 50 ms");
    }

    /// <summary>按列文本造 4 象限目标（其余列留空）。</summary>
    private static ShapeDocument BuildColumn(
        string text)
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

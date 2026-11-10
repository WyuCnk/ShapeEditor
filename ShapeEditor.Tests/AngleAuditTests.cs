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

    /// <summary>
    /// §17.7 的审计口径：≤6 层的 2125 个 S 列，统计前态空洞落在哪一列。
    ///
    /// <para>
    /// 「有空洞」= <see cref="ShapeSignature.IsTightColumn"/> 为 false（材料之间夹着空层）。
    /// 目标列（被求解的角）允许有洞，但必须带子配方；辅助列一律紧列。
    /// 修复前：仅辅助列有洞 7 个（<c>T.ccc</c> / <c>T.ccc.</c> / <c>T.cccT</c> /
    /// <c>T.cccP</c> / <c>P.T..c</c> / <c>TT.ccc</c> / <c>T.cccc</c>）；
    /// 修复后必须为 0，且 S 生成成功数仍是 2125。
    /// </para>
    /// </summary>
    public static void PreStateTightnessAudit()
    {
        int s = 0;
        int success = 0;
        int steps = 0;
        int tight = 0;
        int onlyTarget = 0;
        int onlyAux = 0;
        int both = 0;
        var auxCases = new List<string>();

        for (int layers = 3; layers <= 6; layers++)
        {
            int combinations = (int)Math.Pow(Alphabet.Length, layers);
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
                ShapeDocument target = BuildColumn(text);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(target, 0))
                {
                    continue;
                }

                s++;

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                if (!order.IsComplete)
                {
                    continue;
                }

                success++;

                foreach (AngleWorkStep step in order.Steps)
                {
                    if (step.Kind != AngleWorkStepKind.PreState || step.Shape is null)
                    {
                        continue;
                    }

                    steps++;

                    bool targetHole = !ShapeSignature.IsTightColumn(step.Shape, order.Column);
                    bool auxHole = false;

                    for (int part = 0; part < step.Shape.PartCount; part++)
                    {
                        if (part == order.Column)
                        {
                            continue;
                        }

                        if (!ShapeSignature.IsTightColumn(step.Shape, part))
                        {
                            auxHole = true;
                            auxCases.Add(
                                text + "：第 " + (part + 1) + " 象限 = " +
                                ShapeSignature.ColumnText(step.Shape, part));
                        }
                    }

                    if (targetHole && auxHole)
                    {
                        both++;
                    }
                    else if (auxHole)
                    {
                        onlyAux++;
                    }
                    else if (targetHole)
                    {
                        onlyTarget++;
                    }
                    else
                    {
                        tight++;
                    }
                }
            }
        }

        Console.WriteLine(
            $"  TIGHT totalS={s} success={success} steps={steps} tight={tight} " +
            $"onlyTarget={onlyTarget} onlyAux={onlyAux} both={both}");

        Assert.Equal(2125, s, "≤6 层的 S 列应为 2125");
        Assert.Equal(2125, success, "S 列生成成功数应仍为 2125");
        Assert.True(
            onlyAux == 0 && both == 0,
            "仅辅助列有洞的个数必须为 0（修复前 7）：" + string.Join(" | ", auxCases));
    }

    /// <summary>
    /// 半边工序（候选 3）的外部审计：7–9 层、4 象限、字母表 <c>. T P c</c>。
    ///
    /// <para>
    /// 只挑「五项 ✔ 且不属于 S」的合法列（≤6 层的审计口径不变，仍要求它们是「不可制作」；
    /// 半边候选只覆盖 <c>s = 最高晶体 = 层数−3</c>、顶部 <c>c-P-T</c>、<c>s−1</c> 是洞、
    /// <c>s−2</c> 是普通格的形态，≤6 层没有命中）。
    /// </para>
    /// <para>每一列都按 docs §6 的七条独立核对：</para>
    /// <list type="number">
    /// <item>完成（IsComplete）必须带 <see cref="HalfWorkOrder"/>，候选为
    /// <see cref="HalfCandidate.TempCrystalFuse"/>，且 <c>Half.IsComplete</c>；</item>
    /// <item>前态每一列过五项条件、全部是紧列（§17.7）、整体稳定；</item>
    /// <item>前态目标列 ≠ 目标列（禁止循环）；</item>
    /// <item>真实 <c>ShapePhysics.Cut</c> 回放后，两列逐格等于半边目标；</item>
    /// <item>末步 Target 逐格等于目标；</item>
    /// <item>未完成时 Note 必须分清「不可制作」与「未覆盖」，且未覆盖不得伪造 Target 步。</item>
    /// </list>
    /// </summary>
    public static void HalfSideAudit()
    {
        int legalNonS = 0;
        int halfComplete = 0;
        int notProducible = 0;
        int uncovered = 0;
        var halfColumns = new List<string>();
        var problems = new List<string>();

        for (int layers = 7; layers <= 9; layers++)
        {
            int combinations = (int)Math.Pow(Alphabet.Length, layers);
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

                // 先用列字符串判掉不合法 / 属于 S 的列（S 走现有算法，见别的审计）。
                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                legalNonS++;

                ShapeDocument target = BuildColumn(text);
                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                if (!order.IsComplete)
                {
                    if (order.Half is { IsComplete: true })
                    {
                        problems.Add(text + "：未完成却带已完成的 Half");
                    }

                    if (order.Note is null)
                    {
                        problems.Add(text + "：未完成但没有 Note");
                    }
                    else if (order.Note.StartsWith(
                                 AngleWorkOrderBuilder.NotProduciblePrefix,
                                 StringComparison.Ordinal))
                    {
                        notProducible++;
                    }
                    else if (order.Note.StartsWith(
                                 AngleWorkOrderBuilder.UncoveredPrefix,
                                 StringComparison.Ordinal))
                    {
                        uncovered++;

                        if (order.Steps.Any(step =>
                                step.Kind == AngleWorkStepKind.Target))
                        {
                            problems.Add(text + "：未覆盖却伪造了 Target 步");
                        }
                    }
                    else
                    {
                        problems.Add(text + "：Note 前缀既不是不可制作也不是未覆盖：" +
                                      order.Note);
                    }

                    continue;
                }

                halfComplete++;
                halfColumns.Add(text);

                HalfWorkOrder? half = order.Half;

                if (half is null || !half.IsComplete)
                {
                    problems.Add(text + "：完成却没有已完成的 Half");
                    continue;
                }

                if (half.Candidate != HalfCandidate.TempCrystalFuse)
                {
                    problems.Add(text + "：候选不是 TempCrystalFuse");
                }

                if (half.Target.PrimaryColumn != 0 ||
                    half.Target.Columns.Count != 2 ||
                    half.Target.Columns[0] != 0 ||
                    half.Target.Columns[1] != 1)
                {
                    problems.Add(text + "：Half 的目标列不是第 1、2 象限");
                }

                AngleWorkStep? preStateStep = order.Steps.FirstOrDefault(
                    step => step.Kind == AngleWorkStepKind.PreState &&
                            step.Shape is not null);

                if (preStateStep?.Shape is null)
                {
                    problems.Add(text + "：没有前态");
                    continue;
                }

                ShapeDocument pre = preStateStep.Shape;

                if (!ShapeOps.IsStable(pre))
                {
                    problems.Add(text + "：前态不稳定");
                }

                if (!ShapeSignature.AllColumnsLegal(pre, out int badColumn))
                {
                    problems.Add(
                        text + $"：前态第 {badColumn + 1} 象限不是合法列");
                }

                for (int part = 0; part < pre.PartCount; part++)
                {
                    if (!ShapeSignature.IsTightColumn(pre, part))
                    {
                        problems.Add(
                            text + $"：前态第 {part + 1} 象限不是紧列");
                    }
                }

                if (ShapeSignature.ColumnText(pre, 0) == text)
                {
                    problems.Add(text + "：前态目标列等于目标（循环）");
                }

                if (order.HasIllegalPreState(out _, out _))
                {
                    problems.Add(text + "：HasIllegalPreState 判前态非法");
                }

                CutResult cut = ShapePhysics.Cut(
                    ShapeOps.Rotate(pre, order.Rotation));

                if (!cut.Success || cut.West is null)
                {
                    problems.Add(text + "：真实 Cut 失败");
                    continue;
                }

                ShapeDocument replayed = ShapeOps.Rotate(
                    cut.West!,
                    -order.Rotation);

                if (!ShapeOps.Same(replayed, half.Steps[^1].Shape))
                {
                    problems.Add(text + "：两列没有逐格等于半边目标");
                }

                if (ShapeSignature.ColumnText(replayed, 0) != text)
                {
                    problems.Add(text + "：半破后第 1 列不等于目标列");
                }

                AngleWorkStep? targetStep = order.Steps
                    .LastOrDefault(step => step.Kind == AngleWorkStepKind.Target);

                if (targetStep?.Shape is null ||
                    !ShapeOps.Same(targetStep.Shape, target))
                {
                    problems.Add(text + "：末步没有逐格等于目标");
                }
            }
        }

        Console.WriteLine(
            $"  HALFAUDIT legalNonS={legalNonS} halfComplete={halfComplete} " +
            $"notProducible={notProducible} uncovered={uncovered} " +
            $"problems={problems.Count}");

        if (halfColumns.Count > 0)
        {
            Console.WriteLine("  半边完成的列：" + string.Join(" ", halfColumns));
        }

        if (problems.Count > 0)
        {
            Console.WriteLine("  半边审计问题：" + string.Join(" | ", problems));
        }

        Assert.True(
            problems.Count == 0,
            $"半边工序审计发现 {problems.Count} 个问题：" +
            string.Join(" | ", problems));
        Assert.True(
            halfComplete >= 2,
            "7–9 层至少应有 2 个半边可做的列（cT.T.cPT、cT..T.cPT），实际 " +
            halfComplete);
        Assert.True(
            notProducible > 0,
            "形态外的合法非 S 列仍应按原语义报不可制作");
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

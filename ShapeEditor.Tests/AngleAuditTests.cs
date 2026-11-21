using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

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

                if (order.Kind == AngleBuildKind.NeedsCrystalBreak &&
                    !AngleTestHelpers.TargetColumnMatches(order, target))
                {
                    falsePositives.Add(text + "：末步的目标列没有逐格等于目标");
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
    /// 目标列（被求解的角）允许有洞，但必须「可造」：
    /// </para>
    /// <list type="bullet">
    /// <item>带真实子配方（<see cref="AngleWorkOrder.SubRecipe"/>，算法 6 的造塔；
    /// 或算法 6 论文四步的 <see cref="AngleWorkOrder.Q1SubRecipe"/> 一层半破子配方）；
    /// 或半边候选 1/2 的两根 S 列（<see cref="HalfWorkOrder.TargetColumnsHaveSubRecipe"/>）；</item>
    /// <item>或者是<b>后续前态</b>——它由上一刀/顶针真实产生（目标列与上一步逐格相同），
    /// 不是凭空出现；初始前态不享受这条豁免。</item>
    /// </list>
    /// <para>
    /// 修复前：仅辅助列有洞 7 个（<c>T.ccc</c> / <c>T.ccc.</c> / <c>T.cccT</c> /
    /// <c>T.cccP</c> / <c>P.T..c</c> / <c>TT.ccc</c> / <c>T.cccc</c>）；
    /// 修复后必须为 0，且 S 生成成功数仍是 2125。
    /// 新增的 <c>HOLES</c> 行：初始前态目标列有洞且没有真实子配方的个数必须为 0
    /// （修复前 H 的 18 个初始前态就是这种情况；现在洞已填成临时晶体，顶针时整簇碎掉）。
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
        int targetHoles = 0;
        int targetHoleWithSub = 0;
        int targetHoleDerived = 0;
        int targetHoleNoSub = 0;
        var auxCases = new List<string>();
        var noSubCases = new List<string>();

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

                /*
                 * 后续前态的豁免依据：它必须和上一刀/顶针之前的目标列逐格相同，
                 * 也就是「洞是上一步真实造出来的」，不是初始前态里凭空出现。
                 */
                bool operationSeen = false;
                string? previousTarget = null;

                foreach (AngleWorkStep step in order.Steps)
                {
                    if (step.Kind != AngleWorkStepKind.PreState || step.Shape is null)
                    {
                        if (step.Shape is not null)
                        {
                            previousTarget = ShapeSignature.ColumnText(
                                step.Shape,
                                order.Column);
                        }

                        if (step.Kind is AngleWorkStepKind.Cut or
                            AngleWorkStepKind.Collapse or
                            AngleWorkStepKind.Pin or
                            AngleWorkStepKind.Support or
                            AngleWorkStepKind.Stack)
                        {
                            operationSeen = true;
                        }

                        continue;
                    }

                    steps++;

                    string preTarget = ShapeSignature.ColumnText(
                        step.Shape,
                        order.Column);

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

                    if (targetHole)
                    {
                        targetHoles++;

                        bool hasSubRecipe =
                            order.SubRecipe is not null ||
                            order.Q1SubRecipe is not null ||
                            (order.Half is { TargetColumnsHaveSubRecipe: true } half &&
                             half.Target.Columns.Contains(order.Column));

                        if (hasSubRecipe)
                        {
                            targetHoleWithSub++;
                        }
                        else if (operationSeen && previousTarget == preTarget)
                        {
                            targetHoleDerived++;
                        }
                        else
                        {
                            targetHoleNoSub++;

                            noSubCases.Add(
                                text + "：第 " + (steps) + " 条前态目标列 " +
                                preTarget + "（" + order.Construction + "）");
                        }
                    }

                    previousTarget = preTarget;
                }
            }
        }

        Console.WriteLine(
            $"  TIGHT totalS={s} success={success} steps={steps} tight={tight} " +
            $"onlyTarget={onlyTarget} onlyAux={onlyAux} both={both}");
        Console.WriteLine(
            $"  HOLES totalS={s} targetHole={targetHoles} " +
            $"targetHoleWithSub={targetHoleWithSub} " +
            $"targetHoleDerived={targetHoleDerived} " +
            $"targetHoleNoSub={targetHoleNoSub}");

        Assert.Equal(2125, s, "≤6 层的 S 列应为 2125");
        Assert.Equal(2125, success, "S 列生成成功数应仍为 2125");
        Assert.True(
            onlyAux == 0 && both == 0,
            "仅辅助列有洞的个数必须为 0（修复前 7）：" + string.Join(" | ", auxCases));
        Assert.True(
            targetHoleNoSub == 0,
            "初始前态目标列有洞且没有子配方的个数必须为 0：" +
            string.Join(" | ", noSubCases));
    }

    /// <summary>
    /// 7 层与 8 层的穷举审计（docs §18.2 的规模口径）：
    /// <c>{., T, P, c}</c> 单列全枚举，统计 S 列数与生成失败数。
    ///
    /// <para>
    /// 规模由 4^7 = 16384 与 4^8 = 65536 个列得出；S 列数分别是 4320 / 12646。
    /// 本轮把塔式构造与 §11 通用计划改成纯构造后，两条都必须仍然 0 失败，
    /// 且成功单不得含假阳性（前态每列合法、末步逐格等于目标）。
    /// </para>
    /// </summary>
    public static void ExhaustiveSevenEightLayerAudit()
    {
        var cases = new (int Layers, int ExpectedS)[]
        {
            (7, 4320),
            (8, 12646)
        };

        foreach ((int layers, int expectedS) in cases)
        {
            int combinations = 1 << (2 * layers);
            var buffer = new char[layers];
            var failures = new List<string>();
            var falsePositives = new List<string>();
            var constructions = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int s = 0;
            int success = 0;

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
                    failures.Add(text + "：" + (order.Note ?? "未给出原因"));
                    continue;
                }

                success++;
                string key = order.Construction.ToString();
                constructions[key] = constructions.TryGetValue(key, out int count)
                    ? count + 1
                    : 1;

                if (order.HasIllegalPreState(out int preStateIndex, out int badColumn))
                {
                    falsePositives.Add(
                        text + $"：第 {preStateIndex + 1} 条前态第 {badColumn + 1} 象限不合法");
                    continue;
                }

                if (order.Kind == AngleBuildKind.NeedsCrystalBreak &&
                    !AngleTestHelpers.TargetColumnMatches(order, target))
                {
                    falsePositives.Add(text + "：末步的目标列没有逐格等于目标");
                }
            }

            Console.WriteLine(
                $"  LAYERAUDIT {layers} s={s} success={success} " +
                $"failures={failures.Count} falsePositives={falsePositives.Count} " +
                "constr=" + string.Join(
                    " ",
                    constructions.Select(pair => pair.Key + "=" + pair.Value)));

            Assert.Equal(expectedS, s, layers + " 层的 S 列数");
            Assert.Equal(expectedS, success, layers + " 层的 S 列生成成功数");
            Assert.True(
                failures.Count == 0,
                $"{layers} 层 S 列生成失败 {failures.Count} 个：" +
                string.Join(" | ", failures.Take(20)));
            Assert.True(
                falsePositives.Count == 0,
                $"{layers} 层出现 {falsePositives.Count} 个假阳性：" +
                string.Join(" | ", falsePositives.Take(20)));
        }
    }

    /// <summary>
    /// 基线测量（docs §5.2 / §5.3）：≤6 层的 2125 个 S 列，
    /// 按 <see cref="AngleWorkOrder.Construction"/> 统计构造分布、
    /// 每种构造的辅助列数量分布、<see cref="AngleConstruction.FillBreak"/> 例数，
    /// 以及 <see cref="AngleWorkStepKind.Collapse"/> 步的发射次数（应为 0）。
    ///
    /// <para>
    /// 辅助列数量取两种口径，二者都打印，避免把「声明的辅助列」与
    /// 「前态里真正非空的辅助列」混为一谈：
    /// </para>
    /// <list type="bullet">
    /// <item><c>declared</c> = <see cref="AngleWorkOrder.AuxiliaryColumns"/>.Count（含空象限占位）；</item>
    /// <item><c>nonEmpty</c> = 初始前态里目标列以外有材料的列数（docs §5.2 的统计口径）。</item>
    /// </list>
    /// </summary>
    public static void ConstructionDistributionAudit()
    {
        var constructionCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var declaredAux = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var nonEmptyAux = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var fillBreakColumns = new List<string>();
        var fillBreakUnannotated = new List<string>();
        int collapseSteps = 0;
        int success = 0;
        int dTwoColumns = 0;
        int dTotal = 0;
        int dTailed = 0;
        int dTailedTwoColumns = 0;
        var dTailedCases = new List<string>();

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

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);

                if (!order.IsComplete)
                {
                    continue;
                }

                success++;

                string construction = order.Construction.ToString();

                constructionCounts[construction] =
                    constructionCounts.GetValueOrDefault(construction) + 1;

                CountAux(declaredAux, construction, order.AuxiliaryColumns.Count);
                CountAux(
                    nonEmptyAux,
                    construction,
                    CountNonEmptyAuxColumns(order));

                collapseSteps += order.Steps.Count(
                    step => step.Kind == AngleWorkStepKind.Collapse);

                if (order.Construction is AngleConstruction.D or
                    AngleConstruction.CrystalBreak)
                {
                    dTotal++;

                    if (CountNonEmptyAuxColumns(order) == 2)
                    {
                        dTwoColumns++;
                    }

                    if (order.Steps.Any(step => step.Kind == AngleWorkStepKind.Stack))
                    {
                        dTailed++;
                        dTailedCases.Add(text);

                        if (CountNonEmptyAuxColumns(order) == 2)
                        {
                            dTailedTwoColumns++;
                        }
                    }
                }

                if (order.Construction == AngleConstruction.FillBreak)
                {
                    fillBreakColumns.Add(text);

                    string annotation = order.Note ?? string.Empty;

                    if (!annotation.StartsWith(
                            AngleWorkOrderBuilder.NonPaperConstructionPrefix,
                            StringComparison.Ordinal))
                    {
                        fillBreakUnannotated.Add(text);
                    }
                }
            }
        }

        Console.WriteLine(
            "  CONSTR " + string.Join(
                " ",
                constructionCounts.Select(pair => pair.Key + "=" + pair.Value)));
        Console.WriteLine("  AUXDECL " + FormatAuxTable(declaredAux));
        Console.WriteLine("  AUXNONEMPTY " + FormatAuxTable(nonEmptyAux));
        Console.WriteLine(
            $"  FILLBREAK n={fillBreakColumns.Count}" +
            (fillBreakColumns.Count > 0
                ? "：" + string.Join(" ", fillBreakColumns)
                : string.Empty));
        Console.WriteLine(
            $"  FILLBREAKUNANNOTATED n={fillBreakUnannotated.Count}" +
            (fillBreakUnannotated.Count > 0
                ? "：" + string.Join(" ", fillBreakUnannotated)
                : string.Empty));
        Console.WriteLine($"  COLLAPSE steps={collapseSteps}");
        Console.WriteLine($"  D2RATIO 2列={dTwoColumns}/{dTotal}");
        Console.WriteLine(
            $"  DTAIL tailed={dTailed} twoColumns={dTailedTwoColumns}" +
            (dTailedCases.Count > 0
                ? "：" + string.Join(" ", dTailedCases)
                : string.Empty));

        Assert.Equal(2125, success, "S 列生成成功数应仍为 2125");
        Assert.True(
            constructionCounts.Values.Sum() == success,
            "构造分布应覆盖全部成功工序");
        Assert.Equal(
            0,
            collapseSteps,
            "Collapse 步已并入 Cut / Target，生成器不应再发射 Collapse 步");

        /*
         * T4：D 路径先试 2 根辅助列（支撑 + 爆破）。docs §5.2 的验收是
         * 「单刀样本 286 个里 125 个可以降成 2 列」；这里要求至少达到 125 例，
         * 但不禁止个别形态回退到 3 根。
         */
        Assert.True(
            dTwoColumns >= 125,
            $"构造 D 的两列辅助例数 {dTwoColumns}/{dTotal}，应至少 125（docs §5.2 / T4）");

        // T7：保留下来的 FillBreak 必须全部带「非论文构造」标注。
        Assert.Equal(
            0,
            fillBreakUnannotated.Count,
            "保留的 FillBreak 必须在 Note 里标注「非论文构造」：" +
            string.Join(" ", fillBreakUnannotated));
    }

    private static void CountAux(
        Dictionary<string, Dictionary<string, int>> table,
        string construction,
        int count)
    {
        if (!table.TryGetValue(construction, out Dictionary<string, int>? buckets))
        {
            buckets = new Dictionary<string, int>(StringComparer.Ordinal);
            table[construction] = buckets;
        }

        string key = count switch
        {
            0 => "0",
            1 => "1",
            2 => "2",
            3 => "3",
            _ => ">3"
        };

        buckets[key] = buckets.GetValueOrDefault(key) + 1;
    }

    private static int CountNonEmptyAuxColumns(AngleWorkOrder order)
    {
        int count = 0;

        foreach (AngleWorkStep step in order.Steps)
        {
            if (step.Kind != AngleWorkStepKind.PreState || step.Shape is null)
            {
                continue;
            }

            ShapeDocument pre = step.Shape;

            for (int part = 0; part < pre.PartCount; part++)
            {
                if (part == order.Column)
                {
                    continue;
                }

                for (int layer = 0; layer < pre.LayerCount; layer++)
                {
                    if (pre[layer, part].Type != CellType.Empty)
                    {
                        count++;
                        break;
                    }
                }
            }

            break;
        }

        return count;
    }

    private static string FormatAuxTable(
        Dictionary<string, Dictionary<string, int>> table)
    {
        var parts = new List<string>();

        foreach ((string construction, Dictionary<string, int> buckets) in table
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            parts.Add(
                construction + "[" + string.Join(
                    ",",
                    buckets
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => pair.Key + "列=" + pair.Value)) + "]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 半边工序 v4 的外部审计（7–9 层、4 象限、字母表 <c>. T P c</c>）。
    ///
    /// <para><b>229 个非 S 合法角</b>：每个列 <c>u</c> 与<b>规范伙伴</b>组成两列半边，
    /// 按 v4 判据逐条核对（规范伙伴 = 第一个晶下间隙处留空、下方普通格、上方晶体，
    /// 与 5 个回归例用的伙伴同构）。</para>
    /// <list type="number">
    /// <item>① 两列逆提升后都必须属于 S（否则失败点记 ①）；</item>
    /// <item>② 两张角卡产物必须由真实角工序做出（否则失败点记 ②）；</item>
    /// <item>⑤ 拆柱后一次真实 Collapse 必须逐格等于目标半边（否则失败点记 ⑤）。</item>
    /// </list>
    /// <para>
    /// 失败的 Note 一律 <see cref="AngleWorkOrderBuilder.NotProduciblePrefix"/>，
    /// 并按 ①/②/⑤ 分类；不允许出现「未覆盖」，也不允许伪造 Target 步。
    /// </para>
    /// <para><b>8 条回归</b>：5 条 PASS + 第 6 条 NotProducible + 两个 Cut 反例 PASS。</para>
    /// </summary>
    public static void HalfSideAudit()
    {
        const string alphabet = ".TPc";

        int legalNonS = 0;
        int halfComplete = 0;
        int notProducible = 0;
        int liftFail = 0;
        int cornerFail = 0;
        int settleFail = 0;
        var problems = new List<string>();
        var examples = new List<string>();

        for (int layers = 7; layers <= 9; layers++)
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
                    PillarSCriterion.IsPillarColumn(text))
                {
                    continue;
                }

                legalNonS++;

                string partner = CanonicalPartner(text);
                ShapeDocument target = BuildColumns(text, partner);

                HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(
                    target,
                    0,
                    layers);

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                    target,
                    0,
                    layers,
                    preferCrystalBreak: false);

                if (half is { IsComplete: true })
                {
                    halfComplete++;

                    if (examples.Count < 12)
                    {
                        examples.Add(text + "|" + partner);
                    }

                    VerifyHalfV4(half, target, text, problems);
                    continue;
                }

                notProducible++;

                string note = order.Note ?? string.Empty;

                if (note.Contains("逆提升失败", StringComparison.Ordinal))
                {
                    liftFail++;
                }
                else if (note.Contains("角 u 不可制作", StringComparison.Ordinal) ||
                         note.Contains("角 v 不可制作", StringComparison.Ordinal))
                {
                    cornerFail++;
                }
                else if (note.Contains("拆柱跌落与目标不符", StringComparison.Ordinal))
                {
                    settleFail++;
                }
                else
                {
                    problems.Add(text + "：失败原因没有落到 ①/②/⑤：" + note);
                }

                if (!note.StartsWith(
                        AngleWorkOrderBuilder.NotProduciblePrefix,
                        StringComparison.Ordinal))
                {
                    problems.Add(text + "：失败必须是 NotProducible：" + note);
                }

                if (note.Contains(
                        AngleWorkOrderBuilder.UncoveredPrefix,
                        StringComparison.Ordinal))
                {
                    problems.Add(text + "：不应再出现「未覆盖」：" + note);
                }

                if (order.Steps.Any(step => step.Kind == AngleWorkStepKind.Target))
                {
                    problems.Add(text + "：未完成的工序伪造了 Target 步");
                }
            }
        }

        // ── 8 条回归（5 PASS + 第 6 条 NotProducible + 两个 Cut 反例 PASS）──
        var passCases = new (string U, string V)[]
        {
            ("cT.T.cPT", "TTTT.ccc"),
            ("cT.T..cPT", "TTTT..ccc"),
            ("cT..T.cPT", "TTTTT.ccc"),
            ("cT.T.cPPT", "TTTT.cccc"),
            ("T.cT.T.cPT", "PPTTTT.ccc")
        };

        int regressionPass = 0;

        foreach ((string u, string v) in passCases)
        {
            int layers = Math.Max(u.Length, v.Length);
            ShapeDocument target = BuildColumns(
                u.PadRight(layers, '.'),
                v.PadRight(layers, '.'));

            if (AngleWorkOrderBuilder.BuildHalf(target, 0, layers) is { IsComplete: true })
            {
                regressionPass++;
            }
            else
            {
                problems.Add("回归 " + u + " | " + v + " 应判 PASS");
            }
        }

        int regressionReject = 0;
        {
            const int layers = 10;
            ShapeDocument target = BuildColumns(
                "cT.T.cT.T.c",
                "TTTTTTTTTT");

            if (AngleWorkOrderBuilder.BuildHalf(target, 0, layers) is null)
            {
                regressionReject++;
            }
            else
            {
                problems.Add("回归 6（cT.T.cT.T.c）应判 NotProducible");
            }
        }

        var cutCases = new (string Code, int Layers, string U, string V)[]
        {
            (
                "crcrCu--:CucrCu--:CucrCu--:--crCu--:crcrCu--:crCuCu--:CuCuCu--:" +
                "crCuCu--:crcrcr--:CuCu----:crcr----:P-cr----:Cucr----",
                13,
                "cTT.ccT.T.cPT",
                "cccccTTTT.ccc"),
            (
                "crCuCu--:CuCuCu--:crCuCu--:crcrcr--:CuCuCu--:crcrCu--:P-crCu--:" +
                "CucrCu--:CuCuCu--:Cucrcr--:P-Cu----:Cucr----",
                12,
                "cT.T.cPTTTPT",
                "TTTT.cccTT.c")
        };

        foreach ((string code, int layers, string u, string v) in cutCases)
        {
            ParseResult parsed = new ShortCodeParser().Parse(code, layers, 4);
            CutResult cut = ShapePhysics.Cut(parsed.Document!);
            HalfWorkOrder? half = AngleWorkOrderBuilder.BuildHalf(
                cut.West!,
                0,
                layers);

            if (half is null ||
                ShapeSignature.ColumnText(half.Steps[^1].Shape!, 0) != u ||
                ShapeSignature.ColumnText(half.Steps[^1].Shape!, 1) != v)
            {
                problems.Add("Cut 反例 " + u + " | " + v + " 应被 v4 复现");
            }
            else
            {
                regressionPass++;
            }
        }

        int cornerReady = legalNonS - liftFail - cornerFail;

        Console.WriteLine(
            $"  HALFAUDIT legalNonS={legalNonS} halfComplete={halfComplete} " +
            $"notProducible={notProducible} problems={problems.Count}");
        Console.WriteLine(
            $"  HALFAUDIT-V4 liftFail={liftFail} cornerFail={cornerFail} " +
            $"settleFail={settleFail} cornerReady={cornerReady} " +
            $"regressionPass={regressionPass} regressionReject={regressionReject}");

        if (examples.Count > 0)
        {
            Console.WriteLine("  半边完成的列：" + string.Join(" ", examples));
        }

        if (problems.Count > 0)
        {
            Console.WriteLine("  半边审计问题：" + string.Join(" | ", problems));
        }

        Assert.Equal(229, legalNonS, "7–9 层的非 S 合法角应固定为 229 个");
        Assert.True(
            problems.Count == 0,
            $"半边工序审计发现 {problems.Count} 个问题：" +
            string.Join(" | ", problems));
        Assert.Equal(
            legalNonS,
            halfComplete + notProducible,
            "229 例必须全部落到「成功 / NotProducible」之一");
        Assert.True(
            halfComplete > 6,
            "v4 的半边成功数应显著大于旧基线的 6，实际 " + halfComplete);
        Assert.True(
            cornerReady >= legalNonS - 1,
            "①/② 两步应几乎全通过（短板在 ⑤ 跌落），实际 cornerReady=" +
            cornerReady);
        Assert.Equal(5, regressionPass - 2, "5 条给定半边应全部 PASS");
        Assert.Equal(1, regressionReject, "第 6 条应判 NotProducible");
    }

    /// <summary>
    /// 229 例用到的规范伙伴：第一个「晶体正下方为空」处留一个空层，
    /// 下面普通格、上面晶体（与 5 个回归例的伙伴同构）。
    /// </summary>
    private static string CanonicalPartner(
        string text)
    {
        for (int layer = 1; layer < text.Length; layer++)
        {
            if (text[layer] == ShapeSignature.CrystalCode &&
                text[layer - 1] == ShapeSignature.EmptyCode)
            {
                return new string('T', layer - 1) + "." +
                       new string('c', text.Length - layer);
            }
        }

        return new string('T', text.Length);
    }

    /// <summary>逐条核对一条 v4 半边：五步、两张角卡、真实跌落。</summary>
    private static void VerifyHalfV4(
        HalfWorkOrder half,
        ShapeDocument target,
        string text,
        List<string> problems)
    {
        if (half.Candidate != HalfCandidate.LiftCornerDrop)
        {
            problems.Add(text + "：候选不是 LiftCornerDrop");
        }

        if (half.Steps.Count != 4 ||
            half.Steps[0].Kind != AngleWorkStepKind.PreState ||
            half.Steps[1].Kind != AngleWorkStepKind.Exchange ||
            half.Steps[2].Kind != AngleWorkStepKind.Support ||
            half.Steps[^1].Kind != AngleWorkStepKind.Target)
        {
            problems.Add(text + "：v4 步骤链应为 前态 → 交换 → 拆柱 → 目标");
        }

        if (half.CornerProducts.Count != 2 || half.CornerOrders.Count != 2)
        {
            problems.Add(text + "：v4 必须带两张角卡产物 / 角工序");
            return;
        }

        if (!half.CornerOrders[0].IsComplete || !half.CornerOrders[1].IsComplete)
        {
            problems.Add(text + "：两张角工序都应完整");
        }

        ShapeDocument lifted = half.Steps[0].Shape!;

        if (!PillarSCriterion.IsPillarColumn(lifted, 0) ||
            !PillarSCriterion.IsPillarColumn(lifted, 1))
        {
            problems.Add(text + "：逆提升后的两列都应属于 S");
        }

        if (AngleWorkOrderBuilder.P1Violation(lifted, out _, out _))
        {
            problems.Add(text + "：逆提升后的前态违反 P1");
        }

        ShapeDocument settled = ShapePhysics.Collapse(half.PreSettleShape!).Document!;

        if (!ShapeOps.Same(settled, half.Steps[^1].Shape))
        {
            problems.Add(text + "：Target 步不是真实 Collapse 的结果");
        }

        if (!ShapeOps.Same(half.Steps[^1].Shape, ShapeOps.Project(target, 0, 2)))
        {
            problems.Add(text + "：半边目标没有逐格等于目标那两列");
        }
    }


    /// <summary>按两列文本造 4 象限目标（其余列留空；短列按空层补齐）。</summary>
    private static ShapeDocument BuildColumns(
        string first,
        string second)
    {
        int layers = Math.Max(first.Length, second.Length);
        var document = new ShapeDocument(layers, Parts);

        for (int layer = 0; layer < layers; layer++)
        {
            document[layer, 0] = CellFromCode(
                layer < first.Length ? first[layer] : '.');
            document[layer, 1] = CellFromCode(
                layer < second.Length ? second[layer] : '.');
        }

        return document;
    }

    private static ShapeCell CellFromCode(
        char code)
    {
        return code switch
        {
            'c' => ShapeCell.Crystal('r'),
            'P' => ShapeCell.Pin(),
            'T' => ShapeCell.Shape('C', 'u'),
            _ => ShapeCell.Empty()
        };
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

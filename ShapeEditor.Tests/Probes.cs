using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 诊断入口（不是测试项）：便于逐格核对「构造 H」的前态与回放结果。
///
/// <para>
/// 通过环境变量 <c>SHAPE_PROBE_MODE</c> 选择：
/// </para>
/// <list type="bullet">
/// <item><c>pin</c>：读取 <c>SHAPE_PROBE_A/B/E/N</c> 四列，回放一次
/// <see cref="ShapePhysics.Pin"/>，打印顶针后 col0（可选 <c>SHAPE_PROBE_TARGET</c> 做逐格比较）；</item>
/// <item><c>route</c>：读取 <c>SHAPE_PROBE_TARGET</c>，打印该列的构造 / 算法 / 步骤链；</item>
/// <item><c>stats</c>：3–9 层全部 S 列的弱落 / 多刀统计，并打印残余 47 列的 D/F；</item>
/// <item><c>figure</c>：读取 <c>SHAPE_PROBE_CODE</c>（完整短代码），打印整图求解、
/// 半边装配（两个方向，<c>SHAPE_PROBE_SWAPPED=1</c> 先试方向 B）与
/// 每条轴的 <c>BuildHalf</c> 诊断。</item>
/// <item><c>halfcand</c>：读取 <c>SHAPE_PROBE_CODE</c>（完整短代码）与可选
/// <c>SHAPE_PROBE_COLUMN</c>，用反射分别跑三个半边候选（候选 3 / 1 / 2），
/// 打印各自的可得性、候选名、步骤链与四列快照，最后给出 <c>BuildHalf</c> 的选择。</item>
/// </list>
/// <para>
/// 只有设置环境变量时才会进入，正常运行测试时行为不变。
/// </para>
/// </summary>
internal static class Probes
{
    public static int Run()
    {
        string mode = Environment.GetEnvironmentVariable("SHAPE_PROBE_MODE") ?? "pin";

        switch (mode)
        {
            case "pin":
                RunPin();
                return 0;

            case "route":
                RunRoute();
                return 0;

            case "stats":
                RunStats();
                return 0;

            case "cut":
                RunCut();
                return 0;

            case "tight":
                OneCutTightStats.Print(OneCutTightStats.Measure());
                return 0;

            case "figure":
                RunFigure();
                return 0;

            case "halfcand":
                RunHalfCandidates();
                return 0;

            case "waste":
                RunWasteAudit();
                return 0;

            default:
                Console.WriteLine("未知 SHAPE_PROBE_MODE：" + mode);
                return 1;
        }
    }

    /// <summary>
    /// 复现用户给出的图形：整图求解 + 半边装配 + 每条轴的 BuildHalf 诊断。
    /// 读 <c>SHAPE_PROBE_CODE</c>（完整短代码）与可选 <c>SHAPE_PROBE_CAP</c>。
    /// </summary>
    private static void RunFigure()
    {
        string code = Env("SHAPE_PROBE_CODE");

        if (code.Length == 0)
        {
            Console.WriteLine("需要 SHAPE_PROBE_CODE");
            return;
        }

        if (!ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError))
        {
            Console.WriteLine("短代码尺寸推断失败：" + inferError);
            return;
        }

        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        if (!parsed.Success || parsed.Document is null)
        {
            Console.WriteLine("短代码解析失败：" + parsed.Error);
            return;
        }

        ShapeDocument target = parsed.Document;
        int cap = int.TryParse(
            Environment.GetEnvironmentVariable("SHAPE_PROBE_CAP"),
            out int explicitCap)
            ? explicitCap
            : layers;

        Console.WriteLine("code = " + code);
        Console.WriteLine(
            "n = " + parts + "，层数 = " + layers + "，cap = " + cap);

        for (int part = 0; part < parts; part++)
        {
            string text = ShapeSignature.ColumnText(target, part);

            Console.WriteLine(
                "  q" + (part + 1) + " = " + text +
                "  合法=" + ShapeSignature.CheckColumn(text).IsLegal +
                "  S=" + PillarSCriterion.IsPillarColumn(target, part));
        }

        Console.WriteLine();
        Console.WriteLine("== HalfAssembly.TryBuild ==");

        bool halfOk = HalfAssembly.TryBuild(
            target,
            cap,
            preferSwapped:
                Environment.GetEnvironmentVariable("SHAPE_PROBE_SWAPPED") == "1",
            out Recipe? halfRecipe,
            out string halfNote);

        Console.WriteLine("ok = " + halfOk);
        Console.WriteLine("note = " + halfNote);

        if (halfOk && halfRecipe is not null)
        {
            PrintRecipe(halfRecipe);
        }

        Console.WriteLine();
        Console.WriteLine("== ProductionPipeline.Solve ==");

        PipelineResult solved = ProductionPipeline.Solve(target, cap);

        Console.WriteLine("outcome = " + solved.Outcome);

        foreach (string note in solved.Notes)
        {
            Console.WriteLine("  note: " + note);
        }

        if (solved.Recipe is not null)
        {
            PrintRecipe(solved.Recipe);
        }

        Console.WriteLine();
        Console.WriteLine("== BuildHalf 逐轴 ==");

        for (int column = 0; column < parts; column++)
        {
            HalfWorkOrder? order = AngleWorkOrderBuilder.BuildHalf(
                target,
                column,
                cap);

            Console.WriteLine(
                "  column " + column + " (" + ShapeSignature.ColumnText(target, column) + ")：" +
                (order is null
                    ? "null"
                    : order.Candidate + "，Rotation=" + order.Rotation +
                      "，隔离=" + order.IsComplete));

            if (order is not null)
            {
                ShapeDocument halfShape = order.Steps[^1].Shape!;

                Console.WriteLine(
                    "      half = " + Describe(halfShape) +
                    "  stable=" + ShapeOps.IsStable(halfShape) +
                    "  chain=" + ExchangeChain.TryBuild(halfShape, out _));

                if (order.Steps.Count > 0 && order.Steps[0].Shape is not null)
                {
                    ShapeDocument pre = order.Steps[0].Shape!;

                    Console.WriteLine(
                        "      pre  = " + Describe(pre) +
                        "  stable=" + ShapeOps.IsStable(pre) +
                        "  chain=" + ExchangeChain.TryBuild(pre, out _) +
                        "  collapse(half)=" + ShapeOps.Same(
                            ShapePhysics.Collapse(pre).Document,
                            halfShape));
                }
            }
        }
    }

    /// <summary>
    /// 半边候选逐个测量：读 <c>SHAPE_PROBE_CODE</c>（完整短代码）与可选
    /// <c>SHAPE_PROBE_COLUMN</c>（默认 0），用反射分别调用三个私有候选构造器，
    /// 打印各自的可得性、候选名与步骤链四列。只用于诊断，不参与测试。
    /// </summary>
    private static void RunHalfCandidates()
    {
        string code = Env("SHAPE_PROBE_CODE");

        if (code.Length == 0)
        {
            Console.WriteLine("需要 SHAPE_PROBE_CODE");
            return;
        }

        if (!ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError))
        {
            Console.WriteLine("短代码尺寸推断失败：" + inferError);
            return;
        }

        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        if (!parsed.Success || parsed.Document is null)
        {
            Console.WriteLine("短代码解析失败：" + parsed.Error);
            return;
        }

        ShapeDocument target = parsed.Document;
        int column = int.TryParse(
            Environment.GetEnvironmentVariable("SHAPE_PROBE_COLUMN"),
            out int explicitColumn)
            ? explicitColumn
            : 0;

        AngleBuildKind kind = AngleClassifier.Classify(target, column);

        Console.WriteLine(
            "code = " + code + "；column = " + column +
            "（" + ShapeSignature.ColumnText(target, column) + "）；kind = " + kind);

        foreach ((string Name, string Method) in new[]
                 {
                     ("候选 3（TempCrystalFuse）", "TryBuildTempCrystalFuse"),
                     ("候选 1（Algo8Self）", "TryBuildAlgo8Self"),
                     ("候选 2（Algo8Uplift）", "TryBuildAlgo8Uplift")
                 })
        {
            System.Reflection.MethodInfo? method = typeof(AngleWorkOrderBuilder)
                .GetMethod(
                    Method,
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);

            if (method is null)
            {
                Console.WriteLine(Name + "：找不到方法 " + Method);
                continue;
            }

            AngleWorkOrder? order = method.Invoke(
                null,
                new object[] { target, column, layers, kind }) as AngleWorkOrder;

            Console.WriteLine();
            Console.WriteLine("== " + Name + " ==");

            if (order is null)
            {
                Console.WriteLine("不可得（返回 null）");
                continue;
            }

            Console.WriteLine(
                "IsComplete = " + order.IsComplete +
                "；Construction = " + order.Construction +
                "；Note = " + (order.Note ?? "（无）"));

            foreach (AngleWorkStep step in order.Steps)
            {
                Console.WriteLine(
                    "  [" + step.Kind + "] " + step.Title);

                if (step.Shape is not null)
                {
                    Console.WriteLine("      " + Describe(step.Shape));
                }
            }

            HalfWorkOrder? half = order.Half;

            if (half is null)
            {
                Console.WriteLine("  Half = null");
                continue;
            }

            Console.WriteLine(
                "  Half.Candidate = " + half.Candidate +
                "；IsComplete = " + half.IsComplete +
                "；Rotation = " + half.Rotation +
                "；Columns = " + string.Join(",", half.Target.Columns));

            foreach (AngleWorkStep step in half.Steps)
            {
                Console.WriteLine(
                    "  half [" + step.Kind + "] " + step.Title);

                if (step.Shape is not null)
                {
                    Console.WriteLine("      " + Describe(step.Shape));
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("== BuildHalf ==");

        HalfWorkOrder? chosen = AngleWorkOrderBuilder.BuildHalf(
            target,
            column,
            layers);

        Console.WriteLine(
            chosen is null
                ? "null"
                : "Candidate = " + chosen.Candidate +
                  "；Steps = " + chosen.Steps.Count +
                  "；Rotation = " + chosen.Rotation);
    }

    /// <summary>
    /// 废料统计（诊断入口，不是测试项）：固定种子随机整图，比较
    /// 「交换链废料」与「真实两半交换」的差异，并统计若把判据改成
    /// 「两半都能 BuildHalf 就优先半边装配」会有多少配方被重排。
    ///
    /// <para>
    /// 读 <c>SHAPE_WASTE_SAMPLES</c>（每层样本数，默认 2000）与
    /// <c>SHAPE_WASTE_PARTS</c>（逗号分隔的象限数，默认 <c>4,6,8</c>）。
    /// 输出逐层计数与差异样例（最多前 10 个）。
    /// </para>
    /// </summary>
    private static void RunWasteAudit()
    {
        int samples = int.TryParse(
            Environment.GetEnvironmentVariable("SHAPE_WASTE_SAMPLES"),
            out int parsedSamples)
            ? parsedSamples
            : 2000;

        string partsText =
            Environment.GetEnvironmentVariable("SHAPE_WASTE_PARTS") ?? "4,6,8";

        int[] partsList = partsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.Parse(value.Trim()))
            .ToArray();

        int[] layerRanges = { 3, 4, 5, 6, 7, 8, 9 };

        long totalLegal = 0;
        long totalStable = 0;
        long totalProducible = 0;
        long totalChain = 0;
        long totalLeak = 0;
        long totalHalfUsable = 0;
        long totalSwitchLeak = 0;
        long totalSwitchTrivialColumn = 0;
        long totalSwitchTrivialHalf = 0;
        long totalSwitchAboveTop = 0;
        long totalSwitchThirds = 0;
        long totalSwitchChanged = 0;
        long[] totalLeakHistogram = new long[0];
        long totalHalfAssemblyAlready = 0;
        var leakedSamples = new List<string>();
        var changedSamples = new List<string>();

        foreach (int parts in partsList)
        {
            foreach (int layers in layerRanges)
            {
                if (parts > 4 && layers > 6)
                {
                    continue;
                }

                if (parts > 6 && layers > 5)
                {
                    continue;
                }

                var random = new Random(20261120 + parts * 100 + layers);

                int legal = 0;
                int stable = 0;
                int producible = 0;
                int chain = 0;
                int leak = 0;
                int halfUsable = 0;
                int halfAssemblyAlready = 0;

                for (int sample = 0; sample < samples; sample++)
                {
                    ShapeDocument shape = RandomShape(random, parts, layers);

                    if (!ShapeSignature.AllColumnsLegal(shape, out _))
                    {
                        continue;
                    }

                    legal++;

                    if (!ShapeOps.IsStable(shape))
                    {
                        continue;
                    }

                    stable++;

                    PipelineResult solved = ProductionPipeline.Solve(shape, layers);

                    if (solved.Outcome != PipelineOutcome.Producible ||
                        solved.Recipe is null)
                    {
                        continue;
                    }

                    producible++;

                    if (solved.Recipe.Kind == RecipeKind.HalfAssembly)
                    {
                        halfAssemblyAlready++;
                    }
                }

                int switchLeak = 0;
                int switchTrivialColumn = 0;
                int switchTrivialHalf = 0;
                int switchAboveTop = 0;
                int switchThirds = 0;
                int switchTrivialHalfAny = 0;
                int switchChanged = 0;
                int switchEmptyHalf = 0;
                int[] leakHistogram = new int[layers * parts + 1];

                /*
                 * 第二遍：只看交换链本身。第一遍已经跑过管线，
                 * 这里用堆叠缓存后的稳定性检查会快一些。
                 */
                var randomChain = new Random(20261120 + parts * 100 + layers);

                for (int sample = 0; sample < samples; sample++)
                {
                    ShapeDocument shape = RandomShape(randomChain, parts, layers);

                    if (!ShapeSignature.AllColumnsLegal(shape, out _) ||
                        !ShapeOps.IsStable(shape))
                    {
                        continue;
                    }

                    if (!ExchangeChain.TryBuild(shape, out Recipe? recipe) ||
                        recipe is null)
                    {
                        continue;
                    }

                    chain++;

                    ShapeDocument? waste = recipe.Steps.LastOrDefault(
                        step => step.IsWaste)?.Shape;

                    int leakCells = CountWasteOutsideTarget(recipe, shape);

                    if (waste is null)
                    {
                        continue;
                    }

                    leakHistogram[Math.Min(leakCells, leakHistogram.Length - 1)]++;

                    int wasteCells = 0;

                    for (int layer = 0; layer < waste.LayerCount; layer++)
                    {
                        for (int part = 0; part < waste.PartCount; part++)
                        {
                            if (waste[layer, part].Type != CellType.Empty)
                            {
                                wasteCells++;
                            }
                        }
                    }

                    bool trivialColumn = false;
                    bool[] columnTrivial = new bool[parts];
                    bool[] columnEmpty = new bool[parts];
                    int[] columnTop = new int[parts];

                    for (int part = 0; part < parts; part++)
                    {
                        int occupied = 0;
                        columnTop[part] = -1;

                        for (int layer = 0; layer < layers; layer++)
                        {
                            if (shape[layer, part].Type != CellType.Empty)
                            {
                                occupied++;
                                columnTop[part] = layer;
                            }
                        }

                        columnTrivial[part] = occupied <= 1;
                        columnEmpty[part] = occupied == 0;

                        if (columnTrivial[part])
                        {
                            trivialColumn = true;
                        }
                    }

                    bool trivialHalf = false;
                    bool emptyHalf = false;

                    for (int side = 0; side < 2; side++)
                    {
                        bool all = true;
                        bool empty = true;

                        for (int offset = 0; offset < parts / 2; offset++)
                        {
                            int part = side == 0 ? offset : parts / 2 + offset;

                            if (!columnTrivial[part])
                            {
                                all = false;
                            }

                            if (!columnEmpty[part])
                            {
                                empty = false;
                            }
                        }

                        if (all)
                        {
                            trivialHalf = true;
                        }

                        if (empty)
                        {
                            emptyHalf = true;
                        }
                    }

                    bool leakFarAboveTop = false;

                    for (int layer = 0; layer < Math.Min(waste.LayerCount, layers); layer++)
                    {
                        for (int part = 0; part < Math.Min(waste.PartCount, parts); part++)
                        {
                            if (waste[layer, part].Type == CellType.Empty)
                            {
                                continue;
                            }

                            if (shape[layer, part].Type != CellType.Empty)
                            {
                                continue;
                            }

                            if (layer >= columnTop[part] + 2)
                            {
                                leakFarAboveTop = true;
                            }
                        }
                    }

                    bool hasCrystal = false;

                    for (int layer = 0; layer < layers; layer++)
                    {
                        for (int part = 0; part < parts; part++)
                        {
                            if (shape[layer, part].Type == CellType.Crystal)
                            {
                                hasCrystal = true;
                            }
                        }
                    }

                    bool noTail = !hasCrystal || HighestCrystalLayer(shape) == layers - 1;

                    if (!noTail)
                    {
                        continue;
                    }

                    // 管线在无尾部时才考虑半边装配；有尾部时保持堆叠路径。
                    bool usable = HalfAssembly.TryBuild(
                        shape,
                        layers,
                        out _,
                        out _);

                    if (usable)
                    {
                        halfUsable++;

                        if (leakCells > 0) switchLeak++;
                        if (leakCells > 0 && trivialColumn) switchTrivialColumn++;
                        if (leakCells > 0 && trivialHalf) switchTrivialHalf++;
                        if (leakCells > 0 && leakFarAboveTop) switchAboveTop++;
                        if (leakCells > 0 && leakCells * 3 >= wasteCells) switchThirds++;
                        if (trivialHalf) switchTrivialHalfAny++;

                        if (emptyHalf)
                        {
                            switchEmptyHalf++;
                        }

                        // 最终判据：一半只占最底层 + 废料越出目标 + 两半都能装配；
                        // 旧判据（整半全空）本来就会走半边装配，不算「改变」。
                        if (leakCells > 0 && trivialHalf && !emptyHalf)
                        {
                            switchChanged++;

                            if (changedSamples.Count < 10)
                            {
                                changedSamples.Add(
                                    $"n={parts} L={layers} " +
                                    string.Join(" | ", ColumnTexts(shape)) +
                                    $"  废料漏出={leakCells}");
                            }
                        }

                        if (leakCells > 0 && leakedSamples.Count < 12)
                        {
                            leakedSamples.Add(
                                $"n={parts} L={layers} " +
                                string.Join(" | ", ColumnTexts(shape)) +
                                $"  废料漏出={leakCells} 琐碎列={trivialColumn} " +
                                $"琐碎半边={trivialHalf}");
                        }
                    }

                    if (leakCells > 0)
                    {
                        leak++;
                    }
                }

                Console.WriteLine(
                    $"  WASTE n={parts} L={layers} samples={samples} " +
                    $"legal={legal} stable={stable} producible={producible} " +
                    $"halfAssembly={halfAssemblyAlready} chain={chain} " +
                    $"wasteLeak={leak} halfUsable={halfUsable} " +
                    $"switchLeak={switchLeak} switchTrivialColumn={switchTrivialColumn} " +
                    $"switchTrivialHalf={switchTrivialHalf} " +
                    $"switchAboveTop={switchAboveTop} switchThirds={switchThirds} " +
                    $"trivialHalfAny={switchTrivialHalfAny} " +
                    $"emptyHalf={switchEmptyHalf} changed={switchChanged}");

                totalLeakHistogram = totalLeakHistogram
                    .Select((value, index) =>
                        value + (index < leakHistogram.Length
                            ? leakHistogram[index]
                            : 0))
                    .ToArray();

                totalLegal += legal;
                totalStable += stable;
                totalProducible += producible;
                totalChain += chain;
                totalLeak += leak;
                totalHalfUsable += halfUsable;
                totalSwitchLeak += switchLeak;
                totalSwitchTrivialColumn += switchTrivialColumn;
                totalSwitchTrivialHalf += switchTrivialHalf;
                totalSwitchAboveTop += switchAboveTop;
                totalSwitchThirds += switchThirds;
                totalSwitchChanged += switchChanged;
                totalHalfAssemblyAlready += halfAssemblyAlready;
            }
        }

        Console.WriteLine(
            $"  WASTE TOTAL samples={samples} legal={totalLegal} stable={totalStable} " +
            $"producible={totalProducible} halfAssembly={totalHalfAssemblyAlready} " +
            $"chain={totalChain} wasteLeak={totalLeak} " +
            $"halfUsable={totalHalfUsable} switchLeak={totalSwitchLeak} " +
            $"switchTrivialColumn={totalSwitchTrivialColumn} " +
            $"switchTrivialHalf={totalSwitchTrivialHalf} " +
            $"switchAboveTop={totalSwitchAboveTop} switchThirds={totalSwitchThirds} " +
            $"changed={totalSwitchChanged}");
        Console.WriteLine(
            "  WASTE-LEAK-HIST " +
            string.Join(
                " ",
                totalLeakHistogram
                    .Select((value, index) => index + ":" + value)
                    .Where(pair => !pair.EndsWith(":0", StringComparison.Ordinal))));

        foreach (string sample in leakedSamples)
        {
            Console.WriteLine("  WASTE-SAMPLE " + sample);
        }

        foreach (string sample in changedSamples)
        {
            Console.WriteLine("  WASTE-CHANGED " + sample);
        }

        /*
         * 定向语料：「一半只占最底层」这一族（用户两个反例都属于它）。
         * 随机造半边1，半边2 只保留第 1 层的随机材料；统计这一族里
         * 交换链可解、半边装配可用、最终被改写的比例。
         */
        foreach (int parts in partsList)
        {
            foreach (int layers in layerRanges)
            {
                if (layers < 2)
                {
                    continue;
                }

                if (parts > 4 && layers > 6)
                {
                    continue;
                }

                if (parts > 6 && layers > 5)
                {
                    continue;
                }

                var random = new Random(20261121 + parts * 100 + layers);

                int legal = 0;
                int stable = 0;
                int chain = 0;
                int halfUsable = 0;
                int switched = 0;

                for (int sample = 0; sample < samples; sample++)
                {
                    ShapeDocument shape = RandomShape(random, parts, layers);

                    // 把后半边压成「只占最底层」。
                    for (int part = parts / 2; part < parts; part++)
                    {
                        for (int layer = 1; layer < layers; layer++)
                        {
                            shape[layer, part] = ShapeCell.Empty();
                        }
                    }

                    if (!ShapeSignature.AllColumnsLegal(shape, out _))
                    {
                        continue;
                    }

                    legal++;

                    if (!ShapeOps.IsStable(shape))
                    {
                        continue;
                    }

                    stable++;

                    if (!ExchangeChain.TryBuild(shape, out Recipe? recipe) ||
                        recipe is null)
                    {
                        continue;
                    }

                    chain++;

                    if (CountWasteOutsideTarget(recipe, shape) == 0)
                    {
                        continue;
                    }

                    if (HalfAssembly.TryBuild(shape, layers, out _, out _))
                    {
                        halfUsable++;
                        switched++;
                    }
                }

                Console.WriteLine(
                    $"  WASTE-FLAT n={parts} L={layers} samples={samples} " +
                    $"legal={legal} stable={stable} chain={chain} " +
                    $"halfUsable={halfUsable} switch={switched}");
            }
        }
    }

    /// <summary>固定种子随机整图（字母表 <c>. T P c</c>）。</summary>
    private static ShapeDocument RandomShape(
        Random random,
        int parts,
        int layers)
    {
        var document = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                document[layer, part] = random.Next(4) switch
                {
                    1 => ShapeCell.Shape('C', 'u'),
                    2 => ShapeCell.Crystal('r'),
                    3 => ShapeCell.Pin(),
                    _ => ShapeCell.Empty()
                };
            }
        }

        return document;
    }

    /// <summary>
    /// 交换链废料里「目标本来空、废料却非空」的格子数。
    /// 这些格子就是用户看到的「辅助柱泄漏到废料」。
    /// </summary>
    private static int CountWasteOutsideTarget(
        Recipe recipe,
        ShapeDocument target)
    {
        ShapeDocument? waste = recipe.Steps.LastOrDefault(
            step => step.IsWaste)?.Shape;

        if (waste is null)
        {
            return 0;
        }

        int layers = Math.Max(waste.LayerCount, target.LayerCount);
        int parts = Math.Max(waste.PartCount, target.PartCount);
        int count = 0;

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                bool inWaste = layer < waste.LayerCount &&
                               part < waste.PartCount &&
                               waste[layer, part].Type != CellType.Empty;

                bool inTarget = layer < target.LayerCount &&
                                part < target.PartCount &&
                                target[layer, part].Type != CellType.Empty;

                if (inWaste && !inTarget)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int HighestCrystalLayer(ShapeDocument shape)
    {
        int highest = -1;

        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            for (int part = 0; part < shape.PartCount; part++)
            {
                if (shape[layer, part].Type == CellType.Crystal)
                {
                    highest = layer;
                }
            }
        }

        return highest;
    }

    private static IEnumerable<string> ColumnTexts(ShapeDocument shape)
    {
        for (int part = 0; part < shape.PartCount; part++)
        {
            yield return ShapeSignature.ColumnText(shape, part);
        }
    }

    private static void PrintRecipe(Recipe recipe)    {
        Console.WriteLine(
            "recipe = " + recipe.Kind + "，Rank = " + recipe.Rank +
            "，节点数 = " + recipe.CountNodes());

        for (int index = 0; index < recipe.Steps.Count; index++)
        {
            RecipeStep step = recipe.Steps[index];

            Console.WriteLine(
                "  [" + index + "]" + (step.IsWaste ? "[废料] " : " ") + step.Title);

            for (int part = 0; part < step.Shape.PartCount; part++)
            {
                Console.WriteLine(
                    "        q" + (part + 1) + " = " +
                    ShapeSignature.ColumnText(step.Shape, part));
            }
        }

        HalfAssemblyDetails? details = recipe.HalfAssembly;

        if (details is not null)
        {
            Console.WriteLine(
                "  HalfAssembly：axis=" + details.Axis +
                "，half=" + details.HalfWidth +
                "，direction=" + details.Direction +
                "，finalRotation=" + details.FinalRotation +
                "，left=" + details.Left.Candidate +
                "，right=" + details.Right.Candidate);
            Console.WriteLine(
                "    LeftInput  : " + Describe(details.LeftInput));
            Console.WriteLine(
                "    RightInput : " + Describe(details.RightInput));
            Console.WriteLine(
                "    LeftHalf   : " + Describe(details.LeftHalf));
            Console.WriteLine(
                "    RightHalf  : " + Describe(details.RightHalf));
            Console.WriteLine(
                "    Product    : " + Describe(details.Product));
        }
    }

    private static void RunPin()
    {
        string a = Env("SHAPE_PROBE_A");
        string b = Env("SHAPE_PROBE_B");
        string e = Env("SHAPE_PROBE_E");
        string n = Env("SHAPE_PROBE_N");
        int cap = Math.Max(
            Math.Max(Math.Max(a.Length, b.Length), Math.Max(e.Length, n.Length)),
            Env("SHAPE_PROBE_TARGET").Length);

        ShapeDocument document = BuildCorner(a, b, e, n, cap);

        Console.WriteLine("A|B|E|N = " + a + "|" + b + "|" + e + "|" + n);
        Console.WriteLine("稳定 = " + ShapeOps.IsStable(document));
        Console.WriteLine(
            "各列合法 = " + string.Join(
                " ",
                Enumerable.Range(0, document.PartCount)
                    .Select(part => ShapeSignature.CheckColumn(document, part).IsLegal)));

        PhysicsResult pinned = ShapePhysics.Pin(document);

        if (!pinned.Success || pinned.Document is null)
        {
            Console.WriteLine("Pin 失败：" + pinned.Error);
            return;
        }

        string column = ShapeSignature.ColumnText(pinned.Document, 0);

        Console.WriteLine("顶针后 col0 = " + column);

        string target = Env("SHAPE_PROBE_TARGET");

        if (target.Length > 0)
        {
            Console.WriteLine("目标 = " + target.PadRight(cap, '.'));
            Console.WriteLine("逐格相等 = " + (column == target.PadRight(cap, '.')));
        }
    }

    /// <summary>
    /// 直接看一次真实 Cut：读 A|S|·|F 四列（<c>SHAPE_PROBE_A..D</c>），打印两半与西半的落定结果。
    /// </summary>
    private static void RunCut()
    {
        string[] columns =
        {
            Env("SHAPE_PROBE_A"),
            Env("SHAPE_PROBE_B"),
            Env("SHAPE_PROBE_C"),
            Env("SHAPE_PROBE_D")
        };

        int cap = Math.Max(1, columns.Max(text => text.Length));

        ShapeDocument document = BuildCorner(
            columns[0],
            columns[1],
            columns[2],
            columns[3],
            cap);

        Console.WriteLine("in  = " + Describe(document));

        CutResult cut = ShapePhysics.Cut(document);

        Console.WriteLine("out success=" + cut.Success + " err=" + cut.Error);

        if (cut.West is not null)
        {
            Console.WriteLine("west= " + Describe(cut.West));
        }

        if (cut.East is not null)
        {
            Console.WriteLine("east= " + Describe(cut.East));
        }
    }

    private static string Describe(ShapeDocument document)
    {
        return string.Join(
            " | ",
            Enumerable.Range(0, document.PartCount)
                .Select(part => ShapeSignature.ColumnText(document, part)));
    }

    private static void RunRoute()
    {
        string target = Env("SHAPE_PROBE_TARGET");
        ShapeDocument document = BuildColumn(target);

        bool old = Environment.GetEnvironmentVariable("SHAPE_PROBE_OLD") == "1";

        // SHAPE_PROBE_TIGHT=0：关掉模板 B，用来对比「接入前后」的路线与指标。
        AngleWorkOrderDiagnostics.EnableOneCutTight =
            Environment.GetEnvironmentVariable("SHAPE_PROBE_TIGHT") != "0";

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            document,
            0,
            Math.Max(target.Length, 1),
            preferCrystalBreak: !old);

        AngleWorkOrderDiagnostics.EnableOneCutTight = true;

        Console.WriteLine("目标 = " + target);
        Console.WriteLine(
            "完成 = " + order.IsComplete +
            "；构造 = " + order.Construction +
            "；算法 = " + order.Algorithm +
            "；Rank = " + order.Rank);
        Console.WriteLine("Note = " + (order.Note ?? "（无）"));
        Console.WriteLine(
            "SubRecipe=" + (order.SubRecipe is null ? "null" : order.SubRecipe.Kind.ToString()) +
            " Q1=" + (order.Q1SubRecipe is null ? "null" : order.Q1SubRecipe.Kind.ToString()) +
            " aux=" + string.Join(",", order.AuxiliaryColumns) +
            " weak=" + order.Steps.Count(step => step.Kind == AngleWorkStepKind.WeakDrop) +
            " metrics=" + string.Join("/", order.Metrics.ToVector()));

        foreach (AngleWorkStep step in order.Steps)
        {
            string column = step.Shape is null
                ? "（无数值）"
                : ShapeSignature.ColumnText(step.Shape, order.Column);

            Console.WriteLine(
                "  [" + step.Kind + "] " + step.Title + " => " + column);

            if (step.Kind == AngleWorkStepKind.PreState && step.Shape is not null)
            {
                for (int part = 0; part < step.Shape.PartCount; part++)
                {
                    Console.WriteLine(
                        "      col" + part + " = " +
                        ShapeSignature.ColumnText(step.Shape, part));
                }
            }
        }
    }

    /// <summary>
    /// 残余 47 列（docs/residual-47-weakdrop.md §1）的 D / F 与当前路线。
    /// </summary>
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


    private static void RunStats()
    {
        bool compareOld =
            Environment.GetEnvironmentVariable("SHAPE_PROBE_COMPARE") == "1";
        var alphabet = new[]
        {
            ShapeSignature.EmptyCode,
            ShapeSignature.SolidCode,
            ShapeSignature.PinCode,
            ShapeSignature.CrystalCode
        };

        var total = new Dictionary<int, int>();
        var success = new Dictionary<int, int>();
        var weak = new Dictionary<int, int>();
        var weakSolid = new Dictionary<int, int>();
        var weakOther = new Dictionary<int, int>();
        var multiCut = new Dictionary<int, int>();
        var anyCut = new Dictionary<int, int>();
        var cutOne = new Dictionary<int, int>();
        var cutMulti = new Dictionary<int, int>();
        var pin = new Dictionary<int, int>();
        var failures = new List<string>();
        var weakColumns = new List<string>();
        var weakOtherColumns = new List<string>();
        int peak = 0;
        int peakCuts = 0;

        // O-v2：路线占比与 M1–M7 新旧对比。
        var routes = new Dictionary<AngleRoute, int>();
        var oldRoutes = new Dictionary<AngleRoute, int>();
        int routeTotal = 0;
        int oldRouteTotal = 0;
        int simpleColumns = 0;
        var newSums = new long[7];
        var oldSums = new long[7];
        int weakColumnsTotal = 0;
        int oldWeakColumnsTotal = 0;
        int multiCutTotal = 0;
        int newWorseM1 = 0;
        int lexBetter = 0;
        int lexWorse = 0;
        int lexEqual = 0;
        long newTotalMs = 0;
        long oldTotalMs = 0;
        double newBuildMs = 0;
        double oldBuildMs = 0;
        int compared = 0;
        var lexWorseCases = new List<string>();

        foreach (string column in Residual47)
        {
            bool d = PillarSCriterion.BottomResourceD(column);
            bool f = PillarSCriterion.RecursiveTerminalF(column);
            Console.WriteLine(
                "DF " + column.PadRight(8) + " D=" + d + " F=" + f);
        }

        for (int layers = 3; layers <= 9; layers++)
        {
            total[layers] = 0;
            success[layers] = 0;
            weak[layers] = 0;
            weakSolid[layers] = 0;
            weakOther[layers] = 0;
            multiCut[layers] = 0;
            anyCut[layers] = 0;
            cutOne[layers] = 0;
            cutMulti[layers] = 0;
            pin[layers] = 0;

            int combinations = (int)Math.Pow(alphabet.Length, layers);
            var buffer = new char[layers];
            var watch = Stopwatch.StartNew();

            for (int code = 0; code < combinations; code++)
            {
                int remaining = code;

                for (int layer = 0; layer < layers; layer++)
                {
                    buffer[layer] = alphabet[remaining % alphabet.Length];
                    remaining /= alphabet.Length;
                }

                string text = new(buffer);
                ShapeDocument target = BuildColumn(text);

                if (!ShapeSignature.CheckColumn(text).IsLegal ||
                    !PillarSCriterion.IsPillarColumn(target, 0))
                {
                    continue;
                }

                total[layers]++;

                var newWatch = Stopwatch.StartNew();
                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers);
                newWatch.Stop();
                newTotalMs += newWatch.ElapsedMilliseconds;
                newBuildMs += newWatch.Elapsed.TotalMilliseconds;

                if (!order.IsComplete)
                {
                    failures.Add(text + "：" + (order.Note ?? "未给出原因"));
                    continue;
                }

                success[layers]++;

                AngleWorkOrderMetrics metrics = order.Metrics;

                if (order.Kind == AngleBuildKind.NeedsCrystalBreak)
                {
                    routes[order.EffectiveRoute] =
                        routes.TryGetValue(order.EffectiveRoute, out int routeCount)
                            ? routeCount + 1
                            : 1;

                    routeTotal++;
                }
                else
                {
                    simpleColumns++;
                }

                for (int index = 0; index < 7; index++)
                {
                    newSums[index] += metrics.ToVector()[index];
                }

                if (metrics.WeakDrops > 0)
                {
                    weakColumnsTotal++;
                }

                if (metrics.Cuts > 1)
                {
                    multiCutTotal++;
                }

                // 旧路线（preferCrystalBreak = false）只用于 M1–M7 对比；
                // 设置 SHAPE_PROBE_COMPARE=1 才跑，避免默认统计把耗时翻倍。
                if (compareOld)
                {
                var oldWatch = Stopwatch.StartNew();
                AngleWorkOrder oldOrder = AngleWorkOrderBuilder.Build(
                    target,
                    0,
                    layers,
                    enforceFiveConditions: true,
                    preferCrystalBreak: false);
                oldWatch.Stop();
                oldTotalMs += oldWatch.ElapsedMilliseconds;
                oldBuildMs += oldWatch.Elapsed.TotalMilliseconds;

                if (oldOrder.IsComplete)
                {
                    AngleWorkOrderMetrics oldMetrics = oldOrder.Metrics;

                    if (oldOrder.Kind == AngleBuildKind.NeedsCrystalBreak)
                    {
                        oldRoutes[oldOrder.EffectiveRoute] =
                            oldRoutes.TryGetValue(
                                oldOrder.EffectiveRoute,
                                out int oldRouteCount)
                                ? oldRouteCount + 1
                                : 1;

                        oldRouteTotal++;
                    }

                    for (int index = 0; index < 7; index++)
                    {
                        oldSums[index] += oldMetrics.ToVector()[index];
                    }

                    if (metrics.WeakDrops > oldMetrics.WeakDrops)
                    {
                        newWorseM1++;
                    }

                    if (oldMetrics.WeakDrops > 0)
                    {
                        oldWeakColumnsTotal++;
                    }

                    int first = FirstDifference(metrics, oldMetrics);

                    if (first < 0)
                    {
                        lexEqual++;
                    }
                    else if (metrics.ToVector()[first] < oldMetrics.ToVector()[first])
                    {
                        lexBetter++;
                    }
                    else
                    {
                        lexWorse++;

                        if (lexWorseCases.Count < 20)
                        {
                            lexWorseCases.Add(
                                text + " new=" + order.EffectiveRoute + "/" +
                                string.Join("/", metrics.ToVector()) +
                                " old=" + oldOrder.EffectiveRoute + "/" +
                                string.Join("/", oldMetrics.ToVector()));
                        }
                    }

                    compared++;
                }
                }

                int weakDrops = order.Steps.Count(
                    step => step.Kind == AngleWorkStepKind.WeakDrop);
                int cuts = order.Steps.Count(
                    step => step.Kind == AngleWorkStepKind.Cut);

                if (weakDrops > 0)
                {
                    weak[layers]++;
                    weakColumns.Add(text);

                    if (text[0] == ShapeSignature.SolidCode)
                    {
                        weakSolid[layers]++;
                    }
                    else
                    {
                        weakOther[layers]++;
                        weakOtherColumns.Add(text);
                    }
                }

                if (cuts > 1)
                {
                    multiCut[layers]++;

                    if (order.EffectiveRoute == AngleRoute.CrystalBreak)
                    {
                        cutMulti[layers]++;
                    }
                }

                if (cuts > 0)
                {
                    anyCut[layers]++;

                    if (cuts == 1 &&
                        order.EffectiveRoute == AngleRoute.CrystalBreak)
                    {
                        cutOne[layers]++;
                    }
                }

                if (order.Steps.Any(step => step.Kind == AngleWorkStepKind.Pin))
                {
                    pin[layers]++;
                }

                peak = Math.Max(peak, weakDrops);
                peakCuts = Math.Max(peakCuts, cuts);
            }

            watch.Stop();

            Console.WriteLine(
                "STATS L=" + layers +
                " S=" + total[layers] +
                " success=" + success[layers] +
                " weak=" + weak[layers] +
                " weakL1T=" + weakSolid[layers] +
                " weakOther=" + weakOther[layers] +
                " multiCut=" + multiCut[layers] +
                " 碎晶一刀=" + cutOne[layers] +
                " 碎晶多刀=" + cutMulti[layers] +
                " anyCut=" + anyCut[layers] +
                " pin=" + pin[layers] +
                " ms=" + watch.ElapsedMilliseconds);
        }

        Console.WriteLine("STATS failures=" + failures.Count);
        Console.WriteLine(
            "STATS peakWeakPerColumn=" + peak + " peakCuts=" + peakCuts);

        int successTotal = success.Values.Sum();

        Console.WriteLine(
            "OV2 routes (NeedsCrystalBreak=" + routeTotal +
            " simple=" + simpleColumns + ") " +
            string.Join(
                " ",
                routes.OrderByDescending(pair => pair.Value)
                      .Select(pair =>
                          pair.Key + "=" + pair.Value +
                          "(" + (100.0 * pair.Value / Math.Max(1, routeTotal))
                              .ToString("F2") + "%)")));

        Console.WriteLine(
            "OV2 oldRoutes (NeedsCrystalBreak=" + oldRouteTotal + ") " +
            string.Join(
                " ",
                oldRoutes.OrderByDescending(pair => pair.Value)
                         .Select(pair =>
                             pair.Key + "=" + pair.Value +
                             "(" + (100.0 * pair.Value / Math.Max(1, oldRouteTotal))
                                 .ToString("F2") + "%)")));

        Console.WriteLine(
            "OV2 weakColumns=" + weakColumnsTotal +
            " oldWeakColumns=" + oldWeakColumnsTotal +
            " multiCutColumns=" + multiCutTotal +
            " compared=" + compared +
            " newWorseM1=" + newWorseM1 +
            " lexBetter=" + lexBetter +
            " lexEqual=" + lexEqual +
            " lexWorse=" + lexWorse);

        if (lexWorseCases.Count > 0)
        {
            Console.WriteLine(
                "LEXWORSE n=" + lexWorse + "：" + string.Join(" | ", lexWorseCases));
        }

        string[] names = { "M1", "M2", "M3", "M4", "M5", "M6", "M7" };

        Console.WriteLine(
            "OV2 metrics newAvg=" +
            string.Join(
                " ",
                Enumerable.Range(0, 7).Select(index =>
                    names[index] + "=" +
                    (newSums[index] / (double)Math.Max(1, successTotal))
                        .ToString("F2"))) +
            " oldAvg=" +
            string.Join(
                " ",
                Enumerable.Range(0, 7).Select(index =>
                    names[index] + "=" +
                    (oldSums[index] / (double)Math.Max(1, compared))
                        .ToString("F2"))));

        Console.WriteLine(
            $"OV2 timing newMs={newTotalMs} oldMs={oldTotalMs} " +
            $"newBuildMs={newBuildMs:F1} oldBuildMs={oldBuildMs:F1}");

        if (weakOtherColumns.Count > 0)
        {
            Console.WriteLine(
                "WEAKOTHER n=" + weakOtherColumns.Count + "：" +
                string.Join(" ", weakOtherColumns));
        }

        if (weakColumns.Count > 0)
        {
            Console.WriteLine(
                "WEAKCOLS n=" + weakColumns.Count + "：" +
                string.Join(" ", weakColumns.Take(80)));
        }

        if (failures.Count > 0)
        {
            Console.WriteLine("FAILS " + string.Join(" | ", failures.Take(20)));
        }
    }

    private static string Env(string name)
    {
        return Environment.GetEnvironmentVariable(name) ?? string.Empty;
    }

    /// <summary>M1→M7 字典序：返回第一个不同的下标；全相同返回 -1。</summary>
    private static int FirstDifference(
        AngleWorkOrderMetrics left,
        AngleWorkOrderMetrics right)
    {
        IReadOnlyList<int> a = left.ToVector();
        IReadOnlyList<int> b = right.ToVector();

        for (int index = 0; index < a.Count; index++)
        {
            if (a[index] != b[index])
            {
                return index;
            }
        }

        return -1;
    }

    private static ShapeDocument BuildCorner(
        string a,
        string b,
        string e,
        string n,
        int cap)
    {
        var document = new ShapeDocument(cap, 4);

        Write(document, 0, a);
        Write(document, 1, b);
        Write(document, 2, e);
        Write(document, 3, n);

        return document;
    }

    private static void Write(ShapeDocument document, int part, string text)
    {
        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            char code = layer < text.Length ? text[layer] : ShapeSignature.EmptyCode;

            document[layer, part] = code switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }
    }

    private static ShapeDocument BuildColumn(string text)
    {
        var document = new ShapeDocument(text.Length, 4);

        for (int layer = 0; layer < text.Length; layer++)
        {
            document[layer, 0] = text[layer] switch
            {
                ShapeSignature.CrystalCode => ShapeCell.Crystal('r'),
                ShapeSignature.PinCode => ShapeCell.Pin(),
                ShapeSignature.SolidCode => ShapeCell.Shape('C', 'u'),
                _ => ShapeCell.Empty()
            };
        }

        return document;
    }
}

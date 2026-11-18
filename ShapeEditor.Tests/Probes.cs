using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

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
/// <item><c>stats</c>：3–9 层全部 S 列的弱落 / 多刀统计，并打印残余 47 列的 D/F。</item>
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

            default:
                Console.WriteLine("未知 SHAPE_PROBE_MODE：" + mode);
                return 1;
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

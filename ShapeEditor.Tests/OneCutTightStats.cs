using System.Diagnostics;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Tests;

/// <summary>
/// 模板 B（<c>OneCutTight</c>，docs/angle-route-optimal-v2.md §8.2）接入后的统计：
/// 3–9 层、全部 S 列 ∧ 碎晶角（<see cref="AngleBuildKind.NeedsCrystalBreak"/>，共 20100 列）的
/// 一刀列数（新/旧）、新增列的路线迁移表、模板 B 未覆盖的剩余列、M1/M2 总体平均。
///
/// <para>
/// 「旧」= <see cref="AngleWorkOrderDiagnostics.EnableOneCutTight"/> 关掉（只有模板 A），
/// 也就是接入前的一刀列数基线 12062；「新」= 默认（模板 A 失败后再试模板 B）。
/// </para>
/// <para>
/// 一刀的口径与 <c>docs/handoff/lists/</c> 一致：
/// <see cref="AngleWorkOrder.EffectiveRoute"/> 是 <see cref="AngleRoute.CrystalBreak"/>
/// 且 <see cref="AngleWorkStepKind.Cut"/> 步数 == 1。
/// </para>
/// </summary>
internal static class OneCutTightStats
{
    private const int MinLayers = 3;
    private const int MaxLayers = 9;

    /// <summary>接入模板 B 之前的一刀列数（docs/handoff/README.md）。</summary>
    public const int BaselineOneCut = 12062;

    /// <summary>接入模板 B 之前的 no-one-cut 列数（docs/handoff/README.md）。</summary>
    public const int BaselineNoOneCut = 8038;

    /// <summary>3–9 层 S 列里碎晶角的列数（12062 + 8038）。</summary>
    public const int ExpectedNeedsBreak = 20100;

    /// <summary>统计结果。</summary>
    public sealed record Report(
        int NeedsBreak,
        int OldOneCut,
        int NewOneCut,
        int NewNoOneCut,
        int Incomplete,
        IReadOnlyDictionary<string, int> NewlyCoveredFrom,
        IReadOnlyList<(string Text, string OldRoute)> FirstNewlyCovered,
        IReadOnlyList<string> NewlyCoveredAll,
        int ExistingRouteChanged,
        IReadOnlyList<string> ExistingRouteChangeCases,
        IReadOnlyList<long> NewlyCoveredOldSums,
        IReadOnlyList<long> NewlyCoveredNewSums,
        int NewlyCoveredOldWeakColumns,
        double NewMilliseconds,
        double OldMilliseconds,
        long OldWeakSum,
        long OldPinsLegacySum,
        long OldPinsSum,
        long NewWeakSum,
        long NewPinsLegacySum,
        long NewPinsSum,
        int NewlyCoveredCount);

    /// <summary>跑一遍 3–9 层统计（不打印、不断言）。</summary>
    public static Report Measure()
    {
        var alphabet = new[]
        {
            ShapeSignature.EmptyCode,
            ShapeSignature.SolidCode,
            ShapeSignature.PinCode,
            ShapeSignature.CrystalCode
        };

        int needsBreak = 0;
        int oldOneCut = 0;
        int newOneCut = 0;
        int incomplete = 0;

        var newlyCoveredFrom = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var newlyCovered = new List<string>();
        var firstNewlyCovered = new List<(string Text, string OldRoute)>();
        int existingRouteChanged = 0;
        var existingRouteChangeCases = new List<string>();
        var newlyCoveredOldSums = new long[7];
        var newlyCoveredNewSums = new long[7];
        int newlyCoveredOldWeak = 0;

        long oldWeak = 0;
        long oldPinsLegacy = 0;
        long oldPins = 0;
        long newWeak = 0;
        long newPinsLegacy = 0;
        long newPins = 0;

        double newMs = 0;
        double oldMs = 0;

        for (int layers = MinLayers; layers <= MaxLayers; layers++)
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

                ShapeDocument target = BuildColumn(text);

                // 「旧」：关掉模板 B，只有模板 A。
                AngleWorkOrderDiagnostics.EnableOneCutTight = false;

                var watch = Stopwatch.StartNew();
                AngleWorkOrder oldOrder = AngleWorkOrderBuilder.Build(target, 0, layers);
                watch.Stop();
                oldMs += watch.Elapsed.TotalMilliseconds;

                // 「新」：默认（模板 A 失败后再试模板 B）。
                AngleWorkOrderDiagnostics.EnableOneCutTight = true;

                watch.Restart();
                AngleWorkOrder newOrder = AngleWorkOrderBuilder.Build(target, 0, layers);
                watch.Stop();
                newMs += watch.Elapsed.TotalMilliseconds;

                if (newOrder.Kind != AngleBuildKind.NeedsCrystalBreak)
                {
                    continue;
                }

                needsBreak++;

                if (!oldOrder.IsComplete || !newOrder.IsComplete)
                {
                    incomplete++;
                }

                bool oldIsOneCut = IsOneCut(oldOrder);
                bool newIsOneCut = IsOneCut(newOrder);

                if (oldIsOneCut)
                {
                    oldOneCut++;
                }

                if (newIsOneCut)
                {
                    newOneCut++;
                }

                if (newIsOneCut && !oldIsOneCut)
                {
                    string from = DescribeOldRoute(oldOrder);
                    newlyCoveredFrom[from] = newlyCoveredFrom.GetValueOrDefault(from) + 1;

                    for (int index = 0; index < 7; index++)
                    {
                        newlyCoveredOldSums[index] += oldOrder.Metrics.ToVector()[index];
                        newlyCoveredNewSums[index] += newOrder.Metrics.ToVector()[index];
                    }

                    if (oldOrder.Metrics.WeakDrops > 0)
                    {
                        newlyCoveredOldWeak++;
                    }

                    if (firstNewlyCovered.Count < 20)
                    {
                        firstNewlyCovered.Add((text, from));
                    }

                    newlyCovered.Add(text);
                }
                else if (newIsOneCut && oldIsOneCut &&
                         newOrder.Construction != oldOrder.Construction)
                {
                    existingRouteChanged++;

                    if (existingRouteChangeCases.Count < 12)
                    {
                        existingRouteChangeCases.Add(
                            text + " old=" + oldOrder.Construction +
                            "/" + string.Join("/", oldOrder.Metrics.ToVector()) +
                            " new=" + newOrder.Construction +
                            "/" + string.Join("/", newOrder.Metrics.ToVector()));
                    }
                }

                if (oldOrder.IsComplete)
                {
                    AngleWorkOrderMetrics metrics = oldOrder.Metrics;

                    oldWeak += metrics.WeakDrops;
                    oldPinsLegacy += CountPinSteps(oldOrder);
                    oldPins += metrics.Pins;
                }

                if (newOrder.IsComplete)
                {
                    AngleWorkOrderMetrics metrics = newOrder.Metrics;

                    newWeak += metrics.WeakDrops;
                    newPinsLegacy += CountPinSteps(newOrder);
                    newPins += metrics.Pins;
                }
            }
        }

        // 清单口径与 docs/handoff/lists 一致：先按层数、再按列名字典序。
        List<(string Text, string OldRoute)> sorted = firstNewlyCovered
            .OrderBy(entry => entry.Text.Length)
            .ThenBy(entry => entry.Text, StringComparer.Ordinal)
            .ToList();

        return new Report(
            needsBreak,
            oldOneCut,
            newOneCut,
            needsBreak - newOneCut,
            incomplete,
            newlyCoveredFrom,
            sorted,
            newlyCovered
                .OrderBy(text => text.Length)
                .ThenBy(text => text, StringComparer.Ordinal)
                .ToList(),
            existingRouteChanged,
            existingRouteChangeCases,
            newlyCoveredOldSums,
            newlyCoveredNewSums,
            newlyCoveredOldWeak,
            newMs,
            oldMs,
            oldWeak,
            oldPinsLegacy,
            oldPins,
            newWeak,
            newPinsLegacy,
            newPins,
            newlyCovered.Count);
    }

    /// <summary>打印统计（导出 / 诊断模式用）。</summary>
    public static void Print(Report report)
    {
        Console.WriteLine(
            "TIGHTSTAT needsBreak=" + report.NeedsBreak +
            " oldOneCut=" + report.OldOneCut +
            " newOneCut=" + report.NewOneCut +
            " delta=" + (report.NewOneCut - report.OldOneCut) +
            " newNoOneCut=" + report.NewNoOneCut +
            " oldNoOneCut=" + (report.NeedsBreak - report.OldOneCut) +
            " incomplete=" + report.Incomplete);

        Console.WriteLine(
            "TIGHTSTAT migration(新增一刀列原本走的路线)=" +
            string.Join(
                " ",
                report.NewlyCoveredFrom.Select(pair => pair.Key + "=" + pair.Value)));

        Console.WriteLine(
            "TIGHTSTAT routeChangedAmongExistingOneCut=" + report.ExistingRouteChanged);

        if (report.ExistingRouteChangeCases.Count > 0)
        {
            Console.WriteLine(
                "TIGHTSTAT routeChangedCases=" +
                string.Join(" | ", report.ExistingRouteChangeCases));
        }

        int newlyCovered = report.NewOneCut - report.OldOneCut;
        string[] names = { "M1", "M2", "M3", "M4", "M5", "M6", "M7" };

        Console.WriteLine(
            "TIGHTSTAT newlyCovered=" + newlyCovered +
            " oldAvg=" + string.Join(
                "/",
                Enumerable.Range(0, 7).Select(index =>
                    names[index] + "=" + Avg(report.NewlyCoveredOldSums[index], newlyCovered))) +
            " newAvg=" + string.Join(
                "/",
                Enumerable.Range(0, 7).Select(index =>
                    names[index] + "=" + Avg(report.NewlyCoveredNewSums[index], newlyCovered))) +
            " oldWeakColumns=" + report.NewlyCoveredOldWeakColumns);

        Console.WriteLine(
            "TIGHTSTAT M1M2 oldRoute: M1=" + Avg(report.OldWeakSum, report.NeedsBreak) +
            " M2legacy=" + Avg(report.OldPinsLegacySum, report.NeedsBreak) +
            " M2fixed=" + Avg(report.OldPinsSum, report.NeedsBreak));

        Console.WriteLine(
            "TIGHTSTAT M1M2 newRoute: M1=" + Avg(report.NewWeakSum, report.NeedsBreak) +
            " M2legacy=" + Avg(report.NewPinsLegacySum, report.NeedsBreak) +
            " M2fixed=" + Avg(report.NewPinsSum, report.NeedsBreak));

        Console.WriteLine(
            "TIGHTSTAT timing newMs=" + report.NewMilliseconds.ToString("F0") +
            " oldMs=" + report.OldMilliseconds.ToString("F0"));

        Console.WriteLine(
            "TIGHTSTAT firstNewlyCovered=" +
            string.Join(
                " ",
                report.FirstNewlyCovered.Select(entry => entry.Text + "(" + entry.OldRoute + ")")));

        Console.WriteLine(
            "TIGHTSTAT newlyCoveredTotal=" + report.NewlyCoveredCount +
            " all=" + string.Join(" ", report.NewlyCoveredAll));
    }

    private static double Avg(long sum, int count)
    {
        return sum / (double)Math.Max(1, count);
    }

    /// <summary>一刀口径：碎晶路线且恰好 1 个 <see cref="AngleWorkStepKind.Cut"/> 步。</summary>
    private static bool IsOneCut(AngleWorkOrder order)
    {
        return order.IsComplete &&
               order.EffectiveRoute == AngleRoute.CrystalBreak &&
               CountCuts(order) == 1;
    }

    private static int CountCuts(AngleWorkOrder order)
    {
        return order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut);
    }

    private static long CountPinSteps(AngleWorkOrder order)
    {
        return order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin);
    }

    /// <summary>把「原本走的路线」归类成迁移表的键。</summary>
    private static string DescribeOldRoute(AngleWorkOrder order)
    {
        if (!order.IsComplete)
        {
            return "生成失败";
        }

        int cuts = CountCuts(order);

        if (cuts > 1)
        {
            return "多刀(" + cuts + " 刀)";
        }

        return order.EffectiveRoute switch
        {
            AngleRoute.Pin => "顶针",
            AngleRoute.Algorithm6 => "算法 6",
            AngleRoute.CrystalBreak => "碎晶-" + order.Construction,
            _ => order.EffectiveRoute.ToString()
        };
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

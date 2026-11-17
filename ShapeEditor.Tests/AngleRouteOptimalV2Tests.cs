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
    /// O-v2 §2.1 的四个基线：A / S / F 逐格等于文档表，刀数符合，末步目标列逐格等于目标。
    /// </summary>
    public static void FourCrystalBreakBaselines()
    {
        var baselines = new (string Target, string A, string S, string F, int Cuts)[]
        {
            ("T.c", "cTc", "TPT", "c..", 1),
            ("T...c", "cccTc", "TTTPT", "TTc..", 1),
            (".TT.cTT.c", "cTcTcTcTc", "TTTPTTTPT", "cTcTTTc..", 1),
            ("T.T.c", "cTcTc", "TTTPT", "TTc..", 2)
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
    /// §0.12.2 五步交换支撑柱：位于两刀之间，每一步都有完整四列短代码，并用真实
    /// <see cref="ShapeOps.Rotate"/> / <see cref="ShapePhysics.Cut"/> 回放。
    /// </summary>
    public static void SupportExchangeHasFiveSteps()
    {
        const string text = "T.T.c";

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            FromColumns((0, text)),
            0,
            text.Length);

        int firstCut = order.Steps
            .Select((step, index) => (step, index))
            .First(entry => entry.step.Kind == AngleWorkStepKind.Cut)
            .index;

        int secondCut = order.Steps
            .Select((step, index) => (step, index))
            .Last(entry => entry.step.Kind == AngleWorkStepKind.Cut)
            .index;

        Assert.True(firstCut >= 0 && secondCut > firstCut, "应有两刀");

        List<AngleWorkStep> exchange = order.Steps
            .Skip(firstCut + 1)
            .Where(step => step.Kind is AngleWorkStepKind.Exchange or
                           AngleWorkStepKind.Support)
            .Take(5)
            .ToList();

        Assert.Equal(5, exchange.Count, "轮间交换必须是五步");

        Assert.True(
            exchange.All(step => step.Shape is not null),
            "五步交换的每一步都必须有图形");

        var exporter = new ShortCodeExporter();

        foreach (AngleWorkStep step in exchange)
        {
            string code = exporter.Export(
                step.Shape!,
                omitTopEmptyLayers: false);

            Assert.True(
                code.Split(':').Length > 0 && code.Contains(':'),
                "交换步应带完整四列短代码：" + code);
        }

        // ① 真实旋转：第一步 = 上一刀结果顺转 1 个象限。
        ShapeDocument afterFirstCut = order.Steps[firstCut].Shape!;

        Assert.True(
            ShapeOps.Same(
                exchange[0].Shape,
                ShapeOps.Rotate(afterFirstCut, 1)),
            "① 应是真实 Rotate(after cut, +1)");

        // ② / ④ 的短代码与用户给出的参考序列逐字一致。
        Assert.Equal(
            "CucrCu--:P-CuCu--:CuCuCu--:Cu--P---:CucrCu--",
            exporter.Export(exchange[1].Shape!, omitTopEmptyLayers: false),
            "②（装新支撑柱）应对应参考序列的「旋转」");

        Assert.Equal(
            "Cucrcr--:P-Cu----:CuCu----:Cu------:Cucr----",
            exporter.Export(exchange[3].Shape!, omitTopEmptyLayers: false),
            "④（拆旧柱 / 装保险丝）应对应参考序列的「再换」");

        // 第二刀仍是真实 Cut：用交换后的图形 + 工序旋转回放。
        CutResult replay = ShapePhysics.Cut(
            ShapeOps.Rotate(exchange[3].Shape!, order.Rotation));

        Assert.True(replay.Success && replay.West is not null, "第二刀真实 Cut 失败");

        Assert.True(
            ShapeOps.Same(
                ShapeOps.Rotate(replay.West!, -order.Rotation),
                order.Steps[secondCut].Shape),
            "第二刀的 Cut 步应等于真实物理回放");
    }

    /// <summary>
    /// §5 / §0.12.1 收尾统一形态：Target = 左柱 + 目标列 + 右柱（n = 4），
    /// n = 2 与半边工序保持原行为。
    /// </summary>
    public static void TargetShapeIsUnified()
    {
        foreach (string text in new[] { "T.c", "T...c", ".TT.cTT.c", "T.T.c", ".T.c" })
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
    /// </summary>
    public static void MetricsRecordedAndNotWorse()
    {
        foreach (string text in new[] { "T.c", "T...c", ".TT.cTT.c", "T.T.c" })
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

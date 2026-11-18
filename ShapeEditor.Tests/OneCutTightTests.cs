using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// 模板 B（<c>OneCutTight</c>，docs/angle-route-optimal-v2.md §8.2）的回归：
/// 两个实测样例逐格、判定谓词、预制底针计入 M2、以及 3–9 层的一刀列数与路线迁移。
/// </summary>
internal static class OneCutTightTests
{
    private const int Parts = 4;

    /// <summary>
    /// §8.1 的两个实测样例：前态四列（A | S | · | F）逐格等于文档表，
    /// 一刀后用真实 <see cref="ShapePhysics.Cut"/> 回放，末列逐格等于目标。
    ///
    /// <para>
    /// 模板 B 在前态上就是「双格环下落」那一套；10 层样例
    /// （<c>T.cT.T.ccc</c>）同时也能由构造 G 得到<b>完全相同</b>的四列，
    /// 所以这里只断言「四列 + 一刀 + 末列」，路线名（CrystalBreak / G）不作区分。
    /// </para>
    /// </summary>
    public static void RegressionSamplesReplay()
    {
        var samples = new (string Target, string A, string S, string F, bool TemplateB)[]
        {
            (".T..ccc", "cccTccc", "TccTTPT", "TTc", true),
            // 10 层样例同时被构造 G 覆盖，且 G 的前态与模板 B 逐格相同（同一套双格环下落）。
            ("T.cT.T.ccc", "cTcTccTccc", "PPTTTcTTPT", "cTTTTc", false)
        };

        foreach ((string text, string a, string s, string f, bool templateB) in samples)
        {
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                FromColumns((0, text)),
                0,
                text.Length);

            Assert.True(order.IsComplete, text + " 应完成：" + (order.Note ?? "?"));
            Assert.Equal(
                AngleRoute.CrystalBreak,
                order.EffectiveRoute,
                text + " 应走碎晶路线");
            Assert.Equal(
                1,
                order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut),
                text + " 应是一刀");

            // 模板 B 的判定必须覆盖这两个样例。
            Assert.True(
                AngleWorkOrderBuilder.MatchesOneCutTight(text),
                text + " 应通过模板 B（OneCutTight）的判定");

            if (templateB)
            {
                Assert.Equal(
                    AngleConstruction.CrystalBreak,
                    order.Construction,
                    text + " 应由模板 B 生成（碎晶路线）");

                Assert.True(
                    order.Limitations.Any(line =>
                        line.Contains("OneCutTight", StringComparison.Ordinal)),
                    text + " 的说明里应标注模板 B（OneCutTight）");
            }

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
                text + " 的前态四列应逐格等于模板 B 表：期望 " +
                Describe(expected) + "，实际 " + Describe(preState.Shape!));

            // 真实物理：前态 → Cut（按订单的旋转）→ 转回原象限 → 目标列逐格等于目标。
            CutResult cut = ShapePhysics.Cut(
                ShapeOps.Rotate(preState.Shape!, order.Rotation));

            Assert.True(cut.Success && cut.West is not null, text + " 的真实 Cut 失败");

            ShapeDocument replayed = ShapeOps.Rotate(cut.West!, -order.Rotation);

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(replayed, order.Column),
                text + " 的末列应逐格等于目标");

            Assert.Equal(text, order.TargetColumnText, text + " 的 Target 步应逐格等于目标");

            // M1 必须是 0（一刀碎晶没有弱落）。
            Assert.Equal(0, order.Metrics.WeakDrops, text + " 的弱落应为 0");
        }
    }

    /// <summary>
    /// §8.2 的判定谓词：悬空落点至多一个、且满足「≥3 连晶 + 记录位次」条件。
    /// 用几个最小列直接核对「能不能进模板 B」。
    /// </summary>
    public static void PredicateAcceptsHungLandingFamily()
    {
        // 通过：.T..ccc 是第一条记录、段长 3、悬空落点 d = 2。
        Assert.True(
            AngleWorkOrderBuilder.MatchesOneCutTight(".T..ccc"),
            ".T..ccc 应通过模板 B 的判定");

        // 通过：T.cT.T.ccc 的悬空落点是第二条记录，但地面到第一条记录的 d 层连续非空。
        Assert.True(
            AngleWorkOrderBuilder.MatchesOneCutTight("T.cT.T.ccc"),
            "T.cT.T.ccc 应通过模板 B 的判定");

        // 不通过：悬空段的晶体只有 2 连（双格支撑环要求 ≥3）。
        Assert.False(
            AngleWorkOrderBuilder.MatchesOneCutTight(".T.cc"),
            ".T.cc 的悬空段只有 2 连晶，不应通过");

        // 不通过：悬空落点是第三条记录。
        Assert.False(
            AngleWorkOrderBuilder.MatchesOneCutTight("T.cT.cT.T.ccc"),
            "第三条记录才出现悬空落点不应通过");

        // 不通过：两个悬空落点。
        Assert.False(
            AngleWorkOrderBuilder.MatchesOneCutTight(".T..c..T..ccc"),
            "两个悬空落点不应通过");
    }

    /// <summary>
    /// M2 口径修正（docs §7.1 / 任务书任务 5）：
    /// 辅助列里<b>预制的针</b>必须计入 M2。
    ///
    /// <para>
    /// <c>T.cT.T.ccc</c> 的 S = <c>PPTTTcTTPT</c> 里预制了 3 枚针：
    /// 第 1–2 层的 2 枚底针 + 第 9 层的 1 枚「环锚」针（任务书只提到 2 枚底针）。
    /// 核心恢复时不顶针 ⇒ 修正前 M2 = 0（错），修正后 M2 = 3。
    /// </para>
    /// </summary>
    public static void PrePlacedPinsCountTowardM2()
    {
        const string text = "T.cT.T.ccc";

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            FromColumns((0, text)),
            0,
            text.Length);

        Assert.True(order.IsComplete, text + " 应完成");

        int pinSteps = order.Steps.Count(step => step.Kind == AngleWorkStepKind.Pin);

        Assert.Equal(0, pinSteps, text + " 的工序里不应有 Pin 步（核心恢复不顶针）");
        Assert.Equal(
            3,
            order.Metrics.Pins,
            text + " 的 M2 必须把 S 里预制的针算进去（修正前是 0；" +
            "2 枚底针 + 1 枚第 9 层的环锚针）");

        // 前态里确实有 3 枚针，且都在辅助列。
        AngleWorkStep preState = order.Steps.First(
            step => step.Kind == AngleWorkStepKind.PreState &&
                    step.Shape is not null);

        int prePins = 0;

        for (int part = 0; part < preState.Shape!.PartCount; part++)
        {
            if (part == order.Column)
            {
                continue;
            }

            for (int layer = 0; layer < preState.Shape.LayerCount; layer++)
            {
                if (preState.Shape[layer, part].Type == CellType.Pin)
                {
                    prePins++;
                }
            }
        }

        Assert.Equal(3, prePins, text + " 的辅助列里应正好有 3 枚预制针");
    }

    /// <summary>
    /// 3–9 层统计：一刀列数不低于接入前基线 12062、没有生成失败、
    /// 模板 B 确实补上了原本走顶针 / 多刀 / 算法 6 的列，且统计耗时 ≤ 8 s。
    /// </summary>
    public static void CoversMoreOneCutColumns()
    {
        OneCutTightStats.Report report = OneCutTightStats.Measure();

        OneCutTightStats.Print(report);

        Assert.Equal(
            OneCutTightStats.ExpectedNeedsBreak,
            report.NeedsBreak,
            "3–9 层碎晶角列数应为 20100");

        Assert.Equal(
            OneCutTightStats.BaselineOneCut,
            report.OldOneCut,
            "关掉模板 B 后的一刀列数应等于基线 12062（证明开关只影响模板 B）");

        Assert.Equal(0, report.Incomplete, "模板 B 接入后不应出现生成失败");

        Assert.True(
            report.NewOneCut >= OneCutTightStats.BaselineOneCut,
            $"一刀列数 {report.NewOneCut} 不应低于基线 12062");

        Assert.True(
            report.NewOneCut > OneCutTightStats.BaselineOneCut,
            "模板 B 应至少补上一批新的一刀列");

        Assert.True(
            report.NewNoOneCut < OneCutTightStats.BaselineNoOneCut,
            $"模板 B 覆盖后 no-one-cut 应少于 {OneCutTightStats.BaselineNoOneCut}，实际 {report.NewNoOneCut}");

        Assert.True(
            report.NewMilliseconds <= 8000,
            $"3–9 层统计耗时 {report.NewMilliseconds:F0} ms，应 ≤ 8000 ms");
    }

    private static ShapeDocument FromColumns(params (int Column, string Text)[] columns)
    {
        int layers = Math.Max(1, columns.Max(entry => entry.Text.Length));
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

using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// §15.10 / §15.11（构造 G 的 G5 / G8 / G8a）、§5.3（整图 Hc）、
/// §14.4（孤立列产物规则）与 §5.2（D 辅助列）的落地核对。
/// </summary>
internal static class AuxiliaryColumnRulesTests
{
    private static readonly char[] Alphabet =
    {
        ShapeSignature.EmptyCode,
        ShapeSignature.SolidCode,
        ShapeSignature.PinCode,
        ShapeSignature.CrystalCode
    };

    /// <summary>
    /// §15.10 / §15.11：构造 G 的 B（环伙伴）与保险丝柱必须满足：
    /// <list type="bullet">
    /// <item>B 是紧列；需要「下降层」时放晶体而不是留洞（G5 修正）；</item>
    /// <item>保险丝在独立一列、在所有临时晶体块的顶层放晶体；</item>
    /// <item>保险丝所在层 k，目标列在该层不能是晶体（G8a 第一条）；</item>
    /// <item>真实 Cut 之后目标列的原生晶体一颗不少（G8a 第二条的实测口径）。</item>
    /// </list>
    /// </summary>
    public static void ConstructGFollowsG5G8AndG8a()
    {
        const int cap = 5;
        const string text = "T.ccc";

        ShapeDocument target = FromColumns(4, cap, (0, text));

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, cap, preferCrystalBreak: false);

        Assert.Equal(AngleConstruction.G, order.Construction, text);
        Assert.True(order.IsComplete, order.Note ?? text + " 应可制作");

        ShapeDocument pre = order.Steps
            .First(step =>
                step.Kind == AngleWorkStepKind.PreState &&
                step.Shape is not null)
            .Shape!;

        // 布局：目标列 | B（+1）| 空（+2）| 保险丝柱 B′（−1）。
        Assert.True(
            ShapeSignature.IsTightColumn(pre, 1),
            "G5：环伙伴 B 必须是紧列，实际 " + ShapeSignature.ColumnText(pre, 1));
        Assert.True(
            ShapeSignature.IsTightColumn(pre, 3),
            "G8：保险丝柱必须是独立紧列，实际 " + ShapeSignature.ColumnText(pre, 3));

        Assert.True(
            ShapeSignature.ColumnText(pre, 2)
                .All(code => code == ShapeSignature.EmptyCode),
            "第三象限应留空（辅助列最少化）");

        var partnerCrystals = CrystalLayers(pre, 1);
        var fuseCrystals = CrystalLayers(pre, 3);

        Assert.True(partnerCrystals.Count > 0, "B 的下降层应放晶体（G5 修正）");
        Assert.True(
            partnerCrystals.SetEquals(fuseCrystals),
            "保险丝层应与 B 的下降层一致：" +
            string.Join(",", partnerCrystals) + " vs " + string.Join(",", fuseCrystals));

        foreach (int layer in fuseCrystals)
        {
            // G8a：目标列在该层不能是晶体（否则碎保险丝会连带毁掉目标晶体）。
            Assert.True(
                target[layer, 0].Type != CellType.Crystal,
                "G8a：保险丝层 " + (layer + 1) + " 在目标列里是晶体");

            // 该层在前态里必须是临时晶体（洞的填充），不是原生晶体。
            Assert.True(
                pre[layer, 0].Type == CellType.Crystal,
                "保险丝层在前态应是临时晶体（闸门）");
        }

        // G8：保险丝必须在环下方（比环锁定的晶体段低）。
        int ringBottom = RingBottom(pre);

        Assert.True(
            fuseCrystals.All(layer => layer < ringBottom),
            "G8：保险丝必须在环下方（环底 = 第 " + (ringBottom + 1) + " 层）");

        // 真实 Cut 后目标列原生晶体一颗不少（G8a 的实测口径）。
        int expectedCrystals = text.Count(code => code == 'c');

        AngleWorkStep cut = order.Steps.First(
            step => step.Kind == AngleWorkStepKind.Cut);

        Assert.Equal(
            expectedCrystals,
            CrystalLayers(cut.Shape!, 0).Count,
            "半破后目标列的原生晶体数应不变");

        for (int layer = 0; layer < cap; layer++)
        {
            if (text[layer] == 'c')
            {
                Assert.True(
                    cut.Shape![layer, 0].Type == CellType.Crystal,
                    "半破误伤了第 " + (layer + 1) + " 层的目标晶体");
            }
        }
    }

    /// <summary>
    /// §5.3 ①：<c>Hc</c> = <b>整图</b>最高晶体所在层。用文档的两种情况验证：
    /// <list type="bullet">
    /// <item>情况 A：Q2 无晶体 ⇒ <c>Hc</c> = Q1 的最高晶体（第 3 层），Q1 的 L4/L5 进尾部；</item>
    /// <item>情况 B：Q2 的晶体在 L5 ⇒ <c>Hc</c> = 5，Q1 的「尾部」L5 不再被剥掉
    /// （否则会把它误当成尾部，与整图口径矛盾）。</item>
    /// </list>
    /// </summary>
    public static void TailBoundaryUsesWholeShapeHc()
    {
        const int cap = 5;

        // 情况 A：Q1 = T.c.T，Q2 = TTTTT（无晶体）⇒ Hc = 3。
        ShapeDocument targetA = FromColumns(
            4,
            cap,
            (0, "T.c.T"),
            (1, "TTTTT"));

        Assert.Equal(2, WholeShapeHighestCrystal(targetA));

        AngleWorkOrder orderA = AngleWorkOrderBuilder.Build(targetA, 0, cap, preferCrystalBreak: false);

        Assert.True(orderA.IsComplete, orderA.Note ?? "情况 A 应可制作");
        Assert.True(
            orderA.Steps.Any(step => step.Kind == AngleWorkStepKind.Stack),
            "情况 A：Hc = 3，Q1 的第 4/5 层应作为尾部由 Stack 接回");
        Assert.True(
            orderA.Limitations.Any(line =>
                line.Contains("Hc = 第 3 层", StringComparison.Ordinal)),
            "情况 A：Hc 应取整图的第 3 层（0 起第 2 层）");

        // 情况 B：Q1 = T.c.T，Q2 = TTTTc（晶体在 L5）⇒ Hc = 5，不剥尾部。
        ShapeDocument targetB = FromColumns(
            4,
            cap,
            (0, "T.c.T"),
            (1, "TTTTc"));

        Assert.Equal(4, WholeShapeHighestCrystal(targetB));

        AngleWorkOrder orderB = AngleWorkOrderBuilder.Build(targetB, 0, cap, preferCrystalBreak: false);

        Assert.True(orderB.IsComplete, orderB.Note ?? "情况 B 应可制作");
        Assert.False(
            orderB.Steps.Any(step => step.Kind == AngleWorkStepKind.Stack),
            "情况 B：Hc = 5（Q2 的晶体），Q1 的 L5 不是尾部，不应有 Stack 步");
        Assert.False(
            orderB.Limitations.Any(line =>
                line.Contains("核心 / 尾部拆分", StringComparison.Ordinal)),
            "情况 B：Hc 已到画布顶部，不触发核心 / 尾部拆分");
    }

    /// <summary>
    /// §14.4：角工序的产物规则。
    /// <list type="bullet">
    /// <item>定理 4 成立且施工里有托柱 ⇒ 追加「拆托柱」步（≤6 层实测 604 例，如 <c>cT.</c>）；</item>
    /// <item>定理 4 不成立 ⇒ 停在 S 形态，Limitations 写「保留托柱：该列不是孤立列」；</item>
    /// <item>Theorem 4 成立的 Simple 角（如 <c>TTc</c>）本来就没有托柱，只写「该列是孤立列」。</item>
    /// </list>
    /// </summary>
    public static void IsolatedColumnDowngradeRule()
    {
        // 定理 4 成立、且本来就没有托柱的 Simple 案例。
        {
            ShapeDocument target = FromColumns(4, 3, (0, "TTc"));

            Assert.True(
                IsolatedColumn.IsIsolatedColumn(target, 0),
                "TTc 应是孤立列（定理 4 成立）");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 3, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? "TTc 应可制作");
            Assert.True(
                order.Limitations.Any(line =>
                    line.Contains("该列是孤立列（定理 4 成立）", StringComparison.Ordinal)),
                "Theorem 4 成立的案例要写明可以按孤立列施工");
            Assert.False(
                order.Steps.Any(step => step.Kind == AngleWorkStepKind.Support),
                "TTc 不需要托柱，不应有 Support 步");
        }

        // 定理 4 不成立、施工里带托柱的 S 案例。
        {
            ShapeDocument target = FromColumns(4, 4, (0, ".T.c"));

            Assert.False(
                IsolatedColumn.IsIsolatedColumn(target, 0),
                ".T.c 带洞，不应满足定理 4");

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, 4, preferCrystalBreak: false);

            Assert.True(order.IsComplete, order.Note ?? ".T.c 应可制作");
            Assert.True(
                order.Limitations.Any(line =>
                    line.Contains("保留托柱：该列不是孤立列", StringComparison.Ordinal)),
                "非孤立列必须写明「保留托柱：该列不是孤立列」");
        }

        // 全量口径：
        //   · 有托柱且定理 4 成立 ⇒ 真的追加「拆托柱」步（本语料实测 600+ 例）；
        //   · 有托柱但定理 4 不成立 ⇒ 必须写「保留托柱：该列不是孤立列」；
        //   · 没有托柱的 Simple / 无晶体档只写「该列是孤立列」。
        int withPillar = 0;
        int downgraded = 0;
        int isolatedOrders = 0;
        int pillarButNotIsolated = 0;
        int downgradedVerified = 0;
        string? downgradedExample = null;

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

                AngleWorkOrder order = AngleWorkOrderBuilder.Build(target, 0, layers, preferCrystalBreak: false);

                if (!order.IsComplete)
                {
                    continue;
                }

                bool isolated = IsolatedColumn.IsIsolatedColumn(target, 0);

                if (isolated)
                {
                    isolatedOrders++;
                }

                bool hasPillar =
                    order.AuxiliaryColumns.Count > 0 ||
                    order.Steps.Any(step => step.Kind == AngleWorkStepKind.Support);

                if (hasPillar)
                {
                    withPillar++;

                    if (!isolated &&
                        !order.Limitations.Any(line => line.Contains(
                            "保留托柱：该列不是孤立列",
                            StringComparison.Ordinal)))
                    {
                        pillarButNotIsolated++;
                    }
                }

                int downgradeIndex = -1;

                for (int index = 0; index < order.Steps.Count; index++)
                {
                    if (order.Steps[index].Title.Contains(
                            "拆掉托柱",
                            StringComparison.Ordinal))
                    {
                        downgradeIndex = index;
                        break;
                    }
                }

                if (downgradeIndex < 0)
                {
                    continue;
                }

                downgraded++;
                downgradedExample ??= text;

                AngleWorkStep step = order.Steps[downgradeIndex];

                // 拆托柱这一步必须：只在目标列留材料、稳定、列逐格等于目标列。
                bool onlyTargetColumn = true;

                for (int part = 1; part < step.Shape!.PartCount; part++)
                {
                    for (int layer = 0; layer < step.Shape.LayerCount; layer++)
                    {
                        if (step.Shape[layer, part].Type != CellType.Empty)
                        {
                            onlyTargetColumn = false;
                        }
                    }
                }

                if (step.Shape is not null &&
                    onlyTargetColumn &&
                    ShapeOps.IsStable(step.Shape) &&
                    ShapeSignature.ColumnText(step.Shape, 0) == text)
                {
                    downgradedVerified++;
                }
            }
        }

        Console.WriteLine(
            $"  ISOLATED withPillar={withPillar} downgraded={downgraded} " +
            $"verified={downgradedVerified} isolatedOrders={isolatedOrders} " +
            $"example={downgradedExample}");

        Assert.True(withPillar > 0, "≤6 层应有带托柱的 S 工序单");
        Assert.True(isolatedOrders > 0, "≤6 层应有满足定理 4 的 S 列（如 Tc）");
        Assert.True(downgraded > 0, "定理 4 成立的带托柱案例应真的追加「拆托柱」步");
        Assert.Equal(
            downgraded,
            downgradedVerified,
            "每个「拆托柱」步都必须是稳定的孤立列、且逐格等于目标列");
        Assert.Equal(
            0,
            pillarButNotIsolated,
            "带托柱但不满足定理 4 的工序单必须写「保留托柱：该列不是孤立列」");
    }

    // ───────────────────────── 小工具 ─────────────────────────

    private static HashSet<int> CrystalLayers(ShapeDocument document, int column)
    {
        var layers = new HashSet<int>();

        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            if (document[layer, column].Type == CellType.Crystal)
            {
                layers.Add(layer);
            }
        }

        return layers;
    }

    /// <summary>环底的层号：目标列里最高的连续三连晶段的最下层。</summary>
    private static int RingBottom(ShapeDocument pre)
    {
        string text = ShapeSignature.ColumnText(pre, 0);
        int highestBottom = 0;

        for (int layer = 0; layer + 2 < text.Length; layer++)
        {
            if (text[layer] == 'c' &&
                text[layer + 1] == 'c' &&
                text[layer + 2] == 'c')
            {
                highestBottom = layer;
            }
        }

        return highestBottom;
    }

    private static int WholeShapeHighestCrystal(ShapeDocument document)
    {
        int highest = -1;

        for (int part = 0; part < document.PartCount; part++)
        {
            int layer = AngleClassifier.HighestCrystalLayer(document, part);

            if (layer > highest)
            {
                highest = layer;
            }
        }

        return highest;
    }

    private static ShapeDocument BuildColumn(string text)
    {
        var document = new ShapeDocument(text.Length, 4);

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

    private static ShapeDocument FromColumns(
        int parts,
        int layers,
        params (int Column, string Text)[] columns)
    {
        var document = new ShapeDocument(layers, parts);

        foreach ((int column, string text) in columns)
        {
            for (int layer = 0; layer < layers; layer++)
            {
                char code = layer < text.Length ? text[layer] : '.';

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
}

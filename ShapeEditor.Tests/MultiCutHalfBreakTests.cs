using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// §15 多刀半破的验收语料：<c>TT.T.c</c>、<c>TT.T.T.c</c>、<c>cTT.T.c</c>。
///
/// <para>
/// 口径：<b>刀数 == 初始临时晶体连通块数 K</b>；每刀一个真实
/// <see cref="ShapePhysics.Cut"/>（Cut 步的 Shape 是落定后的结果），
/// 轮间用新增的 <see cref="AngleWorkStepKind.Exchange"/> 步重装辅助柱；
/// 末步 <see cref="AngleWorkStepKind.Target"/> 逐格等于目标。
/// </para>
/// </summary>
internal static class MultiCutHalfBreakTests
{
    private const int Parts = 4;

    public static void ThreeCorporaUseMultiCut()
    {
        foreach (string text in new[] { "TT.T.c", "TT.T.T.c", "cTT.T.c" })
        {
            ShapeDocument target = BuildColumn(text);
            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length);

            Assert.True(order.IsComplete, text + " 应完成：" + (order.Note ?? "?"));
            Assert.Equal(AngleConstruction.D, order.Construction, text);
            Assert.Equal(AngleAlgorithm.Algorithm4, order.Algorithm, text);

            AngleWorkStep preState = order.Steps.First(
                step => step.Kind == AngleWorkStepKind.PreState &&
                        step.Shape is not null);

            string preColumn = ShapeSignature.ColumnText(preState.Shape!, 0);

            // K：前态里多出来的临时晶体按纵向连通分组。
            int blocks = CountTemporaryBlocks(preColumn, text);
            int cuts = order.Steps.Count(step => step.Kind == AngleWorkStepKind.Cut);
            int exchanges = order.Steps.Count(
                step => step.Kind == AngleWorkStepKind.Exchange);

            Assert.True(blocks >= 2, text + " 应是多刀（K ≥ 2）");
            Assert.Equal(blocks, cuts, text + " 的刀数必须等于 |K|");
            Assert.Equal(blocks - 1, exchanges, text + " 的轮间交换次数应为 |K|−1");

            // 前态目标列是紧列；辅助列也必须是紧列。
            Assert.True(
                ShapeSignature.IsTightColumn(preState.Shape!, 0),
                text + " 的初始前态目标列应是紧列：" + preColumn);

            for (int part = 0; part < Parts; part++)
            {
                if (part == 0 ||
                    ShapeSignature.ColumnText(preState.Shape!, part)
                        .All(code => code == ShapeSignature.EmptyCode))
                {
                    continue;
                }

                Assert.True(
                    ShapeSignature.IsTightColumn(preState.Shape!, part),
                    text + $" 的第 {part + 1} 象限辅助列不是紧列");
            }

            // P1：初始前态每个原生晶体段的段底都坐在普通格上。
            for (int layer = 1; layer < text.Length; layer++)
            {
                if (text[layer] != ShapeSignature.CrystalCode ||
                    text[layer - 1] == ShapeSignature.CrystalCode)
                {
                    continue;
                }

                Assert.Equal(
                    ShapeSignature.SolidCode,
                    preColumn[layer - 1],
                    text + $" 的初始前态第 {layer + 1} 层晶体没有坐在普通格上");
            }

            // 逐步回放：PreState → Cut → Exchange → Cut → … → Target。
            ShapeDocument current = preState.Shape!;
            int cutIndex = 0;

            foreach (AngleWorkStep step in order.Steps)
            {
                switch (step.Kind)
                {
                    case AngleWorkStepKind.PreState:
                    case AngleWorkStepKind.Exchange:
                        Assert.True(step.Shape is not null, text + " 的交换步缺图形");
                        Assert.True(
                            ShapeSignature.IsLegalColumn(step.Shape!, 0),
                            text + " 的交换步目标列不合法");

                        if (step.Kind == AngleWorkStepKind.Exchange)
                        {
                            // 交换只重装辅助柱：目标列必须与上一刀落定后逐格相同。
                            Assert.Equal(
                                ShapeSignature.ColumnText(current, 0),
                                ShapeSignature.ColumnText(step.Shape!, 0),
                                text + " 的轮间交换改动了目标列");

                            current = step.Shape!;
                        }

                        break;

                    case AngleWorkStepKind.Cut:
                    {
                        cutIndex++;

                        CutResult cut = ShapePhysics.Cut(
                            ShapeOps.Rotate(current, order.Rotation));

                        Assert.True(
                            cut.Success && cut.West is not null,
                            text + $" 的第 {cutIndex} 刀真实 Cut 失败：" + cut.Error);

                        ShapeDocument replayed = ShapeOps.Rotate(
                            cut.West!,
                            -order.Rotation);

                        Assert.True(
                            ShapeOps.Same(replayed, step.Shape),
                            text + $" 的第 {cutIndex} 刀应逐格等于真实 Cut 的西半边");

                        Assert.True(
                            ShapeOps.IsStable(step.Shape!),
                            text + $" 的第 {cutIndex} 刀落下后应稳定");

                        current = step.Shape!;
                        break;
                    }

                    case AngleWorkStepKind.Target:
                        Assert.True(
                            ShapeOps.Same(step.Shape, target),
                            text + " 的末步应逐格等于目标");
                        break;
                }
            }

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(current, 0),
                text + " 的最后一刀应逐格等于目标列");

            Console.WriteLine(
                $"  MULTICUT {text} K={blocks} cuts={cuts} exchanges={exchanges} " +
                $"pre={preColumn} final={ShapeSignature.ColumnText(current, 0)}");
        }
    }

    private static int CountTemporaryBlocks(
        string preState,
        string target)
    {
        int blocks = 0;

        for (int layer = 0; layer < preState.Length; layer++)
        {
            if (preState[layer] != ShapeSignature.CrystalCode ||
                (layer < target.Length &&
                 target[layer] == ShapeSignature.CrystalCode))
            {
                continue;
            }

            blocks++;

            while (layer + 1 < preState.Length &&
                   preState[layer + 1] == ShapeSignature.CrystalCode &&
                   (layer + 1 >= target.Length ||
                    target[layer + 1] != ShapeSignature.CrystalCode))
            {
                layer++;
            }
        }

        return blocks;
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

using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// docs/angle-work-order-general-algorithm.md §0.8 的 15 个已验证样例。
///
/// <para>
/// 每个样例都从目标角出发调用 <see cref="AngleWorkOrderBuilder.Build"/>，
/// 逐格核对：
/// </para>
/// <list type="number">
/// <item>工序完成（<see cref="AngleWorkOrder.IsComplete"/>），没有「未覆盖」说明；</item>
/// <item>前态每列合法、辅助列紧列（<see cref="AngleWorkOrder.HasIllegalPreState"/> 为 false）；</item>
/// <item>每个 Pin 步都等于对前一张图执行真实 <see cref="ShapePhysics.Pin"/>；</item>
/// <item>末步 <see cref="AngleWorkStepKind.Target"/> 与目标逐格相同
/// （类型 / 形状码 / 颜色码，不只是短代码字符串）。</item>
/// </list>
///
/// <para>
/// 步骤链本身允许论文四步新增的 <see cref="AngleWorkStepKind.WeakDrop"/>；
/// 15 个样例的最终目标列必须全部复现。
/// </para>
/// </summary>
internal static class FourStepAlgorithm6Tests
{
    private const int Parts = 4;

    public static void Docs08RegressionSamples()
    {
        var samples = new (string Target, string DocumentedA)[]
        {
            (".T.c", ".Tc."),
            (".T..T..c", ".TcccTc."),
            ("T..T...c", ".TcccTc."),
            ("T...cT.T.c", "..TcTccTc."),
            ("T..cT.T.c", ".TcTccTc."),
            ("P.T..c", "cccTc."),
            ("..T.cc", "..Tcc."),
            ("..T.cTc", "..TcTc."),
            ("P.T...c", "ccccTc."),
            ("P.T...c.", "ccccTc.."),
            ("..T..cTc", "...TcTc."),
            ("P.T.T..c", "ccTccTc."),
            ("PP.T.cTc", "ccTcTc.."),
            ("P.T.T..cP", "ccTccTc.."),
            ("T.T.cccTc", "TccTcccTc")
        };

        int completed = 0;
        int weakDrops = 0;

        foreach ((string text, string documentedA) in samples)
        {
            var target = new ShapeDocument(text.Length, Parts);

            for (int layer = 0; layer < text.Length; layer++)
            {
                target[layer, 0] = text[layer] switch
                {
                    'c' => ShapeCell.Crystal('r'),
                    'P' => ShapeCell.Pin(),
                    'T' => ShapeCell.Shape('C', 'u'),
                    _ => ShapeCell.Empty()
                };
            }

            AngleWorkOrder order = AngleWorkOrderBuilder.Build(
                target,
                0,
                text.Length);

            Assert.True(
                order.IsComplete,
                text + " 应完成：" + (order.Note ?? "?"));
            Assert.True(
                order.Note is null,
                text + " 不应有说明：" + order.Note);
            Assert.Equal(AngleBuildKind.NeedsCrystalBreak, order.Kind);
            Assert.False(
                order.HasIllegalPreState(out _, out _),
                text + " 的前态不应含非法列");

            // Pin 步必须等于对「上一张图」执行真实顶针；WeakDrop 是论文四步的恢复步。
            ShapeDocument? previous = null;

            foreach (AngleWorkStep step in order.Steps)
            {
                if (step.Kind == AngleWorkStepKind.Pin)
                {
                    Assert.True(previous is not null, text + " 的 Pin 前缺少图形");
                    Assert.True(
                        ShapeOps.Same(
                            ShapePhysics.Pin(previous!).Document,
                            step.Shape),
                        text + " 的 Pin 步不等于真实顶针结果");
                }

                if (step.Kind == AngleWorkStepKind.WeakDrop)
                {
                    weakDrops++;
                }

                if (step.Shape is not null)
                {
                    previous = step.Shape;
                }
            }

            AngleWorkStep targetStep = order.Steps.Last(
                step => step.Kind == AngleWorkStepKind.Target);

            Assert.True(
                targetStep.Shape is not null &&
                ShapeOps.Same(targetStep.Shape, target),
                text + " 的末步没有逐格等于目标（文档前态 A = " +
                documentedA + "）");

            Assert.Equal(
                text,
                ShapeSignature.ColumnText(targetStep.Shape!, 0),
                text + " 的目标列短代码应逐格复现");

            completed++;

            Console.WriteLine(
                $"  DOCS08 {text} algorithm={order.Algorithm} " +
                $"construction={order.Construction} pins={order.Rank} " +
                $"weak={order.Steps.Count(s => s.Kind == AngleWorkStepKind.WeakDrop)} " +
                $"documentedA={documentedA}");
        }

        Assert.Equal(15, completed, "§0.8 的 15 个样例都应复现");
        Assert.True(weakDrops > 0, "论文四步的样例应至少包含一个 WeakDrop 步");
    }
}

using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Tests;

/// <summary>
/// O-v2 收尾形态的测试辅助：<c>Target</c> 步统一为「左柱 + 目标列 + 右柱」后，
/// 验收只看目标列，不再要求整份 <see cref="AngleWorkStep.Shape"/> 等于目标图。
/// </summary>
internal static class AngleTestHelpers
{
    /// <summary>目标列逐格相等（类型 / 形状码 / 颜色），基于 <see cref="AngleWorkOrder.TargetColumnShape"/>。</summary>
    public static void AssertTargetColumn(
        AngleWorkOrder order,
        ShapeDocument target,
        string? message = null)
    {
        string prefix = message is null ? string.Empty : message + "：";

        Assert.True(
            TargetColumnMatches(order, target),
            prefix + "Target 的目标列应与目标逐格相等");
    }

    /// <summary>
    /// 目标列逐格比较，返回布尔值（审计用，不抛异常）。
    ///
    /// <para>
    /// 允许 cap 高于目标层数：多出来的层一律按空层比较。
    /// </para>
    /// </summary>
    public static bool TargetColumnMatches(
        AngleWorkOrder order,
        ShapeDocument target)
    {
        ShapeDocument? actual = order.TargetColumnShape;

        if (actual is null)
        {
            return false;
        }

        int layers = actual.LayerCount;

        string expectedText = ShapeSignature.ColumnText(target, order.Column);
        string actualText = ShapeSignature.ColumnText(actual, order.Column);

        if (actualText.Length < layers)
        {
            actualText = actualText.PadRight(layers, ShapeSignature.EmptyCode);
        }

        if (expectedText.Length < layers)
        {
            expectedText = expectedText.PadRight(layers, ShapeSignature.EmptyCode);
        }

        if (expectedText[..layers] != actualText[..layers])
        {
            return false;
        }

        for (int layer = 0; layer < layers; layer++)
        {
            ShapeCell expected = layer < target.LayerCount
                ? target[layer, order.Column]
                : ShapeCell.Empty();

            if (!ShapeOps.SameCell(expected, actual[layer, order.Column]))
            {
                return false;
            }
        }

        return true;
    }
}

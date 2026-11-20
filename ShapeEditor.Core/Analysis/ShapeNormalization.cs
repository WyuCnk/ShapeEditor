using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 朝向归一化：把「目标角 + 左右支撑柱」这三列旋到 Q1
/// （docs/work-order-card-hierarchy-plan.md §4 Q1 / §5 第 1 条）。
///
/// <para><b>为什么需要</b></para>
/// <para>
/// 角工序的收尾形态统一是「左柱 + 目标列 + 右柱」
/// （见 <see cref="AngleWorkOrder.TargetColumnShape"/> 与 <c>BuildUnifiedTargetShape</c>），
/// 但目标列落在它原本的 <c>column</c> 象限，UI 每张角卡朝向都不一样。
/// 卡片层要求「角卡产物固定 Q1」，所以 Core 提供一个纯函数把这三列整体旋转，
/// UI 不再自己转。
/// </para>
///
/// <para><b>旋转量</b></para>
/// <code>
/// k = (−column) mod n            （n = 象限数，k 为顺时针旋转量，与 ShapeOps.Rotate 一致）
/// 目标列 column      → 第 0 位（Q1）
/// 右柱   column + 1  → 第 1 位（Q2）
/// 左柱   column − 1  → 第 n − 1 位（Qn，n = 4 时是 Q4）
/// </code>
/// <para>
/// 逐格核对（类型 / 形状码 / 颜色）由调用方用
/// <see cref="PillarsAreGrounded"/> 与 <see cref="ShapeOps.Same"/> 完成；
/// 这里只做「整体环形移位」，不增删任何格子。
/// </para>
/// </summary>
public static class ShapeNormalization
{
    /// <summary>
    /// 把 <paramref name="column"/> 旋到 Q1 需要的顺时针旋转量（0 … n−1）。
    /// </summary>
    public static int Q1Rotation(
        int partCount,
        int column)
    {
        if (partCount < 2 || partCount % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partCount),
                partCount,
                "象限数必须是 ≥ 2 的偶数。");
        }

        if (column < 0 || column >= partCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                $"象限索引必须在 0 … {partCount - 1} 之间。");
        }

        return ShapeOps.Wrap(-column, partCount);
    }

    /// <summary>
    /// 把「左柱 + 目标列 + 右柱」整体旋到 Q1：目标列在 Q1、右柱在 Q2、左柱在 Qn。
    ///
    /// <para>
    /// 纯函数：不修改入参，返回新文档；层数与象限数保持不变。
    /// </para>
    /// </summary>
    public static ShapeDocument NormalizeToQ1(
        ShapeDocument shape,
        int column)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        return ShapeOps.Rotate(
            shape,
            Q1Rotation(shape.PartCount, column));
    }

    /// <summary>
    /// 归一化形态验收：目标列在 Q1，右柱 Q2、左柱 Qn 都<b>连续接地</b>，
    /// 且这两根柱子不高于目标列（柱高 = 目标列现有高度）。
    ///
    /// <para>
    /// <paramref name="shape"/> 必须是<b>已归一化</b>的三列形态：列 0 是目标列、
    /// 列 1 是右柱、列 n−1 是左柱；其余象限不属于产物，允许为空也允许有柱前残料
    /// （后续由构造 C 拆掉），这里只核对两根支撑柱本身。
    /// </para>
    /// </summary>
    /// <param name="shape">已归一化的形态。</param>
    /// <param name="height">目标列的现有高度（最高非空层 + 1）。</param>
    /// <param name="error">不合格时的原因。</param>
    public static bool PillarsAreGrounded(
        ShapeDocument shape,
        out int height,
        out string? error)
    {
        if (shape is null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        int parts = shape.PartCount;

        height = ColumnHeight(shape, 0);

        if (height == 0)
        {
            error = "目标列（Q1）全空，没有可以托住的目标角。";
            return false;
        }

        if (parts < 4)
        {
            error = $"n = {parts}：Q2 / Qn 是同一列，放不下左右两根支撑柱。";
            return false;
        }

        return CheckPillar(shape, 1, height, "右柱（Q2）", out error) &&
               CheckPillar(shape, parts - 1, height, $"左柱（Q{parts}）", out error);
    }

    /// <summary>某列的高度：最高非空层 + 1（整列为空时返回 0）。</summary>
    public static int ColumnHeight(
        ShapeDocument shape,
        int column)
    {
        int height = 0;

        for (int layer = 0; layer < shape.LayerCount; layer++)
        {
            if (shape[layer, column].Type != CellType.Empty)
            {
                height = layer + 1;
            }
        }

        return height;
    }

    /// <summary>
    /// 单根支撑柱验收：0 … height−1 层必须全是普通格（连续接地），
    /// height 层以上必须全空（柱不高于目标列）。
    /// </summary>
    private static bool CheckPillar(
        ShapeDocument shape,
        int column,
        int height,
        string label,
        out string? error)
    {
        for (int layer = 0; layer < height; layer++)
        {
            ShapeCell cell = shape[layer, column];

            if (cell.Type == CellType.Empty)
            {
                error = $"{label}第 {layer + 1} 层不是接地支撑柱（连续接地被破坏）。";
                return false;
            }

            if (cell.Type != CellType.Shape)
            {
                error = $"{label}第 {layer + 1} 层是 {cell.ToShortCode()}，" +
                        "不是普通格（支撑柱必须是普通形状）。";
                return false;
            }
        }

        for (int layer = height; layer < shape.LayerCount; layer++)
        {
            if (shape[layer, column].Type != CellType.Empty)
            {
                error = $"{label}第 {layer + 1} 层高过目标列（目标列高度 {height}）。";
                return false;
            }
        }

        error = null;
        return true;
    }
}

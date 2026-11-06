using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis.Production;

/// <summary>
/// 交换链：四个带柱半半边 → 交换得两个半边 → 交换得目标。
///
/// 规则（已在 n = 6 / 8 / 10 上逐格验证）：
/// <code>
/// P1 = 内容 [a, a+ls-1]            + 柱 @ a-1
/// P2 = 内容 [a+ls, a+half-1]       + 柱 @ a+half
/// P3 = 内容 [a+half, a+half+rs-1]  + 柱 @ a+half-1
/// P4 = 内容 [a+half+rs, a-1]       + 柱 @ a
///
/// 预旋转：P1、P2 顺转 (half - ls)；P3、P4 逆转 rs
/// 交换后回旋：左半边逆转 (half - ls)；右半边顺转 rs
/// 末次交换后与目标「旋转等价」即成功。
/// </code>
/// 刀口固定 n/2，交换语义见 <see cref="ShapeExchange"/>。
/// </summary>
public static class ExchangeChain
{
    /// <summary>遍历全部 (轴, 左切点, 右切点)，找不到返回 false。</summary>
    public static bool TryBuild(
        ShapeDocument target,
        out Recipe? recipe)
    {
        int half = target.PartCount / 2;

        if (target.PartCount % 2 != 0 || half < 2)
        {
            recipe = null;
            return false;
        }

        for (int axis = 0; axis < half; axis++)
        {
            for (int leftSplit = 1;
                 leftSplit < half;
                 leftSplit++)
            {
                for (int rightSplit = 1;
                     rightSplit < half;
                     rightSplit++)
                {
                    if (TryBuild(
                            target,
                            axis,
                            leftSplit,
                            rightSplit,
                            out recipe))
                    {
                        return true;
                    }
                }
            }
        }

        recipe = null;
        return false;
    }

    /// <summary>按指定方向构造，失败返回 false。</summary>
    public static bool TryBuild(
        ShapeDocument target,
        int axis,
        int leftSplit,
        int rightSplit,
        out Recipe? recipe)
    {
        recipe = null;

        int n = target.PartCount;
        int half = n / 2;

        if (n % 2 != 0 || half < 2)
        {
            return false;
        }

        if (leftSplit < 1 || leftSplit > half - 1 ||
            rightSplit < 1 || rightSplit > half - 1)
        {
            return false;
        }

        // 把轴转到 0，简化列号计算
        ShapeDocument shifted =
            ShapeOps.Rotate(target, -axis);

        ShapeDocument p1 = PillarPiece(shifted, 0, leftSplit, -1);
        ShapeDocument p2 = PillarPiece(shifted, leftSplit, half - leftSplit, half);
        ShapeDocument p3 = PillarPiece(shifted, half, rightSplit, half - 1);
        ShapeDocument p4 = PillarPiece(shifted, half + rightSplit, half - rightSplit, 0);

        foreach (ShapeDocument piece in new[] { p1, p2, p3, p4 })
        {
            if (!ShapeOps.IsStable(piece))
            {
                return false;
            }
        }

        int leftShift = half - leftSplit;

        ShapeDocument p1r = ShapeOps.Rotate(p1, leftShift);
        ShapeDocument p2r = ShapeOps.Rotate(p2, leftShift);
        ShapeDocument p3r = ShapeOps.Rotate(p3, -rightSplit);
        ShapeDocument p4r = ShapeOps.Rotate(p4, -rightSplit);

        (ShapeDocument? firstA, ShapeDocument? firstB, _) =
            ShapeExchange.Exchange(p1r, p2r);

        (ShapeDocument? secondA, ShapeDocument? secondB, _) =
            ShapeExchange.Exchange(p3r, p4r);

        if (firstB is null || secondB is null ||
            firstA is null || secondA is null)
        {
            return false;
        }

        ShapeDocument leftHalf =
            ShapeOps.Rotate(firstB, -leftShift);

        ShapeDocument rightHalf =
            ShapeOps.Rotate(secondB, rightSplit);

        if (!ShapeOps.IsStable(leftHalf) ||
            !ShapeOps.IsStable(rightHalf))
        {
            return false;
        }

        (ShapeDocument? waste, ShapeDocument? output, _) =
            ShapeExchange.Exchange(leftHalf, rightHalf);

        if (waste is null || output is null)
        {
            return false;
        }

        ShapeDocument? matched = null;

        if (IsEquivalent(output, target))
        {
            matched = output;
        }
        else if (IsEquivalent(waste, target))
        {
            matched = waste;
        }

        if (matched is null)
        {
            return false;
        }

        ShapeDocument? finalWaste =
            matched == output ? waste : output;

        var steps = new List<RecipeStep>
        {
            new("制作左半边零件 1（含柱）", p1r),
            new("制作左半边零件 2（含柱）", p2r),
            new("交换得到左半边", leftHalf),
            new("制作右半边零件 1（含柱）", p3r),
            new("制作右半边零件 2（含柱）", p4r),
            new("交换得到右半边", rightHalf),
            new("交换得到目标图形", matched),
            new("废料", finalWaste!, true)
        };

        var details = new ExchangeDetails(
            axis,
            leftSplit,
            rightSplit,
            new ExchangePiece(p1, p1r),
            new ExchangePiece(p2, p2r),
            new ExchangePiece(p3, p3r),
            new ExchangePiece(p4, p4r),
            new ExchangeHalf(firstB, leftHalf, firstA),
            new ExchangeHalf(secondB, rightHalf, secondA),
            matched,
            finalWaste);

        recipe = Recipe.CreateExchange(matched, steps, details);
        return true;
    }

    /// <summary>
    /// 只保留从 from 开始的 count 个象限，再额外补一根满层柱子。
    /// </summary>
    private static ShapeDocument PillarPiece(
        ShapeDocument source,
        int from,
        int count,
        int pillar)
    {
        ShapeDocument piece =
            ShapeOps.Project(source, from, count);

        ShapeOps.SetPillar(piece, pillar);

        return piece;
    }

    /// <summary>两个图形是否「旋转等价」。</summary>
    public static bool IsEquivalent(
        ShapeDocument left,
        ShapeDocument right)
    {
        for (int k = 0;
             k < left.PartCount;
             k++)
        {
            if (ShapeOps.Same(
                    ShapeOps.Rotate(left, k),
                    right))
            {
                return true;
            }
        }

        return false;
    }
}

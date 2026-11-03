using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 制作路径的一步。
/// </summary>
public sealed record ProductionStep(
    string Title,
    ShapeDocument? Shape);

/// <summary>
/// 一条制作路径。
/// </summary>
public sealed record ProductionPath(
    int Axis,
    int LeftSplit,
    int RightSplit,
    string Direction,
    bool DirectionLegal,
    IReadOnlyList<ProductionStep> Steps);

/// <summary>
/// 制作路径解析结果。
/// </summary>
public sealed record ProductionPathResult(
    bool IsProducible,
    ProductionPath? Path,
    string? Error);

/// <summary>
/// 制作路径解析（交换阶段，规格 §5）。
///
/// 工序：四个带柱子的半半边 → 两次同侧交换得到两个半边 → 末端交换得到目标图形 → 顶针与堆叠。
///
/// 关键规则：
/// 1. 四片各只有一根柱子（<see cref="PillarHalfHalves"/>，规格 §1）；
/// 2. P1/P2 顺转 half−ls、P3/P4 逆转 rs 后再交换（规格 §2）；
/// 3. Cut 固定切在 n/2，交叉拼接 out1 = 东+西、out2 = 西+东（规格 §3）；
/// 4. 前两次交换后要转回来，末端交换不再旋转；
/// 5. 末端两个输出中任意一个与目标旋转等价即成功（规格 §4）。
/// </summary>
public static class ProductionPathParser
{
    public static ProductionPathResult Parse(
        ShapeDocument target,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        int parts = target.PartCount;

        if (parts % 2 != 0)
        {
            return new ProductionPathResult(false, null, "象限数必须为偶数。");
        }

        int half = parts / 2;

        if (half < 2)
        {
            return new ProductionPathResult(false, null, "象限数太少，无法解析制作路径。");
        }

        // 硬否决：目标存在不合法角即不可制作。
        ShapeAnalysisResult angles = ShapeAngleAnalyzer.Analyze(target, mode);

        if (!angles.IsFullyLegal)
        {
            return new ProductionPathResult(false, null, "目标图形存在不合法角，无法制作。");
        }

        // 轴可被最终的旋转等价吸收，固定为 0，只需遍历左右切点。
        const int axis = 0;

        // 稳定性在旋转下不变；一次解析内的重复半半边/半边只塌陷一次。
        var cache = new StabilityCache(mode);

        for (int leftSplit = 1; leftSplit < half; leftSplit++)
        {
            for (int rightSplit = 1; rightSplit < half; rightSplit++)
            {
                (ShapeDocument p1, ShapeDocument p2, ShapeDocument p3, ShapeDocument p4) =
                    BuildPillarHalfHalves(target, axis, leftSplit, rightSplit);

                // Cut 会各自 Collapse 一次，四片必须稳定；
                // 任一片不稳定就直接跳过该方向，不进入交换阶段。
                // 稳定性与旋转无关，因此这里判定未旋转的片（起始列见 BuildPillarHalfHalves）。
                if (!IsStable(p1, cache, axis) ||
                    !IsStable(p2, cache, axis + leftSplit) ||
                    !IsStable(p3, cache, axis + half) ||
                    !IsStable(p4, cache, axis + half + rightSplit))
                {
                    continue;
                }

                // 通过稳定性判定后，再做规格 §2 的预旋转。
                int leftRotation = half - leftSplit;

                p1 = PillarHalfHalves.RotateRight(p1, leftRotation);
                p2 = PillarHalfHalves.RotateRight(p2, leftRotation);
                p3 = PillarHalfHalves.RotateRight(p3, -rightSplit);
                p4 = PillarHalfHalves.RotateRight(p4, -rightSplit);

                // 同侧配对交换，再按 §2 转回。
                ShapeDocument? leftHalf = ExchangeAndRotate(
                    p1, p2, -leftRotation, mode);

                if (leftHalf is null || !IsStable(leftHalf, cache, 0))
                {
                    continue;
                }

                ShapeDocument? rightHalf = ExchangeAndRotate(
                    p3, p4, rightSplit, mode);

                if (rightHalf is null || !IsStable(rightHalf, cache, half))
                {
                    continue;
                }

                // 末端交换：两个输出中任意一个与目标旋转等价即成功。
                var final = ShapeExchange.Exchange(leftHalf, rightHalf, mode);

                if (final.Error is not null)
                {
                    continue;
                }

                ShapeDocument? product = null;

                if (final.First is not null &&
                    IsRotationEquivalent(final.First, target))
                {
                    product = final.First;
                }
                else if (final.Second is not null &&
                         IsRotationEquivalent(final.Second, target))
                {
                    product = final.Second;
                }

                if (product is null)
                {
                    continue;
                }

                string direction = Display(
                    axis,
                    half,
                    leftSplit,
                    rightSplit);

                // 方向合法性沿用半边分析的结论（两个半边 + 四个带柱半半边都稳定）。
                HalfStabilityResult halfResult = HalfStabilityAnalyzer.Analyze(target, mode);

                var steps = new List<ProductionStep>
                {
                    new ProductionStep("制作带柱子的半半边 P1", p1),
                    new ProductionStep("制作带柱子的半半边 P2", p2),
                    new ProductionStep("制作带柱子的半半边 P3", p3),
                    new ProductionStep("制作带柱子的半半边 P4", p4),
                    new ProductionStep("交换得到左半边", leftHalf),
                    new ProductionStep("交换得到右半边", rightHalf),
                    new ProductionStep("交换得到目标图形", product),
                    new ProductionStep("顶针与堆叠（待接入）", null)
                };

                return new ProductionPathResult(
                    true,
                    new ProductionPath(
                        axis,
                        leftSplit,
                        rightSplit,
                        direction,
                        halfResult.LegalDirections.Contains(direction),
                        steps),
                    null);
            }
        }

        return new ProductionPathResult(false, null, "没有找到可行的制作路径。");
    }

    /// <summary>
    /// 按规格 §1 构造四个带柱子的半半边（未做 §2 的预旋转）。
    ///
    /// 返回的片保持构造时的列位置，起始列分别为：
    /// P1 = 轴、P2 = 轴 + 左切点、P3 = 轴 + half、P4 = 轴 + half + 右切点。
    /// 稳定性与旋转无关，缓存键用这些起始列做旋转归一化；
    /// 通过判定后再由调用方按 §2 预旋转。
    /// </summary>
    private static (ShapeDocument P1, ShapeDocument P2, ShapeDocument P3, ShapeDocument P4)
        BuildPillarHalfHalves(
            ShapeDocument target,
            int axis,
            int leftSplit,
            int rightSplit)
    {
        ShapeDocument p1 = PillarHalfHalves.BuildPiece(
            target, axis, leftSplit, rightSplit, PillarHalfHalves.First);

        ShapeDocument p2 = PillarHalfHalves.BuildPiece(
            target, axis, leftSplit, rightSplit, PillarHalfHalves.Second);

        ShapeDocument p3 = PillarHalfHalves.BuildPiece(
            target, axis, leftSplit, rightSplit, PillarHalfHalves.Third);

        ShapeDocument p4 = PillarHalfHalves.BuildPiece(
            target, axis, leftSplit, rightSplit, PillarHalfHalves.Fourth);

        return (p1, p2, p3, p4);
    }

    /// <summary>
    /// 同侧两片交换后转回：取 out2 = Merge(A 西, B 东) 作为需要的半边。
    /// 规格 §5 中左半边回转 −(half − ls)，右半边回转 +rs。
    /// </summary>
    private static ShapeDocument? ExchangeAndRotate(
        ShapeDocument first,
        ShapeDocument second,
        int rotation,
        CollapseMode mode)
    {
        var result = ShapeExchange.Exchange(first, second, mode);

        // ShapeExchange 的 Second 即规格里的 out2 = Merge(A 西, B 东)。
        if (result.Error is not null || result.Second is null)
        {
            return null;
        }

        return PillarHalfHalves.RotateRight(result.Second, rotation);
    }

    /// <summary>交换输出是否与目标旋转等价。</summary>
    private static bool IsRotationEquivalent(
        ShapeDocument candidate,
        ShapeDocument target)
    {
        if (candidate.LayerCount != target.LayerCount ||
            candidate.PartCount != target.PartCount)
        {
            return false;
        }

        int parts = candidate.PartCount;

        for (int rotation = 0; rotation < parts; rotation++)
        {
            bool same = true;

            for (int layer = 0; layer < candidate.LayerCount && same; layer++)
            {
                for (int part = 0; part < parts; part++)
                {
                    // RotateRight(candidate, rotation) 的第 part 列取自原第 part−rotation 列。
                    int source = ((part - rotation) % parts + parts) % parts;

                    if (!CellEquals(candidate[layer, source], target[layer, part]))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (same)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CellEquals(
        ShapeCell left,
        ShapeCell right)
    {
        return left.Type == right.Type &&
               left.ShapeCode == right.ShapeCode &&
               left.ColorCode == right.ColorCode;
    }

    /// <summary>
    /// 稳定性判定，走本次解析共享的缓存。
    /// <paramref name="contentStart"/> 是该片的起始列，用于旋转归一化缓存键。
    /// </summary>
    private static bool IsStable(
        ShapeDocument shape,
        StabilityCache cache,
        int contentStart)
    {
        return cache.IsStable(shape, contentStart);
    }

    private static string Display(
        int axis,
        int half,
        int leftSplit,
        int rightSplit)
    {
        int parts = half * 2;

        string leftText = Numbers(axis, leftSplit, parts) +
                          "+" +
                          Numbers(axis + leftSplit, half - leftSplit, parts);

        string rightText = Numbers(axis + half, rightSplit, parts) +
                           "+" +
                           Numbers(axis + half + rightSplit, half - rightSplit, parts);

        return $"({leftText})+({rightText})";

        static string Numbers(int start, int count, int parts)
        {
            return string.Join("", Enumerable.Range(0, count)
                .Select(index => ((start + index) % parts + 1).ToString()));
        }
    }
}

using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 一条制作路径。
///
/// <see cref="Steps"/> 是向后兼容的线性工序（WPF / MAUI 仍在使用），
/// <see cref="Graph"/> 是按层组织的节点图：交换的两个输出（所需结果与废料）
/// 都作为节点出现，Web 端据此画分层箭头图。
/// </summary>
public sealed record ProductionPath(
    int Axis,
    int LeftSplit,
    int RightSplit,
    string Direction,
    bool DirectionLegal,
    IReadOnlyList<ProductionStep> Steps,
    ProductionGraph Graph)
{
    /// <summary>节点图的节点，按行号与行内顺序排列。</summary>
    public IReadOnlyList<ProductionNode> Nodes => Graph.Nodes;

    /// <summary>节点图的箭头。</summary>
    public IReadOnlyList<ProductionEdge> Edges => Graph.Edges;
}

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
///
/// <see cref="ProductionPath.Steps"/> 按实际计算顺序展开：制作片、预旋转、
/// 交换、交换后转回、半边结果、末端交换，直到顶针与堆叠。
/// 每步的 <see cref="ProductionStep.Shape"/> 是该步产出的图形，UI 可直接画预览。
///
/// <see cref="ProductionPath.Graph"/> 把同一条工序整理成 5 层节点图：
/// 1 = 四个带柱子的半半边，2 = 预旋转结果，3 = 两两交换（两个半边 + 两个废料），
/// 4 = 交换后转回的两个半边，5 = 末端交换的目标图形 + 废料。
/// 交换的另一路输出不再丢弃，而是带图形的「废料」节点。
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
                (ShapeDocument p1Raw, ShapeDocument p2Raw, ShapeDocument p3Raw, ShapeDocument p4Raw) =
                    BuildPillarHalfHalves(target, axis, leftSplit, rightSplit);

                // Cut 会各自 Collapse 一次，四片必须稳定；
                // 任一片不稳定就直接跳过该方向，不进入交换阶段。
                // 稳定性与旋转无关，因此这里判定未旋转的片（起始列见 BuildPillarHalfHalves）。
                if (!IsStable(p1Raw, cache, axis) ||
                    !IsStable(p2Raw, cache, axis + leftSplit) ||
                    !IsStable(p3Raw, cache, axis + half) ||
                    !IsStable(p4Raw, cache, axis + half + rightSplit))
                {
                    continue;
                }

                // 通过稳定性判定后，再做规格 §2 的预旋转。
                int leftRotation = half - leftSplit;

                ShapeDocument p1 = PillarHalfHalves.RotateRight(p1Raw, leftRotation);
                ShapeDocument p2 = PillarHalfHalves.RotateRight(p2Raw, leftRotation);
                ShapeDocument p3 = PillarHalfHalves.RotateRight(p3Raw, -rightSplit);
                ShapeDocument p4 = PillarHalfHalves.RotateRight(p4Raw, -rightSplit);

                // 同侧配对交换，再按 §2 转回。交换的另一路输出保留为废料节点。
                (ShapeDocument? leftRaw, ShapeDocument? leftHalf, ShapeDocument? leftWaste) =
                    ExchangeAndRotate(p1, p2, -leftRotation, mode);

                if (leftRaw is null ||
                    leftHalf is null ||
                    leftWaste is null ||
                    !IsStable(leftHalf, cache, 0))
                {
                    continue;
                }

                (ShapeDocument? rightRaw, ShapeDocument? rightHalf, ShapeDocument? rightWaste) =
                    ExchangeAndRotate(p3, p4, rightSplit, mode);

                if (rightRaw is null ||
                    rightHalf is null ||
                    rightWaste is null ||
                    !IsStable(rightHalf, cache, half))
                {
                    continue;
                }

                // 末端交换：两个输出中任意一个与目标旋转等价即成功，另一个是废料。
                var final = ShapeExchange.Exchange(leftHalf, rightHalf, mode);

                if (final.Error is not null)
                {
                    continue;
                }

                ShapeDocument? product = null;

                ShapeDocument? finalWaste = null;

                if (final.First is not null &&
                    IsRotationEquivalent(final.First, target))
                {
                    product = final.First;
                    finalWaste = final.Second;
                }
                else if (final.Second is not null &&
                         IsRotationEquivalent(final.Second, target))
                {
                    product = final.Second;
                    finalWaste = final.First;
                }

                if (product is null)
                {
                    continue;
                }

                string direction = PillarHalfHalves.FormatDirection(
                    axis,
                    half,
                    leftSplit,
                    rightSplit);

                // 方向合法性沿用半边分析的结论（两个半边 + 四个带柱半半边都稳定）。
                HalfStabilityResult halfResult = HalfStabilityAnalyzer.Analyze(target, mode);

                List<ProductionStep> steps = BuildSteps(
                    parts,
                    leftRotation,
                    rightSplit,
                    p1Raw,
                    p1,
                    p2Raw,
                    p2,
                    leftRaw,
                    leftHalf,
                    p3Raw,
                    p3,
                    p4Raw,
                    p4,
                    rightRaw,
                    rightHalf,
                    product);

                ProductionGraph graph = BuildGraph(
                    parts,
                    leftRotation,
                    rightSplit,
                    p1Raw,
                    p1,
                    p2Raw,
                    p2,
                    leftRaw,
                    leftHalf,
                    leftWaste,
                    p3Raw,
                    p3,
                    p4Raw,
                    p4,
                    rightRaw,
                    rightHalf,
                    rightWaste,
                    product,
                    finalWaste);

                return new ProductionPathResult(
                    true,
                    new ProductionPath(
                        axis,
                        leftSplit,
                        rightSplit,
                        direction,
                        halfResult.LegalDirections.Contains(direction),
                        steps,
                        graph),
                    null);
            }
        }

        return new ProductionPathResult(false, null, "没有找到可行的制作路径。");
    }

    /// <summary>
    /// 按实际计算顺序展开步骤。
    ///
    /// 每一步都带上产出图形，界面据此画节点卡片；旋转步骤同时记录
    /// 方向与数量（已规范成较短方向），交换后转回也作为独立旋转步骤出现。
    /// </summary>
    private static List<ProductionStep> BuildSteps(
        int parts,
        int leftRotation,
        int rightRotation,
        ShapeDocument p1Raw,
        ShapeDocument p1,
        ShapeDocument p2Raw,
        ShapeDocument p2,
        ShapeDocument leftRaw,
        ShapeDocument leftHalf,
        ShapeDocument p3Raw,
        ShapeDocument p3,
        ShapeDocument p4Raw,
        ShapeDocument p4,
        ShapeDocument rightRaw,
        ShapeDocument rightHalf,
        ShapeDocument product)
    {
        (RotationDirection leftDirection, int leftCount, string leftTitle) =
            PillarHalfHalves.DescribeRotation(parts, leftRotation);

        (RotationDirection rightDirection, int rightCount, string rightTitle) =
            PillarHalfHalves.DescribeRotation(parts, -rightRotation);

        (RotationDirection leftBackDirection, int leftBackCount, string leftBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, -leftRotation);

        (RotationDirection rightBackDirection, int rightBackCount, string rightBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, rightRotation);

        return new List<ProductionStep>
        {
            new("制作带柱子的半半边 P1", p1Raw, ProductionStepKind.PillarPiece, RotationDirection.None, 0),
            new(leftTitle, p1, ProductionStepKind.Rotate, leftDirection, leftCount),
            new("制作带柱子的半半边 P2", p2Raw, ProductionStepKind.PillarPiece, RotationDirection.None, 0),
            new(leftTitle, p2, ProductionStepKind.Rotate, leftDirection, leftCount),
            new("交换", leftRaw, ProductionStepKind.Exchange, RotationDirection.None, 0),
            new(leftBackTitle, leftHalf, ProductionStepKind.Rotate, leftBackDirection, leftBackCount),
            new("交换得到左半边", leftHalf, ProductionStepKind.Exchange, RotationDirection.None, 0),

            new("制作带柱子的半半边 P3", p3Raw, ProductionStepKind.PillarPiece, RotationDirection.None, 0),
            new(rightTitle, p3, ProductionStepKind.Rotate, rightDirection, rightCount),
            new("制作带柱子的半半边 P4", p4Raw, ProductionStepKind.PillarPiece, RotationDirection.None, 0),
            new(rightTitle, p4, ProductionStepKind.Rotate, rightDirection, rightCount),
            new("交换", rightRaw, ProductionStepKind.Exchange, RotationDirection.None, 0),
            new(rightBackTitle, rightHalf, ProductionStepKind.Rotate, rightBackDirection, rightBackCount),
            new("交换得到右半边", rightHalf, ProductionStepKind.Exchange, RotationDirection.None, 0),

            new("交换得到目标图形", product, ProductionStepKind.Target, RotationDirection.None, 0),
            new("顶针与堆叠（待接入）", null, ProductionStepKind.Pending, RotationDirection.None, 0)
        };
    }

    /// <summary>
    /// 把同一次计算整理成 5 层节点图。
    ///
    /// 行内顺序即 Web 端的横向排布顺序：
    /// 第 3 行按「废料、左交换、右交换、废料」排列，让 P1/P2、P3/P4
    /// 分别向中间的两个交换节点汇聚，废料落在两侧。
    ///
    /// 连接规则：
    /// P1–P4 → 各自的旋转结果；P1/P2 → 左交换，P3/P4 → 右交换；
    /// 左/右交换 → 转回后的半边，同时各引出一条废料边；
    /// 两个转回后半边 → 目标图形，目标图形再引出末端废料。
    /// </summary>
    private static ProductionGraph BuildGraph(
        int parts,
        int leftRotation,
        int rightRotation,
        ShapeDocument p1Raw,
        ShapeDocument p1,
        ShapeDocument p2Raw,
        ShapeDocument p2,
        ShapeDocument leftRaw,
        ShapeDocument leftHalf,
        ShapeDocument leftWaste,
        ShapeDocument p3Raw,
        ShapeDocument p3,
        ShapeDocument p4Raw,
        ShapeDocument p4,
        ShapeDocument rightRaw,
        ShapeDocument rightHalf,
        ShapeDocument rightWaste,
        ShapeDocument product,
        ShapeDocument? finalWaste)
    {
        (_, _, string leftTitle) =
            PillarHalfHalves.DescribeRotation(parts, leftRotation);

        (_, _, string rightTitle) =
            PillarHalfHalves.DescribeRotation(parts, -rightRotation);

        (_, _, string leftBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, -leftRotation);

        (_, _, string rightBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, rightRotation);

        var nodes = new List<ProductionNode>(16)
        {
            // 第 1 行：四个带柱子的半半边（未旋转）。
            new("pillar-p1", "制作带柱子的半半边 P1", ProductionNodeKind.PillarPiece, 1, p1Raw),
            new("pillar-p2", "制作带柱子的半半边 P2", ProductionNodeKind.PillarPiece, 1, p2Raw),
            new("pillar-p3", "制作带柱子的半半边 P3", ProductionNodeKind.PillarPiece, 1, p3Raw),
            new("pillar-p4", "制作带柱子的半半边 P4", ProductionNodeKind.PillarPiece, 1, p4Raw),

            // 第 2 行：预旋转结果。
            new("rotated-p1", leftTitle, ProductionNodeKind.Rotate, 2, p1),
            new("rotated-p2", leftTitle, ProductionNodeKind.Rotate, 2, p2),
            new("rotated-p3", rightTitle, ProductionNodeKind.Rotate, 2, p3),
            new("rotated-p4", rightTitle, ProductionNodeKind.Rotate, 2, p4),

            // 第 3 行：两次同侧交换。交换节点展示所需半边（转回前），
            // 另一路输出作为废料节点，同样带图形。
            new("waste-left", "废料", ProductionNodeKind.Waste, 3, leftWaste),
            new("exchange-left", "交换得到左半边", ProductionNodeKind.Exchange, 3, leftRaw),
            new("exchange-right", "交换得到右半边", ProductionNodeKind.Exchange, 3, rightRaw),
            new("waste-right", "废料", ProductionNodeKind.Waste, 3, rightWaste),

            // 第 4 行：交换后转回的两个半边。
            new("rotated-left", leftBackTitle, ProductionNodeKind.Rotate, 4, leftHalf),
            new("rotated-right", rightBackTitle, ProductionNodeKind.Rotate, 4, rightHalf),

            // 第 5 行：末端交换的目标图形与废料。
            new("target", "交换得到目标图形", ProductionNodeKind.Target, 5, product),
            new("waste-final", "废料", ProductionNodeKind.Waste, 5, finalWaste)
        };

        var edges = new List<ProductionEdge>(15)
        {
            // 第 1 → 2 行：竖线。
            new("pillar-p1", "rotated-p1"),
            new("pillar-p2", "rotated-p2"),
            new("pillar-p3", "rotated-p3"),
            new("pillar-p4", "rotated-p4"),

            // 第 2 → 3 行：P1/P2 汇入左交换，P3/P4 汇入右交换。
            new("rotated-p1", "exchange-left"),
            new("rotated-p2", "exchange-left"),
            new("rotated-p3", "exchange-right"),
            new("rotated-p4", "exchange-right"),

            // 交换节点输出所需半边与废料。
            new("exchange-left", "rotated-left"),
            new("exchange-left", "waste-left"),
            new("exchange-right", "rotated-right"),
            new("exchange-right", "waste-right"),

            // 第 4 → 5 行：两个转回后的半边汇入末端交换。
            new("rotated-left", "target"),
            new("rotated-right", "target"),

            // 末端交换的废料。
            new("target", "waste-final")
        };

        return new ProductionGraph(nodes, edges);
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
    /// 同侧两片交换后再转回。
    ///
    /// 返回交换的原始输出（out2 = Merge(A 西, B 东)）、转回后的半边，
    /// 以及交换的另一路输出（out1）——它作为「废料」节点出现在图里。
    /// </summary>
    private static (ShapeDocument? Raw, ShapeDocument? Rotated, ShapeDocument? Waste) ExchangeAndRotate(
        ShapeDocument first,
        ShapeDocument second,
        int rotation,
        CollapseMode mode)
    {
        var result = ShapeExchange.Exchange(first, second, mode);

        // ShapeExchange 的 Second 即规格里的 out2 = Merge(A 西, B 东)。
        if (result.Error is not null || result.Second is null)
        {
            return (null, null, null);
        }

        ShapeDocument raw = result.Second;

        return (raw, PillarHalfHalves.RotateRight(raw, rotation), result.First);
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
}

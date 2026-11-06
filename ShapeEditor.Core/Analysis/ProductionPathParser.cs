using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 一条制作路径。
///
/// <see cref="Steps"/> 是线性工序（WPF / MAUI 仍在使用），
/// <see cref="Graph"/> 是按层组织的节点图：交换的两个输出（所需结果与废料）、
/// 顶针与堆叠都作为节点出现，三端据此画分层箭头图。
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
///
/// <see cref="Outcome"/> 区分「不可制作」与「未覆盖」：
/// 前者只用于存在不合法角，后者表示求解器没找到路径但也没有否定。
/// </summary>
public sealed record ProductionPathResult(
    bool IsProducible,
    ProductionPath? Path,
    string? Error)
{
    /// <summary>求解结论；旧调用方可以只看 <see cref="IsProducible"/>。</summary>
    public PipelineOutcome? Outcome { get; init; }
}

/// <summary>
/// 制作路径解析：内部完全由 <see cref="ProductionPipeline"/> 驱动。
///
/// <code>
/// Parse(target)
///   → ProductionPipeline.Solve(target, cap = target.LayerCount, rank ≤ 3)
///   → 把 Recipe 树摊平成节点链：
///       交换叶子 = 四个带柱半半边 / 预旋转 / 两次交换 / 转回 / 末次交换 + 废料
///       顶针     = 内层节点链 + 顶针节点
///       堆叠     = 内层节点链 + 可制作块 + 堆叠节点
///   → ProductionPath { Steps, Graph, 方向 }
/// </code>
///
/// 结论分级：不合法角 ⇒ 不可制作；其余失败 ⇒ 未覆盖。
/// 旧骨架里的「固定轴 0、逐格精确比对、手工预旋转」已整段删除。
///
/// <para>
/// 规格 §5 的穷举数字是在 <see cref="CollapseMode.IncludeCycles"/> 下得到的，
/// 所以 pipeline 内部固定使用 IncludeCycles；<c>mode</c> 目前只用于方向合法性表格
/// （<see cref="HalfStabilityAnalyzer"/>）。循环支撑结构不在本次构造范围内。
/// </para>
/// </summary>
public static class ProductionPathParser
{
    public static ProductionPathResult Parse(
        ShapeDocument target,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        if (target is null)
        {
            return new ProductionPathResult(
                false,
                null,
                "图形为空。")
            {
                Outcome = PipelineOutcome.NotProducible
            };
        }

        int parts = target.PartCount;

        if (parts % 2 != 0)
        {
            return new ProductionPathResult(
                false,
                null,
                "象限数必须为偶数。")
            {
                Outcome = PipelineOutcome.NotProducible
            };
        }

        int half = parts / 2;

        if (half < 2)
        {
            /*
             * n = 2 的孤立列拼装（规格 §5.2）还没有接进 pipeline：
             * 这里必须报「未覆盖」，不能写成「不可制作」。
             */
            return new ProductionPathResult(
                false,
                null,
                "n = 2 的孤立列拼装尚未覆盖（未覆盖，不是不可制作）。")
            {
                Outcome = PipelineOutcome.Uncovered
            };
        }

        PipelineResult solved = ProductionPipeline.Solve(
            target,
            target.LayerCount,
            ProductionPipeline.DefaultMaxRank);

        if (solved.Outcome == PipelineOutcome.NotProducible)
        {
            return new ProductionPathResult(
                false,
                null,
                JoinNotes(solved.Notes))
            {
                Outcome = PipelineOutcome.NotProducible
            };
        }

        if (solved.Outcome != PipelineOutcome.Producible ||
            solved.Recipe is null)
        {
            return new ProductionPathResult(
                false,
                null,
                "未覆盖（没有证明不可制作）：" +
                JoinNotes(solved.Notes))
            {
                Outcome = PipelineOutcome.Uncovered
            };
        }

        Recipe recipe = solved.Recipe;

        Recipe? exchange = FindExchange(recipe);

        if (exchange?.Exchange is null)
        {
            return new ProductionPathResult(
                false,
                null,
                "未覆盖（路径里没有可展示的交换链）。")
            {
                Outcome = PipelineOutcome.Uncovered
            };
        }

        ExchangeDetails details = exchange.Exchange;

        int directionParts = exchange.Shape.PartCount;
        int directionHalf = directionParts / 2;

        string direction = PillarHalfHalves.FormatDirection(
            details.Axis,
            directionHalf,
            details.LeftSplit,
            details.RightSplit);

        bool directionLegal = HalfStabilityAnalyzer
            .Analyze(exchange.Shape, mode)
            .LegalDirections
            .Contains(direction);

        List<ProductionStep> steps = BuildSteps(recipe, isRoot: true);

        ProductionGraph graph = BuildGraph(recipe, isRoot: true);

        return new ProductionPathResult(
            true,
            new ProductionPath(
                details.Axis,
                details.LeftSplit,
                details.RightSplit,
                direction,
                directionLegal,
                steps,
                graph),
            null)
        {
            Outcome = PipelineOutcome.Producible
        };
    }

    private static string JoinNotes(
        IReadOnlyList<string> notes)
    {
        return notes.Count == 0
            ? "制作路径求解失败。"
            : string.Join(" ", notes);
    }

    /// <summary>找到路径树里唯一的交换叶子。</summary>
    private static Recipe? FindExchange(
        Recipe recipe)
    {
        if (recipe.Kind == RecipeKind.Exchange)
        {
            return recipe;
        }

        foreach (Recipe input in recipe.Inputs)
        {
            Recipe? found = FindExchange(input);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    #region 线性工序

    /// <summary>
    /// 把 Recipe 树按「先子后父」摊平成线性工序。
    /// </summary>
    private static List<ProductionStep> BuildSteps(
        Recipe recipe,
        bool isRoot)
    {
        var steps = new List<ProductionStep>();

        AppendSteps(recipe, isRoot, steps);

        return steps;
    }

    private static void AppendSteps(
        Recipe recipe,
        bool isRoot,
        List<ProductionStep> steps)
    {
        switch (recipe.Kind)
        {
            case RecipeKind.Exchange:
                if (recipe.Exchange is not null)
                {
                    AppendExchangeSteps(
                        recipe.Exchange,
                        isRoot,
                        steps);
                }
                else
                {
                    steps.Add(new ProductionStep(
                        isRoot ? "交换得到目标图形" : "交换得到中间图形",
                        recipe.Shape,
                        isRoot
                            ? ProductionStepKind.Target
                            : ProductionStepKind.Exchange,
                        RotationDirection.None,
                        0));
                }

                break;

            case RecipeKind.Pin:
                if (recipe.Inputs.Count == 1)
                {
                    AppendSteps(recipe.Inputs[0], false, steps);
                }

                steps.Add(new ProductionStep(
                    "顶针",
                    recipe.Shape,
                    ProductionStepKind.Pin,
                    RotationDirection.None,
                    0));

                break;

            case RecipeKind.Stack:
                if (recipe.Inputs.Count == 1)
                {
                    AppendSteps(recipe.Inputs[0], false, steps);
                }

                if (recipe.Block is not null)
                {
                    steps.Add(new ProductionStep(
                        recipe.BlockRecipe is not null
                            ? "可制作块（hybrid）"
                            : "堆叠块",
                        recipe.Block,
                        ProductionStepKind.Block,
                        RotationDirection.None,
                        0));
                }

                steps.Add(new ProductionStep(
                    "堆叠",
                    recipe.Shape,
                    ProductionStepKind.Stack,
                    RotationDirection.None,
                    0));

                break;
        }

        // 非交换的根节点补一个显式的目标标记，线性列表也能看到终点。
        if (isRoot &&
            recipe.Kind != RecipeKind.Exchange)
        {
            steps.Add(new ProductionStep(
                "完成目标图形",
                recipe.Shape,
                ProductionStepKind.Target,
                RotationDirection.None,
                0));
        }
    }

    /// <summary>
    /// 交换叶子的 15 步构造：制作四片、预旋转、两次交换、转回、末次交换。
    /// 与旧实现的步骤标题保持一致，避免 UI 文案漂移。
    /// </summary>
    private static void AppendExchangeSteps(
        ExchangeDetails details,
        bool isRoot,
        List<ProductionStep> steps)
    {
        int parts = details.Product.PartCount;
        int half = parts / 2;

        int leftRotation = half - details.LeftSplit;
        int rightSplit = details.RightSplit;

        (RotationDirection leftDirection, int leftCount, string leftTitle) =
            PillarHalfHalves.DescribeRotation(parts, leftRotation);

        (RotationDirection rightDirection, int rightCount, string rightTitle) =
            PillarHalfHalves.DescribeRotation(parts, -rightSplit);

        (RotationDirection leftBackDirection, int leftBackCount, string leftBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, -leftRotation);

        (RotationDirection rightBackDirection, int rightBackCount, string rightBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, rightSplit);

        steps.Add(new ProductionStep(
            "制作带柱子的半半边 P1",
            details.P1.Raw,
            ProductionStepKind.PillarPiece,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            leftTitle,
            details.P1.Rotated,
            ProductionStepKind.Rotate,
            leftDirection,
            leftCount));

        steps.Add(new ProductionStep(
            "制作带柱子的半半边 P2",
            details.P2.Raw,
            ProductionStepKind.PillarPiece,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            leftTitle,
            details.P2.Rotated,
            ProductionStepKind.Rotate,
            leftDirection,
            leftCount));

        steps.Add(new ProductionStep(
            "交换",
            details.Left.Raw,
            ProductionStepKind.Exchange,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            leftBackTitle,
            details.Left.Result,
            ProductionStepKind.Rotate,
            leftBackDirection,
            leftBackCount));

        steps.Add(new ProductionStep(
            "交换得到左半边",
            details.Left.Result,
            ProductionStepKind.Exchange,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            "制作带柱子的半半边 P3",
            details.P3.Raw,
            ProductionStepKind.PillarPiece,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            rightTitle,
            details.P3.Rotated,
            ProductionStepKind.Rotate,
            rightDirection,
            rightCount));

        steps.Add(new ProductionStep(
            "制作带柱子的半半边 P4",
            details.P4.Raw,
            ProductionStepKind.PillarPiece,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            rightTitle,
            details.P4.Rotated,
            ProductionStepKind.Rotate,
            rightDirection,
            rightCount));

        steps.Add(new ProductionStep(
            "交换",
            details.Right.Raw,
            ProductionStepKind.Exchange,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            rightBackTitle,
            details.Right.Result,
            ProductionStepKind.Rotate,
            rightBackDirection,
            rightBackCount));

        steps.Add(new ProductionStep(
            "交换得到右半边",
            details.Right.Result,
            ProductionStepKind.Exchange,
            RotationDirection.None,
            0));

        steps.Add(new ProductionStep(
            isRoot ? "交换得到目标图形" : "交换得到中间图形",
            details.Product,
            isRoot
                ? ProductionStepKind.Target
                : ProductionStepKind.Exchange,
            RotationDirection.None,
            0));
    }

    #endregion

    #region 节点图

    private static ProductionGraph BuildGraph(
        Recipe recipe,
        bool isRoot)
    {
        var nodes = new List<ProductionNode>();
        var edges = new List<ProductionEdge>();

        AppendGraph(
            recipe,
            1,
            string.Empty,
            isRoot,
            nodes,
            edges);

        return new ProductionGraph(nodes, edges);
    }

    /// <summary>
    /// 递归追加节点链，返回（终点节点 Id，下一可用行号）。
    /// 纯交换路径的 Id 与旧实现完全一致（pillar-p1、exchange-left、target…）。
    /// </summary>
    private static (string TerminalId, int NextRow) AppendGraph(
        Recipe recipe,
        int row,
        string prefix,
        bool isRoot,
        List<ProductionNode> nodes,
        List<ProductionEdge> edges)
    {
        switch (recipe.Kind)
        {
            case RecipeKind.Exchange:
                return AppendExchangeGraph(
                    recipe,
                    row,
                    prefix,
                    isRoot,
                    nodes,
                    edges);

            case RecipeKind.Pin:
            {
                var (innerId, nextRow) = AppendGraph(
                    recipe.Inputs[0],
                    row,
                    prefix + "in-",
                    false,
                    nodes,
                    edges);

                string pinId = prefix + "pin";

                nodes.Add(new ProductionNode(
                    pinId,
                    "顶针",
                    ProductionNodeKind.Pin,
                    nextRow,
                    recipe.Shape));

                edges.Add(new ProductionEdge(innerId, pinId));

                if (isRoot)
                {
                    string targetId = prefix + "target";

                    nodes.Add(new ProductionNode(
                        targetId,
                        "目标图形",
                        ProductionNodeKind.Target,
                        nextRow + 1,
                        recipe.Shape));

                    edges.Add(new ProductionEdge(pinId, targetId));

                    return (targetId, nextRow + 2);
                }

                return (pinId, nextRow + 1);
            }

            case RecipeKind.Stack:
            {
                var (innerId, innerNext) = AppendGraph(
                    recipe.Inputs[0],
                    row,
                    prefix + "in-",
                    false,
                    nodes,
                    edges);

                string blockId;

                if (recipe.BlockRecipe is not null)
                {
                    var (blockTerminal, blockNext) = AppendGraph(
                        recipe.BlockRecipe,
                        row,
                        prefix + "blk-",
                        false,
                        nodes,
                        edges);

                    blockId = blockTerminal;

                    innerNext = Math.Max(innerNext, blockNext);
                }
                else
                {
                    blockId = prefix + "block";

                    nodes.Add(new ProductionNode(
                        blockId,
                        "堆叠块",
                        ProductionNodeKind.Block,
                        innerNext,
                        recipe.Block));

                    innerNext++;
                }

                string stackId = prefix + "stack";

                nodes.Add(new ProductionNode(
                    stackId,
                    "堆叠",
                    ProductionNodeKind.Stack,
                    innerNext,
                    recipe.Shape));

                edges.Add(new ProductionEdge(innerId, stackId));
                edges.Add(new ProductionEdge(blockId, stackId));

                if (isRoot)
                {
                    string targetId = prefix + "target";

                    nodes.Add(new ProductionNode(
                        targetId,
                        "目标图形",
                        ProductionNodeKind.Target,
                        innerNext + 1,
                        recipe.Shape));

                    edges.Add(new ProductionEdge(stackId, targetId));

                    return (targetId, innerNext + 2);
                }

                return (stackId, innerNext + 1);
            }

            default:
            {
                string id = prefix + "unknown";

                nodes.Add(new ProductionNode(
                    id,
                    "未知工序",
                    ProductionNodeKind.Pending,
                    row,
                    recipe.Shape));

                return (id, row + 1);
            }
        }
    }

    private static (string TerminalId, int NextRow) AppendExchangeGraph(
        Recipe recipe,
        int row,
        string prefix,
        bool isRoot,
        List<ProductionNode> nodes,
        List<ProductionEdge> edges)
    {
        if (recipe.Exchange is null)
        {
            string id = prefix + "exchange";

            nodes.Add(new ProductionNode(
                id,
                isRoot ? "交换得到目标图形" : "交换得到中间图形",
                isRoot
                    ? ProductionNodeKind.Target
                    : ProductionNodeKind.Exchange,
                row,
                recipe.Shape));

            return (id, row + 1);
        }

        ExchangeDetails details = recipe.Exchange;

        int parts = details.Product.PartCount;
        int half = parts / 2;

        int leftRotation = half - details.LeftSplit;
        int rightSplit = details.RightSplit;

        (_, _, string leftTitle) =
            PillarHalfHalves.DescribeRotation(parts, leftRotation);

        (_, _, string rightTitle) =
            PillarHalfHalves.DescribeRotation(parts, -rightSplit);

        (_, _, string leftBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, -leftRotation);

        (_, _, string rightBackTitle) =
            PillarHalfHalves.DescribeRotation(parts, rightSplit);

        string p1 = prefix + "pillar-p1";
        string p2 = prefix + "pillar-p2";
        string p3 = prefix + "pillar-p3";
        string p4 = prefix + "pillar-p4";
        string rp1 = prefix + "rotated-p1";
        string rp2 = prefix + "rotated-p2";
        string rp3 = prefix + "rotated-p3";
        string rp4 = prefix + "rotated-p4";
        string wasteLeft = prefix + "waste-left";
        string exchangeLeft = prefix + "exchange-left";
        string exchangeRight = prefix + "exchange-right";
        string wasteRight = prefix + "waste-right";
        string rotatedLeft = prefix + "rotated-left";
        string rotatedRight = prefix + "rotated-right";
        string target = prefix + "target";
        string wasteFinal = prefix + "waste-final";

        // 第 1 行：四个带柱子的半半边（未旋转）。
        nodes.Add(new ProductionNode(
            p1,
            "制作带柱子的半半边 P1",
            ProductionNodeKind.PillarPiece,
            row,
            details.P1.Raw));

        nodes.Add(new ProductionNode(
            p2,
            "制作带柱子的半半边 P2",
            ProductionNodeKind.PillarPiece,
            row,
            details.P2.Raw));

        nodes.Add(new ProductionNode(
            p3,
            "制作带柱子的半半边 P3",
            ProductionNodeKind.PillarPiece,
            row,
            details.P3.Raw));

        nodes.Add(new ProductionNode(
            p4,
            "制作带柱子的半半边 P4",
            ProductionNodeKind.PillarPiece,
            row,
            details.P4.Raw));

        // 第 2 行：预旋转结果。
        nodes.Add(new ProductionNode(
            rp1,
            leftTitle,
            ProductionNodeKind.Rotate,
            row + 1,
            details.P1.Rotated));

        nodes.Add(new ProductionNode(
            rp2,
            leftTitle,
            ProductionNodeKind.Rotate,
            row + 1,
            details.P2.Rotated));

        nodes.Add(new ProductionNode(
            rp3,
            rightTitle,
            ProductionNodeKind.Rotate,
            row + 1,
            details.P3.Rotated));

        nodes.Add(new ProductionNode(
            rp4,
            rightTitle,
            ProductionNodeKind.Rotate,
            row + 1,
            details.P4.Rotated));

        // 第 3 行：两次同侧交换（所需输出 + 废料）。
        nodes.Add(new ProductionNode(
            wasteLeft,
            "废料",
            ProductionNodeKind.Waste,
            row + 2,
            details.Left.Waste));

        nodes.Add(new ProductionNode(
            exchangeLeft,
            "交换得到左半边",
            ProductionNodeKind.Exchange,
            row + 2,
            details.Left.Raw));

        nodes.Add(new ProductionNode(
            exchangeRight,
            "交换得到右半边",
            ProductionNodeKind.Exchange,
            row + 2,
            details.Right.Raw));

        nodes.Add(new ProductionNode(
            wasteRight,
            "废料",
            ProductionNodeKind.Waste,
            row + 2,
            details.Right.Waste));

        // 第 4 行：交换后转回的两个半边。
        nodes.Add(new ProductionNode(
            rotatedLeft,
            leftBackTitle,
            ProductionNodeKind.Rotate,
            row + 3,
            details.Left.Result));

        nodes.Add(new ProductionNode(
            rotatedRight,
            rightBackTitle,
            ProductionNodeKind.Rotate,
            row + 3,
            details.Right.Result));

        // 第 5 行：末次交换的产物与废料。
        nodes.Add(new ProductionNode(
            target,
            isRoot ? "交换得到目标图形" : "交换得到中间图形",
            isRoot
                ? ProductionNodeKind.Target
                : ProductionNodeKind.Exchange,
            row + 4,
            details.Product));

        nodes.Add(new ProductionNode(
            wasteFinal,
            "废料",
            ProductionNodeKind.Waste,
            row + 4,
            details.Waste));

        edges.Add(new ProductionEdge(p1, rp1));
        edges.Add(new ProductionEdge(p2, rp2));
        edges.Add(new ProductionEdge(p3, rp3));
        edges.Add(new ProductionEdge(p4, rp4));

        edges.Add(new ProductionEdge(rp1, exchangeLeft));
        edges.Add(new ProductionEdge(rp2, exchangeLeft));
        edges.Add(new ProductionEdge(rp3, exchangeRight));
        edges.Add(new ProductionEdge(rp4, exchangeRight));

        edges.Add(new ProductionEdge(exchangeLeft, rotatedLeft));
        edges.Add(new ProductionEdge(exchangeLeft, wasteLeft));
        edges.Add(new ProductionEdge(exchangeRight, rotatedRight));
        edges.Add(new ProductionEdge(exchangeRight, wasteRight));

        edges.Add(new ProductionEdge(rotatedLeft, target));
        edges.Add(new ProductionEdge(rotatedRight, target));

        edges.Add(new ProductionEdge(target, wasteFinal));

        return (target, row + 5);
    }

    #endregion
}

using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Web.Services;

/// <summary>
/// 交换工序卡片的「半边粒度」视图（只改 Web 显示层，不改 Core 的 Recipe / 节点图）。
///
/// <para>
/// 用户口径：<b>走半边工序的那个半边，交换卡片直接从「半边」开始画</b>，
/// 不再把半边的内部构造展开成「旋转半半边 / 半半边交换」。按两侧是否需要半边工序分三种情形：
/// </para>
/// <list type="bullet">
/// <item><b>两侧都需要</b>：4 个节点「制作半边1 → 制作半边2 → 交换得到目标图形 → 废料」；</item>
/// <item><b>只有一侧需要</b>：需要的一侧直接一个「制作半边1/2」节点，
/// 另一侧保留交换链的半半边展开（带柱半半边 → 预旋转 → 交换得到半边2）；</item>
/// <item><b>两侧都不需要</b>：返回 null，页面继续用原来的交换链节点图。</item>
/// </list>
///
/// <para>
/// 「哪一侧需要半边工序」直接读 <see cref="HalfAssembly"/> 给出的两个
/// <see cref="HalfWorkOrder"/>：候选 1（Q 自身）表示这 k 列都能按单角工序单独做，
/// 不会生成半边工序卡片；候选 2 / 3 才会让 <see cref="AngleWorkOrderBuilder.Build"/>
/// 走半边分支。因此不需要为了判定再跑一遍逐列 Build，避免显示层额外开销。
/// </para>
///
/// <para>
/// 预先用 n = 4 的 S 判据做一次廉价过滤：四列都属于 S 时不可能出现半边工序，
/// 直接跳过 <see cref="HalfAssembly.TryBuild"/>；只有存在非 S 列时才真正求解。
/// 带顶针 / 堆叠 / 块的路径不替换（交换只是子步骤，整段替换会丢节点），保持原图。
/// 结果只在分析阶段算一次并缓存在页面字段（<c>_path / _pathGraph</c>），不会在每次渲染时重算。
/// </para>
/// </summary>
public static class HalfExchangeView
{
    /// <summary>走半边工序时，左侧半边节点的标题。</summary>
    public const string LeftHalfTitle = "制作半边1";

    /// <summary>走半边工序时，右侧半边节点的标题。</summary>
    public const string RightHalfTitle = "制作半边2";

    /// <summary>交换的两侧按「半边」称呼时的左侧名字（混合粒度下普通角工序一侧也用它）。</summary>
    private const string LeftHalfName = "半边1";

    /// <summary>交换的两侧按「半边」称呼时的右侧名字（混合粒度下普通角工序一侧也用它）。</summary>
    private const string RightHalfName = "半边2";

    /// <summary>末次交换的目标节点标题。</summary>
    public const string TargetTitle = "交换得到目标图形";

    /// <summary>交换副产物节点标题。</summary>
    public const string WasteTitle = "废料";

    /// <summary>
    /// 半边粒度视图的方向文本。两侧都是交换的两半，因此统一写成
    /// 「半边1 + 半边2」；<see cref="ExchangeDisplay"/> 对这段文本原样返回。
    /// </summary>
    public const string DirectionText = "半边1 + 半边2";

    /// <summary>交换链左半边（P1 / P2 那一侧）在节点图里的全部节点 Id。</summary>
    private static readonly string[] LeftSideNodeIds =
    {
        "pillar-p1",
        "pillar-p2",
        "rotated-p1",
        "rotated-p2",
        "waste-left",
        "exchange-left",
        "rotated-left"
    };

    /// <summary>交换链右半边（P3 / P4 那一侧）在节点图里的全部节点 Id。</summary>
    private static readonly string[] RightSideNodeIds =
    {
        "pillar-p3",
        "pillar-p4",
        "rotated-p3",
        "rotated-p4",
        "waste-right",
        "exchange-right",
        "rotated-right"
    };

    /// <summary>
    /// 尝试构造半边粒度的交换路径。返回 null 表示保持原来的交换链节点图。
    /// </summary>
    /// <param name="document">当前目标图形。</param>
    /// <param name="exchangePath">
    /// 原来的制作路径（Core 解析结果）。只有交换链路径（含 pillar-p1 … 节点）才可能做
    /// 「一侧保留半半边展开」的混合视图；半边装配路径或不可制作时返回 null。
    /// </param>
    public static ProductionPathResult? TryCreate(
        ShapeDocument document,
        ProductionPathResult? exchangePath)
    {
        if (document is null || document.PartCount != 4)
        {
            return null;
        }

        /*
         * 只有「纯交换链 / 纯半边装配」路径才替换成半边粒度。
         * 带顶针 / 堆叠 / 块的路径里，交换只是子步骤；整段替换会把这些工序节点弄丢，
         * 所以保持原来的完整节点图，避免显示回归。
         */
        if (exchangePath?.Path is { } originalPath &&
            originalPath.Nodes.Any(node =>
                node.Kind is ProductionNodeKind.Pin or
                    ProductionNodeKind.Stack or
                    ProductionNodeKind.Block))
        {
            return null;
        }

        // 半边工序只在 n = 4 时出现：四列都属于 S 时不可能有半边工序，先廉价排除。
        if (!MayNeedHalfProcess(document))
        {
            return null;
        }

        Recipe? recipe;

        try
        {
            if (!HalfAssembly.TryBuild(
                    document,
                    document.LayerCount,
                    out recipe,
                    out _) ||
                recipe?.HalfAssembly is null)
            {
                // 半边装配做不出来：保持现有（半半边）画法，由调用方保留原路径。
                return null;
            }
        }
        catch
        {
            // 显示层的兜底：半边装配异常不应该让整页分析失败，保持原图即可。
            return null;
        }

        HalfAssemblyDetails details = recipe.HalfAssembly!;

        bool leftNeedsHalf = NeedsHalfProcess(details.Left);
        bool rightNeedsHalf = NeedsHalfProcess(details.Right);

        if (!leftNeedsHalf && !rightNeedsHalf)
        {
            return null;
        }

        if (leftNeedsHalf && rightNeedsHalf)
        {
            // 两侧都是半边：4 个节点，从「制作半边1」开始。
            return BuildBothHalves(recipe, details);
        }

        /*
         * 只有一侧需要半边工序：混合粒度。
         * 需要的一侧直接画「制作半边1/2」；另一侧保留原交换链里的半半边展开。
         * 只有原路径确实是交换链（节点 Id 完整）且轴一致时才能拼出混合图，
         * 否则退回「两侧都是半边」的 4 节点视图，至少不再从半半边开始画。
         */
        return BuildMixed(exchangePath, details, leftNeedsHalf)
            ?? BuildBothHalves(recipe, details);
    }

    /// <summary>
    /// n = 4 且至少一列不是带柱列 S 时，才可能出现半边工序。
    /// 四列都属于 S ⇒ 每列都能按单角工序做出来，不存在半边工序卡片。
    /// </summary>
    private static bool MayNeedHalfProcess(
        ShapeDocument document)
    {
        for (int column = 0; column < document.PartCount; column++)
        {
            if (!PillarSCriterion.IsPillarColumn(document, column))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 这条半边是否需要「半边工序」卡片：
    /// 候选 1（Q 自身）等价于 k 列都能按单角工序单独做，候选 2 / 3 才需要半边工序。
    /// </summary>
    private static bool NeedsHalfProcess(
        HalfWorkOrder half) =>
        half.Candidate != HalfCandidate.Algo8Self;

    /// <summary>
    /// 两侧都走半边工序：直接用 <see cref="HalfAssembly"/> 的配方节点链，
    /// 只有「制作半边1 / 制作半边2 / 交换得到目标图形 / 废料」四个节点。
    /// </summary>
    private static ProductionPathResult? BuildBothHalves(
        Recipe recipe,
        HalfAssemblyDetails details)
    {
        ProductionPathResult parsed = ProductionPathParser.FromRecipe(recipe);

        if (parsed.Path is null)
        {
            return null;
        }

        ProductionPath path = parsed.Path;

        var nodes = new List<ProductionNode>(path.Nodes.Count);

        foreach (ProductionNode node in path.Nodes)
        {
            nodes.Add(node with { Title = HalfNodeTitle(node) });
        }

        var steps = new List<ProductionStep>(path.Steps.Count);

        foreach (ProductionStep step in path.Steps)
        {
            steps.Add(step with { Title = HalfStepTitle(step) });
        }

        return CreatePathResult(
            path,
            details.Axis,
            path.DirectionLegal,
            steps,
            nodes,
            path.Edges);
    }

    /// <summary>
    /// 只有一侧需要半边工序：需要的一侧换成一个半边节点，
    /// 另一侧原样保留交换链的「带柱半半边 → 预旋转 → 交换得到半边 → 半边」展开。
    /// </summary>
    private static ProductionPathResult? BuildMixed(
        ProductionPathResult? exchangePath,
        HalfAssemblyDetails details,
        bool leftNeedsHalf)
    {
        ProductionPath? original = exchangePath?.Path;

        if (original is null || original.Axis != details.Axis)
        {
            return null;
        }

        var nodeById = original.Nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);

        string normalFinalId = leftNeedsHalf
            ? "rotated-right"
            : "rotated-left";

        string normalExchangeId = leftNeedsHalf
            ? "exchange-right"
            : "exchange-left";

        string normalHalfTitle = leftNeedsHalf
            ? RightHalfName
            : LeftHalfName;

        string neededHalfTitle = leftNeedsHalf
            ? LeftHalfTitle
            : RightHalfTitle;

        ShapeDocument neededHalfShape = leftNeedsHalf
            ? details.LeftHalf
            : details.RightHalf;

        if (!nodeById.ContainsKey(normalFinalId) ||
            !nodeById.ContainsKey(normalExchangeId) ||
            !nodeById.TryGetValue("target", out ProductionNode? targetNode) ||
            !nodeById.ContainsKey("waste-final"))
        {
            return null;
        }

        // 原交换链里属于「需要半边工序」那一侧的展开节点全部拿掉。
        var removedIds = new HashSet<string>(
            leftNeedsHalf ? LeftSideNodeIds : RightSideNodeIds,
            StringComparer.Ordinal);

        var nodes = new List<ProductionNode>(original.Nodes.Count);

        foreach (ProductionNode node in original.Nodes)
        {
            if (removedIds.Contains(node.Id))
            {
                continue;
            }

            string title = node.Id switch
            {
                // 普通角工序一侧保留展开，但末步统一叫「半边1 / 半边2」。
                _ when node.Id == normalFinalId => normalHalfTitle,
                _ when node.Id == normalExchangeId =>
                    $"交换得到{normalHalfTitle}",
                "target" => TargetTitle,
                "waste-final" => WasteTitle,
                _ => node.Title
            };

            nodes.Add(node with { Title = title });
        }

        // 需要半边工序的一侧：从半边本身开始，只留一个节点，直接汇入末次交换。
        string halfNodeId = leftNeedsHalf
            ? "half-left-process"
            : "half-right-process";

        var halfNode = new ProductionNode(
            halfNodeId,
            neededHalfTitle,
            ProductionNodeKind.PillarPiece,
            1,
            neededHalfShape);

        // 左侧需要的半边放在行首（与「半边1 在左」的阅读顺序一致）。
        if (leftNeedsHalf)
        {
            nodes.Insert(0, halfNode);
        }
        else
        {
            nodes.Add(halfNode);
        }

        var keptIds = new HashSet<string>(
            nodes.Select(node => node.Id),
            StringComparer.Ordinal);

        var edges = new List<ProductionEdge>(original.Edges.Count + 1);

        foreach (ProductionEdge edge in original.Edges)
        {
            if (keptIds.Contains(edge.FromId) &&
                keptIds.Contains(edge.ToId))
            {
                edges.Add(edge);
            }
        }

        edges.Add(new ProductionEdge(halfNodeId, "target"));

        var steps = new List<ProductionStep>
        {
            new(
                neededHalfTitle,
                neededHalfShape,
                ProductionStepKind.PillarPiece,
                RotationDirection.None,
                0),
            new(
                normalHalfTitle,
                nodeById[normalFinalId].Shape,
                ProductionStepKind.PillarPiece,
                RotationDirection.None,
                0),
            new(
                TargetTitle,
                targetNode.Shape,
                ProductionStepKind.Target,
                RotationDirection.None,
                0)
        };

        return CreatePathResult(
            original,
            details.Axis,
            original.DirectionLegal,
            steps,
            nodes,
            edges);
    }

    /// <summary>
    /// 用新的节点 / 箭头 / 方向文本组装展示路径；
    /// 除方向与图以外，轴、切点、合法性沿用原路径。
    /// </summary>
    private static ProductionPathResult CreatePathResult(
        ProductionPath path,
        int axis,
        bool directionLegal,
        IReadOnlyList<ProductionStep> steps,
        IReadOnlyList<ProductionNode> nodes,
        IReadOnlyList<ProductionEdge> edges)
    {
        var displayPath = new ProductionPath(
            axis,
            path.LeftSplit,
            path.RightSplit,
            DirectionText,
            directionLegal,
            steps,
            new ProductionGraph(nodes, edges));

        return new ProductionPathResult(true, displayPath, null)
        {
            Outcome = PipelineOutcome.Producible
        };
    }

    /// <summary>半边配方节点 → 展示标题。节点 Id 由 <see cref="ProductionPathParser"/> 固定给出。</summary>
    private static string HalfNodeTitle(
        ProductionNode node)
    {
        if (node.Id.EndsWith("-half-0", StringComparison.Ordinal) ||
            node.Id == "half-0")
        {
            return LeftHalfTitle;
        }

        if (node.Id.EndsWith("-half-1", StringComparison.Ordinal) ||
            node.Id == "half-1")
        {
            return RightHalfTitle;
        }

        if (node.Id == "exchange" ||
            node.Id.EndsWith("-exchange", StringComparison.Ordinal))
        {
            return TargetTitle;
        }

        if (node.Id.EndsWith("waste-final", StringComparison.Ordinal))
        {
            return WasteTitle;
        }

        return node.Title;
    }

    /// <summary>半边配方步骤 → 展示标题。</summary>
    private static string HalfStepTitle(
        ProductionStep step)
    {
        if (step.Kind == ProductionStepKind.Target)
        {
            return TargetTitle;
        }

        if (step.Title.StartsWith("制作左半边", StringComparison.Ordinal))
        {
            return LeftHalfTitle;
        }

        if (step.Title.StartsWith("制作右半边", StringComparison.Ordinal))
        {
            return RightHalfTitle;
        }

        if (step.Title == "废料")
        {
            return WasteTitle;
        }

        return step.Title;
    }
}

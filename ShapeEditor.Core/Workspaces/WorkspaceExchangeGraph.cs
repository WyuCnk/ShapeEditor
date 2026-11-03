using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Core.Workspaces;

/// <summary>
/// 一次交换的产物：两个结果节点，以及本次需要追加到画布上的节点与连线。
///
/// Core 与 Web 的 WorkspaceStore 各自保存节点列表，所以共享实现只负责
/// 计算与构造，由调用方把 <see cref="AddedNodes"/> / <see cref="AddedEdges"/>
/// 追加到自己的画布并负责持久化。
/// </summary>
public sealed record WorkspaceExchangeOutcome(
    NodeActionResult First,
    NodeActionResult Second,
    IReadOnlyList<WorkspaceNode> AddedNodes,
    IReadOnlyList<WorkspaceEdge> AddedEdges);

/// <summary>
/// 交换运算的图级共享实现。
///
/// Core（WPF / MAUI）与 Web 的 WorkspaceStore 状态各自独立，
/// 但交换的幂等键、结果节点标题、输入记录、连线文字以及切换规则后的重算方式
/// 必须完全一致，所以统一收敛到这里，三端不再各写一套。
/// </summary>
public static class WorkspaceExchangeGraph
{
    /// <summary>两个结果节点共用的标题，不区分第一 / 第二输出。</summary>
    public const string ResultLabel = "交换结果";

    /// <summary>两个输入指向结果节点的连线文字。</summary>
    public const string EdgeLabel = "交换输入";

    private static readonly ShortCodeExporter Exporter = new();

    /// <summary>交换结果的幂等键：exchange1:{a}|{b} / exchange2:{a}|{b}。</summary>
    public static string Key(
        bool firstResult,
        string firstId,
        string secondId)
    {
        return $"exchange{(firstResult ? 1 : 2)}:{firstId}|{secondId}";
    }

    /// <summary>幂等键是否属于交换结果。</summary>
    public static bool IsExchangeKey(
        string? opKey)
    {
        return opKey is not null &&
               (opKey.StartsWith("exchange1:", StringComparison.Ordinal) ||
                opKey.StartsWith("exchange2:", StringComparison.Ordinal));
    }

    /// <summary>幂等键是否对应第一输出（exchange1）。</summary>
    public static bool IsFirstResult(
        string? opKey)
    {
        return opKey?.StartsWith("exchange1:", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// 按两个输入创建（或定位）交换结果。
    ///
    /// 两个结果都已存在时直接返回已有节点；只缺一个时补建缺失的那个。
    /// 结果节点默认不写 X/Y，摆放坐标由调用方用 SetNodePosition 决定，
    /// 与 ComputeStackOf 的约定一致。
    /// </summary>
    public static WorkspaceExchangeOutcome Create(
        IReadOnlyList<WorkspaceNode> nodes,
        IReadOnlyList<WorkspaceEdge> edges,
        int maxLayers,
        CollapseMode mode,
        string firstId,
        string secondId,
        Func<string, WorkspaceNode?> findNode,
        Func<WorkspaceNode, ShapeDocument?> getEffectiveDocument)
    {
        var addedNodes = new List<WorkspaceNode>();

        var addedEdges = new List<WorkspaceEdge>();

        WorkspaceNode? first = findNode(firstId);

        WorkspaceNode? second = findNode(secondId);

        if (first is null || second is null)
        {
            var missing = new NodeActionResult(false, null, "交换输入不存在。");

            return new WorkspaceExchangeOutcome(
                missing,
                missing,
                addedNodes,
                addedEdges);
        }

        string firstKey = Key(true, firstId, secondId);

        string secondKey = Key(false, firstId, secondId);

        WorkspaceNode? firstResult = nodes.FirstOrDefault(
            node => node.OpKey == firstKey);

        WorkspaceNode? secondResult = nodes.FirstOrDefault(
            node => node.OpKey == secondKey);

        bool alreadyExists = firstResult is not null && secondResult is not null;

        ShapeDocument? firstDocument = null;

        ShapeDocument? secondDocument = null;

        if (!alreadyExists)
        {
            ShapeDocument? firstInput = getEffectiveDocument(first);

            ShapeDocument? secondInput = getEffectiveDocument(second);

            if (firstInput is null || secondInput is null)
            {
                var empty = new NodeActionResult(false, null, "交换输入缺少图形。");

                return new WorkspaceExchangeOutcome(
                    empty,
                    empty,
                    addedNodes,
                    addedEdges);
            }

            var exchanged = ShapeExchange.Exchange(firstInput, secondInput, mode);

            if (exchanged.First is null || exchanged.Second is null)
            {
                var failed = new NodeActionResult(
                    false,
                    null,
                    exchanged.Error ?? "交换失败。");

                return new WorkspaceExchangeOutcome(
                    failed,
                    failed,
                    addedNodes,
                    addedEdges);
            }

            firstDocument = ApplyLimit(exchanged.First, maxLayers);

            secondDocument = ApplyLimit(exchanged.Second, maxLayers);
        }

        bool createdFirst = false;

        bool createdSecond = false;

        if (firstResult is null)
        {
            firstResult = CreateResultNode(
                nodes.Concat(addedNodes),
                first,
                firstDocument!,
                firstKey,
                firstId,
                secondId);

            addedNodes.Add(firstResult);

            createdFirst = true;
        }

        if (secondResult is null)
        {
            secondResult = CreateResultNode(
                nodes.Concat(addedNodes),
                first,
                secondDocument!,
                secondKey,
                firstId,
                secondId);

            addedNodes.Add(secondResult);

            createdSecond = true;
        }

        // 每个结果节点都从两个输入各连一条边，已有边按「起点 + 终点 + 文字」去重
        AddEdge(edges, addedEdges, first.Id, firstResult.Id);

        AddEdge(edges, addedEdges, second.Id, firstResult.Id);

        AddEdge(edges, addedEdges, first.Id, secondResult.Id);

        AddEdge(edges, addedEdges, second.Id, secondResult.Id);

        string message = alreadyExists
            ? "该组合已交换过，已定位到结果。"
            : "已生成交换结果。";

        return new WorkspaceExchangeOutcome(
            new NodeActionResult(createdFirst, firstResult, message),
            new NodeActionResult(createdSecond, secondResult, message),
            addedNodes,
            addedEdges);
    }

    /// <summary>
    /// 按结果节点记录的 InputAId / InputBId 重新交换：
    /// exchange1 取 First，exchange2 取 Second。输入缺失或交换失败返回 null。
    /// </summary>
    public static ShapeDocument? Recompute(
        WorkspaceNode node,
        Func<string, WorkspaceNode?> findNode,
        Func<WorkspaceNode, ShapeDocument?> getEffectiveDocument,
        CollapseMode mode)
    {
        if (node.InputAId is null || node.InputBId is null)
        {
            return null;
        }

        WorkspaceNode? first = findNode(node.InputAId);

        WorkspaceNode? second = findNode(node.InputBId);

        if (first is null || second is null)
        {
            return null;
        }

        ShapeDocument? firstDocument = getEffectiveDocument(first);

        ShapeDocument? secondDocument = getEffectiveDocument(second);

        if (firstDocument is null || secondDocument is null)
        {
            return null;
        }

        var exchanged = ShapeExchange.Exchange(firstDocument, secondDocument, mode);

        return IsFirstResult(node.OpKey)
            ? exchanged.First
            : exchanged.Second;
    }

    /// <summary>
    /// 新建一个交换结果节点。图形短代码、尺寸、幂等键与两个输入都写在节点上。
    /// </summary>
    private static WorkspaceNode CreateResultNode(
        IEnumerable<WorkspaceNode> nodes,
        WorkspaceNode parent,
        ShapeDocument document,
        string opKey,
        string firstId,
        string secondId)
    {
        List<WorkspaceNode> all = nodes.ToList();

        (int row, int col) = FindFreeCell(all, parent.Row + 1, parent.Col);

        return new WorkspaceNode
        {
            Row = row,
            Col = col,
            Seq = all.Count == 0 ? 1 : all.Max(node => node.Seq) + 1,
            Kind = "shape",
            IsResult = true,
            OpKey = opKey,
            Label = ResultLabel,
            Code = Exporter.Export(document, omitTopEmptyLayers: true),
            Format = ShortCodeFormat.Full,
            Layers = document.LayerCount,
            Parts = document.PartCount,
            InputAId = firstId,
            InputBId = secondId,
            StackBaseIsTop = false,
            TargetId = null,
            BaseId = null
        };
    }

    /// <summary>追加一条连线；画布上已有同起点、同终点、同文字的边时跳过。</summary>
    private static void AddEdge(
        IReadOnlyList<WorkspaceEdge> edges,
        List<WorkspaceEdge> addedEdges,
        string fromId,
        string toId)
    {
        bool exists =
            edges.Any(edge =>
                edge.FromId == fromId &&
                edge.ToId == toId &&
                edge.Label == EdgeLabel) ||
            addedEdges.Any(edge =>
                edge.FromId == fromId &&
                edge.ToId == toId &&
                edge.Label == EdgeLabel);

        if (exists)
        {
            return;
        }

        addedEdges.Add(new WorkspaceEdge
        {
            FromId = fromId,
            ToId = toId,
            Label = EdgeLabel
        });
    }

    /// <summary>为结果节点找一个空单元格，避免同层节点重叠。</summary>
    private static (int Row, int Col) FindFreeCell(
        IReadOnlyList<WorkspaceNode> nodes,
        int row,
        int col)
    {
        int currentRow = Math.Max(0, row);

        int currentCol = Math.Max(0, col);

        while (nodes.Any(node =>
                   node.Row == currentRow &&
                   node.Col == currentCol))
        {
            currentCol++;
        }

        return (currentRow, currentCol);
    }

    /// <summary>与 WorkspaceStore 一致：超过工作区高度上限时截断。</summary>
    private static ShapeDocument ApplyLimit(
        ShapeDocument document,
        int maxLayers)
    {
        if (document.LayerCount <= maxLayers)
        {
            return document;
        }

        return ShapePhysics.Limit(document, maxLayers).Document ?? document;
    }
}

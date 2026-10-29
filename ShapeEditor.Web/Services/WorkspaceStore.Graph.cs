using System.Text.Json;
using Microsoft.JSInterop;
using ShapeEditor.Core;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Web.Models;

namespace ShapeEditor.Web.Services;

/// <summary>
/// 运算结果节点被定位（已存在）或新建的反馈。
/// </summary>
public sealed record NodeActionResult(
    bool Created,
    WorkspaceNode? Node,
    string Message);

public sealed partial class WorkspaceStore
{
    private const string GraphKey = "shape-workspace-graph-v1";

    private readonly List<WorkspaceNode> _nodes = new();

    private readonly List<WorkspaceEdge> _edges = new();

    private readonly Dictionary<string, ShapeDocument> _nodeCache = new();

    private readonly ShortCodeExporter _nodeExporter = new();

    public IReadOnlyList<WorkspaceNode> Nodes => _nodes;

    public IReadOnlyList<WorkspaceEdge> Edges => _edges;

    public WorkspaceNode? SelectedNode { get; private set; }

    public void SelectNode(WorkspaceNode node)
    {
        SelectedNode = node;

        Changed?.Invoke();
    }

    #region 查询

    public WorkspaceNode? FindNode(string? id)
    {
        return id is null
            ? null
            : _nodes.FirstOrDefault(node => node.Id == id);
    }

    public ShapeDocument? GetNodeDocument(WorkspaceNode node)
    {
        if (!node.HasShape)
        {
            return null;
        }

        if (_nodeCache.TryGetValue(node.Id, out ShapeDocument? cached))
        {
            return cached;
        }

        ParseResult result = new ShortCodeParser().Parse(
            node.Code,
            node.Layers,
            node.Parts,
            node.Format);

        if (!result.Success || result.Document is null)
        {
            return null;
        }

        _nodeCache[node.Id] = result.Document;

        return result.Document;
    }

    /// <summary>
    /// 从节点出发、沿输出边可达的所有节点（不含自身）。
    /// </summary>
    public List<WorkspaceNode> Downstream(string nodeId)
    {
        var result = new List<WorkspaceNode>();

        var visited = new HashSet<string> { nodeId };

        var queue = new Queue<string>();

        queue.Enqueue(nodeId);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();

            foreach (WorkspaceEdge edge in _edges.Where(e => e.FromId == current))
            {
                if (!visited.Add(edge.ToId))
                {
                    continue;
                }

                WorkspaceNode? target = FindNode(edge.ToId);

                if (target is null)
                {
                    continue;
                }

                result.Add(target);

                queue.Enqueue(target.Id);
            }
        }

        return result;
    }

    #endregion

    #region 节点创建与替换

    public WorkspaceNode CreateSourceNode(int row, int col)
    {
        var node = new WorkspaceNode
        {
            Row = row,
            Col = col,
            Seq = NextSeq(),
            Label = "素材"
        };

        (node.Row, node.Col) = FindFreeCell(row, col);

        _nodes.Add(node);

        PersistGraph();
        Changed?.Invoke();

        return node;
    }

    /// <summary>
    /// 写入图形。若有下游，先级联删除下游并返回删除数量。
    /// </summary>
    public int ReplaceNodeShape(
        string nodeId,
        string code,
        ShortCodeFormat format,
        int layers,
        int parts)
    {
        WorkspaceNode? node = FindNode(nodeId);

        if (node is null)
        {
            return 0;
        }

        int removed = DeleteDownstream(nodeId);

        node.Kind = "shape";
        node.Code = code;
        node.Format = format;
        node.Layers = layers;
        node.Parts = parts;
        node.IsResult = false;
        node.OpKey = null;
        node.TargetId = null;
        node.BaseId = null;

        _nodeCache.Remove(nodeId);

        PersistGraph();
        Changed?.Invoke();

        return removed;
    }

    private int DeleteDownstream(string nodeId)
    {
        List<WorkspaceNode> downstream = Downstream(nodeId);

        if (downstream.Count == 0)
        {
            return 0;
        }

        foreach (WorkspaceNode node in downstream)
        {
            _nodes.Remove(node);
            _nodeCache.Remove(node.Id);
        }

        _edges.RemoveAll(edge =>
            edge.FromId == nodeId ||
            downstream.Any(node => node.Id == edge.FromId || node.Id == edge.ToId));

        return downstream.Count;
    }

    #endregion

    #region 单输入运算

    public NodeActionResult ApplyCollapse(string nodeId, CollapseMode mode)
    {
        return ApplySingle(nodeId, "collapse", "下落", "下落结果", document =>
            ShapePhysics.Collapse(document, mode));
    }

    public NodeActionResult ApplyPin(string nodeId, CollapseMode mode)
    {
        return ApplySingle(nodeId, "pin", "顶针", "顶针结果", document =>
            ShapePhysics.Pin(document, mode));
    }

    public NodeActionResult ApplyCut(string nodeId, CollapseMode mode)
    {
        WorkspaceNode? node = FindNode(nodeId);

        if (node is null)
        {
            return new NodeActionResult(false, null, "节点不存在。");
        }

        ShapeDocument? document = GetNodeDocument(node);

        if (document is null)
        {
            return new NodeActionResult(false, null, "该节点还没有图形。");
        }

        string westKey = $"cutw:{nodeId}";
        string eastKey = $"cute:{nodeId}";

        WorkspaceNode? existing = _nodes.FirstOrDefault(
            entry => entry.OpKey == westKey || entry.OpKey == eastKey);

        if (existing is not null)
        {
            return new NodeActionResult(
                false,
                existing,
                "该节点已经切割过，已定位到结果。");
        }

        CutResult cut = ShapePhysics.Cut(document, mode);

        if (!cut.Success || cut.West is null || cut.East is null)
        {
            return new NodeActionResult(false, null, cut.Error ?? "切割失败。");
        }

        WorkspaceNode west = AddResultNode(
            node,
            cut.West,
            westKey,
            "切割西半",
            colOffset: -1);

        WorkspaceNode east = AddResultNode(
            node,
            cut.East,
            eastKey,
            "切割东半",
            colOffset: 1);

        AddEdge(nodeId, west.Id, "切割西半边");
        AddEdge(nodeId, east.Id, "切割东半边");

        PersistGraph();
        Changed?.Invoke();

        return new NodeActionResult(true, west, "已生成切割结果。");
    }

    private NodeActionResult ApplySingle(
        string nodeId,
        string op,
        string edgeLabel,
        string resultLabel,
        Func<ShapeDocument, PhysicsResult> operation)
    {
        WorkspaceNode? node = FindNode(nodeId);

        if (node is null)
        {
            return new NodeActionResult(false, null, "节点不存在。");
        }

        string key = $"{op}:{nodeId}";

        WorkspaceNode? existing = _nodes.FirstOrDefault(entry => entry.OpKey == key);

        if (existing is not null)
        {
            return new NodeActionResult(
                false,
                existing,
                "该操作已存在，已定位到结果。");
        }

        ShapeDocument? document = GetNodeDocument(node);

        if (document is null)
        {
            return new NodeActionResult(false, null, "该节点还没有图形。");
        }

        PhysicsResult result = operation(document);

        if (!result.Success || result.Document is null)
        {
            return new NodeActionResult(false, null, result.Error ?? "运算失败。");
        }

        WorkspaceNode created = AddResultNode(
            node,
            result.Document,
            key,
            resultLabel,
            colOffset: 0);

        AddEdge(nodeId, created.Id, edgeLabel);

        PersistGraph();
        Changed?.Invoke();

        return new NodeActionResult(true, created, "已生成结果。");
    }

    #endregion

    #region 堆叠

    /// <summary>
    /// 为当前节点开一个堆叠槽位；已有未解析槽位时直接复用。
    /// </summary>
    public WorkspaceNode BeginStack(string nodeId, bool currentIsTop)
    {
        WorkspaceNode? node = FindNode(nodeId);

        if (node is null)
        {
            throw new InvalidOperationException("节点不存在。");
        }

        WorkspaceNode? pending = _nodes.FirstOrDefault(
            entry => entry.Kind == "ref" &&
                     entry.BaseId == nodeId &&
                     entry.TargetId is null &&
                     !entry.HasShape);

        if (pending is not null)
        {
            return pending;
        }

        (int row, int col) = FindFreeCell(node.Row, node.Col + 1);

        var slot = new WorkspaceNode
        {
            Kind = "ref",
            BaseId = nodeId,
            TopInput = currentIsTop,
            Row = row,
            Col = col,
            Seq = NextSeq(),
            Label = currentIsTop ? "下方输入" : "上方输入"
        };

        _nodes.Add(slot);

        PersistGraph();
        Changed?.Invoke();

        return slot;
    }

    public void SetReferenceTarget(string slotId, string targetNodeId)
    {
        WorkspaceNode? slot = FindNode(slotId);

        if (slot is null || slot.Kind != "ref")
        {
            return;
        }

        if (Downstream(slot.Id).Count > 0 || slot.HasShape)
        {
            return;
        }

        slot.TargetId = targetNodeId;

        PersistGraph();
        Changed?.Invoke();
    }

    public NodeActionResult ComputeStack(string slotId, CollapseMode mode)
    {
        WorkspaceNode? slot = FindNode(slotId);

        if (slot is null || slot.BaseId is null)
        {
            return new NodeActionResult(false, null, "堆叠槽位无效。");
        }

        WorkspaceNode? baseNode = FindNode(slot.BaseId);

        if (baseNode is null)
        {
            return new NodeActionResult(false, null, "堆叠底节点不存在。");
        }

        WorkspaceNode? other = ResolveSlotInput(slot);

        if (other is null)
        {
            return new NodeActionResult(false, null, "请先为堆叠槽位选择图形。");
        }

        ShapeDocument? baseDocument = GetNodeDocument(baseNode);
        ShapeDocument? otherDocument = GetNodeDocument(other);

        if (baseDocument is null || otherDocument is null)
        {
            return new NodeActionResult(false, null, "堆叠输入缺少图形。");
        }

        string topId = slot.TopInput ? baseNode.Id : other.Id;
        string bottomId = slot.TopInput ? other.Id : baseNode.Id;

        string key = $"stack:{bottomId}|{topId}";

        WorkspaceNode? existing = _nodes.FirstOrDefault(entry => entry.OpKey == key);

        if (existing is not null)
        {
            return new NodeActionResult(false, existing, "该组合已堆叠过，已定位到结果。");
        }

        ShapeDocument stackBase = slot.TopInput ? otherDocument : baseDocument;
        ShapeDocument stackTop = slot.TopInput ? baseDocument : otherDocument;

        int heightLimit = Math.Min(
            ShapeRules.MaxLayerCount,
            stackBase.LayerCount + 1 + stackTop.LayerCount);

        PhysicsResult result = ShapePhysics.Stack(
            stackBase,
            stackTop,
            heightLimit,
            mode);

        if (!result.Success || result.Document is null)
        {
            return new NodeActionResult(false, null, result.Error ?? "堆叠失败。");
        }

        WorkspaceNode created = AddResultNode(
            baseNode,
            result.Document,
            key,
            "堆叠结果",
            colOffset: 0);

        AddEdge(
            bottomId,
            created.Id,
            "在下方堆叠");

        AddEdge(
            topId,
            created.Id,
            "在上方堆叠");

        PersistGraph();
        Changed?.Invoke();

        return new NodeActionResult(true, created, "已生成堆叠结果。");
    }

    private WorkspaceNode? ResolveSlotInput(WorkspaceNode slot)
    {
        if (slot.TargetId is not null)
        {
            return FindNode(slot.TargetId);
        }

        return slot.HasShape ? slot : null;
    }

    #endregion

    #region 删除

    /// <summary>
    /// 删除节点及其全部下游，并清理因此孤立的堆叠槽位。
    /// </summary>
    public int DeleteNode(string nodeId)
    {
        List<WorkspaceNode> downstream = Downstream(nodeId);

        var cascade = new HashSet<string>(downstream.Select(node => node.Id))
        {
            nodeId
        };

        // 结果节点被删时，它专属的引用槽也一起清掉
        foreach (string id in cascade.ToList())
        {
            foreach (WorkspaceNode slot in _nodes.Where(
                         entry => entry.Kind == "ref" &&
                                  entry.BaseId is not null &&
                                  cascade.Contains(entry.BaseId)))
            {
                if (slot.TargetId is not null && cascade.Contains(slot.TargetId))
                {
                    continue;
                }

                if (slot.Id == nodeId)
                {
                    continue;
                }

                bool onlyFeedsCascade = true;

                foreach (WorkspaceEdge edge in _edges.Where(edge => edge.FromId == slot.Id))
                {
                    if (!cascade.Contains(edge.ToId))
                    {
                        onlyFeedsCascade = false;
                    }
                }

                if (onlyFeedsCascade)
                {
                    cascade.Add(slot.Id);
                }
            }
        }

        foreach (string id in cascade)
        {
            WorkspaceNode? node = FindNode(id);

            if (node is not null)
            {
                _nodes.Remove(node);
            }

            _nodeCache.Remove(id);
        }

        _edges.RemoveAll(edge =>
            cascade.Contains(edge.FromId) || cascade.Contains(edge.ToId));

        if (SelectedNode is not null && cascade.Contains(SelectedNode.Id))
        {
            SelectedNode = null;
        }

        PersistGraph();
        Changed?.Invoke();

        return cascade.Count;
    }

    public void ClearGraph()
    {
        _nodes.Clear();
        _edges.Clear();
        _nodeCache.Clear();
        SelectedNode = null;

        PersistGraph();
        Changed?.Invoke();
    }

    #endregion

    #region 内部工具

    private WorkspaceNode AddResultNode(
        WorkspaceNode parent,
        ShapeDocument document,
        string opKey,
        string label,
        int colOffset)
    {
        (int row, int col) = FindFreeCell(
            parent.Row + 1,
            parent.Col + colOffset);

        var node = new WorkspaceNode
        {
            Row = row,
            Col = col,
            Seq = NextSeq(),
            IsResult = true,
            OpKey = opKey,
            Label = label,
            Code = _nodeExporter.Export(document, omitTopEmptyLayers: true),
            Format = ShortCodeFormat.Full,
            Layers = document.LayerCount,
            Parts = document.PartCount
        };

        _nodes.Add(node);

        _nodeCache[node.Id] = document;

        return node;
    }

    private void AddEdge(string fromId, string toId, string label)
    {
        if (_edges.Any(edge => edge.FromId == fromId &&
                               edge.ToId == toId &&
                               edge.Label == label))
        {
            return;
        }

        _edges.Add(new WorkspaceEdge
        {
            FromId = fromId,
            ToId = toId,
            Label = label
        });
    }

    /// <summary>
    /// 给引用槽位写入素材图形，但保持它是 ref 节点（这样计算按钮不会消失）。
    /// </summary>
    public void SetSlotShape(
        string slotId,
        string code,
        ShortCodeFormat format,
        int layers,
        int parts)
    {
        WorkspaceNode? slot = FindNode(slotId);

        if (slot is null || slot.Kind != "ref")
        {
            return;
        }

        slot.Code = code;
        slot.Format = format;
        slot.Layers = layers;
        slot.Parts = parts;
        slot.TargetId = null;

        _nodeCache.Remove(slotId);

        PersistGraph();
        Changed?.Invoke();
    }

    private int NextSeq()
    {
        return _nodes.Count == 0 ? 1 : _nodes.Max(node => node.Seq) + 1;
    }

    private (int Row, int Col) FindFreeCell(int row, int col)
    {
        int currentRow = Math.Max(0, row);
        int currentCol = Math.Max(0, col);

        while (_nodes.Any(node => node.Row == currentRow && node.Col == currentCol))
        {
            currentCol++;
        }

        return (currentRow, currentCol);
    }

    #endregion

    #region 持久化

    public async Task EnsureGraphLoadedAsync()
    {
        try
        {
            string? json = await _js.InvokeAsync<string?>(
                "shapeStorage.load",
                GraphKey);

            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var payload = JsonSerializer.Deserialize<GraphPayload>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Nodes is null)
            {
                return;
            }

            _nodes.Clear();
            _edges.Clear();
            _nodeCache.Clear();

            _nodes.AddRange(payload.Nodes);
            _edges.AddRange(payload.Edges ?? new List<WorkspaceEdge>());
        }
        catch
        {
            /*
             * 旧数据损坏时忽略。
             */
        }

        Changed?.Invoke();
    }

    private void PersistGraph()
    {
        _ = PersistGraphAsync();
    }

    private async Task PersistGraphAsync()
    {
        try
        {
            string json = JsonSerializer.Serialize(new GraphPayload
            {
                Nodes = _nodes.ToList(),
                Edges = _edges.ToList()
            });

            await _js.InvokeVoidAsync(
                "shapeStorage.save",
                GraphKey,
                json);
        }
        catch
        {
        }
    }

    private sealed class GraphPayload
    {
        public List<WorkspaceNode>? Nodes { get; set; }

        public List<WorkspaceEdge>? Edges { get; set; }
    }

    #endregion
}

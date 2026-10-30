using System.IO;
using System.Text.Json;
using ShapeEditor.Core;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Core.Workspaces;

public enum WorkspaceAddResult
{
    Added,
    Duplicate,
    Full,
    Invalid
}

public sealed record NodeActionResult(
    bool Created,
    WorkspaceNode? Node,
    string Message);

/// <summary>
/// WPF 版工作区：多工作区 + 共享素材库 + 制作路径图。
///
/// 规则与 Web 版完全一致（节点只读、运算幂等、删除只向下级联），
/// 差别只在持久化方式：写到 %LOCALAPPDATA%\ShapeEditor\workspaces.json。
/// </summary>
public sealed class WorkspaceStore
{
    public const int MaxMaterials = 200;

    private const int MaxHistoryProjects = 20;

    private readonly string _filePath;

    private readonly ShortCodeParser _parser = new();

    private readonly ShortCodeExporter _exporter = new();

    private readonly Dictionary<string, ShapeDocument> _cache = new();

    private List<WorkspaceProject> _projects = new();

    private WorkspaceProject? _active;

    private string? _activeId;

    public WorkspaceStore(
        string? filePath = null)
    {
        _filePath = filePath ?? DefaultPath();

        Load();
    }

    public event Action? Changed;

    private static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShapeEditor",
            "workspaces.json");
    }

    /// <summary>最近一次保存失败的原因。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 全局素材库，所有工作区共享。
    /// </summary>
    public List<WorkspaceItem> Materials { get; private set; } = new();

    public IReadOnlyList<WorkspaceProject> Projects => _projects;

    public WorkspaceProject? Active => _active;

    public IReadOnlyList<WorkspaceNode> Nodes => _active?.Nodes ?? new List<WorkspaceNode>();

    public IReadOnlyList<WorkspaceEdge> Edges => _active?.Edges ?? new List<WorkspaceEdge>();

    public int MaxLayers => _active?.MaxLayers ?? 100;

    public CollapseMode Mode => _active?.Mode ?? CollapseMode.IncludeCycles;

    /// <summary>
    /// 该工作区固定的象限数：已经有图形时返回锁定值，空工作区返回 4。
    /// </summary>
    public int WorkspaceParts
    {
        get
        {
            WorkspaceNode? first = _active?.Nodes.FirstOrDefault(node => node.HasShape);

            if (first is null)
            {
                return 4;
            }

            return _active!.PartCount > 0 ? _active.PartCount : first.Parts;
        }
    }

    /// <summary>
    /// 判断给定象限数的图形能否加入当前工作区：
    /// 空工作区一律放行，否则必须与已锁定的象限数一致。
    /// </summary>
    public bool PartsAllowed(
        int parts)
    {
        bool hasShape = _active?.Nodes.Any(node => node.HasShape) ?? false;

        return !hasShape || parts == WorkspaceParts;
    }

    public WorkspaceNode? SelectedNode { get; private set; }

    #region 素材库

    public WorkspaceAddResult AddMaterial(
        string code,
        ShortCodeFormat format,
        int layers,
        int parts,
        string source)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !ShapeRules.IsValidPartCount(parts) ||
            layers <= 0)
        {
            return WorkspaceAddResult.Invalid;
        }

        if (Materials.Any(item =>
                item.Code == code &&
                item.Format == format &&
                item.Layers == layers &&
                item.Parts == parts))
        {
            return WorkspaceAddResult.Duplicate;
        }

        if (Materials.Count >= MaxMaterials)
        {
            return WorkspaceAddResult.Full;
        }

        Materials.Add(new WorkspaceItem
        {
            Name = code.Length <= 24 ? code : code[..24] + '…',
            Code = code,
            Format = format,
            Layers = layers,
            Parts = parts,
            Source = source
        });

        Save();
        Changed?.Invoke();

        return WorkspaceAddResult.Added;
    }

    public void RemoveMaterial(
        string id)
    {
        Materials.RemoveAll(item => item.Id == id);

        Save();
        Changed?.Invoke();
    }

    #endregion

    #region 工作区

    public void CreateProject(
        string? name = null)
    {
        var project = new WorkspaceProject
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? $"工作区 {_projects.Count + 1}"
                : name.Trim(),
            MaxLayers = MaxLayers,
            Mode = Mode
        };

        _projects.Add(project);

        Activate(project);
    }

    public void RenameProject(
        string id,
        string? name)
    {
        WorkspaceProject? project = FindProject(id);

        if (project is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        project.Name = name.Trim();

        Save();
        Changed?.Invoke();
    }

    public void DeleteProject(
        string id)
    {
        WorkspaceProject? project = FindProject(id);

        if (project is null)
        {
            return;
        }

        _projects.Remove(project);

        if (_projects.Count == 0)
        {
            _projects.Add(new WorkspaceProject { Name = "工作区 1" });
        }

        if (_active?.Id == id)
        {
            Activate(_projects[0]);
        }
        else
        {
            Save();
            Changed?.Invoke();
        }
    }

    public void ActivateProject(
        string id)
    {
        WorkspaceProject? project = FindProject(id);

        if (project is null || project.Id == _active?.Id)
        {
            return;
        }

        Activate(project);
    }

    private void Activate(
        WorkspaceProject project)
    {
        _active = project;
        _activeId = project.Id;
        _cache.Clear();
        SelectedNode = null;

        Save();
        Changed?.Invoke();
    }

    public void SetMaxLayers(
        int layers)
    {
        if (_active is null)
        {
            return;
        }

        int value = Math.Clamp(layers, 4, ShapeRules.MaxLayerCount);

        if (value == _active.MaxLayers)
        {
            return;
        }

        _active.MaxLayers = value;

        Save();
        Changed?.Invoke();
    }

    public void SetMode(
        CollapseMode mode)
    {
        if (_active is null || _active.Mode == mode)
        {
            return;
        }

        _active.Mode = mode;

        Save();
        Changed?.Invoke();
    }

    #endregion

    #region 节点

    public WorkspaceNode? FindNode(
        string? id)
    {
        return id is null
            ? null
            : _active?.Nodes.FirstOrDefault(node => node.Id == id);
    }

    public void SelectNode(
        WorkspaceNode? node)
    {
        SelectedNode = node;

        Changed?.Invoke();
    }

    public WorkspaceNode CreateNode()
    {
        WorkspaceProject project = EnsureActive();

        int col = project.Nodes.Count == 0
            ? 0
            : project.Nodes.Max(node => node.Col) + 1;

        var node = new WorkspaceNode
        {
            Row = 0,
            Col = col,
            Seq = NextSeq(),
            Label = "素材节点"
        };

        project.Nodes.Add(node);

        Save();
        Changed?.Invoke();

        return node;
    }

    /// <summary>
    /// 在画布指定坐标新建节点（拖放素材时使用）。
    /// </summary>
    public WorkspaceNode CreateNodeAtPoint(
        double x,
        double y)
    {
        WorkspaceProject project = EnsureActive();

        var node = new WorkspaceNode
        {
            Row = 0,
            Col = 0,
            X = x,
            Y = y,
            Seq = NextSeq(),
            Label = "素材节点"
        };

        project.Nodes.Add(node);

        Save();
        Changed?.Invoke();

        return node;
    }

    /// <summary>
    /// 拖动后写入自由坐标，按 10px 网格吸附。
    /// </summary>
    public void SetNodePosition(
        string nodeId,
        double x,
        double y)
    {
        WorkspaceNode? node = FindNode(nodeId);

        if (node is null)
        {
            return;
        }

        node.X = Math.Round(x / 10) * 10;
        node.Y = Math.Round(y / 10) * 10;

        Save();
        Changed?.Invoke();
    }

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

        /*
         * 象限数校验：工作区里除当前节点外已经有图形时，
         * 新图形的象限数必须与锁定值一致，否则一个字节都不改动。
         */
        if (HasOtherShape(nodeId) && parts != WorkspaceParts)
        {
            LastError = $"该工作区的象限数已固定为 {WorkspaceParts}，不能再加入 {parts} 象限的图形。";

            return 0;
        }

        LastError = null;

        int removed = DeleteDownstream(nodeId);

        AlignMaxLayersOnFirstShape(nodeId, layers);

        LockPartCountOnFirstShape(nodeId, parts);

        ApplyShape(node, code, format, layers, parts);
        node.IsResult = false;
        node.OpKey = null;
        node.TargetId = null;
        node.BaseId = null;

        Save();
        Changed?.Invoke();

        return removed;
    }

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

        AlignMaxLayersOnFirstShape(slotId, layers);

        LockPartCountOnFirstShape(slotId, parts);

        ApplyShape(slot, code, format, layers, parts);
        slot.TargetId = null;

        Save();
        Changed?.Invoke();
    }

    public void SetReferenceTarget(
        string slotId,
        string targetId)
    {
        WorkspaceNode? slot = FindNode(slotId);

        if (slot is null ||
            slot.Kind != "ref" ||
            slot.HasShape ||
            Downstream(slotId).Count > 0)
        {
            return;
        }

        slot.TargetId = targetId;

        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// 工作区里第一次出现图形时，把高度上限对齐到该图形的层数。
    /// </summary>
    private void AlignMaxLayersOnFirstShape(
        string nodeId,
        int layers)
    {
        WorkspaceProject project = EnsureActive();

        bool hasOtherShape = project.Nodes.Any(node =>
            node.Id != nodeId && node.HasShape);

        if (hasOtherShape)
        {
            return;
        }

        project.MaxLayers = Math.Clamp(
            layers,
            ShapeRules.MinLayerCount,
            ShapeRules.MaxLayerCount);
    }

    /// <summary>
    /// 工作区里除指定节点外是否已经有图形。
    /// </summary>
    private bool HasOtherShape(
        string nodeId)
    {
        WorkspaceProject project = EnsureActive();

        return project.Nodes.Any(node =>
            node.Id != nodeId && node.HasShape);
    }

    /// <summary>
    /// 工作区里第一次出现图形时，把象限数锁定成该图形的象限数。
    /// </summary>
    private void LockPartCountOnFirstShape(
        string nodeId,
        int parts)
    {
        if (HasOtherShape(nodeId))
        {
            return;
        }

        EnsureActive().PartCount = parts;
    }

    private void ApplyShape(
        WorkspaceNode node,
        string code,
        ShortCodeFormat format,
        int layers,
        int parts)
    {
        node.Code = code;
        node.Format = format;
        node.Layers = layers;
        node.Parts = parts;

        _cache.Remove(node.Id);
    }

    public ShapeDocument? GetNodeDocument(
        WorkspaceNode node)
    {
        if (!node.HasShape)
        {
            return null;
        }

        if (_cache.TryGetValue(node.Id, out ShapeDocument? cached))
        {
            return cached;
        }

        ParseResult result = _parser.Parse(
            node.Code,
            node.Layers,
            node.Parts,
            node.Format);

        if (!result.Success || result.Document is null)
        {
            return null;
        }

        _cache[node.Id] = result.Document;

        return result.Document;
    }

    public ShapeDocument? GetEffectiveDocument(
        WorkspaceNode node)
    {
        if (node.Kind == "ref" && node.TargetId is not null)
        {
            WorkspaceNode? target = FindNode(node.TargetId);

            return target is null ? null : GetNodeDocument(target);
        }

        return GetNodeDocument(node);
    }

    public List<WorkspaceNode> Downstream(
        string nodeId)
    {
        WorkspaceProject project = EnsureActive();

        var result = new List<WorkspaceNode>();

        var visited = new HashSet<string> { nodeId };

        var queue = new Queue<string>();

        queue.Enqueue(nodeId);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();

            foreach (WorkspaceEdge edge in project.Edges.Where(e => e.FromId == current))
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

    #region 运算

    public NodeActionResult ApplyCollapse(
        string nodeId)
    {
        return ApplySingle(
            nodeId,
            "collapse",
            "下落",
            "下落结果",
            document => ShapePhysics.Collapse(document, Mode));
    }

    public NodeActionResult ApplyPin(
        string nodeId)
    {
        return ApplySingle(
            nodeId,
            "pin",
            "顶针",
            "顶针结果",
            document => ShapePhysics.Pin(document, Mode));
    }

    public NodeActionResult ApplyCut(
        string nodeId)
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

        WorkspaceNode? existing = _active!.Nodes.FirstOrDefault(
            entry => entry.OpKey == westKey || entry.OpKey == eastKey);

        if (existing is not null)
        {
            return new NodeActionResult(false, existing, "该节点已经切割过，已定位到结果。");
        }

        CutResult cut = ShapePhysics.Cut(document, Mode);

        if (!cut.Success || cut.West is null || cut.East is null)
        {
            return new NodeActionResult(false, null, cut.Error ?? "切割失败。");
        }

        WorkspaceNode west = AddResultNode(node, cut.West, westKey, "切割西半", -1);
        WorkspaceNode east = AddResultNode(node, cut.East, eastKey, "切割东半", 1);

        AddEdge(nodeId, west.Id, "切割西半边");
        AddEdge(nodeId, east.Id, "切割东半边");

        Save();
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

        WorkspaceNode? existing = _active!.Nodes.FirstOrDefault(entry => entry.OpKey == key);

        if (existing is not null)
        {
            return new NodeActionResult(false, existing, "该操作已存在，已定位到结果。");
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
            ApplyLimit(result.Document),
            key,
            resultLabel,
            0);

        AddEdge(nodeId, created.Id, edgeLabel);

        Save();
        Changed?.Invoke();

        return new NodeActionResult(true, created, "已生成结果。");
    }

    public WorkspaceNode BeginStack(
        string nodeId,
        bool currentIsTop)
    {
        WorkspaceProject project = EnsureActive();

        WorkspaceNode? node = FindNode(nodeId)
            ?? throw new InvalidOperationException("节点不存在。");

        WorkspaceNode? pending = project.Nodes.FirstOrDefault(
            entry => entry.Kind == "ref" &&
                     entry.BaseId == nodeId &&
                     entry.TargetId is null &&
                     !entry.HasShape);

        if (pending is not null)
        {
            return pending;
        }

        var slot = new WorkspaceNode
        {
            Kind = "ref",
            BaseId = nodeId,
            TopInput = currentIsTop,
            Row = node.Row,
            Col = node.Col + 1,
            Seq = NextSeq(),
            Label = currentIsTop ? "下方输入" : "上方输入"
        };

        (slot.Row, slot.Col) = FindFreeCell(slot.Row, slot.Col);

        project.Nodes.Add(slot);

        Save();
        Changed?.Invoke();

        return slot;
    }

    public NodeActionResult ComputeStack(
        string slotId)
    {
        WorkspaceNode? slot = FindNode(slotId);

        if (slot?.BaseId is null)
        {
            return new NodeActionResult(false, null, "堆叠槽位无效。");
        }

        WorkspaceNode? baseNode = FindNode(slot.BaseId);

        if (baseNode is null)
        {
            return new NodeActionResult(false, null, "堆叠底节点不存在。");
        }

        WorkspaceNode? other = slot.TargetId is not null
            ? FindNode(slot.TargetId)
            : slot;

        if (other is null)
        {
            return new NodeActionResult(false, null, "请先为堆叠槽位选择图形。");
        }

        ShapeDocument? baseDocument = GetEffectiveDocument(baseNode);
        ShapeDocument? otherDocument = GetEffectiveDocument(other);

        if (baseDocument is null || otherDocument is null)
        {
            return new NodeActionResult(false, null, "堆叠输入缺少图形。");
        }

        string topId = slot.TopInput ? baseNode.Id : other.Id;
        string bottomId = slot.TopInput ? other.Id : baseNode.Id;

        string key = $"stack:{bottomId}|{topId}";

        WorkspaceNode? existing = _active!.Nodes.FirstOrDefault(entry => entry.OpKey == key);

        if (existing is not null)
        {
            return new NodeActionResult(false, existing, "该组合已堆叠过，已定位到结果。");
        }

        ShapeDocument stackBase = slot.TopInput ? otherDocument : baseDocument;
        ShapeDocument stackTop = slot.TopInput ? baseDocument : otherDocument;

        int heightLimit = Math.Min(
            ShapeRules.MaxLayerCount,
            stackBase.LayerCount + 1 + stackTop.LayerCount);

        PhysicsResult result = ShapePhysics.Stack(stackBase, stackTop, heightLimit, Mode);

        if (!result.Success || result.Document is null)
        {
            return new NodeActionResult(false, null, result.Error ?? "堆叠失败。");
        }

        WorkspaceNode created = AddResultNode(
            baseNode,
            ApplyLimit(result.Document),
            key,
            "堆叠结果",
            0);

        WorkspaceNode bottomSource = slot.TopInput ? slot : baseNode;
        WorkspaceNode topSource = slot.TopInput ? baseNode : slot;

        AddEdge(bottomSource.Id, created.Id, "在下方堆叠");
        AddEdge(topSource.Id, created.Id, "在上方堆叠");

        Save();
        Changed?.Invoke();

        return new NodeActionResult(true, created, "已生成堆叠结果。");
    }

    /// <summary>
    /// 直接按两个节点堆叠（不再经过 ref 槽位），并把两个输入记录到结果节点上。
    /// baseIsTop 为 true 表示 base 节点放在上层。
    /// </summary>
    public NodeActionResult ComputeStackOf(
        string baseId,
        string otherId,
        bool baseIsTop)
    {
        WorkspaceNode? baseNode = FindNode(baseId);

        WorkspaceNode? otherNode = FindNode(otherId);

        if (baseNode is null || otherNode is null)
        {
            return new NodeActionResult(false, null, "堆叠输入不存在。");
        }

        ShapeDocument? baseDocument = GetEffectiveDocument(baseNode);

        ShapeDocument? otherDocument = GetEffectiveDocument(otherNode);

        if (baseDocument is null || otherDocument is null)
        {
            return new NodeActionResult(false, null, "堆叠输入缺少图形。");
        }

        WorkspaceNode topNode = baseIsTop ? baseNode : otherNode;

        WorkspaceNode bottomNode = baseIsTop ? otherNode : baseNode;

        string key = $"stack:{bottomNode.Id}|{topNode.Id}";

        WorkspaceNode? existing = EnsureActive().Nodes.FirstOrDefault(
            node => node.OpKey == key);

        if (existing is not null)
        {
            return new NodeActionResult(false, existing, "该组合已堆叠过，已定位到结果。");
        }

        ShapeDocument stackBottom = baseIsTop ? otherDocument : baseDocument;

        ShapeDocument stackTop = baseIsTop ? baseDocument : otherDocument;

        int heightLimit = Math.Min(
            MaxLayers,
            stackBottom.LayerCount + 1 + stackTop.LayerCount);

        PhysicsResult result = ShapePhysics.Stack(
            stackBottom,
            stackTop,
            heightLimit,
            Mode);

        if (!result.Success || result.Document is null)
        {
            return new NodeActionResult(false, null, result.Error ?? "堆叠失败。");
        }

        WorkspaceNode created = AddResultNode(
            baseNode,
            ApplyLimit(result.Document),
            key,
            "堆叠结果",
            0);

        // 结果节点自己记住两个输入，供重算与界面展示使用
        created.InputAId = baseId;
        created.InputBId = otherId;
        created.StackBaseIsTop = baseIsTop;

        created.Label = $"堆叠结果（上 #{topNode.Seq} / 下 #{bottomNode.Seq}）";

        AddEdge(topNode.Id, created.Id, "在上方堆叠");

        AddEdge(bottomNode.Id, created.Id, "在下方堆叠");

        Save();
        Changed?.Invoke();

        return new NodeActionResult(true, created, "已生成堆叠结果。");
    }

    public (int Updated, int Failed) RecomputeAll()
    {
        WorkspaceProject project = EnsureActive();

        int updated = 0;
        int failed = 0;

        foreach (WorkspaceNode node in project.Nodes
                     .Where(entry => entry.IsResult && entry.OpKey is not null)
                     .OrderBy(entry => entry.Row)
                     .ThenBy(entry => entry.Seq)
                     .ToList())
        {
            ShapeDocument? computed = Recompute(node);

            if (computed is null)
            {
                failed++;

                continue;
            }

            computed = ApplyLimit(computed);

            string code = _exporter.Export(computed, omitTopEmptyLayers: true);

            if (code != node.Code)
            {
                updated++;
            }

            ApplyShape(node, code, ShortCodeFormat.Full, computed.LayerCount, computed.PartCount);

            _cache[node.Id] = computed;
        }

        Save();
        Changed?.Invoke();

        return (updated, failed);
    }

    private ShapeDocument? Recompute(
        WorkspaceNode node)
    {
        string key = node.OpKey!;
        string op = key.Split(':')[0];

        if (op == "stack")
        {
            WorkspaceNode? bottom = null;
            WorkspaceNode? top = null;

            foreach (WorkspaceEdge edge in _active!.Edges.Where(e => e.ToId == node.Id))
            {
                WorkspaceNode? source = FindNode(edge.FromId);

                if (source is null)
                {
                    continue;
                }

                if (edge.Label == "在下方堆叠")
                {
                    bottom = source;
                }
                else if (edge.Label == "在上方堆叠")
                {
                    top = source;
                }
            }

            ShapeDocument? bottomDocument = bottom is null ? null : GetEffectiveDocument(bottom);
            ShapeDocument? topDocument = top is null ? null : GetEffectiveDocument(top);

            if (bottomDocument is null || topDocument is null)
            {
                return null;
            }

            int heightLimit = Math.Min(
                ShapeRules.MaxLayerCount,
                bottomDocument.LayerCount + 1 + topDocument.LayerCount);

            return ShapePhysics.Stack(bottomDocument, topDocument, heightLimit, Mode).Document;
        }

        WorkspaceEdge? incoming = _active!.Edges.FirstOrDefault(e => e.ToId == node.Id);

        WorkspaceNode? parent = incoming is null ? null : FindNode(incoming.FromId);

        ShapeDocument? document = parent is null ? null : GetNodeDocument(parent);

        if (document is null)
        {
            return null;
        }

        return op switch
        {
            "pin" => ShapePhysics.Pin(document, Mode).Document,
            "cutw" => ShapePhysics.Cut(document, Mode).West,
            "cute" => ShapePhysics.Cut(document, Mode).East,
            _ => ShapePhysics.Collapse(document, Mode).Document
        };
    }

    #endregion

    #region 删除与清理

    public int DeleteNode(
        string nodeId)
    {
        WorkspaceProject project = EnsureActive();

        List<WorkspaceNode> downstream = Downstream(nodeId);

        var cascade = new HashSet<string>(downstream.Select(node => node.Id)) { nodeId };

        foreach (string id in cascade.ToList())
        {
            foreach (WorkspaceNode slot in project.Nodes.Where(
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

                bool onlyFeedsCascade = project.Edges
                    .Where(edge => edge.FromId == slot.Id)
                    .All(edge => cascade.Contains(edge.ToId));

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
                project.Nodes.Remove(node);
            }

            _cache.Remove(id);
        }

        project.Edges.RemoveAll(edge =>
            cascade.Contains(edge.FromId) || cascade.Contains(edge.ToId));

        if (SelectedNode is not null && cascade.Contains(SelectedNode.Id))
        {
            SelectedNode = null;
        }

        Save();
        Changed?.Invoke();

        return cascade.Count;
    }

    public void ClearNodes()
    {
        WorkspaceProject project = EnsureActive();

        project.Nodes.Clear();
        project.Edges.Clear();
        _cache.Clear();
        SelectedNode = null;

        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// 把旧的「堆叠输入」槽节点（kind = ref）折叠成结果节点的两个输入，然后删掉槽节点。
    /// 每个槽的结果节点会写入 InputAId / InputBId / StackBaseIsTop，
    /// 并补上两条真实节点到结果的边（标签按上下关系），这样重算不再依赖槽位。
    /// 返回迁移的槽数量。
    /// </summary>
    public int MigrateRefSlots()
    {
        WorkspaceProject project = EnsureActive();

        List<WorkspaceNode> slots = project.Nodes
            .Where(node => node.Kind == "ref")
            .ToList();

        if (slots.Count == 0)
        {
            return 0;
        }

        foreach (WorkspaceNode slot in slots)
        {
            WorkspaceNode? baseNode = slot.BaseId is null ? null : FindNode(slot.BaseId);

            WorkspaceNode? otherNode = slot.TargetId is null
                ? null
                : FindNode(slot.TargetId);

            foreach (WorkspaceEdge edge in project.Edges
                         .Where(entry => entry.FromId == slot.Id)
                         .ToList())
            {
                WorkspaceNode? result = FindNode(edge.ToId);

                if (result is not null && baseNode is not null && otherNode is not null)
                {
                    result.InputAId = baseNode.Id;
                    result.InputBId = otherNode.Id;
                    result.StackBaseIsTop = slot.TopInput;

                    // TopInput 为 true 表示槽位的底节点在上层
                    WorkspaceNode topNode = slot.TopInput ? baseNode : otherNode;

                    WorkspaceNode bottomNode = slot.TopInput ? otherNode : baseNode;

                    AddEdge(topNode.Id, result.Id, "在上方堆叠");

                    AddEdge(bottomNode.Id, result.Id, "在下方堆叠");
                }

                project.Edges.Remove(edge);
            }

            project.Nodes.Remove(slot);

            _cache.Remove(slot.Id);

            if (SelectedNode is not null && SelectedNode.Id == slot.Id)
            {
                SelectedNode = null;
            }
        }

        project.Edges.RemoveAll(edge =>
            slots.Any(slot => slot.Id == edge.FromId || slot.Id == edge.ToId));

        Save();
        Changed?.Invoke();

        return slots.Count;
    }

    public void CycleEdgeLabel(
        string fromId,
        string toId)
    {
        WorkspaceEdge? edge = _active?.Edges.FirstOrDefault(
            entry => entry.FromId == fromId && entry.ToId == toId);

        if (edge is null)
        {
            return;
        }

        edge.LabelT = edge.LabelT switch
        {
            < 0.4 => 0.5,
            < 0.6 => 0.75,
            _ => 0.25
        };

        Save();
        Changed?.Invoke();
    }

    #endregion

    #region 内部工具

    private ShapeDocument ApplyLimit(
        ShapeDocument document)
    {
        if (document.LayerCount <= MaxLayers)
        {
            return document;
        }

        return ShapePhysics.Limit(document, MaxLayers).Document ?? document;
    }

    private WorkspaceNode AddResultNode(
        WorkspaceNode parent,
        ShapeDocument document,
        string opKey,
        string label,
        int colOffset)
    {
        WorkspaceProject project = EnsureActive();

        (int row, int col) = FindFreeCell(parent.Row + 1, parent.Col + colOffset);

        var node = new WorkspaceNode
        {
            Row = row,
            Col = col,
            Seq = NextSeq(),
            IsResult = true,
            OpKey = opKey,
            Label = label,
            Code = _exporter.Export(document, omitTopEmptyLayers: true),
            Format = ShortCodeFormat.Full,
            Layers = document.LayerCount,
            Parts = document.PartCount
        };

        project.Nodes.Add(node);

        _cache[node.Id] = document;

        return node;
    }

    private void AddEdge(
        string fromId,
        string toId,
        string label)
    {
        WorkspaceProject project = EnsureActive();

        if (project.Edges.Any(edge =>
                edge.FromId == fromId &&
                edge.ToId == toId &&
                edge.Label == label))
        {
            return;
        }

        project.Edges.Add(new WorkspaceEdge
        {
            FromId = fromId,
            ToId = toId,
            Label = label
        });
    }

    private (int Row, int Col) FindFreeCell(
        int row,
        int col)
    {
        WorkspaceProject project = EnsureActive();

        int currentRow = Math.Max(0, row);
        int currentCol = Math.Max(0, col);

        while (project.Nodes.Any(node => node.Row == currentRow && node.Col == currentCol))
        {
            currentCol++;
        }

        return (currentRow, currentCol);
    }

    private int NextSeq()
    {
        WorkspaceProject project = EnsureActive();

        return project.Nodes.Count == 0
            ? 1
            : project.Nodes.Max(node => node.Seq) + 1;
    }

    private int DeleteDownstream(
        string nodeId)
    {
        List<WorkspaceNode> downstream = Downstream(nodeId);

        if (downstream.Count == 0)
        {
            return 0;
        }

        WorkspaceProject project = EnsureActive();

        foreach (WorkspaceNode node in downstream)
        {
            project.Nodes.Remove(node);
            _cache.Remove(node.Id);
        }

        project.Edges.RemoveAll(edge =>
            edge.FromId == nodeId ||
            downstream.Any(node => node.Id == edge.FromId || node.Id == edge.ToId));

        return downstream.Count;
    }

    private WorkspaceProject EnsureActive()
    {
        if (_active is null)
        {
            _active = _projects.FirstOrDefault()
                ?? new WorkspaceProject { Name = "工作区 1" };

            if (_projects.Count == 0)
            {
                _projects.Add(_active);
            }

            _activeId = _active.Id;
        }

        return _active;
    }

    private WorkspaceProject? FindProject(
        string id)
    {
        return _projects.FirstOrDefault(project => project.Id == id);
    }

    #endregion

    #region 导入导出

    public string ExportProject()
    {
        WorkspaceProject project = EnsureActive();

        return JsonSerializer.Serialize(
            new
            {
                version = 1,
                name = project.Name,
                nodes = project.Nodes,
                edges = project.Edges,
                maxLayers = project.MaxLayers
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public bool ImportProject(
        string json,
        out string? error)
    {
        error = null;

        try
        {
            ImportPayload? payload = JsonSerializer.Deserialize<ImportPayload>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Nodes is null)
            {
                error = "文件里没有画布数据。";

                return false;
            }

            var project = new WorkspaceProject
            {
                Name = string.IsNullOrWhiteSpace(payload.Name)
                    ? "导入的工作区"
                    : payload.Name,
                Nodes = payload.Nodes,
                Edges = payload.Edges ?? new List<WorkspaceEdge>(),
                MaxLayers = Math.Clamp(payload.MaxLayers, 4, 100)
            };

            _projects.Add(project);

            Activate(project);

            return true;
        }
        catch (JsonException)
        {
            error = "文件不是有效的 JSON。";

            return false;
        }
    }

    private sealed class ImportPayload
    {
        public int Version { get; set; } = 1;

        public string? Name { get; set; }

        public List<WorkspaceNode>? Nodes { get; set; }

        public List<WorkspaceEdge>? Edges { get; set; }

        public int MaxLayers { get; set; } = 100;
    }

    #endregion

    #region 持久化

    private sealed class Snapshot
    {
        public List<WorkspaceProject> Projects { get; set; } = new();

        public List<WorkspaceItem> Materials { get; set; } = new();

        public string? ActiveId { get; set; }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                Snapshot? snapshot = JsonSerializer.Deserialize<Snapshot>(
                    File.ReadAllText(_filePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (snapshot is not null)
                {
                    _projects = snapshot.Projects ?? new List<WorkspaceProject>();
                    Materials = snapshot.Materials ?? new List<WorkspaceItem>();
                    _activeId = snapshot.ActiveId;
                }
            }
        }
        catch
        {
            /*
             * 文件损坏时从空白开始。
             */
        }

        if (_projects.Count == 0)
        {
            _projects.Add(new WorkspaceProject { Name = "工作区 1" });
        }

        _active = _projects.FirstOrDefault(project => project.Id == _activeId)
                  ?? _projects[0];

        _activeId = _active.Id;

        // 加载后顺手把旧版 ref 槽位迁移成结果节点上的输入记录
        MigrateRefSlots();
    }

    private void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(new Snapshot
                {
                    Projects = _projects,
                    Materials = Materials,
                    ActiveId = _active?.Id
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
    }

    #endregion
}

/// <summary>
/// 素材（全局共享）。
/// </summary>
public sealed class WorkspaceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public ShortCodeFormat Format { get; set; } = ShortCodeFormat.Full;

    public int Layers { get; set; }

    public int Parts { get; set; }

    public string Source { get; set; } = "editor";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DimensionText => $"{Layers} 层 × {Parts} 象限";
}

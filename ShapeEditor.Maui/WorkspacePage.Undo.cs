using ShapeEditor.Core;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区的快照式撤销，做法与 Web 的 <c>WorkspaceStore.Graph.cs</c> 一致：
/// 每次改动图结构之前记下整张图，撤销时整图还原。
///
/// 快照内容：节点集合、连线集合、高度上限、下落规则、选中节点 Id；
/// 栈上限 100，只保留当前工作区的历史（切换 / 新建 / 删除工作区后清空）。
///
/// 纯视觉操作（拖动节点位置、缩放画布）不压栈，与 Web 相同。
/// </summary>
public partial class WorkspacePage
{
    /// <summary>撤销栈上限，与 Web / 编辑器页一致。</summary>
    private const int MaxUndoCount = 100;

    /// <summary>画布修改前的快照，栈顶是最近一次。</summary>
    private readonly Stack<WorkspaceGraphSnapshot> _undoStack = new();

    /// <summary>当前是否有可撤销的操作（工具条「撤销」按钮的可用状态）。</summary>
    private bool CanUndo => _undoStack.Count > 0;

    /// <summary>上一次渲染时的工作区 Id，用来发现「在别处换了工作区」。</summary>
    private string? _undoProjectId;

    /// <summary>
    /// 渲染入口统一检查：工作区换了（切换 / 新建 / 删除 / 导入）就丢掉旧快照。
    /// 这样无论从哪个入口切工作区，撤销历史都不会跨工作区生效（与 Web 一致）。
    /// </summary>
    private void SyncUndoHistoryProject()
    {
        string? current = _store?.Active?.Id;

        if (_undoProjectId == current)
        {
            return;
        }

        _undoProjectId = current;

        ClearUndoHistory();
    }

    /// <summary>
    /// 记下当前状态，作为一次修改的撤销起点。
    ///
    /// 操作结束后交给 <see cref="CommitGraphEdit"/> 判断有没有真的改到图：
    /// 没改动（例如命中了已存在的运算结果、确认框被取消）就不压栈。
    /// </summary>
    private WorkspaceGraphSnapshot CaptureSnapshot()
    {
        WorkspaceProject? project = _store?.Active;

        return new WorkspaceGraphSnapshot
        {
            ProjectId = project?.Id ?? "",
            Nodes = project is null
                ? new List<WorkspaceNode>()
                : project.Nodes.Select(CloneNode).ToList(),
            Edges = project is null
                ? new List<WorkspaceEdge>()
                : project.Edges.Select(CloneEdge).ToList(),
            MaxLayers = project?.MaxLayers ?? 0,
            Mode = project?.Mode ?? CollapseMode.IncludeCycles,
            SelectedNodeId = _store?.SelectedNode?.Id
        };
    }

    /// <summary>
    /// 一次修改收尾：图确实变了才把 <paramref name="before"/> 压进撤销栈。
    /// 与 Web 一样没有重做栈，新操作只丢弃更早的快照。
    /// </summary>
    private void CommitGraphEdit(
        string label,
        WorkspaceGraphSnapshot before)
    {
        if (!GraphChanged(before))
        {
            return;
        }

        before.Label = label;

        _undoStack.Push(before);

        while (_undoStack.Count > MaxUndoCount)
        {
            RemoveOldest(_undoStack);
        }

        UndoButton.IsEnabled = CanUndo;
    }

    /// <summary>
    /// 执行一次会改图的操作用：操作前后自动处理快照压栈。
    /// </summary>
    private void RunWithUndo(
        string label,
        Action action)
    {
        WorkspaceGraphSnapshot before = CaptureSnapshot();

        action();

        CommitGraphEdit(label, before);
    }

    /// <summary>当前图与快照相比是否有实质改动（选中项变化不算）。</summary>
    private bool GraphChanged(
        WorkspaceGraphSnapshot before)
    {
        WorkspaceProject? project = _store?.Active;

        // 没有工作区、或操作过程中切走了工作区：这次改动不该进撤销栈
        if (project is null || before.ProjectId != project.Id)
        {
            return false;
        }

        if (before.MaxLayers != project.MaxLayers ||
            before.Mode != project.Mode)
        {
            return true;
        }

        if (before.Nodes.Count != project.Nodes.Count ||
            before.Edges.Count != project.Edges.Count)
        {
            return true;
        }

        for (int index = 0; index < before.Nodes.Count; index++)
        {
            if (!SameNode(before.Nodes[index], project.Nodes[index]))
            {
                return true;
            }
        }

        for (int index = 0; index < before.Edges.Count; index++)
        {
            if (!SameEdge(before.Edges[index], project.Edges[index]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 撤销最近一次画布修改（生成结果、删除、清空、规则 / 上限变更等）。
    /// 与 Web 一致：撤销后重绘并恢复当时的选中节点。
    /// </summary>
    private void OnUndo(
        object? sender,
        EventArgs e)
    {
        if (_store is null || _undoStack.Count == 0)
        {
            return;
        }

        WorkspaceGraphSnapshot snapshot = _undoStack.Pop();

        // 快照只对捕获时的工作区有效
        if (snapshot.ProjectId != _store.Active?.Id)
        {
            ClearUndoHistory();

            Show("已切换工作区，撤销历史已清空。");

            return;
        }

        // 撤销会换掉节点实例：先退出堆叠 / 交换的第二输入状态，避免指向旧节点
        _stackFirstId = null;
        _exchangeFirstId = null;

        RestoreSnapshot(snapshot);

        // 放在重绘之后，避免被 RenderCore 的状态栏覆盖
        StatusLabel.Text = $"已撤销：{snapshot.Label}";

        UpdateActionButtons();
    }

    /// <summary>
    /// 整图还原：节点 / 连线 / 高度上限 / 下落规则 / 选中节点全部回到快照状态。
    /// </summary>
    private void RestoreSnapshot(
        WorkspaceGraphSnapshot snapshot)
    {
        if (_store?.Active is not WorkspaceProject project)
        {
            return;
        }

        bool wasRendering = _rendering;

        _rendering = true;

        try
        {
            /*
             * 先 ClearNodes()：它会把节点、连线、选中一起清掉，
             * 同时清空 Core 内部按节点 Id 缓存的图形。
             * 少了这一步，撤销「切换下落规则 / 修改高度上限」之后，
             * 结果节点会继续显示重算后的图形，看起来像没撤销。
             */
            _store.ClearNodes();

            // 快照里的节点是深拷贝，每次还原再拷一次，栈里的对象不会被改动
            project.Nodes.AddRange(snapshot.Nodes.Select(CloneNode));

            project.Edges.AddRange(snapshot.Edges.Select(CloneEdge));

            project.MaxLayers = snapshot.MaxLayers;

            project.Mode = snapshot.Mode;

            _store.SelectNode(snapshot.SelectedNodeId is null
                ? null
                : _store.FindNode(snapshot.SelectedNodeId));

            PersistWorkspace(project);
        }
        finally
        {
            _rendering = wasRendering;
        }

        Render();
    }

    /// <summary>
    /// 让 Core 把当前工作区落盘。
    ///
    /// Core 的 <see cref="WorkspaceStore"/> 没有公开 Save()，
    /// 这里借「同名重命名」触发一次完整保存（名字不变，只是借用它的持久化路径）；
    /// 名字为空时退回「先改上限再改回」，两次都会落盘。
    /// </summary>
    private void PersistWorkspace(
        WorkspaceProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.Name))
        {
            _store!.RenameProject(project.Id, project.Name);

            return;
        }

        int layers = project.MaxLayers;

        _store!.SetMaxLayers(
            layers >= ShapeRules.MaxLayerCount ? ShapeRules.MinLayerCount : layers + 1);

        _store.SetMaxLayers(layers);
    }

    /// <summary>
    /// 切换 / 新建 / 删除工作区后，旧快照不再适用（与 Web 的 ClearUndoHistory 一致）。
    /// </summary>
    private void ClearUndoHistory()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _undoStack.Clear();

        UndoButton.IsEnabled = false;
    }

    /// <summary>丢掉栈底最旧的一条快照（Stack 没有直接删除栈底的接口）。</summary>
    private static void RemoveOldest(
        Stack<WorkspaceGraphSnapshot> stack)
    {
        WorkspaceGraphSnapshot[] items = stack.ToArray();

        stack.Clear();

        // ToArray 从栈顶开始，数组最后一项就是最旧的快照，跳过它重建
        for (int index = items.Length - 2; index >= 0; index--)
        {
            stack.Push(items[index]);
        }
    }

    private static WorkspaceNode CloneNode(
        WorkspaceNode node)
    {
        return new WorkspaceNode
        {
            Id = node.Id,
            Kind = node.Kind,
            Code = node.Code,
            Format = node.Format,
            Layers = node.Layers,
            Parts = node.Parts,
            IsResult = node.IsResult,
            OpKey = node.OpKey,
            TargetId = node.TargetId,
            BaseId = node.BaseId,
            InputAId = node.InputAId,
            InputBId = node.InputBId,
            StackBaseIsTop = node.StackBaseIsTop,
            TopInput = node.TopInput,
            Seq = node.Seq,
            Row = node.Row,
            Col = node.Col,
            X = node.X,
            Y = node.Y,
            Label = node.Label
        };
    }

    private static WorkspaceEdge CloneEdge(
        WorkspaceEdge edge)
    {
        return new WorkspaceEdge
        {
            FromId = edge.FromId,
            ToId = edge.ToId,
            Label = edge.Label,
            LabelT = edge.LabelT
        };
    }

    /// <summary>逐个字段比较，判断节点有没有被改动过。</summary>
    private static bool SameNode(
        WorkspaceNode left,
        WorkspaceNode right)
    {
        return left.Id == right.Id &&
               left.Kind == right.Kind &&
               left.Code == right.Code &&
               left.Format == right.Format &&
               left.Layers == right.Layers &&
               left.Parts == right.Parts &&
               left.IsResult == right.IsResult &&
               left.OpKey == right.OpKey &&
               left.TargetId == right.TargetId &&
               left.BaseId == right.BaseId &&
               left.InputAId == right.InputAId &&
               left.InputBId == right.InputBId &&
               left.StackBaseIsTop == right.StackBaseIsTop &&
               left.TopInput == right.TopInput &&
               left.Seq == right.Seq &&
               left.Row == right.Row &&
               left.Col == right.Col &&
               left.X == right.X &&
               left.Y == right.Y &&
               left.Label == right.Label;
    }

    private static bool SameEdge(
        WorkspaceEdge left,
        WorkspaceEdge right)
    {
        return left.FromId == right.FromId &&
               left.ToId == right.ToId &&
               left.Label == right.Label &&
               left.LabelT == right.LabelT;
    }

    /// <summary>
    /// 一次画布修改前的整图快照，字段与 Web 的 <c>GraphSnapshot</c> 对应。
    /// </summary>
    private sealed class WorkspaceGraphSnapshot
    {
        /// <summary>快照属于哪个工作区；换了工作区就作废。</summary>
        public string ProjectId { get; init; } = "";

        public List<WorkspaceNode> Nodes { get; init; } = new();

        public List<WorkspaceEdge> Edges { get; init; } = new();

        public int MaxLayers { get; init; }

        public CollapseMode Mode { get; init; }

        public string? SelectedNodeId { get; init; }

        /// <summary>这次操作的名字，撤销后显示在状态栏。</summary>
        public string Label { get; set; } = "";
    }
}

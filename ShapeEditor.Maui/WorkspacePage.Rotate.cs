using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区「旋转」：把选中节点的图形顺时针旋转 1 个象限，
/// 生成一个新的「旋转」结果节点，并排在源节点正下方。
///
/// 与 Web 版 <c>WorkspaceStore.ApplyRotate</c> 的行为一致：
/// 同一源节点只生成一次，重复点击只定位到已有结果。
/// </summary>
public partial class WorkspacePage
{
    /// <summary>结果节点的标题，同时用作「已旋转过」的识别依据。</summary>
    private const string RotateLabel = "旋转";

    /// <summary>连线标签，与结果节点标题一致（Web 版同为「旋转」）。</summary>
    private const string RotateEdgeLabel = "旋转";

    private readonly ShortCodeExporter _rotateExporter = new();

    /// <summary>
    /// 旋转工具条按钮：无选中节点时在 <see cref="UpdateActionButtons"/> 里禁用。
    /// 工作区的节点运算目前都不进撤销栈（MAUI 工作区没有撤销按钮，
    /// Web 的撤销由 Web 自己的 store 提供），这里与 下落 / 顶针 / 切割 一样，
    /// 直接改图后落盘并触发重绘。
    /// </summary>
    private void OnRotateNode(
        object? sender,
        EventArgs e)
    {
        if (_store?.SelectedNode is not WorkspaceNode node)
        {
            return;
        }

        // 幂等：同一个源节点已经旋转过时，只定位到已有的结果节点
        WorkspaceNode? existing = FindRotateResult(node.Id);

        if (existing is not null)
        {
            _store.SelectNode(existing);

            Show("该节点已经旋转过，已定位到结果。");

            return;
        }

        ShapeDocument? document = _store.GetNodeDocument(node);

        if (document is null)
        {
            Show("该节点还没有图形。");

            return;
        }

        WorkspaceNodeBox? source = FindBox(node.Id);

        if (source is null)
        {
            return;
        }

        // 顺时针 1 个象限；只重排象限，层数与象限数都不变
        ShapeDocument rotated = ShapeOps.Rotate(document, 1);

        WorkspaceNode created = new()
        {
            Kind = "shape",
            Row = node.Row + 1,
            Col = node.Col,
            Seq = NextNodeSeq(),
            IsResult = true,
            Label = RotateLabel,

            /*
             * OpKey 故意留空：Core 的 RecomputeAll 只认 pin / cutw / cute / stack /
             * 交换这几种键，其余一律按「下落」重算。旋转结果一旦带上 OpKey，
             * 切换下落规则或高度上限时就会被错误地当成下落结果。
             * 父节点记在 InputAId 上，既方便识别重复旋转，也为将来 Core 支持
             * rotate 重算留好数据。
             */
            OpKey = null,
            InputAId = node.Id,
            Code = _rotateExporter.Export(rotated, omitTopEmptyLayers: true),
            Format = ShortCodeFormat.Full,
            Layers = rotated.LayerCount,
            Parts = rotated.PartCount
        };

        _store.Active!.Nodes.Add(created);

        AddRotateEdge(node.Id, created.Id);

        /*
         * 与「切割结果」一样排在源节点下方：X 对齐源节点，
         * Y = 源节点底边 + 结果间距。SetNodePosition 会按 10px 吸附、
         * 落盘并触发 Changed 重绘，同时把新节点写进工作区 JSON。
         */
        _store.SetNodePosition(
            created.Id,
            source.Bounds.X,
            source.Bounds.Y + source.Bounds.Height + ResultGap);

        _store.SelectNode(created);
    }

    /// <summary>
    /// 找某个源节点已经生成的旋转结果。
    /// 识别依据是「父节点 + 标题」，不依赖 OpKey（见 <see cref="OnRotateNode"/> 的说明）。
    /// </summary>
    private WorkspaceNode? FindRotateResult(
        string parentId)
    {
        if (_store is null)
        {
            return null;
        }

        foreach (WorkspaceNode node in _store.Nodes)
        {
            if (node.Id != parentId &&
                node.InputAId == parentId &&
                node.Label == RotateLabel)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// 追加「源节点 → 旋转结果」的连线；同一条连线只加一次。
    /// </summary>
    private void AddRotateEdge(
        string fromId,
        string toId)
    {
        WorkspaceProject project = _store!.Active!;

        foreach (WorkspaceEdge edge in project.Edges)
        {
            if (edge.FromId == fromId &&
                edge.ToId == toId &&
                edge.Label == RotateEdgeLabel)
            {
                return;
            }
        }

        project.Edges.Add(new WorkspaceEdge
        {
            FromId = fromId,
            ToId = toId,
            Label = RotateEdgeLabel
        });
    }

    /// <summary>
    /// 下一个节点编号，规则与 Core 的 NextSeq 相同（当前最大编号 + 1）。
    /// </summary>
    private int NextNodeSeq()
    {
        int max = 0;

        foreach (WorkspaceNode node in _store!.Nodes)
        {
            if (node.Seq > max)
            {
                max = node.Seq;
            }
        }

        return max + 1;
    }
}

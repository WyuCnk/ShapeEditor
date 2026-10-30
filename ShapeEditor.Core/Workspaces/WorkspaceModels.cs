using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Workspaces;

/// <summary>
/// 画布上的一个节点。
///
/// kind = shape：有具体图形（素材导入或运算结果）。
/// kind = ref  ：堆叠的第二输入占位，指向另一个节点或从素材导入。
/// </summary>
public sealed class WorkspaceNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = "shape";

    public string Code { get; set; } = "";

    public Models.ShortCodeFormat Format { get; set; } = Models.ShortCodeFormat.Full;

    public int Layers { get; set; }

    public int Parts { get; set; }

    public bool IsResult { get; set; }

    public string? OpKey { get; set; }

    public string? TargetId { get; set; }

    public string? BaseId { get; set; }

    /// <summary>主输入节点（下落 / 顶针 / 切割，堆叠时是第一个输入）。</summary>
    public string? InputAId { get; set; }

    /// <summary>堆叠的第二输入节点；单输入运算时为空。</summary>
    public string? InputBId { get; set; }

    /// <summary>堆叠时 InputAId 对应的节点是否为上层。</summary>
    public bool StackBaseIsTop { get; set; }

    /// <summary>ref 槽位专用：槽位的底节点是否为上层（迁移时写入 StackBaseIsTop）。</summary>
    public bool TopInput { get; set; }

    /// <summary>画布上的唯一编号，用于引用与显示。</summary>
    public int Seq { get; set; }

    public int Row { get; set; }

    public int Col { get; set; }

    /// <summary>自由布局坐标；为空时由 Row/Col 折算。</summary>
    public double? X { get; set; }

    public double? Y { get; set; }

    public string Label { get; set; } = "";

    public bool HasShape =>
        !string.IsNullOrWhiteSpace(Code) && Layers > 0 && Parts > 0;
}

/// <summary>
/// 节点之间的带标签连线。
/// </summary>
public sealed class WorkspaceEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>标签在连线上的位置比例（0~1）。</summary>
    public double LabelT { get; set; } = 0.5;
}

/// <summary>
/// 一个工作区：独立的画布（节点与连线）+ 自己的高度上限与下落规则。
/// 素材库是全局共享的，不属于工作区。
/// </summary>
public sealed class WorkspaceProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "工作区";

    public List<WorkspaceNode> Nodes { get; set; } = new();

    public List<WorkspaceEdge> Edges { get; set; } = new();

    public int MaxLayers { get; set; } = 4;

    /// <summary>该工作区固定的象限数；空工作区为 4。</summary>
    public int PartCount { get; set; } = 4;

    public CollapseMode Mode { get; set; } = CollapseMode.IncludeCycles;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

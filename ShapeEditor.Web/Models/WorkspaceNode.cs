using ShapeEditor.Core.Models;

namespace ShapeEditor.Web.Models;

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

    public ShortCodeFormat Format { get; set; } = ShortCodeFormat.Full;

    public int Layers { get; set; }

    public int Parts { get; set; }

    /// <summary>
    /// 运算结果节点：不可从素材导入，但可以继续参与运算。
    /// </summary>
    public bool IsResult { get; set; }

    /// <summary>
    /// 运算键，用于重复点击时定位而不是重建。
    /// </summary>
    public string? OpKey { get; set; }

    /// <summary>
    /// kind=ref 时指向的节点。
    /// </summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// ref 节点所属的堆叠底节点。
    /// </summary>
    public string? BaseId { get; set; }

    /// <summary>
    /// 该槽位代表堆叠的上方输入还是下方输入。
    /// </summary>
    public bool TopInput { get; set; }

    /// <summary>画布上的唯一编号，用于引用与显示。</summary>
    public int Seq { get; set; }

    public int Row { get; set; }

    public int Col { get; set; }

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
}

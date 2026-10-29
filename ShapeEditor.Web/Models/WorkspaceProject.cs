using ShapeEditor.Core.Physics;

namespace ShapeEditor.Web.Models;

/// <summary>
/// 一个工作区：独立的画布（节点与连线）+ 自己的高度上限。
/// 素材库是全局共享的，不属于工作区。
/// </summary>
public sealed class WorkspaceProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "工作区";

    public List<WorkspaceNode> Nodes { get; set; } = new();

    public List<WorkspaceEdge> Edges { get; set; } = new();

    public int MaxLayers { get; set; } = 100;

    /// <summary>
    /// 该工作区使用的下落规则。
    /// </summary>
    public CollapseMode Mode { get; set; } = CollapseMode.IncludeCycles;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

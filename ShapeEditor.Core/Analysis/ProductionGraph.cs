using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 制作路径节点图里的节点类型。
/// </summary>
public enum ProductionNodeKind
{
    /// <summary>制作带柱子的半半边。</summary>
    PillarPiece,

    /// <summary>旋转（预旋转或交换后转回）。</summary>
    Rotate,

    /// <summary>交换，节点图形是该次交换的所需输出。</summary>
    Exchange,

    /// <summary>最终目标图形。</summary>
    Target,

    /// <summary>交换的副产物（废料）。</summary>
    Waste,

    /// <summary>旧骨架的占位值，保留以兼容现有调用；新路径不再产生。</summary>
    Pending,

    /// <summary>顶针：把内层产物整体上移一层。</summary>
    Pin,

    /// <summary>堆叠：在内层产物上压一块 hybrid。</summary>
    Stack,

    /// <summary>可独立制作的 hybrid 块。</summary>
    Block
}

/// <summary>
/// 制作路径节点图里的一个节点。
///
/// <see cref="Row"/> 是从 1 开始的分层号，不一定连续：
/// 纯交换路径仍是 1–5（1 = 带柱子的半半边，2 = 预旋转结果，3 = 两边交换（含废料），
/// 4 = 交换后转回的两个半边，5 = 末端交换的目标图形与废料）；
/// 顶针 / 堆叠会继续追加 6、7、…… 层。
/// <see cref="Shape"/> 是该节点展示的图形，废料节点同样带图形。
/// </summary>
public sealed record ProductionNode(
    string Id,
    string Title,
    ProductionNodeKind Kind,
    int Row,
    ShapeDocument? Shape);

/// <summary>
/// 节点图里的一条有向边：从 <see cref="FromId"/> 流向 <see cref="ToId"/>。
/// </summary>
public sealed record ProductionEdge(
    string FromId,
    string ToId);

/// <summary>
/// 制作路径的图结构。
///
/// 与线性 <see cref="ProductionPath.Steps"/> 不同，这里保留交换的
/// 两个输出（所需结果 + 废料），并显式记录汇入/分出的连接关系，
/// Web 端据此绘制分层箭头图。
/// </summary>
public sealed record ProductionGraph(
    IReadOnlyList<ProductionNode> Nodes,
    IReadOnlyList<ProductionEdge> Edges)
{
    /// <summary>空图，供不可制作时兜底。</summary>
    public static ProductionGraph Empty { get; } = new(
        Array.Empty<ProductionNode>(),
        Array.Empty<ProductionEdge>());
}

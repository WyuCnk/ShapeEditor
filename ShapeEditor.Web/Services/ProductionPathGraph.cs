using System.Globalization;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Web.Services;

/// <summary>
/// 制作路径节点图里的一个节点。
///
/// 尺寸与工作区节点一致：宽 = 象限数 × 30 + 16，高 = 50 + 层数 × 30，
/// 预览按 1:1 的 30px 单元格绘制，不做缩放。
/// </summary>
public sealed class ProductionPathGraphNode
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public ShapeDocument? Shape { get; init; }

    public bool IsResult { get; init; }

    public bool IsPending { get; init; }
}

/// <summary>
/// 节点之间的一条箭头。制作路径是线性工序，按步骤顺序首尾相连。
///
/// <see cref="Path"/> 与 <see cref="Arrow"/> 是首帧的初始几何，
/// 页面渲染后由 shapeGridLayoutEdges 按真实矩形再校正一次。
/// </summary>
public sealed record ProductionPathGraphEdge(
    string FromId,
    string ToId,
    string Path,
    string Arrow);

/// <summary>
/// 制作路径节点图的排布结果。
///
/// 复用 Core 的 <see cref="WorkspaceStore.PlanAutoLayout"/>，把每个步骤当成
/// 一个工作区节点，按步骤顺序连成一条链，得到与工作区完全一致的自动布局。
/// </summary>
public sealed class ProductionPathGraph
{
    /// <summary>与工作区节点一致的单元格边长。</summary>
    public const int CellSize = 30;

    /// <summary>节点标题区高度，与工作区节点一致。</summary>
    public const int HeaderHeight = 50;

    /// <summary>节点左右内边距合计：宽度 = 象限数 × 30 + 16。</summary>
    public const int HorizontalPadding = 16;

    /// <summary>整张图右侧/底部的留白。</summary>
    public const int GraphMargin = 24;

    public IReadOnlyList<ProductionPathGraphNode> Nodes { get; private init; } = Array.Empty<ProductionPathGraphNode>();

    public IReadOnlyList<ProductionPathGraphEdge> Edges { get; private init; } = Array.Empty<ProductionPathGraphEdge>();

    public double Width { get; private init; }

    public double Height { get; private init; }

    public static ProductionPathGraph? Create(
        IReadOnlyList<ProductionStep> steps)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        var layoutNodes = new List<WorkspaceNode>(steps.Count);

        var edgePairs = new List<(string FromId, string ToId)>(Math.Max(0, steps.Count - 1));

        for (int index = 0; index < steps.Count; index++)
        {
            ProductionStep step = steps[index];

            int parts = step.Shape?.PartCount ?? 4;

            int layers = Math.Max(1, step.Shape?.LayerCount ?? 1);

            layoutNodes.Add(new WorkspaceNode
            {
                Id = $"path-{index}",
                Seq = index + 1,
                Kind = "shape",

                // PlanAutoLayout 依据 HasShape 决定占位尺寸；
                // 这里不解析代码，只让节点被视为「有图形」。
                Code = "step",
                Parts = parts,
                Layers = layers,
                Label = step.Title
            });

            if (index > 0)
            {
                edgePairs.Add(($"path-{index - 1}", $"path-{index}"));
            }
        }

        var layoutEdges = edgePairs
            .Select(pair => new WorkspaceEdge
            {
                FromId = pair.FromId,
                ToId = pair.ToId
            })
            .ToList();

        IReadOnlyDictionary<string, (double X, double Y)> plan =
            ShapeEditor.Core.Workspaces.WorkspaceStore.PlanAutoLayout(
                layoutNodes,
                layoutEdges);

        var nodes = new List<ProductionPathGraphNode>(steps.Count);

        double width = 0;

        double height = 0;

        for (int index = 0; index < steps.Count; index++)
        {
            ProductionStep step = steps[index];

            WorkspaceNode layoutNode = layoutNodes[index];

            if (!plan.TryGetValue(layoutNode.Id, out (double X, double Y) point))
            {
                continue;
            }

            double nodeWidth = layoutNode.Parts * CellSize + HorizontalPadding;

            double nodeHeight = HeaderHeight + layoutNode.Layers * CellSize;

            nodes.Add(new ProductionPathGraphNode
            {
                Id = layoutNode.Id,
                Title = step.Title,
                X = point.X,
                Y = point.Y,
                Width = nodeWidth,
                Height = nodeHeight,
                Shape = step.Shape,
                IsResult = step.Kind is ProductionStepKind.Target or ProductionStepKind.Pending,
                IsPending = step.Kind == ProductionStepKind.Pending
            });

            width = Math.Max(width, point.X + nodeWidth);

            height = Math.Max(height, point.Y + nodeHeight);
        }

        var nodesById = nodes.ToDictionary(node => node.Id);

        var edges = new List<ProductionPathGraphEdge>(edgePairs.Count);

        foreach ((string fromId, string toId) in edgePairs)
        {
            if (!nodesById.TryGetValue(fromId, out ProductionPathGraphNode? from) ||
                !nodesById.TryGetValue(toId, out ProductionPathGraphNode? to))
            {
                continue;
            }

            (string path, string arrow) = BuildEdge(from, to);

            edges.Add(new ProductionPathGraphEdge(fromId, toId, path, arrow));
        }

        return new ProductionPathGraph
        {
            Nodes = nodes,
            Edges = edges,
            Width = width + GraphMargin,
            Height = height + GraphMargin
        };
    }

    /// <summary>
    /// 首帧箭头几何：取中心连心线与节点矩形边界的交点，
    /// 与工作区页面 / shapeGridLayoutEdges 的算法一致。
    /// </summary>
    private static (string Path, string Arrow) BuildEdge(
        ProductionPathGraphNode from,
        ProductionPathGraphNode to)
    {
        (double x1, double y1) = Anchor(from, to);

        (double x2, double y2) = Anchor(to, from);

        double dx = x2 - x1;
        double dy = y2 - y1;

        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1)
        {
            length = 1;
        }

        double ux = dx / length;
        double uy = dy / length;

        double startX = x1 - ux;
        double startY = y1 - uy;

        double tipX = x2 + ux;
        double tipY = y2 + uy;

        string path =
            $"M {Number(startX)} {Number(startY)} L {Number(tipX)} {Number(tipY)}";

        const double head = 11;
        const double half = 5;

        double bx = tipX - ux * head;
        double by = tipY - uy * head;

        string arrow =
            $"{Number(tipX)},{Number(tipY)} " +
            $"{Number(bx - uy * half)},{Number(by + ux * half)} " +
            $"{Number(bx + uy * half)},{Number(by - ux * half)}";

        return (path, arrow);
    }

    private static (double X, double Y) Anchor(
        ProductionPathGraphNode node,
        ProductionPathGraphNode other)
    {
        double cx = node.X + node.Width / 2;
        double cy = node.Y + node.Height / 2;

        double ox = other.X + other.Width / 2;
        double oy = other.Y + other.Height / 2;

        double dx = ox - cx;
        double dy = oy - cy;

        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1)
        {
            return (cx, cy + node.Height / 2);
        }

        dx /= length;
        dy /= length;

        double scaleX = Math.Abs(dx) < 1e-6
            ? double.MaxValue
            : (node.Width / 2) / Math.Abs(dx);

        double scaleY = Math.Abs(dy) < 1e-6
            ? double.MaxValue
            : (node.Height / 2) / Math.Abs(dy);

        double scale = Math.Min(scaleX, scaleY);

        return (cx + dx * scale, cy + dy * scale);
    }

    private static string Number(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);
}

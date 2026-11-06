using System.Globalization;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;

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

    public ProductionNodeKind Kind { get; init; }

    /// <summary>层号：从 1 开始，纯交换路径为 1–5，顶针/堆叠会继续追加。</summary>
    public int Row { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; init; }

    public double Height { get; init; }

    public ShapeDocument? Shape { get; init; }

    public bool IsResult => Kind == ProductionNodeKind.Target;

    /// <summary>废料节点：交换的另一路输出。</summary>
    public bool IsWaste => Kind == ProductionNodeKind.Waste;

    public bool IsPending => Kind == ProductionNodeKind.Pending;

    /// <summary>顶针工序：青绿色边框 + 右上角「顶P」标签。</summary>
    public bool IsPin => Kind == ProductionNodeKind.Pin;

    /// <summary>堆叠工序：橙色边框 + 右上角「堆」标签。</summary>
    public bool IsStack => Kind == ProductionNodeKind.Stack;

    /// <summary>可独立制作的 hybrid 块：绿色边框 + 右上角「块」标签。</summary>
    public bool IsBlock => Kind == ProductionNodeKind.Block;

    /// <summary>是否有类型小标签；只有顶针 / 堆叠 / 块需要。</summary>
    public bool HasKindTag => KindTag.Length > 0;

    /// <summary>类型小标签文本，其余节点为空。</summary>
    public string KindTag => Kind switch
    {
        ProductionNodeKind.Pin => "顶P",
        ProductionNodeKind.Stack => "堆",
        ProductionNodeKind.Block => "块",
        _ => ""
    };

    /// <summary>节点类型对应的样式类名，其余节点为空。</summary>
    public string KindClass => Kind switch
    {
        ProductionNodeKind.Pin => "kind-pin",
        ProductionNodeKind.Stack => "kind-stack",
        ProductionNodeKind.Block => "kind-block",
        _ => ""
    };

    /// <summary>节点类型标识，供导出图选择边框色：pin / stack / block。</summary>
    public string KindName => Kind switch
    {
        ProductionNodeKind.Pin => "pin",
        ProductionNodeKind.Stack => "stack",
        ProductionNodeKind.Block => "block",
        _ => ""
    };
}

/// <summary>
/// 节点之间的一条箭头。
///
/// <see cref="Path"/> 与 <see cref="Arrow"/> 是首帧的初始几何，
/// 页面渲染后由 shapeGridLayoutEdges 按真实矩形再校正一次。
/// 指向废料节点的箭头 <see cref="IsWaste"/> 为 true，渲染成灰色虚线。
/// </summary>
public sealed record ProductionPathGraphEdge(
    string FromId,
    string ToId,
    string Path,
    string Arrow,
    bool IsWaste);

/// <summary>
/// 制作路径节点图的排布结果。
///
/// 按 <see cref="ProductionNode.Row"/> 分层（从 1 开始，不一定连续），每层内节点按 Core 给出的
/// 顺序从左到右紧排；层与层之间固定留 80px，行数由 Core 决定（顶针 / 堆叠 / 块会追加新层），
/// 整张图按所有层里最宽的一层确定宽度，较窄的层水平居中。
/// 含目标图形的层把目标放在中线上，废料排在它右侧。
/// </summary>
public sealed class ProductionPathGraph
{
    /// <summary>与工作区节点一致的单元格边长。</summary>
    public const int CellSize = 30;

    /// <summary>节点标题区高度，与工作区节点一致。</summary>
    public const int HeaderHeight = 50;

    /// <summary>节点左右内边距合计：宽度 = 象限数 × 30 + 16。</summary>
    public const int HorizontalPadding = 16;

    /// <summary>行内节点之间的水平间距。</summary>
    public const int ColumnGap = 40;

    /// <summary>相邻两层之间的固定垂直间距。</summary>
    public const int RowGap = 80;

    /// <summary>整张图四周的留白。</summary>
    public const int GraphMargin = 24;

    public IReadOnlyList<ProductionPathGraphNode> Nodes { get; private init; } = Array.Empty<ProductionPathGraphNode>();

    public IReadOnlyList<ProductionPathGraphEdge> Edges { get; private init; } = Array.Empty<ProductionPathGraphEdge>();

    public double Width { get; private init; }

    public double Height { get; private init; }

    public static ProductionPathGraph? Create(
        ProductionPath path)
    {
        if (path.Nodes.Count == 0)
        {
            return null;
        }

        var nodes = new List<ProductionPathGraphNode>(path.Nodes.Count);

        foreach (ProductionNode node in path.Nodes)
        {
            int parts = node.Shape?.PartCount ?? 4;

            int layers = Math.Max(1, node.Shape?.LayerCount ?? 1);

            nodes.Add(new ProductionPathGraphNode
            {
                Id = node.Id,
                Title = node.Title,
                Kind = node.Kind,
                Row = node.Row,
                Width = parts * CellSize + HorizontalPadding,
                Height = HeaderHeight + layers * CellSize,
                Shape = node.Shape
            });
        }

        var rows = nodes
            .GroupBy(node => node.Row)
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToList();

        if (rows.Count == 0)
        {
            return null;
        }

        /*
         * 每层先按实际宽度紧排，最宽的一层决定整张图的宽度。
         * 含目标图形的层会把目标单独居中、左右节点向外展开，
         * 展开后的实际宽度可能比紧排更宽，所以这里再按目标层补一次宽度，
         * 避免目标层越出画布。行数由 Core 决定，这里不假设固定 5 层。
         */
        double contentWidth = rows
            .Select(RowWidth)
            .Max();

        contentWidth = Math.Max(
            contentWidth,
            rows.Select(TargetRowWidth).DefaultIfEmpty(0).Max());

        double contentHeight = rows
            .Select(RowHeight)
            .Sum() +
            RowGap * (rows.Count - 1);

        double top = GraphMargin;

        for (int index = 0; index < rows.Count; index++)
        {
            List<ProductionPathGraphNode> row = rows[index];

            PlaceRow(row, contentWidth, ref top);

            top += RowGap;
        }

        var nodesById = nodes.ToDictionary(node => node.Id);

        var edges = new List<ProductionPathGraphEdge>(path.Edges.Count);

        foreach (ProductionEdge edge in path.Edges)
        {
            if (!nodesById.TryGetValue(edge.FromId, out ProductionPathGraphNode? from) ||
                !nodesById.TryGetValue(edge.ToId, out ProductionPathGraphNode? to))
            {
                continue;
            }

            (string pathText, string arrow) = BuildEdge(from, to);

            edges.Add(new ProductionPathGraphEdge(
                edge.FromId,
                edge.ToId,
                pathText,
                arrow,
                to.IsWaste));
        }

        return new ProductionPathGraph
        {
            Nodes = nodes,
            Edges = edges,
            Width = contentWidth + GraphMargin * 2,
            Height = contentHeight + GraphMargin * 2
        };
    }

    private static double RowWidth(
        IReadOnlyList<ProductionPathGraphNode> row)
    {
        double width = 0;

        for (int index = 0; index < row.Count; index++)
        {
            width += row[index].Width;

            if (index > 0)
            {
                width += ColumnGap;
            }
        }

        return width;
    }

    /// <summary>
    /// 含目标图形的层的实际宽度。
    ///
    /// 目标居中后左右两侧的节点向外展开，整层宽度取左右两翼中较长的一翼，
    /// 不居中（没有目标）的层仍按紧排宽度。
    /// </summary>
    private static double TargetRowWidth(
        IReadOnlyList<ProductionPathGraphNode> row)
    {
        int targetIndex = -1;

        for (int index = 0; index < row.Count; index++)
        {
            if (row[index].Kind == ProductionNodeKind.Target)
            {
                targetIndex = index;

                break;
            }
        }

        if (targetIndex < 0)
        {
            return RowWidth(row);
        }

        ProductionPathGraphNode target = row[targetIndex];

        double leftExtent = target.Width / 2;

        for (int index = 0; index < targetIndex; index++)
        {
            leftExtent += ColumnGap + row[index].Width;
        }

        double rightExtent = target.Width / 2;

        for (int index = targetIndex + 1; index < row.Count; index++)
        {
            rightExtent += ColumnGap + row[index].Width;
        }

        return Math.Max(leftExtent, rightExtent) * 2;
    }

    private static double RowHeight(
        IReadOnlyList<ProductionPathGraphNode> row)
    {
        double height = 0;

        foreach (ProductionPathGraphNode node in row)
        {
            height = Math.Max(height, node.Height);
        }

        return height;
    }

    /// <summary>
    /// 放置一层节点。
    ///
    /// 普通层水平居中；含目标图形的层把目标放在整张图的中线上，
    /// 废料依次排在它右侧，让两条来自旋转后半边的箭头对称地汇入目标。
    /// </summary>
    private static void PlaceRow(
        List<ProductionPathGraphNode> row,
        double contentWidth,
        ref double top)
    {
        double rowWidth = RowWidth(row);

        int targetIndex = row.FindIndex(
            node => node.Kind == ProductionNodeKind.Target);

        if (targetIndex >= 0)
        {
            ProductionPathGraphNode target = row[targetIndex];

            target.X = GraphMargin + contentWidth / 2 - target.Width / 2;

            target.Y = top;

            // 目标左侧的节点从目标左边向左依次排开。
            double left = target.X - ColumnGap;

            for (int index = targetIndex - 1; index >= 0; index--)
            {
                ProductionPathGraphNode node = row[index];

                left -= node.Width;

                node.X = left;
                node.Y = top;

                left -= ColumnGap;
            }

            // 目标右侧的节点（废料）从目标右边向右依次排开。
            double right = target.X + target.Width + ColumnGap;

            for (int index = targetIndex + 1; index < row.Count; index++)
            {
                ProductionPathGraphNode node = row[index];

                node.X = right;
                node.Y = top;

                right += node.Width + ColumnGap;
            }
        }
        else
        {
            double left = GraphMargin + (contentWidth - rowWidth) / 2;

            foreach (ProductionPathGraphNode node in row)
            {
                node.X = left;
                node.Y = top;

                left += node.Width + ColumnGap;
            }
        }

        top += RowHeight(row);
    }

    /// <summary>
    /// 首帧箭头几何。
    ///
    /// 跨层箭头从上排节点底边中点连到下排节点顶边中点，
    /// 一对多/多对一时多条箭头汇入同一点；同层的废料箭头
    /// 则从交换节点的左/右侧边中点连到废料节点相对的侧边中点。
    /// </summary>
    private static (string Path, string Arrow) BuildEdge(
        ProductionPathGraphNode from,
        ProductionPathGraphNode to)
    {
        ((double x1, double y1), (double x2, double y2)) = EdgeAnchors(from, to);

        double dx = x2 - x1;
        double dy = y2 - y1;

        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1)
        {
            length = 1;
        }

        double ux = dx / length;
        double uy = dy / length;

        double startX = x1;
        double startY = y1;

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

    /// <summary>
    /// 分层图的两个锚点：跨层用底边中点 → 顶边中点，
    /// 同层（交换 → 废料）用相对的侧边中点。
    /// </summary>
    private static ((double X, double Y) Start, (double X, double Y) End) EdgeAnchors(
        ProductionPathGraphNode from,
        ProductionPathGraphNode to)
    {
        if (to.Y >= from.Y + from.Height - 0.5)
        {
            return (
                (from.X + from.Width / 2, from.Y + from.Height),
                (to.X + to.Width / 2, to.Y));
        }

        if (from.Y >= to.Y + to.Height - 0.5)
        {
            return (
                (from.X + from.Width / 2, from.Y),
                (to.X + to.Width / 2, to.Y + to.Height));
        }

        bool rightward = to.X >= from.X;

        double fromY = from.Y + from.Height / 2;
        double toY = to.Y + to.Height / 2;

        return rightward
            ? ((from.X + from.Width, fromY), (to.X, toY))
            : ((from.X, fromY), (to.X + to.Width, toY));
    }

    private static string Number(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);
}

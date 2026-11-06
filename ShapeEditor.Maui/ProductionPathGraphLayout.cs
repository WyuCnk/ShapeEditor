using ShapeEditor.Core.Analysis;

namespace ShapeEditor.Maui;

/// <summary>
/// 制作路径节点图里的一个节点盒子。
///
/// 尺寸与工作区节点一致：宽 = 象限数 × 30 + 16，高 = 50 + 层数 × 30；
/// 预览按 1:1 的 30 单元格绘制，不做缩放。
/// </summary>
public sealed class ProductionPathNodeBox
{
    public ProductionPathNodeBox(
        ProductionNode node,
        double width,
        double height)
    {
        Node = node;
        Width = width;
        Height = height;
    }

    /// <summary>Core 给出的节点（标题、类型、层号、图形）。</summary>
    public ProductionNode Node { get; }

    /// <summary>层号：从 1 开始，纯交换路径为 1–5，顶针/堆叠会继续追加。</summary>
    public int Row => Node.Row;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; }

    public double Height { get; }

    public Rect Bounds => new(X, Y, Width, Height);

    /// <summary>最终目标图形节点。</summary>
    public bool IsTarget => Node.Kind == ProductionNodeKind.Target;

    /// <summary>交换的另一路输出，画成淡显的废料。</summary>
    public bool IsWaste => Node.Kind == ProductionNodeKind.Waste;

    /// <summary>旧骨架的占位节点（新路径不再产生）。</summary>
    public bool IsPending => Node.Kind == ProductionNodeKind.Pending;

    /// <summary>顶针工序：青绿色边框 + 右上角「顶P」标签。</summary>
    public bool IsPin => Node.Kind == ProductionNodeKind.Pin;

    /// <summary>堆叠工序：橙色边框 + 右上角「堆」标签。</summary>
    public bool IsStack => Node.Kind == ProductionNodeKind.Stack;

    /// <summary>可独立制作的 hybrid 块：绿色边框 + 右上角「块」标签。</summary>
    public bool IsBlock => Node.Kind == ProductionNodeKind.Block;

    /// <summary>类型小标签文本；其余节点为空。</summary>
    public string KindTag => Node.Kind switch
    {
        ProductionNodeKind.Pin => "顶P",
        ProductionNodeKind.Stack => "堆",
        ProductionNodeKind.Block => "块",
        _ => ""
    };

    public bool HasKindTag => KindTag.Length > 0;
}

/// <summary>
/// 节点图里的一条箭头。指向废料节点的边画成灰色虚线。
/// </summary>
public sealed record ProductionPathEdge(
    ProductionPathNodeBox From,
    ProductionPathNodeBox To)
{
    public bool IsWaste => To.IsWaste;
}

/// <summary>
/// 制作路径的分层节点图排布。
///
/// 按 <see cref="ProductionNode.Row"/> 分层（从 1 开始，不一定连续），每层内节点按 Core 给出的
/// 顺序从左到右紧排，层与层之间固定留 80；行数由 Core 决定（顶针 / 堆叠 / 块会追加新层），
/// 整张图按最宽的一层确定宽度，较窄的层水平居中。
/// 含目标图形的层把目标单独居中，废料排在它右侧。
///
/// 这里只算几何，不建控件：整张图由一个 <see cref="ProductionPathGraphDrawable"/>
/// 一次画完，节点再多也不会创建大量 GraphicsView。
/// </summary>
public sealed class ProductionPathGraphLayout
{
    /// <summary>与工作区节点一致的单元格边长。</summary>
    public const double CellSize = 30;

    /// <summary>节点标题区高度。</summary>
    public const double HeaderHeight = 50;

    /// <summary>节点左右内边距合计：宽度 = 象限数 × 30 + 16。</summary>
    public const double HorizontalPadding = 16;

    /// <summary>行内节点之间的水平间距。</summary>
    public const double ColumnGap = 40;

    /// <summary>相邻两层之间的固定垂直间距，箭头就画在这段空白里。</summary>
    public const double RowGap = 80;

    /// <summary>整张图四周的留白。</summary>
    public const double GraphMargin = 24;

    public IReadOnlyList<ProductionPathNodeBox> Nodes { get; private init; } =
        Array.Empty<ProductionPathNodeBox>();

    public IReadOnlyList<ProductionPathEdge> Edges { get; private init; } =
        Array.Empty<ProductionPathEdge>();

    public double Width { get; private init; }

    public double Height { get; private init; }

    public static ProductionPathGraphLayout? Create(
        ProductionPath path)
    {
        if (path.Nodes.Count == 0)
        {
            return null;
        }

        var nodes = new List<ProductionPathNodeBox>(path.Nodes.Count);

        foreach (ProductionNode node in path.Nodes)
        {
            int parts = node.Shape?.PartCount ?? 4;

            int layers = Math.Max(1, node.Shape?.LayerCount ?? 1);

            nodes.Add(new ProductionPathNodeBox(
                node,
                parts * CellSize + HorizontalPadding,
                HeaderHeight + layers * CellSize));
        }

        List<List<ProductionPathNodeBox>> rows = nodes
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
         * 展开后的实际宽度可能比紧排更宽，所以这里再按目标层补一次宽度。
         * 行数由 Core 决定，这里不假设固定 5 层。
         */
        double contentWidth = Math.Max(
            rows.Max(RowWidth),
            rows.Select(TargetRowWidth).DefaultIfEmpty(0).Max());

        double contentHeight = rows.Sum(RowHeight) + RowGap * (rows.Count - 1);

        double top = GraphMargin;

        foreach (List<ProductionPathNodeBox> row in rows)
        {
            PlaceRow(row, contentWidth, top);

            top += RowHeight(row) + RowGap;
        }

        Dictionary<string, ProductionPathNodeBox> nodesById =
            nodes.ToDictionary(node => node.Node.Id);

        var edges = new List<ProductionPathEdge>(path.Edges.Count);

        foreach (ProductionEdge edge in path.Edges)
        {
            if (!nodesById.TryGetValue(edge.FromId, out ProductionPathNodeBox? from) ||
                !nodesById.TryGetValue(edge.ToId, out ProductionPathNodeBox? to))
            {
                continue;
            }

            edges.Add(new ProductionPathEdge(from, to));
        }

        return new ProductionPathGraphLayout
        {
            Nodes = nodes,
            Edges = edges,
            Width = contentWidth + GraphMargin * 2,
            Height = contentHeight + GraphMargin * 2
        };
    }

    private static double RowWidth(
        IReadOnlyList<ProductionPathNodeBox> row)
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
    /// 含目标图形的层的实际宽度：目标居中后左右两翼取较长的一翼再乘 2。
    /// 没有目标时退回紧排宽度。
    /// </summary>
    private static double TargetRowWidth(
        IReadOnlyList<ProductionPathNodeBox> row)
    {
        int targetIndex = -1;

        for (int index = 0; index < row.Count; index++)
        {
            if (row[index].IsTarget)
            {
                targetIndex = index;

                break;
            }
        }

        if (targetIndex < 0)
        {
            return RowWidth(row);
        }

        ProductionPathNodeBox target = row[targetIndex];

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
        IReadOnlyList<ProductionPathNodeBox> row)
    {
        double height = 0;

        foreach (ProductionPathNodeBox node in row)
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
        List<ProductionPathNodeBox> row,
        double contentWidth,
        double top)
    {
        int targetIndex = row.FindIndex(node => node.IsTarget);

        if (targetIndex >= 0)
        {
            ProductionPathNodeBox target = row[targetIndex];

            target.X = GraphMargin + contentWidth / 2 - target.Width / 2;

            target.Y = top;

            // 目标左侧的节点从目标左边向左依次排开。
            double left = target.X - ColumnGap;

            for (int index = targetIndex - 1; index >= 0; index--)
            {
                ProductionPathNodeBox node = row[index];

                left -= node.Width;

                node.X = left;
                node.Y = top;

                left -= ColumnGap;
            }

            // 目标右侧的节点（废料）从目标右边向右依次排开。
            double right = target.X + target.Width + ColumnGap;

            for (int index = targetIndex + 1; index < row.Count; index++)
            {
                ProductionPathNodeBox node = row[index];

                node.X = right;
                node.Y = top;

                right += node.Width + ColumnGap;
            }

            return;
        }

        double rowLeft = GraphMargin + (contentWidth - RowWidth(row)) / 2;

        foreach (ProductionPathNodeBox node in row)
        {
            node.X = rowLeft;
            node.Y = top;

            rowLeft += node.Width + ColumnGap;
        }
    }
}

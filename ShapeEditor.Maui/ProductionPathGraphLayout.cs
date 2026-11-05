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

    /// <summary>层号 1–5，与 <see cref="ProductionNode.Row"/> 一致。</summary>
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

    /// <summary>顶针与堆叠（尚未接入）。</summary>
    public bool IsPending => Node.Kind == ProductionNodeKind.Pending;
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
/// 按 <see cref="ProductionNode.Row"/> 分层（1–5），每层内节点按 Core 给出的
/// 顺序从左到右紧排，层与层之间固定留 80；整张图按最宽的一层确定宽度，
/// 较窄的层水平居中。第 5 行的目标图形单独居中，废料排在它右侧。
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

        // 每层先按实际宽度紧排，最宽的一层决定整张图的宽度。
        double contentWidth = rows.Max(RowWidth);

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
    /// 普通层水平居中；第 5 行若含目标图形，则把目标图形放在整张图的中线上，
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

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
///
/// <para>
/// <b>目标层正上方那一层是唯一的例外</b>：如果这层里有「制作半边X」
/// （没有上游、直接汇入目标的半边节点），为了让这个节点落在交换卡片正上方
/// （箭头是短竖线，而不是从第一行斜着横跨整张图的长箭头），
/// 该层改为「半边节点并排居中到目标列，其余节点按上游节点所在列摆放后向外推开」。
/// 两侧都需要半边时两个半边节点因此左右对称地并排在同一层，并按标题里的编号
/// 从左到右排列（半边1 在左、半边2 在右）；只有一侧需要时它正好压在目标列上，
/// 另一侧的半边节点按编号站到它左边或右边。
/// </para>
/// </summary>
public sealed class ProductionPathGraph
{
    /// <summary>与卡片节点一致的单元格边长。</summary>
    public const int CellSize = CardMetrics.CellSize;

    /// <summary>节点标题区高度，与卡片节点一致。</summary>
    public const int HeaderHeight = CardMetrics.NodeHeaderHeight;

    /// <summary>节点左右内边距合计：宽度 = 象限数 × 30 + 16。</summary>
    public const int HorizontalPadding = CardMetrics.NodeHorizontalPadding;

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
         * 入边来源：用来识别「没有上游」的起始节点（半边工序的「制作半边X」就是这种）。
         * 同一条边只记第一个上游，节点列的参考点用它就够了。
         */
        var incomingFrom = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ProductionEdge edge in path.Edges)
        {
            incomingFrom.TryAdd(edge.ToId, edge.FromId);
        }

        int targetRowIndex = rows.FindIndex(
            row => row.Exists(node => node.Kind == ProductionNodeKind.Target));

        /*
         * 先按「相对画布中线」算每层节点的横向位置，再由最外的一侧决定整图宽度。
         * 中线取目标层的中线：目标层把目标放在中线上，其余层也以这条中线居中，
         * 这样跨层箭头在两个节点同列时就是一条短竖线。
         *
         * 目标层正上方那层交给 AlignedFeederRowLefts：有「制作半边X」时把它摆到目标列正上方。
         */
        var rowLefts = new List<double[]>(rows.Count);

        var centers = new Dictionary<string, double>(StringComparer.Ordinal);

        for (int index = 0; index < rows.Count; index++)
        {
            List<ProductionPathGraphNode> row = rows[index];

            double[] lefts = (targetRowIndex > 0 && index == targetRowIndex - 1
                    ? AlignedFeederRowLefts(
                        row,
                        rows[targetRowIndex],
                        path.Edges,
                        incomingFrom,
                        centers)
                    : null)
                ?? RowLefts(row);

            rowLefts.Add(lefts);

            for (int offset = 0; offset < row.Count; offset++)
            {
                centers[row[offset].Id] = lefts[offset] + row[offset].Width / 2;
            }
        }

        double contentWidth = 0;

        for (int index = 0; index < rows.Count; index++)
        {
            double[] lefts = rowLefts[index];

            for (int offset = 0; offset < rows[index].Count; offset++)
            {
                contentWidth = Math.Max(
                    contentWidth,
                    Math.Max(
                        Math.Abs(lefts[offset]),
                        Math.Abs(lefts[offset] + rows[index][offset].Width)) * 2);
            }
        }

        double contentHeight = rows
            .Select(RowHeight)
            .Sum() +
            RowGap * (rows.Count - 1);

        double top = GraphMargin;

        double centerX = GraphMargin + contentWidth / 2;

        for (int index = 0; index < rows.Count; index++)
        {
            List<ProductionPathGraphNode> row = rows[index];

            double[] lefts = rowLefts[index];

            for (int offset = 0; offset < row.Count; offset++)
            {
                row[offset].X = centerX + lefts[offset];
                row[offset].Y = top;
            }

            top += RowHeight(row) + RowGap;
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

    /// <summary>一层节点紧排后需要的总宽度。</summary>
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
    /// 一层节点的横向位置（左边 X，相对整图中线）。
    ///
    /// 含目标图形的层把目标放在中线上，其余节点按原顺序向左右两侧展开（废料在右），
    /// 让两条来自旋转后半边的箭头对称地汇入目标；
    /// 其余层整层紧排后以中线居中。
    /// </summary>
    private static double[] RowLefts(
        IReadOnlyList<ProductionPathGraphNode> row)
    {
        var lefts = new double[row.Count];

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
            double cursor = -RowWidth(row) / 2;

            for (int index = 0; index < row.Count; index++)
            {
                lefts[index] = cursor;
                cursor += row[index].Width + ColumnGap;
            }

            return lefts;
        }

        ProductionPathGraphNode target = row[targetIndex];

        lefts[targetIndex] = -target.Width / 2;

        // 目标左侧的节点从目标左边向左依次排开。
        double left = lefts[targetIndex] - ColumnGap;

        for (int index = targetIndex - 1; index >= 0; index--)
        {
            left -= row[index].Width;

            lefts[index] = left;

            left -= ColumnGap;
        }

        // 目标右侧的节点（废料）从目标右边向右依次排开。
        double right = lefts[targetIndex] + target.Width + ColumnGap;

        for (int index = targetIndex + 1; index < row.Count; index++)
        {
            lefts[index] = right;

            right += row[index].Width + ColumnGap;
        }

        return lefts;
    }

    /// <summary>
    /// 目标层正上方那层的横向位置（左边 X，相对整图中线）。
    ///
    /// <para>
    /// 只有这层里存在「制作半边X」（PillarPiece、没有上游、并且直接汇入目标）时才特殊处理，
    /// 其余情况返回 null，仍按普通层紧排：
    /// </para>
    /// <list type="bullet">
    /// <item>半边节点先并排居中到目标列：一个半边时正好压在中线上（箭头是短竖线），
    /// 两个半边时左右对称地并排在同一层；这一组内部按标题里的「半边N」编号
    /// 从左到右排（半边1 在左、半边2 在右），不依赖 Core 给的 Steps 顺序；</item>
    /// <item>其余节点先按上游节点所在列摆放，再向远离中线的一侧推开，彼此至少留 <see cref="ColumnGap"/>。
    /// 如果其余节点自己也是「半边N」（混合粒度下普通角工序一侧的末步），
    /// 则按编号站队：编号小于半边组的一律在左、大于的一律在右，避免被上游列带到反侧。
    /// 于是普通角工序一侧的展开节点仍挂在原来的列下方，不会被半边节点挤到别处。</item>
    /// </list>
    /// </summary>
    /// <param name="row">目标层正上方的那一层。</param>
    /// <param name="targetRow">含目标图形的层。</param>
    /// <param name="edges">节点图的全部箭头（用来判断哪些节点直接汇入目标）。</param>
    /// <param name="incomingFrom">节点 → 第一个上游节点，用来识别「没有上游」的起始节点。</param>
    /// <param name="centers">已排好的节点 → 相对中线的中心 X（上面的层先算）。</param>
    /// <returns>该层每个节点的左边 X；没有半边节点时返回 null。</returns>
    private static double[]? AlignedFeederRowLefts(
        IReadOnlyList<ProductionPathGraphNode> row,
        IReadOnlyList<ProductionPathGraphNode> targetRow,
        IReadOnlyList<ProductionEdge> edges,
        IReadOnlyDictionary<string, string> incomingFrom,
        IReadOnlyDictionary<string, double> centers)
    {
        ProductionPathGraphNode? target = null;

        foreach (ProductionPathGraphNode node in targetRow)
        {
            if (node.Kind == ProductionNodeKind.Target)
            {
                target = node;

                break;
            }
        }

        if (target is null)
        {
            return null;
        }

        var feeders = new HashSet<string>(StringComparer.Ordinal);

        foreach (ProductionEdge edge in edges)
        {
            if (edge.ToId == target.Id)
            {
                feeders.Add(edge.FromId);
            }
        }

        var starts = new List<int>();

        for (int index = 0; index < row.Count; index++)
        {
            ProductionPathGraphNode node = row[index];

            if (node.Kind == ProductionNodeKind.PillarPiece &&
                !incomingFrom.ContainsKey(node.Id) &&
                feeders.Contains(node.Id))
            {
                starts.Add(index);
            }
        }

        if (starts.Count == 0)
        {
            return null;
        }

        var lefts = new double[row.Count];

        /*
         * 第 0 步：半边节点按标题里的编号从左到右排（半边1 在左、半边2 在右）。
         * Core 给出的 Steps 顺序理论上是「左半边、右半边」，但排版只能认标题里的编号：
         * 一旦 Steps 顺序或编号与几何位置对不上，这里就按编号重排 X 位置。
         */
        starts.Sort((first, second) =>
        {
            int firstNumber = HalfNodeNumber(row[first]) ?? int.MaxValue;
            int secondNumber = HalfNodeNumber(row[second]) ?? int.MaxValue;
            int byNumber = firstNumber.CompareTo(secondNumber);

            return byNumber != 0 ? byNumber : first.CompareTo(second);
        });

        // 第 1 步：半边节点并排居中到目标列。
        double groupWidth = ColumnGap * (starts.Count - 1);

        foreach (int index in starts)
        {
            groupWidth += row[index].Width;
        }

        double groupLeft = -groupWidth / 2;

        double cursor = groupLeft;

        foreach (int index in starts)
        {
            lefts[index] = cursor;

            cursor += row[index].Width + ColumnGap;
        }

        double groupRight = groupLeft + groupWidth;

        // 半边组按编号排序后的首尾编号：其余半边节点据此判断该站左边还是右边。
        int? groupFirstNumber = HalfNodeNumber(row[starts[0]]);
        int? groupLastNumber = HalfNodeNumber(row[starts[^1]]);

        // 第 2 步：其余节点先按上游节点所在列摆，再按「在中线哪一侧」向外推开。
        var leftSide = new List<int>();
        var rightSide = new List<int>();

        for (int index = 0; index < row.Count; index++)
        {
            if (starts.Contains(index))
            {
                continue;
            }

            double desiredCenter;

            if (incomingFrom.TryGetValue(row[index].Id, out string? parentId) &&
                centers.TryGetValue(parentId, out double parentCenter))
            {
                desiredCenter = parentCenter;
            }
            else
            {
                // 找不到上游（正常不会发生）：先当作要排在半边组右侧。
                desiredCenter = groupRight + ColumnGap + row[index].Width / 2;
            }

            lefts[index] = desiredCenter - row[index].Width / 2;

            /*
             * 带「半边N」编号的节点按编号站队：编号比半边组小的一律放中线左侧，
             * 比半边组大的一律放右侧。混合粒度下普通角工序一侧的末步叫「半边1/半边2」，
             * 它的上游交换节点可能因为同层还有废料而整体偏到中线的另一侧；
             * 只按父节点中心决定左右会把这个半边节点排到另一个半边节点前面，
             * 也就是用户截图里的「左边半边2、右边半边1」。
             */
            int? halfNumber = HalfNodeNumber(row[index]);

            if (halfNumber is { } number &&
                groupFirstNumber is { } firstNumber &&
                groupLastNumber is { } lastNumber)
            {
                if (number < firstNumber)
                {
                    leftSide.Add(index);
                }
                else if (number > lastNumber)
                {
                    rightSide.Add(index);
                }
                else if (desiredCenter < 0)
                {
                    leftSide.Add(index);
                }
                else
                {
                    rightSide.Add(index);
                }
            }
            else if (desiredCenter < 0)
            {
                leftSide.Add(index);
            }
            else
            {
                rightSide.Add(index);
            }
        }

        // 左侧：从离中线最近的开始向左依次排开（同一列时按节点原顺序，保证排布稳定）。
        leftSide.Sort((first, second) =>
        {
            int byCenter = lefts[second].CompareTo(lefts[first]);

            return byCenter != 0 ? byCenter : first.CompareTo(second);
        });

        double boundary = groupLeft - ColumnGap;

        foreach (int index in leftSide)
        {
            double rightEdge = Math.Min(
                lefts[index] + row[index].Width,
                boundary);

            lefts[index] = rightEdge - row[index].Width;

            boundary = lefts[index] - ColumnGap;
        }

        // 右侧：从离中线最近的开始向右依次排开（同一列时按节点原顺序，保证排布稳定）。
        rightSide.Sort((first, second) =>
        {
            int byCenter = lefts[first].CompareTo(lefts[second]);

            return byCenter != 0 ? byCenter : first.CompareTo(second);
        });

        boundary = groupRight + ColumnGap;

        foreach (int index in rightSide)
        {
            double leftEdge = Math.Max(lefts[index], boundary);

            lefts[index] = leftEdge;

            boundary = leftEdge + row[index].Width + ColumnGap;
        }

        return lefts;
    }

    /// <summary>
    /// 节点标题里的「半边N」编号（例如「制作半边1」「制作半边2」「半边1」「半边2」）；
    /// 标题没有半边编号时返回 null，排布仍然按上游节点所在列决定左右。
    /// </summary>
    private static int? HalfNodeNumber(
        ProductionPathGraphNode node)
    {
        const string Marker = "半边";

        int index = node.Title.IndexOf(Marker, StringComparison.Ordinal);

        if (index < 0)
        {
            return null;
        }

        index += Marker.Length;

        int start = index;

        while (index < node.Title.Length &&
            char.IsAsciiDigit(node.Title[index]))
        {
            index++;
        }

        return index > start &&
            int.TryParse(node.Title[start..index], out int number)
            ? number
            : null;
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

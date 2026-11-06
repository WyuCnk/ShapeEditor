using System.Globalization;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Web.Services;

/// <summary>
/// 「角的工序」里的一个步骤节点。
///
/// 有图形的步骤节点与制作路径节点尺寸一致：宽 = 象限数 × 30 + 16，
/// 高 = 50 + 层数 × 30；纯说明步骤（Shape 为 null）用固定大小的小卡片。
/// 预览始终按 30px 单元格 1:1 绘制，不做缩放。
/// </summary>
public sealed class AngleWorkNode
{
    /// <summary>竖式图单元格边长，与制作路径节点保持一致。</summary>
    public const int CellSize = 30;

    /// <summary>节点标题区高度。</summary>
    public const int HeaderHeight = 50;

    /// <summary>节点左右内边距合计。</summary>
    public const int HorizontalPadding = 16;

    /// <summary>纯说明节点的宽度。</summary>
    public const int NoteWidth = 260;

    /// <summary>纯说明节点的高度：足够放下常见的多行说明文字。</summary>
    public const int NoteHeight = 122;

    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public AngleWorkStepKind Kind { get; init; }

    /// <summary>该步骤执行完后的图形；纯说明步骤为 null。</summary>
    public ShapeDocument? Shape { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    /// <summary>纯说明节点：没有竖式图，正文直接画说明文字。</summary>
    public bool IsNote => Kind == AngleWorkStepKind.Note;

    /// <summary>页面与导出共用的节点样式类。</summary>
    public string KindClass => Kind switch
    {
        AngleWorkStepKind.PreState => "angle-kind-prestate",
        AngleWorkStepKind.Cut => "angle-kind-cut",
        AngleWorkStepKind.Collapse => "angle-kind-collapse",
        AngleWorkStepKind.Pin => "angle-kind-pin",
        AngleWorkStepKind.Crystallize => "angle-kind-crystallize",
        AngleWorkStepKind.Target => "angle-kind-target",
        _ => "angle-kind-note"
    };

    /// <summary>右上角步骤类型小标签；说明节点不显示标签。</summary>
    public string KindTag => Kind switch
    {
        AngleWorkStepKind.PreState => "前态",
        AngleWorkStepKind.Cut => "切",
        AngleWorkStepKind.Collapse => "落",
        AngleWorkStepKind.Pin => "顶",
        AngleWorkStepKind.Crystallize => "晶",
        AngleWorkStepKind.Target => "目标",
        _ => ""
    };

    /// <summary>导出图选择的步骤类型标识，与 shape-grid.js 的配色表对应。</summary>
    public string KindName => Kind switch
    {
        AngleWorkStepKind.PreState => "angle-pre",
        AngleWorkStepKind.Cut => "angle-cut",
        AngleWorkStepKind.Collapse => "angle-collapse",
        AngleWorkStepKind.Pin => "angle-pin",
        AngleWorkStepKind.Crystallize => "angle-crystallize",
        AngleWorkStepKind.Target => "angle-target",
        _ => "angle-note"
    };
}

/// <summary>角工序节点之间的箭头（首帧几何，页面渲染后由 JS 按真实矩形校正）。</summary>
public sealed record AngleWorkEdge(
    string FromId,
    string ToId,
    string Path,
    string Arrow);

/// <summary>
/// 一个角的工序卡片：分类、构造、步骤节点链与验证状态。
/// 页面与导出共用这份数据。
/// </summary>
public sealed class AngleWorkColumn
{
    public int Column { get; init; }

    public AngleBuildKind Kind { get; init; }

    public AngleConstruction Construction { get; init; }

    public bool IsComplete { get; init; }

    /// <summary>未通过验证时的原因；已通过为 null。</summary>
    public string? Note { get; init; }

    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<AngleWorkNode> Nodes { get; init; } = Array.Empty<AngleWorkNode>();

    public IReadOnlyList<AngleWorkEdge> Edges { get; init; } = Array.Empty<AngleWorkEdge>();

    public double GraphWidth { get; init; }

    public double GraphHeight { get; init; }

    public bool IsSimple => Kind == AngleBuildKind.Simple;

    public string KindText => IsSimple ? "简单角" : "碎晶角";

    /// <summary>简单角的一行说明；碎晶角不使用。</summary>
    public string SimpleText => AngleWorkColumnBuilder.SimpleText;

    /// <summary>构造文本；没有可用构造时为 null。</summary>
    public string? ConstructionText => Construction switch
    {
        AngleConstruction.G => "构造 G",
        AngleConstruction.F => "构造 F",
        AngleConstruction.H => "构造 H",
        AngleConstruction.FillBreak => "填充 + 暴破",
        _ => null
    };

    /// <summary>未完全验证时的小字标题。</summary>
    public string? StatusText => IsComplete ? null : "该列工序尚未完全验证";
}

/// <summary>
/// 把 Core 的 <see cref="AngleWorkOrder"/> 转成页面/导出用的列卡片。
///
/// 只在「角的工序」区域显示时调用：每列一次 <see cref="AngleWorkOrderBuilder.Build"/>，
/// 列数多时（例如 20 列）也不会在隐藏状态下白算。
/// </summary>
public static class AngleWorkColumnBuilder
{
    /// <summary>简单角的一行说明。</summary>
    public const string SimpleText = "最高晶体连续触地，无需工序";

    /// <summary>节点链内边距。</summary>
    private const double GraphMargin = 6;

    /// <summary>节点之间的水平间距，留给箭头。</summary>
    private const double ColumnGap = 40;

    /// <summary>
    /// 生成目标图形每一列的工序卡片。
    ///
    /// cap 取目标图形自身的层数：工序前态按图形高度绘制，
    /// 需要更高脚手架才能成立的构造会返回未完成单，由 UI 明确标出。
    /// </summary>
    public static IReadOnlyList<AngleWorkColumn> Build(
        ShapeDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        int cap = document.LayerCount;

        var columns = new List<AngleWorkColumn>(document.PartCount);

        for (int column = 0;
             column < document.PartCount;
             column++)
        {
            columns.Add(BuildColumn(document, column, cap));
        }

        return columns;
    }

    /// <summary>
    /// 只构建一列的工序卡片。
    ///
    /// 分析页需要按列推进进度条，逐列调用本方法即可避免一次阻塞整页；
    /// 隐藏「角的工序」区域时不会调用。
    /// </summary>
    public static AngleWorkColumn BuildColumn(
        ShapeDocument document,
        int column,
        int cap)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (column < 0 || column >= document.PartCount)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        AngleWorkOrder order = AngleWorkOrderBuilder.Build(
            document,
            column,
            cap);

        return Create(order);
    }

    private static AngleWorkColumn Create(
        AngleWorkOrder order)
    {
        // 简单角只显示一行说明，不生成节点预览。
        if (order.Kind == AngleBuildKind.Simple)
        {
            return new AngleWorkColumn
            {
                Column = order.Column,
                Kind = order.Kind,
                Construction = order.Construction,
                IsComplete = order.IsComplete,
                Note = order.Note,
                Limitations = order.Limitations
            };
        }

        var nodes = new List<AngleWorkNode>(order.Steps.Count);
        double x = GraphMargin;
        double graphHeight = 0;

        foreach (AngleWorkStep step in order.Steps)
        {
            bool hasShape = step.Shape is not null;

            double width = hasShape
                ? step.Shape!.PartCount * AngleWorkNode.CellSize +
                  AngleWorkNode.HorizontalPadding
                : AngleWorkNode.NoteWidth;

            double height = hasShape
                ? AngleWorkNode.HeaderHeight +
                  step.Shape!.LayerCount * AngleWorkNode.CellSize
                : AngleWorkNode.NoteHeight;

            nodes.Add(new AngleWorkNode
            {
                Id = $"angle-{order.Column}-{nodes.Count}",
                Title = step.Title,
                Kind = step.Kind,
                Shape = step.Shape,
                X = x,
                Y = GraphMargin,
                Width = width,
                Height = height
            });

            x += width + ColumnGap;
            graphHeight = Math.Max(graphHeight, height);
        }

        var edges = new List<AngleWorkEdge>(Math.Max(0, nodes.Count - 1));

        for (int index = 0;
             index + 1 < nodes.Count;
             index++)
        {
            edges.Add(BuildEdge(nodes[index], nodes[index + 1]));
        }

        double graphWidth = nodes.Count == 0
            ? 0
            : x - ColumnGap + GraphMargin;

        return new AngleWorkColumn
        {
            Column = order.Column,
            Kind = order.Kind,
            Construction = order.Construction,
            IsComplete = order.IsComplete,
            Note = order.Note,
            Limitations = order.Limitations,
            Nodes = nodes,
            Edges = edges,
            GraphWidth = graphWidth,
            GraphHeight = nodes.Count == 0
                ? 0
                : graphHeight + GraphMargin * 2
        };
    }

    /// <summary>
    /// 节点链首帧箭头：同一行内从左侧节点右边缘中点连到右侧节点左边缘中点。
    /// 页面渲染后由 shapeGridLayoutEdges 用真实矩形重新校正。
    /// </summary>
    private static AngleWorkEdge BuildEdge(
        AngleWorkNode from,
        AngleWorkNode to)
    {
        double x1 = from.X + from.Width;
        double y1 = from.Y + from.Height / 2;

        double x2 = to.X;
        double y2 = to.Y + to.Height / 2;

        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1)
        {
            length = 1;
        }

        double ux = dx / length;
        double uy = dy / length;

        double tipX = x2 + ux;
        double tipY = y2 + uy;

        string path =
            $"M {Number(x1)} {Number(y1)} L {Number(tipX)} {Number(tipY)}";

        const double head = 11;
        const double half = 5;

        double bx = tipX - ux * head;
        double by = tipY - uy * head;

        string arrow =
            $"{Number(tipX)},{Number(tipY)} " +
            $"{Number(bx - uy * half)},{Number(by + ux * half)} " +
            $"{Number(bx + uy * half)},{Number(by - ux * half)}";

        return new AngleWorkEdge(from.Id, to.Id, path, arrow);
    }

    private static string Number(
        double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);
}

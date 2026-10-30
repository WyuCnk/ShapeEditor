using ShapeEditor.Core.Models;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区画布的几何规则：节点尺寸只由「象限数 × 层数」决定，
/// 画布与导出图片共用同一套计算，避免两边尺寸对不上。
/// </summary>
public static class WorkspaceLayout
{
    /// <summary>预览格子的固定边长，画布与导出图片都是 30。</summary>
    public const double CellSize = 30;

    /// <summary>标题行高度。</summary>
    public const double HeaderHeight = 26;

    /// <summary>摘要行高度。</summary>
    public const double SummaryHeight = 24;

    /// <summary>节点装饰高度 = 标题行 + 摘要行。</summary>
    public const double ChromeHeight = HeaderHeight + SummaryHeight;

    /// <summary>节点左右留白之和，宽度 = 象限数 × 30 + 16。</summary>
    public const double HorizontalPadding = 8;

    /// <summary>没有图形时给一个 4 象限的占位宽度，保证还能点到卡片。</summary>
    public const int DefaultParts = 4;

    /// <summary>画布与导出图片的外边距。</summary>
    public const double Margin = 24;

    /// <summary>自动排布时列与列之间的空隙。</summary>
    public const double ColumnGap = 40;

    /// <summary>自动排布时行与行之间的空隙，同时也是结果节点与源节点的间距。</summary>
    public const double RowGap = 56;

    public static int LayersOf(
        WorkspaceNode node,
        ShapeDocument? document)
    {
        if (document is not null)
        {
            return document.LayerCount;
        }

        return Math.Max(0, node.Layers);
    }

    public static int PartsOf(
        WorkspaceNode node,
        ShapeDocument? document)
    {
        if (document is not null)
        {
            return document.PartCount;
        }

        return node.Parts > 0 ? node.Parts : DefaultParts;
    }

    public static double WidthOf(
        WorkspaceNode node,
        ShapeDocument? document)
    {
        return PartsOf(node, document) * CellSize + HorizontalPadding * 2;
    }

    public static double HeightOf(
        WorkspaceNode node,
        ShapeDocument? document)
    {
        return ChromeHeight + LayersOf(node, document) * CellSize;
    }

    /// <summary>
    /// 节点标题：素材节点 / 运算结果 / 堆叠输入。
    /// </summary>
    public static string TitleOf(
        WorkspaceNode node)
    {
        if (node.Kind == "ref")
        {
            return "堆叠输入";
        }

        if (!node.IsResult)
        {
            return "素材节点";
        }

        return string.IsNullOrWhiteSpace(node.Label)
            ? "运算结果"
            : node.Label;
    }

    /// <summary>
    /// 计算所有节点的矩形。
    ///
    /// 运算生成的节点带有自由坐标 X/Y，优先使用；
    /// 其余节点按 Row/Col 自动排布，列宽和行高取该列/该行的最大值。
    /// 坐标允许为负，页面渲染时再整体平移，导出时用包围盒裁切。
    /// </summary>
    public static List<WorkspaceNodeBox> Compute(
        IReadOnlyList<WorkspaceNode> nodes,
        Func<WorkspaceNode, ShapeDocument?> documentOf)
    {
        var sizes = new Dictionary<string, Size>(nodes.Count);

        foreach (WorkspaceNode node in nodes)
        {
            sizes[node.Id] = new Size(
                WidthOf(node, documentOf(node)),
                HeightOf(node, documentOf(node)));
        }

        var columnWidths = new Dictionary<int, double>();

        var rowHeights = new Dictionary<int, double>();

        foreach (WorkspaceNode node in nodes)
        {
            Size size = sizes[node.Id];

            if (node.X is null)
            {
                columnWidths[node.Col] = columnWidths.TryGetValue(node.Col, out double width)
                    ? Math.Max(width, size.Width)
                    : size.Width;
            }

            if (node.Y is null)
            {
                rowHeights[node.Row] = rowHeights.TryGetValue(node.Row, out double height)
                    ? Math.Max(height, size.Height)
                    : size.Height;
            }
        }

        var columnLefts = new Dictionary<int, double>();

        double left = Margin;

        foreach (int column in columnWidths.Keys.OrderBy(value => value))
        {
            columnLefts[column] = left;

            left += columnWidths[column] + ColumnGap;
        }

        var rowTops = new Dictionary<int, double>();

        double top = Margin;

        foreach (int row in rowHeights.Keys.OrderBy(value => value))
        {
            rowTops[row] = top;

            top += rowHeights[row] + RowGap;
        }

        var boxes = new List<WorkspaceNodeBox>(nodes.Count);

        foreach (WorkspaceNode node in nodes)
        {
            Size size = sizes[node.Id];

            double x = node.X ?? (columnLefts.TryGetValue(node.Col, out double columnLeft)
                ? columnLeft
                : Margin);

            double y = node.Y ?? (rowTops.TryGetValue(node.Row, out double rowTop)
                ? rowTop
                : Margin);

            boxes.Add(new WorkspaceNodeBox(
                node,
                new Rect(x, y, size.Width, size.Height)));
        }

        return boxes;
    }
}

/// <summary>
/// 一个节点在画布坐标系里的矩形。
/// </summary>
public sealed record WorkspaceNodeBox(
    WorkspaceNode Node,
    Rect Bounds);

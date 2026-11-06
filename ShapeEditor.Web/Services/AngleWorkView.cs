using System.Globalization;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
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
        AngleWorkStepKind.Support => "angle-kind-support",
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
        AngleWorkStepKind.Support => "撑",
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
        AngleWorkStepKind.Support => "angle-support",
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
/// 一个角的工序卡片：分类、算法、构造、顶针次数、造塔子配方、
/// 步骤节点链与验证状态。页面与导出共用这份数据。
/// </summary>
public sealed class AngleWorkColumn
{
    public int Column { get; init; }

    public AngleBuildKind Kind { get; init; }

    public AngleConstruction Construction { get; init; }

    /// <summary>实际采用的论文分档算法。</summary>
    public AngleAlgorithm Algorithm { get; init; }

    /// <summary>角工序自己的顶针次数（子配方 rank 不计入）。</summary>
    public int Rank { get; init; }

    /// <summary>造塔子配方（交换链）；只有算法 6 等带子配方的列才有。</summary>
    public AngleSubRecipe? SubRecipe { get; init; }

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

    /// <summary>
    /// 分类芯片是否显示：无晶体 / Simple 的算法文案已经带分类，
    /// 再显示「简单角」会重复，所以这两种算法隐藏分类芯片。
    /// </summary>
    public bool ShowKindChip =>
        Algorithm is not (AngleAlgorithm.NoCrystal or AngleAlgorithm.SimpleTail);

    /// <summary>
    /// 简单角的一行说明；碎晶角不使用。
    /// 无晶体 / Simple 档按论文分档写明真实做法，避免和标题里的
    /// 「构造 C」「构造 A + C」自相矛盾（这里的「无需工序」指无需碎晶）。
    /// </summary>
    public string SimpleText => Algorithm switch
    {
        AngleAlgorithm.NoCrystal =>
            "无晶体：按构造 C 从地面往上逐层追加 T/P，没有碎晶工序。",
        AngleAlgorithm.SimpleTail =>
            "最高晶体连续接地：先用构造 A 做紧列，再用构造 C 追加尾部，没有碎晶工序。",
        _ => AngleWorkColumnBuilder.SimpleText
    };

    /// <summary>
    /// 算法文案。未知枚举值回退成原始枚举名，绝不返回空字符串；
    /// <see cref="AngleAlgorithm.None"/> 表示没有分档，不显示芯片。
    /// </summary>
    public string? AlgorithmText => Algorithm switch
    {
        AngleAlgorithm.NoCrystal => "无晶体",
        AngleAlgorithm.SimpleTail => "简单角",
        AngleAlgorithm.Algorithm1 => "算法 1",
        AngleAlgorithm.Algorithm2 => "算法 2",
        AngleAlgorithm.Algorithm3 => "算法 3",
        AngleAlgorithm.Algorithm4 => "算法 4",
        AngleAlgorithm.Algorithm5 => "算法 5",
        AngleAlgorithm.Algorithm6 => "算法 6",
        AngleAlgorithm.Fallback => "兜底构造",
        AngleAlgorithm.None => null,
        _ => Algorithm.ToString()
    };

    /// <summary>
    /// 构造文案。未知枚举值回退成原始枚举名，绝不返回空字符串；
    /// Simple 档的枚举只记 A，但实际是「紧列 A + 尾部 C」，这里如实写全。
    /// </summary>
    public string? ConstructionText =>
        Algorithm == AngleAlgorithm.SimpleTail
            ? "构造 A + C"
            : Construction switch
            {
                AngleConstruction.A => "构造 A",
                AngleConstruction.C => "构造 C",
                AngleConstruction.G => "构造 G",
                AngleConstruction.F => "构造 F",
                AngleConstruction.H => "构造 H",
                AngleConstruction.FillBreak => "填充 + 暴破",
                AngleConstruction.None => null,
                _ => Construction.ToString()
            };

    /// <summary>
    /// 顶针次数文案。简单角没有工序；没有分档的失败单（Algorithm 为 None）
    /// 也谈不上顶针次数，这两种情况都不显示芯片。
    /// </summary>
    public string? RankText =>
        !IsSimple && (Algorithm != AngleAlgorithm.None || Rank > 0)
            ? $"顶针次数 {Rank}"
            : null;

    /// <summary>未完全验证时的小字标题。</summary>
    public string? StatusText => IsComplete ? null : "该列工序尚未完全验证";
}

/// <summary>
/// 造塔子配方（角工序里的 rank 0 交换链）的展开视图。
///
/// <para>页面在「展开全部子配方」打开时按 <see cref="Items"/> 逐个画图形预览；
/// 导出图不画预览（四片并排太宽），只在卡片里用小字列出每个步骤名。</para>
/// </summary>
public sealed class AngleSubRecipe
{
    /// <summary>折叠区区标题，例如「造塔子配方（交换链，rank 0）」。</summary>
    public string Title { get; init; } = "";

    /// <summary>一行流程摘要，例如「制作零件 1/2/3/4 → 两次交换 → 目标」。</summary>
    public string Summary { get; init; } = "";

    /// <summary>展开后的步骤，每项带图形预览；废料项标记为 waste。</summary>
    public IReadOnlyList<AngleSubRecipeItem> Items { get; init; } =
        Array.Empty<AngleSubRecipeItem>();
}

/// <summary>造塔子配方展开后的一个步骤（零件 / 交换输出 / 废料 / 目标）。</summary>
public sealed class AngleSubRecipeItem
{
    /// <summary>步骤名，例如「制作零件 1」「左交换」「目标：造塔 X」。</summary>
    public string Title { get; init; } = "";

    /// <summary>该步骤的图形快照；仅用于页面预览。</summary>
    public ShapeDocument? Shape { get; init; }

    /// <summary>废料：页面用淡显虚线样式，与制作路径的废料节点一致。</summary>
    public bool IsWaste { get; init; }
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
        // 子配方与节点链互不影响：简单角也一并带上（当前只有造塔档才有）。
        AngleSubRecipe? subRecipe = CreateSubRecipe(order);

        // 简单角只显示一行说明，不生成节点预览。
        if (order.Kind == AngleBuildKind.Simple)
        {
            return new AngleWorkColumn
            {
                Column = order.Column,
                Kind = order.Kind,
                Construction = order.Construction,
                Algorithm = order.Algorithm,
                Rank = order.Rank,
                SubRecipe = subRecipe,
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
            Algorithm = order.Algorithm,
            Rank = order.Rank,
            SubRecipe = subRecipe,
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
    /// 把 Core 的造塔子配方（交换链，rank 0）转成可展开的视图。
    ///
    /// <para>
    /// 优先使用 <see cref="Recipe.Exchange"/> 的 ExchangeDetails：
    /// 四片（送入交换时的旋转版）→ 左 / 右交换输出 → 废料 → 目标。
    /// 没有 ExchangeDetails 时退回 <see cref="Recipe.Steps"/> 的快照；
    /// 连步骤都没有时只保留「目标」，页面仍会显示流程摘要，不会出现空区块。
    /// </para>
    /// </summary>
    private static AngleSubRecipe? CreateSubRecipe(
        AngleWorkOrder order)
    {
        Recipe? recipe = order.SubRecipe;

        if (recipe is null)
        {
            return null;
        }

        var items = new List<AngleSubRecipeItem>();
        bool hasExchange = recipe.Exchange is not null;

        if (recipe.Exchange is { } exchange)
        {
            /*
             * 四片：画送入交换机时的旋转版（Rotated），这才是实际参与交换的形态；
             * Raw 是刚构造出来的样子，页面不重复画，避免一行八张图过宽。
             */
            items.Add(Piece("制作零件 1", exchange.P1.Rotated));
            items.Add(Piece("制作零件 2", exchange.P2.Rotated));
            items.Add(Piece("制作零件 3", exchange.P3.Rotated));
            items.Add(Piece("制作零件 4", exchange.P4.Rotated));

            items.Add(new AngleSubRecipeItem
            {
                Title = "左交换",
                Shape = exchange.Left.Result
            });

            items.Add(new AngleSubRecipeItem
            {
                Title = "右交换",
                Shape = exchange.Right.Result
            });

            ShapeDocument? waste =
                exchange.Waste ??
                exchange.Left.Waste ??
                exchange.Right.Waste;

            if (waste is not null)
            {
                items.Add(new AngleSubRecipeItem
                {
                    Title = "废料",
                    Shape = waste,
                    IsWaste = true
                });
            }
        }
        else
        {
            // 保守回退：按 Recipe 自带的过程快照逐个列出。
            foreach (RecipeStep step in recipe.Steps)
            {
                items.Add(new AngleSubRecipeItem
                {
                    Title = step.Title,
                    Shape = step.Shape,
                    IsWaste = step.IsWaste
                });
            }
        }

        items.Add(new AngleSubRecipeItem
        {
            Title = "目标：造塔 X",
            Shape = recipe.Shape
        });

        return new AngleSubRecipe
        {
            Title = $"造塔子配方（交换链，rank {recipe.Rank}）",
            Summary = hasExchange
                ? "制作零件 1/2/3/4 → 两次交换 → 目标"
                : "按配方步骤装配 → 目标",
            Items = items
        };
    }

    private static AngleSubRecipeItem Piece(
        string title,
        ShapeDocument shape) =>
        new()
        {
            Title = title,
            Shape = shape
        };

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

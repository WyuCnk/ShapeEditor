using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 把分析窗口的内容导出成一张单栏 PNG。
///
/// 版式与 Web 的「导出分析图」对齐，自上而下依次是：
/// 标题与短代码、竖式图、图形性质、角的合法性、稳定性、
/// 半边稳定性（一轴一块）、制作路径（分层节点图），最后是尺寸与生成时间。
///
/// 全部内容由 DrawingVisual 一次性离屏绘制：
/// 竖式图与节点预览复用 <see cref="ShapeGridDrawing"/>，与编辑器保持同一套配色；
/// 画布宽度会随制作路径节点图自动加宽，节点图不缩放、不分片。
/// </summary>
public static class AnalysisImageExporter
{
    /// <summary>单栏版式的最小画布宽度。</summary>
    private const double MinCanvasWidth = 820;

    /// <summary>四周留白。</summary>
    private const double PagePadding = 36;

    /// <summary>单栏内容的最小宽度。</summary>
    private const double MinContentWidth = MinCanvasWidth - PagePadding * 2;

    /// <summary>顶部主色装饰条高度。</summary>
    private const double TopBarHeight = 6;

    /// <summary>板块标题占用的高度。</summary>
    private const double SectionTitleHeight = 30;

    /// <summary>板块之间的间隔。</summary>
    private const double SectionGap = 22;

    /// <summary>竖式图允许的最大宽度。</summary>
    private const double MaxShapeWidth = 600;

    /// <summary>半边方向在轴块内的最小列宽，与 Web 的 minmax(168px, 1fr) 一致。</summary>
    private const double HalfDirectionColumnWidth = 168;

    /// <summary>半边方向列之间的水平间距。</summary>
    private const double HalfDirectionColumnGap = 16;

    /// <summary>半边方向行高。</summary>
    private const double HalfDirectionRowHeight = 20;

    /// <summary>半边方向行间距。</summary>
    private const double HalfDirectionRowGap = 4;

    /// <summary>节点图四周的圆角半径。</summary>
    private const double GraphCornerRadius = 12;

    private const string UiFontFamily =
        "Microsoft YaHei UI, Microsoft YaHei, Segoe UI";

    private const string MonoFontFamily =
        "Consolas, Microsoft YaHei UI";

    private static readonly Typeface UiTypeface = new(
        new FontFamily(UiFontFamily),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private static readonly Typeface UiBoldTypeface = new(
        new FontFamily(UiFontFamily),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    private static readonly Typeface MonoTypeface = new(
        new FontFamily(MonoFontFamily),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private static readonly Brush TextBrush = CreateBrush(0x21, 0x21, 0x21);

    private static readonly Brush BodyBrush = CreateBrush(0x3A, 0x3A, 0x44);

    private static readonly Brush MutedBrush = CreateBrush(0x6B, 0x6B, 0x76);

    private static readonly Brush PrimaryBrush = CreateBrush(0x51, 0x2B, 0xD4);

    private static readonly Brush PrimaryDarkBrush = CreateBrush(0x45, 0x27, 0xA0);

    private static readonly Brush PrimarySoftBrush = CreateBrush(0xEF, 0xEA, 0xFB);

    private static readonly Brush TableHeaderBrush = CreateBrush(0xF3, 0xF0, 0xFC);

    private static readonly Brush TableHeaderTextBrush = CreateBrush(0x4A, 0x3F, 0x72);

    private static readonly Brush BorderBrush = CreateBrush(0xE6, 0xE2, 0xF2);

    private static readonly Brush DividerBrush = CreateBrush(0xEE, 0xEA, 0xF7);

    private static readonly Brush DangerBrush = CreateBrush(0xA0, 0x00, 0x00);

    private static readonly Brush DangerSoftBrush = CreateBrush(0xFF, 0xF5, 0xF5);

    private static readonly Brush SuccessBrush = CreateBrush(0x2E, 0x7D, 0x32);

    /// <summary>合法的交换方向：与 Web 一致的绿色。</summary>
    private static readonly Brush LegalDirectionBrush =
        CreateBrush(0x2E, 0x9E, 0x5B);

    /// <summary>不合法的交换方向：与 Web 一致的红色。</summary>
    private static readonly Brush IllegalDirectionBrush =
        CreateBrush(0xD6, 0x45, 0x45);

    private static readonly Brush ZebraBrush = CreateBrush(0xFB, 0xFA, 0xFE);

    private static readonly Brush ShapeCardBrush = CreateBrush(0xFA, 0xF8, 0xFF);

    private static readonly Brush GraphBackgroundBrush =
        CreateBrush(0xFB, 0xFA, 0xFF);

    private static readonly Brush NodeBorderBrush = CreateBrush(0xD0, 0xD0, 0xD0);

    private static readonly Brush NodeTitleBrush = CreateBrush(0x4A, 0x3F, 0x72);

    private static readonly Brush WasteFillBrush = CreateBrush(0xFB, 0xFB, 0xFC);

    private static readonly Brush WasteBorderBrush = CreateBrush(0xC4, 0xC4, 0xCC);

    private static readonly Brush WasteTitleBrush = CreateBrush(0x9A, 0x9A, 0xA5);

    private static readonly Brush WasteEdgeBrush = CreateBrush(0x9A, 0x9A, 0xA5);

    /// <summary>顶针工序：青绿色边框与标签，与窗口 / Web 一致。</summary>
    private static readonly Brush PinAccentBrush = CreateBrush(0x0E, 0x7C, 0x86);

    private static readonly Brush PinFillBrush = CreateBrush(0xF2, 0xFB, 0xFB);

    /// <summary>堆叠工序：橙色边框与标签。</summary>
    private static readonly Brush StackAccentBrush = CreateBrush(0xB2, 0x6A, 0x00);

    private static readonly Brush StackFillBrush = CreateBrush(0xFF, 0xF8, 0xEE);

    /// <summary>hybrid 块：绿色边框与标签。</summary>
    private static readonly Brush BlockAccentBrush = CreateBrush(0x2E, 0x7D, 0x32);

    private static readonly Brush BlockFillBrush = CreateBrush(0xF3, 0xFB, 0xF4);

    private static readonly Pen BorderPen = CreatePen(BorderBrush, 1);

    private static readonly Pen DividerPen = CreatePen(DividerBrush, 1);

    private static readonly Pen NodeBorderPen = CreatePen(NodeBorderBrush, 1.2);

    private static readonly Pen ResultNodePen = CreatePen(PrimaryBrush, 2);

    private static readonly Pen PinNodePen = CreatePen(PinAccentBrush, 2);

    private static readonly Pen StackNodePen = CreatePen(StackAccentBrush, 2);

    private static readonly Pen BlockNodePen = CreatePen(BlockAccentBrush, 2);

    private static readonly Pen WasteNodePen = CreateDashedPen(WasteBorderBrush, 1.2);

    private static readonly Pen EdgePen = CreatePen(PrimaryBrush, 2);

    private static readonly Pen WasteEdgePen = CreateDashedPen(WasteEdgeBrush, 2);

    private static readonly Pen PlaceholderPen = CreateDashedPen(
        CreateBrush(0xB9, 0xA9, 0xEE),
        1);

    /// <summary>
    /// 导出当前分析结果。调用方负责处理保存对话框与异常提示。
    /// </summary>
    public static void Export(
        string filePath,
        AnalysisViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (viewModel.Document is not ShapeDocument document)
        {
            throw new InvalidOperationException("没有可导出的图形。");
        }

        /*
         * 制作路径节点图按原始尺寸整张输出：
         * 图比单栏宽时把画布加宽，绝不缩放预览、也不分片。
         */
        double contentWidth = MinContentWidth;

        if (viewModel.HasProductionPath &&
            viewModel.IsPathExpanded &&
            viewModel.IsPathExchangeExpanded &&
            viewModel.PathGraph is ProductionPathGraph graph)
        {
            contentWidth = Math.Max(contentWidth, graph.Width);
        }

        double canvasWidth = PagePadding * 2 + contentWidth;

        DrawingGroup content = BuildContent(
            viewModel,
            document,
            contentWidth,
            out double height);

        var visual = new DrawingVisual();

        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, canvasWidth, height));

            // 顶部主色装饰条，与站点主色 #512BD4 一致。
            context.DrawRectangle(
                PrimaryBrush,
                null,
                new Rect(0, 0, canvasWidth, TopBarHeight));

            context.DrawDrawing(content);
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(canvasWidth),
            (int)Math.Ceiling(height),
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = File.Create(filePath);

        encoder.Save(stream);
    }

    #region 整体排版

    /// <summary>
    /// 按自上而下的顺序画完所有板块，返回内容分组与总高度。
    /// </summary>
    private static DrawingGroup BuildContent(
        AnalysisViewModel viewModel,
        ShapeDocument document,
        double contentWidth,
        out double height)
    {
        var group = new DrawingGroup();

        // 顶部装饰条之下再留出页边距。
        double y = TopBarHeight + PagePadding;

        using (DrawingContext context = group.Open())
        {
            y = DrawHeader(context, viewModel.Code, contentWidth, y);

            y = DrawShapeBlock(context, document, contentWidth, y);

            /*
             * 与 Web 的导出规则一致：窗口里折叠的板块不进入图片，
             * 默认全部展开时与原来完全一致。
             */
            if (viewModel.IsPropertiesExpanded)
            {
                y = DrawPropertiesSection(context, viewModel, contentWidth, y);
            }

            if (viewModel.IsAnglesExpanded)
            {
                y = DrawAnglesSection(context, viewModel, contentWidth, y);
            }

            if (viewModel.IsStabilityExpanded)
            {
                y = DrawStabilitySection(context, viewModel, contentWidth, y);
            }

            if (viewModel.IsHalfExpanded)
            {
                y = DrawHalfSection(context, viewModel, contentWidth, y);
            }

            /*
             * 制作路径与窗口保持一致：
             * 总开关关闭、两个子区域都关闭、或没有可画的内容时不输出。
             */
            bool hasExchange =
                viewModel.HasProductionPath &&
                viewModel.IsPathExchangeExpanded;

            bool hasAnglesProcess = viewModel.IsPathAnglesExpanded;

            if (viewModel.HasPathSection &&
                viewModel.IsPathExpanded &&
                (hasExchange || hasAnglesProcess))
            {
                y = DrawPathSection(context, viewModel, contentWidth, y);
            }

            y = DrawFooter(context, viewModel, contentWidth, y);
        }

        height = Math.Ceiling(y + PagePadding);

        return group;
    }

    #endregion

    #region 顶部

    /// <summary>
    /// 标题与短代码：都按内容宽度居中，短代码过长时最多显示四行。
    /// </summary>
    private static double DrawHeader(
        DrawingContext context,
        string code,
        double contentWidth,
        double y)
    {
        FormattedText title = CreateText(
            "图形分析",
            28,
            TextBrush,
            bold: true);

        title.MaxTextWidth = contentWidth;

        title.TextAlignment = TextAlignment.Center;

        context.DrawText(title, new Point(PagePadding, y));

        y += title.Height + 4;

        if (!string.IsNullOrWhiteSpace(code))
        {
            FormattedText codeText = CreateText(
                code,
                12.5,
                PrimaryDarkBrush,
                mono: true);

            codeText.MaxTextWidth = contentWidth;

            codeText.TextAlignment = TextAlignment.Center;

            codeText.MaxLineCount = 4;

            codeText.Trimming = TextTrimming.CharacterEllipsis;

            context.DrawText(codeText, new Point(PagePadding, y));

            y += codeText.Height;
        }

        y += 16;

        context.DrawLine(
            DividerPen,
            new Point(PagePadding, y),
            new Point(PagePadding + contentWidth, y));

        return y + 16;
    }

    #endregion

    #region 竖式图

    /// <summary>
    /// 竖式图：先按 30 的基准格构建绘图，再整体缩放到目标格宽，
    /// 这样字号和描边会跟着一起缩放，与界面上的观感一致。
    /// </summary>
    private static double DrawShapeBlock(
        DrawingContext context,
        ShapeDocument document,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "竖式图", y);

        const double baseCell = 30;

        double cell = Math.Min(
            MaxShapeWidth / document.PartCount,
            96);

        cell = Math.Max(6, Math.Floor(cell));

        double gridWidth = document.PartCount * cell;

        double gridHeight = document.LayerCount * cell;

        double cardWidth = Math.Min(
            contentWidth,
            gridWidth + 36);

        double cardHeight = gridHeight + 36;

        double cardLeft = PagePadding + (contentWidth - cardWidth) / 2;

        var card = new Rect(
            cardLeft,
            y,
            cardWidth,
            cardHeight);

        context.DrawRoundedRectangle(
            ShapeCardBrush,
            BorderPen,
            card,
            10,
            10);

        DrawingGroup drawing = ShapeGridDrawing.Build(
            document,
            baseCell,
            baseCell);

        double left = cardLeft + (cardWidth - gridWidth) / 2;

        double top = y + (cardHeight - gridHeight) / 2;

        // 先缩放后平移，让基准格尺寸变成目标格尺寸。
        context.PushTransform(new TranslateTransform(left, top));

        context.PushTransform(new ScaleTransform(cell / baseCell, cell / baseCell));

        context.DrawDrawing(drawing);

        context.Pop();

        context.Pop();

        y += cardHeight + 8;

        FormattedText dimension = CreateText(
            $"{document.LayerCount} 层 × {document.PartCount} 象限",
            12,
            MutedBrush);

        context.DrawText(
            dimension,
            new Point(
                PagePadding + (contentWidth - dimension.Width) / 2,
                y));

        return y + dimension.Height + 14;
    }

    #endregion

    #region 各板块

    /// <summary>图形性质：与界面一致的标签流式排布。</summary>
    private static double DrawPropertiesSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "图形性质", y);

        if (viewModel.PropertyNames.Count == 0)
        {
            y = DrawParagraph(
                context,
                "没有识别到任何性质。",
                y,
                contentWidth,
                13,
                MutedBrush);
        }
        else
        {
            y = DrawChips(context, viewModel.PropertyNames, contentWidth, y);
        }

        return EndSection(context, y, contentWidth);
    }

    /// <summary>角的合法性：汇总行 + 四列表格。</summary>
    private static double DrawAnglesSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "角的合法性", y);

        y = DrawParagraph(
            context,
            viewModel.AngleSummary,
            y,
            contentWidth,
            13,
            MutedBrush);

        if (viewModel.Angles.Count > 0)
        {
            var rows = new List<AnalysisTableCell[]>(viewModel.Angles.Count);

            var alertRows = new bool[viewModel.Angles.Count];

            for (int index = 0; index < viewModel.Angles.Count; index++)
            {
                AngleRowViewModel angle = viewModel.Angles[index];

                rows.Add(new[]
                {
                    new AnalysisTableCell(angle.AngleText),
                    new AnalysisTableCell(
                        angle.LegalityText,
                        angle.IsLegal ? SuccessBrush : DangerBrush),
                    new AnalysisTableCell(
                        angle.ManufacturabilityText,
                        MutedBrush),
                    new AnalysisTableCell(
                        angle.NoteText,
                        MutedBrush,
                        Size: 11)
                });

                alertRows[index] = !angle.IsLegal;
            }

            y = DrawTable(
                context,
                new[] { "角", "合法性", "可制作性", "备注" },
                rows,
                new[] { 90.0, 100.0, 120.0, contentWidth - 310 },
                y,
                row => alertRows[row]);

            y = DrawParagraph(
                context,
                "角的可制作性暂时等同于合法性，等角的制作算法接入后细化。",
                y,
                contentWidth,
                11.5,
                MutedBrush);
        }

        return EndSection(context, y, contentWidth);
    }

    /// <summary>稳定性：直接使用分析窗口里的结论文本。</summary>
    private static double DrawStabilitySection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "稳定性", y);

        y = DrawParagraph(
            context,
            viewModel.StabilityText,
            y,
            contentWidth,
            13,
            BodyBrush);

        return EndSection(context, y, contentWidth);
    }

    /// <summary>
    /// 半边稳定性：汇总、定义说明，然后一轴一块，
    /// 每个轴块内的方向按列自动折行，文本按合法性着绿 / 红。
    /// 导出始终列出全部方向，不受窗口「只显示合法方向」开关影响。
    /// </summary>
    private static double DrawHalfSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "半边稳定性", y);

        y = DrawParagraph(
            context,
            viewModel.HalfSummary,
            y,
            contentWidth,
            13,
            BodyBrush);

        if (!viewModel.HasHalfDirections)
        {
            return EndSection(context, y, contentWidth);
        }

        y = DrawParagraph(
            context,
            "交换方向合法的定义：两个半边都稳定，并且该方向上左右各两个带柱子的半半边也全部稳定。",
            y,
            contentWidth,
            11.5,
            MutedBrush);

        y = DrawParagraph(
            context,
            "绿色为合法方向，红色为不合法方向。",
            y,
            contentWidth,
            11.5,
            MutedBrush);

        foreach (IGrouping<int, ExchangeDirection> group in
                 viewModel.ExchangeDirections
                     .GroupBy(direction => direction.Axis)
                     .OrderBy(group => group.Key))
        {
            y = DrawHalfAxisBlock(
                context,
                group.Key,
                group.ToList(),
                contentWidth,
                y);
        }

        return EndSection(context, y, contentWidth);
    }

    /// <summary>
    /// 一个轴块：标题「轴 N」+ 块内方向列表。
    /// 方向按列排布、列数由可用宽度决定（最小列宽 168），超出自动换行。
    /// </summary>
    private static double DrawHalfAxisBlock(
        DrawingContext context,
        int axis,
        IReadOnlyList<ExchangeDirection> directions,
        double contentWidth,
        double y)
    {
        const double paddingX = 12;

        const double paddingTop = 10;

        const double paddingBottom = 12;

        double innerWidth = contentWidth - paddingX * 2;

        int columns = Math.Max(
            1,
            (int)((innerWidth + HalfDirectionColumnGap) /
                  (HalfDirectionColumnWidth + HalfDirectionColumnGap)));

        double columnWidth =
            (innerWidth - (columns - 1) * HalfDirectionColumnGap) / columns;

        int rows = Math.Max(
            1,
            (directions.Count + columns - 1) / columns);

        FormattedText title = CreateText(
            $"轴 {axis + 1}",
            13,
            PrimaryDarkBrush,
            bold: true);

        double titleTop = y + paddingTop;

        double directionsTop = titleTop + title.Height + 8;

        double cardHeight = paddingTop +
                            title.Height +
                            8 +
                            rows * HalfDirectionRowHeight +
                            (rows - 1) * HalfDirectionRowGap +
                            paddingBottom;

        var card = new Rect(
            PagePadding,
            y,
            contentWidth,
            cardHeight);

        context.DrawRoundedRectangle(
            GraphBackgroundBrush,
            BorderPen,
            card,
            10,
            10);

        context.DrawText(title, new Point(PagePadding + paddingX, titleTop));

        for (int index = 0; index < directions.Count; index++)
        {
            ExchangeDirection direction = directions[index];

            int row = index / columns;

            int column = index % columns;

            FormattedText text = CreateText(
                direction.Display,
                13,
                direction.IsLegal ? LegalDirectionBrush : IllegalDirectionBrush);

            text.MaxTextWidth = Math.Max(1, columnWidth);

            context.DrawText(
                text,
                new Point(
                    PagePadding + paddingX + column * (columnWidth + HalfDirectionColumnGap),
                    directionsTop + row * (HalfDirectionRowHeight + HalfDirectionRowGap)));
        }

        return y + cardHeight + 10;
    }

    /// <summary>
    /// 制作路径：先「交换工序」（分层节点图），再按开关画「角的工序」占位。
    /// </summary>
    private static double DrawPathSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "制作路径", y);

        if (viewModel.HasProductionPath &&
            viewModel.IsPathExchangeExpanded)
        {
            y = DrawExchangeSubSection(context, viewModel, contentWidth, y);
        }

        if (viewModel.IsPathAnglesExpanded)
        {
            y = DrawAnglesProcessSubSection(context, contentWidth, y);
        }

        return EndSection(context, y, contentWidth);
    }

    /// <summary>
    /// 交换工序：结论 + 分层节点图。
    ///
    /// 节点图按原始尺寸整张画进导出图：节点宽高与页面一致，
    /// 预览固定 30px 单元格不缩放；废料节点淡显、虚线边框，
    /// 指向废料的箭头为灰色虚线。
    /// </summary>
    private static double DrawExchangeSubSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSubSectionTitle(context, "交换工序", contentWidth, y);

        y = DrawParagraph(
            context,
            viewModel.PathSummary,
            y,
            contentWidth,
            13,
            BodyBrush);

        if (viewModel.HasPathOutcomeNote)
        {
            y = DrawParagraph(
                context,
                viewModel.PathOutcomeNote,
                y,
                contentWidth,
                11.5,
                MutedBrush);
        }

        ProductionPathGraph? graph = viewModel.PathGraph;

        if (graph is null)
        {
            return y;
        }

        var graphRect = new Rect(
            PagePadding,
            y,
            graph.Width,
            graph.Height);

        context.DrawRoundedRectangle(
            GraphBackgroundBrush,
            BorderPen,
            graphRect,
            GraphCornerRadius,
            GraphCornerRadius);

        context.PushClip(new RectangleGeometry(
            graphRect,
            GraphCornerRadius,
            GraphCornerRadius));

        context.PushTransform(new TranslateTransform(
            PagePadding,
            y));

        foreach (ProductionPathGraphNode node in graph.Nodes)
        {
            DrawPathNode(context, node);
        }

        // 箭头画在节点之上，尖端不会被卡片遮住。
        foreach (ProductionPathGraphEdge edge in graph.Edges)
        {
            DrawPathEdge(context, edge);
        }

        context.Pop();

        context.Pop();

        y = graphRect.Bottom + 8;

        y = DrawParagraph(
            context,
            "节点按层从上到下排列，同层节点从左到右紧排，层间固定留 80px 画箭头；行数由制作路径决定（顶针 / 堆叠 / 块会追加新层）。预览保持 30px 单元格原始尺寸。废料节点淡显、虚线边框，指向废料的箭头为灰色虚线；顶针、堆叠、块分别用青绿 / 橙 / 绿边框与「顶P」「堆」「块」小标签区分。",
            y,
            contentWidth,
            11.5,
            MutedBrush);

        return y;
    }

    /// <summary>角的工序：占位说明，等角的制作算法接入后再绘制内容。</summary>
    private static double DrawAnglesProcessSubSection(
        DrawingContext context,
        double contentWidth,
        double y)
    {
        y = DrawSubSectionTitle(context, "角的工序", contentWidth, y);

        const double boxHeight = 64;

        var box = new Rect(
            PagePadding,
            y,
            contentWidth,
            boxHeight);

        context.DrawRoundedRectangle(
            ShapeCardBrush,
            PlaceholderPen,
            box,
            10,
            10);

        FormattedText text = CreateText(
            "待设计：后续按角绘制（每个角补柱后的制作工序）",
            13,
            PrimaryBrush,
            bold: true);

        text.MaxTextWidth = Math.Max(1, contentWidth - 28);

        text.TextAlignment = TextAlignment.Center;

        context.DrawText(
            text,
            new Point(
                PagePadding + 14,
                y + (boxHeight - text.Height) / 2));

        return y + boxHeight + 6;
    }

    #endregion

    #region 节点图

    /// <summary>画一个制作路径节点：卡片、标题与 1:1 预览。</summary>
    private static void DrawPathNode(
        DrawingContext context,
        ProductionPathGraphNode node)
    {
        var rect = new Rect(
            node.X,
            node.Y,
            node.Width,
            node.Height);

        // 节点类型决定卡片底色与边框：废料淡显、目标主色，
        // 顶针 / 堆叠 / 块各用一种强调色，其余保持默认。
        (Brush fill, Pen pen) = node switch
        {
            { IsWaste: true } => (WasteFillBrush, WasteNodePen),
            { IsResult: true } => (ShapeCardBrush, ResultNodePen),
            { IsPin: true } => (PinFillBrush, PinNodePen),
            { IsStack: true } => (StackFillBrush, StackNodePen),
            { IsBlock: true } => (BlockFillBrush, BlockNodePen),
            _ => (Brushes.White, NodeBorderPen)
        };

        context.DrawRoundedRectangle(
            fill,
            pen,
            rect,
            GraphCornerRadius,
            GraphCornerRadius);

        // 标题最多两行，与页面卡片一致；右侧要给类型小标签让出空间。
        FormattedText title = CreateText(
            node.Title,
            11,
            node.IsWaste ? WasteTitleBrush : NodeTitleBrush,
            bold: true);

        title.MaxTextWidth = Math.Max(
            1,
            rect.Width - 14 - (node.HasKindTag ? 22 : 0));

        title.MaxLineCount = 2;

        title.Trimming = TextTrimming.CharacterEllipsis;

        context.DrawText(
            title,
            new Point(rect.X + 7, rect.Y + 7));

        if (node.HasKindTag)
        {
            DrawPathNodeKindTag(context, node, rect);
        }

        if (node.Preview is null)
        {
            FormattedText placeholder = CreateText(
                node.PlaceholderText,
                12,
                MutedBrush);

            context.DrawText(
                placeholder,
                new Point(
                    rect.X + (rect.Width - placeholder.Width) / 2,
                    rect.Y + 37 + (rect.Height - 37 - placeholder.Height) / 2));

            return;
        }

        var previewRect = new Rect(
            rect.X + (rect.Width - node.PreviewWidth) / 2,
            rect.Y + 37,
            node.PreviewWidth,
            node.PreviewHeight);

        // 废料预览降低对比度，与窗口的 0.55 一致。
        if (node.IsWaste)
        {
            context.PushOpacity(0.55);
        }

        context.DrawImage(node.Preview, previewRect);

        if (node.IsWaste)
        {
            context.Pop();
        }
    }

    /// <summary>
    /// 画节点右上角的类型小标签（顶P / 堆 / 块）。
    /// 与窗口和 Web 一致：顶针青绿、堆叠橙、块绿。
    /// </summary>
    private static void DrawPathNodeKindTag(
        DrawingContext context,
        ProductionPathGraphNode node,
        Rect rect)
    {
        Brush accent = node.IsPin
            ? PinAccentBrush
            : node.IsStack
                ? StackAccentBrush
                : BlockAccentBrush;

        FormattedText text = CreateText(
            node.KindTag,
            9,
            Brushes.White,
            bold: true);

        double width = Math.Max(16, text.Width + 8);

        const double height = 14;

        double x = rect.Right - width - 6;

        double y = rect.Y + 6;

        context.DrawRoundedRectangle(
            accent,
            null,
            new Rect(x, y, width, height),
            6,
            6);

        context.DrawText(
            text,
            new Point(
                x + (width - text.Width) / 2,
                y + (height - text.Height) / 2));
    }

    /// <summary>画一条箭头：主路径紫色实线，指向废料的箭头灰色虚线。</summary>
    private static void DrawPathEdge(
        DrawingContext context,
        ProductionPathGraphEdge edge)
    {
        context.DrawGeometry(
            null,
            edge.IsWaste ? WasteEdgePen : EdgePen,
            edge.LineGeometry);

        context.DrawGeometry(
            edge.IsWaste ? WasteEdgeBrush : PrimaryBrush,
            null,
            edge.ArrowGeometry);
    }

    #endregion

    #region 底部

    /// <summary>底部分隔线、图形尺寸与生成时间。</summary>
    private static double DrawFooter(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y += 4;

        context.DrawLine(
            BorderPen,
            new Point(PagePadding, y),
            new Point(PagePadding + contentWidth, y));

        y += 14;

        FormattedText dimension = CreateText(
            viewModel.DimensionText,
            12,
            MutedBrush);

        context.DrawText(dimension, new Point(PagePadding, y));

        string generated =
            $"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}";

        FormattedText generatedText = CreateText(
            generated,
            12,
            MutedBrush);

        context.DrawText(
            generatedText,
            new Point(
                PagePadding + contentWidth - generatedText.Width,
                y));

        return y + dimension.Height;
    }

    #endregion

    #region 绘制工具

    /// <summary>板块标题：左侧一道主色竖条，与 Web 导出图一致。</summary>
    private static double DrawSectionTitle(
        DrawingContext context,
        string title,
        double y)
    {
        context.DrawRoundedRectangle(
            PrimaryBrush,
            null,
            new Rect(PagePadding, y + 4, 4, 18),
            2,
            2);

        FormattedText text = CreateText(
            title,
            18,
            TextBrush,
            bold: true);

        context.DrawText(
            text,
            new Point(PagePadding + 12, y + 1));

        return y + SectionTitleHeight;
    }

    /// <summary>制作路径下的子区域标题，上方用一道细分隔线收束。</summary>
    private static double DrawSubSectionTitle(
        DrawingContext context,
        string title,
        double contentWidth,
        double y)
    {
        y += 4;

        context.DrawLine(
            DividerPen,
            new Point(PagePadding, y),
            new Point(PagePadding + contentWidth, y));

        y += 12;

        FormattedText text = CreateText(
            title,
            14,
            BodyBrush,
            bold: true);

        context.DrawText(text, new Point(PagePadding, y));

        return y + text.Height + 8;
    }

    /// <summary>一段可换行的正文。</summary>
    private static double DrawParagraph(
        DrawingContext context,
        string text,
        double y,
        double contentWidth,
        double size = 13,
        Brush? brush = null,
        double indent = 0)
    {
        FormattedText formatted = CreateText(
            text,
            size,
            brush ?? BodyBrush);

        formatted.MaxTextWidth = Math.Max(1, contentWidth - indent);

        context.DrawText(
            formatted,
            new Point(PagePadding + indent, y));

        return y + formatted.Height + 4;
    }

    /// <summary>图形性质标签，按内容宽度自动换行。</summary>
    private static double DrawChips(
        DrawingContext context,
        IEnumerable<string> labels,
        double contentWidth,
        double y)
    {
        const double chipHeight = 28;

        const double chipGap = 8;

        const double chipPadding = 11;

        double x = PagePadding;

        foreach (string label in labels)
        {
            FormattedText text = CreateText(
                label,
                12,
                PrimaryDarkBrush);

            double chipWidth = text.Width + chipPadding * 2;

            if (x > PagePadding &&
                x + chipWidth > PagePadding + contentWidth)
            {
                x = PagePadding;

                y += chipHeight + chipGap;
            }

            var chip = new Rect(x, y, chipWidth, chipHeight);

            context.DrawRoundedRectangle(
                PrimarySoftBrush,
                null,
                chip,
                10,
                10);

            context.DrawText(
                text,
                new Point(
                    x + chipPadding,
                    y + (chipHeight - text.Height) / 2));

            x += chipWidth + chipGap;
        }

        return y + chipHeight + 8;
    }

    /// <summary>
    /// 画一张表格：淡紫表头、隔行浅底、非法行浅红底，
    /// 行高按单元格里换行后的最高文本计算。
    /// </summary>
    private static double DrawTable(
        DrawingContext context,
        IReadOnlyList<string> headers,
        IReadOnlyList<AnalysisTableCell[]> rows,
        double[] widths,
        double y,
        Func<int, bool>? isAlert = null)
    {
        const double headerHeight = 30;

        const double cellPaddingX = 10;

        const double cellPaddingY = 7;

        double tableWidth = 0;

        foreach (double width in widths)
        {
            tableWidth += width;
        }

        var rowTexts = new List<FormattedText[]>(rows.Count);

        var rowHeights = new List<double>(rows.Count);

        foreach (AnalysisTableCell[] row in rows)
        {
            var texts = new FormattedText[headers.Count];

            double height = 0;

            for (int column = 0; column < headers.Count; column++)
            {
                AnalysisTableCell cell = column < row.Length
                    ? row[column]
                    : new AnalysisTableCell("");

                FormattedText text = CreateText(
                    cell.Text,
                    cell.Size,
                    cell.Brush ?? BodyBrush,
                    cell.Bold,
                    cell.Mono);

                text.MaxTextWidth = Math.Max(
                    1,
                    widths[column] - cellPaddingX * 2);

                texts[column] = text;

                height = Math.Max(height, text.Height);
            }

            rowTexts.Add(texts);

            rowHeights.Add(Math.Max(26, height + cellPaddingY * 2));
        }

        double bodyHeight = 0;

        foreach (double rowHeight in rowHeights)
        {
            bodyHeight += rowHeight;
        }

        var bounds = new Rect(
            PagePadding,
            y,
            tableWidth,
            headerHeight + bodyHeight);

        // 先铺白色底，再在圆角内画表头与行，最后补外框，
        // 避免表头底色盖住外框。
        context.DrawRoundedRectangle(
            Brushes.White,
            null,
            bounds,
            8,
            8);

        context.PushClip(new RectangleGeometry(bounds, 8, 8));

        context.DrawRectangle(
            TableHeaderBrush,
            null,
            new Rect(PagePadding, y, tableWidth, headerHeight));

        double x = PagePadding;

        for (int column = 0; column < headers.Count; column++)
        {
            FormattedText header = CreateText(
                headers[column],
                12,
                TableHeaderTextBrush,
                bold: true);

            header.MaxTextWidth = Math.Max(
                1,
                widths[column] - cellPaddingX * 2);

            header.MaxLineCount = 1;

            header.Trimming = TextTrimming.CharacterEllipsis;

            context.DrawText(
                header,
                new Point(
                    x + cellPaddingX,
                    y + (headerHeight - header.Height) / 2));

            x += widths[column];
        }

        double rowTop = y + headerHeight;

        for (int row = 0; row < rows.Count; row++)
        {
            double rowHeight = rowHeights[row];

            if (isAlert?.Invoke(row) == true)
            {
                context.DrawRectangle(
                    DangerSoftBrush,
                    null,
                    new Rect(PagePadding, rowTop, tableWidth, rowHeight));
            }
            else if (row % 2 == 1)
            {
                context.DrawRectangle(
                    ZebraBrush,
                    null,
                    new Rect(PagePadding, rowTop, tableWidth, rowHeight));
            }

            x = PagePadding;

            FormattedText[] texts = rowTexts[row];

            for (int column = 0; column < texts.Length; column++)
            {
                context.DrawText(
                    texts[column],
                    new Point(
                        x + cellPaddingX,
                        rowTop + (rowHeight - texts[column].Height) / 2));

                x += widths[column];
            }

            context.DrawLine(
                DividerPen,
                new Point(PagePadding, rowTop + rowHeight),
                new Point(PagePadding + tableWidth, rowTop + rowHeight));

            rowTop += rowHeight;
        }

        context.Pop();

        context.DrawRoundedRectangle(
            null,
            BorderPen,
            bounds,
            8,
            8);

        return y + bounds.Height + 10;
    }

    /// <summary>板块内容之后画一条细分隔线，并留出下一板块的间距。</summary>
    private static double EndSection(
        DrawingContext context,
        double y,
        double contentWidth)
    {
        context.DrawLine(
            DividerPen,
            new Point(PagePadding, y + 2),
            new Point(PagePadding + contentWidth, y + 2));

        return y + SectionGap;
    }

    private static FormattedText CreateText(
        string text,
        double size,
        Brush brush,
        bool bold = false,
        bool mono = false)
    {
        return new FormattedText(
            text ?? "",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            mono
                ? MonoTypeface
                : bold
                    ? UiBoldTypeface
                    : UiTypeface,
            size,
            brush,
            1.0);
    }

    private static Brush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(
            Color.FromRgb(red, green, blue));

        brush.Freeze();

        return brush;
    }

    private static Pen CreatePen(
        Brush brush,
        double thickness)
    {
        var pen = new Pen(brush, thickness);

        pen.Freeze();

        return pen;
    }

    /// <summary>灰色虚线画笔，用于废料节点与废料箭头。</summary>
    private static Pen CreateDashedPen(
        Brush brush,
        double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            DashStyle = new DashStyle(new[] { 2.5, 2.0 }, 0)
        };

        pen.Freeze();

        return pen;
    }

    /// <summary>
    /// 表格单元格。颜色为空时使用正文色，字号默认 12。
    /// </summary>
    private readonly record struct AnalysisTableCell(
        string Text,
        Brush? Brush = null,
        bool Bold = false,
        bool Mono = false,
        double Size = 12);

    #endregion
}

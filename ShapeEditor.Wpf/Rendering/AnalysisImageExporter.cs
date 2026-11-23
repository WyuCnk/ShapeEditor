using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Presentation.Styling;
using ShapeEditor.Wpf.Cards;
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

    /// <summary>工序卡片的标题色（与窗口 #3A3A44 一致）。</summary>
    private static readonly Brush CardTitleBrush = CreateBrush(0x3A, 0x3A, 0x44);

    /// <summary>卡片内小节标题色。</summary>
    private static readonly Brush CardSectionTitleBrush = CreateBrush(0x45, 0x27, 0xA0);

    /// <summary>前态小节标题色（紫）。</summary>
    private static readonly Brush CardPreStateTitleBrush = CreateBrush(0x5E, 0x35, 0xB1);

    private static readonly Brush ModeChipFillBrush = CreateBrush(0xEE, 0xF1, 0xFC);

    private static readonly Brush ModeChipTextBrush = CreateBrush(0x3F, 0x51, 0xB5);

    private static readonly Brush VerifiedChipFillBrush = CreateBrush(0xEA, 0xF7, 0xEE);

    private static readonly Brush VerifiedChipTextBrush = CreateBrush(0x2E, 0x7D, 0x32);

    private static readonly Brush UnverifiedChipFillBrush = CreateBrush(0xFD, 0xEA, 0xEA);

    private static readonly Brush UnverifiedChipTextBrush = CreateBrush(0xC6, 0x28, 0x28);

    private static readonly Brush UnverifiedCardFillBrush = CreateBrush(0xFF, 0xFA, 0xFA);

    private static readonly Brush UnverifiedCardBorderBrush = CreateBrush(0xE7, 0xB6, 0xB6);

    private static readonly Brush WarningFillBrush = CreateBrush(0xFD, 0xF6, 0xF6);

    private static readonly Brush WarningBorderBrush = CreateBrush(0xD6, 0x45, 0x45);

    private static readonly Brush WarningNoteBrush = CreateBrush(0x5B, 0x5B, 0x66);

    private static readonly Brush LegacyNoteFillBrush = CreateBrush(0xFF, 0xFA, 0xEB);

    private static readonly Brush LegacyNoteBorderBrush = CreateBrush(0xC9, 0xA2, 0x27);

    private static readonly Brush LegacyNoteTextBrush = CreateBrush(0x7A, 0x64, 0x15);

    private static readonly Brush LayerFillBrush = CreateBrush(0xFB, 0xFA, 0xFF);

    private static readonly Brush LayerBorderBrush = CreateBrush(0xE6, 0xE2, 0xF2);

    private static readonly Brush PreStateFillBrush = CreateBrush(0xF6, 0xF4, 0xFF);

    private static readonly Brush PreStateBorderBrush = CreateBrush(0xB3, 0x9D, 0xDB);

    private static readonly Brush InputFillBrush = CreateBrush(0xFA, 0xF8, 0xFF);

    private static readonly Brush InputBorderBrush = CreateBrush(0xE6, 0xE2, 0xF2);

    private static readonly Brush StepHeaderBrush = CreateBrush(0x4A, 0x3F, 0x72);

    /// <summary>工序卡片的布局常量：与窗口的 WorkOrderCardPanel / XAML 保持一致。</summary>
    private const double WorkOrderCardGap = 12;

    private const double WorkOrderCardPadding = 12;

    private const double WorkOrderLayerPadding = 12;

    private const double WorkOrderChipHeight = 18;

    private const double WorkOrderChipGap = 6;

    private const double WorkOrderStepArrowWidth = 22;

    private const double WorkOrderStepRowGap = 8;

    /// <summary>内容宽度达到这个值时一行放两张卡，否则一行一张（与窗口面板一致）。</summary>
    private const double WorkOrderTwoColumnWidth = 620;

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
             * 工序卡片与窗口同源：有卡片集时画三层卡片（交换 → 半边 → 角），
             * 不再画旧的「交换工序」节点图与「角的工序」占位；
             * 没有卡片（例如 n = 2）时才回退旧的制作路径。
             */
            if (viewModel.HasWorkOrderCards)
            {
                y = DrawWorkOrderCardsSection(
                    context,
                    viewModel,
                    contentWidth,
                    y);
            }
            else
            {
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

        const double baseCell = CardMetrics.CellSize;

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

    #region 工序卡片

    /// <summary>
    /// 三层工序卡片：与窗口消费同一个 <see cref="WorkOrderCardVisualSet"/>，
    /// 不重跑 <c>CardBuilder</c>，也不再画旧的交换工序节点图 / 角的工序占位。
    /// </summary>
    private static double DrawWorkOrderCardsSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double contentWidth,
        double y)
    {
        y = DrawSectionTitle(context, "工序卡片", y);

        y = DrawParagraph(
            context,
            viewModel.CardsSummary,
            y,
            contentWidth,
            13,
            BodyBrush);

        // 与窗口一致：折叠时只保留汇总，不画 Note / 说明与卡片。
        if (!viewModel.IsCardsExpanded)
        {
            return EndSection(context, y, contentWidth);
        }

        if (viewModel.HasCardsNote)
        {
            y = DrawParagraph(
                context,
                viewModel.CardsNote,
                y,
                contentWidth,
                11.5,
                MutedBrush);
        }

        y = DrawParagraph(
            context,
            "按「交换工序 → 半边工序 → 角工序」三层从外到内平铺，同一层内卡片横向成行；" +
            "每张卡显示标题、验证标记、输入（上一层卡产物）、前态、可见步骤与输出。",
            y,
            contentWidth,
            11.5,
            MutedBrush);

        y = DrawParagraph(
            context,
            "前态画在输入之后、可见步骤之前：v4 半边卡是「拆柱后（跌落前）」的中间态，" +
            "角卡是工序起点；没有前态的卡不画这一块。旋转 / 废料 / 纯支撑交接等隐藏步骤不导出；" +
            "未验证的卡不导出输入链，只显示 Note。",
            y,
            contentWidth,
            11.5,
            MutedBrush);

        y = DrawParagraph(
            context,
            "角卡输出已归一化到 Q1：Q1 目标角、Q2 右柱、Q4 左柱（n = 4）；" +
            "预览均为 30px 单元格的 1:1 竖式图。",
            y,
            contentWidth,
            11.5,
            MutedBrush);

        y += 4;

        WorkOrderCardVisualSet? cards = viewModel.CardVisuals;

        if (cards is not null && viewModel.IsCardsExpanded)
        {
            foreach (WorkOrderCardLayerViewModel layer in cards.Layers)
            {
                y = DrawWorkOrderLayer(context, layer, contentWidth, y);
            }
        }

        return EndSection(context, y, contentWidth);
    }

    /// <summary>画一层卡片：左侧强调色条、层标题（带张数）与横向卡片行。</summary>
    private static double DrawWorkOrderLayer(
        DrawingContext context,
        WorkOrderCardLayerViewModel layer,
        double contentWidth,
        double y)
    {
        int columns = contentWidth >= WorkOrderTwoColumnWidth ? 2 : 1;

        double innerWidth = Math.Max(
            1,
            contentWidth - WorkOrderLayerPadding * 2);

        double cardWidth = Math.Max(
            1,
            (innerWidth - WorkOrderCardGap * (columns - 1)) / columns);

        /*
         * 输出 / 单步节点 / 前态都按 1:1 绘制、不缩放；
         * 两列放不下单张卡的内容时退回一列，避免图形越出卡片边框。
         */
        double requiredWidth = 0;

        foreach (WorkOrderCardViewModel card in layer.Cards)
        {
            requiredWidth = Math.Max(requiredWidth, card.Output.Width + 20);

            if (card.PreState is not null)
            {
                requiredWidth = Math.Max(requiredWidth, card.PreState.Width + 18);
            }

            foreach (WorkOrderStepViewModel step in card.Steps)
            {
                requiredWidth = Math.Max(requiredWidth, step.Width);
            }
        }

        if (columns > 1 &&
            requiredWidth > cardWidth - WorkOrderCardPadding * 2)
        {
            columns = 1;

            cardWidth = innerWidth;
        }

        // 层里只有一张卡时让它占满整行，与窗口的 WorkOrderCardPanel 一致。
        if (layer.Cards.Count == 1)
        {
            columns = 1;

            cardWidth = innerWidth;
        }

        var cardHeights = new double[layer.Cards.Count];

        for (int index = 0; index < layer.Cards.Count; index++)
        {
            BuildWorkOrderCard(
                layer.Cards[index],
                0,
                0,
                cardWidth,
                out cardHeights[index]);
        }

        FormattedText title = CreateText(
            layer.Title,
            14,
            CardTitleBrush,
            bold: true);

        FormattedText count = CreateText(
            layer.CountText,
            11,
            MutedBrush,
            bold: true);

        int rowCount = (layer.Cards.Count + columns - 1) / columns;

        double cardsHeight = 0;

        for (int row = 0; row < rowCount; row++)
        {
            double rowHeight = 0;

            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;

                if (index < cardHeights.Length)
                {
                    rowHeight = Math.Max(rowHeight, cardHeights[index]);
                }
            }

            cardsHeight += rowHeight;

            if (row < rowCount - 1)
            {
                cardsHeight += WorkOrderCardGap;
            }
        }

        double headerGap = layer.Cards.Count > 0 ? 8 : 0;

        double layerHeight =
            WorkOrderLayerPadding * 2 +
            title.Height +
            headerGap +
            cardsHeight;

        var layerRect = new Rect(
            PagePadding,
            y,
            contentWidth,
            layerHeight);

        context.DrawRoundedRectangle(
            LayerFillBrush,
            BorderPen,
            layerRect,
            10,
            10);

        // 左侧 3px 强调色条：交换 / 半边 / 角各一种颜色。
        if (layerHeight > 24)
        {
            context.DrawRoundedRectangle(
                layer.AccentBrush,
                null,
                new Rect(PagePadding, y + 10, 3, layerHeight - 20),
                1.5,
                1.5);
        }

        context.DrawText(
            title,
            new Point(PagePadding + WorkOrderLayerPadding, y + WorkOrderLayerPadding));

        context.DrawText(
            count,
            new Point(
                PagePadding + WorkOrderLayerPadding + title.Width + 8,
                y + WorkOrderLayerPadding + title.Height - count.Height));

        double cardTop = y + WorkOrderLayerPadding + title.Height + headerGap;

        for (int row = 0; row < rowCount; row++)
        {
            double rowHeight = 0;

            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;

                if (index < layer.Cards.Count)
                {
                    rowHeight = Math.Max(rowHeight, cardHeights[index]);

                    double cardLeft =
                        PagePadding +
                        WorkOrderLayerPadding +
                        column * (cardWidth + WorkOrderCardGap);

                    List<WorkOrderDrawOp> ops = BuildWorkOrderCard(
                        layer.Cards[index],
                        cardLeft,
                        cardTop,
                        cardWidth,
                        out _);

                    DrawWorkOrderOps(context, ops);
                }
            }

            cardTop += rowHeight;

            if (row < rowCount - 1)
            {
                cardTop += WorkOrderCardGap;
            }
        }

        return y + layerHeight + 12;
    }

    /// <summary>
    /// 生成一张卡片的全部绘制指令（背景、卡框、标题、警示、输入、前态、步骤、输出）。
    ///
    /// <para>
    /// 指令列表同时用于测量与绘制：窗口和导出消费同一份 <see cref="WorkOrderCardViewModel"/>，
    /// 未验证的卡在视图里已经没有输入链，这里自然也不会画出输入。
    /// </para>
    /// </summary>
    private static List<WorkOrderDrawOp> BuildWorkOrderCard(
        WorkOrderCardViewModel card,
        double x,
        double y,
        double width,
        out double height)
    {
        var ops = new List<WorkOrderDrawOp>();

        double contentWidth = Math.Max(1, width - WorkOrderCardPadding * 2);

        double left = x + WorkOrderCardPadding;

        double top = y + WorkOrderCardPadding;

        // 标题：最多一行，超长省略。
        FormattedText title = CreateText(
            card.Title,
            14,
            CardTitleBrush,
            bold: true);

        title.MaxTextWidth = contentWidth;

        title.MaxLineCount = 1;

        title.Trimming = TextTrimming.CharacterEllipsis;

        ops.Add(TextOp(title, left, top));

        // 标题行的小标签：种类 / 半边 Mode / 验证标记。
        // 与窗口的 WrapPanel 一致：优先跟在标题后面，放不下时换行。
        var chips = BuildWorkOrderChips(card);

        double chipX = title.Width + 8;

        double chipTop =
            top + (title.Height - WorkOrderChipHeight) / 2;

        double headerBottom = title.Height;

        int chipLine = 0;

        foreach (WorkOrderChipLayout chip in chips)
        {
            if (chipX + chip.Width > contentWidth)
            {
                chipX = 0;

                chipLine++;

                chipTop =
                    top +
                    title.Height +
                    4 +
                    (chipLine - 1) * (WorkOrderChipHeight + 4);
            }

            ops.Add(new WorkOrderDrawOp
            {
                Rect = new Rect(
                    left + chipX,
                    chipTop,
                    chip.Width,
                    WorkOrderChipHeight),
                Fill = chip.Fill,
                Radius = 9,
                Text = chip.Text,
                TextCentered = true
            });

            chipX += chip.Width + WorkOrderChipGap;

            headerBottom = Math.Max(
                headerBottom,
                chipTop - top + WorkOrderChipHeight);
        }

        top += headerBottom + 8;

        // 旧版 B 类·碎晶半边卡：只兼容历史数据，加一条降级说明。
        if (card.HasLegacyNote)
        {
            FormattedText note = CreateText(
                card.LegacyNote,
                11.5,
                LegacyNoteTextBrush);

            note.MaxTextWidth = Math.Max(1, contentWidth - 20);

            double boxHeight = note.Height + 12;

            ops.Add(new WorkOrderDrawOp
            {
                Rect = new Rect(left, top, contentWidth, boxHeight),
                Fill = LegacyNoteFillBrush,
                Stroke = LegacyNoteBorderBrush,
                StrokeThickness = 1,
                Radius = 6
            });

            ops.Add(TextOp(note, left + 10, top + 6));

            top += boxHeight + 6;
        }

        // 未验证：红色警示 + Note；不画输入链。
        if (card.ShowWarning)
        {
            FormattedText warningTitle = CreateText(
                "未通过真实回放",
                12,
                UnverifiedChipTextBrush,
                bold: true);

            FormattedText? warningNote = null;

            if (card.HasNote)
            {
                warningNote = CreateText(
                    card.Note,
                    12,
                    WarningNoteBrush);

                warningNote.MaxTextWidth = Math.Max(1, contentWidth - 20);
            }

            double boxHeight =
                8 +
                warningTitle.Height +
                (warningNote is null ? 0 : 4 + warningNote.Height) +
                8;

            ops.Add(new WorkOrderDrawOp
            {
                Rect = new Rect(left, top, contentWidth, boxHeight),
                Fill = WarningFillBrush,
                Stroke = WarningBorderBrush,
                StrokeThickness = 1,
                Radius = 6
            });

            ops.Add(TextOp(warningTitle, left + 10, top + 8));

            if (warningNote is not null)
            {
                ops.Add(TextOp(
                    warningNote,
                    left + 10,
                    top + 8 + warningTitle.Height + 4));
            }

            top += boxHeight + 6;
        }

        // 输入：上一层卡产物，多张并排。
        if (card.HasInputs)
        {
            AppendWorkOrderInputs(ops, card, left, contentWidth, ref top);
        }

        // 前态：输入之后、可见步骤之前。
        if (card.HasPreState && card.PreState is not null)
        {
            AppendWorkOrderPreState(ops, card, left, contentWidth, ref top);
        }

        // 可见步骤：操作名 + 该步输出图形。
        AppendWorkOrderSteps(ops, card, left, contentWidth, ref top);

        // 输出：醒目边框。
        AppendWorkOrderOutput(ops, card, left, ref top);

        // 角卡小字：Q1 目标角 / Q2 右柱 / Q4 左柱。
        if (card.HasCornerCaption)
        {
            FormattedText corner = CreateText(
                card.CornerCaption,
                11,
                MutedBrush);

            corner.MaxTextWidth = contentWidth;

            top += 6;

            ops.Add(TextOp(corner, left, top));

            top += corner.Height;
        }

        height = top - y + WorkOrderCardPadding;

        ops.Insert(0, new WorkOrderDrawOp
        {
            Rect = new Rect(x, y, width, height),
            Fill = card.Verified ? Brushes.White : UnverifiedCardFillBrush,
            Stroke = card.Verified
                ? LayerBorderBrush
                : UnverifiedCardBorderBrush,
            StrokeThickness = 1,
            Radius = 10
        });

        return ops;
    }

    private static void AppendWorkOrderInputs(
        List<WorkOrderDrawOp> ops,
        WorkOrderCardViewModel card,
        double left,
        double contentWidth,
        ref double top)
    {
        FormattedText title = CreateText(
            "输入（上一层卡产物）",
            12,
            CardSectionTitleBrush,
            bold: true);

        ops.Add(TextOp(title, left, top));

        top += title.Height + 6;

        var boxes = new List<WorkOrderInputBoxLayout>(card.Inputs.Count);

        double x = 0;

        double rowTop = 0;

        double rowHeight = 0;

        foreach (WorkOrderInputViewModel input in card.Inputs)
        {
            FormattedText inputTitle = CreateText(
                input.Title,
                11,
                MutedBrush,
                bold: true);

            inputTitle.MaxTextWidth = Math.Max(1, contentWidth - 16);

            double boxWidth = Math.Max(
                inputTitle.Width,
                input.Preview.Width) + 16;

            double boxHeight =
                8 +
                inputTitle.Height +
                6 +
                input.Preview.Height +
                8;

            if (x > 0 && x + boxWidth > contentWidth)
            {
                rowTop += rowHeight + 12;

                x = 0;

                rowHeight = 0;
            }

            boxes.Add(new WorkOrderInputBoxLayout(
                inputTitle,
                input.Preview,
                boxWidth,
                boxHeight,
                x,
                rowTop));

            rowHeight = Math.Max(rowHeight, boxHeight);

            x += boxWidth + 12;
        }

        foreach (WorkOrderInputBoxLayout box in boxes)
        {
            double boxLeft = left + box.X;

            double boxTop = top + box.Y;

            ops.Add(new WorkOrderDrawOp
            {
                Rect = new Rect(boxLeft, boxTop, box.Width, box.Height),
                Fill = InputFillBrush,
                Stroke = InputBorderBrush,
                StrokeThickness = 1,
                Radius = 8
            });

            ops.Add(TextOp(box.Title, boxLeft + 8, boxTop + 8));

            ops.Add(ImageOp(
                box.Preview,
                boxLeft + 8,
                boxTop + 8 + box.Title.Height + 6));
        }

        top += rowTop + rowHeight + 8;
    }

    private static void AppendWorkOrderPreState(
        List<WorkOrderDrawOp> ops,
        WorkOrderCardViewModel card,
        double left,
        double contentWidth,
        ref double top)
    {
        FormattedText title = CreateText(
            card.PreStateSectionTitle,
            12,
            CardPreStateTitleBrush,
            bold: true);

        title.MaxTextWidth = Math.Max(1, contentWidth);

        ops.Add(TextOp(title, left, top));

        top += title.Height + 6;

        ShapePreviewViewModel preview = card.PreState!;

        double boxWidth = preview.Width + 18;

        double boxHeight = preview.Height + 18;

        ops.Add(new WorkOrderDrawOp
        {
            Rect = new Rect(left, top, boxWidth, boxHeight),
            Fill = PreStateFillBrush,
            Stroke = PreStateBorderBrush,
            StrokeThickness = 1,
            Radius = 8,
            Dashed = true
        });

        ops.Add(ImageOp(preview, left + 9, top + 9));

        top += boxHeight + 8;
    }

    private static void AppendWorkOrderSteps(
        List<WorkOrderDrawOp> ops,
        WorkOrderCardViewModel card,
        double left,
        double contentWidth,
        ref double top)
    {
        if (!card.HasSteps)
        {
            if (card.ShowEmptySteps)
            {
                FormattedText empty = CreateText(
                    "没有可见步骤：旋转 / 废料 / 纯支撑交接等结构步骤已在 Core 中标记为隐藏。",
                    12,
                    MutedBrush);

                empty.MaxTextWidth = contentWidth;

                ops.Add(TextOp(empty, left, top));

                top += empty.Height + 8;
            }

            return;
        }

        FormattedText title = CreateText(
            card.StepsTitle,
            12,
            CardSectionTitleBrush,
            bold: true);

        ops.Add(TextOp(title, left, top));

        top += title.Height + 6;

        var rows = new List<List<WorkOrderStepViewModel>>();

        var current = new List<WorkOrderStepViewModel>();

        double currentWidth = 0;

        foreach (WorkOrderStepViewModel step in card.Steps)
        {
            double needed = step.Width +
                            (current.Count > 0 ? WorkOrderStepArrowWidth : 0);

            if (current.Count > 0 && currentWidth + needed > contentWidth)
            {
                rows.Add(current);

                current = new List<WorkOrderStepViewModel>();

                currentWidth = 0;

                needed = step.Width;
            }

            current.Add(step);

            currentWidth += needed;
        }

        if (current.Count > 0)
        {
            rows.Add(current);
        }

        FormattedText arrow = CreateText(
            "→",
            16,
            PrimaryBrush,
            bold: true);

        for (int row = 0; row < rows.Count; row++)
        {
            List<WorkOrderStepViewModel> steps = rows[row];

            double rowHeight = 0;

            foreach (WorkOrderStepViewModel step in steps)
            {
                rowHeight = Math.Max(rowHeight, step.Height);
            }

            double nodeLeft = 0;

            for (int index = 0; index < steps.Count; index++)
            {
                WorkOrderStepViewModel step = steps[index];

                double nodeLeftAbsolute = left + nodeLeft;

                ops.Add(new WorkOrderDrawOp
                {
                    Rect = new Rect(
                        nodeLeftAbsolute,
                        top,
                        step.Width,
                        step.Height),
                    Fill = step.FillBrush,
                    Stroke = step.AccentBrush,
                    StrokeThickness = 1.5,
                    Radius = 10
                });

                FormattedText operation = CreateText(
                    step.Operation,
                    11,
                    StepHeaderBrush,
                    bold: true);

                operation.MaxTextWidth = Math.Max(1, step.Width - 14);

                operation.MaxLineCount = 1;

                operation.Trimming = TextTrimming.CharacterEllipsis;

                ops.Add(TextOp(
                    operation,
                    nodeLeftAbsolute + 7,
                    top + 7));

                ShapePreviewViewModel preview = step.Preview;

                ops.Add(ImageOp(
                    preview,
                    nodeLeftAbsolute + (step.Width - preview.Width) / 2,
                    top + 30 + Math.Max(0, step.Height - 30 - preview.Height) / 2));

                nodeLeft += step.Width;

                /*
                 * 「→」画在节点之后：同一行还有下一步，或后面还有换行的步骤时都画，
                 * 换行后的第一张节点不会带着行首箭头。
                 */
                bool hasNext = index < steps.Count - 1 || row < rows.Count - 1;

                if (hasNext &&
                    nodeLeft + WorkOrderStepArrowWidth <= contentWidth)
                {
                    ops.Add(new WorkOrderDrawOp
                    {
                        Rect = new Rect(
                            left + nodeLeft,
                            top + (rowHeight - arrow.Height) / 2,
                            WorkOrderStepArrowWidth,
                            arrow.Height),
                        Text = arrow,
                        TextCentered = true
                    });

                    nodeLeft += WorkOrderStepArrowWidth;
                }
            }

            top += rowHeight;

            if (row < rows.Count - 1)
            {
                top += WorkOrderStepRowGap;
            }
        }

        top += 8;
    }

    private static void AppendWorkOrderOutput(
        List<WorkOrderDrawOp> ops,
        WorkOrderCardViewModel card,
        double left,
        ref double top)
    {
        FormattedText title = CreateText(
            "输出",
            12,
            CardSectionTitleBrush,
            bold: true);

        ops.Add(TextOp(title, left, top));

        if (card.HasOutputCaption)
        {
            FormattedText caption = CreateText(
                card.OutputCaption,
                11,
                MutedBrush);

            ops.Add(TextOp(
                caption,
                left + title.Width + 6,
                top + title.Height - caption.Height));
        }

        top += title.Height + 6;

        ShapePreviewViewModel preview = card.Output;

        double boxWidth = preview.Width + 20;

        double boxHeight = preview.Height + 20;

        ops.Add(new WorkOrderDrawOp
        {
            Rect = new Rect(left, top, boxWidth, boxHeight),
            Fill = InputFillBrush,
            Stroke = PrimaryBrush,
            StrokeThickness = 2,
            Radius = 10
        });

        ops.Add(ImageOp(preview, left + 10, top + 10));

        top += boxHeight;
    }

    /// <summary>标题行的小标签：种类 / 半边 Mode / 验证标记。</summary>
    private static List<WorkOrderChipLayout> BuildWorkOrderChips(
        WorkOrderCardViewModel card)
    {
        var chips = new List<WorkOrderChipLayout>(3)
        {
            CreateChip(card.KindText, PrimarySoftBrush, PrimaryBrush)
        };

        if (card.HasModeText)
        {
            chips.Add(CreateChip(card.ModeText, ModeChipFillBrush, ModeChipTextBrush));
        }

        chips.Add(card.Verified
            ? CreateChip(card.VerifiedText, VerifiedChipFillBrush, VerifiedChipTextBrush)
            : CreateChip(card.VerifiedText, UnverifiedChipFillBrush, UnverifiedChipTextBrush));

        return chips;
    }

    private static WorkOrderChipLayout CreateChip(
        string text,
        Brush fill,
        Brush foreground)
    {
        FormattedText formatted = CreateText(
            text,
            11,
            foreground,
            bold: true);

        return new WorkOrderChipLayout(
            formatted,
            fill,
            formatted.Width + 14);
    }

    private static WorkOrderDrawOp TextOp(
        FormattedText text,
        double x,
        double y) =>
        new()
        {
            Rect = new Rect(x, y, Math.Max(1, text.Width), text.Height),
            Text = text
        };

    private static WorkOrderDrawOp ImageOp(
        ShapePreviewViewModel preview,
        double x,
        double y) =>
        new()
        {
            Rect = new Rect(x, y, preview.Width, preview.Height),
            Image = preview.Preview
        };

    /// <summary>把一张卡片生成的绘制指令按顺序画出来。</summary>
    private static void DrawWorkOrderOps(
        DrawingContext context,
        IReadOnlyList<WorkOrderDrawOp> ops)
    {
        foreach (WorkOrderDrawOp op in ops)
        {
            if (op.Fill is not null || op.Stroke is not null)
            {
                Pen? pen = null;

                if (op.Stroke is not null)
                {
                    pen = op.Dashed
                        ? CreateDashedPen(op.Stroke, op.StrokeThickness)
                        : CreatePen(op.Stroke, op.StrokeThickness);
                }

                context.DrawRoundedRectangle(
                    op.Fill,
                    pen,
                    op.Rect,
                    op.Radius,
                    op.Radius);
            }

            if (op.Image is not null)
            {
                context.DrawImage(op.Image, op.Rect);
            }

            if (op.Text is not null)
            {
                double textX = op.TextCentered
                    ? op.Rect.X + (op.Rect.Width - op.Text.Width) / 2
                    : op.Rect.X;

                double textY = op.TextCentered
                    ? op.Rect.Y + (op.Rect.Height - op.Text.Height) / 2
                    : op.Rect.Y;

                context.DrawText(op.Text, new Point(textX, textY));
            }
        }
    }

    /// <summary>一张卡片的一条绘制指令：圆角底 / 边框、图像与文本三者之一或组合。</summary>
    private sealed class WorkOrderDrawOp
    {
        public Rect Rect;

        public FormattedText? Text;

        public ImageSource? Image;

        public Brush? Fill;

        public Brush? Stroke;

        public double StrokeThickness = 1;

        public double Radius;

        public bool Dashed;

        public bool TextCentered;
    }

    private readonly record struct WorkOrderChipLayout(
        FormattedText Text,
        Brush Fill,
        double Width);

    private readonly record struct WorkOrderInputBoxLayout(
        FormattedText Title,
        ShapePreviewViewModel Preview,
        double Width,
        double Height,
        double X,
        double Y);

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

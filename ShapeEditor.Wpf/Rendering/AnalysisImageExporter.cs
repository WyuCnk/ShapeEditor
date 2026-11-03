using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Rendering;

/// <summary>
/// 把分析窗口的内容导出成一张单栏 PNG。
///
/// 版式与 Web 的「导出分析图」对齐，自上而下依次是：
/// 标题与短代码、竖式图、图形性质、角的合法性、稳定性、
/// 半边稳定性（按轴分组）、制作路径，最后是尺寸与生成时间。
///
/// 全部内容由 DrawingVisual 一次性离屏绘制，
/// 竖式图单元格复用 <see cref="ShapeGridDrawing"/>，与编辑器保持同一套配色。
/// </summary>
public static class AnalysisImageExporter
{
    /// <summary>画布宽度。</summary>
    private const double CanvasWidth = 820;

    /// <summary>四周留白。</summary>
    private const double PagePadding = 36;

    /// <summary>单栏内容宽度。</summary>
    private const double ContentWidth = CanvasWidth - PagePadding * 2;

    /// <summary>顶部主色装饰条高度。</summary>
    private const double TopBarHeight = 6;

    /// <summary>板块标题占用的高度。</summary>
    private const double SectionTitleHeight = 30;

    /// <summary>板块之间的间隔。</summary>
    private const double SectionGap = 22;

    /// <summary>竖式图允许的最大宽度。</summary>
    private const double MaxShapeWidth = 600;

    /// <summary>
    /// 交换方向表格最多画多少行。
    /// 象限数很大时方向数会爆炸，导出图只保留前若干行并给出提示。
    /// </summary>
    private const int MaxDirectionRows = 80;

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

    private static readonly Brush ZebraBrush = CreateBrush(0xFB, 0xFA, 0xFE);

    private static readonly Brush ShapeCardBrush = CreateBrush(0xFA, 0xF8, 0xFF);

    private static readonly Pen BorderPen = CreatePen(BorderBrush, 1);

    private static readonly Pen DividerPen = CreatePen(DividerBrush, 1);

    private static readonly ShortCodeExporter Exporter = new();

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

        DrawingGroup content = BuildContent(
            viewModel,
            document,
            out double height);

        var visual = new DrawingVisual();

        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(
                Brushes.White,
                null,
                new Rect(0, 0, CanvasWidth, height));

            // 顶部主色装饰条，与站点主色 #512BD4 一致。
            context.DrawRectangle(
                PrimaryBrush,
                null,
                new Rect(0, 0, CanvasWidth, TopBarHeight));

            context.DrawDrawing(content);
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(CanvasWidth),
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
        out double height)
    {
        var group = new DrawingGroup();

        // 顶部装饰条之下再留出页边距。
        double y = TopBarHeight + PagePadding;

        using (DrawingContext context = group.Open())
        {
            y = DrawHeader(context, viewModel.Code, y);

            y = DrawShapeBlock(context, document, y);

            y = DrawPropertiesSection(context, viewModel, y);

            y = DrawAnglesSection(context, viewModel, y);

            y = DrawStabilitySection(context, viewModel, y);

            y = DrawHalfSection(context, viewModel, y);

            y = DrawPathSection(context, viewModel, y);

            y = DrawFooter(context, viewModel, y);
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
        double y)
    {
        FormattedText title = CreateText(
            "图形分析",
            28,
            TextBrush,
            bold: true);

        title.MaxTextWidth = ContentWidth;

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

            codeText.MaxTextWidth = ContentWidth;

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
            new Point(PagePadding + ContentWidth, y));

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
            ContentWidth,
            gridWidth + 36);

        double cardHeight = gridHeight + 36;

        double cardLeft = PagePadding + (ContentWidth - cardWidth) / 2;

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
                PagePadding + (ContentWidth - dimension.Width) / 2,
                y));

        return y + dimension.Height + 14;
    }

    #endregion

    #region 各板块

    /// <summary>图形性质：与界面一致的标签流式排布。</summary>
    private static double DrawPropertiesSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double y)
    {
        y = DrawSectionTitle(context, "图形性质", y);

        if (viewModel.PropertyNames.Count == 0)
        {
            y = DrawParagraph(
                context,
                "没有识别到任何性质。",
                y,
                13,
                MutedBrush);
        }
        else
        {
            y = DrawChips(context, viewModel.PropertyNames, y);
        }

        return EndSection(context, y);
    }

    /// <summary>角的合法性：汇总行 + 四列表格。</summary>
    private static double DrawAnglesSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double y)
    {
        y = DrawSectionTitle(context, "角的合法性", y);

        y = DrawParagraph(
            context,
            viewModel.AngleSummary,
            y,
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
                new[] { 90.0, 100.0, 120.0, ContentWidth - 310 },
                y,
                row => alertRows[row]);

            y = DrawParagraph(
                context,
                "角的可制作性暂时等同于合法性，等角的制作算法接入后细化。",
                y,
                11.5,
                MutedBrush);
        }

        return EndSection(context, y);
    }

    /// <summary>稳定性：直接使用分析窗口里的结论文本。</summary>
    private static double DrawStabilitySection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double y)
    {
        y = DrawSectionTitle(context, "稳定性", y);

        y = DrawParagraph(
            context,
            viewModel.StabilityText,
            y,
            13,
            BodyBrush);

        return EndSection(context, y);
    }

    /// <summary>
    /// 半边稳定性：汇总、定义说明，然后按轴分组画交换方向表格。
    /// </summary>
    private static double DrawHalfSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double y)
    {
        y = DrawSectionTitle(context, "半边稳定性", y);

        y = DrawParagraph(
            context,
            viewModel.HalfSummary,
            y,
            13,
            BodyBrush);

        if (!viewModel.HasHalfDirections)
        {
            return EndSection(context, y);
        }

        y = DrawParagraph(
            context,
            "交换方向合法的定义：两个半边都稳定，并且该方向上左右各两个带柱子的半半边也全部稳定。",
            y,
            11.5,
            MutedBrush);

        int drawnRows = 0;

        bool truncated = false;

        foreach (IGrouping<int, ExchangeDirection> group in
                 viewModel.ExchangeDirections
                     .GroupBy(direction => direction.Axis)
                     .OrderBy(group => group.Key))
        {
            if (drawnRows >= MaxDirectionRows)
            {
                truncated = true;

                break;
            }

            var rows = new List<AnalysisTableCell[]>();

            var alertRows = new List<bool>();

            foreach (ExchangeDirection direction in group)
            {
                if (drawnRows >= MaxDirectionRows)
                {
                    truncated = true;

                    break;
                }

                rows.Add(new[]
                {
                    new AnalysisTableCell(
                        direction.Display,
                        BodyBrush,
                        Mono: true,
                        Size: 11),
                    new AnalysisTableCell(
                        direction.IsLegal ? "是" : "否",
                        direction.IsLegal ? SuccessBrush : DangerBrush)
                });

                alertRows.Add(!direction.IsLegal);

                drawnRows++;
            }

            if (rows.Count == 0)
            {
                break;
            }

            y = DrawSubTitle(context, $"轴 {group.Key + 1}", y);

            y = DrawTable(
                context,
                new[] { "交换方向", "合法" },
                rows,
                new[] { ContentWidth - 110.0, 110.0 },
                y,
                row => alertRows[row]);

            if (truncated)
            {
                break;
            }
        }

        if (truncated)
        {
            y = DrawParagraph(
                context,
                $"交换方向较多，导出图只列出前 {MaxDirectionRows} 个。",
                y,
                11.5,
                MutedBrush);
        }

        return EndSection(context, y);
    }

    /// <summary>
    /// 制作路径：可制作时按顺序列出步骤与每步短代码，
    /// 否则只给出不可制作的原因。
    /// </summary>
    private static double DrawPathSection(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double y)
    {
        y = DrawSectionTitle(context, "制作路径", y);

        ProductionPathResult? result = viewModel.GetProductionPath();

        if (result is null)
        {
            y = DrawParagraph(
                context,
                "无法解析。",
                y,
                13,
                MutedBrush);

            return EndSection(context, y);
        }

        if (!result.IsProducible ||
            result.Path is null)
        {
            y = DrawParagraph(
                context,
                "不可制作：" + (result.Error ?? "没有找到可行的制作路径。"),
                y,
                13,
                DangerBrush);

            return EndSection(context, y);
        }

        ProductionPath path = result.Path;

        y = DrawParagraph(
            context,
            $"可制作：方向 {path.Direction}（{(path.DirectionLegal ? "合法方向" : "不合法方向")}）。",
            y,
            13,
            BodyBrush);

        int stepIndex = 1;

        foreach (ProductionStep step in path.Steps)
        {
            FormattedText title = CreateText(
                $"{stepIndex}. {step.Title}",
                12.5,
                BodyBrush,
                bold: true);

            title.MaxTextWidth = ContentWidth - 8;

            context.DrawText(title, new Point(PagePadding + 4, y));

            y += title.Height + 2;

            if (step.Shape is not null)
            {
                string code = Exporter.Export(
                    step.Shape,
                    omitTopEmptyLayers: true);

                FormattedText codeText = CreateText(
                    code,
                    11,
                    PrimaryDarkBrush,
                    mono: true);

                codeText.MaxTextWidth = ContentWidth - 30;

                codeText.MaxLineCount = 3;

                codeText.Trimming = TextTrimming.CharacterEllipsis;

                context.DrawText(codeText, new Point(PagePadding + 26, y));

                y += codeText.Height + 10;
            }
            else
            {
                y += 4;
            }

            stepIndex++;
        }

        return EndSection(context, y);
    }

    #endregion

    #region 底部

    /// <summary>底部分隔线、图形尺寸与生成时间。</summary>
    private static double DrawFooter(
        DrawingContext context,
        AnalysisViewModel viewModel,
        double y)
    {
        y += 4;

        context.DrawLine(
            BorderPen,
            new Point(PagePadding, y),
            new Point(PagePadding + ContentWidth, y));

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
                PagePadding + ContentWidth - generatedText.Width,
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

    /// <summary>子标题，用于按轴分组的表格。</summary>
    private static double DrawSubTitle(
        DrawingContext context,
        string title,
        double y)
    {
        FormattedText text = CreateText(
            title,
            13,
            PrimaryDarkBrush,
            bold: true);

        context.DrawText(text, new Point(PagePadding + 2, y));

        return y + text.Height + 6;
    }

    /// <summary>一段可换行的正文。</summary>
    private static double DrawParagraph(
        DrawingContext context,
        string text,
        double y,
        double size = 13,
        Brush? brush = null,
        double indent = 0)
    {
        FormattedText formatted = CreateText(
            text,
            size,
            brush ?? BodyBrush);

        formatted.MaxTextWidth = Math.Max(1, ContentWidth - indent);

        context.DrawText(
            formatted,
            new Point(PagePadding + indent, y));

        return y + formatted.Height + 4;
    }

    /// <summary>图形性质标签，按内容宽度自动换行。</summary>
    private static double DrawChips(
        DrawingContext context,
        IEnumerable<string> labels,
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
                x + chipWidth > PagePadding + ContentWidth)
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
        double y)
    {
        context.DrawLine(
            DividerPen,
            new Point(PagePadding, y + 2),
            new Point(PagePadding + ContentWidth, y + 2));

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

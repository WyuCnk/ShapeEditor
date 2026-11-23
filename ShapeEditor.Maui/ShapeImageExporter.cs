using SkiaSharp;
using ShapeEditor.Core.Models;
using ShapeEditor.Maui.Cards;
using ShapeEditor.Presentation.Cards;
using ShapeEditor.Presentation.Styling;

namespace ShapeEditor.Maui;

public sealed class ShapeImageExporter
{
    #region 常量

    private const int CellSize = 128;
    private const int GridLineWidth = 2;
    private const int OuterPadding = 24;

    private const int TitleHeight = 54;
    private const int CodeLineHeight = 44;
    private const int BottomPadding = 24;

    private const int MaxImageSide = 16_000;

    #endregion

    #region 公共方法

    /// <summary>
    /// 将当前竖式图导出为 PNG。
    ///
    /// 图形矩阵从最高层绘制到最低层，
    /// 也就是与屏幕上的竖式图顺序一致。
    /// </summary>
    public async Task<string> ExportPng(
        ShapeDocument document,
        string shortCode,
        bool includeCode = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        SKTypeface chineseTypeface = await LoadChineseTypefaceAsync();
        int columns = document.PartCount;
        int rows = document.LayerCount;

        long cellCount =
            (long)rows * columns;

        if (cellCount > 10_000)
        {
            throw new InvalidOperationException(
                "图形过大，暂不支持导出图片。" +
                "请减少层数或象限数后再试。");
        }

        if (columns <= 0 || rows <= 0)
        {
            throw new InvalidOperationException(
                "图形的层数和象限数必须大于 0。");
        }

        int gridWidth;
        int gridHeight;
        int imageWidth;
        int imageHeight;

        try
        {
            gridWidth = checked(
                columns * CellSize +
                (columns + 1) * GridLineWidth);

            gridHeight = checked(
                rows * CellSize +
                (rows + 1) * GridLineWidth);

            imageWidth = checked(
                gridWidth +
                OuterPadding * 2);

            imageHeight = checked(
                gridHeight +
                OuterPadding * 2 +
                TitleHeight +
                (includeCode
                    ? CodeLineHeight
                    : 0) +
                BottomPadding);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(
                "图片尺寸过大，无法生成。");
        }

        if (imageWidth > MaxImageSide ||
            imageHeight > MaxImageSide)
        {
            throw new InvalidOperationException(
                $"图片尺寸过大，当前最大边长为 {MaxImageSide} 像素。");
        }

        using var bitmap = new SKBitmap(
            imageWidth,
            imageHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        using var titlePaint =
    CreateTextPaint(SKColors.Black);

        using var titleFont =
            CreateTextFont(
                chineseTypeface,
                30);

        using var codePaint =
            CreateTextPaint(SKColors.DarkSlateGray);

        using var codeFont =
            CreateTextFont(
                chineseTypeface,
                22);

        float titleX = OuterPadding;
        float titleY = OuterPadding + 34;

        DrawText(
            canvas,
            $"{rows}层 × {columns}象限",
            titleX,
            titleY,
            titleFont,
            titlePaint);

        int gridStartY =
            OuterPadding +
            TitleHeight +
            (includeCode ? CodeLineHeight : 0);

        if (includeCode)
        {
            DrawFittedText(
                canvas,
                shortCode,
                OuterPadding,
                OuterPadding + TitleHeight + 27,
                imageWidth - OuterPadding * 2,
                codeFont,
                codePaint);
        }

        DrawGrid(
            canvas,
            document,
            OuterPadding,
            gridStartY,
            chineseTypeface);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        string fileName =
            $"shape-{DateTime.Now:yyyyMMdd-HHmmss}.png";

        string filePath =
            Path.Combine(
                FileSystem.CacheDirectory,
                fileName);

        using FileStream stream = File.Open(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        data.SaveTo(stream);

        return filePath;
    }

    #endregion

    #region 分析页导出（竖式图 + 工序卡片）

    /// <summary>
    /// 导出分析图：顶部竖式图 + 三层工序卡片（交换 → 半边 → 角）。
    ///
    /// <para>
    /// 卡片直接消费页面已经缓存好的 <see cref="WorkOrderCardPresentation"/>，
    /// 不重新调用 <c>CardBuilder</c>；没有卡片集（例如 n = 2）时保持旧的竖式图导出。
    /// 卡内布局走 <see cref="WorkOrderCardLayout"/>，与页面同一套尺寸常量；
    /// 图形仍复用 <see cref="DrawGrid"/> 的单元格绘制，只是切换成 30px 扁平字母模式。
    /// </para>
    /// </summary>
    public async Task<string> ExportAnalysisPng(
        ShapeDocument document,
        string shortCode,
        WorkOrderCardPresentation? cards,
        bool includeCode = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (cards is null || !cards.HasCards)
        {
            return await ExportPng(
                document,
                shortCode,
                includeCode);
        }

        SKTypeface chineseTypeface = await LoadChineseTypefaceAsync();

        const double contentWidth = WorkOrderCardLayout.ExportContentWidth;

        int columns = document.PartCount;

        int rows = document.LayerCount;

        int gridCell = Math.Clamp(
            (int)Math.Floor(
                Math.Min(
                    480.0 / Math.Max(rows, columns),
                    contentWidth / columns)),
            8,
            48);

        float gridGap = 2;

        float gridWidth =
            columns * (gridCell + gridGap) +
            gridGap;

        float gridHeight =
            rows * (gridCell + gridGap) +
            gridGap;

        // 竖式图可能比卡片区更宽（象限很多时），画布宽度取两者的较大值。
        int imageWidth = (int)Math.Ceiling(
            Math.Max(contentWidth, gridWidth) + OuterPadding * 2);

        // 层宽 = 内容宽；卡片填满层内边距以内，卡内区块由布局器折行。
        double cardWidth =
            contentWidth - WorkOrderCardLayout.LayerPadding * 2;

        double cardContentWidth = Math.Max(
            1,
            cardWidth - WorkOrderCardLayout.CardPadding * 2);

        var layerPlans = new List<AnalysisLayerPlan>(cards.Layers.Count);

        foreach (WorkOrderCardLayer layer in cards.Layers)
        {
            var layouts = layer.Cards
                .Select(card => WorkOrderCardLayout.MeasureCard(
                    card,
                    cardContentWidth))
                .ToArray();

            double cardsHeight = 0;

            for (int index = 0; index < layouts.Length; index++)
            {
                cardsHeight += layouts[index].Height;

                if (index < layouts.Length - 1)
                {
                    cardsHeight += WorkOrderCardLayout.CardGap;
                }
            }

            double layerHeight =
                WorkOrderCardLayout.LayerPadding * 2 +
                24 +
                (layouts.Length > 0 ? 8 : 0) +
                cardsHeight;

            layerPlans.Add(new AnalysisLayerPlan(
                layer,
                layouts,
                (float)layerHeight));
        }

        using var titleFont = CreateTextFont(chineseTypeface, 30);

        using var codeFont = CreateTextFont(chineseTypeface, 22);

        using var sectionFont = CreateTextFont(chineseTypeface, 24);

        using var bodyFont = CreateTextFont(chineseTypeface, 13);

        using var smallFont = CreateTextFont(chineseTypeface, 11.5f);

        using var measurePaint = CreateTextPaint(SKColors.Black);

        using var titlePaint = CreateTextPaint(SKColors.Black);

        using var codePaint = CreateTextPaint(SKColors.DarkSlateGray);

        using var sectionPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.SectionTitleText));

        using var bodyPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.BodyText));

        using var mutedPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.MutedText));

        string summary = WorkOrderCardViewFactory.BuildSummary(cards);

        List<string> summaryLines = WrapLines(
            summary,
            (float)contentWidth,
            bodyFont,
            measurePaint);

        List<string> noteLines = string.IsNullOrWhiteSpace(cards.Note)
            ? new List<string>()
            : WrapLines(
                cards.Note,
                (float)contentWidth,
                smallFont,
                measurePaint);

        const string explanation =
            "按「交换工序 → 半边工序 → 角工序」平铺，同一层内卡片纵向排列；" +
            "每张卡内部按「输入 → 前态 → 可见步骤 → 输出」横向流式排列，窄屏时自动换行。" +
            "前态画在输入之后、可见步骤之前；未验证的卡不画输入链，只显示 Note。";

        List<string> explanationLines = WrapLines(
            explanation,
            (float)contentWidth,
            smallFont,
            measurePaint);

        // 先算好总高度，再创建位图；避免画到一半再扩容。
        float totalHeight = OuterPadding;

        totalHeight += TitleHeight;

        if (includeCode)
        {
            totalHeight += CodeLineHeight;
        }

        totalHeight += gridHeight + 24;

        totalHeight += 30;

        totalHeight += summaryLines.Count * 20 + 6;

        totalHeight += noteLines.Count * 16 + 6;

        totalHeight += explanationLines.Count * 16 + 10;

        foreach (AnalysisLayerPlan plan in layerPlans)
        {
            totalHeight += plan.Height + (float)WorkOrderCardLayout.LayerGap;
        }

        totalHeight += OuterPadding;

        int imageHeight = (int)Math.Ceiling(totalHeight);

        if (imageWidth > MaxImageSide ||
            imageHeight > MaxImageSide)
        {
            throw new InvalidOperationException(
                $"图片尺寸过大，当前最大边长为 {MaxImageSide} 像素。");
        }

        using var bitmap = new SKBitmap(
            imageWidth,
            imageHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);

        float y = OuterPadding;

        DrawTextTop(
            canvas,
            $"{rows}层 × {columns}象限",
            OuterPadding,
            y,
            titleFont,
            titlePaint);

        y += TitleHeight;

        if (includeCode)
        {
            DrawTextTop(
                canvas,
                FitText(
                    shortCode,
                    (float)contentWidth,
                    codeFont,
                    codePaint),
                OuterPadding,
                y,
                codeFont,
                codePaint);

            y += CodeLineHeight;
        }

        DrawGrid(
            canvas,
            document,
            OuterPadding,
            y,
            chineseTypeface,
            gridCell,
            gridGap);

        y += gridHeight + 24;

        DrawTextTop(
            canvas,
            "工序卡片",
            OuterPadding,
            y,
            sectionFont,
            sectionPaint);

        y += 30;

        y = DrawLines(
            canvas,
            summaryLines,
            OuterPadding,
            y,
            20,
            bodyFont,
            bodyPaint);

        y += 6;

        if (noteLines.Count > 0)
        {
            y = DrawLines(
                canvas,
                noteLines,
                OuterPadding,
                y,
                16,
                smallFont,
                mutedPaint);

            y += 6;
        }

        y = DrawLines(
            canvas,
            explanationLines,
            OuterPadding,
            y,
            16,
            smallFont,
            mutedPaint);

        y += 10;

        foreach (AnalysisLayerPlan plan in layerPlans)
        {
            DrawAnalysisLayer(
                canvas,
                chineseTypeface,
                plan,
                OuterPadding,
                y,
                (float)contentWidth);

            y += plan.Height + (float)WorkOrderCardLayout.LayerGap;
        }

        using var image = SKImage.FromBitmap(bitmap);

        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        string fileName =
            $"analysis-{DateTime.Now:yyyyMMdd-HHmmss}.png";

        string filePath =
            Path.Combine(
                FileSystem.CacheDirectory,
                fileName);

        using FileStream stream = File.Open(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        data.SaveTo(stream);

        return filePath;
    }

    private static void DrawAnalysisLayer(
        SKCanvas canvas,
        SKTypeface typeface,
        AnalysisLayerPlan plan,
        float x,
        float y,
        float width)
    {
        float height = plan.Height;

        FillRoundedRectangle(
            canvas,
            new SKRect(x, y, x + width, y + height),
            10,
            ToSKColor(WorkOrderCardColors.LayerBackground));

        StrokeRoundedRectangle(
            canvas,
            new SKRect(x, y, x + width, y + height),
            10,
            1,
            ToSKColor(WorkOrderCardColors.LayerStroke));

        FillRoundedRectangle(
            canvas,
            new SKRect(x, y + 10, x + 3, y + height - 10),
            1.5f,
            ToSKColor(WorkOrderCardColors.LayerAccent(plan.Layer.Kind)));

        using var titleFont = CreateTextFont(typeface, 14);

        using var countFont = CreateTextFont(typeface, 11);

        using var titlePaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.TitleText));

        using var countPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.MutedText));

        float left = x + (float)WorkOrderCardLayout.LayerPadding;

        float top = y + (float)WorkOrderCardLayout.LayerPadding;

        DrawTextTop(
            canvas,
            plan.Layer.Title,
            left,
            top,
            titleFont,
            titlePaint);

        float titleWidth = titleFont.MeasureText(
            plan.Layer.Title,
            titlePaint);

        DrawTextTop(
            canvas,
            $"{plan.Cards.Length} 张卡",
            left + titleWidth + 8,
            top + 4,
            countFont,
            countPaint);

        float cardTop =
            top + 24 + 8;

        for (int index = 0; index < plan.Cards.Length; index++)
        {
            DrawAnalysisCard(
                canvas,
                typeface,
                plan.Cards[index],
                left,
                cardTop);

            cardTop += (float)plan.Cards[index].Height;

            if (index < plan.Cards.Length - 1)
            {
                cardTop += (float)WorkOrderCardLayout.CardGap;
            }
        }
    }

    private static void DrawAnalysisCard(
        SKCanvas canvas,
        SKTypeface typeface,
        WorkOrderCardLayoutResult layout,
        float x,
        float y)
    {
        WorkOrderCardItem card = layout.Card;

        canvas.Save();

        canvas.Translate(x, y);

        FillRoundedRectangle(
            canvas,
            new SKRect(0, 0, (float)layout.Width, (float)layout.Height),
            10,
            ToSKColor(
                card.Verified
                    ? WorkOrderCardColors.CardBackground
                    : WorkOrderCardColors.CardUnverifiedBackground));

        StrokeRoundedRectangle(
            canvas,
            new SKRect(0, 0, (float)layout.Width, (float)layout.Height),
            10,
            1,
            ToSKColor(
                card.Verified
                    ? WorkOrderCardColors.CardStroke
                    : WorkOrderCardColors.CardUnverifiedStroke));

        using var titleFont = CreateTextFont(typeface, 14);

        using var chipFont = CreateTextFont(typeface, 11);

        using var sectionFont = CreateTextFont(typeface, 12);

        using var inputTitleFont = CreateTextFont(typeface, 11);

        using var noteFont = CreateTextFont(typeface, 12);

        using var smallFont = CreateTextFont(typeface, 11);

        using var stepFont = CreateTextFont(typeface, 11);

        using var arrowFont = CreateTextFont(typeface, 16);

        using var titlePaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.TitleText));

        using var sectionPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.SectionTitleText));

        using var preStateTitlePaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.PreStateTitleText));

        using var mutedPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.MutedText));

        using var bodyPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.BodyText));

        using var stepPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.StepHeaderText));

        using var arrowPaint = CreateTextPaint(
            ToSKColor(WorkOrderCardColors.Accent));

        DrawTextTop(
            canvas,
            FitText(
                card.Title,
                (float)layout.ContentWidth,
                titleFont,
                titlePaint),
            (float)WorkOrderCardLayout.CardPadding,
            (float)WorkOrderCardLayout.CardPadding,
            titleFont,
            titlePaint);

        foreach (WorkOrderCardChipLayout chip in layout.Chips)
        {
            FillRoundedRectangle(
                canvas,
                new SKRect(
                    (float)chip.X,
                    (float)chip.Y,
                    (float)(chip.X + chip.Width),
                    (float)(chip.Y + chip.Height)),
                9,
                ToSKColor(chip.Fill));

            using var chipPaint = CreateTextPaint(
                ToSKColor(chip.TextColor));

            DrawCenteredText(
                canvas,
                FitText(
                    chip.Text,
                    (float)chip.Width - 6,
                    chipFont,
                    chipPaint),
                new SKRect(
                    (float)chip.X,
                    (float)chip.Y,
                    (float)(chip.X + chip.Width),
                    (float)(chip.Y + chip.Height)),
                chipFont,
                chipPaint);
        }

        if (layout.LegacyPanel is WorkOrderCardTextPanelLayout legacy)
        {
            DrawTextPanel(canvas, typeface, noteFont, legacy);
        }

        if (layout.WarningPanel is WorkOrderCardTextPanelLayout warning)
        {
            DrawTextPanel(canvas, typeface, noteFont, warning);
        }

        int stepIndex = 0;

        foreach (WorkOrderCardBlockLayout block in layout.Blocks)
        {
            switch (block.Kind)
            {
                case "inputs":
                    DrawTextTop(
                        canvas,
                        block.Title,
                        (float)block.X,
                        (float)block.Y,
                        sectionFont,
                        sectionPaint);

                    for (int index = 0;
                         index < block.Items.Count &&
                         index < card.Inputs.Count;
                         index++)
                    {
                        WorkOrderCardItemLayout item = block.Items[index];

                        WorkOrderShapeBoxLayout? box = item.ShapeBox;

                        if (box is null)
                        {
                            continue;
                        }

                        FillRoundedRectangle(
                            canvas,
                            ToRect(box),
                            8,
                            ToSKColor(WorkOrderCardColors.InputFill));

                        StrokeRoundedRectangle(
                            canvas,
                            ToRect(box),
                            8,
                            1,
                            ToSKColor(WorkOrderCardColors.InputStroke));

                        DrawTextTop(
                            canvas,
                            FitText(
                                item.Label,
                                (float)(box.Width - WorkOrderCardLayout.InputPadding * 2),
                                inputTitleFont,
                                mutedPaint),
                            (float)(box.X + WorkOrderCardLayout.InputPadding),
                            (float)(box.Y + WorkOrderCardLayout.InputPadding),
                            inputTitleFont,
                            mutedPaint);

                        DrawCardShape(
                            canvas,
                            card.Inputs[index].Shape,
                            (float)box.ImageX,
                            (float)box.ImageY,
                            typeface);
                    }

                    break;

                case "prestate":
                    DrawTextTop(
                        canvas,
                        block.Title,
                        (float)block.X,
                        (float)block.Y,
                        sectionFont,
                        preStateTitlePaint);

                    if (block.ShapeBox is WorkOrderShapeBoxLayout preStateBox &&
                        card.PreState is ShapeDocument preState)
                    {
                        FillRoundedRectangle(
                            canvas,
                            ToRect(preStateBox),
                            8,
                            ToSKColor(WorkOrderCardColors.PreStateFill));

                        StrokeRoundedRectangle(
                            canvas,
                            ToRect(preStateBox),
                            8,
                            1,
                            ToSKColor(WorkOrderCardColors.PreStateStroke),
                            dashed: true);

                        DrawCardShape(
                            canvas,
                            preState,
                            (float)preStateBox.ImageX,
                            (float)preStateBox.ImageY,
                            typeface);
                    }

                    break;

                case "steps":
                    DrawTextTop(
                        canvas,
                        block.Title,
                        (float)block.X,
                        (float)block.Y,
                        sectionFont,
                        sectionPaint);

                    foreach (WorkOrderCardItemLayout item in block.Items)
                    {
                        if (item.IsArrow)
                        {
                            DrawCenteredText(
                                canvas,
                                "→",
                                new SKRect(
                                    (float)item.X,
                                    (float)item.Y,
                                    (float)(item.X + item.Width),
                                    (float)(item.Y + item.Height)),
                                arrowFont,
                                arrowPaint);

                            continue;
                        }

                        if (stepIndex >= card.Steps.Count ||
                            item.ShapeBox is not WorkOrderShapeBoxLayout stepBox)
                        {
                            continue;
                        }

                        FillRoundedRectangle(
                            canvas,
                            new SKRect(
                                (float)item.X,
                                (float)item.Y,
                                (float)(item.X + item.Width),
                                (float)(item.Y + item.Height)),
                            10,
                            ToSKColor(item.StepFill));

                        StrokeRoundedRectangle(
                            canvas,
                            new SKRect(
                                (float)item.X,
                                (float)item.Y,
                                (float)(item.X + item.Width),
                                (float)(item.Y + item.Height)),
                            10,
                            1.5f,
                            ToSKColor(item.StepAccent));

                        DrawTextTop(
                            canvas,
                            FitText(
                                item.Label,
                                (float)(item.Width - 14),
                                stepFont,
                                stepPaint),
                            (float)(item.X + 7),
                            (float)(item.Y + 7),
                            stepFont,
                            stepPaint);

                        DrawCardShape(
                            canvas,
                            card.Steps[stepIndex].Shape,
                            (float)stepBox.ImageX,
                            (float)stepBox.ImageY,
                            typeface);

                        stepIndex++;
                    }

                    break;

                case "output":
                    DrawTextTop(
                        canvas,
                        block.Title,
                        (float)block.X,
                        (float)block.Y,
                        sectionFont,
                        sectionPaint);

                    if (!string.IsNullOrWhiteSpace(block.Caption))
                    {
                        DrawTextTop(
                            canvas,
                            FitText(
                                block.Caption,
                                (float)(block.Width - block.TitleWidth - 6),
                                smallFont,
                                mutedPaint),
                            (float)(block.X + block.TitleWidth + 6),
                            (float)(block.Y + 2),
                            smallFont,
                            mutedPaint);
                    }

                    if (block.ShapeBox is WorkOrderShapeBoxLayout outputBox)
                    {
                        FillRoundedRectangle(
                            canvas,
                            ToRect(outputBox),
                            10,
                            ToSKColor(WorkOrderCardColors.OutputFill));

                        StrokeRoundedRectangle(
                            canvas,
                            ToRect(outputBox),
                            10,
                            2,
                            ToSKColor(WorkOrderCardColors.OutputStroke));

                        DrawCardShape(
                            canvas,
                            card.Output,
                            (float)outputBox.ImageX,
                            (float)outputBox.ImageY,
                            typeface);
                    }

                    break;

                case "empty-steps":
                case "corner-caption":
                    DrawLines(
                        canvas,
                        block.TextLines,
                        (float)block.X,
                        (float)block.Y,
                        16,
                        smallFont,
                        mutedPaint);

                    break;
            }
        }

        canvas.Restore();
    }

    private static void DrawTextPanel(
        SKCanvas canvas,
        SKTypeface typeface,
        SKFont noteFont,
        WorkOrderCardTextPanelLayout panel)
    {
        FillRoundedRectangle(
            canvas,
            new SKRect(
                (float)panel.X,
                (float)panel.Y,
                (float)(panel.X + panel.Width),
                (float)(panel.Y + panel.Height)),
            6,
            ToSKColor(panel.Fill));

        FillRoundedRectangle(
            canvas,
            new SKRect(
                (float)panel.X,
                (float)panel.Y,
                (float)(panel.X + panel.BarWidth),
                (float)(panel.Y + panel.Height)),
            2,
            ToSKColor(panel.Stroke));

        using var titleFont = CreateTextFont(typeface, 12);

        using var titlePaint = CreateTextPaint(
            ToSKColor(panel.TextColor));

        using var notePaint = CreateTextPaint(
            ToSKColor(panel.NoteColor));

        if (!string.IsNullOrWhiteSpace(panel.Title))
        {
            DrawTextTop(
                canvas,
                panel.Title,
                (float)(panel.X + panel.BarWidth + panel.Padding),
                (float)(panel.Y + panel.Padding),
                titleFont,
                titlePaint);
        }

        DrawLines(
            canvas,
            panel.NoteLines,
            (float)(panel.X + panel.BarWidth + panel.Padding),
            (float)panel.NoteTop,
            (float)WorkOrderCardLayout.NoteLineHeight,
            noteFont,
            notePaint);
    }

    /// <summary>
    /// 卡片里的 30px 竖式图：白色底 + 扁平色块 + c / S / P 字母 + 浅色格线。
    /// 复用 <see cref="DrawGrid"/>，不另写一套单元格绘制。
    /// </summary>
    private static void DrawCardShape(
        SKCanvas canvas,
        ShapeDocument shape,
        float x,
        float y,
        SKTypeface typeface)
    {
        float width = shape.PartCount * CardMetrics.CellSize;

        float height = shape.LayerCount * CardMetrics.CellSize;

        using var background = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            new SKRect(x, y, x + width, y + height),
            background);

        DrawGrid(
            canvas,
            shape,
            x,
            y,
            typeface,
            CardMetrics.CellSize,
            0,
            shortCodeGlyphs: true,
            flatColors: true,
            gridColor: WorkOrderCardColors.GridLine,
            gridLineWidth: 0.6f);
    }

    private static SKRect ToRect(
        WorkOrderShapeBoxLayout box) =>
        new(
            (float)box.X,
            (float)box.Y,
            (float)(box.X + box.Width),
            (float)(box.Y + box.Height));

    private static void FillRoundedRectangle(
        SKCanvas canvas,
        SKRect rect,
        float radius,
        SKColor color)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        canvas.DrawRoundRect(
            rect,
            radius,
            radius,
            paint);
    }

    private static void StrokeRoundedRectangle(
        SKCanvas canvas,
        SKRect rect,
        float radius,
        float strokeWidth,
        SKColor color,
        bool dashed = false)
    {
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };

        using SKPathEffect? dashEffect = dashed
            ? SKPathEffect.CreateDash(new[] { 4f, 3f }, 0)
            : null;

        if (dashEffect is not null)
        {
            paint.PathEffect = dashEffect;
        }

        canvas.DrawRoundRect(
            rect,
            radius,
            radius,
            paint);
    }

    private static void DrawTextTop(
        SKCanvas canvas,
        string? text,
        float left,
        float top,
        SKFont font,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SKFontMetrics metrics = font.Metrics;

        DrawText(
            canvas,
            text,
            left,
            top - metrics.Ascent,
            font,
            paint);
    }

    private static void DrawCenteredText(
        SKCanvas canvas,
        string text,
        SKRect rect,
        SKFont font,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float textWidth = font.MeasureText(text, paint);

        SKFontMetrics metrics = font.Metrics;

        DrawText(
            canvas,
            text,
            rect.MidX - textWidth / 2f,
            rect.MidY - (metrics.Ascent + metrics.Descent) / 2f,
            font,
            paint);
    }

    private static float DrawLines(
        SKCanvas canvas,
        IReadOnlyList<string> lines,
        float left,
        float top,
        float lineHeight,
        SKFont font,
        SKPaint paint)
    {
        foreach (string line in lines)
        {
            DrawTextTop(
                canvas,
                line,
                left,
                top,
                font,
                paint);

            top += lineHeight;
        }

        return top;
    }

    /// <summary>按实际字体度量折行；显式换行符也保留。</summary>
    private static List<string> WrapLines(
        string? text,
        float maxWidth,
        SKFont font,
        SKPaint paint)
    {
        var lines = new List<string>();

        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var current = new System.Text.StringBuilder();

        float currentWidth = 0;

        foreach (char character in text)
        {
            if (character == '\n')
            {
                lines.Add(current.ToString());

                current.Clear();

                currentWidth = 0;

                continue;
            }

            string candidate = character.ToString();

            float characterWidth = font.MeasureText(
                candidate,
                paint);

            if (current.Length > 0 &&
                currentWidth + characterWidth > maxWidth)
            {
                lines.Add(current.ToString());

                current.Clear();

                currentWidth = 0;
            }

            current.Append(character);

            currentWidth += characterWidth;
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>单行省略：超出最大宽度时截断并加「…」。</summary>
    private static string FitText(
        string? text,
        float maxWidth,
        SKFont font,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text) ||
            maxWidth <= 0 ||
            font.MeasureText(text, paint) <= maxWidth)
        {
            return text ?? "";
        }

        var builder = new System.Text.StringBuilder();

        foreach (char character in text)
        {
            string candidate = builder.ToString() + character;

            if (builder.Length > 0 &&
                font.MeasureText(candidate, paint) > maxWidth)
            {
                break;
            }

            builder.Append(character);
        }

        builder.Append('…');

        return builder.ToString();
    }

    private sealed record AnalysisLayerPlan(
        WorkOrderCardLayer Layer,
        WorkOrderCardLayoutResult[] Cards,
        float Height);

    #endregion

    #region 矩阵绘制

    private static void DrawGrid(
        SKCanvas canvas,
        ShapeDocument document,
        float startX,
        float startY,
        SKTypeface typeface,
        int cellSize = CellSize,
        float cellGap = GridLineWidth,
        bool shortCodeGlyphs = false,
        bool flatColors = false,
        uint gridColor = 0,
        float gridLineWidth = -1f)
    {
        int rows = document.LayerCount;
        int columns = document.PartCount;

        float pitch = cellSize + cellGap;

        for (int visualRow = 0;
             visualRow < rows;
             visualRow++)
        {
            int layer =
                rows - 1 - visualRow;

            for (int part = 0;
                 part < columns;
                 part++)
            {
                float x =
                    startX +
                    cellGap +
                    part * pitch;

                float y =
                    startY +
                    cellGap +
                    visualRow * pitch;

                var cellRect = new SKRect(
                    x,
                    y,
                    x + cellSize,
                    y + cellSize);

                DrawCell(
                    canvas,
                    document[layer, part],
                    cellRect,
                    typeface,
                    shortCodeGlyphs,
                    flatColors);
            }
        }

        DrawGridLines(
            canvas,
            startX,
            startY,
            rows,
            columns,
            cellSize,
            cellGap,
            gridColor,
            gridLineWidth);
    }

    private static void DrawGridLines(
        SKCanvas canvas,
        float startX,
        float startY,
        int rows,
        int columns,
        int cellSize,
        float cellGap,
        uint gridColor,
        float gridLineWidth)
    {
        float pitch = cellSize + cellGap;

        float totalWidth =
            columns * pitch +
            cellGap;

        float totalHeight =
            rows * pitch +
            cellGap;

        float strokeWidth = gridLineWidth > 0
            ? gridLineWidth
            : cellGap;

        if (strokeWidth <= 0)
        {
            strokeWidth = 1;
        }

        using var paint = new SKPaint
        {
            Color = gridColor == 0
                ? SKColors.Gray
                : ToSKColor(gridColor),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = gridLineWidth > 0 && gridLineWidth < 1.5f
        };

        for (int column = 0;
             column <= columns;
             column++)
        {
            float x =
                startX +
                cellGap / 2f +
                column * pitch;

            canvas.DrawLine(
                x,
                startY,
                x,
                startY + totalHeight,
                paint);
        }

        for (int row = 0;
             row <= rows;
             row++)
        {
            float y =
                startY +
                cellGap / 2f +
                row * pitch;

            canvas.DrawLine(
                startX,
                y,
                startX + totalWidth,
                y,
                paint);
        }
    }

    #endregion

    #region 单元格绘制

    private static void DrawCell(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect,
        SKTypeface typeface,
        bool shortCodeGlyphs = false,
        bool flatColors = false)
    {
        if (flatColors)
        {
            DrawFlatCardCell(
                canvas,
                cell,
                rect,
                typeface);

            return;
        }

        switch (cell.Type)
        {
            case CellType.Empty:
                DrawEmptyCell(
                    canvas,
                    rect);

                break;

            case CellType.Pin:
                DrawPinCell(
                    canvas,
                    rect);

                break;

            case CellType.Crystal:
                DrawTextCell(
                    canvas,
                    cell,
                    rect,
                    shortCodeGlyphs
                        ? LetterOf(cell)
                        : CellGlyph.CrystalText,
                    typeface);

                break;

            case CellType.Shape:
                DrawTextCell(
                    canvas,
                    cell,
                    rect,
                    shortCodeGlyphs
                        ? LetterOf(cell)
                        : CellGlyph.ShapeText,
                    typeface);

                break;
        }
    }

    /// <summary>
    /// 工序卡片里的 30px 单元格：与 <see cref="WorkspaceCellDrawable"/> 一致，
    /// 使用扁平底色 + c / S / P 字母，不画晶体条带与内边框；格线由
    /// <see cref="DrawGridLines"/> 统一补上。
    /// </summary>
    private static void DrawFlatCardCell(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect,
        SKTypeface typeface)
    {
        if (cell.Type == CellType.Empty)
        {
            DrawEmptyCell(
                canvas,
                rect);

            return;
        }

        float inset = Math.Max(
            0.5f,
            rect.Width * 0.033f);

        using var fillPaint = new SKPaint
        {
            Color = cell.Type == CellType.Pin
                ? new SKColor(0x9E, 0x9E, 0x9E)
                : GetColor(cell.ColorCode ?? 'u'),
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            new SKRect(
                rect.Left + inset,
                rect.Top + inset,
                rect.Right - inset,
                rect.Bottom - inset),
            fillPaint);

        string glyph = LetterOf(cell);

        if (glyph.Length == 0)
        {
            return;
        }

        using var textPaint =
            CreateTextPaint(SKColors.White);

        using var textFont =
            CreateTextFont(
                typeface,
                Math.Clamp(rect.Width * 0.53f, 4f, 16f));

        float textWidth =
            textFont.MeasureText(
                glyph,
                textPaint);

        SKFontMetrics metrics =
            textFont.Metrics;

        DrawText(
            canvas,
            glyph,
            rect.MidX - textWidth / 2f,
            rect.MidY - (metrics.Ascent + metrics.Descent) / 2f,
            textFont,
            textPaint);
    }

    /// <summary>短代码字形：c 晶体 / S 形状 / P 顶针。</summary>
    private static string LetterOf(
        ShapeCell cell) =>
        cell.Type switch
        {
            CellType.Crystal => "c",
            CellType.Pin => "P",
            CellType.Shape => "S",
            _ => ""
        };

    private static void DrawEmptyCell(
        SKCanvas canvas,
        SKRect rect)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            rect,
            paint);
    }

    /// <summary>
    /// 顶针：白色 | 黑色 | 白色。
    /// </summary>
    private static void DrawPinCell(
        SKCanvas canvas,
        SKRect rect)
    {
        using var whitePaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            rect,
            whitePaint);

        float thirdWidth =
            rect.Width / 3f;

        var middleRect = new SKRect(
            rect.Left + thirdWidth,
            rect.Top,
            rect.Left + thirdWidth * 2f,
            rect.Bottom);

        using var blackPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        canvas.DrawRect(
            middleRect,
            blackPaint);
    }

    private static void DrawTextCell(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect,
        string text,
        SKTypeface typeface)
    {
        char colorCode =
            cell.ColorCode ?? 'u';

        canvas.Save();

        /*
         * 晶体使用缓存的 7 段色带；
         * 普通形状使用纯色背景。
         */
        if (cell.Type == CellType.Crystal)
        {
            DrawCrystalBands(
                canvas,
                cell,
                rect);
        }
        else
        {
            using var backgroundPaint = new SKPaint
            {
                Color = GetColor(colorCode),
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };

            canvas.DrawRect(
                rect,
                backgroundPaint);
        }

        /*
         * 普通形状和晶体统一使用浅色边框，
         * 与当前 ShapeGridDrawable 保持一致。
         */
        DrawCrystalBorder(
            canvas,
            rect,
            colorCode);

        SKColor textColor =
            ShapePalette.NeedsLightText(colorCode)
                ? SKColors.White
                : SKColors.Black;

        using var textPaint =
            CreateTextPaint(textColor);

        using var textFont =
            CreateTextFont(
                typeface,
                Math.Clamp(rect.Width * 0.406f, 8f, 52f));

        float textWidth =
            textFont.MeasureText(
                text,
                textPaint);

        SKFontMetrics metrics =
            textFont.Metrics;

        float textX =
            rect.MidX -
            textWidth / 2f;

        float textY =
            rect.MidY -
            (metrics.Ascent + metrics.Descent) / 2f;

        DrawText(
            canvas,
            text,
            textX,
            textY,
            textFont,
            textPaint);

        canvas.Restore();
    }

    private static void DrawCrystalBands(
        SKCanvas canvas,
        ShapeCell cell,
        SKRect rect)
    {
        char colorCode =
            cell.ColorCode ?? 'r';

        if (!CrystalGradientCache.TryGetValue(
                colorCode,
                out SKColor[]? colors))
        {
            colors =
                CrystalGradientCache['r'];
        }

        canvas.Save();

        canvas.ClipRect(rect);

        float bandWidth =
            rect.Width / colors.Length;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false
        };

        for (int index = 0;
             index < colors.Length;
             index++)
        {
            float x =
                rect.Left +
                index * bandWidth;

            float width =
                index == colors.Length - 1
                    ? rect.Right - x
                    : bandWidth;

            paint.Color =
                colors[index];

            canvas.DrawRect(
                new SKRect(
                    x,
                    rect.Top,
                    x + width,
                    rect.Bottom),
                paint);
        }

        canvas.Restore();
    }

    private static void DrawCrystalBorder(
        SKCanvas canvas,
        SKRect rect,
        char colorCode)
    {
        SKColor borderColor =
            colorCode == 'w'
                ? SKColors.White
                : new SKColor(
                    255,
                    255,
                    255,
                    184);

        float inset =
            Math.Max(
                1f,
                rect.Width * 0.035f);

        using var borderPaint = new SKPaint
        {
            Color = borderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(
                1f,
                rect.Width * 0.025f),
            IsAntialias = true
        };

        canvas.DrawRect(
            new SKRect(
                rect.Left + inset,
                rect.Top + inset,
                rect.Right - inset,
                rect.Bottom - inset),
            borderPaint);
    }

    #endregion

    #region 画笔和颜色

    private static async Task<SKTypeface>
    LoadChineseTypefaceAsync()
    {
        await using Stream stream =
            await FileSystem.OpenAppPackageFileAsync(
                "NotoSansSC-Regular.ttf");

        SKTypeface? typeface =
            SKTypeface.FromStream(stream);

        return typeface
            ?? throw new InvalidOperationException(
                "无法加载中文字体。");
    }

    /// <summary>
    /// 每种颜色对应一组 7 段晶体色带。
    ///
    /// 顺序：
    /// 暗色 → 基础色 → 过渡色 → 高光
    ///       → 过渡色 → 基础色 → 暗色
    /// </summary>
    private static readonly Dictionary<
        char,
        SKColor[]> CrystalGradientCache =
        CreateCrystalGradientCache();

    private static Dictionary<
        char,
        SKColor[]> CreateCrystalGradientCache()
    {
        char[] colorCodes =
        {
        'r',
        'g',
        'b',
        'c',
        'm',
        'y',
        'w',
        'k',
        'u'
    };

        var result =
            new Dictionary<
                char,
                SKColor[]>();

        foreach (char colorCode in colorCodes)
        {
            result[colorCode] =
                CreateCrystalGradientColors(
                    colorCode);
        }

        return result;
    }

    private static SKColor[] CreateCrystalGradientColors(
        char colorCode)
    {
        uint[] bands = ShapePalette.CrystalBands(colorCode);

        var colors = new SKColor[bands.Length];

        for (int index = 0; index < bands.Length; index++)
        {
            colors[index] = ToSKColor(bands[index]);
        }

        return colors;
    }

    private static SKColor ToSKColor(
        uint argb)
    {
        return new SKColor(
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb,
            (byte)(argb >> 24));
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        SKFont font,
        SKPaint paint)
    {
        using SKTextBlob? blob =
            SKTextBlob.Create(
                text,
                font);

        if (blob is not null)
        {
            canvas.DrawText(
                blob,
                x,
                y,
                paint);
        }
    }

    private async static void DrawFittedText(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        float maxWidth,
        SKFont font,
        SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SKTypeface chineseTypeface = await LoadChineseTypefaceAsync();

        float textWidth =
            font.MeasureText(
                text,
                paint);

        if (textWidth <= maxWidth)
        {
            DrawText(
                canvas,
                text,
                x,
                baseline,
                font,
                paint);

            return;
        }

        float newSize =
            font.Size;

        while (newSize > 10)
        {
            newSize -= 1;

            using var smallerFont =
                CreateTextFont(
                    chineseTypeface,
                    newSize);

            if (smallerFont.MeasureText(
                    text,
                    paint) <= maxWidth)
            {
                DrawText(
                    canvas,
                    text,
                    x,
                    baseline,
                    smallerFont,
                    paint);

                return;
            }
        }

        using var minimumFont =
            CreateTextFont(
                chineseTypeface,
                10);

        DrawText(
            canvas,
            text,
            x,
            baseline,
            minimumFont,
            paint);
    }

    private static SKPaint CreateTextPaint(
        SKColor color)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true
        };
    }

    private static SKFont CreateTextFont(
    SKTypeface typeface,
    float textSize)
    {
        return new SKFont(
            typeface,
            textSize);
    }

    private static SKColor GetColor(
        char code)
    {
        return ToSKColor(ShapePalette.Argb(code));
    }

    #endregion
}

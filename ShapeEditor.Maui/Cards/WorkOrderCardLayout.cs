using ShapeEditor.Core.Models;
using ShapeEditor.Presentation.Cards;
using ShapeEditor.Presentation.Styling;

namespace ShapeEditor.Maui.Cards;

/// <summary>
/// 三层工序卡片的尺寸计算。
///
/// <para>
/// 页面用 MAUI 控件按同样的常量排版；导出图没有控件可测量，
/// 由这里按「文字估算宽度 + 30px 单元格图形」算出每张卡的标题、警示块、
/// 卡内区块（输入 / 前态 / 可见步骤 / 输出 / 角卡小字）的位置，
/// 再交给 SkiaSharp 逐块绘制。探针也直接调用这里，验证窄屏换行与自然宽度。
/// </para>
///
/// <para>
/// 这里不引用 MAUI / Skia，只依赖 Core 的 <see cref="ShapeDocument"/> 与
/// Presentation 的 <see cref="CardMetrics"/>，因此可以被临时控制台探针链接编译。
/// 文字宽度按「CJK 占满字号、ASCII 占 0.56 字号」估算，与页面实际字体度量
/// 可能有几像素差异，因此每个文字块都留了内边距，导出不会压到图形。
/// </para>
/// </summary>
public static class WorkOrderCardLayout
{
    /// <summary>卡片左右 / 上下内边距。</summary>
    public const double CardPadding = 12;

    /// <summary>卡内区块之间的水平间距。</summary>
    public const double BlockGap = 12;

    /// <summary>卡内区块换行后的行间距。</summary>
    public const double BlockLineGap = 8;

    /// <summary>标题行小标签高度（种类 / Mode / 验证）。</summary>
    public const double ChipHeight = 18;

    /// <summary>标题行小标签间距。</summary>
    public const double ChipGap = 6;

    /// <summary>小标签左右内边距合计。</summary>
    public const double ChipHorizontalPadding = 14;

    /// <summary>小节标题字号与行高。</summary>
    public const double SectionTitleFontSize = 12;

    public const double SectionTitleHeight = 18;

    /// <summary>小节标题与图形之间的间距。</summary>
    public const double SectionTitleGap = 6;

    /// <summary>步骤节点之间的箭头宽度。</summary>
    public const double StepArrowWidth = 22;

    /// <summary>输入框之间的水平间距。</summary>
    public const double InputGap = 12;

    /// <summary>输入框标题字号与行高。</summary>
    public const double InputTitleFontSize = 11;

    public const double InputTitleHeight = 16;

    /// <summary>输入框内边距。</summary>
    public const double InputPadding = 8;

    /// <summary>角卡小字的最大宽度。</summary>
    public const double CornerCaptionWidth = 220;

    /// <summary>「没有可见步骤」说明的最大宽度。</summary>
    public const double EmptyStepsWidth = 340;

    /// <summary>层卡片内边距。</summary>
    public const double LayerPadding = 12;

    /// <summary>层与层之间的垂直间距。</summary>
    public const double LayerGap = 12;

    /// <summary>同一层内两张卡片之间的垂直间距。</summary>
    public const double CardGap = 12;

    /// <summary>导出图里工序卡片区域的固定内容宽度（与页面窄屏无关）。</summary>
    public const double ExportContentWidth = 960;

    /// <summary>标题字号与行高。</summary>
    public const double CardTitleFontSize = 14;

    public const double CardTitleHeight = 20;

    /// <summary>正文 / Note 的字号与行高。</summary>
    public const double NoteFontSize = 12;

    public const double NoteLineHeight = 18;

    /// <summary>
    /// 估算一段文字的显示宽度：CJK 与全角标点按字号，其余按 0.56 字号。
    /// 与 ProductionPathGraphDrawable 的标题估算保持一致。
    /// </summary>
    public static double MeasureText(
        string? text,
        double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double width = 0;

        foreach (char character in text)
        {
            width += IsWide(character)
                ? fontSize
                : fontSize * 0.56;
        }

        return width;
    }

    /// <summary>按最大宽度把文字拆成多行；返回至少一行（空字符串也返回一个空行）。</summary>
    public static List<string> WrapText(
        string? text,
        double maxWidth,
        double fontSize)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            lines.Add("");

            return lines;
        }

        if (maxWidth <= 1)
        {
            lines.Add(text);

            return lines;
        }

        var current = new System.Text.StringBuilder();

        double currentWidth = 0;

        foreach (char character in text)
        {
            double characterWidth = IsWide(character)
                ? fontSize
                : fontSize * 0.56;

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

        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>
    /// 计算一张卡片在给定内容宽度下的布局。
    ///
    /// <paramref name="contentWidth"/> 是卡片内边距以内的宽度；
    /// 返回结果的 <see cref="WorkOrderCardLayoutResult.Width"/> 已加回左右内边距。
    /// </summary>
    public static WorkOrderCardLayoutResult MeasureCard(
        WorkOrderCardItem card,
        double contentWidth)
    {
        ArgumentNullException.ThrowIfNull(card);

        contentWidth = Math.Max(1, contentWidth);

        var result = new WorkOrderCardLayoutResult
        {
            Card = card,
            ContentWidth = contentWidth,
            Width = contentWidth + CardPadding * 2
        };

        double top = CardPadding;

        // ── 标题行：标题 + 种类 / Mode / 验证小标签 ─────────────────────
        double titleWidth = Math.Min(
            MeasureText(card.Title, CardTitleFontSize),
            contentWidth);

        result.TitleWidth = titleWidth;

        double titleLineHeight = CardTitleHeight;

        double headerBottom = titleLineHeight;

        double chipX = titleWidth > 0
            ? titleWidth + 8
            : 0;

        double chipTop = Math.Max(0, (titleLineHeight - ChipHeight) / 2);

        AddChip(
            result,
            card.KindText,
            WorkOrderCardColors.KindChipFill,
            WorkOrderCardColors.KindChipText,
            contentWidth,
            ref chipX,
            ref chipTop,
            ref headerBottom);

        if (!string.IsNullOrWhiteSpace(card.ModeText))
        {
            AddChip(
                result,
                card.ModeText,
                WorkOrderCardColors.ModeChipFill,
                WorkOrderCardColors.ModeChipText,
                contentWidth,
                ref chipX,
                ref chipTop,
                ref headerBottom);
        }

        AddChip(
            result,
            card.Verified ? "已验证" : "未验证",
            card.Verified
                ? WorkOrderCardColors.VerifiedFill
                : WorkOrderCardColors.UnverifiedFill,
            card.Verified
                ? WorkOrderCardColors.Success
                : WorkOrderCardColors.WarningTitleText,
            contentWidth,
            ref chipX,
            ref chipTop,
            ref headerBottom);

        result.HeaderHeight = headerBottom;

        top += headerBottom + 8;

        // ── 旧版 B 类半边卡说明（历史数据） ─────────────────────────────
        if (card.IsLegacyHalf)
        {
            result.LegacyPanel = AddTextPanel(
                top,
                contentWidth,
                string.Empty,
                "旧版 B 类·碎晶半边卡（仅兼容历史数据）：按原步骤回放展示，新分析不会再生成这种卡。",
                WorkOrderCardColors.LegacyFill,
                WorkOrderCardColors.LegacyStroke,
                WorkOrderCardColors.LegacyText,
                WorkOrderCardColors.LegacyText);

            top += result.LegacyPanel.Height + 8;
        }

        // ── 未验证警示：红色标题 + Note；不画输入链 ─────────────────────
        if (!card.Verified)
        {
            result.WarningPanel = AddTextPanel(
                top,
                contentWidth,
                "未通过真实回放",
                card.Note ?? "",
                WorkOrderCardColors.WarningFill,
                WorkOrderCardColors.WarningStroke,
                WorkOrderCardColors.WarningTitleText,
                WorkOrderCardColors.BodyText);

            top += result.WarningPanel.Height + 8;
        }

        // ── 卡内区块：输入 → 前态 → 可见步骤 → 输出 → 角卡小字 ──────────
        var blocks = new List<WorkOrderCardBlockLayout>(5);

        if (card.Verified && card.Inputs.Count > 0)
        {
            blocks.Add(BuildInputsBlock(card, contentWidth));
        }

        if (card.PreState is not null)
        {
            blocks.Add(BuildPreStateBlock(card, contentWidth));
        }

        if (card.Steps.Count > 0)
        {
            blocks.Add(BuildStepsBlock(card, contentWidth));
        }
        else if (card.Verified)
        {
            blocks.Add(BuildEmptyStepsBlock(contentWidth));
        }

        blocks.Add(BuildOutputBlock(card, contentWidth));

        if (!string.IsNullOrWhiteSpace(card.ColumnCaption))
        {
            blocks.Add(BuildCornerCaptionBlock(card, contentWidth));
        }

        // 区块横向流式排列：一行放不下时换行，与页面的 CardFlowLayout 一致。
        double cursor = 0;

        double rowTop = 0;

        double rowHeight = 0;

        foreach (WorkOrderCardBlockLayout block in blocks)
        {
            if (cursor > 0 &&
                cursor + BlockGap + block.Width > contentWidth)
            {
                rowTop += rowHeight + BlockLineGap;

                cursor = 0;

                rowHeight = 0;
            }

            if (cursor > 0)
            {
                cursor += BlockGap;
            }

            block.X = CardPadding + cursor;

            block.Y = top + rowTop;

            cursor += block.Width;

            rowHeight = Math.Max(rowHeight, block.Height);
        }

        result.Blocks.AddRange(blocks);

        result.Height =
            top +
            rowTop +
            rowHeight +
            CardPadding;

        return result;
    }

    /// <summary>
    /// 不带换行的自然宽度：先按无限宽排成一行，再把内容宽度撑到一行所需宽度。
    /// 导出图用它决定画布至少要多宽；页面不使用。
    /// </summary>
    public static WorkOrderCardLayoutResult MeasureNatural(
        WorkOrderCardItem card)
    {
        ArgumentNullException.ThrowIfNull(card);

        double headerWidth = MeasureHeaderWidth(card);

        double blocksWidth = 0;

        int blockCount = 0;

        foreach (WorkOrderCardBlockLayout block in BuildBlocksForNaturalWidth(card))
        {
            if (blockCount > 0)
            {
                blocksWidth += BlockGap;
            }

            blocksWidth += block.Width;

            blockCount++;
        }

        double contentWidth = Math.Max(
            260,
            Math.Max(headerWidth, blocksWidth));

        return MeasureCard(card, contentWidth);
    }

    /// <summary>标题行自然宽度：标题 + 所有小标签 + 间距。</summary>
    public static double MeasureHeaderWidth(
        WorkOrderCardItem card)
    {
        double width = MeasureText(card.Title, CardTitleFontSize);

        bool anyChip = false;

        void Add(string text)
        {
            if (!anyChip)
            {
                width += 8;

                anyChip = true;
            }
            else
            {
                width += ChipGap;
            }

            width += MeasureChipWidth(text);
        }

        Add(card.KindText);

        if (!string.IsNullOrWhiteSpace(card.ModeText))
        {
            Add(card.ModeText);
        }

        Add(card.Verified ? "已验证" : "未验证");

        return width;
    }

    /// <summary>小标签宽度：文字估算 + 左右内边距。</summary>
    public static double MeasureChipWidth(
        string text)
    {
        return MeasureText(text, 11) + ChipHorizontalPadding;
    }

    private static void AddChip(
        WorkOrderCardLayoutResult result,
        string text,
        uint fill,
        uint textColor,
        double contentWidth,
        ref double chipX,
        ref double chipTop,
        ref double headerBottom)
    {
        double chipWidth = MeasureChipWidth(text);

        if (chipX > 0 &&
            chipX + chipWidth > contentWidth)
        {
            chipX = 0;

            chipTop = headerBottom + 4;

            headerBottom = chipTop + ChipHeight;
        }

        result.Chips.Add(new WorkOrderCardChipLayout
        {
            Text = text,
            X = CardPadding + chipX,
            Y = CardPadding + chipTop,
            Width = chipWidth,
            Height = ChipHeight,
            Fill = fill,
            TextColor = textColor
        });

        chipX += chipWidth + ChipGap;

        headerBottom = Math.Max(headerBottom, chipTop + ChipHeight);
    }

    /// <summary>
    /// 一段带左边框的说明块（旧版说明 / 未验证警示）。
    /// 布局里同时保存拆好的 Note 行，导出图不需要再重新折行。
    /// </summary>
    private static WorkOrderCardTextPanelLayout AddTextPanel(
        double top,
        double contentWidth,
        string title,
        string note,
        uint fill,
        uint stroke,
        uint textColor,
        uint noteColor)
    {
        const double padding = 8;

        const double barWidth = 3;

        double noteWidth = Math.Max(
            1,
            contentWidth - padding * 2 - barWidth);

        List<string> noteLines = WrapText(note, noteWidth, NoteFontSize);

        double titleHeight = string.IsNullOrEmpty(title)
            ? 0
            : NoteLineHeight;

        double noteHeight = string.IsNullOrWhiteSpace(note)
            ? 0
            : noteLines.Count * NoteLineHeight + (titleHeight > 0 ? 4 : 0);

        return new WorkOrderCardTextPanelLayout
        {
            X = CardPadding,
            Y = top,
            Width = contentWidth,
            Height = padding * 2 + titleHeight + noteHeight,
            Title = title,
            TitleHeight = titleHeight,
            NoteLines = string.IsNullOrWhiteSpace(note)
                ? Array.Empty<string>()
                : noteLines,
            NoteTop = top + padding + titleHeight + 4,
            BarWidth = barWidth,
            Padding = padding,
            Fill = fill,
            Stroke = stroke,
            TextColor = textColor,
            NoteColor = noteColor
        };
    }

    private static List<WorkOrderCardBlockLayout> BuildBlocksForNaturalWidth(
        WorkOrderCardItem card)
    {
        var blocks = new List<WorkOrderCardBlockLayout>(5);

        if (card.Verified && card.Inputs.Count > 0)
        {
            blocks.Add(BuildInputsBlock(card, double.PositiveInfinity));
        }

        if (card.PreState is not null)
        {
            blocks.Add(BuildPreStateBlock(card, double.PositiveInfinity));
        }

        if (card.Steps.Count > 0)
        {
            blocks.Add(BuildStepsBlock(card, double.PositiveInfinity));
        }
        else if (card.Verified)
        {
            blocks.Add(BuildEmptyStepsBlock(double.PositiveInfinity));
        }

        blocks.Add(BuildOutputBlock(card, double.PositiveInfinity));

        if (!string.IsNullOrWhiteSpace(card.ColumnCaption))
        {
            blocks.Add(BuildCornerCaptionBlock(card, double.PositiveInfinity));
        }

        return blocks;
    }

    private static WorkOrderCardBlockLayout BuildInputsBlock(
        WorkOrderCardItem card,
        double contentWidth)
    {
        var block = new WorkOrderCardBlockLayout
        {
            Kind = "inputs",
            Title = "输入（上一层卡产物）",
            TitleWidth = MeasureText("输入（上一层卡产物）", SectionTitleFontSize),
            BodyTop = SectionTitleHeight + SectionTitleGap
        };

        double limit = GetLimit(contentWidth);

        double x = 0;

        double y = 0;

        double rowHeight = 0;

        double maxWidth = 0;

        int rowCount = 1;

        foreach (WorkOrderCardInput input in card.Inputs)
        {
            double shapeWidth = input.Shape.PartCount * CardMetrics.CellSize;

            double shapeHeight = input.Shape.LayerCount * CardMetrics.CellSize;

            double boxWidth =
                Math.Max(
                    MeasureText(input.Title, InputTitleFontSize),
                    shapeWidth) +
                InputPadding * 2;

            double boxHeight =
                InputPadding +
                InputTitleHeight +
                6 +
                shapeHeight +
                InputPadding;

            if (x > 0 &&
                x + InputGap + boxWidth > limit)
            {
                y += rowHeight + BlockLineGap;

                x = 0;

                rowHeight = 0;

                rowCount++;
            }

            if (x > 0)
            {
                x += InputGap;
            }

            block.Items.Add(new WorkOrderCardItemLayout
            {
                X = x,
                Y = block.BodyTop + y,
                Width = boxWidth,
                Height = boxHeight,
                Label = input.Title,
                ShapeBox = new WorkOrderShapeBoxLayout
                {
                    X = x,
                    Y = block.BodyTop + y,
                    Width = boxWidth,
                    Height = boxHeight,
                    ImageX = x + InputPadding,
                    ImageY = block.BodyTop + y + InputPadding + InputTitleHeight + 6,
                    ImageWidth = shapeWidth,
                    ImageHeight = shapeHeight
                }
            });

            x += boxWidth;

            rowHeight = Math.Max(rowHeight, boxHeight);

            maxWidth = Math.Max(maxWidth, x);
        }

        block.Width = Math.Min(
            limit,
            Math.Max(block.TitleWidth, maxWidth));

        block.Height = block.BodyTop + y + rowHeight;

        block.RowCount = rowCount;

        return block;
    }

    private static WorkOrderCardBlockLayout BuildPreStateBlock(
        WorkOrderCardItem card,
        double contentWidth)
    {
        ShapeDocument? preState = card.PreState;

        if (preState is null)
        {
            return new WorkOrderCardBlockLayout();
        }

        string title = PreStateSectionTitle(card);

        double shapeWidth = preState.PartCount * CardMetrics.CellSize;

        double shapeHeight = preState.LayerCount * CardMetrics.CellSize;

        const double padding = 9;

        double boxWidth = shapeWidth + padding * 2;

        double boxHeight = shapeHeight + padding * 2;

        double titleWidth = MeasureText(title, SectionTitleFontSize);

        var block = new WorkOrderCardBlockLayout
        {
            Kind = "prestate",
            Title = title,
            TitleWidth = titleWidth,
            BodyTop = SectionTitleHeight + SectionTitleGap,
            Width = Math.Min(
                GetLimit(contentWidth),
                Math.Max(titleWidth, boxWidth)),
            Height = SectionTitleHeight + SectionTitleGap + boxHeight,
            ShapeBox = new WorkOrderShapeBoxLayout
            {
                X = 0,
                Y = SectionTitleHeight + SectionTitleGap,
                Width = boxWidth,
                Height = boxHeight,
                ImageX = padding,
                ImageY = SectionTitleHeight + SectionTitleGap + padding,
                ImageWidth = shapeWidth,
                ImageHeight = shapeHeight
            }
        };

        return block;
    }

    private static WorkOrderCardBlockLayout BuildStepsBlock(
        WorkOrderCardItem card,
        double contentWidth)
    {
        string title = $"可见步骤（{card.Steps.Count} 步）";

        var block = new WorkOrderCardBlockLayout
        {
            Kind = "steps",
            Title = title,
            TitleWidth = MeasureText(title, SectionTitleFontSize),
            BodyTop = SectionTitleHeight + SectionTitleGap
        };

        double limit = GetLimit(contentWidth);

        double x = 0;

        double y = 0;

        double rowHeight = 0;

        double maxWidth = 0;

        int rowCount = 1;

        for (int index = 0; index < card.Steps.Count; index++)
        {
            WorkOrderCardStep step = card.Steps[index];

            double nodeWidth = CardMetrics.NodeWidth(step.Shape.PartCount);

            double nodeHeight = CardMetrics.NodeHeight(step.Shape.LayerCount);

            bool showArrow = index < card.Steps.Count - 1;

            double itemWidth = nodeWidth + (showArrow ? StepArrowWidth : 0);

            if (x > 0 &&
                x + itemWidth > limit)
            {
                y += rowHeight + BlockLineGap;

                x = 0;

                rowHeight = 0;

                rowCount++;
            }

            double itemTop = block.BodyTop + y;

            block.Items.Add(new WorkOrderCardItemLayout
            {
                X = x,
                Y = itemTop,
                Width = nodeWidth,
                Height = nodeHeight,
                Label = step.Operation,
                ShapeBox = new WorkOrderShapeBoxLayout
                {
                    X = x,
                    Y = itemTop,
                    Width = nodeWidth,
                    Height = nodeHeight,
                    ImageX = x + 7,
                    ImageY = itemTop + 30 +
                             Math.Max(
                                 0,
                                 nodeHeight - 30 -
                                 step.Shape.LayerCount * CardMetrics.CellSize) / 2,
                    ImageWidth = step.Shape.PartCount * CardMetrics.CellSize,
                    ImageHeight = step.Shape.LayerCount * CardMetrics.CellSize
                },
                StepAccent = WorkOrderCardColors.StepAccent(step.CssClass),
                StepFill = WorkOrderCardColors.StepFill(step.CssClass)
            });

            x += nodeWidth;

            if (showArrow)
            {
                block.Items.Add(new WorkOrderCardItemLayout
                {
                    X = x,
                    Y = itemTop,
                    Width = StepArrowWidth,
                    Height = nodeHeight,
                    IsArrow = true,
                    Label = "→"
                });

                x += StepArrowWidth;
            }

            maxWidth = Math.Max(maxWidth, x);

            rowHeight = Math.Max(rowHeight, nodeHeight);
        }

        block.Width = Math.Min(
            limit,
            Math.Max(block.TitleWidth, maxWidth));

        block.Height = block.BodyTop + y + rowHeight;

        block.RowCount = rowCount;

        return block;
    }

    private static WorkOrderCardBlockLayout BuildEmptyStepsBlock(
        double contentWidth)
    {
        const string text =
            "没有可见步骤：旋转 / 废料 / 纯支撑交接等结构步骤已在 Core 中标记为隐藏。";

        double limit = Math.Min(
            GetLimit(contentWidth),
            EmptyStepsWidth);

        List<string> lines = WrapText(text, Math.Max(1, limit), NoteFontSize);

        return new WorkOrderCardBlockLayout
        {
            Kind = "empty-steps",
            Width = limit,
            Height = lines.Count * NoteLineHeight,
            TextLines = lines
        };
    }

    private static WorkOrderCardBlockLayout BuildOutputBlock(
        WorkOrderCardItem card,
        double contentWidth)
    {
        var block = new WorkOrderCardBlockLayout
        {
            Kind = "output",
            Title = "输出",
            TitleWidth = MeasureText("输出", SectionTitleFontSize),
            BodyTop = SectionTitleHeight + SectionTitleGap,
            Caption = card.OutputCaption
        };

        double shapeWidth = card.Output.PartCount * CardMetrics.CellSize;

        double shapeHeight = card.Output.LayerCount * CardMetrics.CellSize;

        const double padding = 10;

        double boxWidth = shapeWidth + padding * 2;

        double boxHeight = shapeHeight + padding * 2;

        double headerWidth = block.TitleWidth;

        if (!string.IsNullOrWhiteSpace(card.OutputCaption))
        {
            headerWidth += 6 + MeasureText(card.OutputCaption, 11);
        }

        block.Width = Math.Min(
            GetLimit(contentWidth),
            Math.Max(headerWidth, boxWidth));

        block.Height = SectionTitleHeight + SectionTitleGap + boxHeight;

        block.ShapeBox = new WorkOrderShapeBoxLayout
        {
            X = 0,
            Y = SectionTitleHeight + SectionTitleGap,
            Width = boxWidth,
            Height = boxHeight,
            ImageX = padding,
            ImageY = SectionTitleHeight + SectionTitleGap + padding,
            ImageWidth = shapeWidth,
            ImageHeight = shapeHeight
        };

        return block;
    }

    private static WorkOrderCardBlockLayout BuildCornerCaptionBlock(
        WorkOrderCardItem card,
        double contentWidth)
    {
        double limit = Math.Min(
            GetLimit(contentWidth),
            CornerCaptionWidth);

        List<string> lines = WrapText(
            card.ColumnCaption,
            Math.Max(1, limit),
            11);

        return new WorkOrderCardBlockLayout
        {
            Kind = "corner-caption",
            Width = limit,
            Height = lines.Count * 16,
            TextLines = lines
        };
    }

    /// <summary>前态小节标题：默认「前态」，否则加「前态 · 」前缀（与 Web / WPF 一致）。</summary>
    public static string PreStateSectionTitle(
        WorkOrderCardItem card)
    {
        string title = card.PreStateTitle;

        return string.IsNullOrWhiteSpace(title) || title == "前态"
            ? "前态"
            : "前态 · " + title;
    }

    private static double GetLimit(
        double width) =>
        double.IsInfinity(width) || width <= 0
            ? double.PositiveInfinity
            : width;

    private static bool IsWide(
        char character) =>
        character >= 0x2E80;
}

/// <summary>一张卡片的完整布局结果（坐标相对卡片左上角）。</summary>
public sealed class WorkOrderCardLayoutResult
{
    public required WorkOrderCardItem Card { get; init; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double ContentWidth { get; set; }

    public double TitleWidth { get; set; }

    public double HeaderHeight { get; set; }

    public List<WorkOrderCardChipLayout> Chips { get; } = new();

    public WorkOrderCardTextPanelLayout? LegacyPanel { get; set; }

    public WorkOrderCardTextPanelLayout? WarningPanel { get; set; }

    public List<WorkOrderCardBlockLayout> Blocks { get; } = new();
}

/// <summary>标题行的一个小标签。</summary>
public sealed class WorkOrderCardChipLayout
{
    public string Text { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public uint Fill { get; set; }

    public uint TextColor { get; set; }
}

/// <summary>旧版说明 / 未验证警示块的布局。</summary>
public sealed class WorkOrderCardTextPanelLayout
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string Title { get; set; } = "";

    public double TitleHeight { get; set; }

    public IReadOnlyList<string> NoteLines { get; set; } = Array.Empty<string>();

    public double NoteTop { get; set; }

    public double BarWidth { get; set; } = 3;

    public double Padding { get; set; } = 8;

    public uint Fill { get; set; }

    public uint Stroke { get; set; }

    public uint TextColor { get; set; }

    public uint NoteColor { get; set; } = WorkOrderCardColors.BodyText;
}

/// <summary>卡内一个横向区块（输入 / 前态 / 步骤 / 输出 / 角卡小字）。</summary>
public sealed class WorkOrderCardBlockLayout
{
    public string Kind { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string Title { get; set; } = "";

    public double TitleWidth { get; set; }

    public string Caption { get; set; } = "";

    /// <summary>区块标题占用的高度：标题 + 间距。</summary>
    public double BodyTop { get; set; }

    public WorkOrderShapeBoxLayout? ShapeBox { get; set; }

    public List<WorkOrderCardItemLayout> Items { get; } = new();

    public IReadOnlyList<string> TextLines { get; set; } = Array.Empty<string>();

    public int RowCount { get; set; }
}

/// <summary>输入框或步骤节点。</summary>
public sealed class WorkOrderCardItemLayout
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string Label { get; set; } = "";

    public WorkOrderShapeBoxLayout? ShapeBox { get; set; }

    public bool IsArrow { get; set; }

    public uint StepAccent { get; set; } = WorkOrderCardColors.Accent;

    public uint StepFill { get; set; } = WorkOrderCardColors.InputFill;
}

/// <summary>一个方框及其内部竖式图的矩形。</summary>
public sealed class WorkOrderShapeBoxLayout
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public double ImageX { get; set; }

    public double ImageY { get; set; }

    public double ImageWidth { get; set; }

    public double ImageHeight { get; set; }
}

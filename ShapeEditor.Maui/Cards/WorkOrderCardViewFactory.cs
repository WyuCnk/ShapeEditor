using Microsoft.Maui.Controls.Shapes;
using ShapeEditor.Core.Models;
using ShapeEditor.Presentation.Cards;
using ShapeEditor.Presentation.Styling;

namespace ShapeEditor.Maui.Cards;

/// <summary>
/// 把 Presentation 的三层工序卡片投影成 MAUI 控件树。
///
/// <para>
/// 选择「XAML 控件 + 自绘流式 Layout」而不是整张卡自绘：
/// 标题、种类、Note、验证标记都是可选中的文字，用 Label / Border 更自然，
/// 也能跟随系统字号与深浅色；只有卡内 30px 单元格的竖式图交给
/// <see cref="WorkspaceCellDrawable"/>（与工作区、分析页顶部预览同一套格线 /
/// 字母 / 配色，不另写一套格子绘制）。
/// </para>
/// <para>
/// 卡内区块的换行由 <see cref="WorkOrderCardFlowLayout"/> 负责；每层卡片本身
/// 用纵向 StackLayout 排列，满足手机窄屏「一层一张卡」的要求。
/// </para>
/// </summary>
public static class WorkOrderCardViewFactory
{
    /// <summary>整棵卡片树：交换工序 → 半边工序 → 角工序。</summary>
    public static View Build(
        WorkOrderCardPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var previews = new Dictionary<ShapeDocument, WorkspaceCellDrawable>(
            ReferenceEqualityComparer.Instance);

        var root = new VerticalStackLayout
        {
            Spacing = WorkOrderCardLayout.LayerGap,
            HorizontalOptions = LayoutOptions.Fill
        };

        foreach (WorkOrderCardLayer layer in presentation.Layers)
        {
            root.Add(BuildLayer(layer, previews));
        }

        return root;
    }

    /// <summary>板块摘要：总张数、分层张数与验证进度。</summary>
    public static string BuildSummary(
        WorkOrderCardPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (!presentation.HasCards)
        {
            return "没有工序卡片。";
        }

        string layers = string.Join(
            "、",
            presentation.Layers.Select(layer =>
                $"{layer.Title} {layer.Cards.Count} 张"));

        return $"共 {presentation.TotalCount} 张卡（{layers}）；" +
               $"已验证 {presentation.VerifiedCount}/{presentation.TotalCount}。";
    }

    private static View BuildLayer(
        WorkOrderCardLayer layer,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(3)),
                new ColumnDefinition(GridLength.Star)
            }
        };

        grid.Add(new BoxView
        {
            Color = Color.FromUint(WorkOrderCardColors.LayerAccent(layer.Kind)),
            WidthRequest = 3,
            Margin = new Thickness(0, 10),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill
        }, 0, 0);

        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(12, 10, 12, 12)
        };

        var header = new HorizontalStackLayout
        {
            Spacing = 8
        };

        header.Add(Text(
            layer.Title,
            14,
            FontAttributes.Bold,
            WorkOrderCardColors.TitleText));

        header.Add(Text(
            $"{layer.Cards.Count} 张卡",
            11,
            FontAttributes.Bold,
            WorkOrderCardColors.MutedText,
            verticalAlignment: TextAlignment.End));

        content.Add(header);

        var cardList = new VerticalStackLayout
        {
            Spacing = WorkOrderCardLayout.CardGap
        };

        foreach (WorkOrderCardItem card in layer.Cards)
        {
            cardList.Add(BuildCard(card, previews));
        }

        content.Add(cardList);

        grid.Add(content, 1, 0);

        return new Border
        {
            BackgroundColor = Color.FromUint(WorkOrderCardColors.LayerBackground),
            Stroke = Color.FromUint(WorkOrderCardColors.LayerStroke),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 10
            },
            Content = grid
        };
    }

    private static View BuildCard(
        WorkOrderCardItem card,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 8
        };

        // 标题行：标题 + 种类 + 半边 Mode + 验证标记，窄屏时自动换行。
        var header = new WorkOrderCardFlowLayout
        {
            ItemGap = 6,
            LineGap = 4
        };

        header.Add(Text(
            card.Title,
            14,
            FontAttributes.Bold,
            WorkOrderCardColors.TitleText,
            verticalAlignment: TextAlignment.Center));

        header.Add(Chip(
            card.KindText,
            WorkOrderCardColors.KindChipFill,
            WorkOrderCardColors.KindChipText));

        if (!string.IsNullOrWhiteSpace(card.ModeText))
        {
            header.Add(Chip(
                card.ModeText,
                WorkOrderCardColors.ModeChipFill,
                WorkOrderCardColors.ModeChipText));
        }

        header.Add(Chip(
            card.Verified ? "已验证" : "未验证",
            card.Verified
                ? WorkOrderCardColors.VerifiedFill
                : WorkOrderCardColors.UnverifiedFill,
            card.Verified
                ? WorkOrderCardColors.Success
                : WorkOrderCardColors.WarningTitleText));

        content.Add(header);

        // 旧版 B 类·碎晶半边卡：只给历史数据加降级说明，不隐藏内容。
        if (card.IsLegacyHalf)
        {
            content.Add(NotePanel(
                "旧版 B 类·碎晶半边卡（仅兼容历史数据）：按原步骤回放展示，新分析不会再生成这种卡。",
                isWarning: false));
        }

        // 未验证：红色警示 + Note；不画输入链。
        if (!card.Verified)
        {
            content.Add(NotePanel(
                card.Note ?? "",
                isWarning: true));
        }

        // 卡内区块横向流式排列：输入 → 前态 → 可见步骤 → 输出。
        var flow = new WorkOrderCardFlowLayout
        {
            ItemGap = 12,
            LineGap = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };

        if (card.Verified && card.Inputs.Count > 0)
        {
            flow.Add(BuildInputsBlock(card, previews));
        }

        if (card.PreState is not null)
        {
            flow.Add(BuildPreStateBlock(card, previews));
        }

        if (card.Steps.Count > 0)
        {
            flow.Add(BuildStepsBlock(card, previews));
        }
        else if (card.Verified)
        {
            flow.Add(Text(
                "没有可见步骤：旋转 / 废料 / 纯支撑交接等结构步骤已在 Core 中标记为隐藏。",
                12,
                FontAttributes.None,
                WorkOrderCardColors.MutedText,
                maximumWidth: 340));
        }

        flow.Add(BuildOutputBlock(card, previews));

        content.Add(flow);

        return new Border
        {
            BackgroundColor = Color.FromUint(
                card.Verified
                    ? WorkOrderCardColors.CardBackground
                    : WorkOrderCardColors.CardUnverifiedBackground),
            Stroke = Color.FromUint(
                card.Verified
                    ? WorkOrderCardColors.CardStroke
                    : WorkOrderCardColors.CardUnverifiedStroke),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 10
            },
            Padding = new Thickness(WorkOrderCardLayout.CardPadding),
            HorizontalOptions = LayoutOptions.Fill,
            Content = content
        };
    }

    private static View BuildInputsBlock(
        WorkOrderCardItem card,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        var block = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Start
        };

        block.Add(Text(
            "输入（上一层卡产物）",
            12,
            FontAttributes.Bold,
            WorkOrderCardColors.SectionTitleText));

        var list = new WorkOrderCardFlowLayout
        {
            ItemGap = 12,
            LineGap = 8
        };

        foreach (WorkOrderCardInput input in card.Inputs)
        {
            var itemContent = new VerticalStackLayout
            {
                Spacing = 6
            };

            itemContent.Add(Text(
                input.Title,
                11,
                FontAttributes.Bold,
                WorkOrderCardColors.MutedText));

            itemContent.Add(CreateShapeView(input.Shape, previews));

            list.Add(new Border
            {
                BackgroundColor = Color.FromUint(WorkOrderCardColors.InputFill),
                Stroke = Color.FromUint(WorkOrderCardColors.InputStroke),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle
                {
                    CornerRadius = 8
                },
                Padding = new Thickness(WorkOrderCardLayout.InputPadding),
                HorizontalOptions = LayoutOptions.Start,
                Content = itemContent
            });
        }

        block.Add(list);

        return block;
    }

    private static View BuildPreStateBlock(
        WorkOrderCardItem card,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        if (card.PreState is null)
        {
            return new BoxView();
        }

        var block = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Start
        };

        block.Add(Text(
            WorkOrderCardLayout.PreStateSectionTitle(card),
            12,
            FontAttributes.Bold,
            WorkOrderCardColors.PreStateTitleText));

        block.Add(new Border
        {
            BackgroundColor = Color.FromUint(WorkOrderCardColors.PreStateFill),
            Stroke = Color.FromUint(WorkOrderCardColors.PreStateStroke),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 8
            },
            Padding = 9,
            HorizontalOptions = LayoutOptions.Start,
            Content = CreateShapeView(card.PreState, previews)
        });

        return block;
    }

    private static View BuildStepsBlock(
        WorkOrderCardItem card,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        var block = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Start
        };

        block.Add(Text(
            $"可见步骤（{card.Steps.Count} 步）",
            12,
            FontAttributes.Bold,
            WorkOrderCardColors.SectionTitleText));

        var list = new WorkOrderCardFlowLayout
        {
            ItemGap = 0,
            LineGap = 8
        };

        for (int index = 0; index < card.Steps.Count; index++)
        {
            WorkOrderCardStep step = card.Steps[index];

            double nodeWidth = CardMetrics.NodeWidth(step.Shape.PartCount);

            double nodeHeight = CardMetrics.NodeHeight(step.Shape.LayerCount);

            var nodeGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(new GridLength(26)),
                    new RowDefinition(GridLength.Star)
                }
            };

            nodeGrid.Add(Text(
                step.Operation,
                11,
                FontAttributes.Bold,
                WorkOrderCardColors.StepHeaderText,
                maximumLines: 1,
                lineBreakMode: LineBreakMode.TailTruncation), 0, 0);

            GraphicsView preview = CreateShapeView(step.Shape, previews);

            preview.HorizontalOptions = LayoutOptions.Center;

            preview.VerticalOptions = LayoutOptions.Center;

            nodeGrid.Add(preview, 0, 1);

            var node = new Border
            {
                WidthRequest = nodeWidth,
                HeightRequest = nodeHeight,
                BackgroundColor = Color.FromUint(
                    WorkOrderCardColors.StepFill(step.CssClass)),
                Stroke = Color.FromUint(
                    WorkOrderCardColors.StepAccent(step.CssClass)),
                StrokeThickness = 1.5,
                StrokeShape = new RoundRectangle
                {
                    CornerRadius = 10
                },
                Padding = 7,
                Content = nodeGrid
            };

            var item = new HorizontalStackLayout
            {
                Spacing = 0,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start
            };

            item.Add(node);

            // 「→」跟着步骤节点放在同一个流式单元里，换行时不会落到行首。
            if (index < card.Steps.Count - 1)
            {
                item.Add(new Label
                {
                    Text = "→",
                    WidthRequest = WorkOrderCardLayout.StepArrowWidth,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromUint(WorkOrderCardColors.Accent),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    VerticalOptions = LayoutOptions.Center
                });
            }

            list.Add(item);
        }

        block.Add(list);

        return block;
    }

    private static View BuildOutputBlock(
        WorkOrderCardItem card,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        var block = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Start
        };

        var header = new HorizontalStackLayout
        {
            Spacing = 6
        };

        header.Add(Text(
            "输出",
            12,
            FontAttributes.Bold,
            WorkOrderCardColors.SectionTitleText));

        if (!string.IsNullOrWhiteSpace(card.OutputCaption))
        {
            header.Add(Text(
                card.OutputCaption,
                11,
                FontAttributes.None,
                WorkOrderCardColors.MutedText,
                verticalAlignment: TextAlignment.Center));
        }

        block.Add(header);

        block.Add(new Border
        {
            BackgroundColor = Color.FromUint(WorkOrderCardColors.OutputFill),
            Stroke = Color.FromUint(WorkOrderCardColors.OutputStroke),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 10
            },
            Padding = 10,
            HorizontalOptions = LayoutOptions.Start,
            Content = CreateShapeView(card.Output, previews)
        });

        // 角卡小字：Q1 目标角 / Q2 右柱 / Q4 左柱。
        if (!string.IsNullOrWhiteSpace(card.ColumnCaption))
        {
            block.Add(Text(
                card.ColumnCaption,
                11,
                FontAttributes.None,
                WorkOrderCardColors.MutedText,
                maximumWidth: WorkOrderCardLayout.CornerCaptionWidth));
        }

        return block;
    }

    private static View NotePanel(
        string text,
        bool isWarning)
    {
        uint fill = isWarning
            ? WorkOrderCardColors.WarningFill
            : WorkOrderCardColors.LegacyFill;

        uint stroke = isWarning
            ? WorkOrderCardColors.WarningStroke
            : WorkOrderCardColors.LegacyStroke;

        uint textColor = isWarning
            ? WorkOrderCardColors.BodyText
            : WorkOrderCardColors.LegacyText;

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(3)),
                new ColumnDefinition(GridLength.Star)
            }
        };

        grid.Add(new BoxView
        {
            Color = Color.FromUint(stroke),
            WidthRequest = 3,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill
        }, 0, 0);

        var content = new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(8, 6)
        };

        if (isWarning)
        {
            content.Add(Text(
                "未通过真实回放",
                12,
                FontAttributes.Bold,
                WorkOrderCardColors.WarningTitleText));
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            content.Add(Text(
                text,
                12,
                FontAttributes.None,
                textColor,
                fillWidth: true));
        }

        grid.Add(content, 1, 0);

        return new Border
        {
            BackgroundColor = Color.FromUint(fill),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 6
            },
            Content = grid
        };
    }

    /// <summary>
    /// 一张固定 30px 单元格的竖式图。
    ///
    /// <para>
    /// 直接复用 <see cref="WorkspaceCellDrawable"/> 的 c / S / P 字母、颜色与格线，
    /// 与工作区节点、分析页顶部预览、制作路径节点保持一致。
    /// 没有另写一套格子绘制：<c>ShapePreviewDrawable</c> 是按 dirtyRect 缩放的
    /// 批量预览（并且用大写 C），<c>ShapeGridDrawable</c> 是编辑器整张画布，
    /// 两者都不适合卡片里固定 30px 的 1:1 小图；<see cref="WorkspaceCellDrawable.DrawDocument"/>
    /// 已经是三处共用的固定尺寸画法。
    /// </para>
    /// </summary>
    private static GraphicsView CreateShapeView(
        ShapeDocument shape,
        Dictionary<ShapeDocument, WorkspaceCellDrawable> previews)
    {
        if (!previews.TryGetValue(shape, out WorkspaceCellDrawable? drawable))
        {
            drawable = new WorkspaceCellDrawable(shape);

            previews[shape] = drawable;
        }

        return new GraphicsView
        {
            Drawable = drawable,
            WidthRequest = shape.PartCount * CardMetrics.CellSize,
            HeightRequest = shape.LayerCount * CardMetrics.CellSize,
            InputTransparent = true,
            BackgroundColor = Colors.White,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
    }

    private static View Chip(
        string text,
        uint fill,
        uint textColor)
    {
        return new Border
        {
            BackgroundColor = Color.FromUint(fill),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 10
            },
            Padding = new Thickness(
                WorkOrderCardLayout.ChipHorizontalPadding / 2,
                1),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = Text(
                text,
                11,
                FontAttributes.Bold,
                textColor)
        };
    }

    private static Label Text(
        string text,
        double fontSize,
        FontAttributes attributes,
        uint color,
        TextAlignment verticalAlignment = TextAlignment.Start,
        double? maximumWidth = null,
        int maximumLines = -1,
        LineBreakMode lineBreakMode = LineBreakMode.WordWrap,
        bool fillWidth = false)
    {
        var label = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = attributes,
            TextColor = Color.FromUint(color),
            LineBreakMode = lineBreakMode,
            VerticalTextAlignment = verticalAlignment,
            HorizontalOptions = fillWidth
                ? LayoutOptions.Fill
                : LayoutOptions.Start
        };

        if (maximumWidth is double width)
        {
            label.MaximumWidthRequest = width;
        }

        if (maximumLines > 0)
        {
            label.MaxLines = maximumLines;
        }

        return label;
    }
}

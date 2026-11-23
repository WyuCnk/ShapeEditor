using System.Windows;
using System.Windows.Controls;

namespace ShapeEditor.Wpf.Controls;

/// <summary>
/// 工序卡片内部的横向流式面板：子区块从左到右依次排列，一行放不下时自动换行。
///
/// <para>
/// 与普通 <see cref="WrapPanel"/> 的区别：这里<b>按面板可用宽度测量每个子区块</b>。
/// 某个区块（例如包含 3 个步骤节点的步骤组）自己比可用宽度还宽时，
/// 它会先在自己的内部折行，再作为一个整体参与外层换行；
/// 因此窄窗口下卡片内容只会换行，不会被卡片边框裁切。
/// </para>
/// <para>
/// 无限宽度测量（例如卡片在 <see cref="WorkOrderCardPanel"/> 里测自然宽度时）
/// 会退化成「所有区块排成一行」，供上层决定卡片的自然宽度。
/// </para>
/// </summary>
public sealed class CardFlowPanel : Panel
{
    public static readonly DependencyProperty ItemGapProperty =
        DependencyProperty.Register(
            nameof(ItemGap),
            typeof(double),
            typeof(CardFlowPanel),
            new FrameworkPropertyMetadata(
                12.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty LineGapProperty =
        DependencyProperty.Register(
            nameof(LineGap),
            typeof(double),
            typeof(CardFlowPanel),
            new FrameworkPropertyMetadata(
                8.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>同一行内区块之间的水平间距。</summary>
    public double ItemGap
    {
        get => (double)GetValue(ItemGapProperty);
        set => SetValue(ItemGapProperty, value);
    }

    /// <summary>换行后两行之间的垂直间距。</summary>
    public double LineGap
    {
        get => (double)GetValue(LineGapProperty);
        set => SetValue(LineGapProperty, value);
    }

    protected override Size MeasureOverride(
        Size availableSize)
    {
        double limit = GetLimit(availableSize.Width);

        double lineWidth = 0;

        double lineHeight = 0;

        double totalWidth = 0;

        double totalHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(new Size(0, 0));

                continue;
            }

            // 关键：按可用宽度测量子区块，过长时由区块自己折行。
            child.Measure(new Size(limit, double.PositiveInfinity));

            double itemWidth = child.DesiredSize.Width;

            double itemHeight = child.DesiredSize.Height;

            if (itemWidth <= 0 && itemHeight <= 0)
            {
                continue;
            }

            if (lineWidth > 0 &&
                lineWidth + ItemGap + itemWidth > limit)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);

                totalHeight += lineHeight + LineGap;

                lineWidth = 0;

                lineHeight = 0;
            }

            lineWidth += (lineWidth > 0 ? ItemGap : 0) + itemWidth;

            lineHeight = Math.Max(lineHeight, itemHeight);
        }

        if (lineWidth > 0)
        {
            totalWidth = Math.Max(totalWidth, lineWidth);

            totalHeight += lineHeight;
        }

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(
        Size finalSize)
    {
        double limit = GetLimit(finalSize.Width);

        double x = 0;

        double top = 0;

        double lineHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));

                continue;
            }

            double itemWidth = Math.Min(child.DesiredSize.Width, limit);

            double itemHeight = child.DesiredSize.Height;

            if (x > 0 &&
                x + ItemGap + itemWidth > limit)
            {
                top += lineHeight + LineGap;

                x = 0;

                lineHeight = 0;
            }

            if (x > 0)
            {
                x += ItemGap;
            }

            child.Arrange(new Rect(x, top, itemWidth, itemHeight));

            x += itemWidth;

            lineHeight = Math.Max(lineHeight, itemHeight);
        }

        return new Size(finalSize.Width, top + lineHeight);
    }

    /// <summary>可用宽度无效时按无限宽处理，让所有区块排成一行。</summary>
    private static double GetLimit(
        double width) =>
        double.IsInfinity(width) || width <= 0
            ? double.PositiveInfinity
            : width;
}

using System.Windows;
using System.Windows.Controls;

namespace ShapeEditor.Wpf.Controls;

/// <summary>
/// 工序卡片的纵向列表：<b>一行只放一张卡</b>，卡片宽度按内容自适应。
///
/// <para>
/// 每张卡先按无限宽测量，得到「卡内区块排成一行」的自然宽度：
/// 自然宽度不超过可用宽度时按自然宽度排布（内容少的卡不会硬拉满整行）；
/// 超过可用宽度时按可用宽度复测，卡内 <see cref="CardFlowPanel"/> 自动换行，
/// 因此窄窗口下也不会出现内容被卡片裁切。
/// </para>
/// <para>
/// 卡片之间保持 <see cref="ItemGap"/> 的垂直间距，整层高度为所有卡片高度之和。
/// </para>
/// </summary>
public sealed class WorkOrderCardPanel : Panel
{
    public static readonly DependencyProperty ItemGapProperty =
        DependencyProperty.Register(
            nameof(ItemGap),
            typeof(double),
            typeof(WorkOrderCardPanel),
            new FrameworkPropertyMetadata(
                12.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>卡片之间的垂直间距。</summary>
    public double ItemGap
    {
        get => (double)GetValue(ItemGapProperty);
        set => SetValue(ItemGapProperty, value);
    }

    /// <summary>测量阶段算出的每张卡片宽度，排布阶段直接复用。</summary>
    private readonly List<double> _cardWidths = new();

    protected override Size MeasureOverride(
        Size availableSize)
    {
        _cardWidths.Clear();

        double available = availableSize.Width;

        bool unbounded = double.IsInfinity(available) || available <= 0;

        double totalHeight = 0;

        double maxCardWidth = 0;

        int visibleCount = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(new Size(0, 0));

                _cardWidths.Add(0);

                continue;
            }

            // 先按无限宽测出卡内区块横向排成一行的自然宽度。
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double naturalWidth = child.DesiredSize.Width;

            double cardWidth = unbounded
                ? Math.Max(1, naturalWidth)
                : Math.Max(1, Math.Min(naturalWidth, available));

            if (!unbounded && naturalWidth > available)
            {
                // 内容比可用宽度宽：按可用宽度复测，卡内自动换行，不裁切。
                child.Measure(new Size(available, double.PositiveInfinity));
            }

            _cardWidths.Add(cardWidth);

            maxCardWidth = Math.Max(maxCardWidth, cardWidth);

            totalHeight += child.DesiredSize.Height;

            visibleCount++;
        }

        if (visibleCount > 1)
        {
            totalHeight += ItemGap * (visibleCount - 1);
        }

        return new Size(
            unbounded ? maxCardWidth : available,
            totalHeight);
    }

    protected override Size ArrangeOverride(
        Size finalSize)
    {
        double top = 0;

        int visibleCount = 0;

        for (int index = 0; index < InternalChildren.Count; index++)
        {
            UIElement child = InternalChildren[index];

            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));

                continue;
            }

            double cardWidth = index < _cardWidths.Count
                ? _cardWidths[index]
                : child.DesiredSize.Width;

            if (cardWidth <= 0)
            {
                cardWidth = Math.Max(1, child.DesiredSize.Width);
            }

            if (visibleCount > 0)
            {
                top += ItemGap;
            }

            child.Arrange(new Rect(
                0,
                top,
                cardWidth,
                child.DesiredSize.Height));

            top += child.DesiredSize.Height;

            visibleCount++;
        }

        return new Size(finalSize.Width, top);
    }
}

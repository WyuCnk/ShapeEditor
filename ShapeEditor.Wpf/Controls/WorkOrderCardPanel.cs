using System.Windows;
using System.Windows.Controls;

namespace ShapeEditor.Wpf.Controls;

/// <summary>
/// 工序卡片的横向布局面板。
///
/// <para>
/// 宽屏时一行两张，窄屏时自动变成一张一行：
/// 列数由「最小卡宽 + 卡片间距」推算，最多 <see cref="MaxColumns"/> 列；
/// 每一行的行高取该行最高卡片的高度，卡片之间保持 <see cref="ItemGap"/> 的间距。
/// </para>
/// <para>
/// 之所以不用普通 <see cref="WrapPanel"/>：卡片内容宽度随图形列数变化，
/// 需要把可用宽度平均分给固定列数，才能稳定做到「宽屏 2 张 / 窄屏换行」。
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

    public static readonly DependencyProperty MinCardWidthProperty =
        DependencyProperty.Register(
            nameof(MinCardWidth),
            typeof(double),
            typeof(WorkOrderCardPanel),
            new FrameworkPropertyMetadata(
                300.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty MaxColumnsProperty =
        DependencyProperty.Register(
            nameof(MaxColumns),
            typeof(int),
            typeof(WorkOrderCardPanel),
            new FrameworkPropertyMetadata(
                2,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>卡片之间的水平 / 垂直间距。</summary>
    public double ItemGap
    {
        get => (double)GetValue(ItemGapProperty);
        set => SetValue(ItemGapProperty, value);
    }

    /// <summary>窄屏时一张卡片至少需要的宽度，用来决定列数。</summary>
    public double MinCardWidth
    {
        get => (double)GetValue(MinCardWidthProperty);
        set => SetValue(MinCardWidthProperty, value);
    }

    /// <summary>最多几列；默认 2（宽屏一行两张）。</summary>
    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    protected override Size MeasureOverride(
        Size availableSize)
    {
        double width = availableSize.Width;

        if (double.IsInfinity(width) || width <= 0)
        {
            width = Math.Max(MinCardWidth, 1);
        }

        int columns = GetColumnCount(width);

        double itemWidth = GetItemWidth(width, columns);

        double totalHeight = 0;

        double rowHeight = 0;

        int itemsInRow = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

            itemsInRow++;

            if (itemsInRow == columns)
            {
                totalHeight += rowHeight + ItemGap;

                rowHeight = 0;

                itemsInRow = 0;
            }
        }

        if (itemsInRow > 0)
        {
            totalHeight += rowHeight;
        }
        else if (InternalChildren.Count > 0)
        {
            // 最后一行刚好排满时，上面多加了一次行间距，这里扣掉。
            totalHeight = Math.Max(0, totalHeight - ItemGap);
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(
        Size finalSize)
    {
        double width = finalSize.Width;

        if (double.IsInfinity(width) || width <= 0)
        {
            width = Math.Max(MinCardWidth, 1);
        }

        int columns = GetColumnCount(width);

        double itemWidth = GetItemWidth(width, columns);

        var row = new List<UIElement>(columns);

        double top = 0;

        for (int index = 0; index < InternalChildren.Count; index++)
        {
            row.Add(InternalChildren[index]);

            bool rowComplete = row.Count == columns;

            bool lastChild = index == InternalChildren.Count - 1;

            if (!rowComplete && !lastChild)
            {
                continue;
            }

            ArrangeRow(row, itemWidth, top);

            top += RowHeight(row) + ItemGap;

            row.Clear();
        }

        if (InternalChildren.Count > 0)
        {
            // 最后一行后面不需要行间距。
            top = Math.Max(0, top - ItemGap);
        }

        return new Size(width, top);
    }

    private void ArrangeRow(
        List<UIElement> row,
        double itemWidth,
        double top)
    {
        double rowHeight = RowHeight(row);

        for (int index = 0; index < row.Count; index++)
        {
            row[index].Arrange(new Rect(
                index * (itemWidth + ItemGap),
                top,
                itemWidth,
                rowHeight));
        }
    }

    private static double RowHeight(
        List<UIElement> row)
    {
        double height = 0;

        foreach (UIElement child in row)
        {
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return height;
    }

    /// <summary>可用宽度能放下几列：至少 1 列，最多 <see cref="MaxColumns"/> 列。</summary>
    private int GetColumnCount(
        double width)
    {
        int columns = Math.Max(1, MaxColumns);

        while (columns > 1 &&
               width < MinCardWidth * columns + ItemGap * (columns - 1))
        {
            columns--;
        }

        return columns;
    }

    private double GetItemWidth(
        double width,
        int columns)
    {
        // 层里只有一张卡时让它占满整行，避免右侧留出半行空白。
        if (InternalChildren.Count == 1)
        {
            return Math.Max(1, width);
        }

        return Math.Max(
            1,
            (width - ItemGap * (columns - 1)) / columns);
    }
}

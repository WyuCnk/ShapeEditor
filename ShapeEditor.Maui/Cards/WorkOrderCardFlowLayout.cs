using Microsoft.Maui.Layouts;

namespace ShapeEditor.Maui.Cards;

/// <summary>
/// 工序卡片内部的横向流式布局：子区块从左到右排列，一行放不下时自动换行。
///
/// <para>
/// 与普通 <c>FlexLayout</c> 的区别：这里<b>按布局实际可用宽度测量每个子区块</b>。
/// 某个区块（例如包含多个步骤节点的步骤组）自己比可用宽度还宽时，
/// 它会先在自己的内部折行，再作为一个整体参与外层换行；
/// 因此窄屏下卡片内容只会换行，不会被卡片边框裁切。
/// </para>
/// <para>
/// 这个类对应 WPF 的 <c>CardFlowPanel</c>。MAUI 的 FlexLayout 在
/// 「嵌套换行 + 固定 30px 图形」组合下，父级拿不到子级折行后的高度，
/// 容易出现区块重叠；自定义 Layout 可以明确按同一宽度测量 / 排布，
/// 与导出图使用的 <see cref="WorkOrderCardLayout"/> 规则一致。
/// </para>
/// <para>
/// 无限宽度测量（例如放在横向 ScrollView 里）会退化成「所有区块排成一行」，
/// 由调用方决定是否需要横向滚动。
/// </para>
/// </summary>
public sealed class WorkOrderCardFlowLayout : Layout
{
    /// <summary>同一行内区块之间的水平间距。</summary>
    public double ItemGap { get; set; } = 12;

    /// <summary>换行后两行之间的垂直间距。</summary>
    public double LineGap { get; set; } = 8;

    /// <summary>测量阶段使用的宽度上限，排布阶段在宽度无效时复用。</summary>
    private double _measuredLimit = double.PositiveInfinity;

    /// <summary>自定义布局自己负责测量与排布。</summary>
    protected override ILayoutManager CreateLayoutManager() =>
        new FlowLayoutManager(this);

    internal Size MeasureFlow(
        double widthConstraint,
        double heightConstraint) =>
        MeasureOverride(widthConstraint, heightConstraint);

    internal Size ArrangeFlow(
        Rect bounds) =>
        ArrangeOverride(bounds);

    private sealed class FlowLayoutManager : ILayoutManager
    {
        private readonly WorkOrderCardFlowLayout _owner;

        public FlowLayoutManager(
            WorkOrderCardFlowLayout owner)
        {
            _owner = owner;
        }

        public Size Measure(
            double widthConstraint,
            double heightConstraint) =>
            _owner.MeasureFlow(widthConstraint, heightConstraint);

        public Size ArrangeChildren(
            Rect bounds) =>
            _owner.ArrangeFlow(bounds);
    }

    protected override Size MeasureOverride(
        double widthConstraint,
        double heightConstraint)
    {
        _measuredLimit = GetLimit(widthConstraint);

        double lineWidth = 0;

        double lineHeight = 0;

        double totalWidth = 0;

        double totalHeight = 0;

        foreach (IView child in Children)
        {
            if (IsCollapsed(child))
            {
                child.Measure(0, 0);

                continue;
            }

            // 关键：按可用宽度测量子区块，过长时由区块自己折行。
            child.Measure(_measuredLimit, double.PositiveInfinity);

            Size desired = child.DesiredSize;

            if (desired.Width <= 0 && desired.Height <= 0)
            {
                continue;
            }

            if (lineWidth > 0 &&
                lineWidth + ItemGap + desired.Width > _measuredLimit)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);

                totalHeight += lineHeight + LineGap;

                lineWidth = 0;

                lineHeight = 0;
            }

            lineWidth += (lineWidth > 0 ? ItemGap : 0) + desired.Width;

            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        if (lineWidth > 0)
        {
            totalWidth = Math.Max(totalWidth, lineWidth);

            totalHeight += lineHeight;
        }

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(
        Rect bounds)
    {
        double limit = double.IsInfinity(bounds.Width) ||
                       bounds.Width <= 0
            ? _measuredLimit
            : bounds.Width;

        double x = 0;

        double top = 0;

        double lineHeight = 0;

        foreach (IView child in Children)
        {
            if (IsCollapsed(child))
            {
                child.Arrange(new Rect(0, 0, 0, 0));

                continue;
            }

            Size desired = child.DesiredSize;

            double itemWidth = Math.Min(desired.Width, limit);

            double itemHeight = desired.Height;

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

            child.Arrange(new Rect(
                bounds.X + x,
                bounds.Y + top,
                itemWidth,
                itemHeight));

            x += itemWidth;

            lineHeight = Math.Max(lineHeight, itemHeight);
        }

        return new Size(bounds.Width, top + lineHeight);
    }

    /// <summary>可用宽度无效时按无限宽处理，让所有区块排成一行。</summary>
    private static double GetLimit(
        double width) =>
        double.IsInfinity(width) || double.IsNaN(width) || width <= 0
            ? double.PositiveInfinity
            : width;

    private static bool IsCollapsed(
        IView view) =>
        view.Visibility == Visibility.Collapsed;
}

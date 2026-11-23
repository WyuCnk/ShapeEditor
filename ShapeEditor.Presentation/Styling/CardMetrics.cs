namespace ShapeEditor.Presentation.Styling;

/// <summary>
/// 工序卡片 / 节点链 / 节点预览共用的尺寸常量。
///
/// <para>
/// 这些数值以前在三个端各写一份：Web 的 <c>PathNodePreview</c>、<c>Workspace</c>、
/// <c>ProductionPathGraph</c>、<c>AngleWorkView</c>，WPF / MAUI 的卡片绘制代码里
/// 都散落着 30 / 50 / 16。现在只在这里定义一次：
/// </para>
/// <list type="bullet">
/// <item>竖式图单元格 30px：页面、导出图与节点预览都按 1:1 绘制，不做缩放；</item>
/// <item>节点标题区高度 50px；</item>
/// <item>节点左右内边距合计 16px。</item>
/// </list>
/// <para>
/// 注意与 Core 的 <c>GridMetrics</c> 区分：那里的 100×76 是「可缩放画布」的基准网格，
/// 这里的 30px 是卡片里固定 1:1 的预览尺寸，两者用途不同，不互相替代。
/// </para>
/// </summary>
public static class CardMetrics
{
    /// <summary>卡片内 1:1 竖式图的单元格边长。</summary>
    public const int CellSize = 30;

    /// <summary>节点标题区高度。</summary>
    public const int NodeHeaderHeight = 50;

    /// <summary>节点左右内边距合计：宽 = 象限数 × <see cref="CellSize"/> + 本值。</summary>
    public const int NodeHorizontalPadding = 16;

    /// <summary>节点宽度：象限数 × 单元格 + 左右内边距。</summary>
    public static double NodeWidth(
        int parts)
    {
        return parts * CellSize + NodeHorizontalPadding;
    }

    /// <summary>节点高度：标题区 + 层数 × 单元格。</summary>
    public static double NodeHeight(
        int layers)
    {
        return NodeHeaderHeight + layers * CellSize;
    }
}

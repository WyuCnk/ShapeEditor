using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 制作路径里一步的操作类型。
/// </summary>
public enum ProductionStepKind
{
    /// <summary>制作带柱子的半半边。</summary>
    PillarPiece,

    /// <summary>旋转（预旋转或交换后转回）。</summary>
    Rotate,

    /// <summary>交换。</summary>
    Exchange,

    /// <summary>交换得到目标图形。</summary>
    Target,

    /// <summary>顶针与堆叠（旧骨架的占位值，保留以兼容现有调用）。</summary>
    Pending,

    /// <summary>顶针。</summary>
    Pin,

    /// <summary>堆叠。</summary>
    Stack,

    /// <summary>堆叠用的可制作块。</summary>
    Block
}

/// <summary>
/// 旋转方向。旋转量取「较短方向」，相等时按顺时针的反转处理。
/// </summary>
public enum RotationDirection
{
    None,
    Clockwise,
    CounterClockwise
}

/// <summary>
/// 制作路径的一步。
///
/// <see cref="Shape"/> 是该步产出的图形，供界面直接画竖式图预览；
/// 没有图形的步骤（顶针与堆叠）为 null。
/// </summary>
public sealed record ProductionStep(
    string Title,
    ShapeDocument? Shape,
    ProductionStepKind Kind,
    RotationDirection RotationDirection,
    int RotationCount)
{
    /// <summary>
    /// 兼容旧调用：只给标题与图形，按「制作带柱子的半半边」处理。
    /// </summary>
    public ProductionStep(
        string title,
        ShapeDocument? shape)
        : this(
            title,
            shape,
            ProductionStepKind.PillarPiece,
            global::ShapeEditor.Core.Analysis.RotationDirection.None,
            0)
    {
    }
}

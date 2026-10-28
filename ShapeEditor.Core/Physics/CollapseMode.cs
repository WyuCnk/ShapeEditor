namespace ShapeEditor.Core.Physics;

/// <summary>
/// 下落时“受支撑”的判定方式。
/// </summary>
public enum CollapseMode
{
    /// <summary>
    /// 只从地面向上延伸（原 Collapse2）。
    /// </summary>
    FloorOnly,

    /// <summary>
    /// 地面 + 循环支撑结构，以及被循环结构托住的部分（原 Collapse1）。
    /// </summary>
    IncludeCycles
}

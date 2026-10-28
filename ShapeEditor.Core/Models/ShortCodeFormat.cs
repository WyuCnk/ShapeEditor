namespace ShapeEditor.Core.Models;

/// <summary>
/// 短代码的书写格式。
/// </summary>
public enum ShortCodeFormat
{
    /// <summary>
    /// 常规短代码：每两位表示一个象限，第一位是形状，第二位是颜色。
    /// 例如 CrP---Gu:ckckckck。
    /// </summary>
    Full,

    /// <summary>
    /// 超短代码：每一位表示一个象限，只描述形状，颜色使用默认值。
    /// 例如 CcCc 表示 图、晶、图、晶。
    /// </summary>
    Super
}

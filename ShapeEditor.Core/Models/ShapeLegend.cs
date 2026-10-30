namespace ShapeEditor.Core.Models;

/// <summary>
/// 超短代码相关说明文案，三端共用，避免各写各的。
/// </summary>
public static class ShapeLegend
{
    /// <summary>编码层说明：真正的超短代码字母。</summary>
    public const string Encoding =
        "C 普通形状、c 晶体、P 顶针、- 空位";

    /// <summary>显示层说明：工作区预览与导出图的字母。</summary>
    public const string Display =
        "c 晶体、S 普通形状、P 顶针、- 空位";

    public const string Full =
        "超短代码每位一个象限，冒号分层。编码用 " + Encoding +
        "；工作区预览与导出图显示为 " + Display + "。";
}

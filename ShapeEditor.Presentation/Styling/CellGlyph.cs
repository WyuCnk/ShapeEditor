using ShapeEditor.Core.Models;

namespace ShapeEditor.Presentation.Styling;

/// <summary>
/// 竖式图单元格的字形与种类名。
///
/// <para>
/// 「图 / 晶」两个字以前在 WPF 的 <c>ShapeTheme</c>、MAUI 的 <c>ShapeGridDrawable</c> /
/// <c>ShapeImageExporter</c>、Web 的页面里各写一份；单元格种类名（empty / pin /
/// crystal / shape）也重复出现在 Web 的绘制数据里。这里统一成三端共用的一份。
/// </para>
/// <para>
/// 顶针与空格不画字，<see cref="For"/> 对它们返回空字符串；
/// 调用方需要判断「是否有字」时用 <see cref="HasText"/>。
/// </para>
/// </summary>
public static class CellGlyph
{
    /// <summary>形状单元格的字形。</summary>
    public const string ShapeText = "图";

    /// <summary>晶体单元格的字形。</summary>
    public const string CrystalText = "晶";

    /// <summary>
    /// 单元格类型对应的字形：形状「图」、晶体「晶」，其余为空。
    /// </summary>
    public static string For(
        CellType type)
    {
        return type switch
        {
            CellType.Shape => ShapeText,
            CellType.Crystal => CrystalText,
            _ => ""
        };
    }

    /// <summary>只有形状和晶体需要画字。</summary>
    public static bool HasText(
        CellType type)
    {
        return type is CellType.Shape or CellType.Crystal;
    }

    /// <summary>
    /// 传给 Web 端 shape-grid.js 的单元格种类名，需与 JS 里的分支保持一致。
    /// </summary>
    public static string KindName(
        CellType type)
    {
        return type switch
        {
            CellType.Empty => "empty",
            CellType.Pin => "pin",
            CellType.Crystal => "crystal",
            CellType.Shape => "shape",
            _ => "empty"
        };
    }
}

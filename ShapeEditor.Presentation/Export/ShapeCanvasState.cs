using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;
using ShapeEditor.Presentation.Styling;

namespace ShapeEditor.Presentation.Export;

/// <summary>
/// 传给 shape-grid.js 的绘制数据（页面预览与导出图共用同一份快照）。
///
/// <para>
/// 属性会按 camelCase 序列化，需与 shape-grid.js 中的字段名保持一致；
/// 类型从 Web 迁到 Presentation 只换了命名空间，属性名与顺序不变，
/// 因此 Web 端 JSON 与迁移前逐字相同。
/// </para>
/// </summary>
public sealed class ShapeCanvasState
{
    public int Rows { get; init; }

    public int Columns { get; init; }

    public int? SelectedLayer { get; init; }

    public int? SelectedPart { get; init; }

    public float Zoom { get; init; } = 1;

    public float PanX { get; init; }

    public float PanY { get; init; }

    /// <summary>
    /// 调色板与尺寸常量，来自 ShapeEditor.Core。
    /// </summary>
    public ShapeThemeData Theme { get; init; } = ShapeThemeData.Default;

    public List<ShapeCanvasCell> Cells { get; init; } = new();

    /// <summary>
    /// 竖式图的第一行是最高层，因此行号需要反向映射。
    /// </summary>
    public static ShapeCanvasState Create(
        ShapeDocument document)
    {
        var state = new ShapeCanvasState
        {
            Rows = document.LayerCount,
            Columns = document.PartCount
        };

        for (int visualRow = 0;
             visualRow < document.LayerCount;
             visualRow++)
        {
            int layer =
                document.LayerCount - 1 - visualRow;

            for (int part = 0;
                 part < document.PartCount;
                 part++)
            {
                ShapeCell cell =
                    document[layer, part];

                state.Cells.Add(
                    new ShapeCanvasCell
                    {
                        Type = CellGlyph.KindName(cell.Type),
                        ColorCode = cell.ColorCode,
                        Text = cell.ToDisplayText()
                    });
            }
        }

        return state;
    }
}

/// <summary>绘制数据里的一个单元格。</summary>
public sealed class ShapeCanvasCell
{
    public string Type { get; init; } = "empty";

    public char? ColorCode { get; init; }

    public string Text { get; init; } = "";
}

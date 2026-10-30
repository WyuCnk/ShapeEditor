using System.Text;

namespace ShapeEditor.Core.Models;

/// <summary>
/// 把图形渲染成"超短代码文本"，工作区节点与导出图共用。
///
/// 显示字母：c 晶体、S 普通形状、P 顶针、- 空位。
/// </summary>
public static class ShapeText
{
    /// <summary>
    /// 逐层文本，第一行是最低层（与竖式图一致：最高层在上）。
    /// </summary>
    public static IReadOnlyList<string> Lines(
        ShapeDocument document,
        int maxLines = 0)
    {
        var lines = new List<string>();

        int layers = document.LayerCount;

        for (int layer = layers - 1; layer >= 0; layer--)
        {
            if (maxLines > 0 && lines.Count >= maxLines)
            {
                break;
            }

            var builder = new StringBuilder();

            for (int part = 0; part < document.PartCount; part++)
            {
                builder.Append(LetterOf(document[layer, part]));
            }

            lines.Add(builder.ToString());
        }

        return lines;
    }

    public static char LetterOf(
        ShapeCell cell)
    {
        return cell.Type switch
        {
            CellType.Crystal => 'c',
            CellType.Pin => 'P',
            CellType.Shape => 'S',
            _ => '-'
        };
    }

    /// <summary>
    /// 一行摘要：层数 × 象限数 · 晶体数 · 顶针数。
    /// </summary>
    public static string Summary(
        ShapeDocument document)
    {
        int crystals = 0;
        int pins = 0;
        int shapes = 0;

        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            for (int part = 0; part < document.PartCount; part++)
            {
                switch (document[layer, part].Type)
                {
                    case CellType.Crystal:
                        crystals++;
                        break;

                    case CellType.Pin:
                        pins++;
                        break;

                    case CellType.Shape:
                        shapes++;
                        break;
                }
            }
        }

        return $"{document.LayerCount} 层 × {document.PartCount} 象限";
    }

    public static bool IsTruncated(
        ShapeDocument document,
        int maxLines) =>
        maxLines > 0 && document.LayerCount > maxLines;
}

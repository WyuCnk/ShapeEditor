using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;

namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 交换：两个输入各切割一次，再交叉拼接。
///
/// A → A东、A西；B → B东、B西
/// 输出1 = A东 + B西
/// 输出2 = A西 + B东
/// </summary>
public static class ShapeExchange
{
    public static (ShapeDocument? First, ShapeDocument? Second, string? Error) Exchange(
        ShapeDocument first,
        ShapeDocument second,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        if (first.PartCount != second.PartCount)
        {
            return (null, null, "两个输入的象限数不一致。");
        }

        CutResult firstCut = ShapePhysics.Cut(first, mode);
        CutResult secondCut = ShapePhysics.Cut(second, mode);

        if (!firstCut.Success || !secondCut.Success ||
            firstCut.West is null || firstCut.East is null ||
            secondCut.West is null || secondCut.East is null)
        {
            return (null, null, firstCut.Error ?? secondCut.Error ?? "切割失败。");
        }

        ShapeDocument? merged1 = Merge(firstCut.East, secondCut.West);

        ShapeDocument? merged2 = Merge(firstCut.West, secondCut.East);

        if (merged1 is null || merged2 is null)
        {
            return (null, null, "拼接失败。");
        }

        return (merged1, merged2, null);
    }

    /// <summary>
    /// 把两份半边形的非空单元合成一份：某列在 left 有内容就用 left，否则用 right。
    /// </summary>
    public static ShapeDocument? Merge(
        ShapeDocument left,
        ShapeDocument right)
    {
        if (left.LayerCount != right.LayerCount ||
            left.PartCount != right.PartCount)
        {
            return null;
        }

        int layers = left.LayerCount;
        int parts = left.PartCount;

        var merged = new ShapeDocument(layers, parts);

        for (int layer = 0; layer < layers; layer++)
        {
            for (int part = 0; part < parts; part++)
            {
                ShapeCell cell = left[layer, part];

                if (cell.Type == CellType.Empty)
                {
                    cell = right[layer, part];
                }

                merged[layer, part] = cell.Clone();
            }
        }

        return merged;
    }
}

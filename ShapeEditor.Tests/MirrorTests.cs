using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Tests;

/// <summary>
/// 图形镜像原语：左右镜像（相邻象限成对交换）与上下镜像（层序反转）。
/// 每个尺寸都覆盖两份不同的图形，并逐格断言类型 / 形状码 / 颜色。
/// </summary>
internal static class MirrorTests
{
    /// <summary>可用的普通形状码，跳过顶针专用的 P。</summary>
    private const string ShapeCodes = "ABCDEFGHIJKLMNOQRSTUVWXYZ";

    private static readonly char[] ColorCodes =
    {
        'r', 'g', 'b', 'c', 'm', 'y', 'w', 'k', 'u'
    };

    public static void MirrorHorizontalFourQuadrants()
    {
        RunHorizontalExamples(
            layers: 4,
            parts: 4,
            name: "n=4");
    }

    public static void MirrorVerticalFourQuadrants()
    {
        RunVerticalExamples(
            layers: 4,
            parts: 4,
            name: "n=4");
    }

    public static void MirrorHorizontalSixQuadrants()
    {
        RunHorizontalExamples(
            layers: 4,
            parts: 6,
            name: "n=6");
    }

    public static void MirrorVerticalSixQuadrants()
    {
        RunVerticalExamples(
            layers: 4,
            parts: 6,
            name: "n=6");
    }

    private static void RunHorizontalExamples(
        int layers,
        int parts,
        string name)
    {
        foreach ((string example, ShapeDocument source) in CreateExamples(layers, parts))
        {
            AssertHorizontal(source, $"{name}·{example}");
        }
    }

    private static void RunVerticalExamples(
        int layers,
        int parts,
        string name)
    {
        foreach ((string example, ShapeDocument source) in CreateExamples(layers, parts))
        {
            AssertVertical(source, $"{name}·{example}");
        }
    }

    /// <summary>
    /// 每个尺寸两份样例：一份逐格全为普通形状，一份混有晶体 / 顶针 / 空格。
    /// </summary>
    private static IEnumerable<(string Name, ShapeDocument Document)> CreateExamples(
        int layers,
        int parts)
    {
        yield return ("逐格普通形状", CreateDense(layers, parts));
        yield return ("混合单元格", CreateMixed(layers, parts));
    }

    private static ShapeDocument CreateDense(
        int layers,
        int parts)
    {
        var document = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                int index = layer * parts + part;

                document[layer, part] = ShapeCell.Shape(
                    ShapeCodes[index % ShapeCodes.Length],
                    ColorCodes[index % ColorCodes.Length]);
            }
        }

        return document;
    }

    private static ShapeDocument CreateMixed(
        int layers,
        int parts)
    {
        var document = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                int index = layer * parts + part;

                document[layer, part] = (index % 4) switch
                {
                    0 => ShapeCell.Empty(),
                    1 => ShapeCell.Pin(),
                    2 => ShapeCell.Crystal(
                        ColorCodes[index % ColorCodes.Length]),
                    _ => ShapeCell.Shape(
                        ShapeCodes[index % ShapeCodes.Length],
                        ColorCodes[(index + 3) % ColorCodes.Length])
                };
            }
        }

        return document;
    }

    private static void AssertHorizontal(
        ShapeDocument source,
        string name)
    {
        ShapeDocument original = source.Clone();

        ShapeDocument mirrored = ShapeOps.MirrorHorizontal(source);

        Assert.Equal(source.LayerCount, mirrored.LayerCount, $"{name}：层数不应改变");
        Assert.Equal(source.PartCount, mirrored.PartCount, $"{name}：象限数不应改变");

        for (int layer = 0;
             layer < source.LayerCount;
             layer++)
        {
            for (int part = 0;
                 part < source.PartCount;
                 part++)
            {
                // 左右镜像把 q 与 q^1 成对交换。
                int paired = part ^ 1;

                Assert.True(
                    ShapeOps.SameCell(
                        source[layer, paired],
                        mirrored[layer, part]),
                    $"{name}：第 {layer + 1} 层第 {part + 1} 象限应来自原第 {paired + 1} 象限，" +
                    $"实际为 {mirrored[layer, part].ToShortCode()}，" +
                    $"期望 {source[layer, paired].ToShortCode()}");
            }
        }

        Assert.True(
            ShapeOps.Same(source, original),
            $"{name}：左右镜像不应修改入参");

        Assert.True(
            ShapeOps.Same(
                source,
                ShapeOps.MirrorHorizontal(
                    ShapeOps.MirrorHorizontal(source))),
            $"{name}：左右镜像两次应还原");
    }

    private static void AssertVertical(
        ShapeDocument source,
        string name)
    {
        ShapeDocument original = source.Clone();

        ShapeDocument mirrored = ShapeOps.MirrorVertical(source);

        Assert.Equal(source.LayerCount, mirrored.LayerCount, $"{name}：层数不应改变");
        Assert.Equal(source.PartCount, mirrored.PartCount, $"{name}：象限数不应改变");

        for (int layer = 0;
             layer < source.LayerCount;
             layer++)
        {
            int mirroredLayer =
                source.LayerCount - 1 - layer;

            for (int part = 0;
                 part < source.PartCount;
                 part++)
            {
                // 上下镜像把层序整体反转，同一象限内的位置不变。
                Assert.True(
                    ShapeOps.SameCell(
                        source[mirroredLayer, part],
                        mirrored[layer, part]),
                    $"{name}：第 {layer + 1} 层第 {part + 1} 象限应来自原第 {mirroredLayer + 1} 层，" +
                    $"实际为 {mirrored[layer, part].ToShortCode()}，" +
                    $"期望 {source[mirroredLayer, part].ToShortCode()}");
            }
        }

        Assert.True(
            ShapeOps.Same(source, original),
            $"{name}：上下镜像不应修改入参");

        Assert.True(
            ShapeOps.Same(
                source,
                ShapeOps.MirrorVertical(
                    ShapeOps.MirrorVertical(source))),
            $"{name}：上下镜像两次应还原");
    }
}

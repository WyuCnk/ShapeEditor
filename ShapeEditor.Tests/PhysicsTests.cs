using ShapeEditor.Core;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 物理操作：下落、堆叠、顶针、切割。
/// </summary>
internal static class PhysicsTests
{
    #region 下落

    public static void FloatingCellFallsToFloor()
    {
        ShapeDocument document = Create(4, 4);

        document[3, 0] = ShapeCell.Shape('C', 'r');

        ShapeDocument result =
            ShapePhysics.Collapse(document).Document!;

        Assert.Equal(CellType.Shape, result[0, 0].Type);
        Assert.Equal(CellType.Empty, result[3, 0].Type);
    }

    public static void SupportedColumnStays()
    {
        ShapeDocument document = Create(4, 4);

        document[0, 0] = ShapeCell.Shape('C', 'r');
        document[1, 0] = ShapeCell.Shape('C', 'g');

        ShapeDocument result =
            ShapePhysics.Collapse(document).Document!;

        Assert.Equal(CellType.Shape, result[0, 0].Type);
        Assert.Equal(CellType.Shape, result[1, 0].Type);
    }

    public static void FloatingTowerFallsTogether()
    {
        ShapeDocument document = Create(4, 4);

        document[2, 0] = ShapeCell.Shape('C', 'r');
        document[3, 0] = ShapeCell.Shape('C', 'g');

        ShapeDocument result =
            ShapePhysics.Collapse(document).Document!;

        Assert.Equal(CellType.Shape, result[0, 0].Type);
        Assert.Equal(CellType.Shape, result[1, 0].Type);
        Assert.Equal(CellType.Empty, result[2, 0].Type);
    }

    public static void FallingCrystalDisappears()
    {
        ShapeDocument document = Create(4, 4);

        document[3, 0] = ShapeCell.Crystal('r');

        ShapeDocument result =
            ShapePhysics.Collapse(document).Document!;

        Assert.Equal(CellType.Empty, result[0, 0].Type);
        Assert.Equal(CellType.Empty, result[3, 0].Type);
    }

    public static void CollapseIsIdempotent()
    {
        ShapeDocument document = Create(4, 4);

        document[3, 0] = ShapeCell.Shape('C', 'r');
        document[3, 1] = ShapeCell.Shape('C', 'g');

        ShapeDocument once =
            ShapePhysics.Collapse(document).Document!;

        ShapeDocument twice =
            ShapePhysics.Collapse(once).Document!;

        for (int layer = 0; layer < once.LayerCount; layer++)
        {
            for (int part = 0; part < once.PartCount; part++)
            {
                Assert.Equal(
                    once[layer, part].Type,
                    twice[layer, part].Type);
            }
        }
    }

    public static void CycleSupportKeepsFloatingShape()
    {
        var parser = new ShortCodeParser();

        ParseResult parsed = parser.Parse(
            "------Cu:--------:----Cucr:----P-cr:----Cucr",
            5,
            4);

        Assert.True(parsed.Success, parsed.Error ?? "解析失败");

        ShapeDocument document = parsed.Document!;

        ShapeDocument withCycles = ShapePhysics
            .Collapse(document, CollapseMode.IncludeCycles)
            .Document!;

        ShapeDocument floorOnly = ShapePhysics
            .Collapse(document, CollapseMode.FloorOnly)
            .Document!;

        bool differs = false;

        for (int layer = 0; layer < document.LayerCount; layer++)
        {
            for (int part = 0; part < document.PartCount; part++)
            {
                if (withCycles[layer, part].Type !=
                    floorOnly[layer, part].Type)
                {
                    differs = true;
                }
            }
        }

        Assert.True(differs, "含环规则与仅地面规则应当给出不同结果");

        // 环结构整体悬空：第 2 层右侧的晶体不应掉到地面
        Assert.Equal(CellType.Crystal, withCycles[2, 3].Type);
    }

    #endregion

    #region 堆叠

    public static void StackRejectsDifferentPartCount()
    {
        PhysicsResult result = ShapePhysics.Stack(
            Create(2, 4),
            Create(2, 6),
            4);

        Assert.False(result.Success, "象限数不一致应当失败");
    }

    public static void StackClearsTopCrystalsAndUsesLimit()
    {
        ShapeDocument baseDocument = Create(1, 4);

        baseDocument[0, 1] = ShapeCell.Shape('C', 'r');

        ShapeDocument topDocument = Create(1, 4);

        topDocument[0, 0] = ShapeCell.Crystal('r');
        topDocument[0, 1] = ShapeCell.Shape('C', 'g');

        PhysicsResult result = ShapePhysics.Stack(
            baseDocument,
            topDocument,
            3);

        Assert.True(result.Success, result.Error ?? "堆叠失败");

        ShapeDocument merged = result.Document!;

        Assert.Equal(3, merged.LayerCount);

        // 上方图形里的晶体应当被清空
        for (int layer = 0; layer < merged.LayerCount; layer++)
        {
            for (int part = 0; part < merged.PartCount; part++)
            {
                Assert.NotEqual(
                    CellType.Crystal,
                    merged[layer, part].Type);
            }
        }

        Assert.Equal(CellType.Shape, merged[0, 1].Type);
    }

    public static void StackTruncatesAboveLimit()
    {
        ShapeDocument baseDocument = Create(1, 4);

        baseDocument[0, 0] = ShapeCell.Shape('C', 'r');

        ShapeDocument topDocument = Create(3, 4);

        topDocument[0, 0] = ShapeCell.Shape('C', 'g');
        topDocument[1, 0] = ShapeCell.Shape('C', 'b');
        topDocument[2, 0] = ShapeCell.Shape('C', 'y');

        PhysicsResult result = ShapePhysics.Stack(
            baseDocument,
            topDocument,
            3);

        Assert.True(result.Success, result.Error ?? "堆叠失败");

        ShapeDocument merged = result.Document!;

        Assert.Equal(3, merged.LayerCount);
        Assert.Equal(CellType.Shape, merged[0, 0].Type);
        Assert.Equal(CellType.Shape, merged[1, 0].Type);
        Assert.Equal(CellType.Shape, merged[2, 0].Type);
    }

    #endregion

    #region 顶针

    public static void PinAddsPinsAtBottom()
    {
        ShapeDocument document = Create(2, 4);

        document[0, 0] = ShapeCell.Shape('C', 'r');
        document[1, 0] = ShapeCell.Shape('C', 'g');

        PhysicsResult result = ShapePhysics.Pin(document);

        Assert.True(result.Success, result.Error ?? "顶针失败");

        ShapeDocument pinned = result.Document!;

        Assert.Equal(2, pinned.LayerCount);
        Assert.Equal(CellType.Pin, pinned[0, 0].Type);
        Assert.Equal(CellType.Shape, pinned[1, 0].Type);
    }

    public static void PinDropsTopLayerOverLimit()
    {
        ShapeDocument document = Create(2, 4);

        document[0, 0] = ShapeCell.Shape('C', 'r');
        document[1, 0] = ShapeCell.Crystal('g');

        PhysicsResult result = ShapePhysics.Pin(document);

        Assert.True(result.Success, result.Error ?? "顶针失败");

        ShapeDocument pinned = result.Document!;

        Assert.Equal(2, pinned.LayerCount);
        Assert.Equal(CellType.Pin, pinned[0, 0].Type);

        // 顶层晶体上移后越界，会被截掉
        for (int layer = 0; layer < pinned.LayerCount; layer++)
        {
            Assert.NotEqual(
                CellType.Crystal,
                pinned[layer, 0].Type);
        }
    }

    #endregion

    #region 切割

    public static void CutReturnsBothHalves()
    {
        ShapeDocument document = Create(1, 4);

        document[0, 0] = ShapeCell.Shape('C', 'r');
        document[0, 1] = ShapeCell.Shape('C', 'g');
        document[0, 2] = ShapeCell.Shape('C', 'b');
        document[0, 3] = ShapeCell.Shape('C', 'y');

        CutResult result = ShapePhysics.Cut(document);

        Assert.True(result.Success, result.Error ?? "切割失败");

        Assert.Equal(CellType.Shape, result.West![0, 0].Type);
        Assert.Equal(CellType.Shape, result.West[0, 1].Type);
        Assert.Equal(CellType.Empty, result.West[0, 2].Type);

        Assert.Equal(CellType.Shape, result.East![0, 2].Type);
        Assert.Equal(CellType.Shape, result.East[0, 3].Type);
        Assert.Equal(CellType.Empty, result.East[0, 0].Type);
    }

    public static void CutKeepsPartCount()
    {
        ShapeDocument document = Create(2, 6);

        document[0, 0] = ShapeCell.Shape('C', 'r');

        CutResult result = ShapePhysics.Cut(document);

        Assert.Equal(6, result.West!.PartCount);
        Assert.Equal(6, result.East!.PartCount);
    }

    public static void CutBreaksSpanningCrystal()
    {
        ShapeDocument document = Create(1, 4);

        // 跨越中线（part 1 与 part 2）的晶体，应当整块破碎
        document[0, 1] = ShapeCell.Crystal('r');
        document[0, 2] = ShapeCell.Crystal('r');

        CutResult result = ShapePhysics.Cut(document);

        Assert.Equal(CellType.Empty, result.West![0, 1].Type);
        Assert.Equal(CellType.Empty, result.East![0, 2].Type);
    }

    #endregion

    #region 规则

    public static void OddPartCountIsRejected()
    {
        ShapeDocument document = Create(2, 3);

        Assert.False(ShapePhysics.Collapse(document).Success);

        Assert.False(
            ShapePhysics.Stack(document, document, 4).Success);

        Assert.False(ShapePhysics.Cut(document).Success);
    }

    public static void NormalizePartCountRoundsUpToEven()
    {
        Assert.Equal(4, ShapeRules.NormalizePartCount(3));
        Assert.Equal(6, ShapeRules.NormalizePartCount(5));
        Assert.Equal(4, ShapeRules.NormalizePartCount(4));
        Assert.Equal(100, ShapeRules.NormalizePartCount(99));
        Assert.Equal(100, ShapeRules.NormalizePartCount(1000));
        Assert.Equal(2, ShapeRules.NormalizePartCount(1));
    }

    #endregion

    private static ShapeDocument Create(
        int layers,
        int parts)
    {
        return new ShapeDocument(layers, parts);
    }
}

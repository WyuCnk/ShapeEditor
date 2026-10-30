using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Physics;

/// <summary>
/// 图形物理操作：下落、堆叠、顶针、切割。
///
/// 全部是纯函数，不修改入参，返回新的文档。
/// 颜色会被保留；只有“下落消晶”和“晶体破碎”会让晶体消失。
/// </summary>
public static class ShapePhysics
{
    #region 下落

    public static PhysicsResult Collapse(
        ShapeDocument document,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        if (document is null)
        {
            return PhysicsResult.Fail("图形为空。");
        }

        if (!ShapeRules.IsValidPartCount(document.PartCount))
        {
            return PhysicsResult.Fail("象限数必须为偶数。");
        }

        return PhysicsResult.Ok(
            PhysicsGrid.Collapse(document, mode));
    }

    #endregion

    /// <summary>
    /// 超上限删除：先让超出上限的晶体整块破碎，再截断。
    /// </summary>
    public static PhysicsResult Limit(
        ShapeDocument document,
        int heightLimit)
    {
        if (document is null)
        {
            return PhysicsResult.Fail("图形为空。");
        }

        if (!ShapeRules.IsValidLayerCount(heightLimit))
        {
            return PhysicsResult.Fail("层数上限无效。");
        }

        return PhysicsResult.Ok(
            PhysicsGrid.Truncate(document, heightLimit));
    }

    #region 堆叠

    /// <summary>
    /// 把 top 堆到 base 上。
    ///
    /// 规则：
    /// 1. 两者象限数必须一致；
    /// 2. top 的晶体先被清空；
    /// 3. 中间空一层拼接成临时图形；
    /// 4. 对临时图形执行一次下落，再执行一次超上限删除；
    /// 5. 没有兜底下落（截断碎晶造成的悬空就是游戏原样）。
    /// </summary>
    public static PhysicsResult Stack(
        ShapeDocument baseDocument,
        ShapeDocument topDocument,
        int heightLimit,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        if (baseDocument is null || topDocument is null)
        {
            return PhysicsResult.Fail("参与堆叠的图形为空。");
        }

        if (baseDocument.PartCount != topDocument.PartCount)
        {
            return PhysicsResult.Fail(
                $"象限数不一致：下方 {baseDocument.PartCount}，上方 {topDocument.PartCount}。");
        }

        int parts = baseDocument.PartCount;

        if (!ShapeRules.IsValidPartCount(parts))
        {
            return PhysicsResult.Fail("象限数必须为偶数。");
        }

        if (!ShapeRules.IsValidLayerCount(heightLimit))
        {
            return PhysicsResult.Fail(
                $"层数上限必须在 {ShapeRules.MinLayerCount}–{ShapeRules.MaxLayerCount} 之间。");
        }

        int baseLayers = baseDocument.LayerCount;
        int topLayers = topDocument.LayerCount;

        int totalLayers = baseLayers + 1 + topLayers;

        var temporary = new ShapeDocument(
            totalLayers,
            parts);

        // 下方图形原样放在底部
        for (int layer = 0;
             layer < baseLayers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                temporary[layer, part] =
                    baseDocument[layer, part].Clone();
            }
        }

        // 中间留出一层空层，上方图形从 baseLayers + 1 开始原样拼上
        for (int layer = 0;
             layer < topLayers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                temporary[baseLayers + 1 + layer, part] =
                    topDocument[layer, part].Clone();
            }
        }

        ShapeDocument collapsed =
            PhysicsGrid.Collapse(temporary, mode);

        ShapeDocument truncated =
            PhysicsGrid.Truncate(collapsed, heightLimit);

        return PhysicsResult.Ok(truncated);
    }

    #endregion

    #region 顶针

    /// <summary>
    /// 顶针：内容整体上移一层，底层按原第 0 层的非空位置放顶针。
    ///
    /// 使用图形自身的层数作为上限：
    /// 上移后超出上限的那一层会先触发晶体破碎，再被删除；
    /// 最后执行一次下落。
    /// </summary>
    public static PhysicsResult Pin(
        ShapeDocument document,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        if (document is null)
        {
            return PhysicsResult.Fail("图形为空。");
        }

        int parts = document.PartCount;
        int layers = document.LayerCount;

        if (!ShapeRules.IsValidPartCount(parts))
        {
            return PhysicsResult.Fail("象限数必须为偶数。");
        }

        var temporary = new ShapeDocument(
            layers + 1,
            parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                temporary[layer + 1, part] =
                    document[layer, part].Clone();
            }
        }

        for (int part = 0;
             part < parts;
             part++)
        {
            if (document[0, part].Type != CellType.Empty)
            {
                temporary[0, part] = ShapeCell.Pin();
            }
        }

        ShapeDocument truncated =
            PhysicsGrid.Truncate(temporary, layers);

        ShapeDocument collapsed =
            PhysicsGrid.Collapse(truncated, mode);

        return PhysicsResult.Ok(collapsed);
    }

    #endregion

    #region 切割

    /// <summary>
    /// 切割：沿中线把图形分成东西两半，两半都返回。
    ///
    /// 跨切线的晶体连通块会整块破碎（两半里都不再出现）；
    /// 只在一侧的晶体保留。输出仍使用原象限数，另一半为空。
    /// </summary>
    public static CutResult Cut(
        ShapeDocument document,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        if (document is null)
        {
            return CutResult.Fail("图形为空。");
        }

        int layers = document.LayerCount;
        int parts = document.PartCount;

        if (!ShapeRules.IsValidPartCount(parts))
        {
            return CutResult.Fail("象限数必须为偶数。");
        }

        int half = parts / 2;

        ShapeCell[,] cells =
            PhysicsGrid.Clone(document);

        PhysicsGrid.BreakSpanningCrystals(
            cells,
            layers,
            parts,
            half);

        ShapeDocument west = PhysicsGrid.Collapse(
            PhysicsGrid.TakeHalf(cells, layers, parts, 0, half),
            mode);

        ShapeDocument east = PhysicsGrid.Collapse(
            PhysicsGrid.TakeHalf(cells, layers, parts, half, parts),
            mode);

        return CutResult.Ok(west, east);
    }

    #endregion
}

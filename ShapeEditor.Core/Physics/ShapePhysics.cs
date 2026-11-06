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

    #region 结晶

    /// <summary>
    /// 结晶：把所有空位与顶针替换成指定颜色的晶体，其他格保持不变
    /// （普通形状、已有晶体及其颜色都不动）。
    ///
    /// <para>
    /// <b>使用前提（规格 §5.6）</b>：这个原语是<b>全局</b>的——它会填满整张图里的
    /// 每一个空位和顶针。实际制作时只应在
    /// 「此前的层已经填满、当前层只缺目标格」的地方使用它，也就是先做出
    /// 「除目标象限外全满」的单层件、再对它结晶，然后把这一层堆叠上去。
    /// 若直接对一张还有图下间隙、晶下间隙或辅助顶针的图形结晶，
    /// 那些本该保留的空位/顶针会一起变成晶体。
    /// </para>
    ///
    /// <para>
    /// <paramref name="mode"/> 仅为与 <see cref="Collapse"/>、<see cref="Stack"/>、
    /// <see cref="Pin"/>、<see cref="Cut"/> 保持一致的调用形式；结晶本身不执行下落。
    /// </para>
    /// </summary>
    public static PhysicsResult Crystallize(
        ShapeDocument document,
        char color,
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

        if (!ShapeColor.IsValid(color))
        {
            return PhysicsResult.Fail($"颜色代码无效：{color}。");
        }

        int layers = document.LayerCount;
        int parts = document.PartCount;

        var result = new ShapeDocument(layers, parts);

        for (int layer = 0;
             layer < layers;
             layer++)
        {
            for (int part = 0;
                 part < parts;
                 part++)
            {
                ShapeCell cell = document[layer, part];

                result[layer, part] =
                    cell.Type is CellType.Empty or CellType.Pin
                        ? ShapeCell.Crystal(color)
                        : cell.Clone();
            }
        }

        // mode 不参与结晶；保留参数只为统一物理原语的签名。
        _ = mode;

        return PhysicsResult.Ok(result);
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

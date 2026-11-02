namespace ShapeEditor.Core.Analysis;

/// <summary>
/// 图形的性质。一个图形可以同时具备多项，用位标志表示，便于扩展。
/// </summary>
[Flags]
public enum ShapeProperty
{
    None = 0,

    /// <summary>包含晶体。</summary>
    ContainsCrystal = 1 << 0,

    /// <summary>包含顶针。</summary>
    ContainsPin = 1 << 1,

    /// <summary>同一层里同时存在晶体和空位。</summary>
    CrystalAndEmptySameLayer = 1 << 2,

    /// <summary>同一层里同时存在晶体和顶针。</summary>
    CrystalAndPinSameLayer = 1 << 3,

    /// <summary>图下间隙：空位正上方是普通形状，且更上方存在晶体。</summary>
    GapBelowShape = 1 << 4,

    /// <summary>晶下间隙：空位正上方是晶体。</summary>
    GapBelowCrystal = 1 << 5,

    /// <summary>同一层里存在不同颜色的晶体。</summary>
    MixedCrystalColorsSameLayer = 1 << 6,

    /// <summary>执行一次下落后图形不变。</summary>
    Stable = 1 << 7,

    /// <summary>存在合法的交换方向。</summary>
    Exchangeable = 1 << 8,

    /// <summary>含循环支撑结构（仅在含循环支撑规则下成立）。</summary>
    CycleSupported = 1 << 9
}

/// <summary>
/// 图形性质分析。可制作性依赖制作路径算法，暂不在这里判定。
/// </summary>
public static class ShapePropertyAnalyzer
{
    public static ShapeProperty Analyze(
        Models.ShapeDocument document,
        bool isStable,
        bool isExchangeable,
        Physics.CollapseMode mode = Physics.CollapseMode.IncludeCycles)
    {
        ShapeProperty properties = ShapeProperty.None;

        int layers = document.LayerCount;
        int parts = document.PartCount;

        for (int layer = 0; layer < layers; layer++)
        {
            bool hasCrystal = false;
            bool hasEmpty = false;
            bool hasPin = false;
            char? crystalColor = null;
            bool mixedColors = false;

            for (int part = 0; part < parts; part++)
            {
                Models.ShapeCell cell = document[layer, part];

                switch (cell.Type)
                {
                    case Models.CellType.Crystal:
                        hasCrystal = true;

                        properties |= ShapeProperty.ContainsCrystal;

                        if (crystalColor is null)
                        {
                            crystalColor = cell.ColorCode;
                        }
                        else if (cell.ColorCode != crystalColor)
                        {
                            mixedColors = true;
                        }

                        break;

                    case Models.CellType.Pin:
                        hasPin = true;

                        properties |= ShapeProperty.ContainsPin;

                        break;

                    case Models.CellType.Empty:
                        hasEmpty = true;

                        break;
                }
            }

            if (hasCrystal && hasEmpty)
            {
                properties |= ShapeProperty.CrystalAndEmptySameLayer;
            }

            if (hasCrystal && hasPin)
            {
                properties |= ShapeProperty.CrystalAndPinSameLayer;
            }

            if (mixedColors)
            {
                properties |= ShapeProperty.MixedCrystalColorsSameLayer;
            }
        }

        // 角内间隙：逐列、逐空位判断
        for (int part = 0; part < parts; part++)
        {
            for (int layer = 0; layer < layers; layer++)
            {
                if (document[layer, part].Type != Models.CellType.Empty)
                {
                    continue;
                }

                int above = layer + 1;

                if (above >= layers)
                {
                    continue;
                }

                Models.CellType aboveType = document[above, part].Type;

                if (aboveType == Models.CellType.Crystal)
                {
                    properties |= ShapeProperty.GapBelowCrystal;
                }
                else if (aboveType == Models.CellType.Shape)
                {
                    for (int higher = above + 1; higher < layers; higher++)
                    {
                        if (document[higher, part].Type == Models.CellType.Crystal)
                        {
                            properties |= ShapeProperty.GapBelowShape;

                            break;
                        }
                    }
                }
            }
        }

        if (isStable)
        {
            properties |= ShapeProperty.Stable;
        }

        if (isExchangeable)
        {
            properties |= ShapeProperty.Exchangeable;
        }

        // 含循环支撑结构：两种规则下落后结果不同，说明有部件靠循环支撑站住
        if (mode == Physics.CollapseMode.IncludeCycles)
        {
            Models.ShapeDocument? withCycles = Physics.ShapePhysics
                .Collapse(document, Physics.CollapseMode.IncludeCycles)
                .Document;

            Models.ShapeDocument? floorOnly = Physics.ShapePhysics
                .Collapse(document, Physics.CollapseMode.FloorOnly)
                .Document;

            if (withCycles is not null && floorOnly is not null)
            {
                var exporter = new Services.ShortCodeExporter();

                if (exporter.Export(withCycles) != exporter.Export(floorOnly))
                {
                    properties |= ShapeProperty.CycleSupported;
                }
            }
        }

        return properties;
    }

    /// <summary>性质的中文名称，供界面展示。</summary>
    public static string NameOf(
        ShapeProperty property)
    {
        return property switch
        {
            ShapeProperty.ContainsCrystal => "包含晶体",
            ShapeProperty.ContainsPin => "包含顶针",
            ShapeProperty.CrystalAndEmptySameLayer => "晶空同层",
            ShapeProperty.CrystalAndPinSameLayer => "晶顶同层",
            ShapeProperty.GapBelowShape => "图下间隙",
            ShapeProperty.GapBelowCrystal => "晶下间隙",
            ShapeProperty.MixedCrystalColorsSameLayer => "同层异色晶体",
            ShapeProperty.Stable => "稳定",
            ShapeProperty.Exchangeable => "可交换",
            ShapeProperty.CycleSupported => "循环支撑结构",
            _ => property.ToString()
        };
    }
}

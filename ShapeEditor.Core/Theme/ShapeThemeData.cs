namespace ShapeEditor.Core.Theme;

/// <summary>
/// 传给 Web 端 shape-grid.js 的主题常量。
///
/// 颜色与尺寸全部来自 Core，Web 端不再维护自己的硬编码色值。
/// 序列化后属性名会变成 camelCase，需与 shape-grid.js 保持一致。
/// </summary>
public sealed class ShapeThemeData
{
    /// <summary>
    /// 一次性构建，所有画布共用。
    /// </summary>
    public static ShapeThemeData Default { get; } = Create();

    /// <summary>
    /// 颜色代码到 #RRGGBB 的映射。
    /// </summary>
    public Dictionary<string, string> Colors { get; init; } = new();

    /// <summary>
    /// 颜色代码到七段晶体条带色的映射。
    /// </summary>
    public Dictionary<string, string[]> CrystalBands { get; init; } = new();

    public double CellWidth { get; init; }

    public double CellHeight { get; init; }

    public double FitMargin { get; init; }

    public double MaxFitScale { get; init; }

    public double MinZoom { get; init; }

    public double MaxZoom { get; init; }

    public string GridLineColor { get; init; } = "";

    public string SelectionColor { get; init; } = "";

    public double CellBorderOpacity { get; init; }

    public double PinBarRatio { get; init; }

    public double ExportCellWidth { get; init; }

    public double ExportCellHeight { get; init; }

    public int MaxExportSide { get; init; }

    public static ShapeThemeData Create()
    {
        var colors = new Dictionary<string, string>();

        var bands = new Dictionary<string, string[]>();

        foreach (char code in Models.ShapeColor.All)
        {
            string key = code.ToString();

            colors[key] = ToHex(ShapePalette.FromCode(code));

            bands[key] = global::ShapeEditor.Core.Theme.CrystalBands
                .Get(code)
                .Select(ToHex)
                .ToArray();
        }

        return new ShapeThemeData
        {
            Colors = colors,
            CrystalBands = bands,
            CellWidth = GridMetrics.CellWidth,
            CellHeight = GridMetrics.CellHeight,
            FitMargin = GridMetrics.FitMargin,
            MaxFitScale = GridMetrics.MaxFitScale,
            MinZoom = GridMetrics.MinZoom,
            MaxZoom = GridMetrics.MaxZoom,
            GridLineColor = ToHex(GridMetrics.GridLineColor),
            SelectionColor = ToHex(GridMetrics.SelectionColor),
            CellBorderOpacity = GridMetrics.CellBorderOpacity,
            PinBarRatio = GridMetrics.PinBarRatio,
            ExportCellWidth = GridMetrics.ExportCellWidth,
            ExportCellHeight = GridMetrics.ExportCellHeight,
            MaxExportSide = GridMetrics.MaxExportSide
        };
    }

    private static string ToHex(
        uint argb)
    {
        return $"#{(byte)(argb >> 16):X2}{(byte)(argb >> 8):X2}{(byte)argb:X2}";
    }
}

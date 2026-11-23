using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Theme;
using ShapeEditor.Presentation.Angles;
using ShapeEditor.Presentation.Cards;
using ShapeEditor.Presentation.Exchange;
using ShapeEditor.Presentation.Export;
using ShapeEditor.Presentation.Styling;
using PresentationPalette = ShapeEditor.Presentation.Styling.ShapePalette;

namespace ShapeEditor.Tests;

/// <summary>
/// 共享展示模型 <c>ShapeEditor.Presentation</c> 的单元测试。
///
/// <para>
/// 迁移的验收口径是「行为不变」：这些用例把工序卡片投影、交换半边文案、
/// 角工序节点尺寸与样式常量钉在迁移前的数值上，避免以后改动时悄悄漂移。
/// </para>
/// </summary>
internal static class PresentationTests
{
    /// <summary>与 <c>--cards</c> 验收用的同一张图形（8 层，n = 4）。</summary>
    private const string UserExampleHalfOnly =
        "crCu----:CuCu----:--Cu----:CuCu----:--------:crcr----:P-cr----:Cucr----";

    /// <summary>
    /// 交换方向文案：两侧都是半边或列号互补时写「半边1 + 半边2」；
    /// 不互补时保留旧写法；非 4 象限原样返回。
    /// </summary>
    public static void ExchangeHalfTextRules()
    {
        const string Direction = "(1+2)+(3+4)";

        IReadOnlyList<IReadOnlyList<int>> bothHalves =
            new IReadOnlyList<int>[]
            {
                new[] { 0, 1 },
                new[] { 2, 3 }
            };

        Assert.Equal(
            "半边1 + 半边2",
            ExchangeDisplay.FormatDirection(Direction, 4, bothHalves),
            "两侧都是半边时应写「半边1 + 半边2」");

        IReadOnlyList<IReadOnlyList<int>> leftHalfOnly =
            new IReadOnlyList<int>[]
            {
                new[] { 0, 1 }
            };

        Assert.Equal(
            "半边1 + 半边2",
            ExchangeDisplay.FormatDirection(Direction, 4, leftHalfOnly),
            "只有一侧是半边、但两侧列号互补时仍写「半边1 + 半边2」");

        /* 两侧列号有重叠（1+2 / 2+3）时不互补：保留旧写法。 */
        Assert.Equal(
            "半边1 + (2+3)",
            ExchangeDisplay.FormatDirection("(1+2)+(2+3)", 4, leftHalfOnly),
            "列号不互补时应保留「半边1 + (2+3)」");

        Assert.Equal(
            Direction,
            ExchangeDisplay.FormatDirection(Direction, 6, bothHalves),
            "非 4 象限的方向文本必须原样返回");

        Assert.Equal(
            "半边1",
            ExchangeDisplay.FormatSideNodeTitle(
                "交换得到左半边",
                Direction,
                4,
                leftHalfOnly),
            "左侧是半边时，交换得到左半边的节点标题应简写为「半边1」");

        Assert.Equal(
            "半边2",
            ExchangeDisplay.FormatSideNodeTitle(
                "交换得到右半边",
                Direction,
                4,
                leftHalfOnly),
            "列号互补时，另一侧的节点标题也应写「半边2」");

        Assert.Equal(
            "制作零件 1",
            ExchangeDisplay.FormatSideNodeTitle(
                "制作零件 1",
                Direction,
                4,
                bothHalves),
            "其它节点标题不受影响");
    }

    /// <summary>工序卡片三层投影的标题、种类、模式与角卡小字。</summary>
    public static void CardLayersMirrorCardSet()
    {
        ShapeDocument target = Parse(UserExampleHalfOnly);

        WorkOrderCardSet set = CardBuilder.Build(target);

        WorkOrderCardPresentation? view = WorkOrderCardPresentation.Create(set);

        Assert.True(view is not null, "有卡片时不应返回 null");
        Assert.Equal(3, view!.Layers.Count, "固定三层：交换 → 半边 → 角");
        Assert.Equal(set.Cards.Count, view.TotalCount);
        Assert.Equal(set.VerifiedCount, view.VerifiedCount);

        Assert.Equal(WorkOrderCardLayerKind.Exchange, view.Layers[0].Kind);
        Assert.Equal("交换工序", view.Layers[0].Title);
        Assert.Equal(1, view.Layers[0].Cards.Count);
        Assert.Equal("交换卡", view.Layers[0].Cards[0].KindText);
        Assert.Equal("目标图形", view.Layers[0].Cards[0].OutputCaption);

        Assert.Equal(WorkOrderCardLayerKind.Half, view.Layers[1].Kind);
        Assert.Equal("半边工序", view.Layers[1].Title);
        Assert.Equal(2, view.Layers[1].Cards.Count);
        Assert.Equal("半边卡", view.Layers[1].Cards[0].KindText);
        Assert.Equal("A 类 · 角产物交换", view.Layers[1].Cards[0].ModeText);
        Assert.Equal("整半边", view.Layers[1].Cards[1].ModeText);
        Assert.False(view.Layers[1].Cards[0].IsLegacyHalf, "v4 不应产生旧版 B 类卡");

        Assert.Equal(WorkOrderCardLayerKind.Corner, view.Layers[2].Kind);
        Assert.Equal("角工序", view.Layers[2].Title);

        WorkOrderCardItem corner = view.Layers[2].Cards[0];

        Assert.Equal("角卡", corner.KindText);
        Assert.Equal("Q1 目标角 / Q2 右柱 / Q4 左柱", corner.ColumnCaption);
        Assert.Equal("Q1 目标角 + 双柱（已归一化）", corner.OutputCaption);

        /*
         * 可见步骤必须与 Core 的 Hidden 标记严格对应：
         * 页面 / 导出看到的步骤数 = Core 里 Hidden == false 的步骤数。
         */
        foreach (WorkOrderCardLayer layer in view.Layers)
        {
            foreach (WorkOrderCardItem card in layer.Cards)
            {
                WorkOrderCardView source = set.Cards.First(
                    candidate =>
                        candidate.Kind == card.Kind &&
                        candidate.Title == card.Title);

                Assert.Equal(
                    source.Steps.Count(step => !step.Hidden),
                    card.Steps.Count,
                    $"卡「{card.Title}」的可见步骤数应与 Core 一致");
            }
        }
    }

    /// <summary>导出投影与页面投影逐字段一致（页面与导出图不会各算一遍）。</summary>
    public static void ExportLayersMirrorPresentation()
    {
        ShapeDocument target = Parse(UserExampleHalfOnly);

        WorkOrderCardPresentation view =
            WorkOrderCardPresentation.Create(CardBuilder.Build(target))!;

        IReadOnlyList<WorkOrderCardExportLayer> layers = view.ToExportLayers();

        Assert.Equal(view.Layers.Count, layers.Count);

        int exported = 0;

        for (int index = 0; index < layers.Count; index++)
        {
            WorkOrderCardLayer source = view.Layers[index];
            WorkOrderCardExportLayer export = layers[index];

            Assert.Equal(source.Title, export.Title);
            Assert.Equal(source.Cards.Count, export.Count);

            exported += export.Cards.Count;
        }

        Assert.Equal(view.TotalCount, exported, "导出的卡片总数必须与页面一致");

        WorkOrderCardItem corner = view.Layers[2].Cards[0];
        WorkOrderCardExportCard exportCorner = layers[2].Cards[0];

        Assert.Equal(corner.Title, exportCorner.Title);
        Assert.Equal(corner.KindText, exportCorner.KindText);
        Assert.Equal(corner.ModeText, exportCorner.ModeText);
        Assert.Equal(corner.Verified, exportCorner.Verified);
        Assert.Equal(corner.IsLegacyHalf, exportCorner.IsLegacyHalf);
        Assert.Equal(corner.OutputCaption, exportCorner.OutputCaption);
        Assert.Equal(corner.ColumnCaption, exportCorner.CornerCaption);
        Assert.Equal(corner.PreStateTitle, exportCorner.PreStateTitle);

        ShapeCanvasState output = exportCorner.Output!;

        Assert.Equal(corner.Output.PartCount, output.Columns);
        Assert.Equal(corner.Output.LayerCount, output.Rows);
        Assert.Equal(
            corner.Output.PartCount * corner.Output.LayerCount,
            output.Cells.Count,
            "导出快照必须逐格完整");

        Assert.Equal(corner.Inputs.Count, exportCorner.Inputs.Count);
        Assert.Equal(corner.Steps.Count, exportCorner.Steps.Count);

        WorkOrderCardItem half = view.Layers[1].Cards[0];
        WorkOrderCardExportCard exportHalf = layers[1].Cards[0];

        Assert.Equal(2, half.Inputs.Count, "A 类半边卡应带两张角卡产物");
        Assert.Equal(half.Inputs[0].Title, exportHalf.Inputs[0].Title);
        Assert.True(exportHalf.Inputs[0].State is not null);
    }

    /// <summary>角工序节点链沿用 CardMetrics 的卡片尺寸，不含魔法数。</summary>
    public static void AngleWorkNodesUseCardMetrics()
    {
        ShapeDocument target = Parse(UserExampleHalfOnly);

        IReadOnlyList<AngleWorkColumn> works = AngleWorkColumnBuilder.Build(target);

        Assert.Equal(target.PartCount, works.Count, "每个非空角一张列卡片");

        foreach (AngleWorkColumn work in works)
        {
            foreach (AngleWorkNode node in work.Nodes)
            {
                Assert.Equal(
                    CardMetrics.NodeWidth(node.Shape.PartCount),
                    node.Width,
                    "节点宽度 = 象限数 × 30 + 16");

                Assert.Equal(
                    CardMetrics.NodeHeight(node.Shape.LayerCount),
                    node.Height,
                    "节点高度 = 50 + 层数 × 30");

                Assert.True(
                    node.KindClass.StartsWith("angle-kind-", StringComparison.Ordinal),
                    "节点样式类应沿用 angle-kind-* 前缀");
            }

            if (work.Half is not null)
            {
                Assert.Equal(AngleHalfWork.TitleText, work.Half.Title);
                Assert.Equal(
                    "目标半边 = " + work.Half.CardTitle,
                    work.Half.TargetLine);
            }
        }
    }

    /// <summary>样式常量必须与 Core 的调色板 / 主题数据逐项一致。</summary>
    public static void StylingConstantsMatchCore()
    {
        ShapeThemeData theme = ShapeThemeData.Default;

        foreach (char code in ShapeColor.All)
        {
            string key = code.ToString();

            Assert.Equal(theme.Colors[key], PresentationPalette.Hex(code));
            Assert.Equal(
                ShapeEditor.Core.Theme.ShapePalette.FromCode(code),
                PresentationPalette.Argb(code));
            Assert.Equal(
                ShapeEditor.Core.Theme.ShapePalette.GetShortName(code),
                PresentationPalette.ShortName(code));
            Assert.Equal(
                theme.CrystalBands[key].Length,
                PresentationPalette.CrystalBands(code).Length);
        }

        Assert.Equal(0xFFFFFFFFu, PresentationPalette.Empty);
        Assert.True(
            PresentationPalette.NeedsLightText('k'),
            "黑色需要白色文字");

        Assert.Equal("图", CellGlyph.For(CellType.Shape));
        Assert.Equal("晶", CellGlyph.For(CellType.Crystal));
        Assert.Equal("", CellGlyph.For(CellType.Pin));
        Assert.Equal("", CellGlyph.For(CellType.Empty));
        Assert.True(CellGlyph.HasText(CellType.Crystal));
        Assert.False(CellGlyph.HasText(CellType.Pin));
        Assert.Equal("crystal", CellGlyph.KindName(CellType.Crystal));
        Assert.Equal("shape", CellGlyph.KindName(CellType.Shape));
        Assert.Equal("empty", CellGlyph.KindName(CellType.Empty));

        Assert.Equal(30, CardMetrics.CellSize);
        Assert.Equal(50, CardMetrics.NodeHeaderHeight);
        Assert.Equal(16, CardMetrics.NodeHorizontalPadding);
        Assert.Equal(4 * 30 + 16, CardMetrics.NodeWidth(4));
        Assert.Equal(50 + 2 * 30, CardMetrics.NodeHeight(2));
    }

    /// <summary>没有卡片（例如 n = 2）时返回 null，页面据此回退旧渲染。</summary>
    public static void EmptyCardSetReturnsNull()
    {
        Assert.True(WorkOrderCardPresentation.Create(null) is null);
    }

    /// <summary>按短代码解析出目标图形（与 <c>--cards</c> 的入口一致）。</summary>
    private static ShapeDocument Parse(string code)
    {
        Assert.True(
            ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError),
            inferError ?? "短代码尺寸推断失败");

        ParseResult parsed = new ShortCodeParser().Parse(code, layers, parts);

        Assert.True(
            parsed.Success && parsed.Document is not null,
            parsed.Error ?? "短代码解析失败");

        return parsed.Document!;
    }
}

using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 短代码解析与导出。
/// </summary>
internal static class ShortCodeTests
{
    public static void FullCodeRoundTrip()
    {
        var parser = new ShortCodeParser();

        var exporter = new ShortCodeExporter();

        ParseResult result = parser.Parse(
            "CrP---Gu:ckckckck",
            2,
            4);

        Assert.True(result.Success, result.Error ?? "解析失败");
        Assert.True(result.Document is not null);

        ShapeDocument document = result.Document!;

        Assert.Equal(CellType.Shape, document[0, 0].Type);
        Assert.Equal('r', document[0, 0].ColorCode);
        Assert.Equal(CellType.Pin, document[0, 1].Type);
        Assert.Equal(CellType.Empty, document[0, 2].Type);
        Assert.Equal(CellType.Shape, document[0, 3].Type);
        Assert.Equal('u', document[0, 3].ColorCode);
        Assert.Equal(CellType.Crystal, document[1, 0].Type);

        string exported = exporter.Export(document);

        Assert.Equal("CrP---Gu:ckckckck", exported);
    }

    public static void SuperCodeRoundTrip()
    {
        var parser = new ShortCodeParser();

        var exporter = new ShortCodeExporter();

        ParseResult result = parser.Parse(
            "CcCc",
            1,
            4,
            ShortCodeFormat.Super);

        Assert.True(result.Success, result.Error ?? "解析失败");

        ShapeDocument document = result.Document!;

        Assert.Equal(CellType.Shape, document[0, 0].Type);
        Assert.Equal(CellType.Crystal, document[0, 1].Type);

        string exported = exporter.Export(
            document,
            ShortCodeFormat.Super);

        Assert.Equal("CcCc", exported);
    }

    public static void InferDimensionsWorks()
    {
        Assert.True(ShortCodeParser.TryInferDimensions(
            "CrP---Gu:ckckckck",
            ShortCodeFormat.Full,
            out int layers,
            out int parts,
            out _));

        Assert.Equal(2, layers);
        Assert.Equal(4, parts);

        Assert.True(ShortCodeParser.TryInferDimensions(
            "CcCc",
            ShortCodeFormat.Super,
            out layers,
            out parts,
            out _));

        Assert.Equal(1, layers);
        Assert.Equal(4, parts);
    }

    public static void InvalidCodeFails()
    {
        var parser = new ShortCodeParser();

        ParseResult result = parser.Parse(
            "CrP",
            1,
            2);

        Assert.False(result.Success, "长度不足的代码应当解析失败");
    }

    public static void EmptyLayersAreOmitted()
    {
        var exporter = new ShortCodeExporter();

        var document = new ShapeDocument(4, 2);

        document[0, 0] = ShapeCell.Shape('C', 'r');

        string code = exporter.Export(
            document,
            omitTopEmptyLayers: true);

        Assert.Equal("Cr--", code);
    }
}

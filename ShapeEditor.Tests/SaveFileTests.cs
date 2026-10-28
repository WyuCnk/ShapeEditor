using System.IO;
using ShapeEditor.Core.Models;
using ShapeEditor.Wpf.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// 存档格式：与 Web 端互通的 JSON。
/// </summary>
internal static class SaveFileTests
{
    public static void RoundTripKeepsDocument()
    {
        var service = new SaveFileService();

        var document = new ShapeDocument(2, 4);

        document[0, 0] = ShapeCell.Shape('C', 'r');

        document[1, 0] = ShapeCell.Crystal('k');

        string path = Path.Combine(
            Path.GetTempPath(),
            $"shape-editor-test-{Guid.NewGuid():N}.json");

        try
        {
            service.Save(document, path);

            LoadResult result = service.Load(path);

            Assert.True(result.Success, result.Error ?? "读取失败");

            ShapeDocument loaded = result.Document!;

            Assert.Equal(2, loaded.LayerCount);
            Assert.Equal(4, loaded.PartCount);
            Assert.Equal(CellType.Shape, loaded[0, 0].Type);
            Assert.Equal('r', loaded[0, 0].ColorCode);
            Assert.Equal(CellType.Crystal, loaded[1, 0].Type);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static void RejectsUnknownVersion()
    {
        var service = new SaveFileService();

        LoadResult result = service.Parse(
            """
            {
              "Version": 2,
              "LayerCount": 1,
              "PartCount": 1,
              "ShortCode": "Cr"
            }
            """);

        Assert.False(result.Success, "版本 2 应当被拒绝");
    }

    public static void RejectsInvalidDimensions()
    {
        var service = new SaveFileService();

        LoadResult result = service.Parse(
            """
            {
              "Version": 1,
              "LayerCount": 0,
              "PartCount": 4,
              "ShortCode": ""
            }
            """);

        Assert.False(result.Success, "尺寸 0 应当被拒绝");
    }

    public static void RejectsBrokenJson()
    {
        var service = new SaveFileService();

        LoadResult result = service.Parse("{ not json ");

        Assert.False(result.Success);
        Assert.True(
            result.Error!.Contains("JSON", StringComparison.Ordinal),
            "错误信息应说明 JSON 无效");
    }

    public static void DimensionLimitMatchesParsers()
    {
        Assert.True(SaveFileService.IsValidDimensions(100, 100));
        Assert.False(SaveFileService.IsValidDimensions(101, 1));
        Assert.False(SaveFileService.IsValidDimensions(1, 0));
        Assert.False(SaveFileService.IsValidDimensions(100, 101));
    }
}

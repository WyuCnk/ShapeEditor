using System.IO;
using System.Text.Json;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Wpf.Services;

/// <summary>
/// 存档文件模型。
///
/// 字段名与 Web 端 SaveFile 完全一致，三端可互相导入。
/// </summary>
public sealed class SaveFile
{
    public int Version { get; set; } = 1;

    public int LayerCount { get; set; }

    public int PartCount { get; set; }

    public string ShortCode { get; set; } = "";

    public DateTime SavedAt { get; set; }
}

public sealed class LoadResult
{
    private LoadResult(
        ShapeDocument? document,
        string? error)
    {
        Document = document;
        Error = error;
    }

    public ShapeDocument? Document { get; }

    public string? Error { get; }

    public bool Success => Document is not null;

    public static LoadResult Ok(
        ShapeDocument document)
    {
        return new LoadResult(document, null);
    }

    public static LoadResult Fail(
        string error)
    {
        return new LoadResult(null, error);
    }
}

/// <summary>
/// 存档读写。格式与 Web 端一致，只保存短代码与尺寸。
/// </summary>
public sealed class SaveFileService
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CompactWriteOptions = new();

    private readonly ShortCodeParser _parser = new();

    private readonly ShortCodeExporter _exporter = new();

    public SaveFile Create(
        ShapeDocument document)
    {
        return new SaveFile
        {
            Version = CurrentVersion,
            LayerCount = document.LayerCount,
            PartCount = document.PartCount,
            ShortCode = _exporter.Export(
                document,
                omitTopEmptyLayers: true),
            SavedAt = DateTime.Now
        };
    }

    public string Serialize(
        SaveFile file,
        bool indented = true)
    {
        return JsonSerializer.Serialize(
            file,
            indented ? WriteOptions : CompactWriteOptions);
    }

    public void Save(
        ShapeDocument document,
        string path)
    {
        string json = Serialize(Create(document));

        File.WriteAllText(path, json);
    }

    public LoadResult Load(
        string path)
    {
        try
        {
            string json = File.ReadAllText(path);

            return Parse(json);
        }
        catch (IOException exception)
        {
            return LoadResult.Fail(
                $"读取存档失败：{exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return LoadResult.Fail(
                $"读取存档失败：{exception.Message}");
        }
    }

    public LoadResult Parse(
        string json)
    {
        SaveFile? file;

        try
        {
            file = JsonSerializer.Deserialize<SaveFile>(
                json,
                ReadOptions);
        }
        catch (JsonException)
        {
            return LoadResult.Fail("存档文件不是有效的 JSON 格式。");
        }

        return FromSaveFile(file);
    }

    /// <summary>
    /// 校验并还原存档。自动存档与手动导入共用这段逻辑。
    /// </summary>
    public LoadResult FromSaveFile(
        SaveFile? file)
    {
        if (file is null)
        {
            return LoadResult.Fail("存档文件内容为空。");
        }

        if (file.Version != CurrentVersion)
        {
            return LoadResult.Fail(
                $"不支持的存档版本：{file.Version}。");
        }

        if (!IsValidDimensions(
                file.LayerCount,
                file.PartCount))
        {
            return LoadResult.Fail("存档中的图形尺寸无效。");
        }

        if (string.IsNullOrWhiteSpace(file.ShortCode))
        {
            return LoadResult.Fail("存档中没有短代码。");
        }

        ParseResult result = _parser.Parse(
            file.ShortCode,
            file.LayerCount,
            file.PartCount);

        if (!result.Success ||
            result.Document is null)
        {
            return LoadResult.Fail(
                result.Error ?? "存档中的短代码解析失败。");
        }

        return LoadResult.Ok(result.Document);
    }

    /// <summary>
    /// 与 Web 端一致的限制：1–100 层/象限，总数不超过 10000。
    /// </summary>
    public static bool IsValidDimensions(
        int layerCount,
        int partCount)
    {
        return layerCount >= 1 &&
               layerCount <= ShortCodeParser.MaxLayerCount &&
               partCount >= 1 &&
               partCount <= ShortCodeParser.MaxPartCount &&
               (long)layerCount * partCount <= ShortCodeParser.MaxCellCount;
    }
}

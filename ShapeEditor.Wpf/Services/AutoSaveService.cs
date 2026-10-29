using System.IO;
using ShapeEditor.Core.Models;

namespace ShapeEditor.Wpf.Services;

/// <summary>
/// 自动保存。
///
/// 与 Web 端把当前图形写进 localStorage 对应，
/// 这里写到 %LOCALAPPDATA%\ShapeEditor\autosave.json。
/// </summary>
public sealed class AutoSaveService
{
    private readonly SaveFileService _files = new();

    /// <summary>
    /// 默认写到 %LOCALAPPDATA%\ShapeEditor；
    /// 测试可以传入临时目录，避免碰到真实存档。
    /// </summary>
    public AutoSaveService(
        string? directoryPath = null)
    {
        string root = directoryPath ??
            StoragePaths.Root;

        DirectoryPath = root;

        FilePath = Path.Combine(root, "autosave.json");
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    /// <summary>最近一次保存失败的原因，供界面提示。</summary>
    public string? LastError { get; private set; }

    public void Save(
        ShapeDocument document)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            SaveFile file = _files.Create(document);

            File.WriteAllText(
                FilePath,
                _files.Serialize(file, indented: false));
        }
        catch (Exception exception)
        {
            /*
             * 自动保存失败不能打断编辑，但要记下原因。
             */
            LastError = exception.Message;
        }
    }

    public ShapeDocument? Restore()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            string json = File.ReadAllText(FilePath);

            LoadResult result = _files.Parse(json);

            return result.Document;
        }
        catch
        {
            /*
             * 本地数据损坏时忽略，不影响启动。
             */
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            /*
             * 删除失败忽略。
             */
        }
    }
}

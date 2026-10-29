using System.IO;

namespace ShapeEditor.Wpf.Services;

/// <summary>
/// 数据目录解析：优先 %LOCALAPPDATA%，不可写时退到程序目录下的 data，
/// 再不行退到临时目录。
///
/// 之所以要回退：如果程序是从受限沙箱里启动的，进程令牌没有
/// %LOCALAPPDATA% 的写权限，直接写入会抛异常，数据就“悄悄丢了”。
/// </summary>
public static class StoragePaths
{
    public static string Root { get; } = Resolve();

    public static string AutosaveFile => Path.Combine(Root, "autosave.json");

    public static string WorkspaceFile => Path.Combine(Root, "workspaces.json");

    private static string Resolve()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "ShapeEditor"));
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "ShapeEditor"));

        foreach (string candidate in candidates)
        {
            if (IsWritable(candidate))
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private static bool IsWritable(
        string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            string probe = Path.Combine(directory, $".probe-{Guid.NewGuid():N}");

            File.WriteAllText(probe, "1");
            File.Delete(probe);

            return true;
        }
        catch
        {
            return false;
        }
    }
}

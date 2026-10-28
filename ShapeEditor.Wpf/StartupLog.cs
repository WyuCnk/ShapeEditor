using System.IO;
using System.Text;

namespace ShapeEditor.Wpf;

/// <summary>
/// 极简启动日志。
///
/// 无控制台窗口的程序在开发机上排查启动问题时，
/// 至少能留下一条线索；正式使用时只会写很少几行。
/// </summary>
internal static class StartupLog
{
    private static readonly object Sync = new();

    private static string? _path = Environment.GetEnvironmentVariable("SHAPEEDITOR_LOG");

    public static void Write(
        string message)
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        lock (Sync)
        {
            try
            {
                File.AppendAllText(
                    _path,
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
                /*
                 * 日志失败不能影响主流程。
                 */
            }
        }
    }

    public static void WriteException(
        string context,
        Exception exception)
    {
        Write($"[ERROR] {context}: {exception}");
    }
}

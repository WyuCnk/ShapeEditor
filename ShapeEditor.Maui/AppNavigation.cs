namespace ShapeEditor.Maui;

/// <summary>
/// Shell 导航的集中入口。
///
/// 工作区、批量页、分析页都通过这里跳转，路由名只写一处；
/// 导航失败（比如 Shell 还没准备好）时返回 false，由调用方提示，
/// 不让异常把 App 打崩。
/// </summary>
public static class AppNavigation
{
    /// <summary>分析页的路由名，在 AppShell 里注册。</summary>
    public const string AnalysisRoute = "AnalysisPage";

    /// <summary>编辑器所在标签页的绝对路由。</summary>
    public const string EditorRoute = "//MainPage";

    /// <summary>打开分析页（压入当前标签页的导航栈）。</summary>
    public static Task<bool> GoToAnalysisAsync()
    {
        return TryGoAsync(AnalysisRoute);
    }

    /// <summary>切换到编辑器标签页。</summary>
    public static Task<bool> GoToEditorAsync()
    {
        return TryGoAsync(EditorRoute);
    }

    /// <summary>从压栈页面返回上一页。</summary>
    public static Task<bool> GoBackAsync()
    {
        return TryGoAsync("..");
    }

    private static async Task<bool> TryGoAsync(
        string route)
    {
        try
        {
            if (Shell.Current is null)
            {
                return false;
            }

            await Shell.Current.GoToAsync(route);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

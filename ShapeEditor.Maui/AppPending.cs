using ShapeEditor.Core.Models;

namespace ShapeEditor.Maui;

/// <summary>
/// 分析页的目标图形：短代码 + 原始尺寸 + 格式。
///
/// 工作区和批量页都只传短代码，不能只靠「按代码推断尺寸」，
/// 因为素材可能来自超短代码，必须把原始层数 / 象限数一起带过去。
/// </summary>
public sealed record AnalysisTarget(
    string Code,
    ShortCodeFormat Format,
    int Layers,
    int Parts);

/// <summary>
/// 「加到编辑区」的目标图形：短代码 + 原始尺寸 + 格式。
/// </summary>
public sealed record EditorLoadTarget(
    string Code,
    ShortCodeFormat Format,
    int Layers,
    int Parts);

/// <summary>
/// 页面之间的待处理数据。
///
/// Shell 导航只能带路由字符串，不能直接传对象，这里照 Web 版的
/// PendingLoad 机制做一个进程内中转：写入后由目标页在 OnAppearing
/// 里取用一次，取走即清空，避免下次进入时重复载入。
/// </summary>
public static class AppPending
{
    private static AnalysisTarget? _analysis;

    private static EditorLoadTarget? _editorLoad;

    /// <summary>记录要分析的图形，供分析页取用。</summary>
    public static void SetAnalysis(
        AnalysisTarget target)
    {
        _analysis = target;
    }

    /// <summary>取走待分析图形，取走后不再保留。</summary>
    public static AnalysisTarget? TakeAnalysis()
    {
        AnalysisTarget? target = _analysis;

        _analysis = null;

        return target;
    }

    /// <summary>丢弃待分析图形（导航失败时调用，避免下次误用）。</summary>
    public static void ClearAnalysis()
    {
        _analysis = null;
    }

    /// <summary>记录要载入编辑器的图形，供编辑器取用。</summary>
    public static void SetEditorLoad(
        EditorLoadTarget target)
    {
        _editorLoad = target;
    }

    /// <summary>取走待载入编辑器的图形，取走后不再保留。</summary>
    public static EditorLoadTarget? TakeEditorLoad()
    {
        EditorLoadTarget? target = _editorLoad;

        _editorLoad = null;

        return target;
    }

    /// <summary>丢弃待载入编辑器的图形（导航失败时调用，避免下次误载入）。</summary>
    public static void ClearEditorLoad()
    {
        _editorLoad = null;
    }
}

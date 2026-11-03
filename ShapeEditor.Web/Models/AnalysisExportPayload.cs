namespace ShapeEditor.Web.Models;

/// <summary>
/// 导出分析图时传给 shape-grid.js 的数据。
///
/// 必须使用具名的公开类型：System.Text.Json 在序列化
/// List&lt;object&gt; 里的匿名类型时会直接报
/// SerializationNotSupportedParentType（参考 GraphExportPayload）。
/// </summary>
public sealed class AnalysisExportPayload
{
    /// <summary>顶部标题，默认「图形分析」。</summary>
    public string Title { get; set; } = "图形分析";

    /// <summary>图形短代码。</summary>
    public string Code { get; set; } = "";

    /// <summary>竖式图绘制数据，为 null 时不画竖式图。</summary>
    public ShapeCanvasState? State { get; set; }

    /// <summary>
    /// 当前显示的板块，按此顺序从上到下绘制。
    /// 隐藏的板块不会加入这个列表，因此也不会出现在图片里。
    /// </summary>
    public List<AnalysisExportSection> Sections { get; set; } = new();

    /// <summary>底部左侧：层数 × 象限数。</summary>
    public string Meta { get; set; } = "";

    /// <summary>底部右侧：生成时间。</summary>
    public string GeneratedAt { get; set; } = "";
}

/// <summary>
/// 导出图里的一个板块。
/// </summary>
public sealed class AnalysisExportSection
{
    /// <summary>板块标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>板块内容文本行，由 JS 按画布宽度自动换行。</summary>
    public List<string> Lines { get; set; } = new();

    /// <summary>
    /// 板块的表格数据，为 null 时该板块按 <see cref="Lines"/> 画成纯文本。
    /// 同时存在文本行与表格时，先画文本行（作为说明），再画表格。
    /// </summary>
    public AnalysisExportTable? Table { get; set; }

    /// <summary>是否画成制作路径占位节点框（而不是普通文本行）。</summary>
    public bool IsPlaceholder { get; set; }
}

/// <summary>
/// 导出图里的一个表格板块数据。
///
/// 行数据必须用这种具名的公开类型承载：System.Text.Json 无法序列化
/// 匿名类型组成的 List&lt;object&gt;（SerializationNotSupportedParentType）。
/// </summary>
public sealed class AnalysisExportTable
{
    /// <summary>表头文本，列数以此为准。</summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>表体行，每行的列数与 <see cref="Headers"/> 一一对应。</summary>
    public List<List<string>> Rows { get; set; } = new();
}

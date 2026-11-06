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
    /// 板块的补充说明，由 JS 用更小的字号画在正文行之后。
    /// 目前用于制作路径的「未覆盖」提示（这并不代表不可制作）。
    /// </summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>
    /// 板块的表格数据，为 null 时该板块按 <see cref="Lines"/> 画成纯文本。
    /// 同时存在文本行与表格时，先画文本行（作为说明），再画表格。
    /// </summary>
    public AnalysisExportTable? Table { get; set; }

    /// <summary>
    /// 板块的「轴块」列表，为空时不画。
    /// 同时存在文本行与轴块时，先画文本行（作为说明），再画每个轴块。
    /// </summary>
    public List<AnalysisExportAxisBlock> AxisBlocks { get; set; } = new();

    /// <summary>是否画成制作路径占位节点框（而不是普通文本行）。</summary>
    public bool IsPlaceholder { get; set; }

    /// <summary>
    /// 制作路径节点图。非 null 时在板块标题与说明文本之后按实际尺寸整张绘制，
    /// 预览保持 30px 单元格、不做缩放。
    /// </summary>
    public AnalysisExportGraph? Graph { get; set; }
}

/// <summary>
/// 导出图里的制作路径节点图。
///
/// 坐标为节点图内部坐标（与页面一致），JS 直接按原尺寸绘制；
/// 图比页面宽时整张导出图随之变宽，不分片、不缩放。
/// </summary>
public sealed class AnalysisExportGraph
{
    public double Width { get; set; }

    public double Height { get; set; }

    public List<AnalysisExportGraphNode> Nodes { get; set; } = new();

    public List<AnalysisExportGraphEdge> Edges { get; set; } = new();
}

/// <summary>
/// 导出图里的一个制作路径节点。
/// </summary>
public sealed class AnalysisExportGraphNode
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public ShapeCanvasState? State { get; set; }

    public bool IsResult { get; set; }

    /// <summary>废料节点：导出时淡显、虚线边框。</summary>
    public bool IsWaste { get; set; }

    public bool IsPending { get; set; }

    /// <summary>节点类型：pin / stack / block；其余节点为空。</summary>
    public string Kind { get; set; } = "";

    /// <summary>右上角类型小标签：顶P / 堆 / 块；其余节点为空。</summary>
    public string KindTag { get; set; } = "";
}

/// <summary>
/// 导出图里的一条节点箭头。
/// </summary>
public sealed class AnalysisExportGraphEdge
{
    public string FromId { get; set; } = "";

    public string ToId { get; set; } = "";

    /// <summary>指向废料节点的箭头，导出时画成灰色虚线。</summary>
    public bool IsWaste { get; set; }
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

    /// <summary>每行样式：ok / bad / 空。</summary>
    public List<string> RowStyles { get; set; } = new();
}

/// <summary>
/// 导出图里的一个轴块：一个轴（一组交换方向）画成一张卡片，
/// 块标题 + 块内自动换行的方向文本。
///
/// 轴块不经过表格行样式，颜色直接由每个方向的
/// <see cref="AnalysisExportAxisDirection.IsLegal"/> 决定，
/// 避免表格行样式与行数对不齐导致颜色丢失。
/// </summary>
public sealed class AnalysisExportAxisBlock
{
    /// <summary>块标题，例如「轴 1」。</summary>
    public string Title { get; set; } = "";

    /// <summary>该轴的全部交换方向，按分析结果的顺序逐个列出。</summary>
    public List<AnalysisExportAxisDirection> Items { get; set; } = new();
}

/// <summary>
/// 轴块里的一个交换方向。
/// </summary>
public sealed class AnalysisExportAxisDirection
{
    /// <summary>方向显示文本，例如 <c>(1+2,3,4)+(5+6,7,8)</c>。</summary>
    public string Text { get; set; } = "";

    /// <summary>是否合法：合法画绿色，不合法画红色。</summary>
    public bool IsLegal { get; set; }
}

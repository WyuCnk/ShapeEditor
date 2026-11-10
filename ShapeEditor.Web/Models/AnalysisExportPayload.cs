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

    /// <summary>
    /// 角的工序：每一列一张卡片。非空时按「角 N + 分类/构造 + 节点链」绘制，
    /// 与页面上的角工序卡片一致；为空时该板块仍按普通文本行绘制。
    /// </summary>
    public List<AnalysisExportAngleColumn> AngleColumns { get; set; } = new();
}

/// <summary>
/// 导出图里的一列角工序。
///
/// <see cref="Graph"/> 是这一列的步骤节点链（坐标从 0 开始），
/// 未完全验证时 <see cref="Note"/> 与 <see cref="Limitations"/> 用小字画在节点链下方。
/// </summary>
public sealed class AnalysisExportAngleColumn
{
    /// <summary>列标题，例如「角 1」。</summary>
    public string Title { get; set; } = "";

    /// <summary>分类文本：简单角 / 碎晶角。</summary>
    public string KindText { get; set; } = "";

    /// <summary>
    /// 是否绘制分类芯片。无晶体 / Simple 的算法文本已经带分类，
    /// 页面会隐藏分类芯片，导出沿用同一个判断，保证两边标题一致。
    /// </summary>
    public bool ShowKindChip { get; set; } = true;

    /// <summary>算法文本，例如「算法 6」「无晶体」「简单角」「兜底构造」；没有分档时为空。</summary>
    public string AlgorithmText { get; set; } = "";

    /// <summary>构造文本，例如「构造 G」「构造 C」「填充 + 暴破」；没有构造时为空。</summary>
    public string ConstructionText { get; set; } = "";

    /// <summary>顶针次数文本，例如「顶针次数 1」；简单角为空。</summary>
    public string RankText { get; set; } = "";

    /// <summary>
    /// 造塔子配方的小字步骤名。导出不画子配方预览（四片并排会过宽），
    /// 只按「制作零件 1 → … → 目标」列成一行或多行小字。
    /// </summary>
    public List<string> SubRecipeLines { get; set; } = new();

    /// <summary>
    /// 半边工序的标题（固定「半边工序」）。为空表示这一列没有半边工序，
    /// 导出时不画半边区块。
    /// </summary>
    public string HalfTitle { get; set; } = "";

    /// <summary>半边工序的候选名，例如「候选3·临时晶体+保险丝」；写进卡片标题行。</summary>
    public string HalfCandidateText { get; set; } = "";

    /// <summary>半边工序的配对列提示，例如「与列 2 一起做」；写进卡片标题行。</summary>
    public string HalfColumnsText { get; set; } = "";

    /// <summary>该半边覆盖的全部列，例如「列 1、列 2」；画在半边区块的小标题里。</summary>
    public string HalfPairColumnsText { get; set; } = "";

    /// <summary>半边工序是否逐格验证通过；false 时导出 StatusText、Note 与限制小字。</summary>
    public bool HalfIsComplete { get; set; } = true;

    /// <summary>半边未完全验证时的小字标题：「该半边工序尚未完全验证」。</summary>
    public string HalfStatusText { get; set; } = "";

    /// <summary>半边工序未完成的原因；已通过时为 null。</summary>
    public string? HalfNote { get; set; }

    /// <summary>半边工序的限制与前提，只在未完成时绘制。</summary>
    public List<string> HalfLimitations { get; set; } = new();

    /// <summary>半边步骤链（n/2 列一起做的步骤）；没有半边工序时为 null。</summary>
    public AnalysisExportGraph? HalfGraph { get; set; }

    /// <summary>简单角只画一行说明，不画节点链。</summary>
    public bool IsSimple { get; set; }

    /// <summary>工序是否已逐步验证通过。</summary>
    public bool IsComplete { get; set; }

    /// <summary>未完全验证时的标题：「该列工序尚未完全验证」。</summary>
    public string StatusText { get; set; } = "";

    /// <summary>简单角的一行说明。</summary>
    public string SimpleText { get; set; } = "";

    /// <summary>未完成原因；已通过时为 null。</summary>
    public string? Note { get; set; }

    /// <summary>UI 展示用的限制/前提说明，只在未完成时绘制。</summary>
    public List<string> Limitations { get; set; } = new();

    /// <summary>该列的步骤节点链；简单角为 null。</summary>
    public AnalysisExportGraph? Graph { get; set; }
}

/// <summary>
/// 导出图里的节点图（制作路径，或角的工序的一列节点链）。
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

    /// <summary>
    /// 节点类型：pin / stack / block（制作路径），
    /// 或 angle-pre / angle-cut / angle-collapse / angle-pin / angle-support /
    /// angle-crystallize / angle-target / angle-note（角的工序）。
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// 右上角类型小标签：顶P / 堆 / 块；
    /// 角工序为 前态/切/落/顶/撑/晶/目标。
    /// </summary>
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

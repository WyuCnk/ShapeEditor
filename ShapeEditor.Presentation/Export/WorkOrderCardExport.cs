namespace ShapeEditor.Presentation.Export;

/// <summary>
/// 导出图里的一层工序卡片（交换 / 半边 / 角）。
///
/// <para>
/// 页面与导出图消费同一份 <c>WorkOrderCardPresentation</c>：
/// <c>ToExportLayers</c> 直接投影已缓存的卡片项，不重跑 Core、也不重新建卡。
/// 这组 DTO 只负责「导出图要画什么」，序列化后由 shape-grid.js 读取，
/// 因此属性名就是 JSON 字段名，迁移前后必须完全一致。
/// </para>
/// </summary>
public sealed class WorkOrderCardExportLayer
{
    /// <summary>层标题：交换工序 / 半边工序 / 角工序。</summary>
    public string Title { get; set; } = "";

    /// <summary>这一层的卡片数（标题会写成「……（N 张卡）」）。</summary>
    public int Count { get; set; }

    public List<WorkOrderCardExportCard> Cards { get; set; } = new();
}

/// <summary>
/// 导出图里的一张工序卡片。
/// </summary>
public sealed class WorkOrderCardExportCard
{
    /// <summary>卡片标题（「角1」「半边1」「交换」）。</summary>
    public string Title { get; set; } = "";

    /// <summary>种类文案：角卡 / 半边卡 / 交换卡。</summary>
    public string KindText { get; set; } = "";

    /// <summary>半边卡模式文案；其它卡为空。</summary>
    public string ModeText { get; set; } = "";

    /// <summary>真实回放是否通过。</summary>
    public bool Verified { get; set; }

    /// <summary>未验证时的失败原因 / 已验证时的依据；未验证时导出会画在红色警示框里。</summary>
    public string Note { get; set; } = "";

    /// <summary>是否角卡：为 true 时在输出下方画「Q1 目标角 / Q2 右柱 / Q4 左柱」小字。</summary>
    public bool IsCorner { get; set; }

    /// <summary>
    /// 旧版 B 类·碎晶半边卡（历史数据）：仍按普通卡绘制，但边框改虚线、颜色降级，
    /// 与页面的「旧版」样式一致。v4 已不再产生这种卡。
    /// </summary>
    public bool IsLegacyHalf { get; set; }

    /// <summary>输出说明：Q1 目标角 + 双柱 / 目标半边 / 目标图形。</summary>
    public string OutputCaption { get; set; } = "";

    /// <summary>角卡的小字标注（n = 4 时为 Q1 目标角 / Q2 右柱 / Q4 左柱）。</summary>
    public string CornerCaption { get; set; } = "";

    /// <summary>
    /// 前态标题（「拆柱后（跌落前）」「前态」）。为空表示这张卡没有前态。
    /// </summary>
    public string PreStateTitle { get; set; } = "";

    /// <summary>
    /// 前态图形。与页面一致，画在<b>输入之后、可见步骤之前</b>：
    /// v4 半边卡是「拆柱后、跌落前」的 4 列中间态；角卡是工序起点。
    /// </summary>
    public ShapeCanvasState? PreState { get; set; }

    /// <summary>输出形状；未验证的卡仍然有输出（Core 只清空 Inputs）。</summary>
    public ShapeCanvasState? Output { get; set; }

    /// <summary>
    /// 输入链。未验证的卡这里恒为空；已验证时按「上一层的卡产物」顺序排列。
    /// </summary>
    public List<WorkOrderCardExportInput> Inputs { get; set; } = new();

    /// <summary>可见步骤（Hidden 的步骤在 C# 投影时已剔除）。</summary>
    public List<WorkOrderCardExportStep> Steps { get; set; } = new();
}

/// <summary>
/// 工序卡片的一张输入。
/// </summary>
public sealed class WorkOrderCardExportInput
{
    /// <summary>输入卡标题（例如「角1」「半边1」）。</summary>
    public string Title { get; set; } = "";

    public ShapeCanvasState? State { get; set; }
}

/// <summary>
/// 工序卡片的一条可见步骤。
/// </summary>
public sealed class WorkOrderCardExportStep
{
    /// <summary>步骤名：碎晶 / 交换 / 拆柱 / 跌落 / 顶破 / 堆叠。</summary>
    public string Operation { get; set; } = "";

    public ShapeCanvasState? State { get; set; }
}

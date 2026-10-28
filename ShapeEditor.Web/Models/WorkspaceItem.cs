using ShapeEditor.Core.Models;

namespace ShapeEditor.Web.Models;

/// <summary>
/// 工作区里的一条图形记录。
///
/// 只保存短代码与尺寸，打开时按需解析，
/// 避免 200 条图形常驻内存。
/// </summary>
public sealed class WorkspaceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>
    /// 短代码。统一按 <see cref="Format"/> 指定的格式保存。
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// 短代码格式（完整 / 超短）。
    /// </summary>
    public ShortCodeFormat Format { get; set; } = ShortCodeFormat.Full;

    public int Layers { get; set; }

    public int Parts { get; set; }

    /// <summary>
    /// editor / batch / operation。
    /// </summary>
    public string Source { get; set; } = "editor";

    /// <summary>
    /// 产生它的操作：collapse / stack / pin / cut。
    /// </summary>
    public string? Operation { get; set; }

    public List<string> ParentIds { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string SourceText => Source switch
    {
        "batch" => "批量导入",
        "operation" => Operation switch
        {
            "collapse" => "下落结果",
            "stack" => "堆叠结果",
            "pin" => "顶针结果",
            "cut" => "切割结果",
            _ => "操作结果"
        },
        _ => "编辑器导入"
    };

    public string DimensionText => $"{Layers} 层 × {Parts} 象限";
}

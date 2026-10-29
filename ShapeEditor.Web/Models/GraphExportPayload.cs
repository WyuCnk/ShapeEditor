using ShapeEditor.Web.Models;

namespace ShapeEditor.Web.Models;

/// <summary>
/// 导出路径图时传给 JS 的数据。
///
/// 必须用具体的公开类型：System.Text.Json 在序列化
/// List&lt;object&gt; 里的匿名类型时会直接报
/// SerializationNotSupportedParentType。
/// </summary>
public sealed class GraphExportPayload
{
    public List<GraphExportNode> Nodes { get; set; } = new();

    public List<GraphExportEdge> Edges { get; set; } = new();
}

public sealed class GraphExportNode
{
    public string Id { get; set; } = "";

    public int Seq { get; set; }

    public int Row { get; set; }

    public int Col { get; set; }

    public string Label { get; set; } = "";

    public bool IsResult { get; set; }

    public bool IsRef { get; set; }

    public string RefText { get; set; } = "";

    public ShapeCanvasState? State { get; set; }
}

public sealed class GraphExportEdge
{
    public string From { get; set; } = "";

    public string To { get; set; } = "";

    public string Label { get; set; } = "";

    public double LabelT { get; set; } = 0.5;

    public string Kind { get; set; } = "flow";
}

namespace ShapeEditor.Web.Services;

/// <summary>
/// 编辑器与分析页之间的传递：只带短代码，避免超长 URL。
/// </summary>
public sealed class AnalysisRequest
{
    public string? Code { get; private set; }

    public string? Source { get; private set; }

    public void Set(
        string code,
        string source)
    {
        Code = code;
        Source = source;
    }
}

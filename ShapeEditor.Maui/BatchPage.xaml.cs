using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Maui;

/// <summary>
/// 批量查看：粘贴多行短代码，逐条解析并显示预览。
/// </summary>
public partial class BatchPage : ContentPage
{
    private readonly ShortCodeParser _parser = new();

    public BatchPage()
    {
        InitializeComponent();
    }

    private void OnParse(
        object sender,
        EventArgs e)
    {
        var items = new List<BatchItem>();

        string text = CodeEditor.Text ?? "";

        foreach (string raw in text.Split('\n'))
        {
            string code = raw.Trim();

            if (code.Length == 0)
            {
                continue;
            }

            items.Add(ParseOne(code));
        }

        List.ItemsSource = items;

        int failed = items.Count(item => item.Document is null);

        StatusLabel.Text = items.Count == 0
            ? "没有可解析的内容"
            : $"共 {items.Count} 条" + (failed > 0 ? $"，{failed} 条失败" : "");
    }

    private BatchItem ParseOne(
        string code)
    {
        if (!ShortCodeParser.TryInferDimensions(
                code,
                out int layers,
                out int parts,
                out string? inferError))
        {
            return new BatchItem(code, inferError ?? "无法推断尺寸", null);
        }

        ParseResult result = _parser.Parse(code, layers, parts);

        if (!result.Success || result.Document is null)
        {
            return new BatchItem(code, "解析失败", null);
        }

        var preview = new ShapePreviewDrawable();

        preview.SetDocument(result.Document);

        return new BatchItem(
            code,
            $"{result.Document.LayerCount} 层 × {result.Document.PartCount} 象限",
            result.Document)
        {
            Preview = preview
        };
    }

    private void OnClear(
        object sender,
        EventArgs e)
    {
        CodeEditor.Text = "";

        List.ItemsSource = null;

        StatusLabel.Text = "";
    }

    /// <summary>
    /// 列表项。
    /// </summary>
    public sealed class BatchItem
    {
        public BatchItem(
            string code,
            string detail,
            ShapeDocument? document)
        {
            Code = code;
            Detail = detail;
            Document = document;
            Preview = new ShapePreviewDrawable();
            Preview.SetDocument(document);
        }

        public string Code { get; }

        public string Detail { get; }

        public ShapeDocument? Document { get; }

        public ShapePreviewDrawable Preview { get; set; }
    }
}

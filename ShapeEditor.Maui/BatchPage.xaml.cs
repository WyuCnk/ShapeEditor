using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Maui;

/// <summary>
/// 批量查看：粘贴多行短代码，逐条解析并显示预览。
///
/// 选中条目后可以把图形交给编辑器（加到编辑区）或打开分析页，
/// 两个入口只写 AppPending，再由目标页取用。
/// </summary>
public partial class BatchPage : ContentPage
{
    private readonly ShortCodeParser _parser = new();

    public BatchPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 从编辑器 / 分析页返回时同步一次按钮可用状态
        UpdateActionButtons();
    }

    private BatchItem? SelectedItem => List.SelectedItem as BatchItem;

    private void OnParse(
        object? sender,
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

        // 重新解析后旧选中已失效
        List.SelectedItem = null;

        int failed = items.Count(item => item.Document is null);

        StatusLabel.Text = items.Count == 0
            ? "没有可解析的内容"
            : $"共 {items.Count} 条" + (failed > 0 ? $"，{failed} 条失败" : "");

        UpdateActionButtons();
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
        object? sender,
        EventArgs e)
    {
        CodeEditor.Text = "";

        List.ItemsSource = null;

        StatusLabel.Text = "";

        UpdateActionButtons();
    }

    #region 选中条目的去向

    private void OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        BatchItem? item = SelectedItem;

        ShapeDocument? document = item?.Document;

        bool hasShape = document is not null;

        AddToEditorButton.IsEnabled = hasShape;

        AnalyzeButton.IsEnabled = hasShape;

        SelectionLabel.Text = item is null
            ? "先点选一条图形"
            : hasShape
                ? $"{document!.LayerCount} 层 × {document.PartCount} 象限"
                : "该条解析失败";
    }

    /// <summary>
    /// 把选中的图形交给编辑器，并切到编辑器标签页。
    /// </summary>
    private async void OnAddToEditor(
        object? sender,
        EventArgs e)
    {
        if (SelectedItem is not BatchItem item)
        {
            return;
        }

        ShapeDocument? document = item.Document;

        if (document is null)
        {
            return;
        }

        AppPending.SetEditorLoad(new EditorLoadTarget(
            item.Code,
            ShortCodeFormat.Full,
            document.LayerCount,
            document.PartCount));

        if (!await AppNavigation.GoToEditorAsync())
        {
            AppPending.ClearEditorLoad();

            await DisplayAlertAsync("批量查看", "打开编辑器失败。", "好");
        }
    }

    /// <summary>
    /// 用选中的图形打开分析页。
    /// </summary>
    private async void OnAnalyzeSelected(
        object? sender,
        EventArgs e)
    {
        if (SelectedItem is not BatchItem item)
        {
            return;
        }

        ShapeDocument? document = item.Document;

        if (document is null)
        {
            return;
        }

        AppPending.SetAnalysis(new AnalysisTarget(
            item.Code,
            ShortCodeFormat.Full,
            document.LayerCount,
            document.PartCount));

        if (!await AppNavigation.GoToAnalysisAsync())
        {
            AppPending.ClearAnalysis();

            await DisplayAlertAsync("批量查看", "打开分析页失败。", "好");
        }
    }

    #endregion

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

using System.Windows;
using System.Windows.Controls;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Wpf.Controls;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Views;

public partial class EditorView : UserControl
{
    private readonly ShortCodeExporter _exporter = new();

    private EditorViewModel? _viewModel;

    public EditorView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.GridInvalidated -= OnGridInvalidated;
        }

        _viewModel = e.NewValue as EditorViewModel;

        if (_viewModel is not null)
        {
            _viewModel.GridInvalidated += OnGridInvalidated;
        }
    }

    private void OnGridInvalidated(
        object? sender,
        EventArgs e)
    {
        Grid.RefreshGrid();
    }

    /// <summary>
    /// 把当前 Document 导出成完整短代码，连同尺寸一起交给分析窗口。
    /// 不省略顶部空层，保证分析时的层数与编辑器一致。
    /// </summary>
    private void OnAnalysisClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        ShapeDocument document = _viewModel.Document;

        string code = _exporter.Export(
            document,
            omitTopEmptyLayers: false);

        AnalysisWindow.ShowFor(
            Window.GetWindow(this),
            code,
            document.LayerCount,
            document.PartCount,
            ShortCodeFormat.Full);
    }

    private void OnCellPaintRequested(
        object? sender,
        CellPositionEventArgs e)
    {
        _viewModel?.PaintCell(e.Layer, e.Part);
    }

    private void OnCellSelected(
        object? sender,
        CellPositionEventArgs e)
    {
        _viewModel?.SelectCell(e.Layer, e.Part);
    }

    private void OnStrokeStarted(
        object? sender,
        EventArgs e)
    {
        _viewModel?.BeginStroke();
    }

    private void OnStrokeEnded(
        object? sender,
        EventArgs e)
    {
        _viewModel?.EndStroke();
    }
}

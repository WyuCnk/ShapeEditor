using System.Windows;
using System.Windows.Controls;
using ShapeEditor.Wpf.Controls;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Views;

public partial class EditorView : UserControl
{
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

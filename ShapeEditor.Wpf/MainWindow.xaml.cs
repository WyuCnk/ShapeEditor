using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShapeEditor.Wpf.ViewModels;
using ShapeEditor.Wpf.Views;

namespace ShapeEditor.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    private ShortcutHelpWindow? _helpWindow;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
    }

    private void OnShortcutHelpClick(
        object sender,
        RoutedEventArgs e)
    {
        ShowShortcutHelp();
    }

    private void ShowShortcutHelp()
    {
        if (_helpWindow is not null &&
            _helpWindow.IsVisible)
        {
            _helpWindow.Activate();

            return;
        }

        _helpWindow = new ShortcutHelpWindow
        {
            Owner = this
        };

        _helpWindow.Closed += (_, _) => _helpWindow = null;

        _helpWindow.Show();
    }

    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        /*
         * 只处理明确的全局快捷键；
         * 输入框内的常规编辑按键保持系统行为。
         */
        bool focusInTextBox = Keyboard.FocusedElement is TextBox;

        if (e.Key == Key.F1)
        {
            ShowShortcutHelp();

            e.Handled = true;

            return;
        }

        if (_viewModel.IsEditorVisible)
        {
            HandleEditorKey(e, focusInTextBox);
        }
        else
        {
            HandleBatchKey(e, focusInTextBox);
        }
    }

    private void HandleEditorKey(
        KeyEventArgs e,
        bool focusInTextBox)
    {
        EditorViewModel editor = _viewModel.Editor;

        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    editor.Undo();

                    e.Handled = true;

                    return;

                case Key.Y:
                    editor.Redo();

                    e.Handled = true;

                    return;

                case Key.D0:
                case Key.NumPad0:
                    editor.ResetView();

                    e.Handled = true;

                    return;

                case Key.O:
                    editor.ImportSaveFile();

                    e.Handled = true;

                    return;

                case Key.S:
                    if (shift)
                    {
                        editor.ExportImage();
                    }
                    else
                    {
                        editor.ExportSaveFile();
                    }

                    e.Handled = true;

                    return;

                case Key.Enter:
                    editor.ParseCode();

                    e.Handled = true;

                    return;

                case Key.C:
                    if (!focusInTextBox)
                    {
                        editor.CopyCode();

                        e.Handled = true;
                    }

                    return;

                case Key.V:
                    if (!focusInTextBox)
                    {
                        editor.PasteCode();

                        e.Handled = true;
                    }

                    return;
            }
        }

        if (focusInTextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                editor.CancelInteraction();

                e.Handled = true;

                break;

            case Key.Delete:
                editor.ClearSelectedCell();

                e.Handled = true;

                break;

            case Key.Up:
                if (editor.IsMoveMode)
                {
                    editor.MoveUp();

                    e.Handled = true;
                }

                break;

            case Key.Down:
                if (editor.IsMoveMode)
                {
                    editor.MoveDown();

                    e.Handled = true;
                }

                break;

            case Key.Left:
                if (editor.IsMoveMode)
                {
                    editor.MoveLeft();

                    e.Handled = true;
                }

                break;

            case Key.Right:
                if (editor.IsMoveMode)
                {
                    editor.MoveRight();

                    e.Handled = true;
                }

                break;
        }
    }

    private void HandleBatchKey(
        KeyEventArgs e,
        bool focusInTextBox)
    {
        if (focusInTextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                _viewModel.Batch.ShowPrevious();

                e.Handled = true;

                break;

            case Key.Right:
                _viewModel.Batch.ShowNext();

                e.Handled = true;

                break;

            case Key.PageUp:
                _viewModel.Batch.ShowPreviousPage();

                e.Handled = true;

                break;

            case Key.PageDown:
                _viewModel.Batch.ShowNextPage();

                e.Handled = true;

                break;
        }
    }
}

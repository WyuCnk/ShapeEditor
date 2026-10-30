using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
    }

    private WorkspaceViewModel? ViewModel => DataContext as WorkspaceViewModel;

    private static T? DataOf<T>(object sender)
        where T : class
    {
        return (sender as FrameworkElement)?.DataContext as T;
    }

    private void OnTabClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceTabViewModel>(sender) is WorkspaceTabViewModel tab &&
            ViewModel is WorkspaceViewModel viewModel)
        {
            viewModel.SelectedTab = viewModel.Tabs.FirstOrDefault(entry => entry.Id == tab.Id)
                                     ?? tab;
        }
    }

    private void OnTabDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataOf<WorkspaceTabViewModel>(sender) is not WorkspaceTabViewModel tab ||
            ViewModel is not WorkspaceViewModel viewModel)
        {
            return;
        }

        var dialog = new InputDialog("重命名工作区", tab.Name)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.Value.Length > 0)
        {
            viewModel.RenameProject(tab, dialog.Value);
        }

        e.Handled = true;
    }

    private void OnNewProject(
        object sender,
        RoutedEventArgs e)
    {
        ViewModel?.NewProjectCommand.Execute(null);
    }

    private void OnModeCycle(
        object sender,
        RoutedEventArgs e)
    {
        ViewModel?.SetMode(Core.Physics.CollapseMode.IncludeCycles);
    }

    private void OnModeFloor(
        object sender,
        RoutedEventArgs e)
    {
        ViewModel?.SetMode(Core.Physics.CollapseMode.FloorOnly);
    }

    private void OnNodeClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.HandleNodeClick(node);
        }
    }

    private void OnDeleteNode(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.DeleteNodeCommand.Execute(node);
        }
    }

    /// <summary>
    /// 堆叠待选时，按 Esc 取消并清掉尚未使用的输入槽。
    /// </summary>
    private void OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Escape ||
            Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (ViewModel is not WorkspaceViewModel viewModel)
        {
            return;
        }

        viewModel.CancelStack();

        e.Handled = true;
    }
}

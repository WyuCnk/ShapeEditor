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
            ViewModel?.SelectNodeCommand.Execute(node);
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

    private void OnImportMaterial(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.ImportMaterialCommand.Execute(node);
        }
    }

    private void OnReferenceSelected(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.ReferenceSelectedCommand.Execute(node);
        }
    }

    private void OnComputeStack(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.ComputeStackCommand.Execute(node);
        }
    }

    private void OnCollapse(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.CollapseCommand.Execute(node);
        }
    }

    private void OnPin(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.PinCommand.Execute(node);
        }
    }

    private void OnCut(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.CutCommand.Execute(node);
        }
    }

    private void OnStackBelow(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.StackBelowCommand.Execute(node);
        }
    }

    private void OnStackAbove(
        object sender,
        RoutedEventArgs e)
    {
        if (DataOf<WorkspaceNodeViewModel>(sender) is WorkspaceNodeViewModel node)
        {
            ViewModel?.StackAboveCommand.Execute(node);
        }
    }

    private void OnEdgeLabelClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataOf<WorkspaceEdgeViewModel>(sender) is WorkspaceEdgeViewModel edge)
        {
            ViewModel?.CycleLabelCommand.Execute(edge);
        }
    }
}

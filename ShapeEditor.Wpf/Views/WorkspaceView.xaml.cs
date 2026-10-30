using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Views;

public partial class WorkspaceView : UserControl
{
    /// <summary>按下素材时的起点，用于区分单击/双击和拖动。</summary>
    private Point _materialDragStart;

    private MaterialViewModel? _materialDragItem;

    private bool _materialDragArmed;

    public WorkspaceView()
    {
        InitializeComponent();

        // 切换到工作区时把键盘焦点交给根 Grid，快捷键无需先点一下画布即可使用
        IsVisibleChanged += OnWorkspaceVisibilityChanged;
    }

    private WorkspaceViewModel? ViewModel => DataContext as WorkspaceViewModel;

    /// <summary>
    /// 工作区由隐藏变为可见时，把焦点放到根 Grid。
    /// 只移动焦点，不改动选中状态；焦点已经在文本框里时不抢。
    /// </summary>
    private void OnWorkspaceVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true ||
            Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                // 排队期间焦点可能已经进入文本框，执行前再确认一次
                if (Keyboard.FocusedElement is not TextBox)
                {
                    WorkspaceRoot.Focus();
                }
            }),
            DispatcherPriority.Input);
    }

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

            // 节点点击不再交给画布空白处处理，避免刚选完第二输入就被取消
            e.Handled = true;
        }
    }

    /// <summary>
    /// 双击素材：ViewModel 会在 (80, 80) 新建节点并写入该素材。
    /// </summary>
    private void OnMaterialDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataOf<MaterialViewModel>(sender) is MaterialViewModel material &&
            ViewModel is WorkspaceViewModel viewModel)
        {
            // 双击不进入拖动状态
            _materialDragArmed = false;

            _materialDragItem = null;

            viewModel.AddMaterialAtDefaultPoint(material);

            e.Handled = true;
        }
    }

    private void OnMaterialMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataOf<MaterialViewModel>(sender) is not MaterialViewModel material)
        {
            return;
        }

        _materialDragStart = e.GetPosition(this);

        _materialDragItem = material;

        _materialDragArmed = true;
    }

    /// <summary>
    /// 按住素材移动超过系统拖动阈值后开始 DragDrop。
    /// 双击不会产生位移，因此不会误触发拖动。
    /// </summary>
    private void OnMaterialMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_materialDragArmed ||
            _materialDragItem is not MaterialViewModel material ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(this);

        if (Math.Abs(current.X - _materialDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _materialDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _materialDragArmed = false;

        _materialDragItem = null;

        var data = new DataObject(
            WorkspaceViewModel.MaterialDragFormat,
            material.Id);

        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            data,
            DragDropEffects.Copy);
    }

    private void OnCanvasDragOver(
        object sender,
        DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(WorkspaceViewModel.MaterialDragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    /// <summary>
    /// 接收素材拖放。
    /// GetPosition(CanvasSurface) 已经计入 ScrollViewer 的滚动偏移和布局缩放，
    /// 得到的就是画布内部的逻辑坐标。
    /// </summary>
    private void OnCanvasDrop(
        object sender,
        DragEventArgs e)
    {
        if (ViewModel is not WorkspaceViewModel viewModel ||
            !e.Data.GetDataPresent(WorkspaceViewModel.MaterialDragFormat) ||
            e.Data.GetData(WorkspaceViewModel.MaterialDragFormat) is not string materialId)
        {
            return;
        }

        Point point = e.GetPosition(CanvasSurface);

        viewModel.DropMaterial(materialId, point.X, point.Y);

        e.Handled = true;
    }

    /// <summary>
    /// 点击画布空白处取消堆叠待选。
    /// 节点卡片会先标记事件已处理，不会走到这里。
    /// </summary>
    private void OnCanvasMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ViewModel?.CancelStack();
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
    /// 工作区快捷键：
    /// Ctrl+C 复制选中节点的图形，Ctrl+V 粘贴，Delete 删除选中节点，Esc 取消堆叠待选。
    ///
    /// 全部复用 ViewModel 已有的公开命令/方法，不复制实现；
    /// 焦点在文本框时直接返回，避免影响文字输入和文本框自带的复制粘贴。
    /// </summary>
    private void OnWorkspaceKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (ViewModel is not WorkspaceViewModel viewModel)
        {
            return;
        }

        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.C when control:
                viewModel.CopyCommand.Execute(null);

                e.Handled = true;

                break;

            case Key.V when control:
                viewModel.PasteCommand.Execute(null);

                e.Handled = true;

                break;

            case Key.Delete:
                // 没有选中节点时不拦截按键，Delete 保持无操作
                if (viewModel.SelectedNode is not null)
                {
                    viewModel.DeleteNodeCommand.Execute(viewModel.SelectedNode);

                    e.Handled = true;
                }

                break;

            case Key.Escape:
                viewModel.CancelStack();

                e.Handled = true;

                break;
        }
    }
}

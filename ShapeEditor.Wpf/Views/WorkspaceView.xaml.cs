using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Views;

public partial class WorkspaceView : UserControl
{
    /// <summary>按下节点到开始拖动的最小位移；小于它按点击处理。</summary>
    private const double NodeDragThreshold = 4;

    /// <summary>节点左上角允许的最小坐标。</summary>
    private const double NodeMinPosition = 24;

    /// <summary>按下素材时的起点，用于区分单击/双击和拖动。</summary>
    private Point _materialDragStart;

    private MaterialViewModel? _materialDragItem;

    private bool _materialDragArmed;

    /// <summary>按下节点时鼠标在画布里的位置。</summary>
    private Point _nodeDragStart;

    /// <summary>按下节点时容器的 Canvas.Left / Canvas.Top。</summary>
    private double _nodeDragStartLeft;

    private double _nodeDragStartTop;

    /// <summary>本次按下的节点；为空表示没有节点拖动。</summary>
    private WorkspaceNodeViewModel? _nodeDragItem;

    /// <summary>节点所在的 ContentPresenter，拖动时直接改它的 Canvas 坐标。</summary>
    private ContentPresenter? _nodeDragHost;

    /// <summary>接收鼠标捕获的节点 Border。</summary>
    private UIElement? _nodeDragCapture;

    /// <summary>已按下但还没超过拖动阈值。</summary>
    private bool _nodeDragArmed;

    /// <summary>已超过阈值，正在拖动。</summary>
    private bool _nodeDragging;

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
    /// 节点按下：记录起点、当前 Canvas 坐标并捕获鼠标。
    ///
    /// 这里先接管事件，是为了避免现有“按下即选中”的刷新在拖动开始前
    /// 重建整个节点列表，导致 Border 脱离可视树、收不到后续 MouseMove。
    /// 未超过 4px 的点击会在松开时补一次选中。
    /// </summary>
    private void OnNodePreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Border border ||
            DataOf<WorkspaceNodeViewModel>(sender) is not WorkspaceNodeViewModel node ||
            ViewModel is not WorkspaceViewModel viewModel)
        {
            return;
        }

        // 节点里的 × 按钮有自己的点击逻辑，不能抢它的鼠标
        if (IsInsideButton(e.OriginalSource, border))
        {
            return;
        }

        // 堆叠等待第二输入时，点击节点就是选择第二输入，不进入拖动
        if (viewModel.IsStackPending)
        {
            return;
        }

        ContentPresenter? host = FindNodeHost(border);

        if (host is null)
        {
            return;
        }

        _nodeDragStart = e.GetPosition(CanvasSurface);

        _nodeDragStartLeft = Canvas.GetLeft(host);

        _nodeDragStartTop = Canvas.GetTop(host);

        // 绑定还没生效时（NaN）退回 ViewModel 里的实际坐标
        if (double.IsNaN(_nodeDragStartLeft))
        {
            _nodeDragStartLeft = node.Left;
        }

        if (double.IsNaN(_nodeDragStartTop))
        {
            _nodeDragStartTop = node.Top;
        }

        _nodeDragItem = node;

        _nodeDragHost = host;

        _nodeDragCapture = border;

        _nodeDragArmed = true;

        _nodeDragging = false;

        border.CaptureMouse();

        e.Handled = true;
    }

    /// <summary>
    /// 节点拖动：超过 4px 后直接把位移加到 Canvas.Left/Top 上，
    /// 不重建列表；鼠标捕获期间移出卡片也能继续拖。
    /// </summary>
    private void OnNodeMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_nodeDragArmed && !_nodeDragging)
        {
            return;
        }

        if (_nodeDragHost is null)
        {
            return;
        }

        Point current = e.GetPosition(CanvasSurface);

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            // 捕获意外丢失时按松开处理，尽量不丢最终位置
            FinishNodeDrag(current);

            return;
        }

        if (!_nodeDragging)
        {
            if (Math.Abs(current.X - _nodeDragStart.X) < NodeDragThreshold &&
                Math.Abs(current.Y - _nodeDragStart.Y) < NodeDragThreshold)
            {
                return;
            }

            _nodeDragging = true;
        }

        double left = Math.Max(
            NodeMinPosition,
            _nodeDragStartLeft + current.X - _nodeDragStart.X);

        double top = Math.Max(
            NodeMinPosition,
            _nodeDragStartTop + current.Y - _nodeDragStart.Y);

        Canvas.SetLeft(_nodeDragHost, left);

        Canvas.SetTop(_nodeDragHost, top);

        // 连线跟着节点实时重算，松手前箭头不会停在旧位置
        if (_nodeDragItem is not null &&
            ViewModel is WorkspaceViewModel viewModel)
        {
            viewModel.UpdateEdgesForDrag(
                _nodeDragItem,
                left,
                top);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 节点松开：释放捕获；没拖动则按点击交给原有选中逻辑，
    /// 拖动过则写回坐标并让 Store 触发刷新（节点与连线一起重算）。
    /// </summary>
    private void OnNodePreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_nodeDragArmed && !_nodeDragging)
        {
            return;
        }

        FinishNodeDrag(e.GetPosition(CanvasSurface));

        e.Handled = true;
    }

    private void FinishNodeDrag(
        Point current)
    {
        WorkspaceNodeViewModel? node = _nodeDragItem;

        UIElement? capture = _nodeDragCapture;

        bool dragged = _nodeDragging;

        // 先清空状态再释放捕获，避免后续事件读到半截拖动状态
        _nodeDragArmed = false;

        _nodeDragging = false;

        _nodeDragItem = null;

        _nodeDragHost = null;

        _nodeDragCapture = null;

        capture?.ReleaseMouseCapture();

        if (node is null || ViewModel is not WorkspaceViewModel viewModel)
        {
            return;
        }

        if (!dragged)
        {
            // 位移小于 4px：按点击处理，仍然可以选择节点/作为堆叠第二输入
            viewModel.HandleNodeClick(node);

            return;
        }

        double left = Math.Max(
            NodeMinPosition,
            _nodeDragStartLeft + current.X - _nodeDragStart.X);

        double top = Math.Max(
            NodeMinPosition,
            _nodeDragStartTop + current.Y - _nodeDragStart.Y);

        viewModel.MoveNodeTo(
            node,
            left,
            top);
    }

    /// <summary>从事件源向上找 ContentPresenter，也就是 Canvas 里的节点容器。</summary>
    private static ContentPresenter? FindNodeHost(
        DependencyObject element)
    {
        DependencyObject? current = element;

        while (current is not null)
        {
            if (current is ContentPresenter presenter)
            {
                return presenter;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : null;
        }

        return null;
    }

    /// <summary>判断事件源是不是节点卡片里的按钮（例如 ×）。</summary>
    private static bool IsInsideButton(
        object? originalSource,
        DependencyObject stopAt)
    {
        DependencyObject? current = originalSource as DependencyObject;

        while (current is not null &&
               !ReferenceEquals(current, stopAt))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : null;
        }

        return false;
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

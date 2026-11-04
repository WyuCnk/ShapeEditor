using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区页面：多工作区标签、素材列表、制作路径图。
///
/// 节点卡片只负责展示与选中，操作统一放在工具条里，
/// 连线分两层：线段在节点下方，箭头在节点上方。
/// </summary>
public partial class WorkspacePage : ContentPage
{
    /// <summary>结果节点与源节点的垂直间距。</summary>
    private const double ResultGap = 56;

    /// <summary>切割结果相对源节点的水平偏移。</summary>
    private const double CutOffset = 260;

    private static readonly Color Accent = Color.FromArgb("#512BD4");

    private static readonly Color Muted = Color.FromArgb("#6B6B76");

    /// <summary>拖拽素材时写入数据包的键，用于在 Drop 时找回素材。</summary>
    private const string MaterialDragKey = "shapeeditor.materialId";

    /// <summary>
    /// 拖动节点时，相邻两帧累计位移允许的最大跳变量（屏幕像素）。
    ///
    /// 手指在两次回调之间不可能移动这么远，超过就说明平台中途换了触点
    /// （典型是第二根手指抬起后 MotionEvent 从另一根手指取坐标），
    /// 这时重新取基准：宁可少走一段，也不让节点瞬移。
    /// </summary>
    private const double MaxDragStep = 200;

    private WorkspaceStore? _store;

    private WorkspaceItem? _selectedMaterial;

    private NodeClipboard? _clipboard;

    /// <summary>堆叠第一步选中的节点 Id；为空表示不在堆叠选择状态。</summary>
    private string? _stackFirstId;

    /// <summary>素材卡片与该素材 Id 的对应关系，选中有变化时只改边框，不重建卡片。</summary>
    private readonly Dictionary<string, Border> _materialBorders = new();

    /// <summary>渲染画布时为负坐标做的整体平移，Drop 时要把视觉坐标换回节点坐标。</summary>
    private double _canvasShiftX;

    private double _canvasShiftY;

    /// <summary>当前画布上的节点卡片，拖动时只改布局，不重建。</summary>
    private readonly Dictionary<string, Border> _nodeCards = new();

    /// <summary>节点当前的视觉矩形（已含整体平移），拖动时用来实时重算连线。</summary>
    private readonly Dictionary<string, Rect> _nodeBounds = new();

    /// <summary>正在进行的节点拖动；null 表示当前没有在拖节点（全局单拖动锁）。</summary>
    private NodeDragSession? _nodeDrag;

    /// <summary>线段层与箭头层，拖动过程中只重绘这两个 Drawable。</summary>
    private WorkspaceLineDrawable? _lineDrawable;

    private WorkspaceArrowDrawable? _arrowDrawable;

    private GraphicsView? _lineLayer;

    private GraphicsView? _arrowLayer;

    /// <summary>
    /// 当前被拖动锁挡住的卡片：已有节点在拖时，其它卡片的手势整段忽略。
    /// </summary>
    private readonly HashSet<Border> _blockedPanCards = new();

    public WorkspacePage()
    {
        InitializeComponent();

        // 画布 / 滚动宿主的原生视图换过（首次显示、旋转等）就重新挂一次多指监听
        Canvas.HandlerChanged += (_, _) => AttachCanvasTouchSuppression();

        ScaledHost.HandlerChanged += (_, _) => AttachCanvasTouchSuppression();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _store ??= AppServices.Workspace;

        _store.Changed -= Render;
        _store.Changed += Render;

        AttachCanvasTouchSuppression();

        Render();
    }

    protected override void OnDisappearing()
    {
        if (_store is not null)
        {
            _store.Changed -= Render;
        }

        DetachCanvasTouchSuppression();

        base.OnDisappearing();
    }

    #region 渲染

    private bool _rendering;

    private void Render()
    {
        if (_store is null || _rendering)
        {
            return;
        }

        _rendering = true;

        try
        {
            RenderCore();
        }
        catch (Exception exception)
        {
            // 渲染异常写日志，避免直接把 App 打崩
            try
            {
                File.AppendAllText(
                    Path.Combine(FileSystem.AppDataDirectory, "workspace-error.log"),
                    DateTime.Now + " " + exception + Environment.NewLine);
            }
            catch
            {
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderCore()
    {

        RenderTabs();
        RenderMaterials();
        RenderCanvas();

        CycleModeButton.Text = _store.Mode == CollapseMode.IncludeCycles
            ? "含循环支撑"
            : "仅地面";

        MaxLayersEntry.Text = _store.MaxLayers.ToString();

        // 等第二个输入期间如果底节点被删掉，自动退出堆叠 / 交换状态
        if (_stackFirstId is not null && _store.FindNode(_stackFirstId) is null)
        {
            _stackFirstId = null;
        }

        if (_exchangeFirstId is not null && _store.FindNode(_exchangeFirstId) is null)
        {
            _exchangeFirstId = null;
        }

        WorkspaceNode? selected = _store.SelectedNode;

        // 节点与素材二选一：通过任何入口选中节点后都清掉素材选中，
        // 让「加到编辑区 / 分析」的目标始终唯一。
        if (selected is not null && _selectedMaterial is not null)
        {
            _selectedMaterial = null;

            UpdateMaterialSelection();
        }

        bool waitingSecondInput = _stackFirstId is not null || _exchangeFirstId is not null;

        StatusLabel.Text = waitingSecondInput
            ? "再点一个节点作为第二个输入"
            : $"素材 {_store.Materials.Count} 条 · 节点 {_store.Nodes.Count} 个" +
              (selected is null ? "" : $" · 已选中 #{selected.Seq}");

        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        bool hasSelection = _store is not null &&
                            _store.SelectedNode is WorkspaceNode selected &&
                            _store.FindNode(selected.Id) is not null;

        CollapseButton.IsEnabled = hasSelection;
        PinButton.IsEnabled = hasSelection;
        CutButton.IsEnabled = hasSelection;

        // 等待第二输入时也要能再点一次「堆叠」取消
        StackButton.IsEnabled = hasSelection || _stackFirstId is not null;

        // 等待第二输入时也要能再点一次「交换」取消
        ExchangeButton.IsEnabled = hasSelection || _exchangeFirstId is not null;

        CopyButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;

        // 自动布局不依赖选中项，只要画布上有节点就可用
        AutoLayoutButton.IsEnabled = _store is not null && _store.Nodes.Count > 0;

        // 选中带图形的素材或节点时，才能交给编辑器 / 打开分析页
        bool hasTarget = HasSelectableShape();

        AddToEditorButton.IsEnabled = hasTarget;

        AnalyzeButton.IsEnabled = hasTarget;

        // 粘贴还需要剪贴板里有内容
        PasteButton.IsEnabled = hasSelection && _clipboard is not null;
    }

    private void RenderTabs()
    {
        TabBar.Clear();

        foreach (WorkspaceProject project in _store!.Projects)
        {
            bool active = project.Id == _store.Active?.Id;

            var button = new Button
            {
                Text = project.Name,
                FontSize = 12,
                HeightRequest = 32,
                CornerRadius = 8,
                Padding = new Thickness(10, 0),
                BackgroundColor = active ? Accent : Color.FromArgb("#EFEAF9"),
                TextColor = active ? Colors.White : Accent
            };

            string id = project.Id;

            button.Clicked += (_, _) => _store.ActivateProject(id);

            TabBar.Add(button);
        }

        var add = new Button
        {
            Text = "+",
            FontSize = 14,
            WidthRequest = 36,
            HeightRequest = 32,
            CornerRadius = 8,
            BackgroundColor = Color.FromArgb("#EFEAF9"),
            TextColor = Accent
        };

        add.Clicked += (_, _) => _store!.CreateProject();

        TabBar.Add(add);

        var export = new Button
        {
            Text = "导出",
            FontSize = 12,
            HeightRequest = 32,
            CornerRadius = 8,
            Padding = new Thickness(10, 0),
            BackgroundColor = Color.FromArgb("#EFEAF9"),
            TextColor = Accent
        };

        export.Clicked += async (_, _) => await ExportProjectAsync();

        TabBar.Add(export);
    }

    private void RenderMaterials()
    {
        MaterialList.Clear();

        _materialBorders.Clear();

        // 素材可能已在别处被删除（例如刚点了卡片上的删除按钮），先清掉失效的选中
        if (_selectedMaterial is not null &&
            !_store!.Materials.Any(item => item.Id == _selectedMaterial.Id))
        {
            _selectedMaterial = null;
        }

        foreach (WorkspaceItem item in _store!.Materials)
        {
            var preview = new GraphicsView
            {
                HeightRequest = 72,
                WidthRequest = 96,
                InputTransparent = true,
                Drawable = PreviewOf(item.Code, item.Format, item.Layers, item.Parts)
            };

            bool selected = item.Id == _selectedMaterial?.Id;

            WorkspaceItem captured = item;

            // 名称行：名称 + 删除按钮（用 Button.Clicked，不加任何双击手势）
            var nameRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 2
            };

            nameRow.Add(new Label
            {
                Text = item.Name,
                FontSize = 10,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, 0);

            var delete = new Button
            {
                Text = "×",
                FontSize = 11,
                WidthRequest = 20,
                HeightRequest = 20,
                Padding = 0,
                MinimumWidthRequest = 0,
                MinimumHeightRequest = 0,
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#A00000")
            };

            delete.Clicked += async (_, _) => await DeleteMaterialAsync(captured);

            nameRow.Add(delete, 1, 0);

            var border = new Border
            {
                Stroke = selected ? Accent : Color.FromArgb("#E6E2F2"),
                StrokeThickness = selected ? 2 : 1,
                Padding = 4,
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        preview,
                        nameRow
                    }
                }
            };

            // 单击只切换素材选中；不重建卡片，避免双击/拖拽的手势目标被顶掉
            var tap = new TapGestureRecognizer();

            tap.Tapped += (_, _) =>
            {
                _selectedMaterial = captured;

                // 素材与节点是二选一：选素材时清掉节点选中，
                // 否则「加到编辑区 / 分析」会分不清目标。
                if (_store?.SelectedNode is not null)
                {
                    _store.SelectNode(null);
                }

                UpdateMaterialSelection();

                UpdateActionButtons();
            };

            border.GestureRecognizers.Add(tap);

            // 拖拽：MAUI 的 DragGestureRecognizer 没有 Data 属性，
            // 素材 Id 通过 DragStarting 的数据包（Properties + Text）交给画布
            var drag = new DragGestureRecognizer
            {
                CanDrag = true
            };

            drag.DragStarting += (_, e) =>
            {
                e.Data.Properties.Add(MaterialDragKey, captured.Id);
                e.Data.Text = captured.Id;
            };

            border.GestureRecognizers.Add(drag);

            _materialBorders[item.Id] = border;

            MaterialList.Add(border);
        }
    }

    /// <summary>
    /// 删除素材：先用 DisplayAlert 二次确认，删除后清空指向它的选中状态。
    /// 这里始终走 Button.Clicked，不使用双击手势。
    /// </summary>
    private async Task DeleteMaterialAsync(
        WorkspaceItem item)
    {
        bool confirmed = await DisplayAlertAsync(
            "删除素材",
            $"将从素材库删除「{item.Name}」。\n\n此操作不可撤销，继续？",
            "删除",
            "取消");

        if (!confirmed || _store is null)
        {
            return;
        }

        // 先在内存里清掉选中，再让 store 触发重绘，避免重绘后又渲染出选中框
        if (_selectedMaterial?.Id == item.Id)
        {
            _selectedMaterial = null;
        }

        _store.RemoveMaterial(item.Id);

        UpdateMaterialSelection();

        UpdateActionButtons();
    }

    /// <summary>只刷新素材卡片边框，保持卡片视图不被重建。</summary>
    private void UpdateMaterialSelection()
    {
        foreach (KeyValuePair<string, Border> pair in _materialBorders)
        {
            bool selected = pair.Key == _selectedMaterial?.Id;

            pair.Value.Stroke = selected ? Accent : Color.FromArgb("#E6E2F2");
            pair.Value.StrokeThickness = selected ? 2 : 1;
        }
    }

    private void RenderCanvas()
    {
        Canvas.Clear();

        // 每次完整重绘都会换掉卡片与两个连线层，旧引用一并清掉
        _nodeCards.Clear();
        _nodeBounds.Clear();

        // 卡片视图马上要换新，拖动会话没有可用目标了，直接丢掉（不回滚位置）
        AbortNodeDrag();

        _lineDrawable = null;
        _arrowDrawable = null;
        _lineLayer = null;
        _arrowLayer = null;

        if (_store!.Nodes.Count == 0)
        {
            _canvasShiftX = 0;
            _canvasShiftY = 0;

            // 画布尺寸与滚动宿主的请求尺寸一起更新（缩放比例保持当前值）
            SetCanvasContentSize(320, 320);

            return;
        }

        List<WorkspaceNodeBox> boxes = WorkspaceLayout.Compute(
            _store.Nodes,
            _store.GetEffectiveDocument);

        // 自由坐标可能为负（例如切割的左下结果），整体平移到 24px 边距以内
        double minLeft = boxes.Min(box => box.Bounds.X);
        double minTop = boxes.Min(box => box.Bounds.Y);

        double shiftX = minLeft < WorkspaceLayout.Margin
            ? WorkspaceLayout.Margin - minLeft
            : 0;

        double shiftY = minTop < WorkspaceLayout.Margin
            ? WorkspaceLayout.Margin - minTop
            : 0;

        // Drop 的落点是视觉坐标，换回节点坐标时要用这里的平移量
        _canvasShiftX = shiftX;
        _canvasShiftY = shiftY;

        var placed = new List<WorkspaceNodeBox>(boxes.Count);

        foreach (WorkspaceNodeBox box in boxes)
        {
            placed.Add(new WorkspaceNodeBox(
                box.Node,
                new Rect(
                    box.Bounds.X + shiftX,
                    box.Bounds.Y + shiftY,
                    box.Bounds.Width,
                    box.Bounds.Height)));
        }

        double width = Math.Max(
            320,
            placed.Max(box => box.Bounds.Right) + WorkspaceLayout.Margin);

        double height = Math.Max(
            320,
            placed.Max(box => box.Bounds.Bottom) + WorkspaceLayout.Margin);

        // 画布逻辑尺寸变了：卡片布局坐标不变，只同步缩放后的滚动范围
        SetCanvasContentSize(width, height);

        var lookup = placed.ToDictionary(box => box.Node.Id);

        var lineDrawable = new WorkspaceLineDrawable();

        var arrowDrawable = new WorkspaceArrowDrawable();

        // 保存到字段，拖动节点时直接改这两个 Drawable 并 Invalidate
        _lineDrawable = lineDrawable;
        _arrowDrawable = arrowDrawable;

        foreach (WorkspaceEdge edge in _store.Edges)
        {
            if (!lookup.TryGetValue(edge.FromId, out WorkspaceNodeBox? from) ||
                !lookup.TryGetValue(edge.ToId, out WorkspaceNodeBox? to))
            {
                continue;
            }

            WorkspaceLink link = WorkspaceLinkGeometry.Create(
                from.Bounds,
                to.Bounds);

            lineDrawable.Links.Add(link);
            arrowDrawable.Links.Add(link);
        }

        // 线段层：在节点之下
        var lineLayer = new GraphicsView
        {
            InputTransparent = true,
            Drawable = lineDrawable
        };

        AbsoluteLayout.SetLayoutBounds(
            lineLayer,
            new Rect(0, 0, width, height));

        _lineLayer = lineLayer;

        Canvas.Add(lineLayer);

        foreach (WorkspaceNodeBox box in placed)
        {
            Border card = BuildNodeCard(
                box.Node,
                _store.GetEffectiveDocument(box.Node));

            // 记录卡片和视觉矩形，拖动时只更新它们的布局
            _nodeCards[box.Node.Id] = card;
            _nodeBounds[box.Node.Id] = box.Bounds;

            AbsoluteLayout.SetLayoutBounds(card, box.Bounds);

            Canvas.Add(card);
        }

        // 箭头层：固定在节点之上；即使暂时没有箭头也保留，拖动时可以直接重绘
        var arrowLayer = new GraphicsView
        {
            InputTransparent = true,
            Drawable = arrowDrawable
        };

        AbsoluteLayout.SetLayoutBounds(
            arrowLayer,
            new Rect(0, 0, width, height));

        _arrowLayer = arrowLayer;

        Canvas.Add(arrowLayer);
    }

    /// <summary>
    /// 节点卡片：编号 + 标题、× 删除、竖式图预览、层数 × 象限数摘要。
    /// 卡片内不放操作按钮，也不放滚动条。
    /// </summary>
    private Border BuildNodeCard(
        WorkspaceNode node,
        ShapeDocument? document)
    {
        bool selected = node.Id == _store!.SelectedNode?.Id;

        int layers = WorkspaceLayout.LayersOf(node, document);
        int parts = WorkspaceLayout.PartsOf(node, document);

        double width = WorkspaceLayout.WidthOf(node, document);
        double height = WorkspaceLayout.HeightOf(node, document);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(8, 0, 2, 0)
        };

        header.Add(new Label
        {
            Text = $"#{node.Seq} {WorkspaceLayout.TitleOf(node)}",
            FontSize = 10,
            TextColor = Muted,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center
        }, 0);

        var delete = new Button
        {
            Text = "×",
            FontSize = 12,
            WidthRequest = 22,
            HeightRequest = 22,
            Padding = 0,
            MinimumWidthRequest = 0,
            MinimumHeightRequest = 0,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#A00000")
        };

        delete.Clicked += (_, _) => _store!.DeleteNode(node.Id);

        header.Add(delete, 1);

        var content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(new GridLength(WorkspaceLayout.HeaderHeight)),
                new RowDefinition(new GridLength(layers * WorkspaceLayout.CellSize)),
                new RowDefinition(new GridLength(WorkspaceLayout.SummaryHeight))
            }
        };

        content.Add(header, 0, 0);

        if (document is not null && layers > 0)
        {
            var preview = new GraphicsView
            {
                WidthRequest = parts * WorkspaceLayout.CellSize,
                HeightRequest = layers * WorkspaceLayout.CellSize,
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Start,
                Drawable = new WorkspaceCellDrawable(document)
            };

            content.Add(preview, 0, 1);
        }

        content.Add(new Label
        {
            Text = document is null ? "空" : ShapeText.Summary(document),
            FontSize = 10,
            TextColor = Muted,
            Margin = new Thickness(8, 0),
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center
        }, 0, 2);

        var border = new Border
        {
            WidthRequest = width,
            HeightRequest = height,
            Stroke = selected ? Accent : Color.FromArgb("#D0D0D0"),
            StrokeThickness = selected ? 2 : 1,
            BackgroundColor = node.IsResult ? Color.FromArgb("#FAF8FF") : Colors.White,
            Padding = 0,
            Content = content
        };

        var tap = new TapGestureRecognizer();

        tap.Tapped += (_, _) =>
        {
            // 缩放过程中（及刚结束的一小段时间）不触发节点选中
            if (IsPinching)
            {
                return;
            }

            // 删除按钮和卡片点击可能落在同一帧里，先确认节点还在
            if (_store!.FindNode(node.Id) is null)
            {
                return;
            }

            // 交换第二步：这个节点作为第二个输入
            if (_exchangeFirstId is not null)
            {
                CompleteExchange(node);

                return;
            }

            // 堆叠第二步：这个节点作为第二个输入
            if (_stackFirstId is not null)
            {
                CompleteStack(node);

                return;
            }

            // 节点与素材是二选一：选节点时清掉素材选中，
            // 让「加到编辑区 / 分析」的目标没有歧义。
            _selectedMaterial = null;

            _store.SelectNode(node);
        };

        border.GestureRecognizers.Add(tap);

        /*
         * 手指一落到卡片上，先让外层的 ScrollView 放弃拦截本次手势。
         *
         * Android 的 ScrollView 在手指移动超过阈值时会抢先拦截整个手势，
         * 卡片上的 Pan 会立刻收到 Canceled；而 Pan 的 Started 往往在那之后才发出，
         * 事后再禁用滚动就来不及了。按下时就请求「本次手势不要拦截」，
         * 节点拖动与画布滚动才不会互相抢，画布也就只剩一套坐标。
         */
        var pointer = new PointerGestureRecognizer();

        pointer.PointerPressed += (_, _) => SuppressScrollIntercept(border);

        border.GestureRecognizers.Add(pointer);

        /*
         * 拖动节点：PanUpdated 里只改卡片的 LayoutBounds 和两个连线层，
         * 不写 Translation、不碰节点坐标；松手后才调用 SetNodePosition 落盘，
         * 整页 Render 也只发生在松手之后。
         */
        var pan = new PanGestureRecognizer();

        pan.PanUpdated += (_, e) => OnNodePanUpdated(
            node.Id,
            border,
            e);

        border.GestureRecognizers.Add(pan);

        /*
         * 触摸落在节点卡片上时事件不会冒泡到画布容器，
         * 所以卡片也挂一份捏合手势，并与画布共用同一个处理函数；
         * 缩放的是整块画布，卡片自身不缩放。
         */
        var pinch = new PinchGestureRecognizer();

        pinch.PinchUpdated += OnCanvasPinchUpdated;

        border.GestureRecognizers.Add(pinch);

        return border;
    }

    /// <summary>
    /// 拖动节点卡片。
    ///
    /// Running 阶段只改卡片的 LayoutBounds 并重绘两个连线层，
    /// 不写 Translation、不改节点坐标、不重建卡片、不整页重绘、不落盘；
    /// Completed / Canceled 时把最终坐标交给 store，由 store 负责 10px 吸附与保存。
    /// 捏合期间（及刚结束的冷却期）不处理拖动，双指缩放不会把节点带走。
    /// 全局只允许一个节点在拖：已有拖动时，其它卡片的手势整段忽略。
    /// </summary>
    private void OnNodePanUpdated(
        string nodeId,
        Border card,
        PanUpdatedEventArgs e)
    {
        if (_store is null)
        {
            _blockedPanCards.Remove(card);

            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                BeginNodeDrag(nodeId, card);

                return;

            case GestureStatus.Running:
                // 被单拖动锁挡住的卡片：整段手势都不参与
                if (_blockedPanCards.Contains(card))
                {
                    return;
                }

                MoveNodeDrag(
                    nodeId,
                    card,
                    e.TotalX,
                    e.TotalY);

                return;

            default:
                _blockedPanCards.Remove(card);

                EndNodeDrag(nodeId, card);

                return;
        }
    }

    /// <summary>
    /// 开始一次节点拖动：记下起始矩形，之后的位移都以它为基准。
    /// </summary>
    private void BeginNodeDrag(
        string nodeId,
        Border card)
    {
        // 捏合中、或刚结束捏合还在冷却期：这次手势不当成拖动
        if (IsPinching)
        {
            _blockedPanCards.Remove(card);

            if (_nodeDrag is not null)
            {
                // 万一上一段拖动没收到结束回调，这里一并还原，不留半途位置
                CancelNodeDrag();
            }

            return;
        }

        // 卡片若已被整页重绘替换，旧卡片上的手势直接忽略，避免改到失效的视图
        if (!_nodeCards.TryGetValue(nodeId, out Border? current) ||
            !ReferenceEquals(current, card) ||
            !_nodeBounds.TryGetValue(nodeId, out Rect origin))
        {
            return;
        }

        if (_store!.FindNode(nodeId) is not WorkspaceNode node)
        {
            return;
        }

        /*
         * 全局单拖动锁：已经有节点在拖时，第二张卡片的手势整段忽略。
         *
         * 双指分别落在两个节点上时，两张卡片都会收到 Started；
         * 这里绝不能「接管」会话，否则两个节点会同时跟着手指走。
         *
         * 同一张卡片的手势重启（父容器抢手势后归还、第二根手指按下又抬起等）
         * 则要保留已经走过的位移：平台会把累计位移重新从 0 开始发，
         * 只清基准会让卡片往回跳一段，跳的距离正好是「累计位移 ÷ 缩放」，
         * 缩放越小跳得越远，拖起来就是明显的来回抖动。
         * 正确做法是把已走过的位移冻结进 Rebase，再让下一帧重新取基准。
         */
        if (_nodeDrag is not null)
        {
            if (_nodeDrag.NodeId == nodeId &&
                ReferenceEquals(_nodeDrag.Card, card))
            {
                _nodeDrag.FreezeDisplacement(
                    _canvasScale > 0 ? 1d / _canvasScale : 1d);

                _nodeDrag.ResetTotalBase();

                return;
            }

            _blockedPanCards.Add(card);

            return;
        }

        // 新的一次拖动：清掉上一轮可能残留的忽略标记（丢过 Completed 也不会永久卡住）
        _blockedPanCards.Clear();

        _nodeDrag = new NodeDragSession(
            nodeId,
            card,
            origin,
            node.X,
            node.Y);

        /*
         * 上一次拖动如果没走到 Completed（例如被整页重绘打断），
         * 卡片上可能还挂着旧方案留下的 Translation；这里归零，
         * 新方案只改 LayoutBounds，不再叠加位移。
         */
        card.TranslationX = 0;

        card.TranslationY = 0;

        // 这次手势直到抬手都不要被 ScrollView 拦截
        SuppressScrollIntercept(card);
    }

    /// <summary>
    /// 拖动进行中：位置 = 起始矩形 + （当前累计位移 − 基准累计位移）÷ 缩放比例。
    ///
    /// 只用「起点 + 累计位移」算绝对位置，绝不把每帧位移加到当前位置上，
    /// 这样误差不会累积，平台重发手势也不会把位移重复计入。
    /// 结果直接写进卡片的 LayoutBounds（不写 Translation、不取整），
    /// 节点坐标不动，落盘留到 <see cref="EndNodeDrag"/> 一次完成。
    /// </summary>
    private void MoveNodeDrag(
        string nodeId,
        Border card,
        double totalX,
        double totalY)
    {
        NodeDragSession? session = _nodeDrag;

        // 不是当前会话的回调（卡片已重绘 / 已被捏合取消）：什么都不做
        if (session is null ||
            session.NodeId != nodeId ||
            !ReferenceEquals(session.Card, card))
        {
            return;
        }

        // 捏合中途才开始：结束这次拖动并把节点放回起点，别让缩放带出半个位移
        if (IsPinching)
        {
            CancelNodeDrag();

            return;
        }

        // 第一帧 Running 只用来取基准：不假设平台把 TotalX/TotalY 从 0 开始
        if (!session.HasTotalBase)
        {
            session.SetTotalBase(
                totalX,
                totalY);

            return;
        }

        // 触点被平台换过（第二根手指抬起等）：重新取基准，宁可少走一段也不瞬移
        if (Math.Abs(totalX - session.LastTotalX) > MaxDragStep ||
            Math.Abs(totalY - session.LastTotalY) > MaxDragStep)
        {
            session.ShiftTotalBase(
                totalX,
                totalY);

            return;
        }

        session.LastTotalX = totalX;
        session.LastTotalY = totalY;

        /*
         * 画布是整体缩放的，TotalX/TotalY 是屏幕位移，
         * 除以缩放比例才是逻辑画布的位移：
         * 放大后拖动依然跟手，缩小后同样的手指位移对应更大的逻辑位移。
         */
        double inverseScale = _canvasScale > 0
            ? 1d / _canvasScale
            : 1d;

        /*
         * 左、上边界按画布外边距夹取。
         *
         * 位置始终按「起点 + 累计位移 ÷ 缩放」一次算出来，不是逐帧累加，
         * 也绝不在这里做 Math.Round：缩放后坐标本来就是小数，
         * 中途取整会让卡片在「吸附点」和手指位置之间来回跳。
         */
        double x = Math.Max(
            WorkspaceLayout.Margin,
            session.Origin.X + session.RebaseX + (totalX - session.BaseTotalX) * inverseScale);

        double y = Math.Max(
            WorkspaceLayout.Margin,
            session.Origin.Y + session.RebaseY + (totalY - session.BaseTotalY) * inverseScale);

        var current = new Rect(
            x,
            y,
            session.Origin.Width,
            session.Origin.Height);

        // 记下当前视觉矩形：松手时用它换算节点坐标，不必每帧写 store
        session.Current = current;

        _nodeBounds[nodeId] = current;

        // 整个画布只有这一套坐标：卡片位置就是 AbsoluteLayout 的布局矩形
        AbsoluteLayout.SetLayoutBounds(card, current);

        session.Moved = true;

        // 只让两个连线层重绘（不重建卡片），连线才能跟着卡片走
        UpdateLinkLayers();
    }

    /// <summary>
    /// 拖动结束：按当前手指位置算出最终坐标交给 store（10px 吸附 + 落盘 + 整页重绘）。
    /// 整页重绘与落盘只在这里发生一次。
    /// </summary>
    private void EndNodeDrag(
        string nodeId,
        Border card)
    {
        NodeDragSession? session = _nodeDrag;

        // 不是当前节点的回调：当前会话还没结束，别人的收尾不能影响它
        if (session is null || session.NodeId != nodeId)
        {
            return;
        }

        // 同一个节点的手势结束就一定清状态，否则拖动锁会一直挡着其它节点
        _nodeDrag = null;

        // 一次都没移动过：布局本来就停在起点，不需要落盘
        if (!session.Moved || _store is null)
        {
            return;
        }

        // 卡片已被整页重绘替换：新卡片已按 store 坐标摆好，旧卡片不用再管
        if (!ReferenceEquals(session.Card, card))
        {
            return;
        }

        Rect final = session.Current;

        // 逻辑画布坐标扣掉渲染时的整体平移，才是节点自由坐标；吸附与保存由 store 负责
        _store.SetNodePosition(
            nodeId,
            final.X - _canvasShiftX,
            final.Y - _canvasShiftY);
    }

    /// <summary>
    /// 取消正在进行的节点拖动：卡片布局与节点坐标一起放回拖动起点。
    ///
    /// 双指捏合抢走手势时调用，画布上不会留下拖了一半的节点。
    /// </summary>
    private void CancelNodeDrag()
    {
        NodeDragSession? session = _nodeDrag;

        _nodeDrag = null;

        _blockedPanCards.Clear();

        if (session is null ||
            !_nodeBounds.ContainsKey(session.NodeId))
        {
            return;
        }

        _nodeBounds[session.NodeId] = session.Origin;

        if (_nodeCards.TryGetValue(session.NodeId, out Border? card) &&
            ReferenceEquals(card, session.Card))
        {
            AbsoluteLayout.SetLayoutBounds(card, session.Origin);
        }

        // 坐标一起还原；原来按行列自动排布的节点要写回 null，不能固化成自由坐标
        if (_store?.FindNode(session.NodeId) is WorkspaceNode node)
        {
            node.X = session.OriginNodeX;

            node.Y = session.OriginNodeY;
        }

        UpdateLinkLayers();
    }

    /// <summary>丢掉拖动状态但不改节点位置（卡片马上要整体重绘时用）。</summary>
    private void AbortNodeDrag()
    {
        _nodeDrag = null;

        _blockedPanCards.Clear();
    }

    /// <summary>
    /// 一次节点拖动会话。
    ///
    /// 各平台对 PanUpdated 的 TotalX/TotalY 保证并不一致：
    /// 手势可能中途重发 Started（父容器抢完手势再归还、多指切换），
    /// Total 也不一定从 0 开始。所以这里不用「拖动起点 + Total」硬算，
    /// 而是把 Started 之后第一帧 Running 的 Total 当作基准，
    /// 之后按「当前 Total − 基准」换算位移，起点始终是发起拖动时的矩形；
    /// 手势重启时把已走过的位移冻结进 Rebase，卡片就不会跳回原点。
    /// </summary>
    private sealed class NodeDragSession
    {
        public NodeDragSession(
            string nodeId,
            Border card,
            Rect origin,
            double? originNodeX,
            double? originNodeY)
        {
            NodeId = nodeId;
            Card = card;
            Origin = origin;
            OriginNodeX = originNodeX;
            OriginNodeY = originNodeY;
            Current = origin;
        }

        /// <summary>拖动的节点 Id。</summary>
        public string NodeId { get; }

        /// <summary>发起拖动的卡片；整页重绘后这个实例会失效。</summary>
        public Border Card { get; }

        /// <summary>拖动开始时卡片的视觉矩形。</summary>
        public Rect Origin { get; }

        /// <summary>
        /// 最近一帧卡片的视觉矩形（起点 + 累计位移 ÷ 缩放）。
        ///
        /// 它同时也是卡片当前的 LayoutBounds（拖动期间只改这一处），
        /// 松手落盘用这里的值换算节点坐标。
        /// </summary>
        public Rect Current { get; set; }

        /// <summary>拖动前的自由坐标；null 表示原来按行列自动排布，还原时要写回 null。</summary>
        public double? OriginNodeX { get; }

        /// <summary>同上，纵向。</summary>
        public double? OriginNodeY { get; }

        /// <summary>是否已经取到累计位移的基准。</summary>
        public bool HasTotalBase { get; private set; }

        /// <summary>累计位移的基准值，拖动位移 = 当前 Total − 基准。</summary>
        public double BaseTotalX { get; private set; }

        /// <summary>同上，纵向。</summary>
        public double BaseTotalY { get; private set; }

        /// <summary>上一帧的累计位移，用来识别平台给出的跳变值。</summary>
        public double LastTotalX { get; set; }

        /// <summary>同上，纵向。</summary>
        public double LastTotalY { get; set; }

        /// <summary>
        /// 手势重启前已经走过的位移（已除以缩放比例）。
        ///
        /// 重启后平台会从 0 重新发 Total，位移要接着这里累加，
        /// 否则卡片会按「累计位移 ÷ 缩放」往回跳一段。
        /// </summary>
        public double RebaseX { get; private set; }

        /// <summary>同上，纵向。</summary>
        public double RebaseY { get; private set; }

        /// <summary>本次是否真的移动过；没移动过就不落盘。</summary>
        public bool Moved { get; set; }

        /// <summary>
        /// 把当前已经走过的位移冻结进 <see cref="RebaseX"/> / <see cref="RebaseY"/>，
        /// 之后重新取累计位移基准时卡片停在同一位置。
        /// </summary>
        public void FreezeDisplacement(
            double dragScale)
        {
            // 还没取过基准：没有位移可冻结
            if (!HasTotalBase)
            {
                return;
            }

            RebaseX += (LastTotalX - BaseTotalX) * dragScale;
            RebaseY += (LastTotalY - BaseTotalY) * dragScale;
        }

        /// <summary>用这一帧的累计位移重新取基准（不产生位移）。</summary>
        public void SetTotalBase(
            double totalX,
            double totalY)
        {
            HasTotalBase = true;

            BaseTotalX = totalX;
            BaseTotalY = totalY;

            LastTotalX = totalX;
            LastTotalY = totalY;
        }

        /// <summary>平台给出跳变值时，把基准平移同样的量，让节点原地不动。</summary>
        public void ShiftTotalBase(
            double totalX,
            double totalY)
        {
            BaseTotalX += totalX - LastTotalX;
            BaseTotalY += totalY - LastTotalY;

            LastTotalX = totalX;
            LastTotalY = totalY;
        }

        /// <summary>手势重启：清掉基准，下一次 Running 重新取。</summary>
        public void ResetTotalBase()
        {
            HasTotalBase = false;

            BaseTotalX = 0;
            BaseTotalY = 0;

            LastTotalX = 0;
            LastTotalY = 0;
        }
    }

    /// <summary>
    /// 按当前节点矩形重算所有连线的两个端点，并只让线段层和箭头层重绘。
    /// </summary>
    private void UpdateLinkLayers()
    {
        if (_store is null ||
            _lineDrawable is null ||
            _arrowDrawable is null)
        {
            return;
        }

        _lineDrawable.Links.Clear();
        _arrowDrawable.Links.Clear();

        foreach (WorkspaceEdge edge in _store.Edges)
        {
            if (!_nodeBounds.TryGetValue(edge.FromId, out Rect from) ||
                !_nodeBounds.TryGetValue(edge.ToId, out Rect to))
            {
                continue;
            }

            WorkspaceLink link = WorkspaceLinkGeometry.Create(
                from,
                to);

            _lineDrawable.Links.Add(link);
            _arrowDrawable.Links.Add(link);
        }

        _lineLayer?.Invalidate();
        _arrowLayer?.Invalidate();
    }

    private ShapePreviewDrawable PreviewOf(
        string code,
        ShortCodeFormat format,
        int layers,
        int parts)
    {
        var preview = new ShapePreviewDrawable();

        ParseResult result = new ShortCodeParser().Parse(code, layers, parts, format);

        preview.SetDocument(result.Success ? result.Document : null);

        return preview;
    }

    #endregion

    #region 素材导入

    /// <summary>
    /// 把素材拖到画布：新建节点并写入图形。
    /// 象限数不符时只提示，不创建节点。
    /// </summary>
    private void ImportMaterial(
        WorkspaceItem item,
        double x,
        double y)
    {
        try
        {
            ImportMaterialCore(item, x, y);
        }
        catch (Exception exception)
        {
            Show("导入失败：" + exception.Message);
        }
    }

    private void ImportMaterialCore(
        WorkspaceItem item,
        double x,
        double y)
    {
        if (_store is null)
        {
            return;
        }

        if (!_store.PartsAllowed(item.Parts))
        {
            ShowQuadrantMismatch(item.Parts);

            return;
        }

        WorkspaceNode node = _store.CreateNodeAtPoint(x, y);

        _store.ReplaceNodeShape(
            node.Id,
            item.Code,
            item.Format,
            item.Layers,
            item.Parts);

        // 万一 Core 仍然拒绝，删掉刚建的空节点，保持画布干净
        WorkspaceNode? imported = _store.FindNode(node.Id);

        if (imported is null || !imported.HasShape)
        {
            string message = _store.LastError ?? "无法导入该素材。";

            if (imported is not null)
            {
                _store.DeleteNode(imported.Id);
            }

            Show(message);

            return;
        }

        _store.SelectNode(imported);
    }

    /// <summary>提示象限数被工作区锁定。</summary>
    private void ShowQuadrantMismatch(
        int parts)
    {
        Show($"该工作区的象限数已固定为 {_store!.WorkspaceParts}，不能再加入 {parts} 象限的图形。");
    }

    /// <summary>
    /// 素材拖到画布视口：把视口落点换算回画布内容坐标，再创建节点并导入。
    /// </summary>
    private async void OnCanvasDrop(
        object? sender,
        DropEventArgs e)
    {
        if (_store is null)
        {
            return;
        }

        string? materialId = null;

        if (e.Data.Properties.TryGetValue(MaterialDragKey, out object? value))
        {
            materialId = value as string;
        }

        // 部分平台只带回文本，这里用 Text 兜底
        if (string.IsNullOrWhiteSpace(materialId))
        {
            try
            {
                materialId = await e.Data.GetTextAsync();
            }
            catch
            {
                materialId = null;
            }
        }

        if (string.IsNullOrWhiteSpace(materialId))
        {
            return;
        }

        WorkspaceItem? item = _store.Materials
            .FirstOrDefault(material => material.Id == materialId);

        if (item is null)
        {
            return;
        }

        /*
         * 落点由画布视口接收（铺满，与虚线边框一致），坐标是视口坐标。
         * ViewportToCanvas 会先加上 ScrollView 的 ScrollX/ScrollY，再除以缩放比例，
         * 得到逻辑画布坐标；最后扣掉渲染时为负坐标做的整体平移。
         * 这样边框圈出来的任意可见位置都能放素材，
         * 不会出现「看着在边框里、拖过去却没反应」。
         */
        Point? position = e.GetPosition(CanvasViewport);

        Point content = position is Point point
            ? ViewportToCanvas(point)
            : new Point(80, 80);

        // 画布可能因为负坐标整体平移过，落点要扣回平移量
        double x = Math.Max(0, content.X - _canvasShiftX);

        double y = Math.Max(0, content.Y - _canvasShiftY);

        ImportMaterial(item, x, y);

        e.Handled = true;
    }

    #endregion

    #region 节点操作

    private void OnCollapseNode(
        object? sender,
        EventArgs e)
    {
        ApplySingleOperation("collapse");
    }

    private void OnPinNode(
        object? sender,
        EventArgs e)
    {
        ApplySingleOperation("pin");
    }

    private void OnCutNode(
        object? sender,
        EventArgs e)
    {
        if (_store!.SelectedNode is not WorkspaceNode node)
        {
            return;
        }

        WorkspaceNodeBox? source = FindBox(node.Id);

        if (source is null)
        {
            return;
        }

        HashSet<string> before = _store.Nodes
            .Select(entry => entry.Id)
            .ToHashSet();

        NodeActionResult result = _store.ApplyCut(node.Id);

        if (!result.Created)
        {
            Show(result.Message);

            if (result.Node is not null)
            {
                _store.SelectNode(result.Node);
            }

            return;
        }

        WorkspaceNode? west = null;

        foreach (WorkspaceNode created in _store.Nodes)
        {
            if (before.Contains(created.Id))
            {
                continue;
            }

            bool isWest = created.OpKey == $"cutw:{node.Id}";

            created.X = source.Bounds.X + (isWest ? -CutOffset : CutOffset);
            created.Y = source.Bounds.Y + source.Bounds.Height + ResultGap;

            if (isWest)
            {
                west = created;
            }
        }

        _store.SelectNode(west ?? result.Node);
    }

    private void ApplySingleOperation(
        string operation)
    {
        if (_store!.SelectedNode is not WorkspaceNode node)
        {
            return;
        }

        WorkspaceNodeBox? source = FindBox(node.Id);

        if (source is null)
        {
            return;
        }

        NodeActionResult result = operation == "pin"
            ? _store.ApplyPin(node.Id)
            : _store.ApplyCollapse(node.Id);

        if (!result.Created || result.Node is null)
        {
            Show(result.Message);

            if (result.Node is not null)
            {
                _store.SelectNode(result.Node);
            }

            return;
        }

        // 结果放在源节点正下方
        result.Node.X = source.Bounds.X;
        result.Node.Y = source.Bounds.Y + source.Bounds.Height + ResultGap;

        _store.SelectNode(result.Node);
    }

    /// <summary>
    /// 堆叠分两步：选中第一个节点后点「堆叠」，再点第二个节点出结果。
    /// 再点一次「堆叠」或点画布空白处取消。
    /// </summary>
    private void OnStackNode(
        object? sender,
        EventArgs e)
    {
        if (_stackFirstId is not null)
        {
            CancelStack();

            return;
        }

        if (_store!.SelectedNode is not WorkspaceNode node ||
            _store.FindNode(node.Id) is null)
        {
            return;
        }

        // 「堆叠」和「交换」共用第二次点击，进入堆叠前先退出交换状态
        CancelExchange();

        _stackFirstId = node.Id;

        Render();
    }

    /// <summary>
    /// 堆叠第二步：第二个节点点下后直接按两个节点计算，
    /// 结果放在它右侧约 80px（Core 的 SetNodePosition 会做 10px 吸附）。
    /// </summary>
    private void CompleteStack(
        WorkspaceNode second)
    {
        if (_store is null || _stackFirstId is null)
        {
            return;
        }

        if (second.Id == _stackFirstId)
        {
            Show("请点另一个节点作为第二个输入。");

            return;
        }

        string firstId = _stackFirstId;

        _stackFirstId = null;

        if (_store.FindNode(firstId) is null)
        {
            Show("第一个输入节点已不存在，请重新选择。");

            Render();

            return;
        }

        // 落点按第二个节点当前的视觉矩形计算
        WorkspaceNodeBox? secondBox = FindBox(second.Id);

        NodeActionResult result = _store.ComputeStackOf(firstId, second.Id, true);

        if (!result.Created || result.Node is null)
        {
            Show(result.Message);

            if (result.Node is not null)
            {
                _store.SelectNode(result.Node);
            }
            else
            {
                Render();
            }

            return;
        }

        double x = secondBox is null
            ? WorkspaceLayout.Margin
            : secondBox.Bounds.Right + 80;

        double y = secondBox?.Bounds.Y ?? WorkspaceLayout.Margin;

        _store.SetNodePosition(result.Node.Id, x, y);

        _store.SelectNode(result.Node);
    }

    /// <summary>取消堆叠选择，恢复状态栏。</summary>
    private void CancelStack()
    {
        if (_stackFirstId is null)
        {
            return;
        }

        _stackFirstId = null;

        Render();
    }

    /// <summary>点画布空白处取消堆叠 / 交换；点在节点卡片上时交给卡片的点击处理。</summary>
    private void OnCanvasTapped(
        object? sender,
        TappedEventArgs e)
    {
        // 缩放过程中不处理点击，避免误取消当前选择
        if (IsPinching)
        {
            return;
        }

        if (_stackFirstId is null && _exchangeFirstId is null)
        {
            return;
        }

        Point? position = e.GetPosition(CanvasViewport);

        if (position is Point point && IsOverNode(ViewportToCanvas(point)))
        {
            return;
        }

        CancelStack();

        CancelExchange();
    }

    /// <summary>判断画布坐标是否落在任一节点卡片里。</summary>
    private bool IsOverNode(
        Point position)
    {
        if (_store is null)
        {
            return false;
        }

        double x = position.X - _canvasShiftX;

        double y = position.Y - _canvasShiftY;

        return WorkspaceLayout
            .Compute(_store.Nodes, _store.GetEffectiveDocument)
            .Any(box => box.Bounds.Contains(x, y));
    }

    private void OnCopyNode(
        object? sender,
        EventArgs e)
    {
        if (_store!.SelectedNode is not WorkspaceNode node)
        {
            return;
        }

        if (!node.HasShape)
        {
            Show("该节点还没有可复制的图形。");

            return;
        }

        _clipboard = new NodeClipboard(
            node.Code,
            node.Format,
            node.Layers,
            node.Parts);

        StatusLabel.Text = $"已复制节点 #{node.Seq} 的短代码。";

        UpdateActionButtons();
    }

    private void OnPasteNode(
        object? sender,
        EventArgs e)
    {
        if (_store!.SelectedNode is null || _clipboard is null)
        {
            return;
        }

        NodeClipboard clipboard = _clipboard;

        WorkspaceNode node = _store.CreateNode();

        // 粘贴固定放在 (80, 80)
        node.X = 80;
        node.Y = 80;

        _store.ReplaceNodeShape(
            node.Id,
            clipboard.Code,
            clipboard.Format,
            clipboard.Layers,
            clipboard.Parts);

        _store.SelectNode(node);
    }

    private void OnDeleteNode(
        object? sender,
        EventArgs e)
    {
        if (_store!.SelectedNode is not WorkspaceNode node)
        {
            return;
        }

        _store.DeleteNode(node.Id);
    }

    private WorkspaceNodeBox? FindBox(
        string nodeId)
    {
        return WorkspaceLayout
            .Compute(_store!.Nodes, _store.GetEffectiveDocument)
            .FirstOrDefault(box => box.Node.Id == nodeId);
    }

    #endregion

    #region 工作区级操作

    private void OnCreateNode(
        object? sender,
        EventArgs e)
    {
        _store!.CreateNode();
    }

    /// <summary>
    /// 按依赖关系重新排列所有节点；Store 会落盘并触发一次重绘，
    /// 这里再显式 Render 一次，保证坐标与卡片位置立即同步。
    /// </summary>
    private void OnAutoLayout(
        object? sender,
        EventArgs e)
    {
        if (_store is null)
        {
            return;
        }

        _store.AutoLayout();

        Render();
    }

    private void OnToggleMode(
        object? sender,
        EventArgs e)
    {
        if (_store is null)
        {
            return;
        }

        CollapseMode mode = _store.Mode == CollapseMode.IncludeCycles
            ? CollapseMode.FloorOnly
            : CollapseMode.IncludeCycles;

        (int Updated, int Failed) result = (0, 0);

        // 重算过程中会连续触发 Changed，先压住整页重绘，结束后统一 Render
        bool wasRendering = _rendering;

        _rendering = true;

        try
        {
            _store.SetMode(mode);

            // Core 的 RecomputeAll 已原生支持交换结果，无需再手工补算
            result = _store.RecomputeAll();
        }
        finally
        {
            _rendering = wasRendering;
        }

        Render();

        if (result.Failed > 0)
        {
            Show($"已重算 {result.Updated} 个节点，{result.Failed} 个因输入缺失被跳过。");
        }
    }

    private void OnMaxLayersChanged(
        object? sender,
        EventArgs e)
    {
        if (!int.TryParse(MaxLayersEntry.Text, out int layers) || _store is null)
        {
            return;
        }

        (int Updated, int Failed) result = (0, 0);

        // 高度上限变了同样要按新规则重算交换节点
        bool wasRendering = _rendering;

        _rendering = true;

        try
        {
            _store.SetMaxLayers(layers);

            // Core 的 RecomputeAll 已原生支持交换结果，无需再手工补算
            result = _store.RecomputeAll();
        }
        finally
        {
            _rendering = wasRendering;
        }

        Render();

        if (result.Failed > 0)
        {
            Show($"已重算 {result.Updated} 个节点，{result.Failed} 个因输入缺失被跳过。");
        }
    }

    private void OnClear(
        object? sender,
        EventArgs e)
    {
        _store!.ClearNodes();
    }

    private async void OnExportImage(
        object? sender,
        EventArgs e)
    {
        if (_store is null || _store.Nodes.Count == 0)
        {
            Show("画布是空的。");

            return;
        }

        try
        {
            /*
             * 导出走离屏渲染，只读节点的逻辑坐标：
             * 固定按 1 倍（30px 单元格）输出，与画布当前的缩放比例、平移量无关，
             * 导出图片尺寸与缩放前一致（这里也不把 _canvasScale 传下去）。
             */
            List<WorkspaceNodeBox> boxes = WorkspaceLayout.Compute(
                _store.Nodes,
                _store.GetEffectiveDocument);

            var exporter = new WorkspaceImageExporter();

            string path = await exporter.ExportAsync(
                boxes,
                _store.Edges,
                _store.GetEffectiveDocument);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "导出路径图",
                File = new ShareFile(path)
            });
        }
        catch (Exception exception)
        {
            Show("导出失败：" + exception.Message);
        }
    }

    private async Task ExportProjectAsync()
    {
        string json = _store!.ExportProject();

        string path = Path.Combine(
            FileSystem.CacheDirectory,
            $"workspace-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        await File.WriteAllTextAsync(path, json);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "导出工作区",
            File = new ShareFile(path)
        });
    }

    #endregion

    private async void Show(
        string message)
    {
        await DisplayAlert("工作区", message, "好");
    }

    /// <summary>
    /// 复制到剪贴板的节点信息：短代码 + 解析尺寸。
    /// </summary>
    private sealed record NodeClipboard(
        string Code,
        ShortCodeFormat Format,
        int Layers,
        int Parts);
}

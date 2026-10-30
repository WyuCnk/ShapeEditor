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

    public WorkspacePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _store ??= AppServices.Workspace;

        _store.Changed -= Render;
        _store.Changed += Render;

        Render();
    }

    protected override void OnDisappearing()
    {
        if (_store is not null)
        {
            _store.Changed -= Render;
        }

        base.OnDisappearing();
    }

    #region 渲染

    private void Render()
    {
        if (_store is null)
        {
            return;
        }

        RenderTabs();
        RenderMaterials();
        RenderCanvas();

        CycleModeButton.Text = _store.Mode == CollapseMode.IncludeCycles
            ? "含循环支撑"
            : "仅地面";

        MaxLayersEntry.Text = _store.MaxLayers.ToString();

        // 等第二个输入期间如果底节点被删掉，自动退出堆叠状态
        if (_stackFirstId is not null && _store.FindNode(_stackFirstId) is null)
        {
            _stackFirstId = null;
        }

        WorkspaceNode? selected = _store.SelectedNode;

        StatusLabel.Text = _stackFirstId is not null
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
        CopyButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;

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
                        new Label
                        {
                            Text = item.Name,
                            FontSize = 10,
                            LineBreakMode = LineBreakMode.TailTruncation
                        }
                    }
                }
            };

            WorkspaceItem captured = item;

            // 单击只切换素材选中；不重建卡片，避免双击/拖拽的手势目标被顶掉
            var tap = new TapGestureRecognizer();

            tap.Tapped += (_, _) =>
            {
                _selectedMaterial = captured;

                UpdateMaterialSelection();
            };

            border.GestureRecognizers.Add(tap);

            // 双击：在 (80, 80) 新建节点并导入该素材
            var doubleTap = new TapGestureRecognizer
            {
                NumberOfTapsRequired = 2
            };

            doubleTap.Tapped += (_, _) => ImportMaterial(captured, 80, 80);

            border.GestureRecognizers.Add(doubleTap);

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

        if (_store!.Nodes.Count == 0)
        {
            Canvas.WidthRequest = 320;
            Canvas.HeightRequest = 320;

            _canvasShiftX = 0;
            _canvasShiftY = 0;

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

        Canvas.WidthRequest = width;
        Canvas.HeightRequest = height;

        var lookup = placed.ToDictionary(box => box.Node.Id);

        var lineDrawable = new WorkspaceLineDrawable();

        var arrowDrawable = new WorkspaceArrowDrawable();

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

        Canvas.Add(lineLayer);

        foreach (WorkspaceNodeBox box in placed)
        {
            View card = BuildNodeCard(
                box.Node,
                _store.GetEffectiveDocument(box.Node));

            AbsoluteLayout.SetLayoutBounds(card, box.Bounds);

            Canvas.Add(card);
        }

        // 箭头层：在节点之上
        if (arrowDrawable.Links.Count > 0)
        {
            var arrowLayer = new GraphicsView
            {
                InputTransparent = true,
                Drawable = arrowDrawable
            };

            AbsoluteLayout.SetLayoutBounds(
                arrowLayer,
                new Rect(0, 0, width, height));

            Canvas.Add(arrowLayer);
        }
    }

    /// <summary>
    /// 节点卡片：编号 + 标题、× 删除、竖式图预览、层数 × 象限数摘要。
    /// 卡片内不放操作按钮，也不放滚动条。
    /// </summary>
    private View BuildNodeCard(
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
            // 删除按钮和卡片点击可能落在同一帧里，先确认节点还在
            if (_store!.FindNode(node.Id) is null)
            {
                return;
            }

            // 堆叠第二步：这个节点作为第二个输入
            if (_stackFirstId is not null)
            {
                CompleteStack(node);

                return;
            }

            _store.SelectNode(node);
        };

        border.GestureRecognizers.Add(tap);

        return border;
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
    /// 双击素材或把素材拖到画布：新建节点并写入图形。
    /// 象限数不符时只提示，不创建节点。
    /// </summary>
    private void ImportMaterial(
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
    /// 素材拖到画布：按落点（相对画布）创建节点并导入。
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

        Point? position = e.GetPosition(Canvas);

        // 画布可能因为负坐标整体平移过，落点要扣回平移量
        double x = Math.Max(0, (position?.X ?? 80) - _canvasShiftX);

        double y = Math.Max(0, (position?.Y ?? 80) - _canvasShiftY);

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

    /// <summary>点画布空白处取消堆叠；点在节点卡片上时交给卡片的点击处理。</summary>
    private void OnCanvasTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (_stackFirstId is null)
        {
            return;
        }

        Point? position = e.GetPosition(Canvas);

        if (position is not null && IsOverNode(position.Value))
        {
            return;
        }

        CancelStack();
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

    private void OnToggleMode(
        object? sender,
        EventArgs e)
    {
        CollapseMode mode = _store!.Mode == CollapseMode.IncludeCycles
            ? CollapseMode.FloorOnly
            : CollapseMode.IncludeCycles;

        _store.SetMode(mode);

        (int updated, int failed) = _store.RecomputeAll();

        if (failed > 0)
        {
            Show($"已重算 {updated} 个节点，{failed} 个因输入缺失被跳过。");
        }
    }

    private void OnMaxLayersChanged(
        object? sender,
        EventArgs e)
    {
        if (!int.TryParse(MaxLayersEntry.Text, out int layers))
        {
            return;
        }

        _store!.SetMaxLayers(layers);

        (int updated, int failed) = _store.RecomputeAll();

        if (failed > 0)
        {
            Show($"已重算 {updated} 个节点，{failed} 个因输入缺失被跳过。");
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

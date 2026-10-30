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

    private WorkspaceStore? _store;

    private WorkspaceItem? _selectedMaterial;

    private NodeClipboard? _clipboard;

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

        WorkspaceNode? selected = _store.SelectedNode;

        StatusLabel.Text = $"素材 {_store.Materials.Count} 条 · 节点 {_store.Nodes.Count} 个" +
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
        StackButton.IsEnabled = hasSelection;
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

        foreach (WorkspaceItem item in _store!.Materials)
        {
            var preview = new GraphicsView
            {
                HeightRequest = 72,
                WidthRequest = 96,
                InputTransparent = true,
                Drawable = PreviewOf(item.Code, item.Format, item.Layers, item.Parts)
            };

            var border = new Border
            {
                Stroke = item.Id == _selectedMaterial?.Id ? Accent : Color.FromArgb("#E6E2F2"),
                StrokeThickness = item.Id == _selectedMaterial?.Id ? 2 : 1,
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

            var tap = new TapGestureRecognizer();

            tap.Tapped += (_, _) =>
            {
                _selectedMaterial = captured;

                Render();
            };

            border.GestureRecognizers.Add(tap);

            MaterialList.Add(border);
        }
    }

    private void RenderCanvas()
    {
        Canvas.Clear();

        if (_store!.Nodes.Count == 0)
        {
            Canvas.WidthRequest = 320;
            Canvas.HeightRequest = 320;

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
    /// 堆叠：
    /// 选中素材/结果节点时创建一个第二输入槽位；
    /// 选中堆叠输入槽位时计算堆叠结果。
    /// </summary>
    private void OnStackNode(
        object? sender,
        EventArgs e)
    {
        if (_store!.SelectedNode is not WorkspaceNode node)
        {
            return;
        }

        WorkspaceNodeBox? source = FindBox(node.Id);

        double left = source?.Bounds.X ?? WorkspaceLayout.Margin;

        double top = source is null
            ? WorkspaceLayout.Margin
            : source.Bounds.Y + source.Bounds.Height + ResultGap;

        if (node.Kind == "ref")
        {
            NodeActionResult result = _store.ComputeStack(node.Id);

            if (!result.Created || result.Node is null)
            {
                Show(result.Message);

                if (result.Node is not null)
                {
                    _store.SelectNode(result.Node);
                }

                return;
            }

            result.Node.X = left;
            result.Node.Y = top;

            _store.SelectNode(result.Node);

            return;
        }

        WorkspaceNode slot = _store.BeginStack(node.Id, false);

        slot.X = left;
        slot.Y = top;

        _store.SelectNode(slot);
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

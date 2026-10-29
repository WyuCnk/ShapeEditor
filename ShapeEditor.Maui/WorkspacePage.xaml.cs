using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区页面：多工作区标签、素材列表、制作路径图。
///
/// 第一步先做标签、素材、节点卡片与基础运算，连线在下一步补。
/// </summary>
public partial class WorkspacePage : ContentPage
{
    private const double NodeWidth = 168;

    private const double NodeHeight = 150;

    private const double CellWidth = 190;

    private const double CellHeight = 300;

    private static readonly Color Accent = Color.FromArgb("#512BD4");

    private static readonly Color Muted = Color.FromArgb("#6B6B76");

    private WorkspaceStore? _store;

    private WorkspaceNode? _selectedNode;

    private WorkspaceItem? _selectedMaterial;

    public WorkspacePage()
    {
        InitializeComponent();
    }



    protected override void OnAppearing()
    {
        base.OnAppearing();

        _store ??= AppServices.Workspace;

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

        CycleModeButton.Text = _store.Mode == Core.Physics.CollapseMode.IncludeCycles
            ? "含循环支撑"
            : "仅地面";

        MaxLayersEntry.Text = _store.MaxLayers.ToString();

        int nodes = _store.Nodes.Count;

        StatusLabel.Text = $"素材 {_store.Materials.Count} 条 · 节点 {nodes} 个" +
                           (_selectedNode is null ? "" : $" · 已选中 #{_selectedNode.Seq}");
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
            return;
        }

        double width = _store.Nodes.Max(node => node.Col) * CellWidth + CellWidth;
        double height = _store.Nodes.Max(node => node.Row) * CellHeight + CellHeight;

        Canvas.WidthRequest = Math.Max(320, width);
        Canvas.HeightRequest = Math.Max(320, height);

        foreach (WorkspaceNode node in _store.Nodes)
        {
            View card = BuildNodeCard(node);

            AbsoluteLayout.SetLayoutBounds(
                card,
                new Rect(12 + node.Col * CellWidth, 12 + node.Row * CellHeight, NodeWidth, AbsoluteLayout.AutoSize));

            Canvas.Add(card);
        }
    }

    private View BuildNodeCard(
        WorkspaceNode node)
    {
        bool selected = node.Id == _selectedNode?.Id;

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        header.Add(new Label
        {
            Text = $"#{node.Seq} {TitleOf(node)}",
            FontSize = 10,
            TextColor = Muted,
            VerticalTextAlignment = TextAlignment.Center
        }, 0);

        var delete = new Button
        {
            Text = "×",
            FontSize = 12,
            WidthRequest = 24,
            HeightRequest = 24,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#A00000")
        };

        delete.Clicked += (_, _) => DeleteNode(node);

        header.Add(delete, 1);

        var stack = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                header,
                new GraphicsView
                {
                    HeightRequest = 80,
                    InputTransparent = true,
                    Drawable = NodePreview(node)
                }
            }
        };

        if (node.Kind == "ref")
        {
            if (node.TargetId is not null &&
                _store!.FindNode(node.TargetId) is WorkspaceNode target)
            {
                stack.Add(new Label
                {
                    Text = $"引用 #{target.Seq}",
                    FontSize = 10,
                    TextColor = Color.FromArgb("#8B7BBF")
                });
            }

            stack.Add(ActionRow(
                ("导入", () => ImportInto(node)),
                ("引用", () => ReferenceInto(node)),
                ("计算", () => ComputeStack(node))));
        }
        else if (node.IsResult)
        {
            stack.Add(new Label
            {
                Text = node.Label,
                FontSize = 10,
                TextColor = Muted
            });
        }
        else
        {
            stack.Add(ActionRow(("导入", () => ImportInto(node))));
        }

        stack.Add(ActionRow(
            ("下落", () => Apply(node, "collapse")),
            ("顶针", () => Apply(node, "pin")),
            ("切割", () => Apply(node, "cut"))));

        stack.Add(ActionRow(
            ("下堆", () => BeginStack(node, false)),
            ("上堆", () => BeginStack(node, true))));

        var border = new Border
        {
            Stroke = selected ? Accent : Color.FromArgb("#D0D0D0"),
            StrokeThickness = selected ? 2 : 1,
            BackgroundColor = node.IsResult ? Color.FromArgb("#FAF8FF") : Colors.White,
            Padding = 6,
            Content = stack
        };

        var tap = new TapGestureRecognizer();

        tap.Tapped += (_, _) =>
        {
            _selectedNode = node;

            _store!.SelectNode(node);

            Render();
        };

        border.GestureRecognizers.Add(tap);

        return border;
    }

    private static HorizontalStackLayout ActionRow(
        params (string Text, Action Action)[] actions)
    {
        var row = new HorizontalStackLayout { Spacing = 3 };

        foreach ((string text, Action action) in actions)
        {
            var button = new Button
            {
                Text = text,
                FontSize = 10,
                HeightRequest = 26,
                WidthRequest = 46,
                Padding = 0,
                MinimumWidthRequest = 0,
                CornerRadius = 6,
                BackgroundColor = Color.FromArgb("#F2F0F8"),
                TextColor = Accent
            };

            button.Clicked += (_, _) => action();

            row.Add(button);
        }

        return row;
    }

    private static string TitleOf(
        WorkspaceNode node)
    {
        if (node.Kind == "ref")
        {
            return "堆叠输入";
        }

        return node.IsResult ? node.Label : "素材节点";
    }

    private ShapePreviewDrawable NodePreview(
        WorkspaceNode node)
    {
        var preview = new ShapePreviewDrawable();

        preview.SetDocument(_store!.GetEffectiveDocument(node));

        return preview;
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

    #region 操作

    private void Apply(
        WorkspaceNode node,
        string operation)
    {
        NodeActionResult result = operation switch
        {
            "pin" => _store!.ApplyPin(node.Id),
            "cut" => _store!.ApplyCut(node.Id),
            _ => _store!.ApplyCollapse(node.Id)
        };

        if (!result.Created)
        {
            Show(result.Message);
        }
    }

    private void BeginStack(
        WorkspaceNode node,
        bool currentIsTop)
    {
        _store!.BeginStack(node.Id, currentIsTop);
    }

    private void ComputeStack(
        WorkspaceNode node)
    {
        Show(_store!.ComputeStack(node.Id).Message);
    }

    private void ImportInto(
        WorkspaceNode node)
    {
        if (_selectedMaterial is null)
        {
            Show("请先在左侧选择一个素材。");

            return;
        }

        if (node.IsResult)
        {
            Show("结果节点不能导入素材。");

            return;
        }

        WorkspaceItem material = _selectedMaterial;

        if (_store!.Downstream(node.Id).Count > 0)
        {
            Show("该节点已有下游结果，请先删除下游再更换图形。");

            return;
        }

        if (node.Kind == "ref")
        {
            _store.SetSlotShape(node.Id, material.Code, material.Format, material.Layers, material.Parts);
        }
        else
        {
            _store.ReplaceNodeShape(node.Id, material.Code, material.Format, material.Layers, material.Parts);
        }
    }

    private void ReferenceInto(
        WorkspaceNode slot)
    {
        WorkspaceNode? target = _selectedNode;

        if (target is null ||
            target.Kind == "ref" ||
            target.Id == slot.Id ||
            target.Id == slot.BaseId ||
            !target.HasShape)
        {
            Show("请先点选一个其它有图形的节点。");

            return;
        }

        _store!.SetReferenceTarget(slot.Id, target.Id);
    }

    private void DeleteNode(
        WorkspaceNode node)
    {
        _store!.DeleteNode(node.Id);
    }

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
        Core.Physics.CollapseMode mode = _store!.Mode == Core.Physics.CollapseMode.IncludeCycles
            ? Core.Physics.CollapseMode.FloorOnly
            : Core.Physics.CollapseMode.IncludeCycles;

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

    private async void Show(
        string message)
    {
        await DisplayAlert("工作区", message, "好");
    }

    #endregion
}

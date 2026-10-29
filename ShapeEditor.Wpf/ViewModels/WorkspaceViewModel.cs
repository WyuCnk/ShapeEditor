using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;
using ShapeEditor.Wpf.Rendering;
using ShapeEditor.Wpf.Services;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 工作区页面：多工作区、素材库、制作路径图。
/// </summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly WorkspaceStore _store;

    private readonly ShortCodeParser _parser = new();

    private readonly ShortCodeExporter _exporter = new();

    private WorkspaceTabViewModel? _selectedTab;

    private MaterialViewModel? _selectedMaterial;

    private WorkspaceNodeViewModel? _selectedNode;

    private double _canvasWidth = 800;

    private double _canvasHeight = 600;

    private bool _storageErrorShown;

    public WorkspaceViewModel(
        WorkspaceStore? store = null)
    {
        _store = store ?? new WorkspaceStore();

        _store.Changed += Refresh;

        CollapseCommand = new RelayCommand(p => ApplyNode(p, "collapse"));
        PinCommand = new RelayCommand(p => ApplyNode(p, "pin"));
        CutCommand = new RelayCommand(p => ApplyNode(p, "cut"));
        StackBelowCommand = new RelayCommand(p => BeginStack(p, false));
        StackAboveCommand = new RelayCommand(p => BeginStack(p, true));
        ComputeStackCommand = new RelayCommand(p => ComputeStack(p));
        DeleteNodeCommand = new RelayCommand(p => DeleteNode(p));
        SelectNodeCommand = new RelayCommand(p => SelectNode(p));
        ImportMaterialCommand = new RelayCommand(p => ImportMaterial(p));
        ReferenceSelectedCommand = new RelayCommand(p => ReferenceSelected(p));
        CycleLabelCommand = new RelayCommand(p => CycleLabel(p));

        NewNodeCommand = new RelayCommand(() => _store.CreateNode());
        NewProjectCommand = new RelayCommand(NewProject);
        ExportImageCommand = new RelayCommand(ExportImage);
        ExportProjectCommand = new RelayCommand(ExportProject);
        ImportProjectCommand = new RelayCommand(ImportProject);
        ClearNodesCommand = new RelayCommand(ClearNodes);

        Refresh();
    }

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = new();

    public ObservableCollection<MaterialViewModel> Materials { get; } = new();

    public ObservableCollection<WorkspaceNodeViewModel> Nodes { get; } = new();

    public ObservableCollection<WorkspaceEdgeViewModel> Edges { get; } = new();

    public System.Windows.Input.ICommand CollapseCommand { get; }

    public System.Windows.Input.ICommand PinCommand { get; }

    public System.Windows.Input.ICommand CutCommand { get; }

    public System.Windows.Input.ICommand StackBelowCommand { get; }

    public System.Windows.Input.ICommand StackAboveCommand { get; }

    public System.Windows.Input.ICommand ComputeStackCommand { get; }

    public System.Windows.Input.ICommand DeleteNodeCommand { get; }

    public System.Windows.Input.ICommand SelectNodeCommand { get; }

    public System.Windows.Input.ICommand ImportMaterialCommand { get; }

    public System.Windows.Input.ICommand ReferenceSelectedCommand { get; }

    public System.Windows.Input.ICommand CycleLabelCommand { get; }

    public System.Windows.Input.ICommand NewNodeCommand { get; }

    public System.Windows.Input.ICommand ExportImageCommand { get; }

    public System.Windows.Input.ICommand NewProjectCommand { get; }

    public System.Windows.Input.ICommand ExportProjectCommand { get; }

    public System.Windows.Input.ICommand ImportProjectCommand { get; }

    public System.Windows.Input.ICommand ClearNodesCommand { get; }

    public WorkspaceTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value)
            {
                return;
            }

            _selectedTab = value;

            OnPropertyChanged();

            if (value is not null)
            {
                _store.ActivateProject(value.Id);
            }
        }
    }

    public MaterialViewModel? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value))
            {
                OnPropertyChanged(nameof(HasSelectedMaterial));
            }
        }
    }

    public bool HasSelectedMaterial => _selectedMaterial is not null;

    public WorkspaceNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                OnPropertyChanged(nameof(HasSelectedNode));
            }
        }
    }

    public bool HasSelectedNode => _selectedNode is not null;

    public double CanvasWidth
    {
        get => _canvasWidth;
        private set => SetProperty(ref _canvasWidth, value);
    }

    public double CanvasHeight
    {
        get => _canvasHeight;
        private set => SetProperty(ref _canvasHeight, value);
    }

    public bool IsCycleMode => _store.Mode == CollapseMode.IncludeCycles;

    public bool IsFloorMode => _store.Mode == CollapseMode.FloorOnly;

    /// <summary>
    /// 高度上限，绑定到输入框。
    /// </summary>
    public int MaxLayers
    {
        get => _store.MaxLayers;
        set
        {
            if (value == _store.MaxLayers)
            {
                return;
            }

            _store.SetMaxLayers(value);

            (int updated, int failed) = _store.RecomputeAll();

            ReportRecompute(updated, failed);
        }
    }

    public string StatusText { get; private set; } = "";

    /// <summary>数据实际保存的位置，便于排查。</summary>
    public string StoragePath => StoragePaths.Root;

    #region 模式

    public void SetMode(
        CollapseMode mode)
    {
        if (_store.Mode == mode)
        {
            return;
        }

        _store.SetMode(mode);

        (int updated, int failed) = _store.RecomputeAll();

        ReportRecompute(updated, failed);

        OnPropertyChanged(nameof(IsCycleMode));
        OnPropertyChanged(nameof(IsFloorMode));
    }

    private void ReportRecompute(
        int updated,
        int failed)
    {
        if (failed > 0)
        {
            MessageBox.Show(
                $"已按新设置重算 {updated} 个节点，{failed} 个节点因输入缺失被跳过。",
                "重新计算",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    #endregion

    #region 刷新

    public void Refresh()
    {
        if (_store.LastError is not null && !_storageErrorShown)
        {
            _storageErrorShown = true;

            MessageBox.Show(
                $"工作区保存失败，数据可能无法恢复。\n原因：{_store.LastError}",
                "存储失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Tabs.Clear();

        foreach (WorkspaceProject project in _store.Projects)
        {
            Tabs.Add(new WorkspaceTabViewModel
            {
                Id = project.Id,
                Name = project.Name,
                IsActive = project.Id == _store.Active?.Id
            });
        }

        _selectedTab = Tabs.FirstOrDefault(tab => tab.IsActive);

        OnPropertyChanged(nameof(SelectedTab));

        Materials.Clear();

        foreach (WorkspaceItem item in _store.Materials)
        {
            Materials.Add(new MaterialViewModel
            {
                Id = item.Id,
                Name = item.Name,
                DimensionText = item.DimensionText,
                Code = item.Code,
                Format = item.Format,
                Layers = item.Layers,
                Parts = item.Parts,
                Thumbnail = RenderThumbnail(item.Code, item.Format, item.Layers, item.Parts)
            });
        }

        BuildNodes();

        OnPropertyChanged(nameof(MaxLayers));
        OnPropertyChanged(nameof(IsCycleMode));
        OnPropertyChanged(nameof(IsFloorMode));
    }

    private void BuildNodes()
    {
        Nodes.Clear();
        Edges.Clear();

        IReadOnlyList<WorkspaceNode> nodes = _store.Nodes;

        foreach (WorkspaceNode node in nodes)
        {
            Nodes.Add(new WorkspaceNodeViewModel(
                node,
                NodeThumbnail(node),
                node.Id == _store.SelectedNode?.Id)
            {
                RefText = node.TargetId is not null &&
                          _store.FindNode(node.TargetId) is WorkspaceNode target
                    ? $"引用 #{target.Seq}"
                    : ""
            });
        }

        var outgoing = _store.Edges
            .GroupBy(edge => edge.FromId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var incoming = _store.Edges
            .GroupBy(edge => edge.ToId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (WorkspaceEdge edge in _store.Edges)
        {
            WorkspaceNode? from = _store.FindNode(edge.FromId);
            WorkspaceNode? to = _store.FindNode(edge.ToId);

            if (from is null || to is null)
            {
                continue;
            }

            Edges.Add(new WorkspaceEdgeViewModel(
                edge,
                from,
                to,
                outgoing[edge.FromId].IndexOf(edge),
                outgoing[edge.FromId].Count,
                incoming[edge.ToId].IndexOf(edge),
                incoming[edge.ToId].Count));
        }

        double maxCol = nodes.Count == 0 ? 1 : nodes.Max(node => node.Col) + 1;
        double maxRow = nodes.Count == 0 ? 1 : nodes.Max(node => node.Row) + 1;

        CanvasWidth = Math.Max(700, maxCol * WorkspaceLayout.CellWidth + 80);
        CanvasHeight = Math.Max(400, maxRow * WorkspaceLayout.CellHeight + 80);
    }

    private ImageSource? NodeThumbnail(
        WorkspaceNode node)
    {
        ShapeDocument? document = _store.GetEffectiveDocument(node);

        return document is null
            ? null
            : new DrawingImage(ShapeGridDrawing.Build(document, 100, 76));
    }

    private static ImageSource? RenderThumbnail(
        string code,
        ShortCodeFormat format,
        int layers,
        int parts)
    {
        ParseResult result = new ShortCodeParser().Parse(code, layers, parts, format);

        return result.Success && result.Document is not null
            ? new DrawingImage(ShapeGridDrawing.Build(result.Document, 100, 76))
            : null;
    }

    #endregion

    #region 节点操作

    private void SelectNode(
        object? parameter)
    {
        if (parameter is not WorkspaceNodeViewModel node)
        {
            return;
        }

        SelectedNode = node;

        _store.SelectNode(node.Node);
    }

    private void ApplyNode(
        object? parameter,
        string operation)
    {
        if (parameter is not WorkspaceNodeViewModel node)
        {
            return;
        }

        NodeActionResult result = operation switch
        {
            "pin" => _store.ApplyPin(node.Id),
            "cut" => _store.ApplyCut(node.Id),
            _ => _store.ApplyCollapse(node.Id)
        };

        if (!result.Created)
        {
            MessageBox.Show(result.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BeginStack(
        object? parameter,
        bool currentIsTop)
    {
        if (parameter is not WorkspaceNodeViewModel node)
        {
            return;
        }

        if (!node.HasShape)
        {
            MessageBox.Show("该节点还没有图形。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

            return;
        }

        _store.BeginStack(node.Id, currentIsTop);
    }

    private void ComputeStack(
        object? parameter)
    {
        if (parameter is not WorkspaceNodeViewModel node)
        {
            return;
        }

        NodeActionResult result = _store.ComputeStack(node.Id);

        MessageBox.Show(result.Message, "堆叠", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteNode(
        object? parameter)
    {
        if (parameter is not WorkspaceNodeViewModel node)
        {
            return;
        }

        int downstream = _store.Downstream(node.Id).Count;

        if (downstream > 0)
        {
            MessageBoxResult answer = MessageBox.Show(
                $"将删除该节点及其 {downstream} 个下游节点，继续？",
                "删除节点",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        _store.DeleteNode(node.Id);
    }

    private void ImportMaterial(
        object? parameter)
    {
        if (parameter is not WorkspaceNodeViewModel node ||
            SelectedMaterial is null)
        {
            MessageBox.Show(
                "请先在左侧选择一个素材。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (node.IsResult)
        {
            MessageBox.Show(
                "结果节点不能导入素材，只能作为其它节点的输入。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        MaterialViewModel material = SelectedMaterial;

        int downstream = _store.Downstream(node.Id).Count;

        if (downstream > 0)
        {
            MessageBoxResult answer = MessageBox.Show(
                $"该节点有 {downstream} 个下游结果，更换图形会一并删除，继续？",
                "确认更换图形",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        if (node.IsRef)
        {
            _store.SetSlotShape(node.Id, material.Code, material.Format, material.Layers, material.Parts);
        }
        else
        {
            _store.ReplaceNodeShape(node.Id, material.Code, material.Format, material.Layers, material.Parts);
        }
    }

    private void ReferenceSelected(
        object? parameter)
    {
        if (parameter is not WorkspaceNodeViewModel slot || SelectedNode is null)
        {
            return;
        }

        WorkspaceNode target = SelectedNode.Node;

        if (target.Kind == "ref" ||
            target.Id == slot.Id ||
            target.Id == slot.Node.BaseId ||
            !target.HasShape)
        {
            MessageBox.Show(
                "请点选一个其它节点（不能引用自己、引用槽位或堆叠底节点）。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _store.SetReferenceTarget(slot.Id, target.Id);
    }

    private void CycleLabel(
        object? parameter)
    {
        if (parameter is WorkspaceEdgeViewModel edge)
        {
            _store.CycleEdgeLabel(edge.Edge.FromId, edge.Edge.ToId);
        }
    }

    #endregion

    #region 工作区操作

    private void NewProject()
    {
        _store.CreateProject();
    }

    public void RenameProject(
        WorkspaceTabViewModel tab,
        string? name)
    {
        _store.RenameProject(tab.Id, name);
    }

    public void DeleteProject(
        WorkspaceTabViewModel tab)
    {
        MessageBoxResult answer = MessageBox.Show(
            $"将删除工作区「{tab.Name}」及其中的全部节点，素材列表不受影响。",
            "删除工作区",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.OK)
        {
            _store.DeleteProject(tab.Id);
        }
    }

    private void ExportImage()
    {
        if (_store.Nodes.Count == 0)
        {
            MessageBox.Show("画布是空的。", "导出图片", MessageBoxButton.OK, MessageBoxImage.Information);

            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出路径图",
            Filter = "PNG 图片 (*.png)|*.png",
            InitialDirectory = StoragePaths.Root,
            FileName = $"shape-path-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            WorkspaceImageExporter.Export(
                dialog.FileName,
                _store.Nodes,
                _store.Edges,
                _store.GetEffectiveDocument);

            MessageBox.Show($"已导出：{dialog.FileName}", "导出图片", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (UnauthorizedAccessException)
        {
            string fallback = Path.Combine(
                StoragePaths.Root,
                Path.GetFileName(dialog.FileName));

            MessageBoxResult answer = MessageBox.Show(
                $"当前运行环境没有写入该目录的权限。\n\n是否改存到数据目录？\n{fallback}",
                "导出图片",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                WorkspaceImageExporter.Export(
                    fallback,
                    _store.Nodes,
                    _store.Edges,
                    _store.GetEffectiveDocument);

                MessageBox.Show($"已导出：{fallback}", "导出图片", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportProject()
    {
        string json = _store.ExportProject();

        var dialog = new SaveFileDialog
        {
            Title = "导出工作区",
            Filter = "工作区 JSON (*.json)|*.json",
            InitialDirectory = StoragePaths.Root,
            FileName = $"workspace-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, json);
        }
        catch (UnauthorizedAccessException)
        {
            string fallback = Path.Combine(
                StoragePaths.Root,
                Path.GetFileName(dialog.FileName));

            MessageBoxResult answer = MessageBox.Show(
                $"当前运行环境没有写入该目录的权限。\n\n是否改存到数据目录？\n{fallback}",
                "导出工作区",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
            {
                File.WriteAllText(fallback, json);

                MessageBox.Show($"已导出：{fallback}", "导出工作区", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void ImportProject()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入工作区",
            Filter = "工作区 JSON (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!_store.ImportProject(File.ReadAllText(dialog.FileName), out string? error))
        {
            MessageBox.Show(error ?? "导入失败。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearNodes()
    {
        if (_store.Nodes.Count == 0)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            "将删除画布上的所有节点，素材列表不受影响。",
            "清空画布",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.OK)
        {
            _store.ClearNodes();
        }
    }

    /// <summary>
    /// 从编辑器把当前图形加入素材库。
    /// </summary>
    public void AddMaterial(
        ShapeDocument document,
        string source = "editor")
    {
        string code = _exporter.Export(document, omitTopEmptyLayers: true);

        WorkspaceAddResult result = _store.AddMaterial(
            code,
            ShortCodeFormat.Full,
            document.LayerCount,
            document.PartCount,
            source);

        MessageBox.Show(
            result switch
            {
                WorkspaceAddResult.Added => "已加入工作区素材库。",
                WorkspaceAddResult.Duplicate => "素材库里已经有相同的图形。",
                WorkspaceAddResult.Full => "素材库已满 200 条。",
                _ => "当前图形无法加入素材库。"
            },
            "加入工作区",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion
}

/// <summary>
/// 标签页。
/// </summary>
public sealed class WorkspaceTabViewModel
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public bool IsActive { get; init; }
}

/// <summary>
/// 素材条目。
/// </summary>
public sealed class MaterialViewModel
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string DimensionText { get; init; } = "";

    public string Code { get; init; } = "";

    public ShortCodeFormat Format { get; init; }

    public int Layers { get; init; }

    public int Parts { get; init; }

    public ImageSource? Thumbnail { get; init; }
}

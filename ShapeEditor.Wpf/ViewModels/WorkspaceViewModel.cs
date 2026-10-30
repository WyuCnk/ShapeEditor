using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
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
///
/// 节点卡片只负责显示；下落、顶针、切割、堆叠、复制、粘贴、删除
/// 全部集中到标签行下方的工具条。
/// </summary>
public sealed class WorkspaceViewModel : ObservableObject
{
    private const string DefaultHint =
        "提示：选中节点后使用工具条操作；双击素材或把素材拖到画布可加入。数据目录：";

    private const string StackHint =
        "堆叠：再点一个节点作为第二个输入，Esc 取消。数据目录：";

    /// <summary>素材拖放时使用的数据格式，值为素材 Id。</summary>
    public const string MaterialDragFormat = "ShapeEditor.Material";

    private readonly WorkspaceStore _store;

    private readonly ShortCodeExporter _exporter = new();

    private WorkspaceTabViewModel? _selectedTab;

    private MaterialViewModel? _selectedMaterial;

    private WorkspaceNodeViewModel? _selectedNode;

    private double _canvasWidth = 800;

    private double _canvasHeight = 500;

    private bool _storageErrorShown;

    /// <summary>复制下来的短代码以及原始尺寸。</summary>
    private string? _clipCode;

    private ShortCodeFormat _clipFormat = ShortCodeFormat.Full;

    private int _clipLayers;

    private int _clipParts;

    /// <summary>正在进行中的堆叠：第一个输入的节点 Id；为空表示没有待选的第二输入。</summary>
    private string? _stackFirstId;

    private string _hintText = DefaultHint;

    public WorkspaceViewModel(
        WorkspaceStore? store = null)
    {
        _store = store ?? new WorkspaceStore();

        _store.Changed += Refresh;

        CollapseCommand = new RelayCommand(p => ApplyNode(p, "collapse"));
        PinCommand = new RelayCommand(p => ApplyNode(p, "pin"));
        CutCommand = new RelayCommand(p => ApplyNode(p, "cut"));
        StackCommand = new RelayCommand(_ => BeginStackFromToolbar());
        CopyCommand = new RelayCommand(_ => CopySelected());
        PasteCommand = new RelayCommand(_ => PasteClipboard());
        DeleteNodeCommand = new RelayCommand(p => DeleteNode(p));
        SelectNodeCommand = new RelayCommand(p => SelectNode(p));

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

    public System.Windows.Input.ICommand StackCommand { get; }

    public System.Windows.Input.ICommand CopyCommand { get; }

    public System.Windows.Input.ICommand PasteCommand { get; }

    public System.Windows.Input.ICommand DeleteNodeCommand { get; }

    public System.Windows.Input.ICommand SelectNodeCommand { get; }

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

    /// <summary>工具条下方的提示文字。</summary>
    public string HintText
    {
        get => _hintText;
        private set => SetProperty(ref _hintText, value);
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

    /// <summary>没有图形时，空节点按工作区默认象限数显示。</summary>
    private int FallbackParts
    {
        get
        {
            int parts = _store.Active?.PartCount ?? 0;

            return parts > 0 ? parts : 4;
        }
    }

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
        if (_stackFirstId is not null && _store.FindNode(_stackFirstId) is null)
        {
            // 第一个输入被删除或切换了工作区，堆叠待选自动取消
            _stackFirstId = null;

            HintText = DefaultHint;
        }

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

        int fallbackParts = FallbackParts;

        IReadOnlyList<WorkspaceNode> nodes = _store.Nodes;

        var nodeLookup = new Dictionary<string, WorkspaceNodeViewModel>();

        foreach (WorkspaceNode node in nodes)
        {
            ShapeDocument? document = _store.GetEffectiveDocument(node);

            var viewModel = new WorkspaceNodeViewModel(
                node,
                document,
                fallbackParts,
                node.Id == _store.SelectedNode?.Id)
            {
                CodeText = document is null
                    ? ""
                    : string.Join(Environment.NewLine, ShapeText.Lines(document)),
                CodeSummary = document is null
                    ? ""
                    : ShapeText.Summary(document)
            };

            nodeLookup[node.Id] = viewModel;

            Nodes.Add(viewModel);
        }

        foreach (WorkspaceEdge edge in _store.Edges)
        {
            if (!nodeLookup.TryGetValue(edge.FromId, out WorkspaceNodeViewModel? from) ||
                !nodeLookup.TryGetValue(edge.ToId, out WorkspaceNodeViewModel? to))
            {
                continue;
            }

            Edges.Add(new WorkspaceEdgeViewModel(edge, from, to));
        }

        double maxRight = 0;

        double maxBottom = 0;

        foreach (WorkspaceNodeViewModel node in Nodes)
        {
            maxRight = Math.Max(maxRight, node.Left + node.NodeWidth);

            maxBottom = Math.Max(maxBottom, node.Top + node.NodeHeight);
        }

        CanvasWidth = Math.Max(800, Math.Ceiling(maxRight + 80));

        CanvasHeight = Math.Max(500, Math.Ceiling(maxBottom + 80));

        SyncSelectedNode();
    }

    /// <summary>
    /// 刷新会重建节点视图模型，选中项要重新按 Id 对应，
    /// 否则工具条会保留已删除节点的旧引用。
    /// </summary>
    private void SyncSelectedNode()
    {
        WorkspaceNode? selected = _store.SelectedNode;

        _selectedNode = selected is null
            ? null
            : Nodes.FirstOrDefault(node => node.Id == selected.Id);

        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelectedNode));
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

    private WorkspaceNode? ResolveNode(
        object? parameter)
    {
        if (parameter is WorkspaceNodeViewModel viewModel)
        {
            return _store.FindNode(viewModel.Id);
        }

        if (parameter is WorkspaceNode node)
        {
            return _store.FindNode(node.Id) ?? node;
        }

        return _store.SelectedNode;
    }

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

    /// <summary>
    /// 处理节点点击：堆叠待选时把该节点作为第二输入，否则只做选中。
    /// </summary>
    public void HandleNodeClick(
        WorkspaceNodeViewModel node)
    {
        if (_stackFirstId is not null)
        {
            WorkspaceNode? first = _store.FindNode(_stackFirstId);

            if (first is null)
            {
                CancelStack();
            }
            else if (node.Id == first.Id)
            {
                HintText = "堆叠：不能用同一个节点作为第二个输入，请选择另一个节点（Esc 取消）。数据目录：";

                return;
            }
            else
            {
                CompleteStack(first, node.Node);

                return;
            }
        }

        SelectNode(node);
    }

    private void ApplyNode(
        object? parameter,
        string operation)
    {
        WorkspaceNode? source = ResolveNode(parameter);

        if (source is null)
        {
            return;
        }

        NodeActionResult result = operation switch
        {
            "pin" => _store.ApplyPin(source.Id),
            "cut" => _store.ApplyCut(source.Id),
            _ => _store.ApplyCollapse(source.Id)
        };

        if (result.Node is null)
        {
            if (!result.Created)
            {
                MessageBox.Show(
                    result.Message,
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        PositionResult(source, result.Node, operation);

        _store.SelectNode(result.Node);
    }

    /// <summary>
    /// 结果节点放在源节点正下方；
    /// 切割生成的两个节点分别放在源节点左下、右下。
    /// </summary>
    private void PositionResult(
        WorkspaceNode source,
        WorkspaceNode result,
        string operation)
    {
        ShapeDocument? sourceDocument = _store.GetEffectiveDocument(source);

        Size sourceSize = WorkspaceLayout.SizeFor(
            source,
            sourceDocument,
            FallbackParts);

        Rect sourceBounds = WorkspaceLayout.BoundsFor(source, sourceSize);

        double below = sourceBounds.Bottom + 56;

        if (operation != "cut")
        {
            _store.SetNodePosition(
                result.Id,
                sourceBounds.Left,
                below);

            return;
        }

        WorkspaceNode? west = _store.Nodes.FirstOrDefault(
            node => node.OpKey == $"cutw:{source.Id}");

        WorkspaceNode? east = _store.Nodes.FirstOrDefault(
            node => node.OpKey == $"cute:{source.Id}");

        if (west is null && east is null)
        {
            _store.SetNodePosition(
                result.Id,
                sourceBounds.Left - 260,
                below);

            return;
        }

        if (west is not null)
        {
            _store.SetNodePosition(
                west.Id,
                Math.Max(WorkspaceLayout.Padding, sourceBounds.Left - 260),
                below);
        }

        if (east is not null)
        {
            _store.SetNodePosition(
                east.Id,
                sourceBounds.Left + 260,
                below);
        }
    }

    private void DeleteNode(
        object? parameter)
    {
        WorkspaceNode? node = ResolveNode(parameter);

        if (node is null)
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

    #endregion

    #region 素材加入画布

    /// <summary>
    /// 双击素材：在 (80, 80) 新建节点并写入图形。
    /// </summary>
    public void AddMaterialAtDefaultPoint(
        MaterialViewModel material)
    {
        AddMaterialToCanvas(
            material,
            80,
            80);
    }

    /// <summary>
    /// 把素材放到画布坐标 (x, y)；
    /// 失败时弹出提示，适合双击/拖放这类直接操作。
    /// </summary>
    public void AddMaterialToCanvas(
        MaterialViewModel material,
        double x,
        double y)
    {
        if (!TryAddMaterialToCanvas(
                material,
                x,
                y,
                out string? error))
        {
            ShowMessage(
                error ?? "素材写入失败。",
                "加入素材");
        }
    }

    /// <summary>
    /// 尝试把素材放到画布坐标 (x, y)。
    /// 象限数不符、数据无效或写入失败时返回 false，不弹窗，便于自检。
    /// </summary>
    public bool TryAddMaterialToCanvas(
        MaterialViewModel material,
        double x,
        double y,
        out string? error)
    {
        error = null;

        if (material.Layers <= 0 ||
            material.Parts <= 0 ||
            string.IsNullOrWhiteSpace(material.Code))
        {
            error = "该素材缺少可用的图形数据。";

            return false;
        }

        if (!_store.PartsAllowed(material.Parts))
        {
            error = $"该工作区的象限数已固定为 {_store.WorkspaceParts}";

            return false;
        }

        WorkspaceNode node = _store.CreateNodeAtPoint(
            x,
            y);

        _store.ReplaceNodeShape(
            node.Id,
            material.Code,
            material.Format,
            material.Layers,
            material.Parts);

        if (!node.HasShape)
        {
            // 写入失败（例如象限数在两次检查之间被锁定）时撤掉空节点
            error = _store.LastError ?? "素材写入失败。";

            _store.DeleteNode(node.Id);

            return false;
        }

        _store.SelectNode(node);

        HintText = $"已把素材 #{node.Seq} 加入画布。数据目录：";

        return true;
    }

    /// <summary>拖放结束时按素材 Id 查找素材并放入画布。</summary>
    public void DropMaterial(
        string materialId,
        double x,
        double y)
    {
        MaterialViewModel? material = Materials.FirstOrDefault(
            item => item.Id == materialId);

        if (material is null)
        {
            return;
        }

        AddMaterialToCanvas(
            material,
            x,
            y);
    }

    private static void ShowMessage(
        string message,
        string title)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    #region 工具条操作

    private void BeginStackFromToolbar()
    {
        if (_stackFirstId is not null)
        {
            CancelStack();

            return;
        }

        WorkspaceNode? source = _store.SelectedNode;

        if (source is null)
        {
            return;
        }

        if (_store.GetEffectiveDocument(source) is null)
        {
            MessageBox.Show(
                "请先选择一个有图形的节点，再点“堆叠”。",
                "堆叠",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _stackFirstId = source.Id;

        HintText = StackHint;
    }

    /// <summary>
    /// 第二个输入就绪：直接调用 ComputeStackOf，
    /// 结果节点放在第二个输入右侧 80px 并选中。
    /// </summary>
    private void CompleteStack(
        WorkspaceNode first,
        WorkspaceNode second)
    {
        NodeActionResult result = _store.ComputeStackOf(
            first.Id,
            second.Id,
            baseIsTop: true);

        if (result.Node is null)
        {
            MessageBox.Show(
                result.Message,
                "堆叠",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // 输入仍不完整时留在待选状态，方便换一个节点重试
            return;
        }

        ShapeDocument? secondDocument = _store.GetEffectiveDocument(second);

        Rect secondBounds = WorkspaceLayout.BoundsFor(
            second,
            WorkspaceLayout.SizeFor(second, secondDocument, FallbackParts));

        _store.SetNodePosition(
            result.Node.Id,
            secondBounds.Right + 80,
            secondBounds.Top);

        _stackFirstId = null;

        HintText = result.Created
            ? "已生成堆叠结果。数据目录："
            : $"{result.Message}数据目录：";

        _store.SelectNode(result.Node);
    }

    /// <summary>
    /// 取消进行中的堆叠（Esc、再点“堆叠”或点画布空白处）。
    /// 新流程不再创建 ref 槽位，因此这里只清状态。
    /// </summary>
    public void CancelStack()
    {
        if (_stackFirstId is null)
        {
            return;
        }

        _stackFirstId = null;

        HintText = DefaultHint;
    }

    private void CopySelected()
    {
        WorkspaceNode? node = _store.SelectedNode;

        if (node is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.Code))
        {
            _clipCode = node.Code;

            _clipFormat = node.Format;

            _clipLayers = node.Layers;

            _clipParts = node.Parts;
        }
        else
        {
            ShapeDocument? document = _store.GetEffectiveDocument(node);

            if (document is null)
            {
                MessageBox.Show(
                    "该节点还没有图形，无法复制。",
                    "复制",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _clipCode = _exporter.Export(document, omitTopEmptyLayers: false);

            _clipFormat = ShortCodeFormat.Full;

            _clipLayers = document.LayerCount;

            _clipParts = document.PartCount;
        }

        if (_clipLayers <= 0 ||
            _clipParts <= 0 ||
            string.IsNullOrWhiteSpace(_clipCode))
        {
            return;
        }

        HintText = $"已复制 #{node.Seq} 的短代码。数据目录：";
    }

    private void PasteClipboard()
    {
        if (_clipCode is null ||
            _clipLayers <= 0 ||
            _clipParts <= 0)
        {
            MessageBox.Show(
                "剪贴板里还没有复制过的图形。",
                "粘贴",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        WorkspaceNode node = _store.CreateNodeAtPoint(
            80,
            80);

        node.Label = "素材节点";

        _store.ReplaceNodeShape(
            node.Id,
            _clipCode,
            _clipFormat,
            _clipLayers,
            _clipParts);

        _store.SelectNode(node);

        HintText = "已粘贴到 (80, 80)。数据目录：";
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
                _store.GetEffectiveDocument,
                FallbackParts);

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
                    _store.GetEffectiveDocument,
                    FallbackParts);

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

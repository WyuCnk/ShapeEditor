namespace ShapeEditor.Wpf.ViewModels;

using System.Reflection;

/// <summary>
/// 主窗口状态：两页之间切换。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private int _tabIndex;

    public MainViewModel()
    {
        Editor = new EditorViewModel();

        Batch = new BatchViewModel();

        Workspace = new WorkspaceViewModel();

        Editor.AddToWorkspaceHandler = document => Workspace.AddMaterial(document);

        Workspace.AddToEditorHandler = AddWorkspaceTargetToEditor;

        Batch.AddToEditorHandler = AddBatchItemToEditor;

        VersionText = "v" +
            (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
             ?? "1.1.0");
    }

    /// <summary>
    /// 状态栏右侧显示的版本号。
    /// </summary>
    public string VersionText { get; }

    public EditorViewModel Editor { get; }

    public BatchViewModel Batch { get; }

    public WorkspaceViewModel Workspace { get; }

    /// <summary>
    /// 工作区「加到编辑区」：按图形原始尺寸载入编辑器，然后切到编辑器页。
    /// 工作区与分析共用同一份取图结果，因此这里直接沿用它的短代码与尺寸。
    /// </summary>
    private bool AddWorkspaceTargetToEditor(
        AnalysisTarget target)
    {
        if (!Editor.LoadFromBatch(
                target.Code,
                target.Layers,
                target.Parts,
                target.Format))
        {
            return false;
        }

        TabIndex = 0;

        return true;
    }

    /// <summary>
    /// 批量查看页「加入编辑区」：按原始尺寸载入图形，然后切到编辑器页。
    /// </summary>
    private bool AddBatchItemToEditor(
        BatchItemViewModel item)
    {
        if (!Editor.LoadFromBatch(
                item.Code,
                item.LayerCount,
                item.PartCount,
                Batch.Format))
        {
            return false;
        }

        TabIndex = 0;

        return true;
    }

    public int TabIndex
    {
        get => _tabIndex;
        set
        {
            if (SetProperty(ref _tabIndex, value))
            {
                OnPropertyChanged(nameof(IsEditorTab));
                OnPropertyChanged(nameof(IsBatchTab));
                OnPropertyChanged(nameof(IsWorkspaceTab));
                OnPropertyChanged(nameof(IsEditorVisible));
                OnPropertyChanged(nameof(IsBatchVisible));
                OnPropertyChanged(nameof(IsWorkspaceVisible));
            }
        }
    }

    public bool IsEditorVisible => TabIndex == 0;

    public bool IsBatchVisible => TabIndex == 1;

    public bool IsWorkspaceVisible => TabIndex == 2;

    public bool IsWorkspaceTab
    {
        get => TabIndex == 2;
        set
        {
            if (value)
            {
                TabIndex = 2;
            }
        }
    }

    public bool IsEditorTab
    {
        get => TabIndex == 0;
        set
        {
            if (value)
            {
                TabIndex = 0;
            }
        }
    }

    public bool IsBatchTab
    {
        get => TabIndex == 1;
        set
        {
            if (value)
            {
                TabIndex = 1;
            }
        }
    }
}

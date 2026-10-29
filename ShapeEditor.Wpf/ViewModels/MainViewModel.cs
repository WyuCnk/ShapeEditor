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

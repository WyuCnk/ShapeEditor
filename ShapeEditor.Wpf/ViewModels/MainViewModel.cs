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

    public int TabIndex
    {
        get => _tabIndex;
        set
        {
            if (SetProperty(ref _tabIndex, value))
            {
                OnPropertyChanged(nameof(IsEditorTab));
                OnPropertyChanged(nameof(IsBatchTab));
                OnPropertyChanged(nameof(IsEditorVisible));
                OnPropertyChanged(nameof(IsBatchVisible));
            }
        }
    }

    public bool IsEditorVisible => TabIndex == 0;

    public bool IsBatchVisible => TabIndex == 1;

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

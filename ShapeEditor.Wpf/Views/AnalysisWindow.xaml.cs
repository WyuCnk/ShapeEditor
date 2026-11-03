using System.Windows;
using System.Windows.Input;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Wpf.Views;

/// <summary>
/// 图形分析窗口。
///
/// 编辑器和工作区都通过 <see cref="ShowFor"/> 打开同一个窗口，
/// 传入短代码及其原始尺寸；窗口内切换下落类型会重新分析。
/// </summary>
public partial class AnalysisWindow : Window
{
    public AnalysisWindow()
        : this("")
    {
    }

    public AnalysisWindow(
        string code,
        int layers = 0,
        int parts = 0,
        ShortCodeFormat format = ShortCodeFormat.Full,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        InitializeComponent();

        DataContext = new AnalysisViewModel(
            code,
            layers,
            parts,
            format,
            mode);
    }

    public AnalysisViewModel? ViewModel => DataContext as AnalysisViewModel;

    /// <summary>
    /// 以非模态方式打开分析窗口；有宿主窗口时跟随其居中。
    /// </summary>
    public static AnalysisWindow ShowFor(
        Window? owner,
        string code,
        int layers = 0,
        int parts = 0,
        ShortCodeFormat format = ShortCodeFormat.Full,
        CollapseMode mode = CollapseMode.IncludeCycles)
    {
        var window = new AnalysisWindow(
            code,
            layers,
            parts,
            format,
            mode);

        if (owner is not null)
        {
            window.Owner = owner;
        }

        window.Show();

        return window;
    }

    private void OnCloseClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnPreviewKeyDown(
        KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();

            e.Handled = true;
        }
    }
}

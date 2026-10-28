using System.Windows;
using System.Windows.Input;

namespace ShapeEditor.Wpf.Views;

public partial class ShortcutHelpWindow : Window
{
    public ShortcutHelpWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 窗口是非模态打开的（Show 而不是 ShowDialog），
    /// IsCancel 不会自动关窗，必须显式处理。
    /// </summary>
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

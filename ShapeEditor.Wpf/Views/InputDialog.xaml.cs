using System.Windows;

namespace ShapeEditor.Wpf.Views;

/// <summary>
/// 单行文本输入对话框（用于重命名工作区）。
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog(
        string title,
        string initialValue)
    {
        InitializeComponent();

        Title = title;
        NameBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string Value => NameBox.Text.Trim();

    private void OnConfirm(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

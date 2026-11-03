using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Wpf.Rendering;
using ShapeEditor.Wpf.Services;
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

    /// <summary>
    /// 导出分析图：与工作区导出图片一样走 SaveFileDialog，
    /// 再把当前分析结果画成单栏 PNG。
    /// </summary>
    private void OnExportImageClick(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel is not AnalysisViewModel viewModel ||
            viewModel.Document is null)
        {
            MessageBox.Show(
                "没有可导出的图形。",
                "导出图片",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出分析图",
            Filter = "PNG 图片 (*.png)|*.png",
            InitialDirectory = StoragePaths.Root,
            FileName = $"shape-analysis-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            AddExtension = true,
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            AnalysisImageExporter.Export(
                dialog.FileName,
                viewModel);

            MessageBox.Show(
                $"已导出：{dialog.FileName}",
                "导出图片",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"导出图片失败：{exception.Message}",
                "导出图片",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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

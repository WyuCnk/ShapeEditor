using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Wpf.Controls;
using ShapeEditor.Wpf.Rendering;
using ShapeEditor.Wpf.ViewModels;
using ShapeEditor.Wpf.Views;

namespace ShapeEditor.Wpf.SelfTest;

/// <summary>
/// 离屏渲染自检。
///
/// 开发机没有可视化桌面时，用它确认绘制管线：
///   ShapeEditor.Wpf.exe --render-test &lt;输出目录&gt;
/// </summary>
public static class RenderRunner
{
    public static int Run(
        string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);

            ShapeDocument document = CreateSampleDocument();

            PngExporter.Save(
                document,
                Path.Combine(outputDirectory, "export-full.png"));

            RenderControl(
                document,
                720,
                460,
                Path.Combine(outputDirectory, "canvas-720x460.png"));

            RenderControl(
                document,
                240,
                160,
                Path.Combine(outputDirectory, "thumb-240x160.png"));

            RenderMany(
                document,
                10,
                Path.Combine(outputDirectory, "stress-10x10.png"));

            RenderEditorView(
                Path.Combine(outputDirectory, "ui-editor.png"));

            RenderBatchView(
                Path.Combine(outputDirectory, "ui-batch.png"));

            RenderMainWindow(
                Path.Combine(outputDirectory, "ui-mainwindow.png"));

            /*
             * 快捷键说明窗口用非模态 Show() 打开，
             * 这里验证「关闭」按钮确实能关掉窗口。
             */
            File.WriteAllText(
                Path.Combine(outputDirectory, "help-window-close.txt"),
                CheckHelpWindowCloses() ? "PASS" : "FAIL");

            File.WriteAllText(
                Path.Combine(outputDirectory, "ok.txt"),
                "render self-test finished");

            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                Path.Combine(outputDirectory, "error.txt"),
                exception.ToString());

            return 1;
        }
    }

    private static ShapeDocument CreateSampleDocument()
    {
        var document = new ShapeDocument(
            layerCount: 4,
            partCount: 4);

        var editor = new ShapeEditorService();

        // 底层：一排图形
        editor.SetType(document, 0, 0, CellType.Shape);
        editor.ApplyColorBrush(document, 0, 0, 'r');
        editor.SetType(document, 0, 1, CellType.Shape);
        editor.ApplyColorBrush(document, 0, 1, 'g');
        editor.SetType(document, 0, 2, CellType.Crystal);
        editor.ApplyColorBrush(document, 0, 2, 'b');
        editor.SetType(document, 0, 3, CellType.Pin);

        // 第二层：晶体
        for (int part = 0; part < 4; part++)
        {
            editor.SetType(document, 1, part, CellType.Crystal);
            editor.ApplyColorBrush(
                document,
                1,
                part,
                part switch
                {
                    0 => 'r',
                    1 => 'm',
                    2 => 'y',
                    _ => 'c'
                });
        }

        // 第三层：黑色图形，验证浅色文字
        editor.SetType(document, 2, 1, CellType.Shape);
        editor.ApplyColorBrush(document, 2, 1, 'k');

        editor.SetType(document, 2, 2, CellType.Shape);
        editor.ApplyColorBrush(document, 2, 2, 'w');

        return document;
    }

    private static void RenderControl(
        ShapeDocument document,
        int width,
        int height,
        string path)
    {
        var view = new ShapeGridView
        {
            Document = document,
            Width = width,
            Height = height,
            SelectedLayer = 1,
            SelectedPart = 1
        };

        var size = new Size(width, height);

        view.Measure(size);

        view.Arrange(new Rect(size));

        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(view);

        SaveBitmap(bitmap, path);
    }

    private static void RenderMany(
        ShapeDocument source,
        int layerCount,
        string path)
    {
        var document = new ShapeDocument(
            layerCount,
            layerCount);

        for (int layer = 0;
             layer < layerCount;
             layer++)
        {
            for (int part = 0;
                 part < layerCount;
                 part++)
            {
                ShapeCell cell = source[
                    layer % source.LayerCount,
                    part % source.PartCount].Clone();

                document[layer, part] = cell;
            }
        }

        PngExporter.Save(document, path);
    }

    private static void RenderEditorView(
        string path)
    {
        var viewModel = new EditorViewModel();

        viewModel.Code = "CrP---Gu:ckckckck";

        viewModel.ParseCode();

        var view = new EditorView
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800
        };

        RenderElement(view, 1280, 800, path);
    }

    private static void RenderBatchView(
        string path)
    {
        var viewModel = new BatchViewModel
        {
            RawText =
                "CrP---Gu:ckckckck" + Environment.NewLine +
                "CcCc" + Environment.NewLine +
                "P-P-P-P-" + Environment.NewLine +
                "-CC-" + Environment.NewLine +
                "CuCuCuCu" + Environment.NewLine +
                "crpcrpcrpcrp"
        };

        viewModel.ParseEntries();

        var view = new BatchView
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800
        };

        RenderElement(view, 1280, 800, path);
    }

    private static void RenderMainWindow(
        string path)
    {
        /*
         * 完整走一遍真实启动路径：构造 MainWindow、MainViewModel，
         * 显示到屏幕外再离屏渲染，用于检查 XAML 资源与绑定。
         */
        var window = new MainWindow
        {
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = -20000,
            Top = -20000,
            Width = 1280,
            Height = 860,
            Opacity = 0
        };

        window.Show();

        window.UpdateLayout();

        if (window.Content is FrameworkElement content)
        {
            int width = (int)Math.Max(1, content.ActualWidth);
            int height = (int)Math.Max(1, content.ActualHeight);

            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);

            bitmap.Render(content);

            SaveBitmap(bitmap, path);
        }

        window.Close();
    }

    private static bool CheckHelpWindowCloses()
    {
        var window = new ShortcutHelpWindow
        {
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = 400,
            Height = 300,
            Opacity = 0
        };

        window.Show();

        window.CloseButton.RaiseEvent(
            new RoutedEventArgs(Button.ClickEvent));

        bool closed = !window.IsVisible;

        if (window.IsVisible)
        {
            window.Close();
        }

        return closed;
    }

    private static void RenderElement(
        FrameworkElement element,
        int width,
        int height,
        string path)
    {
        var size = new Size(width, height);

        element.Measure(size);

        element.Arrange(new Rect(size));

        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(element);

        SaveBitmap(bitmap, path);
    }

    private static void SaveBitmap(
        BitmapSource bitmap,
        string path)
    {
        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using FileStream stream = File.Create(path);

        encoder.Save(stream);
    }
}

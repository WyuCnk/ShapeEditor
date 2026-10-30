using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Wpf.Controls;
using ShapeEditor.Wpf.Rendering;
using ShapeEditor.Core.Workspaces;
using ShapeEditor.Wpf.Services;
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

            RenderWorkspaceView(
                Path.Combine(outputDirectory, "ui-workspace.png"));

            RenderWorkspaceExport(
                Path.Combine(outputDirectory, "workspace-export.png"));

            CheckWorkspaceToolbar(
                Path.Combine(outputDirectory, "workspace-ops.txt"));

            CheckWorkspaceSync2(
                Path.Combine(outputDirectory, "workspace-sync2.txt"));

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

    private static void RenderWorkspaceView(
        string path)
    {
        string storePath = Path.Combine(
            Path.GetTempPath(),
            $"shape-workspace-render-{Guid.NewGuid():N}.json");

        var store = new WorkspaceStore(storePath);

        /*
         * 造一小段制作路径：一个源节点加两个结果节点，
         * 用来检查自适应卡片、连线和箭头层。
         */
        WorkspaceNode source = store.CreateNodeAtPoint(
            80,
            60);

        store.ReplaceNodeShape(
            source.Id,
            "CrP---Gu:ckckckck",
            Core.Models.ShortCodeFormat.Full,
            4,
            4);

        NodeActionResult collapse = store.ApplyCollapse(source.Id);

        NodeActionResult pin = store.ApplyPin(source.Id);

        if (collapse.Node is not null)
        {
            store.SetNodePosition(
                collapse.Node.Id,
                80,
                290);
        }

        if (pin.Node is not null)
        {
            store.SetNodePosition(
                pin.Node.Id,
                300,
                290);

            store.SelectNode(pin.Node);
        }

        var viewModel = new WorkspaceViewModel(store);

        var view = new WorkspaceView
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800
        };

        RenderElement(view, 1280, 800, path);
    }

    private static void RenderWorkspaceExport(
        string path)
    {
        string storePath = Path.Combine(
            Path.GetTempPath(),
            $"shape-workspace-export-{Guid.NewGuid():N}.json");

        var store = new WorkspaceStore(storePath);

        WorkspaceNode node = store.CreateNode();

        store.ReplaceNodeShape(
            node.Id,
            "CuCu",
            Core.Models.ShortCodeFormat.Full,
            1,
            4);

        store.ApplyCollapse(node.Id);
        store.ApplyPin(node.Id);

        Rendering.WorkspaceImageExporter.Export(
            path,
            store.Nodes,
            store.Edges,
            store.GetEffectiveDocument);
    }

    private static void CheckWorkspaceToolbar(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            string storePath = Path.Combine(
                Path.GetTempPath(),
                $"shape-workspace-toolbar-{Guid.NewGuid():N}.json");

            var store = new WorkspaceStore(storePath);

            var viewModel = new WorkspaceViewModel(store);

            WorkspaceNode source = store.CreateNodeAtPoint(
                80,
                60);

            store.ReplaceNodeShape(
                source.Id,
                "CrP---Gu:ckckckck",
                Core.Models.ShortCodeFormat.Full,
                4,
                4);

            store.SelectNode(source);

            WorkspaceNodeViewModel? sourceView = viewModel.Nodes
                .FirstOrDefault(node => node.Id == source.Id);

            if (sourceView is null)
            {
                throw new InvalidOperationException("源节点没有出现在视图集合中。");
            }

            viewModel.CollapseCommand.Execute(sourceView);

            WorkspaceNode? collapsed = store.Nodes.FirstOrDefault(
                node => node.OpKey == $"collapse:{source.Id}");

            lines.Add($"下落结果：{Describe(collapsed)}（期望 X=80，Y=290）");

            store.SelectNode(source);

            sourceView = viewModel.Nodes.FirstOrDefault(node => node.Id == source.Id);

            if (sourceView is not null)
            {
                viewModel.CutCommand.Execute(sourceView);
            }

            WorkspaceNode? west = store.Nodes.FirstOrDefault(
                node => node.OpKey == $"cutw:{source.Id}");

            WorkspaceNode? east = store.Nodes.FirstOrDefault(
                node => node.OpKey == $"cute:{source.Id}");

            lines.Add($"切割左：{Describe(west)}（期望 X=20，Y=290）");

            lines.Add($"切割右：{Describe(east)}（期望 X=340，Y=290）");

            viewModel.CopyCommand.Execute(null);

            viewModel.PasteCommand.Execute(null);

            WorkspaceNode? pasted = store.Nodes.FirstOrDefault(
                node => Math.Abs((node.X ?? -1) - 80) < 0.1 &&
                        Math.Abs((node.Y ?? -1) - 80) < 0.1 &&
                        node.Id != source.Id);

            lines.Add($"粘贴结果：{Describe(pasted)}（期望 X=80，Y=80）");

            /*
             * 堆叠：把粘贴出来的节点当底节点，再点下落结果作为第二输入。
             */
            if (pasted is not null && collapsed is not null)
            {
                store.SelectNode(pasted);

                viewModel.StackCommand.Execute(null);

                WorkspaceNodeViewModel? otherView = viewModel.Nodes
                    .FirstOrDefault(node => node.Id == collapsed.Id);

                if (otherView is not null)
                {
                    viewModel.HandleNodeClick(otherView);
                }
            }

            WorkspaceNode? stacked = store.Nodes.FirstOrDefault(
                node => node.OpKey?.StartsWith("stack:", StringComparison.Ordinal) == true);

            lines.Add($"堆叠结果：{Describe(stacked)}");

            bool deleted = false;

            if (stacked is not null)
            {
                WorkspaceNodeViewModel? stackedView = viewModel.Nodes
                    .FirstOrDefault(node => node.Id == stacked.Id);

                if (stackedView is not null)
                {
                    viewModel.DeleteNodeCommand.Execute(stackedView);

                    deleted = store.FindNode(stacked.Id) is null;
                }
            }

            lines.Add($"删除选中节点：{(deleted ? "成功" : "失败")}");

            ok =
                collapsed is not null &&
                Math.Abs((collapsed.X ?? -1) - 80) < 0.1 &&
                Math.Abs((collapsed.Y ?? -1) - 290) < 0.1 &&
                west is not null &&
                Math.Abs((west.X ?? -1) - 20) < 0.1 &&
                Math.Abs((west.Y ?? -1) - 290) < 0.1 &&
                east is not null &&
                Math.Abs((east.X ?? -1) - 340) < 0.1 &&
                Math.Abs((east.Y ?? -1) - 290) < 0.1 &&
                pasted is not null &&
                stacked is not null &&
                deleted;
        }
        catch (Exception exception)
        {
            lines.Add(exception.ToString());
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");

        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// 自检第二轮改动：素材双击/拖放进入画布、象限数闸门、两击堆叠与取消。
    /// 全部走 ViewModel 的公开入口，不弹窗，输出逐项结果。
    /// </summary>
    private static void CheckWorkspaceSync2(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            string storePath = Path.Combine(
                Path.GetTempPath(),
                $"shape-workspace-sync2-{Guid.NewGuid():N}.json");

            var store = new WorkspaceStore(storePath);

            var viewModel = new WorkspaceViewModel(store);

            // 素材双击：在 (80, 80) 建节点并写入图形
            store.AddMaterial(
                "CuCu",
                Core.Models.ShortCodeFormat.Full,
                1,
                4,
                "selftest");

            MaterialViewModel? material = viewModel.Materials.FirstOrDefault(
                item => item.Code == "CuCu" && item.Parts == 4);

            bool doubleClickAdded = viewModel.TryAddMaterialToCanvas(
                material!,
                80,
                80,
                out string? doubleClickError);

            WorkspaceNode? first = store.Nodes.LastOrDefault();

            bool doubleClickOk =
                doubleClickAdded &&
                first is not null &&
                first.HasShape &&
                Math.Abs((first.X ?? -1) - 80) < 0.1 &&
                Math.Abs((first.Y ?? -1) - 80) < 0.1;

            lines.Add(
                $"双击素材：{Describe(first)}，" +
                $"{(doubleClickOk ? "成功" : "失败")}" +
                (doubleClickError is null ? "" : $"（{doubleClickError}）"));

            // 象限数闸门：工作区已固定为 4 象限，2 象限素材必须被拒绝
            store.AddMaterial(
                "Cu",
                Core.Models.ShortCodeFormat.Full,
                1,
                2,
                "selftest");

            MaterialViewModel? twoPart = viewModel.Materials.FirstOrDefault(
                item => item.Code == "Cu" && item.Parts == 2);

            int beforeReject = store.Nodes.Count;

            bool rejected = !viewModel.TryAddMaterialToCanvas(
                twoPart!,
                500,
                80,
                out string? rejectError);

            bool rejectOk =
                rejected &&
                store.Nodes.Count == beforeReject &&
                rejectError?.Contains("固定为 4", StringComparison.Ordinal) == true;

            lines.Add(
                $"拒绝 2 象限素材：{(rejectOk ? "成功" : "失败")}" +
                (rejectError is null ? "" : $"（{rejectError}）"));

            // 拖放落点：按传入的画布坐标新建节点
            bool dropAdded = viewModel.TryAddMaterialToCanvas(
                material!,
                240,
                160,
                out string? dropError);

            WorkspaceNode? second = store.Nodes.LastOrDefault();

            bool dropOk =
                dropAdded &&
                second is not null &&
                second.HasShape &&
                Math.Abs((second.X ?? -1) - 240) < 0.1 &&
                Math.Abs((second.Y ?? -1) - 160) < 0.1 &&
                second.Id != first?.Id;

            lines.Add(
                $"拖放素材：{Describe(second)}，" +
                $"{(dropOk ? "成功" : "失败")}" +
                (dropError is null ? "" : $"（{dropError}）"));

            // 堆叠第一击后取消：不创建任何槽节点，提示复原
            int beforeStack = store.Nodes.Count;

            store.SelectNode(first);

            viewModel.StackCommand.Execute(null);

            bool hintOk =
                viewModel.HintText.Contains(
                    "再点一个节点作为第二个输入",
                    StringComparison.Ordinal) &&
                viewModel.HintText.Contains(
                    "Esc 取消",
                    StringComparison.Ordinal);

            viewModel.CancelStack();

            bool cancelOk =
                hintOk &&
                store.Nodes.Count == beforeStack &&
                !viewModel.HintText.Contains("Esc 取消", StringComparison.Ordinal) &&
                store.Nodes.All(node => node.Kind != "ref");

            lines.Add(
                $"堆叠提示与取消：{(cancelOk ? "成功" : "失败")}" +
                $"（提示={(hintOk ? "有" : "无")}，" +
                $"节点数={store.Nodes.Count}/{beforeStack}）");

            // 两击堆叠：第二击直接生成结果，结果放在第二输入右侧 80px
            store.SelectNode(first);

            viewModel.StackCommand.Execute(null);

            WorkspaceNodeViewModel? secondView = viewModel.Nodes.FirstOrDefault(
                node => node.Id == second?.Id);

            if (secondView is not null)
            {
                viewModel.HandleNodeClick(secondView);
            }

            WorkspaceNode? stacked = store.Nodes.LastOrDefault();

            bool stackOk =
                secondView is not null &&
                stacked is not null &&
                stacked.OpKey?.StartsWith("stack:", StringComparison.Ordinal) == true &&
                Math.Abs((stacked.X ?? -1) - 460) < 0.1 &&
                Math.Abs((stacked.Y ?? -1) - 160) < 0.1 &&
                store.SelectedNode?.Id == stacked.Id &&
                store.Nodes.All(node => node.Kind != "ref") &&
                !viewModel.HintText.Contains("Esc 取消", StringComparison.Ordinal);

            lines.Add(
                $"两击堆叠：{Describe(stacked)}（期望 X=460，Y=160），" +
                $"{(stackOk ? "成功" : "失败")}");

            ok =
                doubleClickOk &&
                rejectOk &&
                dropOk &&
                cancelOk &&
                stackOk;
        }
        catch (Exception exception)
        {
            lines.Add(exception.ToString());
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");

        File.WriteAllLines(path, lines);
    }

    private static string Describe(
        WorkspaceNode? node)
    {
        return node is null
            ? "缺失"
            : $"#{node.Seq} X={node.X?.ToString("0.##") ?? "?"} Y={node.Y?.ToString("0.##") ?? "?"}";
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

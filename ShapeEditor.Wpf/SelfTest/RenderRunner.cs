using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Analysis;
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
            // 自检期间不要因为窗口关闭而结束应用，否则后续创建视图会失败
            if (System.Windows.Application.Current is not null)
            {
                System.Windows.Application.Current.ShutdownMode =
                    System.Windows.ShutdownMode.OnExplicitShutdown;
            }

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

            RenderWorkspaceExchange(
                Path.Combine(outputDirectory, "ui-workspace-exchange.png"));

            RenderAnalysisWindow(
                Path.Combine(outputDirectory, "ui-analysis.png"));

            RenderAnalysisExport(
                Path.Combine(outputDirectory, "analysis-export.png"));

            CheckAnalysis(
                Path.Combine(outputDirectory, "analysis-check.txt"));

            CheckBatchActions(
                Path.Combine(outputDirectory, "batch-actions.txt"));

            RenderWorkspaceExport(
                Path.Combine(outputDirectory, "workspace-export.png"));

            CheckWorkspaceToolbar(
                Path.Combine(outputDirectory, "workspace-ops.txt"));

            CheckWorkspaceSync2(
                Path.Combine(outputDirectory, "workspace-sync2.txt"));

            CheckWorkspaceExchange(
                Path.Combine(outputDirectory, "workspace-exchange.txt"));

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

    /// <summary>
    /// 交换流程的界面渲染：两个源节点 + 左右两个交换结果节点，
    /// 用来检查「交换」按钮、结果卡片、四条输入连线与摆放位置。
    /// </summary>
    private static void RenderWorkspaceExchange(
        string path)
    {
        string storePath = Path.Combine(
            Path.GetTempPath(),
            $"shape-workspace-exchange-view-{Guid.NewGuid():N}.json");

        var store = new WorkspaceStore(storePath);

        WorkspaceNode first = store.CreateNodeAtPoint(
            80,
            60);

        store.ReplaceNodeShape(
            first.Id,
            "CrP---Gu:ckckckck",
            Core.Models.ShortCodeFormat.Full,
            4,
            4);

        WorkspaceNode second = store.CreateNodeAtPoint(
            300,
            60);

        store.ReplaceNodeShape(
            second.Id,
            "CcCcCcCc:ckckckck",
            Core.Models.ShortCodeFormat.Full,
            4,
            4);

        var viewModel = new WorkspaceViewModel(store);

        store.SelectNode(first);

        viewModel.ExchangeCommand.Execute(null);

        WorkspaceNodeViewModel? secondView = viewModel.Nodes.FirstOrDefault(
            node => node.Id == second.Id);

        if (secondView is not null)
        {
            viewModel.HandleNodeClick(secondView);
        }

        var view = new WorkspaceView
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800
        };

        RenderElement(view, 1280, 800, path);
    }

    /// <summary>
    /// 分析图导出自检：用与分析窗口相同的数据走同一条导出路径，
    /// 生成一张单栏 PNG 供人工检查版式。
    /// </summary>
    private static void RenderAnalysisExport(
        string path)
    {
        ShapeDocument document = CreateSampleDocument();

        string code = new ShortCodeExporter().Export(
            document,
            omitTopEmptyLayers: false);

        var viewModel = new AnalysisViewModel(
            code,
            document.LayerCount,
            document.PartCount,
            Core.Models.ShortCodeFormat.Full,
            Core.Physics.CollapseMode.IncludeCycles);

        AnalysisImageExporter.Export(path, viewModel);
    }

    private static void RenderAnalysisWindow(
        string path)
    {
        ShapeDocument document = CreateSampleDocument();

        string code = new ShortCodeExporter().Export(
            document,
            omitTopEmptyLayers: false);

        /*
         * 分析窗口的入口在编辑器和工作区，这里直接按同样的参数构造，
         * 显示到屏幕外再离屏渲染，检查 XAML、绑定与表格布局。
         *
         * 自检图需要同时看到半边稳定的「一轴一块」与制作路径的分层节点图，
         * 所以把窗口放大到能容纳整页内容（真实使用时由外层滚动查看）。
         */
        var window = new AnalysisWindow(
            code,
            document.LayerCount,
            document.PartCount,
            Core.Models.ShortCodeFormat.Full,
            Core.Physics.CollapseMode.IncludeCycles)
        {
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = 1160,
            Height = 2500,
            Opacity = 0
        };

        window.Show();

        window.UpdateLayout();

        /*
         * 正式界面里节点图容器限高 560，图更大时在容器内滚动。
         * 自检图需要在同一张 PNG 里看到完整的分层 DAG，
         * 这里临时取消限高，让整页按节点图的实际高度排布。
         */
        double graphMaxHeight = window.PathGraphScroll.MaxHeight;

        window.PathGraphScroll.MaxHeight = double.PositiveInfinity;

        window.UpdateLayout();

        if (window.AnalysisScroll?.Content is FrameworkElement page)
        {
            /*
             * 窗口高度会被桌面大小限制，屏幕外的窗口装不下整页内容。
             * 外层 ScrollViewer 已按无限高度排布内容，因此直接渲染
             * 它的内容元素就能得到「一整页」自检图：
             * 半边稳定的一轴一块与制作路径的分层节点图都会出现在同一张图里。
             */
            page.UpdateLayout();

            Thickness margin = page.Margin;

            int width = (int)Math.Max(
                1,
                Math.Ceiling(page.ActualWidth + margin.Left + margin.Right));

            int height = (int)Math.Max(
                1,
                Math.Ceiling(page.ActualHeight + margin.Top + margin.Bottom));

            var visual = new DrawingVisual();

            using (DrawingContext context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    Brushes.White,
                    null,
                    new Rect(0, 0, width, height));

                // 用 VisualBrush 把整页内容画进白底，保留 Grid 自身的 14px 边距。
                var brush = new VisualBrush(page)
                {
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };

                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(
                        margin.Left,
                        margin.Top,
                        page.ActualWidth,
                        page.ActualHeight));
            }

            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);

            bitmap.Render(visual);

            SaveBitmap(bitmap, path);
        }

        window.PathGraphScroll.MaxHeight = graphMaxHeight;

        window.Close();
    }

    /// <summary>
    /// 分析窗口的自检：确认短代码被解析、三个分析器都产出了内容，
    /// 并且切换下落类型后结果会重新计算。
    /// </summary>
    private static void CheckAnalysis(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            ShapeDocument document = CreateSampleDocument();

            string code = new ShortCodeExporter().Export(
                document,
                omitTopEmptyLayers: false);

            var viewModel = new AnalysisViewModel(
                code,
                document.LayerCount,
                document.PartCount,
                Core.Models.ShortCodeFormat.Full,
                Core.Physics.CollapseMode.IncludeCycles);

            bool loaded =
                viewModel.HasDocument &&
                !viewModel.HasError &&
                viewModel.PreviewImage is not null &&
                viewModel.Angles.Count == document.PartCount;

            bool properties = viewModel.PropertyNames.Count > 0;

            bool stability = !string.IsNullOrWhiteSpace(viewModel.StabilityText);

            bool half = !string.IsNullOrWhiteSpace(viewModel.HalfSummary);

            bool halfDirections =
                viewModel.HasHalfDirections &&
                viewModel.ExchangeDirections.Count > 0;

            /*
             * 半边稳定性：一轴一块。默认勾选「只显示合法方向」，
             * 每个轴块只放合法方向；取消勾选后恢复该轴的全部方向。
             */
            int halfAxisCount = viewModel.ExchangeDirections
                .Select(direction => direction.Axis)
                .Distinct()
                .Count();

            bool halfGroupsReady =
                halfAxisCount > 0 &&
                viewModel.HalfAxisGroups.Count == halfAxisCount &&
                viewModel.HalfAxisGroups.All(
                    group => group.Directions.All(direction => direction.IsLegal));

            viewModel.OnlyLegalDirections = false;

            bool halfAllVisible =
                viewModel.HalfAxisGroups.Sum(group => group.Directions.Count) ==
                viewModel.ExchangeDirections.Count;

            viewModel.OnlyLegalDirections = true;

            ProductionPathResult? productionPath =
                viewModel.GetProductionPath();

            bool pathReady = productionPath is not null;

            bool pathSummaryReady =
                !string.IsNullOrWhiteSpace(viewModel.PathSummary);

            string initialPathSummary = viewModel.PathSummary;

            ProductionPathGraph? initialGraph = viewModel.PathGraph;

            int initialPathNodeCount = initialGraph?.Nodes.Count ?? 0;

            int initialPathEdgeCount = initialGraph?.Edges.Count ?? 0;

            // 节点图必须与 Core 的图结构一一对应，且使用 1:1 预览。
            bool pathGraphReady =
                productionPath?.Path is null ||
                (initialGraph is not null &&
                 initialGraph.Nodes.Count == productionPath.Path.Nodes.Count &&
                 initialGraph.Edges.Count == productionPath.Path.Edges.Count);

            /*
             * 4 象限样例：16 节点 / 15 条边，按层分布 4/4/4/2/2，
             * 3 个废料节点（含末端废料）与 3 条废料箭头。
             */
            string rowDistribution = initialGraph is null
                ? ""
                : string.Join(
                    "/",
                    initialGraph.Nodes
                        .GroupBy(node => node.Row)
                        .OrderBy(group => group.Key)
                        .Select(group => group.Count()));

            bool graphLayoutReady =
                initialGraph is not null &&
                initialPathNodeCount == 16 &&
                initialPathEdgeCount == 15 &&
                rowDistribution == "4/4/4/2/2" &&
                initialGraph.Nodes.Count(node => node.IsWaste) == 3 &&
                initialGraph.Edges.Count(edge => edge.IsWaste) == 3 &&
                initialGraph.Nodes.All(node => node.Preview is not null);

            // 制作路径分两区：交换工序默认显示，角的工序默认隐藏。
            bool pathSubSectionsReady =
                viewModel.IsPathExpanded &&
                viewModel.IsPathExchangeExpanded &&
                !viewModel.IsPathAnglesExpanded;

            viewModel.IsFloorMode = true;

            bool modeSwitched =
                viewModel.IsFloorMode &&
                !viewModel.IsCycleMode &&
                !string.IsNullOrWhiteSpace(viewModel.StabilityText);

            // 另建一个仅地面模式的实例，确认切换规则后制作路径确实按新规则重算。
            var floorViewModel = new AnalysisViewModel(
                code,
                document.LayerCount,
                document.PartCount,
                Core.Models.ShortCodeFormat.Full,
                Core.Physics.CollapseMode.FloorOnly);

            bool pathFollowsMode =
                viewModel.PathSummary == floorViewModel.PathSummary &&
                (viewModel.PathGraph?.Nodes.Count ?? -1) ==
                    (floorViewModel.PathGraph?.Nodes.Count ?? -1) &&
                (viewModel.PathGraph?.Edges.Count ?? -1) ==
                    (floorViewModel.PathGraph?.Edges.Count ?? -1);

            /*
             * 2 象限没有合法的交换方向：
             * 「交换工序」整块隐藏，但「角的工序」占位仍在，
             * 也不能退化成「不可制作」文本。
             */
            var noPathViewModel = new AnalysisViewModel(
                "Cu",
                1,
                2,
                Core.Models.ShortCodeFormat.Full,
                Core.Physics.CollapseMode.IncludeCycles);

            bool pathGateHidden =
                noPathViewModel.HasDocument &&
                noPathViewModel.HasPathSection &&
                !noPathViewModel.HasProductionPath &&
                noPathViewModel.PathGraph is null &&
                string.IsNullOrWhiteSpace(noPathViewModel.PathSummary);

            lines.Add($"解析与预览：{(loaded ? "成功" : "失败")}");
            lines.Add($"图形性质：{viewModel.PropertyNames.Count} 项，{(properties ? "成功" : "失败")}");
            lines.Add($"稳定性：{viewModel.StabilityText}");
            lines.Add($"半边稳定性：{viewModel.HalfSummary}");
            lines.Add($"交换方向：{viewModel.ExchangeDirections.Count} 个，{halfAxisCount} 个轴块，默认仅合法：{(halfGroupsReady ? "成功" : "失败")}，取消勾选后全部方向：{(halfAllVisible ? "成功" : "失败")}");
            lines.Add($"交换工序节点图：{initialPathNodeCount} 节点 / {initialPathEdgeCount} 箭头，行分布 {rowDistribution}，废料 {initialGraph?.Nodes.Count(node => node.IsWaste) ?? 0} 个，{(graphLayoutReady ? "成功" : "失败")}");
            lines.Add($"制作路径：{initialPathSummary}（{initialPathNodeCount} 节点 / {initialPathEdgeCount} 箭头）");
            lines.Add($"切换仅地面：{(modeSwitched ? "成功" : "失败")}");
            lines.Add($"切换后制作路径重算：{(pathFollowsMode ? "成功" : "失败")}");
            lines.Add($"制作路径分两区：{(pathSubSectionsReady ? "成功" : "失败")}");
            lines.Add($"无合法交换方向时隐藏交换工序：{(pathGateHidden ? "成功" : "失败")}");

            ok = loaded &&
                 properties &&
                 stability &&
                 half &&
                 halfDirections &&
                 halfGroupsReady &&
                 halfAllVisible &&
                 pathReady &&
                 pathSummaryReady &&
                 pathGraphReady &&
                 graphLayoutReady &&
                 pathSubSectionsReady &&
                 modeSwitched &&
                 pathFollowsMode &&
                 pathGateHidden;
        }
        catch (Exception exception)
        {
            lines.Add(exception.ToString());
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");

        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// 批量查看页动作的自检：确认选中条目可以加入编辑区、
    /// 可以交给分析入口，并且空列表时两个动作都不会生效。
    /// </summary>
    private static void CheckBatchActions(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            var main = new MainViewModel();

            main.Batch.RawText =
                "CuCuCuCu" + Environment.NewLine +
                "CcCcCcCc";

            main.Batch.ParseEntries();

            bool selected =
                main.Batch.HasCurrentDocument &&
                main.Batch.CurrentItem is { Document: not null };

            string? analyzedCode = null;

            main.Batch.AnalyzeHandler = item => analyzedCode = item.Code;

            main.Batch.AddToEditorCommand.Execute(null);

            main.Batch.AnalyzeCommand.Execute(null);

            bool editorLoaded =
                main.TabIndex == 0 &&
                main.Editor.Document.LayerCount == 1 &&
                main.Editor.Document.PartCount == 4 &&
                !string.IsNullOrWhiteSpace(main.Editor.Code) &&
                main.Editor.Code.Length > 0;

            bool analyzed = analyzedCode == "CuCuCuCu";

            /*
             * 清空后没有当前条目：即使命令被触发，
             * 也不应改动编辑器或调用分析入口。
             */
            main.Batch.ClearAll();

            int layerBefore = main.Editor.Document.LayerCount;

            string? analyzedAfterClear = null;

            main.Batch.AnalyzeHandler = item => analyzedAfterClear = item.Code;

            main.Batch.AddToEditorCommand.Execute(null);

            main.Batch.AnalyzeCommand.Execute(null);

            bool emptyIgnored =
                !main.Batch.HasCurrentDocument &&
                analyzedAfterClear is null &&
                main.Editor.Document.LayerCount == layerBefore;

            lines.Add($"选中条目：{(selected ? "成功" : "失败")}");
            lines.Add(
                $"加入编辑区：{(editorLoaded ? "成功" : "失败")}" +
                $"（{main.Editor.Document.LayerCount} 层 × {main.Editor.Document.PartCount} 象限，" +
                $"标签页={main.TabIndex}）");
            lines.Add($"打开分析：{(analyzed ? "成功" : "失败")}（{analyzedCode ?? "无"}）");
            lines.Add($"空列表禁用：{(emptyIgnored ? "成功" : "失败")}");

            ok = selected && editorLoaded && analyzed && emptyIgnored;
        }
        catch (Exception exception)
        {
            lines.Add(exception.ToString());
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");

        File.WriteAllLines(path, lines);
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

            // 没有任何节点/素材时，「加到编辑区」应保持禁用
            bool disabledWithoutSelection = !viewModel.HasSelection;

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

            // 「加到编辑区」：应取选中节点的图形、保留原始尺寸，成功后由主窗口切页
            AnalysisTarget? editorTarget = null;

            viewModel.AddToEditorHandler = target =>
            {
                editorTarget = target;

                return true;
            };

            viewModel.AddToEditorCommand.Execute(null);

            bool addToEditorOk =
                viewModel.HasSelection &&
                editorTarget is not null &&
                editorTarget.Layers == 4 &&
                editorTarget.Parts == 4 &&
                !string.IsNullOrWhiteSpace(editorTarget.Code);

            lines.Add(
                $"加到编辑区：{(addToEditorOk ? "成功" : "失败")}" +
                (editorTarget is null
                    ? ""
                    : $"（{editorTarget.Layers} 层 × {editorTarget.Parts} 象限）"));

            lines.Add($"未选中时禁用：{(disabledWithoutSelection ? "成功" : "失败")}");

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
                deleted &&
                addToEditorOk &&
                disabledWithoutSelection;
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

    /// <summary>
    /// 交换自检：两击流程、标题/幂等键/输入记录、连线与摆放、
    /// 重复定位、取消，以及切换下落规则后按两个输入重算。
    /// 全部走 ViewModel 的公开入口，不弹窗，输出逐项结果。
    /// </summary>
    private static void CheckWorkspaceExchange(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            string storePath = Path.Combine(
                Path.GetTempPath(),
                $"shape-workspace-exchange-{Guid.NewGuid():N}.json");

            var store = new WorkspaceStore(storePath);

            var viewModel = new WorkspaceViewModel(store);

            WorkspaceNode first = store.CreateNodeAtPoint(
                80,
                60);

            store.ReplaceNodeShape(
                first.Id,
                "CrP---Gu:ckckckck",
                Core.Models.ShortCodeFormat.Full,
                4,
                4);

            WorkspaceNode second = store.CreateNodeAtPoint(
                300,
                60);

            store.ReplaceNodeShape(
                second.Id,
                "CcCcCcCc:ckckckck",
                Core.Models.ShortCodeFormat.Full,
                4,
                4);

            // 第一击：进入待选并给出提示
            store.SelectNode(first);

            viewModel.ExchangeCommand.Execute(null);

            bool hintOk =
                viewModel.IsExchangePending &&
                viewModel.HintText.Contains(
                    "再点一个节点作为第二个输入",
                    StringComparison.Ordinal) &&
                viewModel.HintText.Contains(
                    "Esc 取消",
                    StringComparison.Ordinal);

            lines.Add($"交换提示：{(hintOk ? "成功" : "失败")}（{viewModel.HintText}）");

            // 第二击：点另一个节点完成交换
            WorkspaceNodeViewModel? secondView = viewModel.Nodes.FirstOrDefault(
                node => node.Id == second.Id);

            if (secondView is not null)
            {
                viewModel.HandleNodeClick(secondView);
            }

            string firstKey = $"exchange1:{first.Id}|{second.Id}";

            string secondKey = $"exchange2:{first.Id}|{second.Id}";

            WorkspaceNode? firstResult = store.Nodes.FirstOrDefault(
                node => node.OpKey == firstKey);

            WorkspaceNode? secondResult = store.Nodes.FirstOrDefault(
                node => node.OpKey == secondKey);

            bool titleOk =
                firstResult?.Label == WorkspaceExchangeGraph.ResultLabel &&
                secondResult?.Label == WorkspaceExchangeGraph.ResultLabel;

            bool inputOk =
                firstResult is not null &&
                secondResult is not null &&
                firstResult.InputAId == first.Id &&
                firstResult.InputBId == second.Id &&
                secondResult.InputAId == first.Id &&
                secondResult.InputBId == second.Id;

            bool positionOk =
                firstResult is not null &&
                secondResult is not null &&
                Math.Abs((firstResult.X ?? -1) - 40) < 0.1 &&
                Math.Abs((firstResult.Y ?? -1) - 480) < 0.1 &&
                Math.Abs((secondResult.X ?? -1) - 560) < 0.1 &&
                Math.Abs((secondResult.Y ?? -1) - 480) < 0.1;

            bool edgeOk =
                firstResult is not null &&
                secondResult is not null &&
                store.Edges.Count(
                    edge => edge.Label == WorkspaceExchangeGraph.EdgeLabel) == 4 &&
                store.Edges.Count(
                    edge => edge.ToId == firstResult.Id &&
                            edge.Label == WorkspaceExchangeGraph.EdgeLabel) == 2 &&
                store.Edges.Count(
                    edge => edge.ToId == secondResult.Id &&
                            edge.Label == WorkspaceExchangeGraph.EdgeLabel) == 2;

            bool selectedOk =
                firstResult is not null &&
                store.SelectedNode?.Id == firstResult.Id;

            bool pendingCleared =
                !viewModel.IsExchangePending &&
                !viewModel.HintText.Contains("Esc 取消", StringComparison.Ordinal);

            // 初始结果应与 IncludeCycles 下的 Exchange 完全一致
            ShapeDocument? firstDocument = store.GetEffectiveDocument(first);

            ShapeDocument? secondDocument = store.GetEffectiveDocument(second);

            string expectedFirstCode = "";

            string expectedSecondCode = "";

            if (firstDocument is not null && secondDocument is not null)
            {
                var initial = ShapeExchange.Exchange(
                    firstDocument,
                    secondDocument,
                    Core.Physics.CollapseMode.IncludeCycles);

                if (initial.First is not null && initial.Second is not null)
                {
                    expectedFirstCode = new ShortCodeExporter().Export(
                        initial.First,
                        omitTopEmptyLayers: true);

                    expectedSecondCode = new ShortCodeExporter().Export(
                        initial.Second,
                        omitTopEmptyLayers: true);
                }
            }

            bool contentOk =
                firstResult is not null &&
                secondResult is not null &&
                !string.IsNullOrEmpty(expectedFirstCode) &&
                firstResult.Code == expectedFirstCode &&
                secondResult.Code == expectedSecondCode;

            lines.Add($"交换结果：{Describe(firstResult)} / {Describe(secondResult)}（期望 40,480 / 560,480）");
            lines.Add($"标题/输入/内容：{(titleOk && inputOk && contentOk ? "成功" : "失败")}");
            lines.Add($"连线：{(edgeOk ? "成功" : "失败")}，选中左结果：{(selectedOk ? "成功" : "失败")}，待选：{(pendingCleared ? "已清除" : "仍在")}");

            int nodeCount = store.Nodes.Count;

            // 同一组合再交换一次：定位到已有节点，不新增节点
            store.SelectNode(first);

            viewModel.ExchangeCommand.Execute(null);

            secondView = viewModel.Nodes.FirstOrDefault(
                node => node.Id == second.Id);

            if (secondView is not null)
            {
                viewModel.HandleNodeClick(secondView);
            }

            bool idempotent =
                store.Nodes.Count == nodeCount &&
                store.Nodes.Count(node => node.OpKey == firstKey) == 1 &&
                store.Nodes.Count(node => node.OpKey == secondKey) == 1 &&
                viewModel.HintText.Contains("已定位到结果", StringComparison.Ordinal);

            lines.Add($"重复交换：{viewModel.HintText}（{(idempotent ? "成功" : "失败")}）");

            // 再点一次「交换」按钮取消待选
            store.SelectNode(first);

            viewModel.ExchangeCommand.Execute(null);

            bool pendingBeforeCancel = viewModel.IsExchangePending;

            viewModel.ExchangeCommand.Execute(null);

            bool cancelOk =
                pendingBeforeCancel &&
                !viewModel.IsExchangePending &&
                !viewModel.HintText.Contains("Esc 取消", StringComparison.Ordinal);

            lines.Add($"交换取消：{(cancelOk ? "成功" : "失败")}");

            // 切换下落规则：两个结果节点按两个输入重新 Exchange
            viewModel.SetMode(Core.Physics.CollapseMode.FloorOnly);

            ShapeDocument? floorFirstDocument = store.GetEffectiveDocument(first);

            ShapeDocument? floorSecondDocument = store.GetEffectiveDocument(second);

            string expectedFloorFirst = "";

            string expectedFloorSecond = "";

            if (floorFirstDocument is not null && floorSecondDocument is not null)
            {
                var exchanged = ShapeExchange.Exchange(
                    floorFirstDocument,
                    floorSecondDocument,
                    Core.Physics.CollapseMode.FloorOnly);

                if (exchanged.First is not null && exchanged.Second is not null)
                {
                    expectedFloorFirst = new ShortCodeExporter().Export(
                        exchanged.First,
                        omitTopEmptyLayers: true);

                    expectedFloorSecond = new ShortCodeExporter().Export(
                        exchanged.Second,
                        omitTopEmptyLayers: true);
                }
            }

            string cachedFirstCode = firstResult is null
                ? ""
                : store.GetEffectiveDocument(firstResult) is ShapeDocument cachedFirst
                    ? new ShortCodeExporter().Export(cachedFirst, omitTopEmptyLayers: true)
                    : "";

            string cachedSecondCode = secondResult is null
                ? ""
                : store.GetEffectiveDocument(secondResult) is ShapeDocument cachedSecond
                    ? new ShortCodeExporter().Export(cachedSecond, omitTopEmptyLayers: true)
                    : "";

            bool cacheOk =
                cachedFirstCode == expectedFloorFirst &&
                cachedSecondCode == expectedFloorSecond;

            bool recomputeOk =
                firstResult is not null &&
                secondResult is not null &&
                !string.IsNullOrEmpty(expectedFloorFirst) &&
                firstResult.Code == expectedFloorFirst &&
                secondResult.Code == expectedFloorSecond &&
                firstResult.OpKey == firstKey &&
                secondResult.OpKey == secondKey &&
                cacheOk;

            lines.Add($"仅地面重算：交换1={firstResult?.Code}，交换2={secondResult?.Code}，缓存：{(cacheOk ? "已更新" : "仍是旧图形")}，{(recomputeOk ? "成功" : "失败")}");

            ok =
                hintOk &&
                titleOk &&
                inputOk &&
                positionOk &&
                edgeOk &&
                selectedOk &&
                pendingCleared &&
                contentOk &&
                idempotent &&
                cancelOk &&
                recomputeOk;
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

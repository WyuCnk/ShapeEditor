using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Presentation.Cards;
using ShapeEditor.Wpf.Controls;
using ShapeEditor.Wpf.Cards;
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
    /// <summary>验收图形：只有第 1–2 象限有材料，三层卡片全部通过真实回放。</summary>
    private const string WorkOrderHalfOnlyExample =
        "crCu----:CuCu----:--Cu----:CuCu----:--------:crcr----:P-cr----:Cucr----";

    /// <summary>失败形态：半边1 / 交换卡未通过回放，展示红色警示且没有输入链。</summary>
    private const string WorkOrderFailureExample =
        "crCu----:CuCu----:--Cu----:CuCu----:--------:cr------:crcr----:crcr----:Cucr----:crcr----";

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

            RenderAnalysisCardsExport(
                Path.Combine(outputDirectory, "analysis-export-cards.png"));

            RenderAnalysisCardsFailureExport(
                Path.Combine(outputDirectory, "analysis-export-cards-failure.png"));

            RenderAnalysisCardsWindow(
                Path.Combine(outputDirectory, "ui-analysis-cards.png"));

            RenderAnalysisCardsNarrowWindow(
                Path.Combine(outputDirectory, "ui-analysis-cards-narrow.png"));

            RenderAnalysisCardsMediumWindow(
                Path.Combine(outputDirectory, "ui-analysis-cards-1160.png"));

            RenderAnalysisCardsFailureWindow(
                Path.Combine(outputDirectory, "ui-analysis-cards-failure.png"));

            RenderAnalysisFallbackWindow(
                Path.Combine(outputDirectory, "ui-analysis-fallback.png"));

            CheckAnalysis(
                Path.Combine(outputDirectory, "analysis-check.txt"));

            CheckWorkOrderCards(
                Path.Combine(outputDirectory, "cards-check.txt"));

            CheckBatchActions(
                Path.Combine(outputDirectory, "batch-actions.txt"));

            RenderWorkspaceExport(
                Path.Combine(outputDirectory, "workspace-export.png"));

            CheckWorkspaceToolbar(
                Path.Combine(outputDirectory, "workspace-ops.txt"));

            CheckWorkspaceProjects(
                Path.Combine(outputDirectory, "workspace-projects.txt"));

            CheckWorkspaceSync2(
                Path.Combine(outputDirectory, "workspace-sync2.txt"));

            CheckWorkspaceExchange(
                Path.Combine(outputDirectory, "workspace-exchange.txt"));

            CheckEditorTransforms(
                Path.Combine(outputDirectory, "editor-transforms.txt"));

            CheckWorkspaceRotate(
                Path.Combine(outputDirectory, "workspace-rotate.txt"));

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

        var viewModel = new AnalysisViewModel(
            CreateSampleCode(document),
            document.LayerCount,
            document.PartCount,
            Core.Models.ShortCodeFormat.Full,
            Core.Physics.CollapseMode.IncludeCycles);

        AnalysisImageExporter.Export(path, viewModel);
    }

    /// <summary>验收图形的导出图：与窗口消费同一份卡片视图。</summary>
    private static void RenderAnalysisCardsExport(
        string path)
    {
        AnalysisImageExporter.Export(
            path,
            new AnalysisViewModel(WorkOrderHalfOnlyExample));
    }

    /// <summary>失败形态的导出图：未验证卡的红色警示框。</summary>
    private static void RenderAnalysisCardsFailureExport(
        string path)
    {
        AnalysisImageExporter.Export(
            path,
            new AnalysisViewModel(WorkOrderFailureExample));
    }

    /// <summary>样例图形的完整短代码（保留顶部空层，便于与窗口尺寸对齐）。</summary>
    private static string CreateSampleCode(
        ShapeDocument document) =>
        new ShortCodeExporter().Export(
            document,
            omitTopEmptyLayers: false);

    private static void RenderAnalysisWindow(
        string path)
    {
        ShapeDocument document = CreateSampleDocument();

        /*
         * 自检图需要同时看到半边稳定的「一轴一块」与三层工序卡片，
         * 所以把窗口放大到能容纳整页内容（真实使用时由外层滚动查看）。
         */
        RenderAnalysisPage(
            CreateSampleCode(document),
            path,
            1160,
            2600);
    }

    /// <summary>验收图形的三层工序卡片（全部已验证）渲染图。</summary>
    private static void RenderAnalysisCardsWindow(
        string path)
    {
        /*
         * B 方案后单卡变宽（约 1200–1600），窗口取 1700 宽，
         * 让「输入 → 前态 → 步骤 → 输出」在截图里完整排成一行。
         */
        RenderAnalysisPage(
            WorkOrderHalfOnlyExample,
            path,
            1700,
            2400);
    }

    /// <summary>
    /// 窄窗口下的三层卡片：卡内区块自动换行，用来确认没有内容被裁切。
    /// </summary>
    private static void RenderAnalysisCardsNarrowWindow(
        string path)
    {
        RenderAnalysisPage(
            WorkOrderHalfOnlyExample,
            path,
            900,
            3200);
    }

    /// <summary>
    /// 旧截图用的 1160 宽窗口：用于和改前 1105×4492 的整页高度直接对比。
    /// </summary>
    private static void RenderAnalysisCardsMediumWindow(
        string path)
    {
        RenderAnalysisPage(
            WorkOrderHalfOnlyExample,
            path,
            1160,
            3000);
    }

    /// <summary>失败形态：未验证卡的红色警示 / Note / 无输入链渲染图。</summary>
    private static void RenderAnalysisCardsFailureWindow(
        string path)
    {
        RenderAnalysisPage(
            WorkOrderFailureExample,
            path,
            1700,
            2600);
    }

    /// <summary>没有卡片集（n = 2）时回退旧「制作路径」区块的渲染图。</summary>
    private static void RenderAnalysisFallbackWindow(
        string path)
    {
        RenderAnalysisPage(
            "Cu",
            path,
            900,
            1400,
            layers: 1,
            parts: 2);
    }

    /// <summary>
    /// 把分析窗口的整页内容离屏渲染成一张 PNG。
    /// 窗口放在屏幕外，外层 ScrollViewer 按无限高度排布内容，
    /// 因此直接渲染它的内容元素就能得到完整一页。
    /// </summary>
    private static void RenderAnalysisPage(
        string code,
        string path,
        double windowWidth,
        double windowHeight,
        int layers = 0,
        int parts = 0)
    {
        var window = new AnalysisWindow(
            code,
            layers,
            parts,
            Core.Models.ShortCodeFormat.Full,
            Core.Physics.CollapseMode.IncludeCycles)
        {
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = windowWidth,
            Height = windowHeight,
            Opacity = 0
        };

        window.Show();

        window.UpdateLayout();

        /*
         * 正式界面里旧的节点图容器限高 560，图更大时在容器内滚动。
         * 自检图需要在同一张 PNG 里看到完整的分层 DAG，
         * 这里临时取消限高，让整页按节点图的实际高度排布。
         */
        double graphMaxHeight = window.PathGraphScroll.MaxHeight;

        window.PathGraphScroll.MaxHeight = double.PositiveInfinity;

        window.UpdateLayout();

        if (window.AnalysisScroll?.Content is FrameworkElement page)
        {
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

            /*
             * 工序卡片：分析时按图形建一次并缓存。
             * 有卡片集时，旧的 ProductionPathParser 不再运行，
             * 窗口与导出都消费同一个 CardVisuals。
             */
            WorkOrderCardVisualSet? cardSet = viewModel.CardVisuals;

            bool cardsReady =
                viewModel.HasWorkOrderCards &&
                cardSet is not null &&
                cardSet.Layers.Count == 3 &&
                cardSet.Layers[0].Kind == WorkOrderCardLayerKind.Exchange &&
                cardSet.Layers[1].Kind == WorkOrderCardLayerKind.Half &&
                cardSet.Layers[2].Kind == WorkOrderCardLayerKind.Corner &&
                cardSet.Layers[0].Cards.Count == 1 &&
                cardSet.Layers[1].Cards.Count == 2 &&
                cardSet.Layers[2].Cards.Count == 4 &&
                cardSet.TotalCount == 7 &&
                cardSet.VerifiedCount == 7;

            bool cardPreviewsReady =
                cardSet is not null &&
                cardSet.Layers
                    .SelectMany(layer => layer.Cards)
                    .All(card =>
                        card.Output.Preview is not null &&
                        (card.PreState is null || card.PreState.Preview is not null) &&
                        card.Inputs.All(input => input.Preview.Preview is not null) &&
                        card.Steps.All(step => step.Preview.Preview is not null));

            bool cardsReplaceLegacyPath =
                viewModel.GetProductionPath() is null &&
                viewModel.PathGraph is null &&
                string.IsNullOrWhiteSpace(viewModel.PathSummary) &&
                !viewModel.ShowLegacyPathSection;

            string initialCardsSummary = viewModel.CardsSummary;

            int initialCardCount = cardSet?.TotalCount ?? 0;

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

            /*
             * 卡片只依赖图形，切换下落规则应复用缓存而不是重跑 CardBuilder；
             * 这里用引用相等确认同一个视图对象被保留下来。
             */
            bool cardsReused =
                ReferenceEquals(cardSet, viewModel.CardVisuals) &&
                viewModel.HasWorkOrderCards &&
                viewModel.CardVisuals?.TotalCount == initialCardCount &&
                viewModel.CardsSummary == initialCardsSummary;

            /*
             * 2 象限没有合法的交换方向，CardBuilder 也返回空卡片集：
             * 保留旧的「制作路径」区块作为回退。
             */
            var noPathViewModel = new AnalysisViewModel(
                "Cu",
                1,
                2,
                Core.Models.ShortCodeFormat.Full,
                Core.Physics.CollapseMode.IncludeCycles);

            bool cardsFallback =
                noPathViewModel.HasDocument &&
                !noPathViewModel.HasWorkOrderCards &&
                noPathViewModel.CardVisuals is null &&
                noPathViewModel.WorkOrderCards is null &&
                noPathViewModel.ShowLegacyPathSection &&
                noPathViewModel.HasPathSection;

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
            lines.Add($"工序卡片：{cardSet?.TotalCount ?? 0} 张（交换 {cardSet?.Layers[0].Cards.Count ?? 0} / 半边 {cardSet?.Layers[1].Cards.Count ?? 0} / 角 {cardSet?.Layers[2].Cards.Count ?? 0}），已验证 {cardSet?.VerifiedCount ?? 0}/{cardSet?.TotalCount ?? 0}，{(cardsReady ? "成功" : "失败")}");
            lines.Add($"卡片 30px 预览：{(cardPreviewsReady ? "成功" : "失败")}");
            lines.Add($"有卡片时替换旧制作路径：{(cardsReplaceLegacyPath ? "成功" : "失败")}");
            lines.Add($"切换仅地面：{(modeSwitched ? "成功" : "失败")}");
            lines.Add($"切换后卡片缓存复用：{(cardsReused ? "成功" : "失败")}");
            lines.Add($"制作路径分两区（旧开关保留）：{(pathSubSectionsReady ? "成功" : "失败")}");
            lines.Add($"n=2 无卡片时回退旧制作路径：{(cardsFallback && pathGateHidden ? "成功" : "失败")}");

            ok = loaded &&
                 properties &&
                 stability &&
                 half &&
                 halfDirections &&
                 halfGroupsReady &&
                 halfAllVisible &&
                 cardsReady &&
                 cardPreviewsReady &&
                 cardsReplaceLegacyPath &&
                 pathSubSectionsReady &&
                 modeSwitched &&
                 cardsReused &&
                 cardsFallback &&
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
    /// 三层工序卡片自检：
    /// <list type="number">
    /// <item>验收图形 = 5 张卡（交换 1 / 半边 2 / 角 2），全部已验证；</item>
    /// <item>半边1 = A 类·角产物交换，可见步骤 交换 → 拆柱 → 跌落，前态「拆柱后（跌落前）」；</item>
    /// <item>角卡保留自己的碎晶步骤，交换卡只有交换、没有可见废料；</item>
    /// <item>失败形态里未验证卡有红色警示 / Note，且没有输入链；</item>
    /// <item>n = 2 没有卡片集时回退旧「制作路径」区块；</item>
    /// <item>窗口里一层一行一张卡，卡内区块横向排列；窄窗口自动换行且不裁切。</item>
    /// </list>
    /// </summary>
    private static void CheckWorkOrderCards(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            var viewModel = new AnalysisViewModel(WorkOrderHalfOnlyExample);

            WorkOrderCardVisualSet? cards = viewModel.CardVisuals;

            bool threeLayers =
                cards is not null &&
                viewModel.HasWorkOrderCards &&
                cards.Layers.Count == 3 &&
                cards.Layers[0].Title == "交换工序" &&
                cards.Layers[1].Title == "半边工序" &&
                cards.Layers[2].Title == "角工序" &&
                cards.Layers[0].Cards.Count == 1 &&
                cards.Layers[1].Cards.Count == 2 &&
                cards.Layers[2].Cards.Count == 2 &&
                cards.TotalCount == 5 &&
                cards.VerifiedCount == 5;

            WorkOrderCardViewModel? half1 = cards?.Layers[1].Cards[0];

            string half1Steps = half1 is null
                ? ""
                : string.Join("→", half1.Steps.Select(step => step.Operation));

            bool half1Ready =
                half1 is { Verified: true } &&
                half1.ModeText == "A 类 · 角产物交换" &&
                half1.Inputs.Count == 2 &&
                half1Steps == "交换→拆柱→跌落" &&
                half1.Steps.All(step => step.Operation != "碎晶") &&
                half1 is { HasPreState: true, PreStateTitle: "拆柱后（跌落前）" } &&
                half1.PreStateSectionTitle == "前态 · 拆柱后（跌落前）";

            WorkOrderCardViewModel[] corners = cards is null
                ? Array.Empty<WorkOrderCardViewModel>()
                : cards.Layers[2].Cards.ToArray();

            bool cornersReady =
                corners.Length == 2 &&
                corners.All(card =>
                    card.Verified &&
                    card.Steps.Any(step => step.Operation == "碎晶") &&
                    card.HasCornerCaption &&
                    card.CornerCaption == "Q1 目标角 / Q2 右柱 / Q4 左柱" &&
                    card.Output.Preview is not null);

            WorkOrderCardViewModel? exchange = cards?.Layers[0].Cards[0];

            bool exchangeReady =
                exchange is { Verified: true } &&
                exchange.Inputs.Count == 2 &&
                exchange.Steps.Count == 1 &&
                exchange.Steps[0].Operation == "交换";

            bool noWaste =
                cards is not null &&
                cards.Layers
                    .SelectMany(layer => layer.Cards)
                    .SelectMany(card => card.Steps)
                    .All(step => step.Operation != "废料");

            bool previewsReady =
                cards is not null &&
                cards.Layers
                    .SelectMany(layer => layer.Cards)
                    .All(card =>
                        card.Output.Preview is not null &&
                        (card.PreState is null || card.PreState.Preview is not null) &&
                        card.Inputs.All(input => input.Preview.Preview is not null) &&
                        card.Steps.All(step => step.Preview.Preview is not null));

            int cardCount = cards is null
                ? 0
                : cards.Layers.Sum(layer => layer.Cards.Count);

            int exportCount = viewModel.WorkOrderCards?
                .ToExportLayers()
                .Sum(layer => layer.Count) ?? -1;

            bool exportMirrorsCards =
                viewModel.WorkOrderCards is not null &&
                viewModel.WorkOrderCards.TotalCount == cardCount &&
                exportCount == cardCount;

            /*
             * 失败形态：半边1 与交换卡未通过回放，必须显示红色警示 + Note，
             * 而且不显示输入链。
             */
            var failureViewModel = new AnalysisViewModel(WorkOrderFailureExample);

            WorkOrderCardVisualSet? failureCards = failureViewModel.CardVisuals;

            WorkOrderCardViewModel? failureHalf = failureCards?.Layers[1].Cards[0];

            WorkOrderCardViewModel? failureExchange = failureCards?.Layers[0].Cards[0];

            bool failureReady =
                failureCards is { TotalCount: 5, VerifiedCount: 3 } &&
                failureHalf is
                {
                    Verified: false,
                    ShowWarning: true,
                    HasNote: true,
                    HasInputs: false
                } &&
                failureExchange is
                {
                    Verified: false,
                    ShowWarning: true,
                    HasNote: true,
                    HasInputs: false
                } &&
                failureHalf.Inputs.Count == 0 &&
                failureExchange.Inputs.Count == 0;

            /*
             * 回退：n = 2 时 CardBuilder 返回空卡片集，
             * 旧的「制作路径」区块必须保留。
             */
            var fallbackViewModel = new AnalysisViewModel(
                "Cu",
                1,
                2,
                Core.Models.ShortCodeFormat.Full,
                Core.Physics.CollapseMode.IncludeCycles);

            bool fallbackReady =
                fallbackViewModel.HasDocument &&
                !fallbackViewModel.HasWorkOrderCards &&
                fallbackViewModel.CardVisuals is null &&
                fallbackViewModel.ShowLegacyPathSection;

            bool layoutWide =
                CheckWorkOrderCardWindowLayout(
                    WorkOrderHalfOnlyExample,
                    expectedCards: 5,
                    windowWidth: 1700,
                    expectSingleFlowRow: true,
                    minimumFlowBlocks: 4,
                    out string layoutWideDetail);

            bool layoutMedium =
                CheckWorkOrderCardWindowLayout(
                    WorkOrderHalfOnlyExample,
                    expectedCards: 5,
                    windowWidth: 1160,
                    expectSingleFlowRow: false,
                    minimumFlowBlocks: 4,
                    out string layoutMediumDetail);

            bool layoutNarrow =
                CheckWorkOrderCardWindowLayout(
                    WorkOrderHalfOnlyExample,
                    expectedCards: 5,
                    windowWidth: 900,
                    expectSingleFlowRow: false,
                    minimumFlowBlocks: 4,
                    out string layoutNarrowDetail);

            bool layoutFailure =
                CheckWorkOrderCardWindowLayout(
                    WorkOrderFailureExample,
                    expectedCards: 5,
                    windowWidth: 1700,
                    expectSingleFlowRow: true,
                    minimumFlowBlocks: 3,
                    out string layoutFailureDetail);

            bool windowLayout =
                layoutWide &&
                layoutMedium &&
                layoutNarrow &&
                layoutFailure;

            lines.Add($"验收图形卡片：{(threeLayers ? "通" : "失")}（层 {cards?.Layers.Count ?? 0}，共 {cardCount} 张，已验证 {cards?.VerifiedCount ?? 0}）");
            lines.Add($"半边1：{(half1Ready ? "通" : "失")}（{half1?.ModeText}，步骤 {half1Steps}，前态 {half1?.PreStateSectionTitle}）");
            lines.Add($"角卡保留碎晶 / Q1/Q2/Q4 小字：{(cornersReady ? "通" : "失")}");
            lines.Add($"交换卡只有交换、无可见废料：{(exchangeReady && noWaste ? "通" : "失")}");
            lines.Add($"卡片 30px 预览：{(previewsReady ? "通" : "失")}");
            lines.Add($"导出与窗口同源：{(exportMirrorsCards ? "通" : "失")}（导出 {exportCount} 张）");
            lines.Add($"失败形态红色警示 / Note / 无输入链：{(failureReady ? "通" : "失")}");
            lines.Add($"n=2 回退旧制作路径：{(fallbackReady ? "通" : "失")}");
            lines.Add($"窗口卡片一行一张 + 卡内横向排列：{(windowLayout ? "通" : "失")}（{layoutWideDetail}）");
            lines.Add($"窗口 1160 宽卡内换行 / 不裁切：{(layoutMedium ? "通" : "失")}（{layoutMediumDetail}）");
            lines.Add($"窗口 900 宽卡内换行 / 不裁切：{(layoutNarrow ? "通" : "失")}（{layoutNarrowDetail}）");
            lines.Add($"失败形态窗口卡内横向排列：{(layoutFailure ? "通" : "失")}（{layoutFailureDetail}）");

            ok = threeLayers &&
                 half1Ready &&
                 cornersReady &&
                 exchangeReady &&
                 noWaste &&
                 previewsReady &&
                 exportMirrorsCards &&
                 failureReady &&
                 fallbackReady &&
                 windowLayout;
        }
        catch (Exception exception)
        {
            lines.Add(exception.ToString());
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");

        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// 打开分析窗口，确认三层卡片「一行一张」、卡内区块从左到右依次排列，
    /// 并确认窄窗口下卡内自动换行、没有任何区块超出卡片。
    /// </summary>
    private static bool CheckWorkOrderCardWindowLayout(
        string code,
        int expectedCards,
        double windowWidth,
        bool expectSingleFlowRow,
        int minimumFlowBlocks,
        out string detail)
    {
        detail = "";

        var window = new AnalysisWindow(
            code,
            0,
            0,
            Core.Models.ShortCodeFormat.Full,
            Core.Physics.CollapseMode.IncludeCycles)
        {
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = windowWidth,
            Height = 2600,
            Opacity = 0
        };

        try
        {
            window.Show();

            window.UpdateLayout();

            window.UpdateLayout();

            if (window.ViewModel?.HasWorkOrderCards != true ||
                window.WorkOrderCardsRoot.Items.Count != 3)
            {
                return false;
            }

            WorkOrderCardPanel[] panels = FindVisualChildren<WorkOrderCardPanel>(window)
                .ToArray();

            int realizedCards = panels.Sum(panel => panel.Children.Count);

            if (panels.Length != 3 || realizedCards != expectedCards)
            {
                return false;
            }

            bool oneCardPerRow = true;

            bool cardsInsidePanel = true;

            foreach (WorkOrderCardPanel panel in panels)
            {
                FrameworkElement[] cards = panel.Children
                    .OfType<FrameworkElement>()
                    .ToArray();

                for (int index = 0; index < cards.Length; index++)
                {
                    if (cards[index].ActualWidth > panel.ActualWidth + 0.5 ||
                        cards[index].ActualWidth <= 0)
                    {
                        cardsInsidePanel = false;
                    }

                    if (index > 0)
                    {
                        Point current = cards[index].TranslatePoint(
                            new Point(0, 0),
                            panel);

                        Point previous = cards[index - 1].TranslatePoint(
                            new Point(0, 0),
                            panel);

                        // 一行一张：后一张卡必须在前一张卡下方，左边缘对齐。
                        if (current.Y <= previous.Y + 0.5 ||
                            Math.Abs(current.X) > 0.5)
                        {
                            oneCardPerRow = false;
                        }
                    }
                }
            }

            // 半边层选区块最多的一张卡：验收图形里是半边1（输入 2 + 前态 + 步骤 3 + 输出）。
            WorkOrderCardPanel halfPanel = panels[1];

            FrameworkElement? halfCard = null;

            CardFlowPanel? flow = null;

            int blockCount = -1;

            foreach (FrameworkElement candidateCard in halfPanel.Children
                         .OfType<FrameworkElement>())
            {
                CardFlowPanel? candidateFlow =
                    FindVisualChildren<CardFlowPanel>(candidateCard)
                        .FirstOrDefault();

                if (candidateFlow is null)
                {
                    continue;
                }

                int candidateCount = candidateFlow.Children
                    .OfType<FrameworkElement>()
                    .Count(block => block.Visibility == Visibility.Visible);

                if (candidateCount > blockCount)
                {
                    blockCount = candidateCount;

                    halfCard = candidateCard;

                    flow = candidateFlow;
                }
            }

            if (halfCard is null || flow is null)
            {
                return false;
            }

            FrameworkElement[] blocks = flow.Children
                .OfType<FrameworkElement>()
                .Where(block => block.Visibility == Visibility.Visible)
                .ToArray();

            bool orderReady = blocks.Length >= minimumFlowBlocks;

            bool insideCard = flow.ActualWidth <= halfCard.ActualWidth + 0.5;

            int rows = 0;

            double rowY = double.NaN;

            double previousRight = double.NaN;

            foreach (FrameworkElement block in blocks)
            {
                Point position = block.TranslatePoint(new Point(0, 0), flow);

                double right = position.X + block.ActualWidth;

                if (double.IsNaN(rowY) ||
                    Math.Abs(position.Y - rowY) > 0.5)
                {
                    // 换行后必须继续向下，不能回到上面的行。
                    if (!double.IsNaN(rowY) &&
                        position.Y <= rowY + 0.5)
                    {
                        orderReady = false;
                    }

                    rows++;

                    rowY = position.Y;

                    previousRight = double.NaN;
                }
                else if (position.X <= previousRight + 0.5)
                {
                    // 同一行内必须严格从左到右。
                    orderReady = false;
                }

                previousRight = right;

                if (right > flow.ActualWidth + 0.5)
                {
                    orderReady = false;
                }
            }

            bool rowModeReady = expectSingleFlowRow
                ? rows == 1
                : rows >= 2;

            detail =
                $"层宽 {halfPanel.ActualWidth:0} / 卡宽 {halfCard.ActualWidth:0}" +
                $"（每行 1 张；卡内 {rows} 行 / {blocks.Length} 块；" +
                $"单列={oneCardPerRow} 卡在内={cardsInsidePanel} " +
                $"顺序={orderReady} 未溢出={insideCard} 行数={rowModeReady}）";

            return oneCardPerRow &&
                   cardsInsidePanel &&
                   orderReady &&
                   insideCard &&
                   rowModeReady;
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>在可视树里查找指定类型的子元素。</summary>
    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
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
    /// 工作区新建/删除的自检：确认删除前有确认、删除当前工作区后自动切换、
    /// 删掉最后一个时会补一个空工作区，并且全局素材库不受影响。
    /// 确认回调被替换成返回值，避免离屏运行时弹出 MessageBox。
    /// </summary>
    private static void CheckWorkspaceProjects(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            string storePath = Path.Combine(
                Path.GetTempPath(),
                $"shape-workspace-projects-{Guid.NewGuid():N}.json");

            var store = new WorkspaceStore(storePath);

            var viewModel = new WorkspaceViewModel(store);

            bool initialOk = store.Projects.Count == 1 && store.Active is not null;

            string firstId = store.Active?.Id ?? "";

            // 素材是全局共享的，删除工作区不应影响它
            store.AddMaterial(
                "CuCu",
                Core.Models.ShortCodeFormat.Full,
                1,
                4,
                "selftest");

            WorkspaceNode firstNode = store.CreateNodeAtPoint(80, 60);

            viewModel.NewProjectCommand.Execute(null);

            string secondId = store.Active?.Id ?? "";

            WorkspaceNode secondNode = store.CreateNodeAtPoint(200, 60);

            int createdCount = store.Projects.Count;

            bool created =
                store.Projects.Count == 2 &&
                secondId.Length > 0 &&
                secondId != firstId &&
                viewModel.Tabs.Count == 2 &&
                viewModel.Tabs.Count(tab => tab.IsActive) == 1 &&
                store.Active?.Id == secondId;

            // 取消确认：工作区数量与当前工作区都不变
            int confirmCount = 0;

            viewModel.ConfirmDeleteProject = _ =>
            {
                confirmCount++;

                return false;
            };

            viewModel.DeleteProject(viewModel.Tabs.First(tab => tab.Id == secondId));

            bool cancelOk =
                confirmCount == 1 &&
                store.Projects.Count == 2 &&
                store.Active?.Id == secondId;

            // 确认删除当前工作区：连同节点一起删掉，自动切回剩下的第一个
            viewModel.ConfirmDeleteProject = _ => true;

            viewModel.DeleteProject(viewModel.Tabs.First(tab => tab.Id == secondId));

            bool removed =
                store.Projects.Count == 1 &&
                store.Active?.Id == firstId &&
                store.FindNode(secondNode.Id) is null &&
                store.FindNode(firstNode.Id) is not null &&
                store.Materials.Count == 1 &&
                viewModel.Tabs.Count == 1 &&
                viewModel.Tabs[0].Id == firstId;

            // 删除最后一个工作区：自动补一个空工作区，而不是留下 0 个
            viewModel.DeleteProject(viewModel.Tabs[0]);

            bool recreated =
                store.Projects.Count == 1 &&
                store.Active is not null &&
                store.Active.Id != firstId &&
                store.Nodes.Count == 0 &&
                store.Materials.Count == 1 &&
                viewModel.Tabs.Count == 1 &&
                viewModel.Tabs[0].IsActive;

            lines.Add($"初始工作区：{(initialOk ? "成功" : "失败")}");
            lines.Add($"新建工作区：{(created ? "成功" : "失败")}（当前 {createdCount} 个）");
            lines.Add($"取消删除：{(cancelOk ? "成功" : "失败")}");
            lines.Add($"删除工作区：{(removed ? "成功（自动切换到剩余工作区）" : "失败")}");
            lines.Add($"删除最后一个工作区：{(recreated ? "成功（自动新建空工作区）" : "失败")}");

            ok = initialOk && created && cancelOk && removed && recreated;
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

    /// <summary>
    /// 编辑器镜像自检：左右镜像 Q1↔Q2、上下镜像层序反转、
    /// 短代码同步、撤销回到镜像前。
    /// </summary>
    private static void CheckEditorTransforms(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            var viewModel = new EditorViewModel();

            viewModel.Code = "CrP---Gu:ckckckck";

            viewModel.ParseCode();

            if (viewModel.HasError)
            {
                throw new InvalidOperationException(
                    "示例短代码解析失败：" + viewModel.ErrorMessage);
            }

            var exporter = new ShortCodeExporter();

            ShapeDocument before = viewModel.Document.Clone();

            viewModel.MirrorHorizontal();

            ShapeDocument horizontal = viewModel.Document;

            bool horizontalCells =
                before.LayerCount == horizontal.LayerCount &&
                before.PartCount == horizontal.PartCount;

            if (horizontalCells)
            {
                for (int layer = 0;
                     layer < before.LayerCount && horizontalCells;
                     layer++)
                {
                    for (int part = 0;
                         part < before.PartCount && horizontalCells;
                         part++)
                    {
                        horizontalCells = ShapeOps.SameCell(
                            before[layer, part],
                            horizontal[layer, part ^ 1]);
                    }
                }
            }

            bool horizontalCode =
                viewModel.Code == exporter.Export(
                    horizontal,
                    ShortCodeFormat.Full,
                    omitTopEmptyLayers: true);

            lines.Add($"左右镜像单元格互换：{(horizontalCells ? "成功" : "失败")}");

            lines.Add($"左右镜像短代码同步：{(horizontalCode ? "成功" : "失败")}");

            viewModel.Undo();

            bool horizontalUndo = ShapeOps.Same(before, viewModel.Document);

            lines.Add($"左右镜像撤销：{(horizontalUndo ? "成功" : "失败")}");

            // 上下镜像：重新解析原始图形，排除上一步撤销留下的状态
            viewModel.Code = "CrP---Gu:ckckckck";

            viewModel.ParseCode();

            before = viewModel.Document.Clone();

            viewModel.MirrorVertical();

            ShapeDocument vertical = viewModel.Document;

            bool verticalCells =
                before.LayerCount == vertical.LayerCount &&
                before.PartCount == vertical.PartCount;

            if (verticalCells)
            {
                for (int layer = 0;
                     layer < before.LayerCount && verticalCells;
                     layer++)
                {
                    int mirroredLayer = before.LayerCount - 1 - layer;

                    for (int part = 0;
                         part < before.PartCount && verticalCells;
                         part++)
                    {
                        verticalCells = ShapeOps.SameCell(
                            before[layer, part],
                            vertical[mirroredLayer, part]);
                    }
                }
            }

            bool verticalCode =
                viewModel.Code == exporter.Export(
                    vertical,
                    ShortCodeFormat.Full,
                    omitTopEmptyLayers: true);

            lines.Add($"上下镜像层序反转：{(verticalCells ? "成功" : "失败")}");

            lines.Add($"上下镜像短代码同步：{(verticalCode ? "成功" : "失败")}");

            viewModel.Undo();

            bool verticalUndo = ShapeOps.Same(before, viewModel.Document);

            lines.Add($"上下镜像撤销：{(verticalUndo ? "成功" : "失败")}");

            ok =
                horizontalCells &&
                horizontalCode &&
                horizontalUndo &&
                verticalCells &&
                verticalCode &&
                verticalUndo;
        }
        catch (Exception exception)
        {
            lines.Add(exception.ToString());
        }

        lines.Insert(0, ok ? "PASS" : "FAIL");

        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// 工作区旋转自检：生成「旋转」节点（顺时针 90°）、连线标签、
    /// 排在源节点下方、短代码正确，并且撤销能删掉这个新节点。
    /// </summary>
    private static void CheckWorkspaceRotate(
        string path)
    {
        var lines = new List<string>();

        bool ok = false;

        try
        {
            string storePath = Path.Combine(
                Path.GetTempPath(),
                $"shape-workspace-rotate-{Guid.NewGuid():N}.json");

            var store = new WorkspaceStore(storePath);

            var viewModel = new WorkspaceViewModel(store);

            WorkspaceNode source = store.CreateNodeAtPoint(80, 60);

            store.ReplaceNodeShape(
                source.Id,
                "CrP---Gu:ckckckck",
                ShortCodeFormat.Full,
                4,
                4);

            store.SelectNode(source);

            WorkspaceNodeViewModel? sourceView = viewModel.Nodes
                .FirstOrDefault(node => node.Id == source.Id);

            if (sourceView is null)
            {
                throw new InvalidOperationException("旋转源节点没有出现在视图集合中。");
            }

            ShapeDocument? sourceDocument = store.GetEffectiveDocument(source);

            if (sourceDocument is null)
            {
                throw new InvalidOperationException("旋转源节点没有图形。");
            }

            ShapeDocument expected = ShapeOps.Rotate(sourceDocument, 1);

            Rect sourceBounds = WorkspaceLayout.BoundsFor(
                source,
                WorkspaceLayout.SizeFor(source, sourceDocument, 4));

            double expectedX = Math.Round(sourceBounds.Left / 10) * 10;

            double expectedY = Math.Round((sourceBounds.Bottom + 56) / 10) * 10;

            viewModel.RotateCommand.Execute(sourceView);

            WorkspaceNode? created = store.Nodes.FirstOrDefault(
                node => node.Id != source.Id && node.Label == "旋转");

            ShapeDocument? rotated = created is null
                ? null
                : store.GetEffectiveDocument(created);

            bool titleOk = created is not null && created.Label == "旋转";

            bool shapeOk =
                rotated is not null &&
                ShapeOps.Same(expected, rotated);

            bool codeOk =
                created is not null &&
                created.Code == new ShortCodeExporter().Export(
                    expected,
                    ShortCodeFormat.Full,
                    omitTopEmptyLayers: true);

            bool edgeOk =
                created is not null &&
                store.Edges.Any(edge =>
                    edge.FromId == source.Id &&
                    edge.ToId == created.Id &&
                    edge.Label == "旋转");

            bool positionOk =
                created is not null &&
                Math.Abs((created.X ?? -1) - expectedX) < 0.1 &&
                Math.Abs((created.Y ?? -1) - expectedY) < 0.1;

            bool canUndo = viewModel.CanUndo;

            viewModel.Undo();

            bool undoOk =
                created is not null &&
                store.FindNode(created.Id) is null &&
                store.FindNode(source.Id) is not null &&
                !viewModel.CanUndo;

            lines.Add($"旋转节点标题：{created?.Label ?? "缺失"}（{(titleOk ? "成功" : "失败")}）");

            lines.Add($"顺时针 90°：{(shapeOk ? "成功" : "失败")}");

            lines.Add($"短代码：{(codeOk ? "成功" : "失败")}");

            lines.Add($"连线「旋转」：{(edgeOk ? "成功" : "失败")}");

            lines.Add($"排在源节点下方：{(positionOk ? "成功" : "失败")}（期望 X={expectedX:0.##}，Y={expectedY:0.##}）");

            lines.Add($"撤销：{(undoOk ? "成功" : "失败")}（撤销前可撤销：{(canUndo ? "是" : "否")}）");

            ok =
                titleOk &&
                shapeOk &&
                codeOk &&
                edgeOk &&
                positionOk &&
                canUndo &&
                undoOk;
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

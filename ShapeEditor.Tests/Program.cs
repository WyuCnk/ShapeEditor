using System.Diagnostics;

namespace ShapeEditor.Tests;

/// <summary>
/// 无第三方依赖的测试入口。
///
/// 运行：
///   dotnet run --project ShapeEditor.Tests -c Release
///
/// 全部通过返回 0，有失败返回 1，可直接用于 CI。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        /*
         * WPF 的画刷等对象需要 STA 线程，
         * 测试统一在 STA 上跑。
         */
        int exitCode = 1;

        var thread = new Thread(() => exitCode = RunAll())
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();

        thread.Join();

        return exitCode;
    }

    private static int RunAll()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("主题:调色板与 Web 色值一致", ThemeTests.PaletteMatchesWebHexValues),
            ("主题:未知颜色回退白色", ThemeTests.UnknownColorFallsBackToWhite),
            ("主题:晶体七段", ThemeTests.CrystalBandsHaveSevenSteps),
            ("主题:晶体条带对称", ThemeTests.CrystalBandsAreSymmetric),
            ("主题:晶体高光更亮", ThemeTests.CrystalHighlightIsBrighterThanBase),
            ("主题:黑色晶体高光加强", ThemeTests.BlackCrystalUsesStrongerHighlight),
            ("主题:适应屏幕缩放有上下限", ThemeTests.FitScaleIsClamped),
            ("主题:Web 主题数据来自 Core", ThemeTests.WebThemeDataMirrorsCore),

            ("短代码:完整格式往返", ShortCodeTests.FullCodeRoundTrip),
            ("短代码:超短格式往返", ShortCodeTests.SuperCodeRoundTrip),
            ("短代码:尺寸推断", ShortCodeTests.InferDimensionsWorks),
            ("短代码:非法代码报错", ShortCodeTests.InvalidCodeFails),
            ("短代码:省略顶层空层", ShortCodeTests.EmptyLayersAreOmitted),

            ("存档:往返一致", SaveFileTests.RoundTripKeepsDocument),
            ("存档:拒绝未知版本", SaveFileTests.RejectsUnknownVersion),
            ("存档:拒绝非法尺寸", SaveFileTests.RejectsInvalidDimensions),
            ("存档:拒绝坏 JSON", SaveFileTests.RejectsBrokenJson),
            ("存档:尺寸上限", SaveFileTests.DimensionLimitMatchesParsers),

            ("编辑器:形状画笔", EditorViewModelTests.PaintWithShapeBrush),
            ("编辑器:形状+颜色", EditorViewModelTests.CombinedBrushAppliesColor),
            ("编辑器:撤销", EditorViewModelTests.UndoRestoresPreviousState),
            ("编辑器:调整尺寸", EditorViewModelTests.ApplyDimensionsResizes),
            ("编辑器:拒绝超限尺寸", EditorViewModelTests.RejectsOversizedDimensions),
            ("编辑器:解析短代码", EditorViewModelTests.ParseCodeBuildsDocument),
            ("编辑器:解析超短代码", EditorViewModelTests.ParseSuperCode),
            ("编辑器:非法代码提示", EditorViewModelTests.BadCodeShowsError),
            ("编辑器:上移一层", EditorViewModelTests.MoveUpShiftsLayers),
            ("编辑器:清空", EditorViewModelTests.ClearDocumentEmptiesGrid),
            ("编辑器:切换模式清画笔", EditorViewModelTests.ModeSwitchClearsBrushes),

            ("批量:解析并跳过空行", BatchViewModelTests.ParsesLinesAndSkipsBlank),
            ("批量:统计失败条目", BatchViewModelTests.CountsFailedEntries),
            ("批量:上一条下一条", BatchViewModelTests.NavigatesBetweenEntries),
            ("批量:分页", BatchViewModelTests.PagingCapsPageCount),
            ("批量:选中项映射", BatchViewModelTests.SelectedOffsetMapsToGlobalIndex),
            ("批量:清空", BatchViewModelTests.ClearResetsEverything),
            ("批量:空输入报错", BatchViewModelTests.EmptyInputShowsError),

            ("物理:悬空单格落底", PhysicsTests.FloatingCellFallsToFloor),
            ("物理:受支撑塔不动", PhysicsTests.SupportedColumnStays),
            ("物理:悬空塔整体下落", PhysicsTests.FloatingTowerFallsTogether),
            ("物理:下落消晶", PhysicsTests.FallingCrystalDisappears),
            ("物理:坍塌幂等", PhysicsTests.CollapseIsIdempotent),
            ("物理:循环支撑悬空", PhysicsTests.CycleSupportKeepsFloatingShape),
            ("物理:堆叠拒绝不同象限数", PhysicsTests.StackRejectsDifferentPartCount),
            ("物理:堆叠清晶并使用上限", PhysicsTests.StackClearsTopCrystalsAndUsesLimit),
            ("物理:堆叠超限截断", PhysicsTests.StackTruncatesAboveLimit),
            ("物理:顶针加底", PhysicsTests.PinAddsPinsAtBottom),
            ("物理:顶针截顶层", PhysicsTests.PinDropsTopLayerOverLimit),
            ("物理:切割返回两半", PhysicsTests.CutReturnsBothHalves),
            ("物理:切割保持象限数", PhysicsTests.CutKeepsPartCount),
            ("物理:切割破碎跨线晶体", PhysicsTests.CutBreaksSpanningCrystal),
            ("物理:奇数象限被拒", PhysicsTests.OddPartCountIsRejected),
            ("物理:象限数取偶", PhysicsTests.NormalizePartCountRoundsUpToEven)
        };

        int failed = 0;

        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach ((string name, Action run) in tests)
        {
            try
            {
                run();

                Console.WriteLine($"  PASS  {name}");
            }
            catch (Exception exception)
            {
                failed++;

                Console.WriteLine($"  FAIL  {name}");
                Console.WriteLine($"        {exception.Message}");
            }
        }

        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine(
            $"{tests.Length - failed}/{tests.Length} 通过，用时 {stopwatch.ElapsedMilliseconds} ms");

        return failed == 0 ? 0 : 1;
    }
}

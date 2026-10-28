using System.IO;
using ShapeEditor.Core.Models;
using ShapeEditor.Wpf.Services;
using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Tests;

/// <summary>
/// 编辑器视图模型：画笔、尺寸、撤销、短代码。
/// </summary>
internal static class EditorViewModelTests
{
    public static void PaintWithShapeBrush()
    {
        EditorViewModel viewModel = Create();

        viewModel.ShapeOptions.Single(o => o.Type == CellType.Shape).IsActive = true;

        viewModel.PaintCell(0, 0);

        Assert.Equal(CellType.Shape, viewModel.Document[0, 0].Type);
    }

    public static void CombinedBrushAppliesColor()
    {
        EditorViewModel viewModel = Create();

        viewModel.ShapeOptions.Single(o => o.Type == CellType.Shape).IsActive = true;

        viewModel.ColorOptions.Single(o => o.Code == 'r').IsActive = true;

        viewModel.PaintCell(0, 1);

        Assert.Equal(CellType.Shape, viewModel.Document[0, 1].Type);
        Assert.Equal('r', viewModel.Document[0, 1].ColorCode);
    }

    public static void UndoRestoresPreviousState()
    {
        EditorViewModel viewModel = Create();

        viewModel.ShapeOptions.Single(o => o.Type == CellType.Shape).IsActive = true;

        viewModel.PaintCell(0, 0);

        Assert.True(viewModel.CanUndo);

        viewModel.EndStroke();

        viewModel.Undo();

        Assert.Equal(CellType.Empty, viewModel.Document[0, 0].Type);
        Assert.True(viewModel.CanRedo);
    }

    public static void ApplyDimensionsResizes()
    {
        EditorViewModel viewModel = Create();

        viewModel.LayerCount = 6;
        viewModel.PartCount = 5;

        viewModel.ApplyDimensions();

        Assert.Equal(6, viewModel.Document.LayerCount);
        Assert.Equal(5, viewModel.Document.PartCount);
        Assert.Equal("", viewModel.ErrorMessage);
    }

    public static void RejectsOversizedDimensions()
    {
        EditorViewModel viewModel = Create();

        viewModel.LayerCount = 101;
        viewModel.PartCount = 4;

        viewModel.ApplyDimensions();

        Assert.Equal(4, viewModel.Document.LayerCount);
        Assert.True(viewModel.HasError, "超过 100 层应当被拒绝");
    }

    public static void ParseCodeBuildsDocument()
    {
        EditorViewModel viewModel = Create();

        viewModel.Code = "CrP---Gu:ckckckck";

        viewModel.ParseCode();

        Assert.Equal("", viewModel.ErrorMessage);
        Assert.Equal(4, viewModel.Document.LayerCount);
        Assert.Equal(4, viewModel.Document.PartCount);
        Assert.Equal(CellType.Shape, viewModel.Document[0, 0].Type);
        Assert.Equal(CellType.Pin, viewModel.Document[0, 1].Type);
        Assert.Equal(CellType.Crystal, viewModel.Document[1, 0].Type);
    }

    public static void ParseSuperCode()
    {
        EditorViewModel viewModel = Create();

        viewModel.IsSuperFormat = true;
        viewModel.Code = "CcCc";

        viewModel.ParseCode();

        Assert.Equal("", viewModel.ErrorMessage);
        Assert.Equal(CellType.Shape, viewModel.Document[0, 0].Type);
        Assert.Equal(CellType.Crystal, viewModel.Document[0, 1].Type);
    }

    public static void BadCodeShowsError()
    {
        EditorViewModel viewModel = Create();

        // 层代码长度必须是偶数，这里故意给 3 个字符
        viewModel.Code = "CrP";

        viewModel.ParseCode();

        Assert.True(viewModel.HasError, "非法代码应当给出错误提示");
    }

    public static void MoveUpShiftsLayers()
    {
        EditorViewModel viewModel = Create();

        viewModel.Document[0, 0] = ShapeCell.Shape('C', 'r');

        viewModel.MoveUp();

        Assert.Equal(CellType.Empty, viewModel.Document[0, 0].Type);
        Assert.Equal(CellType.Shape, viewModel.Document[1, 0].Type);
    }

    public static void ClearDocumentEmptiesGrid()
    {
        EditorViewModel viewModel = Create();

        viewModel.ShapeOptions.Single(o => o.Type == CellType.Shape).IsActive = true;

        viewModel.PaintCell(0, 0);

        viewModel.ClearDocument();

        Assert.Equal(CellType.Empty, viewModel.Document[0, 0].Type);
        Assert.Equal("", viewModel.Code);
    }

    public static void ModeSwitchClearsBrushes()
    {
        EditorViewModel viewModel = Create();

        viewModel.ShapeOptions.Single(o => o.Type == CellType.Shape).IsActive = true;

        Assert.True(viewModel.IsPaintBrushActive);

        viewModel.IsMoveMode = true;

        Assert.False(viewModel.IsPaintBrushActive);
        Assert.True(viewModel.IsMoveMode);
    }

    private static EditorViewModel Create()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "shape-editor-tests",
            Guid.NewGuid().ToString("N"));

        return new EditorViewModel(new AutoSaveService(directory));
    }
}

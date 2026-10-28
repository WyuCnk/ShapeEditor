using ShapeEditor.Wpf.ViewModels;

namespace ShapeEditor.Tests;

/// <summary>
/// 批量查看：解析、分页、导航。
/// </summary>
internal static class BatchViewModelTests
{
    public static void ParsesLinesAndSkipsBlank()
    {
        BatchViewModel viewModel = Create();

        viewModel.RawText =
            "CrP---Gu:ckckckck" + Environment.NewLine +
            "" + Environment.NewLine +
            "CcCc";

        viewModel.ParseEntries();

        Assert.Equal(2, viewModel.EntryCount);
        Assert.True(viewModel.HasEntries);
        Assert.True(viewModel.StatusMessage.Contains("2 条"));
    }

    public static void CountsFailedEntries()
    {
        BatchViewModel viewModel = Create();

        viewModel.RawText =
            "CrP---Gu:ckckckck" + Environment.NewLine +
            "!!invalid!!";

        viewModel.ParseEntries();

        Assert.Equal(2, viewModel.EntryCount);
        Assert.True(
            viewModel.StatusMessage.Contains("1 条解析失败"),
            viewModel.StatusMessage);
    }

    public static void NavigatesBetweenEntries()
    {
        BatchViewModel viewModel = Create();

        viewModel.RawText =
            "CcCc" + Environment.NewLine +
            "P-P-" + Environment.NewLine +
            "CuCu";

        viewModel.ParseEntries();

        Assert.Equal(0, viewModel.CurrentIndex);
        Assert.False(viewModel.CanPrevious);
        Assert.True(viewModel.CanNext);

        viewModel.ShowNext();

        Assert.Equal(1, viewModel.CurrentIndex);
        Assert.True(viewModel.CanPrevious);

        viewModel.ShowPrevious();

        Assert.Equal(0, viewModel.CurrentIndex);
    }

    public static void PagingCapsPageCount()
    {
        BatchViewModel viewModel = Create();

        var lines = new List<string>();

        for (int index = 0; index < 30; index++)
        {
            lines.Add("CcCc");
        }

        viewModel.RawText = string.Join(Environment.NewLine, lines);

        viewModel.ParseEntries();

        Assert.Equal(30, viewModel.EntryCount);
        Assert.Equal(3, viewModel.PageCount);
        Assert.Equal(12, viewModel.PageItems.Count);

        viewModel.ShowNextPage();

        Assert.Equal(1, viewModel.PageIndex);
        Assert.Equal(12, viewModel.PageItems.Count);
        Assert.Equal(12, viewModel.CurrentIndex);
    }

    public static void SelectedOffsetMapsToGlobalIndex()
    {
        BatchViewModel viewModel = Create();

        var lines = new List<string>();

        for (int index = 0; index < 20; index++)
        {
            lines.Add("CcCc");
        }

        viewModel.RawText = string.Join(Environment.NewLine, lines);

        viewModel.ParseEntries();

        viewModel.SelectedPageOffset = 3;

        Assert.Equal(3, viewModel.CurrentIndex);
    }

    public static void ClearResetsEverything()
    {
        BatchViewModel viewModel = Create();

        viewModel.RawText = "CcCc";

        viewModel.ParseEntries();

        viewModel.ClearAll();

        Assert.Equal(0, viewModel.EntryCount);
        Assert.False(viewModel.HasEntries);
        Assert.Equal("", viewModel.RawText);
    }

    public static void EmptyInputShowsError()
    {
        BatchViewModel viewModel = Create();

        viewModel.RawText = "   ";

        viewModel.ParseEntries();

        Assert.True(viewModel.HasError);
    }

    private static BatchViewModel Create()
    {
        return new BatchViewModel();
    }
}

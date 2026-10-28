using System.Collections.ObjectModel;
using System.Windows.Input;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Wpf.Services;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 批量查看页面的状态与命令。
///
/// 与 Web 端策略一致：全量只保留短代码与解析结论，
/// 当前页的条目才真正解析成文档，避免一次导入上万条时占满内存。
/// </summary>
public sealed class BatchViewModel : ObservableObject
{
    public const int PageSize = 12;

    public const int MaxEntryCount = 10_000;

    private readonly ShortCodeParser _parser = new();

    private readonly DialogService _dialogs;

    private readonly List<BatchEntry> _entries = new();

    private string _rawText = "";

    private string _statusMessage = "";

    private string _errorMessage = "";

    private ShortCodeFormat _format = ShortCodeFormat.Full;

    private int _currentIndex;

    private int _pageIndex;

    private int _pageStart;

    public BatchViewModel(
        DialogService? dialogs = null)
    {
        _dialogs = dialogs ?? new DialogService();

        PageItems = new ObservableCollection<BatchItemViewModel>();

        ParseCommand = new RelayCommand(ParseEntries);
        ClearCommand = new RelayCommand(ClearAll);
        ImportTextCommand = new RelayCommand(ImportTextFile);
        PreviousCommand = new RelayCommand(ShowPrevious);
        NextCommand = new RelayCommand(ShowNext);
        PreviousPageCommand = new RelayCommand(ShowPreviousPage);
        NextPageCommand = new RelayCommand(ShowNextPage);
    }

    public ObservableCollection<BatchItemViewModel> PageItems { get; }

    public ICommand ParseCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand ImportTextCommand { get; }

    public ICommand PreviousCommand { get; }

    public ICommand NextCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public string RawText
    {
        get => _rawText;
        set => SetProperty(ref _rawText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ShortCodeFormat Format
    {
        get => _format;
        set
        {
            if (!SetProperty(ref _format, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsFullFormat));
            OnPropertyChanged(nameof(IsSuperFormat));
            OnPropertyChanged(nameof(BatchHint));
            OnPropertyChanged(nameof(InputPlaceholder));

            /*
             * 已经载入内容时按新格式重新解析，
             * 避免列表停留在另一种格式的结果上。
             */
            if (_entries.Count > 0)
            {
                ParseEntries();
            }
        }
    }

    public bool IsFullFormat
    {
        get => Format == ShortCodeFormat.Full;
        set
        {
            if (value)
            {
                Format = ShortCodeFormat.Full;
            }
        }
    }

    public bool IsSuperFormat
    {
        get => Format == ShortCodeFormat.Super;
        set
        {
            if (value)
            {
                Format = ShortCodeFormat.Super;
            }
        }
    }

    public string BatchHint =>
        Format == ShortCodeFormat.Super
            ? $"一行一个超短代码（只有形状），空行会忽略；一次最多 {MaxEntryCount} 行。"
            : $"一行一个短代码，空行会忽略；一次最多 {MaxEntryCount} 行。";

    public string InputPlaceholder =>
        Format == ShortCodeFormat.Super
            ? "在这里粘贴超短代码，每行一条，例如：\nCcCc\nP---\n-CC-"
            : "在这里粘贴短代码，每行一条，例如：\nCrP---Gu:ckckckck\nCuCuCuCu";

    public bool HasEntries => _entries.Count > 0;

    public int EntryCount => _entries.Count;

    public int PageCount =>
        Math.Max(
            1,
            (_entries.Count + PageSize - 1) / PageSize);

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageText));
                OnPropertyChanged(nameof(CanPreviousPage));
                OnPropertyChanged(nameof(CanNextPage));
            }
        }
    }

    public string PageText => $"{PageIndex + 1} / {PageCount}";

    public int CurrentIndex
    {
        get => _currentIndex;
        private set
        {
            if (SetProperty(ref _currentIndex, value))
            {
                OnPropertyChanged(nameof(CurrentItem));
                OnPropertyChanged(nameof(IndexText));
                OnPropertyChanged(nameof(CanPrevious));
                OnPropertyChanged(nameof(CanNext));
            }
        }
    }

    public string IndexText =>
        _entries.Count == 0
            ? ""
            : $"第 {CurrentIndex + 1} / {_entries.Count} 条";

    public BatchItemViewModel? CurrentItem
    {
        get
        {
            if (_entries.Count == 0)
            {
                return null;
            }

            int offset = CurrentIndex - _pageStart;

            if (offset < 0 || offset >= PageItems.Count)
            {
                return null;
            }

            return PageItems[offset];
        }
    }

    public bool CanPrevious => CurrentIndex > 0;

    public bool CanNext => CurrentIndex < _entries.Count - 1;

    public bool CanPreviousPage => PageIndex > 0;

    public bool CanNextPage => PageIndex < PageCount - 1;

    /// <summary>
    /// 当前页内的选中序号，直接绑定给缩略图列表。
    /// </summary>
    public int SelectedPageOffset
    {
        get
        {
            if (PageItems.Count == 0)
            {
                return -1;
            }

            return Math.Clamp(
                CurrentIndex - _pageStart,
                0,
                PageItems.Count - 1);
        }
        set
        {
            if (value < 0 ||
                value >= PageItems.Count)
            {
                return;
            }

            SetCurrent(_pageStart + value);
        }
    }

    #region 导入与解析

    public void ImportTextFile()
    {
        ErrorMessage = "";
        StatusMessage = "";

        string? path = _dialogs.OpenTextFile();

        if (path is null)
        {
            return;
        }

        try
        {
            RawText = DialogService.ReadTextFile(path);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"读取文件失败：{exception.Message}";

            return;
        }

        ParseEntries();
    }

    public void ParseEntries()
    {
        ErrorMessage = "";
        StatusMessage = "";

        if (string.IsNullOrWhiteSpace(RawText))
        {
            ErrorMessage = "请先粘贴短代码，或者导入一个 txt 文件。";

            return;
        }

        string[] lines = RawText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');

        _entries.Clear();
        PageItems.Clear();

        int failedCount = 0;

        bool truncated = false;

        foreach (string rawLine in lines)
        {
            string code = rawLine.Trim();

            if (code.Length == 0)
            {
                continue;
            }

            if (_entries.Count >= MaxEntryCount)
            {
                truncated = true;

                break;
            }

            BatchEntry entry = CreateEntry(code);

            if (entry.Error is not null)
            {
                failedCount++;
            }

            _entries.Add(entry);
        }

        if (_entries.Count == 0)
        {
            ErrorMessage = "没有找到包含短代码的行。";

            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(EntryCount));
            OnPropertyChanged(nameof(PageCount));

            return;
        }

        StatusMessage = failedCount == 0
            ? $"已加载 {_entries.Count} 条短代码。"
            : $"已加载 {_entries.Count} 条短代码，其中 {failedCount} 条解析失败。";

        if (truncated)
        {
            ErrorMessage =
                $"一次最多处理 {MaxEntryCount} 条，后面的行已忽略。";
        }

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(PageCount));

        _pageIndex = 0;

        OnPropertyChanged(nameof(PageIndex));

        BuildPage(0);
    }

    public void ClearAll()
    {
        RawText = "";
        StatusMessage = "";
        ErrorMessage = "";

        _entries.Clear();
        PageItems.Clear();

        _currentIndex = 0;
        _pageIndex = 0;
        _pageStart = 0;

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(PageIndex));
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(IndexText));
    }

    private BatchEntry CreateEntry(
        string code)
    {
        if (!ShortCodeParser.TryInferDimensions(
                code,
                Format,
                out int layerCount,
                out int partCount,
                out string? error))
        {
            return new BatchEntry
            {
                Code = code,
                Error = error ?? "短代码无效。"
            };
        }

        ParseResult result = _parser.Parse(
            code,
            layerCount,
            partCount,
            Format);

        if (!result.Success ||
            result.Document is null)
        {
            return new BatchEntry
            {
                Code = code,
                Error = result.Error ?? "短代码解析失败。"
            };
        }

        return new BatchEntry
        {
            Code = code,
            LayerCount = layerCount,
            PartCount = partCount
        };
    }

    #endregion

    #region 分页与条目缓存

    private void BuildPage(
        int pageIndex)
    {
        PageItems.Clear();

        _pageStart = pageIndex * PageSize;

        int end = Math.Min(
            _pageStart + PageSize,
            _entries.Count);

        for (int index = _pageStart;
             index < end;
             index++)
        {
            PageItems.Add(
                CreateView(_entries[index], index));
        }

        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(SelectedPageOffset));
    }

    private BatchItemViewModel CreateView(
        BatchEntry entry,
        int index)
    {
        if (entry.Error is not null)
        {
            return new BatchItemViewModel
            {
                Index = index,
                Code = entry.Code,
                Error = entry.Error
            };
        }

        ParseResult result = _parser.Parse(
            entry.Code,
            entry.LayerCount,
            entry.PartCount,
            Format);

        if (!result.Success ||
            result.Document is null)
        {
            return new BatchItemViewModel
            {
                Index = index,
                Code = entry.Code,
                Error = result.Error ?? "短代码解析失败。"
            };
        }

        return new BatchItemViewModel
        {
            Index = index,
            Code = entry.Code,
            Document = result.Document,
            LayerCount = entry.LayerCount,
            PartCount = entry.PartCount
        };
    }

    #endregion

    #region 导航

    public void ShowPrevious()
    {
        if (!CanPrevious)
        {
            return;
        }

        SetCurrent(CurrentIndex - 1);
    }

    public void ShowNext()
    {
        if (!CanNext)
        {
            return;
        }

        SetCurrent(CurrentIndex + 1);
    }

    public void ShowItem(
        int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        SetCurrent(index);
    }

    private void SetCurrent(
        int index)
    {
        int page = index / PageSize;

        if (page != PageIndex)
        {
            PageIndex = page;

            BuildPage(page);
        }

        CurrentIndex = index;

        /*
         * 同一页内切换时 CurrentItem 的取法不变，
         * 但对象本身会变，需要显式通知。
         */
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(SelectedPageOffset));
    }

    public void ShowPreviousPage()
    {
        if (!CanPreviousPage)
        {
            return;
        }

        GoToPage(PageIndex - 1);
    }

    public void ShowNextPage()
    {
        if (!CanNextPage)
        {
            return;
        }

        GoToPage(PageIndex + 1);
    }

    private void GoToPage(
        int page)
    {
        PageIndex = Math.Clamp(
            page,
            0,
            PageCount - 1);

        BuildPage(PageIndex);

        CurrentIndex = _pageStart;
    }

    public bool IsSelectedThumb(
        int index)
    {
        return index == CurrentIndex;
    }

    #endregion

    private sealed class BatchEntry
    {
        public string Code { get; init; } = "";

        public string? Error { get; init; }

        public int LayerCount { get; init; }

        public int PartCount { get; init; }
    }
}

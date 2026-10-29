using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Theme;
using ShapeEditor.Wpf.Rendering;
using ShapeEditor.Wpf.Services;

namespace ShapeEditor.Wpf.ViewModels;

public enum CanvasMode
{
    Paint,
    Move
}

public enum BrushMode
{
    None,
    Shape,
    Color,
    Combined
}

/// <summary>
/// 编辑器页面的状态与命令。
///
/// 所有短代码逻辑都来自 ShapeEditor.Core，这里只负责组织交互。
/// </summary>
public sealed class EditorViewModel : ObservableObject
{
    /// <summary>
    /// 解析短代码后至少保留 4 层 × 4 象限，与 Web 端一致。
    /// </summary>
    private const int MinimumDimension = 4;

    private const int MaxHistoryCount = 100;

    private readonly ShapeEditorService _editor = new();

    private readonly ShortCodeParser _parser = new();

    private readonly ShortCodeExporter _exporter = new();

    private readonly AutoSaveService _autoSave;

    private readonly DialogService _dialogs;

    private readonly Stack<ShapeDocument> _undoStack = new();

    private readonly Stack<ShapeDocument> _redoStack = new();

    private readonly DispatcherTimer _autoSaveTimer;

    private bool _syncingOptions;

    private bool _strokeStarted;

    private bool _storageErrorShown;

    private ShapeDocument _document;

    private int _layerCount = MinimumDimension;

    private int _partCount = MinimumDimension;

    private string _code = "";

    private string _errorMessage = "";

    private string _statusMessage = "";

    private ShortCodeFormat _format = ShortCodeFormat.Full;

    private CanvasMode _mode = CanvasMode.Paint;

    private bool _shapeBrushActive;

    private bool _colorBrushActive;

    private CellType _brushShape = CellType.Shape;

    private char _brushColor = 'u';

    private int? _selectedLayer;

    private int? _selectedPart;

    private double _zoom = 1;

    private double _panX;

    private double _panY;

    public EditorViewModel(
        AutoSaveService? autoSave = null,
        DialogService? dialogs = null)
    {
        _autoSave = autoSave ?? new AutoSaveService();

        _dialogs = dialogs ?? new DialogService();

        _document = _autoSave.Restore() ?? new ShapeDocument(
            MinimumDimension,
            MinimumDimension);

        _layerCount = _document.LayerCount;
        _partCount = _document.PartCount;

        ShapeOptions = new ObservableCollection<ShapeBrushOption>
        {
            new(CellType.Empty, "空"),
            new(CellType.Pin, "顶"),
            new(CellType.Crystal, "晶"),
            new(CellType.Shape, "图")
        };

        ColorOptions = new ObservableCollection<ColorBrushOption>();

        foreach (char code in ShapeColor.All)
        {
            ColorOptions.Add(new ColorBrushOption(code));
        }

        foreach (ShapeBrushOption option in ShapeOptions)
        {
            option.PropertyChanged += OnShapeOptionChanged;
        }

        foreach (ColorBrushOption option in ColorOptions)
        {
            option.PropertyChanged += OnColorOptionChanged;
        }

        UndoCommand = new RelayCommand(Undo);
        RedoCommand = new RelayCommand(Redo);
        ClearCommand = new RelayCommand(ClearDocument);
        ApplyDimensionsCommand = new RelayCommand(ApplyDimensions);
        FitViewCommand = new RelayCommand(ResetView);
        ParseCommand = new RelayCommand(ParseCode);
        ExportCodeCommand = new RelayCommand(ExportCode);
        CopyCodeCommand = new RelayCommand(CopyCode);
        PasteCodeCommand = new RelayCommand(PasteCode);
        ExportImageCommand = new RelayCommand(ExportImage);
        CopyImageCommand = new RelayCommand(CopyImage);
        ExportSaveFileCommand = new RelayCommand(ExportSaveFile);
        ImportSaveFileCommand = new RelayCommand(ImportSaveFile);
        MoveUpCommand = new RelayCommand(MoveUp);
        MoveDownCommand = new RelayCommand(MoveDown);
        MoveLeftCommand = new RelayCommand(MoveLeft);
        MoveRightCommand = new RelayCommand(MoveRight);
        ClearCellCommand = new RelayCommand(ClearSelectedCell);
        AddToWorkspaceCommand = new RelayCommand(AddToWorkspace);

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };

        _autoSaveTimer.Tick += OnAutoSaveTick;
    }

    /// <summary>
    /// 内容被就地修改，需要重建画布缓存。
    /// </summary>
    public event EventHandler? GridInvalidated;

    public ObservableCollection<ShapeBrushOption> ShapeOptions { get; }

    public ObservableCollection<ColorBrushOption> ColorOptions { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand ApplyDimensionsCommand { get; }

    public ICommand FitViewCommand { get; }

    public ICommand ParseCommand { get; }

    public ICommand ExportCodeCommand { get; }

    public ICommand CopyCodeCommand { get; }

    public ICommand PasteCodeCommand { get; }

    public ICommand ExportImageCommand { get; }

    public ICommand CopyImageCommand { get; }

    public ICommand ExportSaveFileCommand { get; }

    public ICommand ImportSaveFileCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand MoveLeftCommand { get; }

    public ICommand MoveRightCommand { get; }

    public ICommand ClearCellCommand { get; }

    public ICommand AddToWorkspaceCommand { get; }

    /// <summary>
    /// 由 MainViewModel 注入：把当前图形加入工作区素材库。
    /// </summary>
    public Action<ShapeDocument>? AddToWorkspaceHandler { get; set; }

    private void AddToWorkspace()
    {
        AddToWorkspaceHandler?.Invoke(Document);
    }

    public ShapeDocument Document
    {
        get => _document;
        private set
        {
            _document = value;

            OnPropertyChanged();

            _layerCount = value.LayerCount;
            _partCount = value.PartCount;

            OnPropertyChanged(nameof(LayerCount));
            OnPropertyChanged(nameof(PartCount));
            OnPropertyChanged(nameof(SelectedCellText));
            OnPropertyChanged(nameof(DocumentSummary));
        }
    }

    /// <summary>
    /// 输入框里的层数，应用尺寸前不改变文档。
    /// </summary>
    public int LayerCount
    {
        get => _layerCount;
        set => SetProperty(ref _layerCount, value);
    }

    public int PartCount
    {
        get => _partCount;
        set => SetProperty(ref _partCount, value);
    }

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
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

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string DocumentSummary =>
        $"当前图形：{Document.LayerCount} 层 × {Document.PartCount} 象限";

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
            OnPropertyChanged(nameof(CodePlaceholder));
            OnPropertyChanged(nameof(ParseButtonText));
            OnPropertyChanged(nameof(ExportButtonText));

            ErrorMessage = "";
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

    public string CodePlaceholder =>
        Format == ShortCodeFormat.Super
            ? "输入超短代码，例如 CcCc"
            : "输入短代码，例如 CrP---Gu:ckckckck";

    public string ParseButtonText =>
        Format == ShortCodeFormat.Super
            ? "解析超短代码"
            : "解析短代码";

    public string ExportButtonText =>
        Format == ShortCodeFormat.Super
            ? "导出超短代码"
            : "导出短代码";

    public CanvasMode Mode
    {
        get => _mode;
        private set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsPaintMode));
            OnPropertyChanged(nameof(IsMoveMode));
        }
    }

    public bool IsPaintMode
    {
        get => Mode == CanvasMode.Paint;
        set
        {
            if (value)
            {
                SetMode(CanvasMode.Paint);
            }
        }
    }

    public bool IsMoveMode
    {
        get => Mode == CanvasMode.Move;
        set
        {
            if (value)
            {
                SetMode(CanvasMode.Move);
            }
        }
    }

    public bool ShapeBrushActive
    {
        get => _shapeBrushActive;
        private set
        {
            if (SetProperty(ref _shapeBrushActive, value))
            {
                OnPropertyChanged(nameof(IsPaintBrushActive));
            }
        }
    }

    public bool ColorBrushActive
    {
        get => _colorBrushActive;
        private set
        {
            if (SetProperty(ref _colorBrushActive, value))
            {
                OnPropertyChanged(nameof(IsPaintBrushActive));
            }
        }
    }

    /// <summary>
    /// 只要任一画笔激活，画布就进入绘制模式。
    /// </summary>
    public bool IsPaintBrushActive =>
        ShapeBrushActive || ColorBrushActive;

    public CellType BrushShape
    {
        get => _brushShape;
        private set => SetProperty(ref _brushShape, value);
    }

    public char BrushColor
    {
        get => _brushColor;
        private set => SetProperty(ref _brushColor, value);
    }

    public int? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (SetProperty(ref _selectedLayer, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedCellText));
            }
        }
    }

    public int? SelectedPart
    {
        get => _selectedPart;
        set
        {
            if (SetProperty(ref _selectedPart, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedCellText));
            }
        }
    }

    public bool HasSelection =>
        SelectedLayer is not null &&
        SelectedPart is not null;

    public string SelectedCellText
    {
        get
        {
            if (SelectedLayer is not int layer ||
                SelectedPart is not int part)
            {
                return "未选择单元格";
            }

            return $"第 {layer + 1} 层，第 {part + 1} 象限：{Document[layer, part].ToShortCode()}";
        }
    }

    public string BrushStatusText
    {
        get
        {
            BrushMode mode = CurrentBrushMode;

            return mode switch
            {
                BrushMode.Shape => $"画笔模式：{GetShapeName(BrushShape)}",
                BrushMode.Color => $"画笔模式：{ShapePalette.GetShortName(BrushColor)}",
                BrushMode.Combined =>
                    $"画笔模式：{GetShapeName(BrushShape)} + {ShapePalette.GetShortName(BrushColor)}",
                _ => "画笔模式：未选画笔"
            };
        }
    }

    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, value);
    }

    public double PanX
    {
        get => _panX;
        set => SetProperty(ref _panX, value);
    }

    public double PanY
    {
        get => _panY;
        set => SetProperty(ref _panY, value);
    }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    private BrushMode CurrentBrushMode
    {
        get
        {
            if (ShapeBrushActive && ColorBrushActive)
            {
                return BrushMode.Combined;
            }

            if (ShapeBrushActive)
            {
                return BrushMode.Shape;
            }

            if (ColorBrushActive)
            {
                return BrushMode.Color;
            }

            return BrushMode.None;
        }
    }

    #region 画布交互

    /// <summary>
    /// 一次拖动绘制的开始：只记录一次历史。
    /// </summary>
    public void BeginStroke()
    {
        if (_strokeStarted)
        {
            return;
        }

        _strokeStarted = true;

        SaveUndoState();
    }

    public void EndStroke()
    {
        if (!_strokeStarted)
        {
            return;
        }

        _strokeStarted = false;

        OnPropertyChanged(nameof(SelectedCellText));

        ScheduleAutoSave();
    }

    public void PaintCell(
        int layer,
        int part)
    {
        if (CurrentBrushMode == BrushMode.None)
        {
            return;
        }

        EnsureStroke();

        if (!ApplyBrush(layer, part))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCellText));

        GridInvalidated?.Invoke(this, EventArgs.Empty);

        UpdateStatus();
    }

    public void SelectCell(
        int layer,
        int part)
    {
        SelectedLayer = layer;

        SelectedPart = part;
    }

    #endregion

    #region 画笔

    private bool ApplyBrush(
        int layer,
        int part)
    {
        switch (CurrentBrushMode)
        {
            case BrushMode.Shape:
                _editor.SetType(
                    Document,
                    layer,
                    part,
                    BrushShape);

                return true;

            case BrushMode.Color:
                return _editor.ApplyColorBrush(
                    Document,
                    layer,
                    part,
                    BrushColor);

            case BrushMode.Combined:
                _editor.SetType(
                    Document,
                    layer,
                    part,
                    BrushShape);

                ShapeCell cell = Document[layer, part];

                if (cell.Type is CellType.Shape or CellType.Crystal)
                {
                    cell.SetColor(BrushColor);
                }

                return true;

            default:
                return false;
        }
    }

    private void OnShapeOptionChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_syncingOptions ||
            e.PropertyName != nameof(ShapeBrushOption.IsActive) ||
            sender is not ShapeBrushOption option)
        {
            return;
        }

        _syncingOptions = true;

        try
        {
            if (option.IsActive)
            {
                foreach (ShapeBrushOption other in ShapeOptions)
                {
                    if (!ReferenceEquals(other, option))
                    {
                        other.IsActive = false;
                    }
                }

                BrushShape = option.Type;

                ShapeBrushActive = true;
            }
            else if (BrushShape == option.Type)
            {
                ShapeBrushActive = false;
            }
        }
        finally
        {
            _syncingOptions = false;
        }

        UpdateStatus();
    }

    private void OnColorOptionChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (_syncingOptions ||
            e.PropertyName != nameof(ColorBrushOption.IsActive) ||
            sender is not ColorBrushOption option)
        {
            return;
        }

        _syncingOptions = true;

        try
        {
            if (option.IsActive)
            {
                foreach (ColorBrushOption other in ColorOptions)
                {
                    if (!ReferenceEquals(other, option))
                    {
                        other.IsActive = false;
                    }
                }

                BrushColor = option.Code;

                ColorBrushActive = true;
            }
            else if (BrushColor == option.Code)
            {
                ColorBrushActive = false;
            }
        }
        finally
        {
            _syncingOptions = false;
        }

        UpdateStatus();
    }

    private void ClearBrushes()
    {
        _syncingOptions = true;

        try
        {
            foreach (ShapeBrushOption option in ShapeOptions)
            {
                option.IsActive = false;
            }

            foreach (ColorBrushOption option in ColorOptions)
            {
                option.IsActive = false;
            }
        }
        finally
        {
            _syncingOptions = false;
        }

        ShapeBrushActive = false;
        ColorBrushActive = false;

        UpdateStatus();
    }

    #endregion

    #region 模式切换

    public void SetMode(
        CanvasMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;

        ClearBrushes();

        ClearSelection();
    }

    #endregion

    #region 文档操作

    public void ApplyDimensions()
    {
        ErrorMessage = "";

        if (!SaveFileService.IsValidDimensions(LayerCount, PartCount))
        {
            ErrorMessage =
                "图形尺寸无效。层数和象限数必须大于 0，且单元格总数不能超过 10000。";

            return;
        }

        SaveUndoState();

        Document = Document.Resize(
            LayerCount,
            PartCount);

        ClearSelection();

        InvalidateGrid();

        ScheduleAutoSave();
    }

    public void ClearDocument()
    {
        ErrorMessage = "";

        SaveUndoState();

        Document = new ShapeDocument(
            LayerCount,
            PartCount);

        Code = "";

        ClearSelection();

        ResetView();

        InvalidateGrid();

        ScheduleAutoSave();
    }

    public void ClearSelectedCell()
    {
        if (SelectedLayer is not int layer ||
            SelectedPart is not int part)
        {
            return;
        }

        EnsureStroke();

        _editor.SetType(
            Document,
            layer,
            part,
            CellType.Empty);

        OnPropertyChanged(nameof(SelectedCellText));

        GridInvalidated?.Invoke(this, EventArgs.Empty);

        ScheduleAutoSave();
    }

    public void MoveUp()
    {
        SaveUndoState();

        Document.MoveUpOneLayer();

        AfterDocumentTransform();
    }

    public void MoveDown()
    {
        SaveUndoState();

        Document.MoveDownOneLayer();

        AfterDocumentTransform();
    }

    public void MoveLeft()
    {
        SaveUndoState();

        Document.MoveLeftCircular();

        AfterDocumentTransform();
    }

    public void MoveRight()
    {
        SaveUndoState();

        Document.MoveRightCircular();

        AfterDocumentTransform();
    }

    private void AfterDocumentTransform()
    {
        ClearSelection();

        InvalidateGrid();

        ScheduleAutoSave();
    }

    #endregion

    #region 撤销与重做

    private void SaveUndoState()
    {
        _undoStack.Push(Document.Clone());

        while (_undoStack.Count > MaxHistoryCount)
        {
            RemoveOldest(_undoStack);
        }

        _redoStack.Clear();

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(Document.Clone());

        Document = _undoStack.Pop();

        AfterHistoryChange();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(Document.Clone());

        Document = _redoStack.Pop();

        AfterHistoryChange();
    }

    private void AfterHistoryChange()
    {
        ClearSelection();

        ResetPointerState();

        InvalidateGrid();

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));

        ScheduleAutoSave();
    }

    private static void RemoveOldest(
        Stack<ShapeDocument> stack)
    {
        ShapeDocument[] items = stack.ToArray();

        stack.Clear();

        for (int index = items.Length - 2;
             index >= 0;
             index--)
        {
            stack.Push(items[index]);
        }
    }

    #endregion

    #region 短代码

    public void ParseCode()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Code))
        {
            ErrorMessage = "请输入短代码。";

            return;
        }

        if (!ShortCodeParser.TryInferDimensions(
                Code,
                Format,
                out int inferredLayers,
                out int inferredParts,
                out string? error))
        {
            ErrorMessage = error ?? "短代码格式错误。";

            return;
        }

        int layerCount = Math.Max(inferredLayers, MinimumDimension);
        int partCount = Math.Max(inferredParts, MinimumDimension);

        ParseResult result = _parser.Parse(
            Code.Trim(),
            layerCount,
            partCount,
            Format);

        if (!result.Success ||
            result.Document is null)
        {
            ErrorMessage = result.Error ?? "短代码解析失败。";

            return;
        }

        SaveUndoState();

        Document = result.Document;

        Code = _exporter.Export(
            Document,
            Format,
            omitTopEmptyLayers: true);

        ClearSelection();

        InvalidateGrid();

        ScheduleAutoSave();
    }

    public void ExportCode()
    {
        ErrorMessage = "";

        Code = _exporter.Export(
            Document,
            Format,
            omitTopEmptyLayers: true);

        StatusMessage = "已导出短代码。";
    }

    public void CopyCode()
    {
        ErrorMessage = "";

        string text = string.IsNullOrWhiteSpace(Code)
            ? _exporter.Export(Document, Format, omitTopEmptyLayers: true)
            : Code.Trim();

        if (string.IsNullOrEmpty(text))
        {
            ErrorMessage = "当前没有可复制的短代码。";

            return;
        }

        Clipboard.SetText(text);

        StatusMessage = "短代码已复制到剪贴板。";
    }

    public void PasteCode()
    {
        ErrorMessage = "";

        string? text;

        try
        {
            text = Clipboard.GetText();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"读取剪贴板失败：{exception.Message}";

            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ErrorMessage = "剪贴板里没有文本。";

            return;
        }

        Code = text.Trim();

        StatusMessage = "已从剪贴板粘贴，点击解析查看结果。";
    }

    /// <summary>
    /// Esc：取消画笔与选中。
    /// </summary>
    public void CancelInteraction()
    {
        ClearBrushes();

        ClearSelection();
    }

    #endregion

    #region 图片与存档

    public void ExportImage()
    {
        ErrorMessage = "";

        string? path = _dialogs.SavePngFile(
            DialogService.BuildTimestampedName(
                "shape-editor",
                ".png"));

        if (path is null)
        {
            return;
        }

        try
        {
            PngExporter.Save(Document, path);

            StatusMessage = $"已导出图片：{path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"图片导出失败：{exception.Message}";
        }
    }

    public void CopyImage()
    {
        ErrorMessage = "";

        try
        {
            Clipboard.SetImage(PngExporter.Render(Document));

            StatusMessage = "图片已复制到剪贴板。";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"复制图片失败：{exception.Message}";
        }
    }

    public void ExportSaveFile()
    {
        ErrorMessage = "";

        string? path = _dialogs.SaveSaveFile(
            DialogService.BuildTimestampedName(
                "shape-editor",
                ".json"));

        if (path is null)
        {
            return;
        }

        try
        {
            new SaveFileService().Save(Document, path);

            StatusMessage = $"已导出存档：{path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"导出存档失败：{exception.Message}";
        }
    }

    public void ImportSaveFile()
    {
        ErrorMessage = "";

        string? path = _dialogs.OpenSaveFile();

        if (path is null)
        {
            return;
        }

        LoadResult result = new SaveFileService().Load(path);

        if (!result.Success ||
            result.Document is null)
        {
            ErrorMessage = result.Error ?? "导入存档失败。";

            return;
        }

        SaveUndoState();

        Document = result.Document;

        Code = _exporter.Export(
            Document,
            Format,
            omitTopEmptyLayers: true);

        ClearSelection();

        InvalidateGrid();

        ScheduleAutoSave();

        StatusMessage = $"已导入存档：{Path.GetFileName(path)}";
    }

    #endregion

    #region 视图

    public void ResetView()
    {
        Zoom = 1;

        PanX = 0;

        PanY = 0;
    }

    #endregion

    #region 状态与自动保存

    private void UpdateStatus()
    {
        OnPropertyChanged(nameof(BrushStatusText));
    }

    private void ClearSelection()
    {
        SelectedLayer = null;

        SelectedPart = null;
    }

    private void ResetPointerState()
    {
        _strokeStarted = false;
    }

    private void EnsureStroke()
    {
        if (!_strokeStarted)
        {
            BeginStroke();
        }
    }

    private void InvalidateGrid()
    {
        GridInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleAutoSave()
    {
        _autoSaveTimer.Stop();

        _autoSaveTimer.Start();
    }

    private void OnAutoSaveTick(
        object? sender,
        EventArgs e)
    {
        _autoSaveTimer.Stop();

        _autoSave.Save(Document);

        if (_autoSave.LastError is not null && !_storageErrorShown)
        {
            _storageErrorShown = true;

            MessageBox.Show(
                $"自动保存失败，数据可能无法恢复。\n\n路径：{_autoSave.FilePath}\n原因：{_autoSave.LastError}",
                "存储失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string GetShapeName(
        CellType type)
    {
        return type switch
        {
            CellType.Empty => "空",
            CellType.Pin => "顶针",
            CellType.Crystal => "晶体",
            CellType.Shape => "图形",
            _ => ""
        };
    }

    #endregion
}

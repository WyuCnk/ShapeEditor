using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Maui;

public partial class MainPage : ContentPage
{
    #region 类型

    private readonly record struct CellAddress(
        int Layer,
        int Part);

    private enum BrushMode
    {
        None,
        Shape,
        Color,
        Combined
    }

    #endregion

    #region 画布状态

    /// <summary>
    /// 当前是否正在进行单指移动画布。
    /// </summary>
    private bool _isPanning;

    /// <summary>
    /// 上一次移动画布时的触摸位置。
    /// </summary>
    private PointF _lastPanPoint;

    /// <summary>
    /// 当前一次触摸操作的起点。
    /// </summary>
    private PointF _touchStartPoint;

    /// <summary>
    /// 当前触摸是否已经超过拖动阈值。
    /// </summary>
    private bool _touchMoved;

    /// <summary>
    /// 画笔触摸开始时对应的单元格。
    /// </summary>
    private int? _touchStartLayer;

    private int? _touchStartPart;

    private const float DragThreshold = 8f;


    #endregion

    #region 字段

    private const int MaxLayerCount = 100;
    private const int MaxPartCount = 100;
    private const int MaxCellCount = 10_000;

    private DateTime _lastGridRefreshTime =
    DateTime.MinValue;

    private static readonly TimeSpan GridRefreshInterval =
        TimeSpan.FromMilliseconds(30);

    private const string SavedLayerCountKey =
        "shape_editor_layer_count";

    private const string SavedPartCountKey =
        "shape_editor_part_count";

    private const string SavedShortCodeKey =
        "shape_editor_short_code";

    private bool _documentRestored;

    private ShapeDocument _document =
        new(layerCount: 5, partCount: 4);

    private readonly ShapeEditorService _editor = new();

    private readonly ShapeGridDrawable _shapeDrawable = new();

    private readonly ShortCodeParser _parser = new();

    private readonly ShortCodeExporter _exporter = new();

    private readonly ShapeImageExporter _imageExporter = new();

    /// <summary>
    /// 形状画笔是否启用。
    /// </summary>
    private bool _shapeBrushActive;

    /// <summary>
    /// 颜色画笔是否启用。
    /// </summary>
    private bool _colorBrushActive;

    /// <summary>
    /// 当前形状画笔类型。
    /// </summary>
    private CellType _selectedBrushShape =
        CellType.Shape;

    /// <summary>
    /// 当前颜色画笔颜色。
    /// </summary>
    private char _selectedBrushColor =
        'u';

    /// <summary>
    /// 是否正在进行单指画笔绘制。
    /// </summary>
    private bool _isPainting;

    /// <summary>
    /// 本次拖动已经处理过的单元格。
    /// </summary>
    private readonly HashSet<CellAddress> _paintedCells = new();

    /// <summary>
    /// 撤销栈。
    /// 栈顶是最近一次操作之前的状态。
    /// </summary>
    private readonly Stack<ShapeDocument> _undoStack = new();

    /// <summary>
    /// 重做栈。
    /// 栈顶是最近一次撤销掉的状态。
    /// </summary>
    private readonly Stack<ShapeDocument> _redoStack = new();

    /// <summary>
    /// 当前一次画笔拖动是否已经保存过撤销快照。
    /// </summary>
    private bool _paintingHistorySaved;

    private bool _paintingChanged;

    /// <summary>
    /// 当前选中的单元格。
    /// </summary>
    private int? _selectedLayer;

    private int? _selectedPart;

    /// <summary>
    /// 是否存在当前选中的单元格。
    /// </summary>
    private bool HasSelectedCell =>
        _selectedLayer is not null &&
        _selectedPart is not null;

    /// <summary>
    /// 是否正在进行双指缩放/平移。
    /// </summary>
    private bool _isTransforming;

    /// <summary>
    /// 双指手势开始时两指之间的距离。
    /// </summary>
    private float _lastTouchDistance;

    /// <summary>
    /// 双指手势开始时两指中心点。
    /// </summary>
    private PointF _lastTouchCenter;

    /// <summary>
    /// 双指手势开始时的视图参数。
    /// </summary>
    private float _startZoom;
    private float _startPanX;
    private float _startPanY;

    #endregion

    #region 页面模式

    private enum CanvasMode
    {
        Paint,
        Move
    }

    private CanvasMode _canvasMode =
        CanvasMode.Paint;

    #endregion

    #region 画笔状态

    private BrushMode CurrentBrushMode
    {
        get
        {
            if (_shapeBrushActive &&
                _colorBrushActive)
            {
                return BrushMode.Combined;
            }

            if (_shapeBrushActive)
            {
                return BrushMode.Shape;
            }

            if (_colorBrushActive)
            {
                return BrushMode.Color;
            }

            return BrushMode.None;
        }
    }

    #endregion

    #region 颜色常量

    private static readonly Color SelectedBorderColor =
        Color.FromArgb("#212121");

    private static readonly Color NormalBorderColor =
        Color.FromArgb("#BDBDBD");

    #endregion

    #region 生命周期

    public MainPage()
    {
        InitializeComponent();

        ShapeGraphicsView.Drawable =
            _shapeDrawable;

        RestoreDocument();

        RefreshShapeGrid();
        RefreshModeButtons();
        RefreshEditorPanel();
        UpdateUndoRedoState();
        UpdateStatus();
    }

    #endregion

    #region 输入校验



    private bool TryReadDimensions(
    out int layers,
    out int parts)
    {
        layers = 0;
        parts = 0;

        if (!int.TryParse(
                LayerEntry.Text?.Trim(),
                out layers) ||
            !int.TryParse(
                PartEntry.Text?.Trim(),
                out parts))
        {
            CodeErrorLabel.Text =
                "层数和象限数必须是整数。";

            return false;
        }

        if (layers < 1 ||
            layers > MaxLayerCount)
        {
            CodeErrorLabel.Text =
                $"层数必须在 1～{MaxLayerCount} 之间。";

            return false;
        }

        if (parts < 1 ||
            parts > MaxPartCount)
        {
            CodeErrorLabel.Text =
                $"象限数必须在 1～{MaxPartCount} 之间。";

            return false;
        }

        long cellCount =
            (long)layers * parts;

        if (cellCount > MaxCellCount)
        {
            CodeErrorLabel.Text =
                $"单元格总数不能超过 {MaxCellCount}。";

            return false;
        }

        return true;
    }

    #endregion

    #region 短代码尺寸推断
    private bool TryInferCodeDimensions(
    string code,
    out int layers,
    out int parts)
    {
        layers = 0;
        parts = 0;

        string[] layerCodes =
            code.Split(
                ':',
                StringSplitOptions.None);

        if (layerCodes.Length == 0)
        {
            CodeErrorLabel.Text =
                "短代码没有包含有效层。";

            return false;
        }

        layers = layerCodes.Length;

        int firstLength =
            layerCodes[0].Length;

        if (firstLength == 0 ||
            firstLength % 2 != 0)
        {
            CodeErrorLabel.Text =
                "每层短代码长度必须是偶数。";

            return false;
        }

        parts = firstLength / 2;

        if (parts <= 0)
        {
            CodeErrorLabel.Text =
                "每层至少需要包含一个象限。";

            return false;
        }

        for (int index = 0;
             index < layerCodes.Length;
             index++)
        {
            if (layerCodes[index].Length != firstLength)
            {
                CodeErrorLabel.Text =
                    $"第 {index + 1} 层的象限数与其他层不一致。";

                return false;
            }
        }

        if (layers > MaxLayerCount)
        {
            CodeErrorLabel.Text =
                $"短代码层数不能超过 {MaxLayerCount}。";

            return false;
        }

        if (parts > MaxPartCount)
        {
            CodeErrorLabel.Text =
                $"短代码象限数不能超过 {MaxPartCount}。";

            return false;
        }

        long cellCount =
            (long)layers * parts;

        if (cellCount > MaxCellCount)
        {
            CodeErrorLabel.Text =
                $"短代码单元格总数不能超过 {MaxCellCount}。";

            return false;
        }

        return true;
    }

    #endregion

    #region 视图刷新

    private void RefreshShapeGridThrottled()
    {
        DateTime now =
            DateTime.UtcNow;

        if (now - _lastGridRefreshTime <
            GridRefreshInterval)
        {
            return;
        }

        _lastGridRefreshTime =
            now;

        ShapeGraphicsView.Invalidate();
    }

    private void RefreshShapeGrid()
    {
        _shapeDrawable.SetDocument(_document);
        ShapeGraphicsView.Invalidate();
    }

    private void ResetViewButton_Clicked(
        object? sender,
        EventArgs e)
    {
        StopPainting();
        StopTransform();

        _shapeDrawable.ResetView();
        ShapeGraphicsView.Invalidate();
    }

    private void ClearSelectedCell()
    {
        _selectedLayer = null;
        _selectedPart = null;

        _shapeDrawable.SetSelectedCell(
            null,
            null);
    }

    #endregion

    #region 撤销和重做

    private const int MaxHistoryCount = 100;

    /// <summary>
    /// 保存当前状态，作为一次新操作的撤销起点。
    /// </summary>
    private void SaveUndoState()
    {
        _undoStack.Push(
            _document.Clone());

        while (_undoStack.Count > MaxHistoryCount)
        {
            RemoveOldest(
                _undoStack);
        }

        // 一旦产生新操作，之前的重做历史失效。
        _redoStack.Clear();

        UpdateUndoRedoState();
    }

    /// <summary>
    /// 撤销最近一次操作。
    /// </summary>
    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(
            _document.Clone());

        _document =
            _undoStack.Pop();

        SyncDimensionInputs();
        SaveCurrentDocument();

        ClearSelectedCell();
        StopPainting();
        StopTransform();

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateStatus();
        UpdateUndoRedoState();
    }

    /// <summary>
    /// 重做最近撤销的操作。
    /// </summary>
    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(
            _document.Clone());

        _document =
            _redoStack.Pop();

        SyncDimensionInputs();
        SaveCurrentDocument();

        ClearSelectedCell();
        StopPainting();
        StopTransform();

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateStatus();
        UpdateUndoRedoState();
    }

    /// <summary>
    /// 删除 Stack 中最早加入的状态。
    /// Stack 没有直接删除底部元素，因此转换为数组后重建。
    /// </summary>
    private static void RemoveOldest(
        Stack<ShapeDocument> stack)
    {
        ShapeDocument[] items =
            stack.ToArray();

        stack.Clear();

        for (int index = items.Length - 2;
             index >= 0;
             index--)
        {
            stack.Push(items[index]);
        }
    }

    /// <summary>
    /// 更新撤销、重做按钮的可用状态。
    /// </summary>
    private void UpdateUndoRedoState()
    {
        UndoButton.IsEnabled =
            _undoStack.Count > 0;

        RedoButton.IsEnabled =
            _redoStack.Count > 0;
    }

    #endregion

    #region 页面模式按钮

    private void PaintModeButton_Clicked(
        object? sender,
        EventArgs e)
    {
        SetCanvasMode(CanvasMode.Paint);
    }

    private void MoveModeButton_Clicked(
        object? sender,
        EventArgs e)
    {
        SetCanvasMode(CanvasMode.Move);
    }

    private void SetCanvasMode(
        CanvasMode mode)
    {
        FinishPainting();
        StopTransform();
        ResetTouchState();

        _canvasMode = mode;
        _isPanning = false;

        if (mode == CanvasMode.Move)
        {
            ClearSelectedCell();
        }

        RefreshModeButtons();
        RefreshEditorPanel();
        UpdateStatus();
    }

    #endregion

    #region 触摸交互

    private void ResetTouchState()
    {
        _isPanning = false;
        _touchMoved = false;
        _touchStartPoint = default;
        _touchStartLayer = null;
        _touchStartPart = null;
    }

    private bool IsDragBeyondThreshold(
        PointF point)
    {
        float dx =
            point.X - _touchStartPoint.X;

        float dy =
            point.Y - _touchStartPoint.Y;

        return MathF.Sqrt(
            dx * dx + dy * dy) >= DragThreshold;
    }

    private void BeginPaintingIfNeeded()
    {
        if (_isPainting)
        {
            return;
        }

        _isPainting = true;
        _paintedCells.Clear();
        _paintingHistorySaved = false;
        _paintingChanged = false;

        if (_touchStartLayer is int layer &&
            _touchStartPart is int part)
        {
            PaintCell(
                layer,
                part);
        }
    }

    private void CancelBrushesAndSelection()
    {
        _shapeBrushActive = false;
        _colorBrushActive = false;

        ClearSelectedCell();
        RefreshEditorPanel();
        UpdateStatus();
    }

    private void ShapeGraphicsView_StartInteraction(
    object? sender,
    TouchEventArgs e)
    {
        /*
         * 双指操作优先级最高。
         */
        if (e.Touches.Length >= 2)
        {
            FinishPainting();
            ResetTouchState();
            StartTransform(e.Touches);
            return;
        }

        if (e.Touches.Length == 0)
        {
            return;
        }

        PointF point =
            e.Touches[0];

        _touchStartPoint = point;
        _touchMoved = false;
        _isPanning = false;
        _isPainting = false;
        _paintedCells.Clear();

        if (_canvasMode == CanvasMode.Move)
        {
            _touchStartLayer = null;
            _touchStartPart = null;
            _lastPanPoint = point;

            return;
        }

        bool hitCell =
            TryGetTouchCell(
                point,
                out int layer,
                out int part);

        if (hitCell)
        {
            _touchStartLayer = layer;
            _touchStartPart = part;
        }
        else
        {
            _touchStartLayer = null;
            _touchStartPart = null;
        }

        /*
         * 注意：
         * 这里暂时不修改单元格。
         *
         * 是否是点击还是拖动，要等后续移动或抬起时再判断。
         */
    }

    private void ShapeGraphicsView_DragInteraction(
    object? sender,
    TouchEventArgs e)
    {
        /*
         * 双指：缩放和平移。
         */
        if (e.Touches.Length >= 2)
        {
            FinishPainting();
            _isPanning = false;
            UpdateTransform(e.Touches);
            return;
        }

        if (e.Touches.Length == 0)
        {
            return;
        }

        PointF point =
            e.Touches[0];

        if (_canvasMode == CanvasMode.Move)
        {
            MoveCanvasWithSingleFinger(point);
            return;
        }

        /*
         * 未超过阈值时，仍然视为点击。
         */
        if (!_touchMoved &&
            !IsDragBeyondThreshold(point))
        {
            return;
        }

        _touchMoved = true;

        /*
         * 没有启用画笔：
         * 单指拖动移动画布。
         */
        if (CurrentBrushMode == BrushMode.None)
        {
            MoveCanvasWithSingleFinger(point);
            return;
        }

        /*
         * 已启用画笔：
         * 超过阈值后才开始连续绘制。
         */
        BeginPaintingIfNeeded();

        if (!TryGetTouchCell(
                point,
                out int layer,
                out int part))
        {
            return;
        }

        PaintCell(
            layer,
            part);
    }

    private void MoveCanvasWithSingleFinger(
    PointF point)
    {
        if (!_isPanning)
        {
            _isPanning = true;
            _lastPanPoint = point;
            return;
        }

        float deltaX =
            point.X - _lastPanPoint.X;

        float deltaY =
            point.Y - _lastPanPoint.Y;

        _shapeDrawable.ApplyTransform(
            _shapeDrawable.Zoom,
            _shapeDrawable.PanX + deltaX,
            _shapeDrawable.PanY + deltaY);

        _lastPanPoint = point;

        ShapeGraphicsView.Invalidate();
    }

    private void ShapeGraphicsView_EndInteraction(
    object? sender,
    TouchEventArgs e)
    {
        if (_isTransforming)
        {
            StopTransform();
            ResetTouchState();
            return;
        }

        if (_canvasMode == CanvasMode.Move)
        {
            _isPanning = false;
            ResetTouchState();
            return;
        }

        if (!_touchMoved)
        {
            HandleSingleTap();
        }
        else if (_isPainting)
        {
            FinishPainting();
        }

        ResetTouchState();
    }

    private void HandleSingleTap()
    {
        /*
         * 点击画布外：
         * 取消画笔和当前单元格选择。
         */
        if (_touchStartLayer is not int layer ||
            _touchStartPart is not int part)
        {
            CancelBrushesAndSelection();
            return;
        }

        /*
         * 没有启用画笔：
         * 点击单元格只选择并查看状态。
         */
        if (CurrentBrushMode == BrushMode.None)
        {
            SelectCell(
                layer,
                part);

            return;
        }

        /*
         * 有画笔：
         * 点击单元格只修改一次。
         */
        BeginPaintingIfNeeded();
        FinishPainting();
    }

    private bool TryGetTouchCell(
        PointF point,
        out int layer,
        out int part)
    {
        layer = -1;
        part = -1;

        float width = (float)ShapeGraphicsView.Width;
        float height = (float)ShapeGraphicsView.Height;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        return _shapeDrawable.TryHitTest(
            point,
            width,
            height,
            out layer,
            out part);
    }

    #endregion

    #region 双指缩放和平移

    private void StartTransform(PointF[] touches)
    {
        FinishPainting();

        _isPanning = false;

        if (touches.Length < 2)
        {
            return;
        }

        _isTransforming = true;

        PointF first = touches[0];
        PointF second = touches[1];

        _lastTouchDistance =
            GetTouchDistance(first, second);

        _lastTouchCenter =
            GetTouchCenter(first, second);

        /*
         * 记录本次双指手势开始时的状态。
         *
         * 后续 UpdateTransform 始终基于这三个值计算，
         * 避免每次拖动事件都叠加导致画面跳动。
         */
        _startZoom = _shapeDrawable.Zoom;
        _startPanX = _shapeDrawable.PanX;
        _startPanY = _shapeDrawable.PanY;
    }

    private void UpdateTransform(PointF[] touches)
    {
        if (!_isTransforming ||
            touches.Length < 2 ||
            _lastTouchDistance <= 0)
        {
            return;
        }

        PointF first = touches[0];
        PointF second = touches[1];

        float currentDistance =
            GetTouchDistance(first, second);

        PointF currentCenter =
            GetTouchCenter(first, second);

        float scale =
            currentDistance / _lastTouchDistance;

        float newZoom =
            _startZoom * scale;

        float deltaX =
            currentCenter.X - _lastTouchCenter.X;

        float deltaY =
            currentCenter.Y - _lastTouchCenter.Y;

        _shapeDrawable.ApplyTransform(
            newZoom,
            _startPanX + deltaX,
            _startPanY + deltaY);

        ShapeGraphicsView.Invalidate();
    }

    private void StopTransform()
    {
        _isTransforming = false;
        _lastTouchDistance = 0;
        _lastTouchCenter = default;
    }

    private static float GetTouchDistance(
        PointF first,
        PointF second)
    {
        float dx = second.X - first.X;
        float dy = second.Y - first.Y;

        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static PointF GetTouchCenter(
        PointF first,
        PointF second)
    {
        return new PointF(
            (first.X + second.X) / 2f,
            (first.Y + second.Y) / 2f);
    }

    #endregion

    #region 画笔绘制

    private void PaintCell(
        int layer,
        int part)
    {
        var address = new CellAddress(
            layer,
            part);

        /*
         * 同一次拖动中，同一个单元格只处理一次。
         */
        if (!_paintedCells.Add(address))
        {
            return;
        }

        if (!_paintingHistorySaved)
        {
            SaveUndoState();
            _paintingHistorySaved = true;
        }

        BrushMode brushMode =
    CurrentBrushMode;

        if (brushMode == BrushMode.None)
        {
            return;
        }

        bool changed =
            brushMode switch
            {
                BrushMode.Shape =>
                    ApplyShapeBrush(
                        layer,
                        part),

                BrushMode.Color =>
                    ApplyColorBrush(
                        layer,
                        part),

                BrushMode.Combined =>
                    ApplyCombinedBrush(
                        layer,
                        part),

                _ =>
                    false
            };

        if (changed)
        {
            _paintingChanged = true;
            RefreshShapeGridThrottled();
        }
    }

    private bool ApplyShapeBrush(
    int layer,
    int part)
    {
        _editor.ApplyShapeBrush(
            _document,
            layer,
            part,
            _selectedBrushShape);

        return true;
    }

    private bool ApplyColorBrush(
    int layer,
    int part)
    {
        return _editor.ApplyColorBrush(
            _document,
            layer,
            part,
            _selectedBrushColor);
    }

    private bool ApplyCombinedBrush(
    int layer,
    int part)
    {
        _editor.SetType(
            _document,
            layer,
            part,
            _selectedBrushShape);

        ShapeCell cell =
            _document[layer, part];

        if (cell.Type is CellType.Shape or CellType.Crystal)
        {
            cell.SetColor(
                _selectedBrushColor);
        }

        return true;
    }

    private void FinishPainting()
    {
        if (_isPainting &&
            _paintingChanged)
        {
            SaveCurrentDocument();
        }

        StopPainting();
    }

    private void StopPainting()
    {
        _isPainting = false;
        _paintedCells.Clear();
        _paintingHistorySaved = false;
        _paintingChanged = false;
    }

    #endregion

    #region 单元格选择

    private void SelectCell(
    int layer,
    int part)
    {
        _selectedLayer = layer;
        _selectedPart = part;

        _shapeDrawable.SetSelectedCell(
            layer,
            part);

        ShapeGraphicsView.Invalidate();

        RefreshEditorPanel();
    }

    #endregion

    #region 编辑面板刷新

    private void RefreshEditorPanel()
    {
        EditorPanel.IsVisible = true;

        RefreshModePanels();
        RefreshBrushStatus();
        RefreshSelectedCellStatus();
        RefreshTypeButtons();
        RefreshColorButtons();
    }

    private void RefreshModePanels()
    {
        bool paintMode =
            _canvasMode == CanvasMode.Paint;

        PaintToolsPanel.IsVisible =
            paintMode;

        MoveToolsPanel.IsVisible =
            !paintMode;

        TypePanel.IsEnabled =
            paintMode;

        ColorPanel.IsEnabled =
            paintMode;
    }

    private void RefreshBrushStatus()
    {
        if (_canvasMode == CanvasMode.Move)
        {
            PageStatusLabel.Text =
                "移动模式：单指拖动，双指缩放/移动";

            return;
        }

        PageStatusLabel.Text =
            CurrentBrushMode switch
            {
                BrushMode.None =>
                    "画笔模式：未选画笔",

                BrushMode.Shape =>
                    $"形状画笔：{GetCellTypeName(_selectedBrushShape)}",

                BrushMode.Color =>
                    $"颜色画笔：{GetColorName(_selectedBrushColor)}",

                BrushMode.Combined =>
                    $"组合画笔：{GetCellTypeName(_selectedBrushShape)} + " +
                    $"{GetColorName(_selectedBrushColor)}",

                _ =>
                    "画笔模式"
            };
    }

    private void RefreshSelectedCellStatus()
    {
        if (!HasSelectedCell)
        {
            SelectedCellLabel.Text =
                "未选择单元格";

            return;
        }

        ShapeCell cell =
            _document[
                _selectedLayer!.Value,
                _selectedPart!.Value];

        string colorText =
            cell.ColorCode is char color
                ? GetColorName(color)
                : "无颜色";

        SelectedCellLabel.Text =
            $"第 {_selectedLayer.Value + 1} 层，" +
            $"第 {_selectedPart.Value + 1} 象限\n" +
            $"类型：{GetCellTypeName(cell.Type)}，" +
            $"颜色：{colorText}，" +
            $"短代码：{cell.ToShortCode()}";
    }

    #endregion

    #region 类型按钮

    private void EmptyButton_Clicked(
        object? sender,
        EventArgs e)
    {
        HandleTypeButton(CellType.Empty);
    }

    private void PinButton_Clicked(
        object? sender,
        EventArgs e)
    {
        HandleTypeButton(CellType.Pin);
    }

    private void CrystalButton_Clicked(
        object? sender,
        EventArgs e)
    {
        HandleTypeButton(CellType.Crystal);
    }

    private void ShapeButton_Clicked(
        object? sender,
        EventArgs e)
    {
        HandleTypeButton(CellType.Shape);
    }

    private void HandleTypeButton(
    CellType type)
    {
        /*
         * 类型按钮现在是画笔开关：
         *
         * 未启用 → 点击后启用
         * 已启用且点击当前类型 → 取消
         * 已启用但点击其他类型 → 切换类型
         */
        if (!_shapeBrushActive)
        {
            _shapeBrushActive = true;
            _selectedBrushShape = type;
        }
        else if (_selectedBrushShape == type)
        {
            _shapeBrushActive = false;
        }
        else
        {
            _selectedBrushShape = type;
        }

        RefreshEditorPanel();
        UpdateStatus();
    }

    private void RefreshTypeButtons()
    {
        SetButtonState(
            EmptyButton,
            CellType.Empty);

        SetButtonState(
            PinButton,
            CellType.Pin);

        SetButtonState(
            CrystalButton,
            CellType.Crystal);

        SetButtonState(
            ShapeButton,
            CellType.Shape);
    }

    private void SetButtonState(
    Button button,
    CellType type)
    {
        bool isCurrentCellType =
            HasSelectedCell &&
            _document[
                _selectedLayer!.Value,
                _selectedPart!.Value].Type == type;

        bool isActiveBrush =
            _shapeBrushActive &&
            _selectedBrushShape == type;

        SetButtonVisual(
            button,
            isCurrentCellType,
            isActiveBrush);
    }

    #endregion

    #region 颜色按钮

    private void ColorButton_Clicked(
    object? sender,
    EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.CommandParameter is not string colorText ||
            colorText.Length != 1)
        {
            return;
        }

        char color = colorText[0];

        /*
         * 颜色按钮也是独立开关：
         *
         * 未启用 → 启用该颜色
         * 已启用且点击当前颜色 → 取消颜色画笔
         * 已启用但点击其他颜色 → 切换颜色
         */
        if (!_colorBrushActive)
        {
            _colorBrushActive = true;
            _selectedBrushColor = color;
        }
        else if (_selectedBrushColor == color)
        {
            _colorBrushActive = false;
        }
        else
        {
            _selectedBrushColor = color;
        }

        RefreshEditorPanel();
        UpdateStatus();
    }

    private void RefreshColorButtons()
    {
        foreach (View child in ColorPanel.Children)
        {
            if (child is not Button button)
            {
                continue;
            }

            if (button.CommandParameter is not string parameter ||
                parameter.Length != 1)
            {
                continue;
            }

            char color =
                parameter[0];

            bool isCurrentCellColor =
                HasSelectedCell &&
                _document[
                    _selectedLayer!.Value,
                    _selectedPart!.Value].ColorCode == color;

            bool isActiveBrush =
                _colorBrushActive &&
                _selectedBrushColor == color;

            SetButtonVisual(
                button,
                isCurrentCellColor,
                isActiveBrush);
        }
    }

    #endregion

    #region 撤销和重做按钮

    private void UndoButton_Clicked(
        object? sender,
        EventArgs e)
    {
        Undo();
    }

    private void RedoButton_Clicked(
        object? sender,
        EventArgs e)
    {
        Redo();
    }

    #endregion

    #region 图形整体移动

    private void MoveUpButton_Clicked(
    object? sender,
    EventArgs e)
    {
        SaveUndoState();

        _document.MoveUpOneLayer();

        AfterDocumentChanged();
    }

    private void MoveDownButton_Clicked(
        object? sender,
        EventArgs e)
    {
        SaveUndoState();

        _document.MoveDownOneLayer();

        AfterDocumentChanged();
    }

    private void MoveLeftButton_Clicked(
        object? sender,
        EventArgs e)
    {
        SaveUndoState();

        _document.MoveLeftCircular();

        AfterDocumentChanged();
    }

    private void MoveRightButton_Clicked(
        object? sender,
        EventArgs e)
    {
        SaveUndoState();

        _document.MoveRightCircular();

        AfterDocumentChanged();
    }

    private void AfterDocumentChanged()
    {
        ClearSelectedCell();

        SaveCurrentDocument();

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateUndoRedoState();
        UpdateStatus();
    }

    #endregion

    #region 清空和尺寸

    private void ClearButton_Clicked(
        object? sender,
        EventArgs e)
    {
        StopPainting();
        StopTransform();

        SaveUndoState();

        _editor.Clear(_document);

        ClearSelectedCell();

        SaveCurrentDocument();

        CodeEntry.Text = "";
        CodeErrorLabel.Text = "";

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateStatus();
    }

    private async void ApplySizeButton_Clicked(
    object? sender,
    EventArgs e)
    {
        CodeErrorLabel.Text = "";

        if (!TryReadDimensions(
                out int layers,
                out int parts))
        {
            return;
        }

        bool sizeChanged =
            layers != _document.LayerCount ||
            parts != _document.PartCount;

        if (!sizeChanged)
        {
            UpdateStatus();
            return;
        }

        /*
         * 缩小尺寸可能会删除已有内容。
         * 只有确实存在将被删除的非空内容时才询问。
         */
        if (_document.HasContentOutside(
                layers,
                parts))
        {
            bool confirmed =
                await DisplayAlertAsync(
                    "确认调整尺寸",
                    "新尺寸会删除部分已有内容，是否继续？",
                    "继续",
                    "取消");

            if (!confirmed)
            {
                // 用户取消时恢复输入框中的旧尺寸。
                SyncDimensionInputs();
                return;
            }
        }

        StopPainting();
        StopTransform();

        /*
         * 在替换文档之前保存撤销快照。
         */
        SaveUndoState();

        _document =
            _document.Resize(
                layers,
                parts);

        ClearSelectedCell();

        SaveCurrentDocument();

        SyncDimensionInputs();

        CodeErrorLabel.Text = "";

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateUndoRedoState();
        UpdateStatus();
    }

    #endregion

    #region 短代码解析和导出

    private async void ParseCodeButton_Clicked(
    object? sender,
    EventArgs e)
    {
        CodeErrorLabel.Text = "";

        string code =
            CodeEntry.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code))
        {
            CodeErrorLabel.Text =
                "请输入短代码。";

            return;
        }

        /*
         * 先从短代码本身推断尺寸，
         * 不再使用输入框中的旧尺寸。
         */
        if (!TryInferCodeDimensions(
                code,
                out int layers,
                out int parts))
        {
            return;
        }

        if (layers < 4) layers = 4;
        if (parts < 4) parts = 4;

        ParseResult result =
            _parser.Parse(
                code,
                layers,
                parts);

        if (!result.Success ||
            result.Document is null)
        {
            CodeErrorLabel.Text =
                result.Error ?? "短代码解析失败。";

            return;
        }

        StopPainting();
        StopTransform();
        ResetTouchState();

        SaveUndoState();

        _document =
            result.Document;

        ClearSelectedCell();

        /*
         * 自动修改层数和象限数输入框。
         */
        SyncDimensionInputs();

        CodeEntry.Text =
            code;

        SaveCurrentDocument();

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateUndoRedoState();
        UpdateStatus();

        await DisplayAlertAsync(
            "解析成功",
            $"已自动调整为 {layers} 层 × {parts} 象限。",
            "确定");
    }

    private void ExportCodeButton_Clicked(
        object? sender,
        EventArgs e)
    {
        CodeErrorLabel.Text = "";

        string code =
            _exporter.Export(
                _document,
                omitTopEmptyLayers: true);

        CodeEntry.Text = code;

        UpdateStatus();
    }

    private async void CopyCodeButton_Clicked(
        object? sender,
        EventArgs e)
    {
        string code =
            _exporter.Export(
                _document,
                omitTopEmptyLayers: true);

        CodeEntry.Text = code;

        await Clipboard.Default.SetTextAsync(code);

        UpdateStatus();

        await DisplayAlertAsync(
            "复制成功",
            "短代码已经复制到剪贴板。",
            "确定");
    }

    #endregion

    #region 图片导出和分享

    private async Task<string> ExportCurrentImageAsync()
    {
        string shortCode =
            _exporter.Export(
                _document,
                omitTopEmptyLayers: true);

        return await _imageExporter.ExportPng(
            _document,
            shortCode,
            includeCode: true);
    }

    private async void ExportImageButton_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            string filePath =
                await ExportCurrentImageAsync();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "图片文件没有成功生成。",
                    filePath);
            }

            await DisplayAlertAsync(
                "导出成功",
                "竖式图图片已经生成。",
                "确定");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                "导出失败",
                GetUserFriendlyExportError(ex),
                "确定");
        }
    }

    private async void ShareImageButton_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            string filePath =
                await ExportCurrentImageAsync();

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "图片文件没有成功生成。",
                    filePath);
            }

            await Share.Default.RequestAsync(
                new ShareFileRequest
                {
                    Title = "分享竖式图",
                    File = new ShareFile(filePath)
                });
        }
        catch (OperationCanceledException)
        {
            /*
             * 用户关闭分享面板属于正常操作，不显示错误。
             */
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                "分享失败",
                GetUserFriendlyExportError(ex),
                "确定");
        }
    }

    private static string GetUserFriendlyExportError(
        Exception exception)
    {
        return exception switch
        {
            InvalidOperationException =>
                exception.Message,

            FileNotFoundException =>
                exception.Message,

            IOException =>
                "文件读写失败，请检查设备存储空间。",

            UnauthorizedAccessException =>
                "没有访问文件的权限。",

            _ =>
                $"操作失败：{exception.Message}"
        };
    }

    #endregion

    #region 按钮状态

    private static void SetButtonVisual(
    Button button,
    bool isCurrentCellState,
    bool isActiveBrush)
    {
        if (isActiveBrush)
        {
            /*
             * 黑色粗边：
             * 表示这是当前启用的画笔。
             */
            button.BorderColor =
                SelectedBorderColor;

            button.BorderWidth = 3;
            button.Scale = 0.96;
        }
        else if (isCurrentCellState)
        {
            /*
             * 蓝色细边：
             * 表示这是当前选中单元格的状态。
             */
            button.BorderColor =
                Color.FromArgb("#1976D2");

            button.BorderWidth = 1;
            button.Scale = 1;
        }
        else
        {
            button.BorderColor =
                NormalBorderColor;

            button.BorderWidth = 1;
            button.Scale = 1;
        }
    }

    private static void SetButtonSelected(
        Button button,
        bool selected)
    {
        if (selected)
        {
            button.BorderColor = SelectedBorderColor;
            button.BorderWidth = 3;
            button.Scale = 0.96;
        }
        else
        {
            button.BorderColor = NormalBorderColor;
            button.BorderWidth = 1;
            button.Scale = 1;
        }
    }

    private void RefreshModeButtons()
    {
        SetCanvasModeButtonVisual(
            PaintModeButton,
            _canvasMode == CanvasMode.Paint);

        SetCanvasModeButtonVisual(
            MoveModeButton,
            _canvasMode == CanvasMode.Move);
    }

    private static void SetCanvasModeButtonVisual(
        Button button,
        bool selected)
    {
        button.BackgroundColor =
            selected
                ? Color.FromArgb("#311B92")
                : Color.FromArgb("#512BD4");

        button.TextColor =
            Colors.White;

        button.BorderColor =
            selected
                ? Colors.White
                : Colors.Transparent;

        button.BorderWidth =
            selected ? 2 : 1;
    }

    #endregion

    #region 文本和状态

    private static string GetCellTypeName(CellType type)
    {
        return type switch
        {
            CellType.Empty => "空",
            CellType.Pin => "顶针",
            CellType.Crystal => "晶体",
            CellType.Shape => "普通形状",
            _ => ""
        };
    }

    private static string GetColorName(char color)
    {
        return color switch
        {
            'r' => "红色",
            'g' => "绿色",
            'b' => "蓝色",
            'c' => "青色",
            'm' => "洋红色",
            'y' => "黄色",
            'w' => "白色",
            'k' => "黑色",
            'u' => "未着色",
            _ => "未知颜色"
        };
    }

    private void UpdateStatus()
    {
        Title = "竖式图编辑器";
    }

    #endregion

    #region 文档状态同步

    private void SyncDimensionInputs()
    {
        LayerEntry.Text =
            _document.LayerCount.ToString();

        PartEntry.Text =
            _document.PartCount.ToString();
    }

    #endregion

    #region 自动保存和恢复

    /// <summary>
    /// 将当前图形保存到应用本地设置。
    /// </summary>
    private void SaveCurrentDocument()
    {
        string shortCode =
            _exporter.Export(
                _document,
                omitTopEmptyLayers: true);

        Preferences.Default.Set(
            SavedLayerCountKey,
            _document.LayerCount);

        Preferences.Default.Set(
            SavedPartCountKey,
            _document.PartCount);

        Preferences.Default.Set(
            SavedShortCodeKey,
            shortCode);
    }

    /// <summary>
    /// 从应用本地设置恢复上次图形。
    /// </summary>
    private void RestoreDocument()
    {
        if (_documentRestored)
        {
            return;
        }

        _documentRestored = true;

        if (!Preferences.Default.ContainsKey(
                SavedShortCodeKey))
        {
            return;
        }

        int savedLayers =
            Preferences.Default.Get(
                SavedLayerCountKey,
                5);

        int savedParts =
            Preferences.Default.Get(
                SavedPartCountKey,
                4);

        string savedCode =
            Preferences.Default.Get(
                SavedShortCodeKey,
                "");

        if (string.IsNullOrWhiteSpace(savedCode))
        {
            return;
        }

        if (!IsValidDimensionValue(
                savedLayers,
                savedParts))
        {
            RemoveSavedDocument();
            return;
        }

        ParseResult result =
            _parser.Parse(
                savedCode,
                savedLayers,
                savedParts);

        if (!result.Success ||
            result.Document is null)
        {
            /*
             * 保存数据已经损坏或与当前格式不兼容。
             * 删除损坏数据，使用默认空白图继续运行。
             */
            RemoveSavedDocument();
            return;
        }

        _document =
            result.Document;

        SyncDimensionInputs();

        CodeEntry.Text =
            savedCode;
    }

    /// <summary>
    /// 判断保存数据中的尺寸是否有效。
    /// </summary>
    private static bool IsValidDimensionValue(
        int layers,
        int parts)
    {
        if (layers < 1 ||
            layers > MaxLayerCount)
        {
            return false;
        }

        if (parts < 1 ||
            parts > MaxPartCount)
        {
            return false;
        }

        long cellCount =
            (long)layers * parts;

        return cellCount <= MaxCellCount;
    }

    /// <summary>
    /// 删除保存的图形数据。
    /// </summary>
    private static void RemoveSavedDocument()
    {
        Preferences.Default.Remove(
            SavedLayerCountKey);

        Preferences.Default.Remove(
            SavedPartCountKey);

        Preferences.Default.Remove(
            SavedShortCodeKey);
    }

    /// <summary>
    /// 删除当前自动保存数据。
    /// 供“新建空白图”功能使用。
    /// </summary>
    private void ClearSavedDocument()
    {
        RemoveSavedDocument();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        RestoreDocument();

        RefreshShapeGrid();
        RefreshEditorPanel();
        UpdateUndoRedoState();
        UpdateStatus();
    }

    protected override void OnDisappearing()
    {
        SaveCurrentDocument();

        base.OnDisappearing();
    }

    #endregion
}
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Theme;
using ShapeEditor.Wpf.Rendering;

namespace ShapeEditor.Wpf.Controls;

/// <summary>
/// 单元格位置参数。
/// </summary>
public sealed class CellPositionEventArgs : EventArgs
{
    public CellPositionEventArgs(
        int layer,
        int part)
    {
        Layer = layer;
        Part = part;
    }

    /// <summary>
    /// 层号，0 是最低层。
    /// </summary>
    public int Layer { get; }

    /// <summary>
    /// 象限号，0 是该层第一个象限。
    /// </summary>
    public int Part { get; }
}

/// <summary>
/// 竖式图画布。
///
/// 文档只构建一次 DrawingGroup，平移和缩放通过变换完成，
/// 因此拖动与滚轮不会重建绘制内容。
/// </summary>
public sealed class ShapeGridView : FrameworkElement
{
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(ShapeDocument),
            typeof(ShapeGridView),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(ShapeGridView),
            new FrameworkPropertyMetadata(
                1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PanXProperty =
        DependencyProperty.Register(
            nameof(PanX),
            typeof(double),
            typeof(ShapeGridView),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PanYProperty =
        DependencyProperty.Register(
            nameof(PanY),
            typeof(double),
            typeof(ShapeGridView),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedLayerProperty =
        DependencyProperty.Register(
            nameof(SelectedLayer),
            typeof(int?),
            typeof(ShapeGridView),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSelectionChanged));

    public static readonly DependencyProperty SelectedPartProperty =
        DependencyProperty.Register(
            nameof(SelectedPart),
            typeof(int?),
            typeof(ShapeGridView),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnSelectionChanged));

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(
            nameof(IsInteractive),
            typeof(bool),
            typeof(ShapeGridView),
            new PropertyMetadata(true));

    public static readonly DependencyProperty PaintEnabledProperty =
        DependencyProperty.Register(
            nameof(PaintEnabled),
            typeof(bool),
            typeof(ShapeGridView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowGridProperty =
        DependencyProperty.Register(
            nameof(ShowGrid),
            typeof(bool),
            typeof(ShapeGridView),
            new PropertyMetadata(true, OnDocumentChanged));

    private DrawingGroup? _drawing;

    private bool _isPainting;

    private bool _isPanning;

    private Point _panStart;

    private double _panOriginX;

    private double _panOriginY;

    private int _lastPaintedLayer = -1;

    private int _lastPaintedPart = -1;

    public ShapeGridView()
    {
        Focusable = true;

        ClipToBounds = true;

        SizeChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>
    /// 绘制发生前触发一次，用于重新构建画布。
    /// </summary>
    public event EventHandler<CellPositionEventArgs>? CellPaintRequested;

    /// <summary>
    /// 未激活画笔时点击单元格。
    /// </summary>
    public event EventHandler<CellPositionEventArgs>? CellSelected;

    /// <summary>
    /// 一次连续绘制开始（按下左键）。用于只记录一次撤销历史。
    /// </summary>
    public event EventHandler? StrokeStarted;

    /// <summary>
    /// 一次连续绘制结束（松开左键）。
    /// </summary>
    public event EventHandler? StrokeEnded;

    public ShapeDocument? Document
    {
        get => (ShapeDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double PanX
    {
        get => (double)GetValue(PanXProperty);
        set => SetValue(PanXProperty, value);
    }

    public double PanY
    {
        get => (double)GetValue(PanYProperty);
        set => SetValue(PanYProperty, value);
    }

    public int? SelectedLayer
    {
        get => (int?)GetValue(SelectedLayerProperty);
        set => SetValue(SelectedLayerProperty, value);
    }

    public int? SelectedPart
    {
        get => (int?)GetValue(SelectedPartProperty);
        set => SetValue(SelectedPartProperty, value);
    }

    /// <summary>
    /// 为 false 时只显示，不响应鼠标。
    /// </summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>
    /// 是否已选中画笔。为 true 时按住左键拖动会连续绘制。
    /// </summary>
    public bool PaintEnabled
    {
        get => (bool)GetValue(PaintEnabledProperty);
        set => SetValue(PaintEnabledProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// 文档内容被就地修改后调用，重新构建缓存。
    /// </summary>
    public void RefreshGrid()
    {
        _drawing = null;

        InvalidateVisual();
    }

    /// <summary>
    /// 恢复为适应屏幕：缩放归 1、平移归零。
    /// </summary>
    public void FitToView()
    {
        Zoom = 1;

        PanX = 0;

        PanY = 0;

        InvalidateVisual();
    }

    protected override void OnRender(
        DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawingContext.DrawRectangle(
            Brushes.White,
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        ShapeDocument? document = Document;

        if (document is null ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        _drawing ??= ShapeGridDrawing.Build(
            document,
            GridMetrics.CellWidth,
            GridMetrics.CellHeight,
            SelectedLayer,
            SelectedPart,
            ShowGrid);

        (double originX, double originY, double scale) = GetLayout(document);

        if (scale <= 0)
        {
            return;
        }

        drawingContext.PushTransform(
            new TranslateTransform(originX, originY));

        drawingContext.PushTransform(
            new ScaleTransform(scale, scale));

        drawingContext.DrawDrawing(_drawing);

        drawingContext.Pop();

        drawingContext.Pop();
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!IsInteractive)
        {
            return;
        }

        Focus();

        if (Keyboard.IsKeyDown(Key.Space))
        {
            StartPan(e.GetPosition(this));

            e.Handled = true;

            return;
        }

        if (!TryGetCell(
                e.GetPosition(this),
                out int layer,
                out int part))
        {
            return;
        }

        if (PaintEnabled)
        {
            _isPainting = true;
            _lastPaintedLayer = layer;
            _lastPaintedPart = part;

            CaptureMouse();

            StrokeStarted?.Invoke(this, EventArgs.Empty);

            CellPaintRequested?.Invoke(
                this,
                new CellPositionEventArgs(layer, part));

            e.Handled = true;

            return;
        }

        CellSelected?.Invoke(
            this,
            new CellPositionEventArgs(layer, part));

        e.Handled = true;
    }

    protected override void OnMouseMove(
        MouseEventArgs e)
    {
        base.OnMouseMove(e);

        Point position = e.GetPosition(this);

        if (_isPanning)
        {
            PanX = _panOriginX + position.X - _panStart.X;

            PanY = _panOriginY + position.Y - _panStart.Y;

            return;
        }

        if (!_isPainting ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!TryGetCell(
                position,
                out int layer,
                out int part))
        {
            return;
        }

        if (layer == _lastPaintedLayer &&
            part == _lastPaintedPart)
        {
            return;
        }

        _lastPaintedLayer = layer;
        _lastPaintedPart = part;

        CellPaintRequested?.Invoke(
            this,
            new CellPositionEventArgs(layer, part));
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_isPainting)
        {
            _isPainting = false;

            _lastPaintedLayer = -1;
            _lastPaintedPart = -1;

            StrokeEnded?.Invoke(this, EventArgs.Empty);

            ReleaseMouseCapture();
        }

        if (_isPanning)
        {
            _isPanning = false;

            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsInteractive)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            StartPan(e.GetPosition(this));

            e.Handled = true;
        }
    }

    protected override void OnMouseUp(
        MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton == MouseButton.Middle &&
            _isPanning)
        {
            _isPanning = false;

            ReleaseMouseCapture();

            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(
        MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!IsInteractive ||
            Document is null)
        {
            return;
        }

        Point position = e.GetPosition(this);

        double oldScale = GetLayout(Document).scale;

        if (oldScale <= 0)
        {
            return;
        }

        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;

        double newZoom = Math.Clamp(
            Zoom * factor,
            GridMetrics.MinZoom,
            GridMetrics.MaxZoom);

        if (Math.Abs(newZoom - Zoom) < 0.0001)
        {
            return;
        }

        double fitScale = GridMetrics.GetFitScale(
            ActualWidth,
            ActualHeight,
            Document.PartCount,
            Document.LayerCount);

        double newScale = fitScale * newZoom;

        /*
         * 让光标下的图形位置保持不动：
         * 先换算成局部坐标，再反推出新的平移量。
         */
        double localX = (position.X - GetLayout(Document).originX) / oldScale;
        double localY = (position.Y - GetLayout(Document).originY) / oldScale;

        double centerX = (ActualWidth - Document.PartCount * GridMetrics.CellWidth * newScale) / 2;
        double centerY = (ActualHeight - Document.LayerCount * GridMetrics.CellHeight * newScale) / 2;

        PanX = position.X - centerX - localX * newScale;
        PanY = position.Y - centerY - localY * newScale;

        Zoom = newZoom;

        e.Handled = true;
    }

    /// <summary>
    /// 把鼠标位置换算成层号与象限号。
    /// </summary>
    public bool TryGetCell(
        Point point,
        out int layer,
        out int part)
    {
        layer = -1;
        part = -1;

        ShapeDocument? document = Document;

        if (document is null)
        {
            return false;
        }

        (double originX, double originY, double scale) = GetLayout(document);

        if (scale <= 0)
        {
            return false;
        }

        double localX = (point.X - originX) / scale;
        double localY = (point.Y - originY) / scale;

        /*
         * 必须在转 int 之前判断负数，(int)(-0.2) 会得到 0。
         */
        if (localX < 0 ||
            localY < 0 ||
            localX >= document.PartCount * GridMetrics.CellWidth ||
            localY >= document.LayerCount * GridMetrics.CellHeight)
        {
            return false;
        }

        part = (int)(localX / GridMetrics.CellWidth);

        int visualRow = (int)(localY / GridMetrics.CellHeight);

        layer = document.LayerCount - 1 - visualRow;

        return true;
    }

    private void StartPan(
        Point position)
    {
        _isPanning = true;

        _panStart = position;

        _panOriginX = PanX;
        _panOriginY = PanY;

        CaptureMouse();
    }

    private (double originX, double originY, double scale) GetLayout(
        ShapeDocument document)
    {
        double fitScale = GridMetrics.GetFitScale(
            ActualWidth,
            ActualHeight,
            document.PartCount,
            document.LayerCount);

        double scale = fitScale * Zoom;

        double width = document.PartCount * GridMetrics.CellWidth * scale;
        double height = document.LayerCount * GridMetrics.CellHeight * scale;

        return (
            (ActualWidth - width) / 2 + PanX,
            (ActualHeight - height) / 2 + PanY,
            scale);
    }

    private static void OnDocumentChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        ((ShapeGridView)d)._drawing = null;

        ((ShapeGridView)d).InvalidateVisual();
    }

    private static void OnSelectionChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        ((ShapeGridView)d)._drawing = null;

        ((ShapeGridView)d).InvalidateVisual();
    }
}

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区画布的显示变换：双指捏合缩放、单指拖空白处平移、右下角缩放按钮。
///
/// 缩放与平移都只是显示变换：
/// 节点逻辑坐标（X/Y）、卡片布局，以及导出图片的分辨率都不受影响。
/// 导出走 <see cref="WorkspaceImageExporter"/> 的离屏渲染，固定按 1 倍（30px 单元格）输出，
/// 不读取这里的比例与平移量。
///
/// 缩放期间不处理节点拖动与点选，缩放结束后保持当前选中状态。
/// </summary>
public partial class WorkspacePage
{
    /// <summary>画布最小缩放比例。</summary>
    private const double MinCanvasScale = 0.4;

    /// <summary>画布最大缩放比例。</summary>
    private const double MaxCanvasScale = 2.0;

    /// <summary>缩放按钮每点一次的步进。</summary>
    private const double ScaleStep = 0.15;

    /// <summary>
    /// 捏合结束后继续忽略拖动 / 点击的时间。
    /// 手指抬起时可能还有残留回调，留一点余量避免误选或误拖。
    /// </summary>
    private static readonly TimeSpan GestureCooldown = TimeSpan.FromMilliseconds(250);

    /// <summary>当前画布缩放比例，只影响显示。</summary>
    private double _canvasScale = 1.0;

    /// <summary>是否正在双指捏合。</summary>
    private bool _pinching;

    /// <summary>最近一次捏合结束的时间。</summary>
    private DateTime _pinchEndedAt = DateTime.MinValue;

    /// <summary>
    /// 单指平移画布的状态机，与编辑器「移动模式」共用 <see cref="PanViewport"/>。
    /// </summary>
    private readonly PanViewport _panViewport = new();

    /// <summary>缩放中，或刚结束 <see cref="GestureCooldown"/> 之内。</summary>
    private bool IsPinching =>
        _pinching ||
        DateTime.UtcNow - _pinchEndedAt < GestureCooldown;

    #region 视口

    /// <summary>
    /// 视口尺寸变化：把滚动宿主固定为视口大小。
    ///
    /// ScrollView 在这里只负责裁剪，不参与滚动；画布在宿主内左上角对齐，
    /// 平移与缩放都由画布自身的 Translation / Scale 完成。
    /// </summary>
    private void OnCanvasViewportSizeChanged(
        object? sender,
        EventArgs e)
    {
        if (CanvasViewport.Width <= 0 || CanvasViewport.Height <= 0)
        {
            return;
        }

        CanvasHost.WidthRequest = CanvasViewport.Width;
        CanvasHost.HeightRequest = CanvasViewport.Height;

        // 视口变了，平移量要重新夹回可见范围
        SetCanvasPan(Canvas.TranslationX, Canvas.TranslationY);
    }

    #endregion

    #region 缩放

    /// <summary>
    /// 应用画布显示缩放。
    ///
    /// 只改画布容器的 Scale（锚点 0.5，绕画布中心）；
    /// 同时把「缩放前视口中心对应的内容点」重新放回视口中心，
    /// 这样点按钮或捏合时画面不会突然跳位。节点坐标与卡片布局一律不动，导出也不读取这个比例。
    /// </summary>
    private void ApplyCanvasScale(
        double scale)
    {
        // 按 0.001 收敛，避免 1.15 + 0.15 这类浮点误差让比例显示成 99%
        double target = Math.Round(scale, 3);

        // 缩放前视口中心对应的内容坐标
        double anchorX = ContentAtViewportCenter(
            CanvasViewport.Width,
            Canvas.WidthRequest,
            _canvasScale,
            Canvas.TranslationX);

        double anchorY = ContentAtViewportCenter(
            CanvasViewport.Height,
            Canvas.HeightRequest,
            _canvasScale,
            Canvas.TranslationY);

        _canvasScale = Math.Clamp(
            target,
            MinCanvasScale,
            MaxCanvasScale);

        Canvas.Scale = _canvasScale;

        // 把同一个内容点放回视口中心
        Canvas.TranslationX = TranslationForContentPoint(
            anchorX,
            CanvasViewport.Width,
            Canvas.WidthRequest,
            _canvasScale);

        Canvas.TranslationY = TranslationForContentPoint(
            anchorY,
            CanvasViewport.Height,
            Canvas.HeightRequest,
            _canvasScale);

        UpdateZoomLabel();

        // 比例变了，可平移范围也跟着变，重新夹一次
        SetCanvasPan(Canvas.TranslationX, Canvas.TranslationY);
    }

    /// <summary>
    /// 视口中心横向 / 纵向对应的内容坐标。
    ///
    /// 画布以中心为锚点缩放，所以视觉左上角比布局位置偏移 content × (1 − scale) / 2。
    /// </summary>
    private static double ContentAtViewportCenter(
        double viewport,
        double content,
        double scale,
        double offset)
    {
        if (viewport <= 0 || content <= 0 || scale <= 0)
        {
            return 0;
        }

        double visualLeft = content * (1 - scale) / 2 + offset;

        return (viewport / 2 - visualLeft) / scale;
    }

    /// <summary>让指定内容坐标落在视口中心所需的平移量。</summary>
    private static double TranslationForContentPoint(
        double contentPoint,
        double viewport,
        double content,
        double scale)
    {
        if (viewport <= 0 || content <= 0 || scale <= 0)
        {
            return 0;
        }

        return viewport / 2
            - content * (1 - scale) / 2
            - contentPoint * scale;
    }

    /// <summary>按钮旁显示当前比例，例如 100%。</summary>
    private void UpdateZoomLabel()
    {
        ZoomLabel.Text = $"{_canvasScale * 100:0}%";
    }

    /// <summary>
    /// 双指捏合缩放。
    ///
    /// e.Scale 是相对上一次回调的增量比例，累乘到当前比例上，
    /// 再夹到 0.4–2.0；整个过程只改显示比例，不碰节点坐标与选中状态。
    ///
    /// 画布、画布宿主与节点卡片共用这一个处理函数：
    /// 触摸落在哪一层，就由哪一层收到捏合事件。
    /// </summary>
    private void OnCanvasPinchUpdated(
        object? sender,
        PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                BeginPinch();

                return;

            case GestureStatus.Running:
                // 个别平台会漏发 Started（或先给出 Running），这里补一次抢占
                if (!_pinching)
                {
                    BeginPinch();
                }

                if (!double.IsFinite(e.Scale) || e.Scale <= 0)
                {
                    return;
                }

                ApplyCanvasScale(_canvasScale * e.Scale);

                return;

            default:
                // Completed / Canceled：只结束捏合，当前选中状态保持不变
                EndPinch();

                return;
        }
    }

    /// <summary>
    /// 捏合开始：抢占当前手势。
    ///
    /// 双指落在节点卡片上时，卡片自己的 Pan 往往在第一根手指落下时就已经开始拖动，
    /// 捏合事件反而是后到的。这里立刻把那次拖动整体撤销（节点放回拖动起点），
    /// 保证缩放过程中没有拖了一半的节点，之后的 Pan 回调也会被 IsPinching 挡住。
    /// </summary>
    private void BeginPinch()
    {
        // 先置位再撤销：撤销过程中触发的 Pan / Tap 回调要能立刻看到「正在捏合」
        _pinching = true;

        CancelNodeDrag();

        // 单指平移量一起作废，避免平移与缩放叠加
        _panViewport.End();
    }

    /// <summary>
    /// 捏合结束：进入 <see cref="GestureCooldown"/> 冷却。
    /// 冷却期内 <see cref="IsPinching"/> 仍为真，节点拖动与点选都会被忽略。
    /// </summary>
    private void EndPinch()
    {
        _pinching = false;

        _pinchEndedAt = DateTime.UtcNow;
    }

    /// <summary>缩小一档（Button.Clicked，不使用双击手势）。</summary>
    private void OnZoomOut(
        object? sender,
        EventArgs e)
    {
        ApplyCanvasScale(_canvasScale - ScaleStep);
    }

    /// <summary>放大一档（Button.Clicked，不使用双击手势）。</summary>
    private void OnZoomIn(
        object? sender,
        EventArgs e)
    {
        ApplyCanvasScale(_canvasScale + ScaleStep);
    }

    /// <summary>重置回 100%。</summary>
    private void OnZoomReset(
        object? sender,
        EventArgs e)
    {
        ApplyCanvasScale(1.0);
    }

    #endregion

    #region 平移

    /// <summary>
    /// 单指在空白处拖动：整体平移画布。
    ///
    /// 与编辑器「移动模式」共用 <see cref="PanViewport"/>：
    /// PanUpdated 只提供相对起点的累计位移，这里把 (TotalX, TotalY)
    /// 当作一串从原点出发的触点喂给同一个状态机，取出逐帧位移。
    /// 平移只改画布的 Translation，节点逻辑坐标不变。
    /// </summary>
    private void OnCanvasPanUpdated(
        object? sender,
        PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                // 捏合中（含刚结束的冷却期）、或正在拖节点时不开始平移画布
                if (IsPinching || IsDraggingNode)
                {
                    _panViewport.End();

                    return;
                }

                _panViewport.Begin(new Point(0, 0));

                return;

            case GestureStatus.Running:
                // 手势被抢占（第二根手指落下开始捏合、或节点拖动接管）：直接作废，不留半截平移
                if (IsPinching || IsDraggingNode)
                {
                    _panViewport.End();

                    return;
                }

                if (!_panViewport.TryMove(
                        new Point(e.TotalX, e.TotalY),
                        out double deltaX,
                        out double deltaY))
                {
                    return;
                }

                SetCanvasPan(
                    Canvas.TranslationX + deltaX,
                    Canvas.TranslationY + deltaY);

                return;

            default:
                _panViewport.End();

                return;
        }
    }

    /// <summary>
    /// 设置画布平移量，并夹在可见范围内。
    ///
    /// 平移量是画布相对左上角的显示偏移：0 表示看画布左上角（和旧版滚动到 0 一致）。
    /// 内容比视口大时可以平移到内容边缘；比视口小时直接居中，避免把画布拖出视口。
    /// </summary>
    private void SetCanvasPan(
        double x,
        double y)
    {
        Canvas.TranslationX = ClampPanAxis(
            x,
            CanvasViewport.Width,
            Canvas.WidthRequest,
            _canvasScale);

        Canvas.TranslationY = ClampPanAxis(
            y,
            CanvasViewport.Height,
            Canvas.HeightRequest,
            _canvasScale);
    }

    /// <summary>
    /// 单个方向上的平移夹取。
    ///
    /// 画布按 1 倍尺寸布局、以中心为锚点缩放，
    /// 所以视觉左上角比布局位置偏移 content × (1 − scale) / 2；
    /// 夹取上下限时要把这段偏移算进去，否则放大后画布边缘会露白。
    /// </summary>
    private static double ClampPanAxis(
        double offset,
        double viewport,
        double content,
        double scale)
    {
        if (viewport <= 0 || content <= 0 || scale <= 0)
        {
            return 0;
        }

        double scaled = content * scale;

        // 内容比视口小：居中显示，居中位置与缩放比例无关
        if (scaled <= viewport)
        {
            return (viewport - content) / 2;
        }

        double half = content * (scale - 1) / 2;

        // 上界：内容左边贴住视口左边；下界：内容右边贴住视口右边
        double min = viewport - scaled + half;

        return Math.Clamp(offset, min, half);
    }

    #endregion
}

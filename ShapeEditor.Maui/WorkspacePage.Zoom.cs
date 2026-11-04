namespace ShapeEditor.Maui;

/// <summary>
/// 工作区画布的显示变换：双指捏合缩放、右下角缩放按钮，
/// 以及「本次手势不要被 ScrollView 拦截」的平台请求。
///
/// 画布只有一个坐标系：
/// 节点逻辑坐标 = 画布布局坐标（不缩放、不平移）。
/// 缩放只改 <c>Canvas.Scale</c> 与 <c>ScaledHost</c> 的请求尺寸，
/// 平移与滚动边界全部交给 <c>CanvasScroll</c>（ScrollView），
/// 画布侧不再有任何手写 Translation。
///
/// 导出走 <see cref="WorkspaceImageExporter"/> 的离屏渲染，固定按 1 倍（30px 单元格）输出，
/// 不读取这里的比例，也不读取滚动位置。
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
    /// 手指抬起时可能还有残留回调，留一点余量避免误拖或误选。
    /// </summary>
    private static readonly TimeSpan GestureCooldown = TimeSpan.FromMilliseconds(250);

    /// <summary>当前画布缩放比例，只影响显示。</summary>
    private double _canvasScale = 1.0;

    /// <summary>画布逻辑宽度（未缩放），由 RenderCanvas 维护。</summary>
    private double _canvasContentWidth;

    /// <summary>画布逻辑高度（未缩放），由 RenderCanvas 维护。</summary>
    private double _canvasContentHeight;

    /// <summary>是否正在双指捏合。</summary>
    private bool _pinching;

    /// <summary>最近一次捏合结束的时间。</summary>
    private DateTime _pinchEndedAt = DateTime.MinValue;

    /// <summary>缩放后待补的滚动位置，等新尺寸生效后再滚一次。</summary>
    private bool _hasPendingScroll;

    /// <summary>是否已经排过延迟兜底，避免捏合时每帧都排一个回调。</summary>
    private bool _scrollFallbackScheduled;

    private double _pendingScrollX;

    private double _pendingScrollY;

#if ANDROID
    /// <summary>
    /// 已挂上「多指按下」监听的画布元素（画布 + 滚动宿主）。
    /// 触摸落在哪一层，就由哪一层的原生视图收到 ACTION_POINTER_DOWN。
    /// </summary>
    private readonly Dictionary<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> _touchHooks = new();
#endif

    /// <summary>缩放中，或刚结束 <see cref="GestureCooldown"/> 之内。</summary>
    private bool IsPinching =>
        _pinching ||
        DateTime.UtcNow - _pinchEndedAt < GestureCooldown;

    #region 尺寸

    /// <summary>
    /// 记录逻辑画布尺寸，并按当前比例同步滚动宿主的请求尺寸。
    ///
    /// 画布自身仍按 1 倍尺寸布局（节点坐标就是布局坐标），
    /// 视觉大小由 <c>Canvas.Scale</c> 决定；
    /// <c>ScaledHost</c> 的请求尺寸 = 逻辑尺寸 × 比例，
    /// 这样 ScrollView 的滚动范围与缩放后看到的画布完全一致，
    /// 不需要再手写平移量，也不需要自己夹取边界。
    /// </summary>
    private void SetCanvasContentSize(
        double width,
        double height)
    {
        _canvasContentWidth = Math.Max(1, width);
        _canvasContentHeight = Math.Max(1, height);

        Canvas.WidthRequest = _canvasContentWidth;
        Canvas.HeightRequest = _canvasContentHeight;
        Canvas.Scale = _canvasScale;

        UpdateScaledHostSize();
    }

    /// <summary>让滚动宿主覆盖「缩放后的画布」，不改画布自身的布局。</summary>
    private void UpdateScaledHostSize()
    {
        ScaledHost.WidthRequest = Math.Max(1, _canvasContentWidth * _canvasScale);
        ScaledHost.HeightRequest = Math.Max(1, _canvasContentHeight * _canvasScale);
    }

    /// <summary>
    /// ScaledHost 拿到新尺寸：原生 ScrollView 此时才知道新的滚动范围，
    /// 把缩放时暂存的滚动位置补上。
    /// </summary>
    private void OnScaledHostSizeChanged(
        object? sender,
        EventArgs e)
    {
        ApplyPendingScroll();
    }

    #endregion

    #region 缩放

    /// <summary>
    /// 应用画布显示缩放。
    ///
    /// 锚点是视口中心：缩放前后，同一个逻辑内容点始终落在视口中心。
    /// 实现上不挪画布，只改 <c>Canvas.Scale</c> 与 <c>ScaledHost</c> 请求尺寸，
    /// 再让 ScrollView 滚到「内容点 × 新比例 − 视口中心」，
    /// 滚动范围由 ScrollView 自己夹取（内容比视口小时即为 0，不会露负滚动）。
    ///
    /// 节点坐标与卡片布局一律不动，导出也不读取这个比例。
    /// </summary>
    private void ApplyCanvasScale(
        double scale)
    {
        // 按 0.001 收敛，避免 1.15 + 0.15 这类浮点误差让比例显示成 99%
        double target = Math.Clamp(
            Math.Round(scale, 3),
            MinCanvasScale,
            MaxCanvasScale);

        if (Math.Abs(target - _canvasScale) < 0.0005)
        {
            UpdateZoomLabel();

            return;
        }

        // 缩放前视口中心对应的逻辑画布坐标（换算要用缩放前的比例）
        Point anchor = ViewportToCanvas(new Point(
            CanvasViewport.Width / 2,
            CanvasViewport.Height / 2));

        _canvasScale = target;

        Canvas.Scale = _canvasScale;

        UpdateScaledHostSize();

        UpdateZoomLabel();

        RequestScrollToContentCenter(anchor);
    }

    /// <summary>
    /// 缩放后把指定逻辑内容点放回视口中心。
    ///
    /// 只记录目标滚动位置：ScaledHost 的新尺寸要等下一次布局才落到原生 ScrollView 上，
    /// 立刻滚会被旧的内容范围夹住，所以先排一次主线程回调，
    /// 再由 <see cref="OnScaledHostSizeChanged"/> 与一次延迟回调补上。
    /// </summary>
    private void RequestScrollToContentCenter(
        Point contentPoint)
    {
        if (CanvasViewport.Width <= 0 || CanvasViewport.Height <= 0)
        {
            return;
        }

        _pendingScrollX = Math.Max(
            0,
            contentPoint.X * _canvasScale - CanvasViewport.Width / 2);

        _pendingScrollY = Math.Max(
            0,
            contentPoint.Y * _canvasScale - CanvasViewport.Height / 2);

        _hasPendingScroll = true;

        Dispatcher.Dispatch(ApplyPendingScroll);

        // 布局偶尔会比主线程回调更晚，留一次兜底；读的是当前字段，不会滚到旧目标
        if (!_scrollFallbackScheduled)
        {
            _scrollFallbackScheduled = true;

            Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(120),
                ApplyPendingScrollAndClear);
        }
    }

    /// <summary>有暂存目标时滚过去（不清标记，等尺寸变化或兜底再滚一次）。</summary>
    private void ApplyPendingScroll()
    {
        if (!_hasPendingScroll)
        {
            return;
        }

        ScrollToPending();
    }

    /// <summary>兜底：再滚一次并清掉暂存目标。</summary>
    private void ApplyPendingScrollAndClear()
    {
        _scrollFallbackScheduled = false;

        if (!_hasPendingScroll)
        {
            return;
        }

        _hasPendingScroll = false;

        ScrollToPending();
    }

    /// <summary>按当前暂存目标滚动（不判断标记）。</summary>
    private void ScrollToPending()
    {
        if (CanvasScroll.Handler is null)
        {
            return;
        }

        _ = CanvasScroll.ScrollToAsync(
            _pendingScrollX,
            _pendingScrollY,
            false);
    }

    /// <summary>
    /// 视口坐标 → 逻辑画布坐标（拖放落点 / 点选判断用）。
    ///
    /// 视口坐标加上 ScrollView 的滚动量，就是「缩放后的内容坐标」；
    /// 再除以比例得到逻辑画布坐标。
    /// 画布在 ScaledHost 里贴左上角摆放，逻辑画布原点偏移为 0，
    /// 所以这里不需要再平移。
    /// </summary>
    private Point ViewportToCanvas(
        Point viewportPoint)
    {
        double scale = _canvasScale > 0
            ? _canvasScale
            : 1d;

        return new Point(
            (viewportPoint.X + CanvasScroll.ScrollX) / scale,
            (viewportPoint.Y + CanvasScroll.ScrollY) / scale);
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
    /// 画布与节点卡片共用这一个处理函数：
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

        // 捏合期间也不让 ScrollView 抢走触摸
        SuppressScrollIntercept(Canvas);
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

    #region 手势让路

    /// <summary>
    /// 挂上画布与滚动宿主的原生触摸监听：第二根手指一落下就让 ScrollView 让路。
    ///
    /// 捏合手势要等手指移动、两指间距变化超过阈值后才会开始，
    /// 而 ScrollView 在同一帧的移动里就可能已经把触摸抢走；
    /// 所以在原生的 ACTION_POINTER_DOWN 上先置「不要拦截本次手势」。
    /// 画布缩得比视口小时，空白处落在滚动宿主上，所以两层都要挂。
    /// </summary>
    private void AttachCanvasTouchSuppression()
    {
#if ANDROID
        DetachCanvasTouchSuppression();

        AttachTouchSuppression(Canvas);

        AttachTouchSuppression(ScaledHost);
#endif
    }

    /// <summary>给一个画布元素的平台视图挂上「第二根手指按下」的监听。</summary>
    private void AttachTouchSuppression(
        VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is not Android.Views.View view)
        {
            return;
        }

        void Handler(
            object? sender,
            Android.Views.View.TouchEventArgs e)
        {
            if (e.Event is { } motion &&
                motion.ActionMasked == Android.Views.MotionEventActions.PointerDown)
            {
                SuppressScrollIntercept(element);
            }
        }

        view.Touch += Handler;

        _touchHooks[view] = Handler;
#endif
    }

    /// <summary>摘掉所有原生触摸监听。</summary>
    private void DetachCanvasTouchSuppression()
    {
#if ANDROID
        foreach (KeyValuePair<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> pair in _touchHooks)
        {
            pair.Key.Touch -= pair.Value;
        }

        _touchHooks.Clear();
#endif
    }

    /// <summary>
    /// 请求外层滚动容器在本次手势中不要拦截触摸（仅 Android 需要）。
    ///
    /// 节点卡片在手指按下、画布在捏合开始时调用。
    /// Android 的 ViewGroup 会把「不要拦截」逐级传给所有父容器，
    /// 所以 ScrollView（Orientation=Both 时还包括它内层的横向滚动容器）
    /// 都不会再把手势从卡片 / 画布手里抢走。
    /// 这个标志由 Android 在下一根手指按下时自动清零，不需要手工恢复；
    /// 也因此没有退回「Canvas.Translation + card.Translation」的双写方案。
    /// </summary>
    private static void SuppressScrollIntercept(
        VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is Android.Views.View view &&
            view.Parent is Android.Views.IViewParent parent)
        {
            parent.RequestDisallowInterceptTouchEvent(true);
        }
#endif
    }

    #endregion
}

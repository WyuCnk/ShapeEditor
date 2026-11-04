using Microsoft.Maui.Graphics;

namespace ShapeEditor.Maui;

/// <summary>
/// 视口单指平移的公共状态机。
///
/// 编辑器（<see cref="MainPage"/>）的「移动模式」与工作区画布共用这一份实现，
/// 不再各自维护「上一次触点」与「是否正在平移」：
/// 编辑器把位移加到图形 Drawable 的 PanX/PanY 上，
/// 工作区把位移加到画布容器的 TranslationX/TranslationY 上。
/// 两边都只改变显示位置，图形 / 节点的逻辑坐标保持不变。
/// </summary>
public sealed class PanViewport
{
    /// <summary>上一次触点；只有 <see cref="Begin"/> 之后才有意义。</summary>
    private Point _last;

    /// <summary>是否已经开始平移。第一次移动只用来记录起点，不产生位移。</summary>
    public bool IsPanning { get; private set; }

    /// <summary>开始一次平移，把当前触点记为起点。</summary>
    public void Begin(
        Point point)
    {
        _last = point;

        IsPanning = true;
    }

    /// <summary>
    /// 触点移动到新位置：把与上一次触点的差值作为本次视口位移。
    ///
    /// 未开始平移时返回 false；位移为 0 时同样返回 false，避免无意义的刷新。
    /// </summary>
    public bool TryMove(
        Point point,
        out double deltaX,
        out double deltaY)
    {
        deltaX = 0;
        deltaY = 0;

        if (!IsPanning)
        {
            return false;
        }

        double dx = point.X - _last.X;

        double dy = point.Y - _last.Y;

        if (dx == 0 && dy == 0)
        {
            return false;
        }

        _last = point;

        deltaX = dx;
        deltaY = dy;

        return true;
    }

    /// <summary>结束本次平移；下一次拖动需要重新 <see cref="Begin"/>。</summary>
    public void End()
    {
        IsPanning = false;
    }
}

using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区「交换」操作。
///
/// 交互与 Web / WPF 一致：选中第一个节点后点工具条「交换」，再点另一个节点完成；
/// 再点一次「交换」或点画布空白处取消。交换生成两个结果节点，
/// 幂等键分别为 exchange1:{a}|{b} / exchange2:{a}|{b}，重复执行时定位到已有节点。
///
/// 结果节点的创建、输入记录、连线与切换规则后的重算全部由 Core 的
/// <see cref="WorkspaceStore.ComputeExchange"/> / <see cref="WorkspaceStore.RecomputeAll"/>
/// 负责，MAUI 只保留两次点击流程与摆放坐标。
/// </summary>
public partial class WorkspacePage
{
    /// <summary>交换结果相对源节点的水平偏移。</summary>
    private const double ExchangeOffset = 260;

    /// <summary>交换结果相对源节点的垂直偏移。</summary>
    private const double ExchangeRowOffset = 420;

    /// <summary>左侧结果允许的最小 X，避免负坐标把画布撑得太宽。</summary>
    private const double ExchangeMinX = 24;

    /// <summary>交换第一步选中的节点 Id；为空表示不在交换选择状态。</summary>
    private string? _exchangeFirstId;

    /// <summary>
    /// 工具条「交换」：第一次点击记住当前选中节点，第二次点击取消。
    /// 这里用普通 Button.Clicked，不引入任何双击手势。
    /// </summary>
    private void OnExchangeNode(
        object? sender,
        EventArgs e)
    {
        if (_exchangeFirstId is not null)
        {
            CancelExchange();

            return;
        }

        if (_store?.SelectedNode is not WorkspaceNode node ||
            _store.FindNode(node.Id) is null)
        {
            return;
        }

        // 「堆叠」和「交换」都占用第二次节点点击，进入交换前先退出堆叠状态
        CancelStack();

        _exchangeFirstId = node.Id;

        Render();
    }

    /// <summary>取消交换选择，恢复状态栏。</summary>
    private void CancelExchange()
    {
        if (_exchangeFirstId is null)
        {
            return;
        }

        _exchangeFirstId = null;

        Render();
    }

    /// <summary>
    /// 交换第二步：第二个节点点下后按两个节点计算。
    ///
    /// 摆放规则与 Web 版一致：左结果 X − 260（最小 24）、右结果 X + 260，
    /// 两个结果的 Y 都是源 Y + 420（源取第二个点击的节点）。
    /// </summary>
    private void CompleteExchange(
        WorkspaceNode second)
    {
        if (_store is null || _exchangeFirstId is null)
        {
            return;
        }

        if (second.Id == _exchangeFirstId)
        {
            Show("请点另一个节点作为第二个输入。");

            return;
        }

        string firstId = _exchangeFirstId;

        _exchangeFirstId = null;

        if (_store.FindNode(firstId) is null)
        {
            Show("第一个输入节点已不存在，请重新选择。");

            Render();

            return;
        }

        WorkspaceNodeBox? secondBox = FindBox(second.Id);

        double sourceX = secondBox?.Bounds.X ?? WorkspaceLayout.Margin;

        double sourceY = secondBox?.Bounds.Y ?? WorkspaceLayout.Margin;

        (NodeActionResult First, NodeActionResult Second) pair = (
            new NodeActionResult(false, null, "交换失败。"),
            new NodeActionResult(false, null, "交换失败。"));

        string? error = null;

        // 创建节点、补边、定位会连续触发 Changed，先压住整页重绘，结束时只 Render 一次
        bool wasRendering = _rendering;

        _rendering = true;

        try
        {
            pair = ComputeExchange(firstId, second.Id);

            if (pair.First.Node is not null)
            {
                _store.SetNodePosition(
                    pair.First.Node.Id,
                    Math.Max(ExchangeMinX, sourceX - ExchangeOffset),
                    sourceY + ExchangeRowOffset);

                _store.SelectNode(pair.First.Node);
            }

            if (pair.Second.Node is not null)
            {
                _store.SetNodePosition(
                    pair.Second.Node.Id,
                    sourceX + ExchangeOffset,
                    sourceY + ExchangeRowOffset);
            }
        }
        catch (Exception exception)
        {
            error = "交换失败：" + exception.Message;
        }
        finally
        {
            _rendering = wasRendering;
        }

        Render();

        if (error is not null)
        {
            Show(error);

            return;
        }

        if (pair.First.Node is null)
        {
            Show(string.IsNullOrWhiteSpace(pair.First.Message)
                ? "交换失败。"
                : pair.First.Message);

            return;
        }

        // 命中的是已有节点：已经定位并选中，再提示一次
        if (!pair.First.Created || !pair.Second.Created)
        {
            Show("该组合已交换过，已定位到结果。");
        }
    }

    /// <summary>
    /// 按两个节点计算交换结果；创建、幂等定位与输入记录均由 Core 统一实现。
    /// </summary>
    private (NodeActionResult First, NodeActionResult Second) ComputeExchange(
        string firstId,
        string secondId)
    {
        return _store!.ComputeExchange(firstId, secondId, _store.Mode);
    }
}

using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Maui;

/// <summary>
/// 工作区「交换」操作。
///
/// 交互与 Web 版一致：选中第一个节点后点工具条「交换」，再点另一个节点完成；
/// 再点一次「交换」或点画布空白处取消。交换生成两个结果节点，
/// 幂等键分别为 exchange1:{a}|{b} / exchange2:{a}|{b}，重复执行时定位到已有节点。
///
/// Core 的 WorkspaceStore 只公开了单输入 / 堆叠运算与节点创建、定位、落盘接口，
/// 没有公开的交换入口，所以这里用 Core 的 ShapeExchange 算结果，
/// 再通过 store 的公开 API 建节点、补边并触发保存。
/// </summary>
public partial class WorkspacePage
{
    /// <summary>交换结果相对源节点的水平偏移。</summary>
    private const double ExchangeOffset = 260;

    /// <summary>交换结果相对源节点的垂直偏移。</summary>
    private const double ExchangeRowOffset = 420;

    /// <summary>左侧结果允许的最小 X，避免负坐标把画布撑得太宽。</summary>
    private const double ExchangeMinX = 24;

    /// <summary>两条输入边共用的标签。</summary>
    private const string ExchangeEdgeLabel = "交换输入";

    /// <summary>交换结果节点共用的标题。</summary>
    private const string ExchangeResultLabel = "交换结果";

    private readonly ShortCodeExporter _exchangeExporter = new();

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
    /// 按两个节点计算交换结果；两个结果都已存在时直接返回已有节点。
    /// </summary>
    private (NodeActionResult First, NodeActionResult Second) ComputeExchange(
        string firstId,
        string secondId)
    {
        WorkspaceNode? a = _store!.FindNode(firstId);

        WorkspaceNode? b = _store.FindNode(secondId);

        if (a is null || b is null)
        {
            var missing = new NodeActionResult(false, null, "交换输入不存在。");

            return (missing, missing);
        }

        ShapeDocument? documentA = _store.GetEffectiveDocument(a);

        ShapeDocument? documentB = _store.GetEffectiveDocument(b);

        if (documentA is null || documentB is null)
        {
            var empty = new NodeActionResult(false, null, "交换输入缺少图形。");

            return (empty, empty);
        }

        string key1 = $"exchange1:{firstId}|{secondId}";

        string key2 = $"exchange2:{firstId}|{secondId}";

        WorkspaceNode? existing1 = _store.Nodes.FirstOrDefault(node => node.OpKey == key1);

        WorkspaceNode? existing2 = _store.Nodes.FirstOrDefault(node => node.OpKey == key2);

        // 两个结果都在就直接定位，不再重新计算（与 Web 版一致）
        if (existing1 is not null && existing2 is not null)
        {
            AddExchangeEdge(a.Id, existing1.Id);

            AddExchangeEdge(b.Id, existing1.Id);

            AddExchangeEdge(a.Id, existing2.Id);

            AddExchangeEdge(b.Id, existing2.Id);

            const string again = "该组合已交换过，已定位到结果。";

            return (
                new NodeActionResult(false, existing1, again),
                new NodeActionResult(false, existing2, again));
        }

        var exchanged = ShapeExchange.Exchange(documentA, documentB, _store.Mode);

        if (exchanged.First is null || exchanged.Second is null)
        {
            var failed = new NodeActionResult(false, null, exchanged.Error ?? "交换失败。");

            return (failed, failed);
        }

        bool createdFirst = existing1 is null;

        bool createdSecond = existing2 is null;

        // 只缺一个结果时补建缺失的那个，已有节点保持原样
        WorkspaceNode first = existing1 ?? CreateExchangeResult(
            a,
            key1,
            ApplyExchangeLimit(exchanged.First),
            firstId,
            secondId);

        WorkspaceNode second = existing2 ?? CreateExchangeResult(
            a,
            key2,
            ApplyExchangeLimit(exchanged.Second),
            firstId,
            secondId);

        // 每个结果都要有两条「交换输入」边；旧数据缺边时在这里补齐
        AddExchangeEdge(a.Id, first.Id);

        AddExchangeEdge(b.Id, first.Id);

        AddExchangeEdge(a.Id, second.Id);

        AddExchangeEdge(b.Id, second.Id);

        const string message = "已生成交换结果。";

        return (
            new NodeActionResult(createdFirst, first, message),
            new NodeActionResult(createdSecond, second, message));
    }

    /// <summary>
    /// 新建一个交换结果节点：图形、幂等键与两个输入都写在节点上，
    /// 位置暂放 (0, 0)，由外层按源节点统一摆放并落盘。
    /// </summary>
    private WorkspaceNode CreateExchangeResult(
        WorkspaceNode parent,
        string opKey,
        ShapeDocument document,
        string firstId,
        string secondId)
    {
        // CreateNodeAtPoint 是 store 的公开建节点入口，会顺带 Save + Changed
        WorkspaceNode node = _store!.CreateNodeAtPoint(0, 0);

        int row = parent.Row + 1;

        node.Row = row;

        node.Col = FindFreeColumn(row, parent.Col);

        node.Code = _exchangeExporter.Export(document, omitTopEmptyLayers: true);

        node.Format = ShortCodeFormat.Full;

        node.Layers = document.LayerCount;

        node.Parts = document.PartCount;

        node.IsResult = true;

        node.OpKey = opKey;

        node.Label = ExchangeResultLabel;

        node.InputAId = firstId;

        node.InputBId = secondId;

        return node;
    }

    /// <summary>给结果节点补一条「交换输入」边，重复的不再添加。</summary>
    private void AddExchangeEdge(
        string fromId,
        string toId)
    {
        if (_store?.Active is not WorkspaceProject project)
        {
            return;
        }

        if (project.Edges.Any(edge =>
                edge.FromId == fromId &&
                edge.ToId == toId &&
                edge.Label == ExchangeEdgeLabel))
        {
            return;
        }

        project.Edges.Add(new WorkspaceEdge
        {
            FromId = fromId,
            ToId = toId,
            Label = ExchangeEdgeLabel
        });
    }

    /// <summary>为结果节点找一个同行的空列，保持自动布局时不会重叠。</summary>
    private int FindFreeColumn(
        int row,
        int col)
    {
        int current = Math.Max(0, col);

        while (_store!.Nodes.Any(node => node.Row == row && node.Col == current))
        {
            current++;
        }

        return current;
    }

    /// <summary>与 Core 的 ApplyLimit 一致：超过高度上限时截断。</summary>
    private ShapeDocument ApplyExchangeLimit(
        ShapeDocument document)
    {
        if (_store is null || document.LayerCount <= _store.MaxLayers)
        {
            return document;
        }

        return ShapePhysics.Limit(document, _store.MaxLayers).Document ?? document;
    }

    #region 重算

    /// <summary>
    /// 切换下落规则 / 高度上限后重算。
    ///
    /// Core 的 RecomputeAll 只认识 stack / pin / cut / collapse，
    /// 遇到 exchange1/2 会当成单输入结果重算，覆盖掉交换结果。
    /// 因此先把交换节点临时标记成非结果让 Core 跳过，Core 跑完后
    /// 再按节点上的 InputAId / InputBId 重新 Exchange（First / Second 对应）；
    /// 交换结果的下游节点由下一轮 Core 重算跟上，最多迭代几轮避免死循环。
    /// </summary>
    private (int Updated, int Failed) RecomputeAllWithExchange()
    {
        if (_store is null)
        {
            return (0, 0);
        }

        List<WorkspaceNode> exchangeNodes = _store.Nodes
            .Where(node => node.IsResult && IsExchangeKey(node.OpKey))
            .OrderBy(node => node.Row)
            .ThenBy(node => node.Seq)
            .ToList();

        if (exchangeNodes.Count == 0)
        {
            return _store.RecomputeAll();
        }

        // 临时摘掉结果标记：Core 的 RecomputeAll 会跳过它们，避免覆盖交换结果
        foreach (WorkspaceNode node in exchangeNodes)
        {
            node.IsResult = false;
        }

        int updated = 0;

        int failed = 0;

        try
        {
            (int coreUpdated, int coreFailed) = _store.RecomputeAll();

            updated += coreUpdated;

            failed += coreFailed;

            for (int round = 0; round < 4; round++)
            {
                (bool changed, int exchangeFailed) = RecomputeExchangeNodes(exchangeNodes);

                failed += exchangeFailed;

                if (!changed)
                {
                    break;
                }

                (coreUpdated, coreFailed) = _store.RecomputeAll();

                updated += coreUpdated;

                failed += coreFailed;
            }
        }
        finally
        {
            foreach (WorkspaceNode node in exchangeNodes)
            {
                node.IsResult = true;
            }
        }

        // 恢复结果标记后需要再落盘一次；任取一个仍在画布上的交换节点即可保存整份快照
        WorkspaceNode? persistTarget = exchangeNodes
            .FirstOrDefault(node => _store.FindNode(node.Id) is not null);

        if (persistTarget is not null)
        {
            SaveExchangeNodePosition(persistTarget);
        }

        return (updated, failed);
    }

    /// <summary>
    /// 按节点上记录的 InputAId / InputBId 重算交换节点，
    /// 返回是否改动了图形以及因输入缺失跳过的节点数。
    /// </summary>
    private (bool Changed, int Failed) RecomputeExchangeNodes(
        List<WorkspaceNode> nodes)
    {
        bool changed = false;

        int failed = 0;

        foreach (WorkspaceNode node in nodes)
        {
            if (_store is null ||
                node.OpKey is null ||
                _store.FindNode(node.Id) is null)
            {
                continue;
            }

            string op = node.OpKey.Split(':')[0];

            if (op != "exchange1" && op != "exchange2")
            {
                continue;
            }

            WorkspaceNode? sourceA = node.InputAId is null
                ? null
                : _store.FindNode(node.InputAId);

            WorkspaceNode? sourceB = node.InputBId is null
                ? null
                : _store.FindNode(node.InputBId);

            ShapeDocument? documentA =
                sourceA is null ? null : _store.GetEffectiveDocument(sourceA);

            ShapeDocument? documentB =
                sourceB is null ? null : _store.GetEffectiveDocument(sourceB);

            if (documentA is null || documentB is null)
            {
                failed++;

                continue;
            }

            var exchanged = ShapeExchange.Exchange(documentA, documentB, _store.Mode);

            ShapeDocument? computed = op == "exchange1"
                ? exchanged.First
                : exchanged.Second;

            if (computed is null)
            {
                failed++;

                continue;
            }

            computed = ApplyExchangeLimit(computed);

            string code = _exchangeExporter.Export(computed, omitTopEmptyLayers: true);

            if (code == node.Code &&
                node.Layers == computed.LayerCount &&
                node.Parts == computed.PartCount)
            {
                continue;
            }

            if (UpdateExchangeNodeShape(node, code, computed.LayerCount, computed.PartCount))
            {
                changed = true;
            }
            else
            {
                failed++;
            }
        }

        return (changed, failed);
    }

    /// <summary>
    /// 更新交换结果的图形。
    ///
    /// Core 的 ReplaceNodeShape 会级联删除下游节点，这里先把该节点的出边摘掉，
    /// 让它的 DeleteDownstream 找不到下游；调用完立即恢复出边与结果标记。
    /// ReplaceNodeShape 同时会清掉该节点在 store 里的解析缓存，
    /// 保证后面 GetEffectiveDocument 读到的是新图形。
    /// </summary>
    private bool UpdateExchangeNodeShape(
        WorkspaceNode node,
        string code,
        int layers,
        int parts)
    {
        if (_store?.Active is not WorkspaceProject project)
        {
            return false;
        }

        List<WorkspaceEdge> outgoing = project.Edges
            .Where(edge => edge.FromId == node.Id)
            .ToList();

        string? opKey = node.OpKey;

        bool isResult = node.IsResult;

        project.Edges.RemoveAll(edge => edge.FromId == node.Id);

        try
        {
            _store.ReplaceNodeShape(
                node.Id,
                code,
                ShortCodeFormat.Full,
                layers,
                parts);
        }
        finally
        {
            node.OpKey = opKey;

            node.IsResult = isResult;

            project.Edges.AddRange(outgoing);
        }

        if (node.Code != code)
        {
            return false;
        }

        // ReplaceNodeShape 内的 Save 发生在出边/标记恢复之前，这里再落盘一次最终状态
        SaveExchangeNodePosition(node);

        return true;
    }

    /// <summary>用 store 的公开定位接口触发一次保存；自由坐标为空时退回布局坐标。</summary>
    private void SaveExchangeNodePosition(
        WorkspaceNode node)
    {
        if (_store is null)
        {
            return;
        }

        double x = node.X ?? WorkspaceLayout.Margin;

        double y = node.Y ?? WorkspaceLayout.Margin;

        if (node.X is null || node.Y is null)
        {
            WorkspaceNodeBox? box = FindBox(node.Id);

            x = node.X ?? box?.Bounds.X ?? WorkspaceLayout.Margin;

            y = node.Y ?? box?.Bounds.Y ?? WorkspaceLayout.Margin;
        }

        _store.SetNodePosition(node.Id, x, y);
    }

    /// <summary>幂等键是否属于交换结果（exchange1:... / exchange2:...）。</summary>
    private static bool IsExchangeKey(
        string? opKey)
    {
        return opKey is not null &&
               (opKey.StartsWith("exchange1:", StringComparison.Ordinal) ||
                opKey.StartsWith("exchange2:", StringComparison.Ordinal));
    }

    #endregion
}

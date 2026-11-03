using System.Collections;
using System.Reflection;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;
using ShapeEditor.Wpf.Rendering;

namespace ShapeEditor.Wpf.Services;

/// <summary>
/// WPF 工作区的「交换」运算。
///
/// Core 的 WorkspaceStore 只公开了单输入与堆叠的创建/重算入口，
/// 交换结果节点由 WPF 层自己创建、记录输入、连边，并在切换下落规则时重算。
/// 规则与 Web 版 WorkspaceStore.ComputeExchange 一致：
///   exchange1：输出 1（A 东 + B 西）
///   exchange2：输出 2（A 西 + B 东）
/// </summary>
internal static class WorkspaceExchangeService
{
    /// <summary>两个结果节点共用的标题，不区分第一/第二输出。</summary>
    public const string ResultLabel = "交换结果";

    /// <summary>两个输入指向结果节点的连线文字。</summary>
    public const string EdgeLabel = "交换输入";

    private static readonly ShortCodeExporter Exporter = new();

    /// <summary>
    /// Core 的节点解析缓存是私有字段，也没有公开的失效方法。
    /// 交换结果节点重算后会改写短代码，必须清掉该节点的缓存，
    /// 否则缩略图和后续运算会继续使用旧图形。
    /// </summary>
    private static readonly FieldInfo? DocumentCacheField =
        typeof(WorkspaceStore).GetField(
            "_cache",
            BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// 按两个输入生成交换结果；已存在的结果节点直接复用。
    /// 返回的节点还没有写入摆放坐标，由调用方按第二个输入定位并落盘。
    /// </summary>
    public static (NodeActionResult First, NodeActionResult Second) ComputeExchange(
        WorkspaceStore store,
        string firstId,
        string secondId)
    {
        WorkspaceNode? first = store.FindNode(firstId);

        WorkspaceNode? second = store.FindNode(secondId);

        if (first is null || second is null)
        {
            var missing = new NodeActionResult(false, null, "交换输入不存在。");

            return (missing, missing);
        }

        ShapeDocument? firstDocument = store.GetEffectiveDocument(first);

        ShapeDocument? secondDocument = store.GetEffectiveDocument(second);

        if (firstDocument is null || secondDocument is null)
        {
            var empty = new NodeActionResult(false, null, "交换输入缺少图形。");

            return (empty, empty);
        }

        string firstKey = $"exchange1:{firstId}|{secondId}";

        string secondKey = $"exchange2:{firstId}|{secondId}";

        WorkspaceNode? firstResult = FindByKey(store, firstKey);

        WorkspaceNode? secondResult = FindByKey(store, secondKey);

        bool alreadyExists = firstResult is not null && secondResult is not null;

        ShapeDocument? exchangedFirst = null;

        ShapeDocument? exchangedSecond = null;

        if (!alreadyExists)
        {
            var exchanged = ShapeExchange.Exchange(
                firstDocument,
                secondDocument,
                store.Mode);

            if (exchanged.First is null || exchanged.Second is null)
            {
                var failed = new NodeActionResult(
                    false,
                    null,
                    exchanged.Error ?? "交换失败。");

                return (failed, failed);
            }

            exchangedFirst = ApplyLimit(store, exchanged.First);

            exchangedSecond = ApplyLimit(store, exchanged.Second);
        }

        bool createdFirst = false;

        bool createdSecond = false;

        if (firstResult is null)
        {
            firstResult = CreateResultNode(
                store,
                first,
                exchangedFirst!,
                firstKey,
                firstId,
                secondId);

            createdFirst = true;
        }

        if (secondResult is null)
        {
            secondResult = CreateResultNode(
                store,
                first,
                exchangedSecond!,
                secondKey,
                firstId,
                secondId);

            createdSecond = true;
        }

        /*
         * 每个结果节点都从两个输入各连一条边；
         * 重复执行时 AddEdge 会按「起点 + 终点 + 文字」去重。
         */
        AddEdge(store, first.Id, firstResult.Id);

        AddEdge(store, second.Id, firstResult.Id);

        AddEdge(store, first.Id, secondResult.Id);

        AddEdge(store, second.Id, secondResult.Id);

        string message = alreadyExists
            ? "该组合已交换过，已定位到结果。"
            : "已生成交换结果。";

        return (
            new NodeActionResult(createdFirst, firstResult, message),
            new NodeActionResult(createdSecond, secondResult, message));
    }

    /// <summary>
    /// 切换下落规则或高度上限后重算所有节点。
    ///
    /// Core 的 RecomputeAll 不认识 exchange1/exchange2，
    /// 会按单输入运算处理，所以这里先把交换节点的 OpKey 暂时置空让 Core 跳过，
    /// 再由本方法按两个输入重新交换，First/Second 分别写回交换 1/2。
    /// </summary>
    public static (int Updated, int Failed) RecomputeAll(
        WorkspaceStore store)
    {
        WorkspaceProject? project = store.Active;

        if (project is null)
        {
            return store.RecomputeAll();
        }

        List<WorkspaceNode> exchangeNodes = project.Nodes
            .Where(node => node.IsResult && IsExchangeKey(node.OpKey))
            .OrderBy(node => node.Row)
            .ThenBy(node => node.Seq)
            .ToList();

        if (exchangeNodes.Count == 0)
        {
            return store.RecomputeAll();
        }

        string[] keys = exchangeNodes
            .Select(node => node.OpKey!)
            .ToArray();

        foreach (WorkspaceNode node in exchangeNodes)
        {
            node.OpKey = null;
        }

        (int updated, int failed) = store.RecomputeAll();

        for (int index = 0; index < exchangeNodes.Count; index++)
        {
            exchangeNodes[index].OpKey = keys[index];
        }

        foreach (WorkspaceNode node in exchangeNodes)
        {
            ShapeDocument? computed = RecomputeExchange(store, node);

            if (computed is null)
            {
                failed++;

                continue;
            }

            computed = ApplyLimit(store, computed);

            string code = Exporter.Export(
                computed,
                omitTopEmptyLayers: true);

            if (!string.Equals(code, node.Code, StringComparison.Ordinal))
            {
                updated++;
            }

            ApplyDocument(store, node, computed);
        }

        /*
         * Core 没有公开的「只保存」入口。
         * 这里把工作区重命名成原名，触发一次保存与界面刷新；
         * 不碰节点坐标，避免旧数据因为没有 X/Y 被吸附而挪位。
         */
        store.RenameProject(project.Id, project.Name);

        return (updated, failed);
    }

    /// <summary>
    /// 按结果节点记录的 InputAId / InputBId 重新做一次交换。
    /// 第一个结果取 First，第二个结果取 Second。
    /// </summary>
    private static ShapeDocument? RecomputeExchange(
        WorkspaceStore store,
        WorkspaceNode node)
    {
        if (node.InputAId is null || node.InputBId is null)
        {
            return null;
        }

        WorkspaceNode? first = store.FindNode(node.InputAId);

        WorkspaceNode? second = store.FindNode(node.InputBId);

        if (first is null || second is null)
        {
            return null;
        }

        ShapeDocument? firstDocument = store.GetEffectiveDocument(first);

        ShapeDocument? secondDocument = store.GetEffectiveDocument(second);

        if (firstDocument is null || secondDocument is null)
        {
            return null;
        }

        var exchanged = ShapeExchange.Exchange(
            firstDocument,
            secondDocument,
            store.Mode);

        return IsFirstResult(node.OpKey)
            ? exchanged.First
            : exchanged.Second;
    }

    /// <summary>
    /// 新建交换结果节点。CreateNodeAtPoint 会先落一次盘并刷新界面，
    /// 随后补齐结果节点字段；最终坐标由调用方写入时再统一保存。
    /// </summary>
    private static WorkspaceNode CreateResultNode(
        WorkspaceStore store,
        WorkspaceNode parent,
        ShapeDocument document,
        string opKey,
        string inputAId,
        string inputBId)
    {
        WorkspaceNode node = store.CreateNodeAtPoint(80, 80);

        (node.Row, node.Col) = FindFreeCell(
            store,
            parent.Row + 1,
            parent.Col);

        node.Kind = "shape";

        node.IsResult = true;

        node.OpKey = opKey;

        node.Label = ResultLabel;

        node.InputAId = inputAId;

        node.InputBId = inputBId;

        node.StackBaseIsTop = false;

        node.TargetId = null;

        node.BaseId = null;

        ApplyDocument(store, node, document);

        return node;
    }

    private static WorkspaceNode? FindByKey(
        WorkspaceStore store,
        string key)
    {
        return store.Nodes.FirstOrDefault(node => node.OpKey == key);
    }

    private static bool IsExchangeKey(
        string? key)
    {
        return key is not null &&
               (key.StartsWith("exchange1:", StringComparison.Ordinal) ||
                key.StartsWith("exchange2:", StringComparison.Ordinal));
    }

    private static bool IsFirstResult(
        string? key)
    {
        return key?.StartsWith("exchange1:", StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// 写入结果图形的短代码与尺寸，并清掉 Core 里该节点可能存在的旧解析缓存。
    /// </summary>
    private static void ApplyDocument(
        WorkspaceStore store,
        WorkspaceNode node,
        ShapeDocument document)
    {
        node.Code = Exporter.Export(
            document,
            omitTopEmptyLayers: true);

        node.Format = ShortCodeFormat.Full;

        node.Layers = document.LayerCount;

        node.Parts = document.PartCount;

        InvalidateDocumentCache(store, node.Id);
    }

    private static ShapeDocument ApplyLimit(
        WorkspaceStore store,
        ShapeDocument document)
    {
        if (document.LayerCount <= store.MaxLayers)
        {
            return document;
        }

        return ShapePhysics.Limit(document, store.MaxLayers).Document
               ?? document;
    }

    private static void AddEdge(
        WorkspaceStore store,
        string fromId,
        string toId)
    {
        WorkspaceProject? project = store.Active;

        if (project is null)
        {
            return;
        }

        bool exists = project.Edges.Any(edge =>
            edge.FromId == fromId &&
            edge.ToId == toId &&
            edge.Label == EdgeLabel);

        if (exists)
        {
            return;
        }

        project.Edges.Add(new WorkspaceEdge
        {
            FromId = fromId,
            ToId = toId,
            Label = EdgeLabel
        });
    }

    private static (int Row, int Col) FindFreeCell(
        WorkspaceStore store,
        int row,
        int col)
    {
        int currentRow = Math.Max(0, row);

        int currentCol = Math.Max(0, col);

        while (store.Nodes.Any(node =>
                   node.Row == currentRow &&
                   node.Col == currentCol))
        {
            currentCol++;
        }

        return (currentRow, currentCol);
    }

    private static void InvalidateDocumentCache(
        WorkspaceStore store,
        string nodeId)
    {
        if (DocumentCacheField?.GetValue(store) is IDictionary cache)
        {
            cache.Remove(nodeId);
        }
    }
}

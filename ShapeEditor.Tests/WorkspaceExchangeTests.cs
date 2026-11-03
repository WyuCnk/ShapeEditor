using System.IO;
using ShapeEditor.Core.Analysis;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Physics;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Tests;

/// <summary>
/// 工作区交换：Core 的 ComputeExchange 创建两个结果节点，
/// 切换下落规则后 RecomputeAll 仍按交换规则重算。
/// </summary>
internal static class WorkspaceExchangeTests
{
    public static void ExchangeCreatesTwoResultsAndRecomputes()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"workspace-exchange-{Guid.NewGuid():N}.json");

        try
        {
            var store = new WorkspaceStore(path);

            WorkspaceNode first = store.CreateNode();

            store.ReplaceNodeShape(
                first.Id,
                "Cr--",
                ShortCodeFormat.Full,
                2,
                2);

            WorkspaceNode second = store.CreateNode();

            store.ReplaceNodeShape(
                second.Id,
                "Cg--",
                ShortCodeFormat.Full,
                2,
                2);

            (NodeActionResult firstResult, NodeActionResult secondResult) pair =
                store.ComputeExchange(
                    first.Id,
                    second.Id,
                    CollapseMode.IncludeCycles);

            Assert.True(pair.firstResult.Created);
            Assert.True(pair.secondResult.Created);
            Assert.True(pair.firstResult.Node is not null);
            Assert.True(pair.secondResult.Node is not null);

            WorkspaceNode resultFirst = pair.firstResult.Node!;

            WorkspaceNode resultSecond = pair.secondResult.Node!;

            // 标题、幂等键与两个输入都要写在结果节点上
            Assert.Equal(WorkspaceExchangeGraph.ResultLabel, resultFirst.Label);
            Assert.Equal(WorkspaceExchangeGraph.ResultLabel, resultSecond.Label);

            Assert.Equal(
                WorkspaceExchangeGraph.Key(true, first.Id, second.Id),
                resultFirst.OpKey);

            Assert.Equal(
                WorkspaceExchangeGraph.Key(false, first.Id, second.Id),
                resultSecond.OpKey);

            Assert.Equal(first.Id, resultFirst.InputAId);
            Assert.Equal(second.Id, resultFirst.InputBId);
            Assert.Equal(first.Id, resultSecond.InputAId);
            Assert.Equal(second.Id, resultSecond.InputBId);

            // 两个素材 + 两个交换结果
            Assert.Equal(4, store.Nodes.Count);

            Assert.Equal(
                4,
                store.Edges.Count(
                    edge => edge.Label == WorkspaceExchangeGraph.EdgeLabel));

            // 幂等：同一组合再交换一次只定位已有结果，不新增节点
            (NodeActionResult againFirst, NodeActionResult againSecond) again =
                store.ComputeExchange(
                    first.Id,
                    second.Id,
                    CollapseMode.IncludeCycles);

            Assert.False(again.againFirst.Created);
            Assert.False(again.againSecond.Created);
            Assert.Equal(resultFirst.Id, again.againFirst.Node?.Id);
            Assert.Equal(resultSecond.Id, again.againSecond.Node?.Id);
            Assert.Equal(4, store.Nodes.Count);

            /*
             * 故意把两个结果改成错误图形，再切换规则重算。
             * 若 RecomputeAll 仍把 exchange 当单输入 collapse，
             * 结果会被覆盖成输入节点的图形；只有按 InputAId/InputBId
             * 重新交换才能得到期望的两个不同结果。
             */
            resultFirst.Code = "Cc--";

            resultSecond.Code = "Cc--";

            store.SetMode(CollapseMode.FloorOnly);

            (int updated, int failed) = store.RecomputeAll();

            Assert.Equal(0, failed);
            Assert.Equal(2, updated);

            ShapeDocument firstDocument = store.GetEffectiveDocument(first)!;

            ShapeDocument secondDocument = store.GetEffectiveDocument(second)!;

            var expected = ShapeExchange.Exchange(
                firstDocument,
                secondDocument,
                CollapseMode.FloorOnly);

            Assert.True(expected.First is not null);
            Assert.True(expected.Second is not null);

            var exporter = new ShortCodeExporter();

            Assert.Equal(
                exporter.Export(expected.First!, omitTopEmptyLayers: true),
                store.FindNode(resultFirst.Id)!.Code);

            Assert.Equal(
                exporter.Export(expected.Second!, omitTopEmptyLayers: true),
                store.FindNode(resultSecond.Id)!.Code);

            Assert.Equal(4, store.Nodes.Count);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

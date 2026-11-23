using System.IO;
using ShapeEditor.Core.Analysis.Production;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Core.Workspaces;

namespace ShapeEditor.Tests;

/// <summary>
/// 工作区旋转：Core 的 RecomputeAll 认 rotate:{源节点 Id}，
/// 按父节点当前图形顺时针转 1 个象限重算；
/// 父节点刷新后旋转结果跟着刷新，父节点缺失时按单输入运算的失败语义处理。
/// </summary>
internal static class WorkspaceRotateTests
{
    public static void RecomputeRotatesParentClockwiseOneQuadrant()
    {
        string path = TempPath();

        try
        {
            var store = new WorkspaceStore(path);

            var exporter = new ShortCodeExporter();

            WorkspaceNode source = store.CreateNode();

            store.ReplaceNodeShape(
                source.Id,
                "Cr--:Cg--",
                ShortCodeFormat.Full,
                2,
                4);

            ShapeDocument sourceDocument = store.GetNodeDocument(source)!;

            // 故意写一个错误图形，重算后应被旋转结果覆盖
            WorkspaceNode rotate = AddRotateResult(
                store,
                source,
                "Cc--",
                1,
                4);

            (int updated, int failed) = store.RecomputeAll();

            Assert.Equal(0, failed);
            Assert.Equal(1, updated);

            WorkspaceNode reloaded = store.FindNode(rotate.Id)!;

            ShapeDocument expected = ShapeOps.Rotate(sourceDocument, 1);

            Assert.Equal(
                exporter.Export(expected, omitTopEmptyLayers: true),
                reloaded.Code);

            ShapeDocument actual = store.GetNodeDocument(reloaded)!;

            // 逐格核对：顺时针 1 个象限，层数与象限数都不变
            Assert.True(
                ShapeOps.Same(expected, actual),
                "旋转结果应与父节点顺时针 90° 逐格一致");

            Assert.Equal(sourceDocument.LayerCount, reloaded.Layers);
            Assert.Equal(sourceDocument.PartCount, reloaded.Parts);
            Assert.Equal("rotate:" + source.Id, reloaded.OpKey);
        }
        finally
        {
            Delete(path);
        }
    }

    public static void RotateResultRefreshesAfterParentRecompute()
    {
        string path = TempPath();

        try
        {
            var store = new WorkspaceStore(path);

            var exporter = new ShortCodeExporter();

            WorkspaceNode source = store.CreateNode();

            /*
             * 6 层实心单列：第一次下落保持 6 层；
             * 高度上限改成 4 后父节点被截到 4 层，旋转结果应随之刷新。
             */
            store.ReplaceNodeShape(
                source.Id,
                "Cr--:Cg--:Cb--:Cu--:Cr--:Cg--",
                ShortCodeFormat.Full,
                6,
                4);

            NodeActionResult parentResult = store.ApplyCollapse(source.Id);

            Assert.True(parentResult.Created, parentResult.Message);

            WorkspaceNode parent = parentResult.Node!;

            Assert.Equal(6, parent.Layers);

            ShapeDocument parentDocument = store.GetNodeDocument(parent)!;

            WorkspaceNode rotate = AddRotateResult(
                store,
                parent,
                exporter.Export(
                    ShapeOps.Rotate(parentDocument, 1),
                    omitTopEmptyLayers: true),
                6,
                4);

            // 降低高度上限：父节点与旋转结果都应重算
            store.SetMaxLayers(4);

            (int updated, int failed) = store.RecomputeAll();

            Assert.Equal(0, failed);
            Assert.Equal(2, updated);

            WorkspaceNode reloadedParent = store.FindNode(parent.Id)!;

            WorkspaceNode reloadedRotate = store.FindNode(rotate.Id)!;

            Assert.Equal(4, reloadedParent.Layers);

            ShapeDocument newParentDocument =
                store.GetNodeDocument(reloadedParent)!;

            ShapeDocument expected = ShapeOps.Rotate(newParentDocument, 1);

            ShapeDocument actual = store.GetNodeDocument(reloadedRotate)!;

            Assert.True(
                ShapeOps.Same(expected, actual),
                "父节点被截断后，旋转结果应跟着父节点重算");

            Assert.Equal(4, reloadedRotate.Layers);
            Assert.Equal(4, reloadedRotate.Parts);
        }
        finally
        {
            Delete(path);
        }
    }

    public static void MissingParentFailsWithoutCrash()
    {
        string path = TempPath();

        try
        {
            var store = new WorkspaceStore(path);

            WorkspaceNode orphan = store.CreateNode();

            orphan.IsResult = true;
            orphan.OpKey = $"rotate:{Guid.NewGuid():N}";
            orphan.Label = "旋转";
            orphan.Code = "Cr--";
            orphan.Format = ShortCodeFormat.Full;
            orphan.Layers = 1;
            orphan.Parts = 4;

            string missingId = Guid.NewGuid().ToString("N");

            store.Active!.Edges.Add(new WorkspaceEdge
            {
                FromId = missingId,
                ToId = orphan.Id,
                Label = "旋转"
            });

            (int updated, int failed) = store.RecomputeAll();

            // 与其它单输入 OpKey 一致：父节点缺失时只计 failed，不改节点内容
            Assert.Equal(0, updated);
            Assert.Equal(1, failed);
            Assert.Equal("Cr--", store.FindNode(orphan.Id)!.Code);
            Assert.Equal(1, store.FindNode(orphan.Id)!.Layers);
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>
    /// 手工搭一个旋转结果节点：OpKey 为 rotate:{源节点 Id}，
    /// 并从源节点拉一条「旋转」连线（Core 的 Recompute 按第一条入边找父节点）。
    /// </summary>
    private static WorkspaceNode AddRotateResult(
        WorkspaceStore store,
        WorkspaceNode source,
        string code,
        int layers,
        int parts)
    {
        WorkspaceNode rotate = store.CreateNode();

        rotate.Row = source.Row + 1;
        rotate.Col = source.Col;
        rotate.IsResult = true;
        rotate.OpKey = $"rotate:{source.Id}";
        rotate.Label = "旋转";
        rotate.InputAId = source.Id;
        rotate.Code = code;
        rotate.Format = ShortCodeFormat.Full;
        rotate.Layers = layers;
        rotate.Parts = parts;

        store.Active!.Edges.Add(new WorkspaceEdge
        {
            FromId = source.Id,
            ToId = rotate.Id,
            Label = "旋转"
        });

        return rotate;
    }

    private static string TempPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"workspace-rotate-{Guid.NewGuid():N}.json");
    }

    private static void Delete(
        string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

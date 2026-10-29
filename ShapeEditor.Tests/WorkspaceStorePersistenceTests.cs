using System.IO;
using ShapeEditor.Core.Models;
using ShapeEditor.Wpf.Services;

namespace ShapeEditor.Tests;

/// <summary>
/// WPF 工作区的持久化往返。
/// </summary>
internal static class WorkspaceStorePersistenceTests
{
    public static void SaveAndReloadKeepsData()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"workspace-store-{Guid.NewGuid():N}.json");

        try
        {
            var store = new WorkspaceStore(path);

            store.AddMaterial("Cr--", ShortCodeFormat.Full, 2, 2, "editor");
            store.CreateNode();

            var reloaded = new WorkspaceStore(path);

            Assert.Equal(1, reloaded.Materials.Count);
            Assert.Equal(1, reloaded.Nodes.Count);
            Assert.Equal(1, reloaded.Projects.Count);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static void MaterialSurvivesNewProject()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"workspace-store-{Guid.NewGuid():N}.json");

        try
        {
            var store = new WorkspaceStore(path);

            store.AddMaterial("CuCu", ShortCodeFormat.Full, 1, 4, "editor");
            store.CreateNode();
            store.CreateProject("第二个");

            Assert.Equal(2, store.Projects.Count);
            Assert.Equal(1, store.Materials.Count);
            Assert.Equal(0, store.Nodes.Count);
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

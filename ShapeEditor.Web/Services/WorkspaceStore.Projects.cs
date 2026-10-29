using System.Text.Json;
using ShapeEditor.Web.Models;

namespace ShapeEditor.Web.Services;

public sealed partial class WorkspaceStore
{
    private readonly List<WorkspaceProject> _projects = new();

    private WorkspaceProject? _active;

    /// <summary>
    /// 所有工作区。
    /// </summary>
    public IReadOnlyList<WorkspaceProject> Projects => _projects;

    public WorkspaceProject? ActiveProject => _active;

    public string ActiveName => _active?.Name ?? "";

    #region 工作区管理

    public void CreateProject(string? name = null)
    {
        SnapshotActive();

        int index = _projects.Count + 1;

        var project = new WorkspaceProject
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"工作区 {index}" : name.Trim(),
            MaxLayers = MaxLayers
        };

        _projects.Add(project);

        Activate(project);

        Changed?.Invoke();
    }

    public void RenameProject(string id, string? name)
    {
        WorkspaceProject? project = _projects.FirstOrDefault(entry => entry.Id == id);

        if (project is null)
        {
            return;
        }

        project.Name = string.IsNullOrWhiteSpace(name) ? project.Name : name.Trim();

        PersistGraph();
        Changed?.Invoke();
    }

    public void DeleteProject(string id)
    {
        WorkspaceProject? project = _projects.FirstOrDefault(entry => entry.Id == id);

        if (project is null)
        {
            return;
        }

        _projects.Remove(project);

        if (_projects.Count == 0)
        {
            _projects.Add(new WorkspaceProject { Name = "工作区 1" });
        }

        if (_active?.Id == id)
        {
            Activate(_projects[0]);
        }

        PersistGraph();
        Changed?.Invoke();
    }

    public void ActivateProject(string id)
    {
        WorkspaceProject? project = _projects.FirstOrDefault(entry => entry.Id == id);

        if (project is null || project.Id == _active?.Id)
        {
            return;
        }

        SnapshotActive();

        Activate(project);

        Changed?.Invoke();
    }

    private void Activate(WorkspaceProject project)
    {
        _active = project;

        _nodes = project.Nodes;
        _edges = project.Edges;
        MaxLayers = project.MaxLayers;

        _nodeCache.Clear();
        SelectedNode = null;

        PersistGraph();
    }

    #endregion

    #region 导入导出

    /// <summary>
    /// 把当前工作区导出成 JSON 文本。
    /// </summary>
    public string ExportActiveProject()
    {
        SnapshotActive();

        return JsonSerializer.Serialize(new
        {
            version = 1,
            name = _active?.Name ?? "工作区",
            nodes = _nodes,
            edges = _edges,
            maxLayers = MaxLayers
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 从 JSON 导入一个工作区（新建，不影响现有工作区）。
    /// </summary>
    public bool ImportProject(string json, out string? error)
    {
        error = null;

        try
        {
            ImportPayload? payload = JsonSerializer.Deserialize<ImportPayload>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload?.Nodes is null)
            {
                error = "文件里没有画布数据。";

                return false;
            }

            SnapshotActive();

            var project = new WorkspaceProject
            {
                Name = string.IsNullOrWhiteSpace(payload.Name)
                    ? "导入的工作区"
                    : payload.Name,
                Nodes = payload.Nodes,
                Edges = payload.Edges ?? new List<WorkspaceEdge>(),
                MaxLayers = Math.Clamp(payload.MaxLayers, 1, 100)
            };

            _projects.Add(project);

            Activate(project);

            Changed?.Invoke();

            return true;
        }
        catch (JsonException)
        {
            error = "文件不是有效的 JSON。";

            return false;
        }
    }

    private sealed class ImportPayload
    {
        public int Version { get; set; } = 1;

        public string? Name { get; set; }

        public List<WorkspaceNode>? Nodes { get; set; }

        public List<WorkspaceEdge>? Edges { get; set; }

        public int MaxLayers { get; set; } = 100;
    }

    #endregion
}

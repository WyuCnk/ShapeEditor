using ShapeEditor.Core.Workspaces;
using System.Text.Json;
using Microsoft.JSInterop;
using ShapeEditor.Core;
using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;
using ShapeEditor.Web.Models;

namespace ShapeEditor.Web.Services;

public enum WorkspaceAddResult
{
    Added,
    Duplicate,
    Full,
    Invalid
}

/// <summary>
/// 工作区状态：图形列表、选中项、持久化。
///
/// 页面通过 <see cref="Changed"/> 事件刷新。
/// </summary>
public sealed partial class WorkspaceStore
{
    public const int MaxItems = 200;

    private const string StorageKey = "shape-workspace-v1";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IJSRuntime _js;

    private readonly ShortCodeParser _parser = new();

    private readonly Dictionary<string, ShapeDocument> _cache = new();

    private List<WorkspaceItem> _items = new();

    public WorkspaceStore(
        IJSRuntime js)
    {
        _js = js;
    }

    public event Action? Changed;

    public IReadOnlyList<WorkspaceItem> Items => _items;

    /// <summary>
    /// 选中的图形 Id，按点击顺序排列，最多两个。
    /// </summary>
    public List<string> SelectedIds { get; } = new();

    /// <summary>
    /// 从工作区载入编辑器时暂存的目标。
    /// </summary>
    public WorkspaceItem? PendingLoad { get; set; }

    public bool Loaded { get; private set; }

    public async Task EnsureLoadedAsync()
    {
        if (Loaded)
        {
            return;
        }

        Loaded = true;

        try
        {
            string? json = await _js.InvokeAsync<string?>(
                "shapeStorage.load",
                StorageKey);

            if (!string.IsNullOrWhiteSpace(json))
            {
                _items = JsonSerializer.Deserialize<List<WorkspaceItem>>(
                    json,
                    ReadOptions) ?? new List<WorkspaceItem>();
            }
        }
        catch
        {
            _items = new List<WorkspaceItem>();
        }

        Changed?.Invoke();
    }

    public WorkspaceAddResult TryAdd(
        string code,
        ShortCodeFormat format,
        int layers,
        int parts,
        string source,
        string? operation = null,
        IEnumerable<string>? parents = null)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !ShapeRules.IsValidPartCount(parts) ||
            layers <= 0)
        {
            return WorkspaceAddResult.Invalid;
        }

        if (_items.Any(item =>
                item.Code == code &&
                item.Format == format &&
                item.Layers == layers &&
                item.Parts == parts))
        {
            return WorkspaceAddResult.Duplicate;
        }

        if (_items.Count >= MaxItems)
        {
            return WorkspaceAddResult.Full;
        }

        var item = new WorkspaceItem
        {
            Name = code.Length <= 24 ? code : code[..24] + '…',
            Code = code,
            Format = format,
            Layers = layers,
            Parts = parts,
            Source = source,
            Operation = operation,
            CreatedAt = DateTime.Now
        };

        if (parents is not null)
        {
            item.ParentIds.AddRange(parents);
        }

        _items.Add(item);

        _cache[item.Id] = ParseCode(code, layers, parts, format)!;

        Persist();

        Changed?.Invoke();

        return WorkspaceAddResult.Added;
    }

    public void Rename(
        string id,
        string name)
    {
        WorkspaceItem? item = _items.FirstOrDefault(entry => entry.Id == id);

        if (item is null)
        {
            return;
        }

        item.Name = string.IsNullOrWhiteSpace(name)
            ? item.Code
            : name.Trim();

        Persist();

        Changed?.Invoke();
    }

    public void ToggleSelect(
        string id)
    {
        if (SelectedIds.Remove(id))
        {
            Changed?.Invoke();

            return;
        }

        if (SelectedIds.Count >= 2)
        {
            SelectedIds.RemoveAt(0);
        }

        SelectedIds.Add(id);

        Changed?.Invoke();
    }

    public void ClearSelection()
    {
        SelectedIds.Clear();

        Changed?.Invoke();
    }

    public void Remove(
        string id)
    {
        _items.RemoveAll(item => item.Id == id);

        SelectedIds.Remove(id);

        _cache.Remove(id);

        Persist();

        Changed?.Invoke();
    }

    public void Clear()
    {
        _items.Clear();

        SelectedIds.Clear();

        _cache.Clear();

        Persist();

        Changed?.Invoke();
    }

    /// <summary>
    /// 解析图形，失败返回 null 并给出原因。
    /// </summary>
    public ShapeDocument? GetDocument(
        WorkspaceItem item,
        out string? error)
    {
        error = null;

        if (_cache.TryGetValue(item.Id, out ShapeDocument? document))
        {
            return document;
        }

        document = ParseCode(item.Code, item.Layers, item.Parts, item.Format);

        if (document is null)
        {
            error = "短代码无法解析";

            return null;
        }

        _cache[item.Id] = document;

        return document;
    }

    private ShapeDocument? ParseCode(
        string code,
        int layers,
        int parts,
        ShortCodeFormat format)
    {
        ParseResult result = _parser.Parse(
            code,
            layers,
            parts,
            format);

        return result.Success
            ? result.Document
            : null;
    }

    private void Persist()
    {
        _ = PersistAsync();
    }

    private async Task PersistAsync()
    {
        try
        {
            string json = JsonSerializer.Serialize(_items);

            await _js.InvokeVoidAsync(
                "shapeStorage.save",
                StorageKey,
                json);
        }
        catch
        {
            /*
             * 持久化失败不影响当前会话。
             */
        }
    }
}

using ShapeEditor.Core.Models;
using ShapeEditor.Core.Services;

namespace ShapeEditor.Wpf.ViewModels;

/// <summary>
/// 批量查看里的一条短代码。
/// </summary>
public sealed class BatchItemViewModel
{
    /// <summary>
    /// 在整个列表中的序号，从 0 开始。
    /// </summary>
    public int Index { get; init; }

    public int DisplayIndex => Index + 1;

    public string Code { get; init; } = "";

    public string? Error { get; init; }

    public ShapeDocument? Document { get; init; }

    public int LayerCount { get; init; }

    public int PartCount { get; init; }

    public bool HasError => Error is not null;

    public string DimensionText =>
        Document is null
            ? ""
            : $"{LayerCount} 层 × {PartCount} 象限";
}

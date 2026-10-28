using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Physics;

/// <summary>
/// 单个结果的操作返回值。
/// </summary>
public sealed record PhysicsResult(
    bool Success,
    ShapeDocument? Document,
    string? Error)
{
    public static PhysicsResult Ok(
        ShapeDocument document)
    {
        return new PhysicsResult(true, document, null);
    }

    public static PhysicsResult Fail(
        string error)
    {
        return new PhysicsResult(false, null, error);
    }
}

/// <summary>
/// 切割会同时产出东西两半。
/// </summary>
public sealed record CutResult(
    bool Success,
    ShapeDocument? West,
    ShapeDocument? East,
    string? Error)
{
    public static CutResult Ok(
        ShapeDocument west,
        ShapeDocument east)
    {
        return new CutResult(true, west, east, null);
    }

    public static CutResult Fail(
        string error)
    {
        return new CutResult(false, null, null, error);
    }
}

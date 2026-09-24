using ShapeEditor.Core.Models;

namespace ShapeEditor.Core.Services;

public sealed class ParseResult
{
    private ParseResult(
        bool success,
        ShapeDocument? document,
        string? error)
    {
        Success = success;
        Document = document;
        Error = error;
    }

    public bool Success { get; }

    public ShapeDocument? Document { get; }

    public string? Error { get; }

    public static ParseResult Ok(
        ShapeDocument document)
    {
        return new ParseResult(
            true,
            document,
            null);
    }

    public static ParseResult Fail(
        string error)
    {
        return new ParseResult(
            false,
            null,
            error);
    }
}
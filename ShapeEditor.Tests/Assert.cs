namespace ShapeEditor.Tests;

/// <summary>
/// 极简断言工具。
/// </summary>
internal static class Assert
{
    public static void True(
        bool condition,
        string message = "条件不成立")
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(
        bool condition,
        string message = "条件成立")
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(
        T expected,
        T actual,
        string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                message ??
                $"期望 {expected}，实际 {actual}");
        }
    }

    public static void NotEqual<T>(
        T unexpected,
        T actual,
        string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new InvalidOperationException(
                message ?? $"不应等于 {unexpected}");
        }
    }

    public static void Throws<TException>(
        Action action,
        string? message = null)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            message ?? $"没有抛出 {typeof(TException).Name}");
    }
}

namespace MultiCut.Services;

/// <summary>
/// Describes the outcome of a UI-facing backend operation.
/// </summary>
/// <remarks>
/// View models can bind this shape directly instead of catching low-level file,
/// shortcut, or future database exceptions.
/// </remarks>
public class OperationResult
{
    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets a short user-facing status message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets detailed error messages when the operation fails.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="message">An optional user-facing status message.</param>
    /// <returns>A successful operation result.</returns>
    public static OperationResult Succeeded(string message = "")
    {
        return new OperationResult
        {
            Success = true,
            Message = message
        };
    }

    /// <summary>
    /// Creates a failed result from one or more error messages.
    /// </summary>
    /// <param name="message">A short user-facing failure message.</param>
    /// <param name="errors">Detailed error messages.</param>
    /// <returns>A failed operation result.</returns>
    public static OperationResult Failed(string message, params string[] errors)
    {
        return new OperationResult
        {
            Success = false,
            Message = message,
            Errors = BuildErrors(message, errors)
        };
    }

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="message">A short user-facing failure message.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed operation result.</returns>
    public static OperationResult Failed(string message, Exception exception)
    {
        return Failed(message, exception.Message);
    }

    /// <summary>
    /// Normalizes error messages so the UI always has something useful to display.
    /// </summary>
    /// <param name="message">The fallback message.</param>
    /// <param name="errors">Detailed error messages.</param>
    /// <returns>A cleaned error list.</returns>
    protected static IReadOnlyList<string> BuildErrors(string message, IEnumerable<string> errors)
    {
        string[] cleanedErrors = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Trim())
            .ToArray();

        return cleanedErrors.Length > 0 || string.IsNullOrWhiteSpace(message)
            ? cleanedErrors
            : [message];
    }
}

/// <summary>
/// Describes the outcome of a UI-facing backend operation that returns a value.
/// </summary>
/// <typeparam name="T">The value type returned by the operation.</typeparam>
public sealed class OperationResult<T> : OperationResult
{
    /// <summary>
    /// Gets the operation value when <see cref="OperationResult.Success"/> is <see langword="true"/>.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <param name="value">The operation value.</param>
    /// <param name="message">An optional user-facing status message.</param>
    /// <returns>A successful operation result.</returns>
    public static OperationResult<T> Succeeded(T value, string message = "")
    {
        return new OperationResult<T>
        {
            Success = true,
            Value = value,
            Message = message
        };
    }

    /// <summary>
    /// Creates a failed result from one or more error messages.
    /// </summary>
    /// <param name="message">A short user-facing failure message.</param>
    /// <param name="errors">Detailed error messages.</param>
    /// <returns>A failed operation result.</returns>
    public static new OperationResult<T> Failed(string message, params string[] errors)
    {
        return new OperationResult<T>
        {
            Success = false,
            Message = message,
            Errors = BuildErrors(message, errors)
        };
    }

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="message">A short user-facing failure message.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed operation result.</returns>
    public static new OperationResult<T> Failed(string message, Exception exception)
    {
        return Failed(message, exception.Message);
    }
}

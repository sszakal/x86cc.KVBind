namespace x86cc.KVBind.Sample.Api;

/// <summary>
/// The request itself is wrong (an unknown path, a value that does not fit, an operation that is not allowed
/// now): answered with 400 and this message. Thrown by the service for its own checks and around the KVBind
/// calls that act on request input, so any other exception stays what it is — a server error (500).
/// </summary>
public sealed class ApiRequestException : Exception
{
    public ApiRequestException()
    {
    }

    public ApiRequestException(string message)
        : base(message)
    {
    }

    public ApiRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Runs a KVBind call on request input; what KVBind rejects becomes an <see cref="ApiRequestException"/>.</summary>
    public static T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new ApiRequestException(exception.Message, exception);
        }
    }

    /// <inheritdoc cref="Run{T}(Func{T})"/>
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Run(() =>
        {
            action();
            return true;
        });
    }

    public static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ApiRequestException($"'{name}' is required.");
        }
    }
}

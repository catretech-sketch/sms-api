namespace Sms.Shared.Kernel.Results;

public sealed record Error(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    private Result(bool ok, T? value, Error? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(Error error) => new(false, default, error);
}

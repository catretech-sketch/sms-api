using Sms.Shared.Kernel.Results;

namespace Sms.Shared.Kernel.Http;

public sealed record DataEnvelope<T>(T Data);
public sealed record CursorPage<T>(IReadOnlyList<T> Data, string? NextCursor);
public sealed record ErrorBody(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details);
public sealed record ErrorEnvelope(ErrorBody Error)
{
    public static ErrorEnvelope From(Error e) => new(new ErrorBody(e.Code, e.Message, e.Details));
}

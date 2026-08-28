namespace Sms.Application.Services.AiSearch;

public interface IAiIntentHandler
{
    string Intent { get; }

    Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default);
}

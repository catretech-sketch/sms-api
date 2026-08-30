namespace Sms.Application.Services.AiSearch;

public sealed record AiConversationHint(string EntityName, string EntityType);

public interface IAiClassificationClient
{
    Task<AiClassificationResult> ClassifyAsync(string query, AiConversationHint? hint = null, CancellationToken ct = default);
}

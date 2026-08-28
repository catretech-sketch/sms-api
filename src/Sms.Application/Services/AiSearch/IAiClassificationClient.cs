namespace Sms.Application.Services.AiSearch;

public interface IAiClassificationClient
{
    Task<AiClassificationResult> ClassifyAsync(string query, CancellationToken ct = default);
}

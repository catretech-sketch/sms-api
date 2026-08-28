using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.AiSearch;

public interface IAiSearchAuditService
{
    Task LogAsync(
        Guid tenantId, Guid userId, string role, string question,
        string? language, string? intent, int resultCount, bool success, CancellationToken ct = default);
}

public sealed class AiSearchAuditService(AiSearchLogRepository repo, IClock clock) : IAiSearchAuditService
{
    public async Task LogAsync(
        Guid tenantId, Guid userId, string role, string question,
        string? language, string? intent, int resultCount, bool success, CancellationToken ct = default)
    {
        try
        {
            await repo.InsertAsync(new AiSearchLogEntry(
                tenantId, userId, role, question, language, intent, resultCount, success,
                clock.UtcNow), ct);
        }
        catch
        {
            // Audit logging must never break the actual search response.
        }
    }
}

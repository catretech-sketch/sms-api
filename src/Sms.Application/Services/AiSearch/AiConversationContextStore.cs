using System.Text.Json;
using Microsoft.Extensions.Options;
using Sms.Modules.AiSearch.Data;
using Sms.Shared.Kernel.AiSearch;

namespace Sms.Application.Services.AiSearch;

public sealed record PendingCandidate(Guid Id, string Type);

public sealed record AiConversationContext(
    Guid? ResolvedEntityId, string? ResolvedEntityType, string? LanguageOverride,
    IReadOnlyList<PendingCandidate>? PendingCandidates, string? LastIntent);

public interface IAiConversationContextStore
{
    Task<AiConversationContext?> LoadAsync(Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<Guid> SaveAsync(Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default);
    Task ClearAsync(Guid conversationId, CancellationToken ct = default);
}

/// <summary>
/// conversation_id is a conversational convenience ONLY -- see AiSearchAuthorizationService's own
/// doc comments for the authorization invariant this store must never violate. This class is
/// deliberately dumb: it stores and expires a hint. Nothing here makes an authorization decision.
/// </summary>
public sealed class AiConversationContextStore(
    AiSearchConversationRepository repo, IOptions<AiSearchOptions> options, TimeProvider clock)
    : IAiConversationContextStore
{
    public async Task<AiConversationContext?> LoadAsync(
        Guid conversationId, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var row = await repo.FindAsync(conversationId, tenantId, userId, ct);
        if (row is null) return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var absoluteDeadline = row.CreatedAt.AddMinutes(options.Value.ConversationContextAbsoluteMaxMinutes);
        if (now >= row.ExpiresAt || now >= absoluteDeadline)
        {
            await repo.DeleteAsync(conversationId, ct);
            return null;
        }

        var candidates = string.IsNullOrWhiteSpace(row.PendingCandidates)
            ? null
            : JsonSerializer.Deserialize<List<PendingCandidate>>(row.PendingCandidates);

        return new AiConversationContext(
            row.ResolvedEntityId, row.ResolvedEntityType, row.LanguageOverride, candidates, row.LastIntent);
    }

    public async Task<Guid> SaveAsync(
        Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default)
    {
        var id = conversationId ?? Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;

        // CreatedAt must be preserved across renewals for the absolute cap to mean anything -- only
        // set it to "now" for a genuinely new conversation id. A renewal's CreatedAt is read back
        // from the existing row where possible; falling back to "now" for a caller-supplied id this
        // store has never seen is the safe default (a fresh conversation, not an error).
        var existing = conversationId is { } existingId ? await repo.FindAsync(existingId, tenantId, userId, ct) : null;
        var createdAt = existing?.CreatedAt ?? now;

        await repo.UpsertAsync(new AiSearchConversationRow(
            id, tenantId, userId, context.ResolvedEntityId, context.ResolvedEntityType,
            context.LanguageOverride,
            context.PendingCandidates is null ? null : JsonSerializer.Serialize(context.PendingCandidates),
            context.LastIntent, createdAt, now.AddMinutes(options.Value.ConversationContextTtlMinutes)), ct);

        return id;
    }

    public Task ClearAsync(Guid conversationId, CancellationToken ct = default) => repo.DeleteAsync(conversationId, ct);
}

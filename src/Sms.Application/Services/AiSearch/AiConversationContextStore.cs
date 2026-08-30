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

    /// <summary>
    /// IMPORTANT -- this method trusts the caller to have already established that
    /// <paramref name="conversationId"/> (when supplied) is still live, normally by having called
    /// <see cref="LoadAsync"/> in the same turn (exactly what AiSearchService does). SaveAsync itself
    /// does re-fetch the existing row to recover <c>CreatedAt</c>, and as a guard it will mint a
    /// brand-new conversation id (fresh CreatedAt, fresh ExpiresAt) whenever the existing row's
    /// absolute cap (CreatedAt + ConversationContextAbsoluteMaxMinutes) has already elapsed -- so an
    /// absolute-capped id can never be resurrected with a renewed-looking ExpiresAt. But a row that is
    /// merely idle-expired (past ExpiresAt, not yet past the absolute cap) IS renewed by design: its
    /// ExpiresAt is pushed forward while CreatedAt is preserved. Callers that skip LoadAsync therefore
    /// still risk silently reviving an idle-expired conversation's context (which LoadAsync would
    /// otherwise have rejected and deleted) -- only the absolute-cap trap is closed at the source here.
    /// </summary>
    public async Task<Guid> SaveAsync(
        Guid? conversationId, Guid tenantId, Guid userId, AiConversationContext context, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // CreatedAt must be preserved across renewals for the absolute cap to mean anything -- only
        // set it to "now" for a genuinely new conversation id. A renewal's CreatedAt is read back
        // from the existing row where possible; falling back to "now" for a caller-supplied id this
        // store has never seen is the safe default (a fresh conversation, not an error).
        var existing = conversationId is { } existingId ? await repo.FindAsync(existingId, tenantId, userId, ct) : null;

        // Guard: if the existing row's absolute deadline has already elapsed, treat it as if no
        // existing conversation was found at all -- mint a genuinely new id with a fresh CreatedAt,
        // rather than writing a renewed ExpiresAt onto a row whose CreatedAt is already dead. Without
        // this, a caller that calls SaveAsync against a truly-expired id without an intervening
        // LoadAsync would silently produce a row that LoadAsync can never read back (its absolute
        // deadline check would still fail), leaving a permanently dead row lingering in the table.
        if (existing is not null)
        {
            var absoluteDeadline = existing.CreatedAt.AddMinutes(options.Value.ConversationContextAbsoluteMaxMinutes);
            if (now >= absoluteDeadline)
            {
                existing = null;
            }
        }

        var id = existing is null ? Guid.NewGuid() : conversationId!.Value;
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

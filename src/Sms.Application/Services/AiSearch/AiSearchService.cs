using Microsoft.Extensions.Options;
using Sms.Application.Common;
using Sms.Shared.Kernel.AiSearch;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.AiSearch;

public interface IAiSearchService
{
    Task<AiSearchResponse> SearchAsync(
        AiSearchRequest request, IReadOnlyList<string> callerRoles, CancellationToken ct = default);
}

/// <summary>
/// The single entry point for AI global search. It owns the fixed pipeline every query must walk:
/// plan-tier feature gate -> input validation -> conversation-context load (a hint only) -> LLM
/// classification -> write-intent refusal -> intent support check -> authorization/scope clamping ->
/// conversation re-authorization -> handler dispatch -> audit log -> conversation-context save.
/// <para>
/// The ordering is load-bearing. The feature gate runs before classification so a locked tenant never
/// costs an LLM call; the write-intent refusal runs before authorization so a mutation phrased by an
/// otherwise-authorized caller is still refused; and no handler is ever reached without an
/// <see cref="AiAuthorizationResult"/> produced by <see cref="IAiSearchAuthorizationService"/>, which
/// is the only component permitted to derive the caller's scope. Handlers receive that result and
/// nothing else from the request, so raw LLM-extracted filters can never reach a repository unclamped.
/// </para>
/// <para>
/// <b>conversation_id is a conversational convenience ONLY, never an authorization artifact.</b> Every
/// single turn re-runs <see cref="IAiSearchAuthorizationService.AuthorizeAsync"/> in full, exactly as if
/// no conversation_id were present. A stored resolved-entity hint is consulted STRICTLY AFTER that
/// fresh authorization result exists, purely to check whether the previously-discussed entity is still
/// inside the scope that fresh call just computed -- never before, never instead of it. If the entity is
/// no longer in scope (moved class, a parent-child link was severed, the caller's role changed), the
/// turn fails exactly like a cold no-match and the stale conversation-context row is cleared.
/// </para>
/// </summary>
public sealed class AiSearchService(
    IAiClassificationClient classifier,
    IAiSearchAuthorizationService authz,
    IEnumerable<IAiIntentHandler> handlers,
    IAiAnswerTemplateService templates,
    IAiSearchAuditService audit,
    IAiConversationContextStore contextStore,
    IPersonResolver personResolver,
    ITenantContext tenant,
    ITenantFeatureSet features,
    IOptions<AiSearchOptions> options) : IAiSearchService
{
    private readonly Dictionary<string, IAiIntentHandler> _handlersByIntent =
        handlers.GroupBy(h => h.Intent, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public async Task<AiSearchResponse> SearchAsync(
        AiSearchRequest request, IReadOnlyList<string> callerRoles, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.AiSearch))
            return AiSearchResponse.Fail("FeatureNotEnabled", "AI Search is not available on your plan.");

        var query = request.Query;
        if (string.IsNullOrWhiteSpace(query))
            return AiSearchResponse.Fail("InvalidRequest", "query is required.");

        var maxLength = options.Value.MaxQueryLength;
        if (query.Length > maxLength)
            return AiSearchResponse.Fail("InvalidRequest", $"query exceeds {maxLength} characters.");

        var page = Math.Max(1, request.Page ?? 1);
        var pageSize = Math.Clamp(request.PageSize ?? 20, 1, 100);

        // Conversation context is a hint ONLY -- loaded before classification so it can inform the
        // classifier's phrasing understanding, but AuthorizeAsync below re-runs in full regardless, and
        // the stored ResolvedEntity is independently re-checked against that fresh result before any
        // handler is allowed to use it (see the re-authorization block after AuthorizeAsync).
        // AiConversationContextStore.LoadAsync is itself scoped by the AMBIENT (JWT-derived) tenant and
        // user id, never by anything in the request -- so a conversation_id minted for a different
        // tenant or a different user in the same tenant simply finds no row and is silently treated as
        // absent, exactly like an unknown or expired id. No special-casing is needed here for that.
        AiConversationContext? storedContext = null;
        if (Guid.TryParse(request.ConversationId, out var requestedConversationId)
            && tenant.TenantId is { } tid && tenant.UserId is { } uid)
            storedContext = await contextStore.LoadAsync(requestedConversationId, tid, uid, ct);

        AiConversationHint? hint = storedContext?.ResolvedEntityId is not null
            ? new AiConversationHint("the previously-discussed person", storedContext.ResolvedEntityType ?? "person")
            : null;

        var classification = await classifier.ClassifyAsync(query, hint, ct);
        var perTurnLanguage = classification.Language;
        var effectiveLanguage = classification.LanguageDirective ?? storedContext?.LanguageOverride ?? perTurnLanguage;
        var languageOverrideToStore = classification.LanguageDirective ?? storedContext?.LanguageOverride;

        if (string.Equals(classification.Intent, WriteIntent, StringComparison.OrdinalIgnoreCase))
            return await TerminalWithConversationAsync(
                callerRoles, query, effectiveLanguage, "WriteBlocked", classification.Intent,
                templates.RenderWriteBlocked(effectiveLanguage), "write_blocked",
                requestedConversationId: TryGetGuid(request.ConversationId), languageOverrideToStore, ct);

        if (!_handlersByIntent.TryGetValue(classification.Intent, out var handler))
            return await TerminalWithConversationAsync(
                callerRoles, query, effectiveLanguage, "Unsupported", classification.Intent,
                templates.RenderUnsupported(effectiveLanguage), "unsupported",
                requestedConversationId: TryGetGuid(request.ConversationId), languageOverrideToStore, ct);

        var auth = await authz.AuthorizeAsync(classification.Intent, classification.Filters, callerRoles, ct);
        if (!auth.Allowed)
            // A caller who is not (or no longer) permitted to run this intent gets no benefit from
            // persisting anything about the attempt -- any existing conversation context is left
            // completely untouched (never renewed, never cleared) rather than routed through the
            // language-override-persisting path below.
            return await TerminalAsync(
                callerRoles, query, effectiveLanguage, "Forbidden", classification.Intent,
                templates.RenderForbidden(effectiveLanguage), "forbidden", ct);

        // Re-authorization of the stored hint: a previously-resolved entity is used to auto-fill a
        // follow-up's target ONLY if it is still inside the scope AuthorizeAsync just (freshly)
        // computed. This is THE load-bearing security check in this entire pipeline -- never skip it,
        // never trust the stored entity on its own, and never move it before AuthorizeAsync runs.
        if (string.Equals(classification.Intent, PersonLookupIntent, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(auth.ClampedFilters.StudentName)
            && !auth.NameUnmatched
            && storedContext?.ResolvedEntityId is { } storedEntityId)
        {
            var stillInScope = auth.Unrestricted
                || (auth.AllowedChildStudentIds?.Contains(storedEntityId) ?? false)
                || (storedContext.ResolvedEntityType == "student" && auth.AllowedClassNames is not null
                    && await personResolver.IsStillInTeacherScopeAsync(storedEntityId, auth.AllowedClassNames, ct));

            if (!stillInScope)
            {
                if (Guid.TryParse(request.ConversationId, out var expiredId))
                    await contextStore.ClearAsync(expiredId, ct);
                return await TerminalAsync(
                    callerRoles, query, effectiveLanguage, "Unsupported", classification.Intent,
                    templates.RenderNoMatch(effectiveLanguage), "no_match", ct);
            }

            // Still in scope: hand the handler a synthetic pre-resolved entity id/type, bypassing
            // PersonResolver's name search entirely for this turn (there is no name to search for --
            // the whole point of a follow-up). PersonLookupHandler reads this via a direct short-circuit
            // (Task 12, Step 5) that re-fetches the entity's CURRENT name/detail rather than trusting
            // anything carried in from prior context.
            auth = auth with
            {
                PreResolvedEntityId = storedEntityId,
                PreResolvedEntityType = storedContext.ResolvedEntityType,
            };
        }
        // A bare follow-up to a still-open clarification (pending candidates, no new name) is
        // intentionally NOT auto-resolved here -- narrowing "the teacher, not the student" from a
        // vague reply is out of scope for this task; the handler will report no_match for a nameless
        // PersonLookup with no single already-resolved entity, which is the safe default.

        AiSearchResponse response;
        try
        {
            response = await handler.HandleAsync(auth, effectiveLanguage, page, pageSize, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller walked away; there is no response to shape and no outcome worth auditing.
            throw;
        }
        catch (Exception)
        {
            // Handlers talk to repositories directly, so a SQL timeout or deadlock is a live
            // possibility. An escaping exception would skip the audit row and surface as a generic
            // 500 instead of the documented AiSearchResponse failure shape, so it is contained here:
            // every request gets audited, and infra failures stay inside the response contract.
            await audit.LogAsync(
                TenantId, UserId, PrimaryRole(callerRoles), query, effectiveLanguage,
                classification.Intent, 0, false, ct);
            return AiSearchResponse.Fail("SearchFailed", "Search could not be completed. Please try again.");
        }

        await audit.LogAsync(
            TenantId, UserId, PrimaryRole(callerRoles), query, effectiveLanguage,
            response.Intent ?? classification.Intent, response.Count ?? 0, Audited(response), ct);

        Guid? newConversationId;
        try
        {
            newConversationId = await PersistConversationAsync(
                request.ConversationId, response, classification.Intent, languageOverrideToStore, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Finding I-3: conversation-context persistence is best-effort bookkeeping, not the
            // actual search result the caller asked for. By this point the search already
            // succeeded and its audit row was already written -- a transient SQL failure writing
            // or renewing the AiSearchConversation row must never surface as an unhandled
            // exception / raw 500 and must never cost the caller the response they are owed. This
            // mirrors how AiSearchAuditService.LogAsync already swallows its own failures so
            // logging can never break the real response.
            newConversationId = null;
        }

        return response with { ConversationId = newConversationId?.ToString() };
    }

    /// <summary>
    /// Persists a handler's <see cref="AiConversationUpdate"/> (resolved entity / pending candidates)
    /// together with any active language override as the conversation's new state for the NEXT turn.
    /// Returns null (no conversation id to echo) whenever there is no authenticated tenant/user to
    /// scope the row to -- conversation state is meaningless without that -- and ALSO when there is
    /// genuinely nothing worth writing a row for at all (Finding I-2): no <see cref="AiConversationUpdate"/>
    /// from the handler, no language override to persist, and no incoming conversation_id whose TTL
    /// needs renewing. Without this early-out, a plain query with no conversation context whatsoever
    /// (e.g. "aaj kitne bachche aaye") would still mint a brand-new row on every single request that
    /// nothing will ever clean up (the IX_AiSearchConversation_Expiry index exists for a sweep job
    /// that was never written).
    /// </summary>
    private async Task<Guid?> PersistConversationAsync(
        string? requestedConversationId, AiSearchResponse response, string intent, string? languageOverride,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid) return null;

        var update = response.ConversationUpdate;
        var sanitizedLanguageOverride = SanitizeLanguageOverrideForStorage(languageOverride);
        var hasIncomingConversationId =
            Guid.TryParse(requestedConversationId, out var existingId) && existingId != Guid.Empty;

        if (update is null && sanitizedLanguageOverride is null && !hasIncomingConversationId)
            return null;

        var context = new AiConversationContext(
            update?.ResolvedEntityId, update?.ResolvedEntityType,
            sanitizedLanguageOverride, update?.PendingCandidates,
            ClampIntentForStorage(intent));

        return await contextStore.SaveAsync(
            hasIncomingConversationId ? existingId : null, tid, uid, context, ct);
    }

    private static Guid? TryGetGuid(string? s) => Guid.TryParse(s, out var g) ? g : null;

    private const string WriteIntent = "WriteRequestDetected";
    private const string PersonLookupIntent = "PersonLookup";

    /// AiSearchConversation.LastIntent is NVARCHAR(60) (see M0161_AiSearchConversation) and is
    /// write-only bookkeeping -- never read back by this service -- so clamping is the correct,
    /// lossless-enough fix: an over-long or adversarial classifier "intent" string is truncated
    /// rather than allowed to hit the column raw and throw an unhandled SQL truncation exception.
    private const int MaxStoredIntentLength = 60;

    private static string? ClampIntentForStorage(string? intent) =>
        intent is null ? null : intent.Length <= MaxStoredIntentLength ? intent : intent[..MaxStoredIntentLength];

    /// AiSearchConversation.LanguageOverride is NVARCHAR(10), but more importantly it is READ BACK
    /// and used directly as the effective language for a later turn (see the
    /// <c>storedContext?.LanguageOverride</c> fallback above) -- so truncating a malformed value
    /// would still persist meaningless garbage that later gets treated as a real language. The only
    /// two values that are ever meaningful downstream are "en"/"hi" (see AiConversationContextStore's
    /// own doc comment and the migration's column comment), so anything else -- however short -- is
    /// dropped entirely rather than stored.
    private static string? SanitizeLanguageOverrideForStorage(string? languageOverride) =>
        languageOverride is "en" or "hi" ? languageOverride : null;

    /// <summary>
    /// The audited Success flag answers "did this query actually return data", which is deliberately
    /// NOT the same question as <see cref="AiSearchResponse.Success"/> ("was this a well-formed answer
    /// rather than an infra failure"). Handlers return <see cref="AiSearchResponse.Terminal"/> — whose
    /// Success is true — for refusals and no-match outcomes that are structurally identical to the
    /// orchestrator's own WriteBlocked/Forbidden/Unsupported short-circuits, which audit false. Deriving
    /// the flag from the row count keeps both origins consistent, so <c>WHERE Success = 0</c> in the
    /// audit log reliably selects refusals and empty results wherever they were decided.
    /// </summary>
    private static bool Audited(AiSearchResponse response) => response.Success && response.Count is > 0;

    private Guid TenantId => tenant.TenantId ?? Guid.Empty;

    private Guid UserId => tenant.UserId ?? Guid.Empty;

    private static string PrimaryRole(IReadOnlyList<string> callerRoles) =>
        callerRoles.Count > 0 ? callerRoles[0] : "";

    /// A refusal that never touches conversation state (Forbidden): audited, returned, nothing more.
    private async Task<AiSearchResponse> TerminalAsync(
        IReadOnlyList<string> callerRoles, string query, string language,
        string outcomeIntent, string auditedIntent, string answer, string status, CancellationToken ct)
    {
        await audit.LogAsync(TenantId, UserId, PrimaryRole(callerRoles), query, language, auditedIntent, 0, false, ct);
        return AiSearchResponse.Terminal(language, outcomeIntent, answer, status);
    }

    /// A refusal (WriteBlocked/Unsupported) that still renews/creates conversation state when a
    /// language override needs to persist for the NEXT turn -- e.g. "Hindi mein batao, delete all
    /// students" should still stick the language override even though this turn itself is WriteBlocked.
    private async Task<AiSearchResponse> TerminalWithConversationAsync(
        IReadOnlyList<string> callerRoles, string query, string language,
        string outcomeIntent, string auditedIntent, string answer, string status,
        Guid? requestedConversationId, string? languageOverride, CancellationToken ct)
    {
        await audit.LogAsync(TenantId, UserId, PrimaryRole(callerRoles), query, language, auditedIntent, 0, false, ct);
        var response = AiSearchResponse.Terminal(language, outcomeIntent, answer, status);

        if (tenant.TenantId is null || tenant.UserId is null)
            return response;

        var sanitizedLanguageOverride = SanitizeLanguageOverrideForStorage(languageOverride);
        if (sanitizedLanguageOverride is null)
            // Nothing meaningful survives sanitization (no override at all, or a malformed/adversarial
            // one) -- there is nothing worth persisting a row for, so leave any existing context alone
            // exactly as if no language override had ever been present.
            return response;

        try
        {
            var newId = await contextStore.SaveAsync(
                requestedConversationId, tenant.TenantId.Value, tenant.UserId.Value,
                new AiConversationContext(null, null, sanitizedLanguageOverride, null, ClampIntentForStorage(auditedIntent)), ct);
            return response with { ConversationId = newId.ToString() };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Finding I-3 (same rationale as PersistConversationAsync's callsite): a language-override
            // persistence failure must never turn an already-decided WriteBlocked/Unsupported outcome
            // into an unhandled exception.
            return response;
        }
    }
}

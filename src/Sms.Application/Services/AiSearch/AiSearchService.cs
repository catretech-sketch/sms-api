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
/// plan-tier feature gate -> input validation -> LLM classification -> write-intent refusal ->
/// intent support check -> authorization/scope clamping -> handler dispatch -> audit log.
/// <para>
/// The ordering is load-bearing. The feature gate runs before classification so a locked tenant never
/// costs an LLM call; the write-intent refusal runs before authorization so a mutation phrased by an
/// otherwise-authorized caller is still refused; and no handler is ever reached without an
/// <see cref="AiAuthorizationResult"/> produced by <see cref="IAiSearchAuthorizationService"/>, which
/// is the only component permitted to derive the caller's scope. Handlers receive that result and
/// nothing else from the request, so raw LLM-extracted filters can never reach a repository unclamped.
/// </para>
/// </summary>
public sealed class AiSearchService(
    IAiClassificationClient classifier,
    IAiSearchAuthorizationService authz,
    IEnumerable<IAiIntentHandler> handlers,
    IAiAnswerTemplateService templates,
    IAiSearchAuditService audit,
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

        var classification = await classifier.ClassifyAsync(query, ct: ct);
        var language = classification.Language;

        // A mutation request is refused before authorization runs: "can this caller read X" is not the
        // question being asked, and an admin must be refused just as firmly as a parent.
        if (string.Equals(classification.Intent, WriteIntent, StringComparison.OrdinalIgnoreCase))
            return await TerminalAsync(
                callerRoles, query, language, "WriteBlocked", classification.Intent,
                templates.RenderWriteBlocked(language), ct);

        // Resolve the handler before authorizing so an intent nobody implements (including the
        // classifier's own "Unsupported") is reported as unsupported. AiIntentAccessRules.IsAllowed
        // returns false for any unknown intent, so authorizing first would mislabel "I didn't
        // understand that" as "you are not permitted to see that".
        if (!_handlersByIntent.TryGetValue(classification.Intent, out var handler))
            return await TerminalAsync(
                callerRoles, query, language, "Unsupported", classification.Intent,
                templates.RenderUnsupported(language), ct);

        var auth = await authz.AuthorizeAsync(classification.Intent, classification.Filters, callerRoles, ct);
        if (!auth.Allowed)
            return await TerminalAsync(
                callerRoles, query, language, "Forbidden", classification.Intent,
                templates.RenderForbidden(language), ct);

        AiSearchResponse response;
        try
        {
            response = await handler.HandleAsync(auth, language, page, pageSize, ct);
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
                TenantId, UserId, PrimaryRole(callerRoles), query, language,
                classification.Intent, 0, false, ct);
            return AiSearchResponse.Fail("SearchFailed", "Search could not be completed. Please try again.");
        }

        await audit.LogAsync(
            TenantId, UserId, PrimaryRole(callerRoles), query, language,
            response.Intent ?? classification.Intent, response.Count ?? 0, Audited(response), ct);

        return response;
    }

    private const string WriteIntent = "WriteRequestDetected";

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

    private async Task<AiSearchResponse> TerminalAsync(
        IReadOnlyList<string> callerRoles, string query, string language,
        string outcomeIntent, string auditedIntent, string answer, CancellationToken ct)
    {
        // Refusals are audited as unsuccessful searches: they returned no rows, and the audit trail
        // exists precisely so blocked write attempts and permission failures are reviewable. The
        // audited intent is the classifier's actual attempted intent (auditedIntent), never the
        // outcome label (outcomeIntent) — an admin reviewing "WHERE Success = 0" needs to see what
        // the caller was really trying to reach, not just that it was refused. The response's own
        // "intent" field still reports the outcome label to the caller, unchanged.
        await audit.LogAsync(
            TenantId, UserId, PrimaryRole(callerRoles), query, language, auditedIntent, 0, false, ct);
        var status = outcomeIntent switch
        {
            "Forbidden" => "forbidden",
            "WriteBlocked" => "write_blocked",
            _ => "unsupported",
        };
        return AiSearchResponse.Terminal(language, outcomeIntent, answer, status);
    }
}

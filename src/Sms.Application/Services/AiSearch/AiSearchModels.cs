using System.Text.Json.Serialization;

namespace Sms.Application.Services.AiSearch;

public sealed record AiSearchRequest(string Query, int? Page, int? PageSize, string? ConversationId);

public sealed record AiSearchFilters(
    string? StudentName,
    string? ClassName,
    string? Section,
    string? DateExpression,
    bool TargetSelf);

public sealed record AiClassificationResult(string Language, string Intent, AiSearchFilters Filters, string? LanguageDirective = null);

public sealed record AiSearchError(string Code, string Message);

public sealed record PersonCandidate(string Name, string Type, string? Detail);

/// <summary>
/// A handler's internal-only signal to the orchestrator about what to persist to the conversation
/// context store for this turn. Never serialized to the client -- see AiSearchResponse.ConversationUpdate.
/// Every handler except PersonLookupHandler leaves this null.
/// </summary>
public sealed record AiConversationUpdate(
    Guid? ResolvedEntityId, string? ResolvedEntityType, IReadOnlyList<PendingCandidate>? PendingCandidates);

public sealed record AiSearchResponse(
    bool Success,
    string? Language,
    string? Intent,
    string Status,
    string? Answer,
    object? Data,
    int? Page,
    int? PageSize,
    int? Count,
    bool? HasNextPage,
    AiSearchError? Error)
{
    /// Echoed back to the caller by the controller; set by the orchestrator after a handler runs,
    /// never by a handler itself (handlers don't know or manage the conversation id).
    public string? ConversationId { get; init; }

    [JsonIgnore]
    public AiConversationUpdate? ConversationUpdate { get; init; }

    public static AiSearchResponse Ok(
        string language, string intent, string answer, object? data,
        int page, int pageSize, int count, bool hasNextPage) =>
        new(true, language, intent, "success", answer, data, page, pageSize, count, hasNextPage, null);

    public static AiSearchResponse Terminal(string language, string intent, string answer, string status) =>
        new(true, language, intent, status, answer, null, null, null, 0, false, null);

    public static AiSearchResponse Fail(string code, string message) =>
        new(false, null, null, "error", null, null, null, null, null, null, new AiSearchError(code, message));

    public static AiSearchResponse NeedsClarification(
        string language, string intent, string answer, IReadOnlyList<PersonCandidate> candidates) =>
        new(true, language, intent, "needs_clarification", answer, candidates,
            1, candidates.Count, candidates.Count, false, null);
}

namespace Sms.Application.Services.AiSearch;

public sealed record AiSearchRequest(string Query, int? Page, int? PageSize);

public sealed record AiSearchFilters(
    string? StudentName,
    string? ClassName,
    string? Section,
    string? DateExpression,
    bool TargetSelf);

public sealed record AiClassificationResult(string Language, string Intent, AiSearchFilters Filters);

public sealed record AiSearchError(string Code, string Message);

public sealed record AiSearchResponse(
    bool Success,
    string? Language,
    string? Intent,
    string? Answer,
    object? Data,
    int? Page,
    int? PageSize,
    int? Count,
    bool? HasNextPage,
    AiSearchError? Error)
{
    public static AiSearchResponse Ok(
        string language, string intent, string answer, object? data,
        int page, int pageSize, int count, bool hasNextPage) =>
        new(true, language, intent, answer, data, page, pageSize, count, hasNextPage, null);

    public static AiSearchResponse Terminal(string language, string intent, string answer) =>
        new(true, language, intent, answer, null, null, null, 0, false, null);

    public static AiSearchResponse Fail(string code, string message) =>
        new(false, null, null, null, null, null, null, null, null, new AiSearchError(code, message));
}

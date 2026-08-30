using Sms.Application.Services.Transport;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// One-shot snapshot of the caller's linked children's bus location — no SignalR, no live stream.
/// Delegates entirely to <see cref="IStudentBusService.GetMyChildrenBusAsync"/>, which is already
/// scoped to the caller's own linked children via <c>Users.StudentId</c>. Parents only ever see their
/// own children's bus via this existing, already-scoped service. Admin/staff school-wide bus-by-route
/// lookup is deferred — GetMyChildrenBusAsync alone satisfies the parent use case, which is the only
/// one this MVP wires up.
/// </summary>
public sealed class BusLocationSearchHandler(
    IStudentBusService buses, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public string Intent => "BusLocationSearch";

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await buses.GetMyChildrenBusAsync(ct);
        if (!result.IsSuccess)
            return AiSearchResponse.Terminal(language, "Forbidden", templates.RenderForbidden(language), "forbidden");

        var rows = result.Data!;
        if (rows.Count == 0)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoMatch(language), "no_match");

        var bus = rows[0];
        var answer = language switch
        {
            "hi" => $"{bus.StudentName} की बस ({bus.BusNo}) {bus.Status} है।",
            "hinglish" => $"{bus.StudentName} ki bus ({bus.BusNo}) {bus.Status} hai.",
            _ => $"{bus.StudentName}'s bus ({bus.BusNo}) is {bus.Status}."
        };
        return AiSearchResponse.Ok(language, Intent, answer, rows, 1, pageSize, rows.Count, false);
    }
}

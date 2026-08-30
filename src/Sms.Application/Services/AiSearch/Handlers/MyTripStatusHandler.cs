using Sms.Application.Services.Transport;

namespace Sms.Application.Services.AiSearch.Handlers;

/// <summary>
/// Self-scoped exactly like TeacherAttendance/StaffAttendance -- ITripService.GetCurrentAsync()
/// already resolves the caller's own trip internally via ITenantContext, never a request-supplied
/// id, so this handler never reads any field off <paramref name="auth"/> beyond it having already
/// passed <c>AiIntentAccessRules</c> (checked upstream by AiSearchService before any handler runs).
/// </summary>
public sealed class MyTripStatusHandler(ITripService trips, IAiAnswerTemplateService templates) : IAiIntentHandler
{
    public const string IntentName = "MyTripStatus";

    public string Intent => IntentName;

    public async Task<AiSearchResponse> HandleAsync(
        AiAuthorizationResult auth, string language, int page, int pageSize, CancellationToken ct = default)
    {
        var result = await trips.GetCurrentAsync(ct);
        if (!result.IsSuccess || result.Data is not { } trip)
            return AiSearchResponse.Terminal(language, "Unsupported", templates.RenderNoActiveTrip(language), "no_match");

        var answer = templates.RenderTripStatus(language, trip.BusNo ?? "", trip.Direction, trip.Status);
        var data = new { trip.Id, trip.BusNo, trip.Direction, trip.Status, trip.StartedAt };
        return AiSearchResponse.Ok(language, IntentName, answer, data, 1, pageSize, 1, false);
    }
}

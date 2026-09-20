using Sms.Application.Common;
using Sms.Modules.Sports.Contracts;

namespace Sms.Application.Services.Sports;

public interface ISportsService
{
    Task<ApiResult<SportsSummaryResponse>> GetSummaryAsync(CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<SportsTeamResponse>>> ListTeamsAsync(CancellationToken ct = default);
    Task<ApiResult<SportsTeamResponse>> CreateTeamAsync(CreateSportsTeamRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<SportsEventResponse>>> ListEventsAsync(CancellationToken ct = default);
    Task<ApiResult<SportsEventResponse>> CreateEventAsync(CreateSportsEventRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<SportsMedalResponse>>> ListMedalsAsync(CancellationToken ct = default);
    Task<ApiResult<SportsMedalResponse>> CreateMedalAsync(CreateSportsMedalRequest req, CancellationToken ct = default);
}

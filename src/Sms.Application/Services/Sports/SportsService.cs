using Sms.Application.Common;
using Sms.Modules.Sports.Contracts;
using Sms.Modules.Sports.Data;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Sports;

public sealed class SportsService(SportsRepository repo, ITenantContext tenant, IClock clock) : ISportsService
{
    private static readonly HashSet<string> MedalKinds = new(StringComparer.OrdinalIgnoreCase) { "gold", "silver", "bronze" };

    public async Task<ApiResult<SportsSummaryResponse>> GetSummaryAsync(CancellationToken ct = default) =>
        ApiResult<SportsSummaryResponse>.Ok(await repo.SummaryAsync(clock.UtcNow.Year, ct));

    public async Task<ApiResult<IReadOnlyList<SportsTeamResponse>>> ListTeamsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<SportsTeamResponse>>.Ok(await repo.ListTeamsAsync(ct));

    public async Task<ApiResult<SportsTeamResponse>> CreateTeamAsync(CreateSportsTeamRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SportsTeamResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Sport))
            return ApiResult<SportsTeamResponse>.Fail(new Error("bad_request", "team name and sport are required"), 400);
        if (req.Athletes < 0)
            return ApiResult<SportsTeamResponse>.Fail(new Error("bad_request", "athletes cannot be negative"), 400);
        return ApiResult<SportsTeamResponse>.Ok((await repo.CreateTeamAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<SportsEventResponse>>> ListEventsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<SportsEventResponse>>.Ok(await repo.ListEventsAsync(ct));

    public async Task<ApiResult<SportsEventResponse>> CreateEventAsync(CreateSportsEventRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SportsEventResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiResult<SportsEventResponse>.Fail(new Error("bad_request", "event name is required"), 400);
        return ApiResult<SportsEventResponse>.Ok((await repo.CreateEventAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<SportsMedalResponse>>> ListMedalsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<SportsMedalResponse>>.Ok(await repo.ListMedalsAsync(ct));

    public async Task<ApiResult<SportsMedalResponse>> CreateMedalAsync(CreateSportsMedalRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SportsMedalResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
        if (!MedalKinds.Contains(kind))
            return ApiResult<SportsMedalResponse>.Fail(new Error("bad_request", "kind must be gold, silver or bronze"), 400);
        var year = req.Year ?? clock.UtcNow.Year;
        return ApiResult<SportsMedalResponse>.Ok((await repo.CreateMedalAsync(tid, kind, req.Title, year, ct))!, 201);
    }
}

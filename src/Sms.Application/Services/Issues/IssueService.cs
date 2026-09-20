using System.Security.Claims;
using Sms.Application.Common;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;
using Sms.Modules.Issues;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Issues;

public sealed record IssueDetailResponse(IssueResponse Issue, IReadOnlyList<IssueNoteResponse> Notes);

public interface IIssueService
{
    Task<ApiResult<IssueResponse>> CreateAsync(CreateIssueRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<IssueResponse>>> ListAsync(string? status, ClaimsPrincipal caller, CancellationToken ct = default);
    Task<ApiResult<IssueDetailResponse>> GetAsync(Guid id, ClaimsPrincipal caller, CancellationToken ct = default);
    Task<ApiResult<IssueResponse>> UpdateAsync(Guid id, UpdateIssueRequest req, ClaimsPrincipal caller, CancellationToken ct = default);
}

public sealed class IssueService(
    IssueRepository repo, TripRepository trips, INotificationService notifications, ITenantContext tenant)
    : IIssueService
{
    public async Task<ApiResult<IssueResponse>> CreateAsync(CreateIssueRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<IssueResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (!IssueEnums.ValidCategories.Contains(req.Category))
            return ApiResult<IssueResponse>.Fail(
                new Error("invalid_request", $"Category must be one of: {string.Join(", ", IssueEnums.ValidCategories)}"), 400);
        if (!IssueEnums.ValidPriorities.Contains(req.Priority))
            return ApiResult<IssueResponse>.Fail(
                new Error("invalid_request", $"Priority must be one of: {string.Join(", ", IssueEnums.ValidPriorities)}"), 400);
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Description))
            return ApiResult<IssueResponse>.Fail(new Error("invalid_request", "Title and description are required"), 400);
        if (ImageUrlValidation.Validate(req.PhotoUrl) is { } photoError)
            return ApiResult<IssueResponse>.Fail(photoError, 400);

        Guid? vehicleId = null, routeId = null;
        if (req.TripId is { } tripId)
        {
            if (await trips.GetParticipantRoleAsync(tid, tripId, uid, ct) is null)
                return ApiResult<IssueResponse>.Fail(new Error("invalid_request", "trip does not belong to you"), 400);
            vehicleId = await trips.GetBusIdAsync(tripId, ct);
            routeId = await trips.GetTripRouteIdAsync(tripId, ct);
        }

        var created = await repo.CreateAsync(tid, uid, req, vehicleId, routeId, ImageUrlValidation.Normalize(req.PhotoUrl), ct);
        if (created is null)
            return ApiResult<IssueResponse>.Fail(new Error("server_error", "could not create issue"), 500);

        foreach (var managerId in await repo.GetManagerUserIdsAsync(tid, ct))
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                Icon: "alert",
                Tone: "warning",
                Title: $"New issue reported: {created.Title}",
                Body: created.Description,
                UserId: managerId), ct);
        }

        return ApiResult<IssueResponse>.Ok(created, 201);
    }

    public async Task<ApiResult<IReadOnlyList<IssueResponse>>> ListAsync(
        string? status, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<IssueResponse>>.Fail(new Error("forbidden", "no user context"), 403);
        var reporterFilter = RoleChecks.IsIssueManager(caller) ? (Guid?)null : uid;
        return ApiResult<IReadOnlyList<IssueResponse>>.Ok(await repo.ListAsync(status, reporterFilter, ct));
    }

    public async Task<ApiResult<IssueDetailResponse>> GetAsync(Guid id, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IssueDetailResponse>.Fail(new Error("forbidden", "no user context"), 403);
        if (await repo.GetAsync(id, ct) is not { } issue)
            return ApiResult<IssueDetailResponse>.Fail(new Error("not_found", "resource not found"), 404);
        if (!RoleChecks.IsIssueManager(caller) && issue.ReporterUserId != uid)
            return ApiResult<IssueDetailResponse>.Fail(new Error("forbidden", "not your issue"), 403);
        var notes = await repo.GetNotesAsync(id, ct);
        return ApiResult<IssueDetailResponse>.Ok(new IssueDetailResponse(issue, notes));
    }

    public async Task<ApiResult<IssueResponse>> UpdateAsync(
        Guid id, UpdateIssueRequest req, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (!RoleChecks.IsIssueManager(caller))
            return ApiResult<IssueResponse>.Fail(new Error("forbidden", "manager only"), 403);
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<IssueResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (req.Status is { } status && !IssueEnums.ValidStatuses.Contains(status))
            return ApiResult<IssueResponse>.Fail(
                new Error("invalid_request", $"Status must be one of: {string.Join(", ", IssueEnums.ValidStatuses)}"), 400);
        if (await repo.GetAsync(id, ct) is null)
            return ApiResult<IssueResponse>.Fail(new Error("not_found", "resource not found"), 404);

        if (!string.IsNullOrWhiteSpace(req.Note))
            await repo.AddNoteAsync(tid, id, uid, req.Note, ct);

        var updated = req.Status is { } s ? await repo.UpdateStatusAsync(id, s, ct) : await repo.GetAsync(id, ct);
        return ApiResult<IssueResponse>.Ok(updated!);
    }
}

using System.Text.Json;
using Sms.Application.Common;
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Staffing.Contracts;
using Sms.Modules.Staffing.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Staffing;

public interface IStaffingService
{
    Task<ApiResult<CursorPage<TeacherResponse>>> ListTeachersAsync(
        string? q, string? dept, string? status, CancellationToken ct = default);
    Task<ApiResult<TeacherResponse>> GetTeacherAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<TeacherResponse>> CreateTeacherAsync(CreateTeacherRequest req, CancellationToken ct = default);
    Task<ApiResult<TeacherResponse>> UpdateTeacherAsync(Guid id, UpdateTeacherRequest req, CancellationToken ct = default);

    Task<ApiResult<CursorPage<StaffResponse>>> ListStaffAsync(
        string? q, string? cat, CancellationToken ct = default);
    Task<ApiResult<StaffResponse>> GetStaffAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<StaffResponse>> CreateStaffAsync(CreateStaffRequest req, CancellationToken ct = default);
    Task<ApiResult<StaffResponse>> UpdateStaffAsync(Guid id, UpdateStaffRequest req, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<LeaveResponse>>> ListMyLeaveAsync(CancellationToken ct = default);
    Task<ApiResult<LeaveResponse>> CreateLeaveAsync(CreateLeaveRequest req, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<LeaveResponse>>> ListApprovalsAsync(string? status, CancellationToken ct = default);
    Task<ApiResult<LeaveResponse>> DecideLeaveAsync(Guid id, DecideLeaveRequest req, CancellationToken ct = default);
}

public sealed class StaffingService(
    TeacherRepository teachers,
    StaffRepository staff,
    LeaveRepository leave,
    IAuthDao users,
    ITenantContext tenant,
    ITenantFeatureSet features) : IStaffingService
{
    private bool StaffSupportAllowed => FeatureGate.Allowed(tenant, features, FeatureCatalog.StaffSupport);

    public async Task<ApiResult<CursorPage<TeacherResponse>>> ListTeachersAsync(
        string? q, string? dept, string? status, CancellationToken ct = default)
    {
        var rows = await teachers.ListAsync(q, dept, status, ct);
        return ApiResult<CursorPage<TeacherResponse>>.Ok(new CursorPage<TeacherResponse>(rows, null));
    }

    public async Task<ApiResult<TeacherResponse>> GetTeacherAsync(Guid id, CancellationToken ct = default)
    {
        var teacher = await teachers.GetAsync(id, ct);
        return teacher is null
            ? ApiResult<TeacherResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<TeacherResponse>.Ok(teacher);
    }

    public async Task<ApiResult<TeacherResponse>> CreateTeacherAsync(CreateTeacherRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<TeacherResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var created = (await teachers.CreateAsync(tid, req, ct))!;
        return ApiResult<TeacherResponse>.Ok(created, 201);
    }

    public async Task<ApiResult<TeacherResponse>> UpdateTeacherAsync(
        Guid id, UpdateTeacherRequest req, CancellationToken ct = default)
    {
        var existing = await teachers.GetAsync(id, ct);
        if (existing is null)
            return ApiResult<TeacherResponse>.Fail(new Error("not_found", "resource not found"), 404);

        if (req.SetPhoto)
        {
            if (ImageUrlValidation.Validate(req.PhotoUrl) is { } error)
                return ApiResult<TeacherResponse>.Fail(error, 422);
            var userId = await teachers.GetUserIdAsync(id, ct);
            if (userId is null)
                return ApiResult<TeacherResponse>.Fail(new Error("no_linked_user",
                    "This teacher hasn't accepted their sign-in invite yet — the photo can be set once they have."), 409);
            await users.SetPhotoAsync(userId.Value, ImageUrlValidation.Normalize(req.PhotoUrl), ct);
        }

        if (req.Email is not null && !string.Equals(req.Email, existing.Email, StringComparison.OrdinalIgnoreCase))
        {
            var userId = await teachers.GetUserIdAsync(id, ct);
            if (await SyncLinkedEmailAsync(userId, req.Email, ct) is { } error)
                return ApiResult<TeacherResponse>.Fail(error, 409);
        }

        var updated = (await teachers.UpdateAsync(id, req, ct))!;
        return ApiResult<TeacherResponse>.Ok((await teachers.GetAsync(id, ct))!);
    }

    public async Task<ApiResult<CursorPage<StaffResponse>>> ListStaffAsync(
        string? q, string? cat, CancellationToken ct = default)
    {
        if (!StaffSupportAllowed)
            return FeatureGate.Locked<CursorPage<StaffResponse>>(FeatureCatalog.StaffSupport);
        var rows = await staff.ListAsync(q, cat, ct);
        return ApiResult<CursorPage<StaffResponse>>.Ok(new CursorPage<StaffResponse>(rows, null));
    }

    public async Task<ApiResult<StaffResponse>> GetStaffAsync(Guid id, CancellationToken ct = default)
    {
        if (!StaffSupportAllowed)
            return FeatureGate.Locked<StaffResponse>(FeatureCatalog.StaffSupport);
        var member = await staff.GetAsync(id, ct);
        return member is null
            ? ApiResult<StaffResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<StaffResponse>.Ok(member);
    }

    public async Task<ApiResult<StaffResponse>> CreateStaffAsync(CreateStaffRequest req, CancellationToken ct = default)
    {
        if (!StaffSupportAllowed)
            return FeatureGate.Locked<StaffResponse>(FeatureCatalog.StaffSupport);
        if (tenant.TenantId is not { } tid)
            return ApiResult<StaffResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var created = (await staff.CreateAsync(tid, req, ct))!;
        return ApiResult<StaffResponse>.Ok(created, 201);
    }

    public async Task<ApiResult<StaffResponse>> UpdateStaffAsync(
        Guid id, UpdateStaffRequest req, CancellationToken ct = default)
    {
        if (!StaffSupportAllowed)
            return FeatureGate.Locked<StaffResponse>(FeatureCatalog.StaffSupport);
        var existing = await staff.GetAsync(id, ct);
        if (existing is null)
            return ApiResult<StaffResponse>.Fail(new Error("not_found", "resource not found"), 404);

        if (req.SetPhoto)
        {
            if (ImageUrlValidation.Validate(req.PhotoUrl) is { } error)
                return ApiResult<StaffResponse>.Fail(error, 422);
            var userId = await staff.GetUserIdAsync(id, ct);
            if (userId is null)
                return ApiResult<StaffResponse>.Fail(new Error("no_linked_user",
                    "This staff member hasn't accepted their sign-in invite yet — the photo can be set once they have."), 409);
            await users.SetPhotoAsync(userId.Value, ImageUrlValidation.Normalize(req.PhotoUrl), ct);
        }

        if (req.Email is not null && !string.Equals(req.Email, existing.Email, StringComparison.OrdinalIgnoreCase))
        {
            var userId = await staff.GetUserIdAsync(id, ct);
            if (await SyncLinkedEmailAsync(userId, req.Email, ct) is { } error)
                return ApiResult<StaffResponse>.Fail(error, 409);
        }

        var updated = (await staff.UpdateAsync(id, req, ct))!;
        return ApiResult<StaffResponse>.Ok((await staff.GetAsync(id, ct))!);
    }

    /// <summary>Writes an Email edit through to the linked Users row (the single source
    /// of truth for login/GET /auth/me) when one exists; unlinked Teacher/Staff rows
    /// (not yet invited/accepted) have no Users row to sync. Returns a conflict Error
    /// if another account in the tenant already owns that email (Users has a unique
    /// (TenantId, Email) index), leaving both rows untouched.</summary>
    private async Task<Error?> SyncLinkedEmailAsync(Guid? userId, string? newEmail, CancellationToken ct)
    {
        if (userId is null)
            return null;
        if (tenant.TenantId is { } tid)
        {
            var conflictUser = await users.GetByEmailAndTenantAsync(newEmail!, tid, ct);
            if (conflictUser is not null && conflictUser.Id != userId.Value)
                return new Error("conflict", "A user with this email already exists in this school.");
        }
        await users.SetEmailAsync(userId.Value, newEmail, ct);
        return null;
    }

    public async Task<ApiResult<IReadOnlyList<LeaveResponse>>> ListMyLeaveAsync(CancellationToken ct = default)
    {
        var rows = await leave.ListMineAsync(tenant.UserId, ct);
        return ApiResult<IReadOnlyList<LeaveResponse>>.Ok(rows);
    }

    public async Task<ApiResult<LeaveResponse>> CreateLeaveAsync(CreateLeaveRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<LeaveResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        string? attachmentUrlsJson = null;
        if (req.AttachmentUrls is { Count: > 0 })
        {
            if (req.AttachmentUrls.Count > 5)
                return ApiResult<LeaveResponse>.Fail(new Error("invalid_request", "max 5 attachment URLs allowed"), 400);

            var normalized = new List<string>();
            foreach (var url in req.AttachmentUrls)
            {
                if (ImageUrlValidation.Validate(url) is { } error)
                    return ApiResult<LeaveResponse>.Fail(error, 400);
                var n = ImageUrlValidation.Normalize(url);
                if (n is not null)
                    normalized.Add(n);
            }

            if (normalized.Count > 0)
                attachmentUrlsJson = JsonSerializer.Serialize(normalized);
        }

        var created = (await leave.CreateAsync(tid, tenant.UserId, req, attachmentUrlsJson, ct))!;
        return ApiResult<LeaveResponse>.Ok(created, 201);
    }

    public async Task<ApiResult<IReadOnlyList<LeaveResponse>>> ListApprovalsAsync(
        string? status, CancellationToken ct = default)
    {
        var rows = await leave.ListByStatusAsync(status ?? "pending", ct);
        return ApiResult<IReadOnlyList<LeaveResponse>>.Ok(rows);
    }

    public async Task<ApiResult<LeaveResponse>> DecideLeaveAsync(
        Guid id, DecideLeaveRequest req, CancellationToken ct = default)
    {
        if (await leave.GetAsync(id, ct) is null)
            return ApiResult<LeaveResponse>.Fail(new Error("not_found", "resource not found"), 404);
        var decided = (await leave.DecideAsync(id, req.Status, tenant.UserId, req.DecidedNote, ct))!;
        return ApiResult<LeaveResponse>.Ok(decided);
    }
}

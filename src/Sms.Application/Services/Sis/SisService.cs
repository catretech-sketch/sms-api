using Sms.Application.Common;
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Sis.Contracts;
using Sms.Modules.Sis.Data;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Sis;

public interface ISisService
{
    Task<ApiResult<CursorPage<StudentResponse>>> ListStudentsAsync(
        string? q, string? grade, string? status, string? fee, CancellationToken ct = default);
    Task<ApiResult<StudentResponse>> GetStudentAsync(Guid id, CancellationToken ct = default);
    /// <summary>Roster row for the authenticated user (Users.StudentId = admission number, not Users.Id).</summary>
    Task<ApiResult<StudentResponse>> GetMyStudentAsync(CancellationToken ct = default);
    Task<ApiResult<StudentResponse>> CreateStudentAsync(CreateStudentRequest req, CancellationToken ct = default);
    Task<ApiResult<StudentResponse>> UpdateStudentAsync(Guid id, UpdateStudentRequest req, CancellationToken ct = default);
    Task<ApiResult<CursorPage<StudentResponse>>> ListClassStudentsAsync(
        Guid classId, int? limit, string? cursor, CancellationToken ct = default);
    /// <summary>Persist guardian email from enrolment extras and provision the parent login.</summary>
    Task SyncGuardianEmailAsync(Guid studentId, string? guardianEmail, CancellationToken ct = default);
}

public sealed class SisService(
    StudentRepository repo,
    IUserProvisioningDao users,
    IAuthDao auth,
    ITenantContext tenant) : ISisService
{
    public async Task<ApiResult<CursorPage<StudentResponse>>> ListStudentsAsync(
        string? q, string? grade, string? status, string? fee, CancellationToken ct = default)
    {
        var rows = await repo.ListAsync(q, grade, status, fee, ct);
        return ApiResult<CursorPage<StudentResponse>>.Ok(new CursorPage<StudentResponse>(rows, null));
    }

    public async Task<ApiResult<StudentResponse>> GetStudentAsync(Guid id, CancellationToken ct = default)
    {
        var student = await repo.GetAsync(id, ct);
        return student is null
            ? ApiResult<StudentResponse>.Fail(new Error("not_found", "resource not found"), 404)
            : ApiResult<StudentResponse>.Ok(student);
    }

    public async Task<ApiResult<StudentResponse>> GetMyStudentAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<StudentResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);

        var user = await auth.GetByIdAsync(uid, ct);
        if (user is null)
            return ApiResult<StudentResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);

        if (string.IsNullOrWhiteSpace(user.StudentId))
            return ApiResult<StudentResponse>.Fail(
                new Error("not_found", "no linked student record"), 404);

        var student = await repo.GetByAdmissionNoAsync(user.StudentId, ct);
        return student is null
            ? ApiResult<StudentResponse>.Fail(new Error("not_found", "student roster not found"), 404)
            : ApiResult<StudentResponse>.Ok(student);
    }

    public async Task<ApiResult<StudentResponse>> CreateStudentAsync(CreateStudentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<StudentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var created = (await repo.CreateAsync(tid, req, ct))!;

        // Provision the student's login account alongside the roster record, so
        // admission-ID login/forgot-password resolves immediately — previously
        // Students and Users were populated by entirely separate, disconnected paths.
        try
        {
            await users.CreateUserAsync(tid, created.Email, null, isPlatform: false, roles: ["student"], ct,
                studentId: created.AdmissionNo, mustSetPassword: true);
        }
        catch (Exception)
        {
            /* Student Users row is best-effort. Enrolment create must not 500 after the roster saved. */
        }
        await TrySyncStudentLoginEmailAsync(created.AdmissionNo, created.Email, ct);
        await TrySyncParentLoginAsync(created, ct);

        return ApiResult<StudentResponse>.Ok(created, 201);
    }

    public async Task<ApiResult<StudentResponse>> UpdateStudentAsync(
        Guid id, UpdateStudentRequest req, CancellationToken ct = default)
    {
        if (await repo.GetAsync(id, ct) is null)
            return ApiResult<StudentResponse>.Fail(new Error("not_found", "resource not found"), 404);

        if (req.SetPhoto)
        {
            if (ImageUrlValidation.Validate(req.PhotoUrl) is { } error)
                return ApiResult<StudentResponse>.Fail(error, 422);
            req = req with { PhotoUrl = ImageUrlValidation.Normalize(req.PhotoUrl) };
        }

        var updated = (await repo.UpdateAsync(id, req, ct))!;
        await TrySyncStudentLoginEmailAsync(updated.AdmissionNo, updated.Email, ct);
        await TrySyncParentLoginAsync(updated, ct);
        return ApiResult<StudentResponse>.Ok(updated);
    }

    public async Task SyncGuardianEmailAsync(Guid studentId, string? guardianEmail, CancellationToken ct = default)
    {
        var email = guardianEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return;
        var student = await repo.GetAsync(studentId, ct);
        if (student is null) return;
        await repo.SetGuardianEmailAsync(studentId, email, ct);
        student = await repo.GetAsync(studentId, ct);
        if (student is not null)
            await TrySyncParentLoginAsync(student, ct);
    }

    private async Task TrySyncParentLoginAsync(StudentResponse student, CancellationToken ct)
    {
        try
        {
            await SyncParentLoginAsync(student, ct);
        }
        catch (Exception)
        {
            /* Parent Users row is best-effort. Duplicate phone/email vs the student
             * login must not fail SIS create/PATCH with 500 after the roster row saved. */
        }
    }

    private Task SyncParentLoginAsync(StudentResponse student, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(student.AdmissionNo)) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(student.GuardianEmail) && string.IsNullOrWhiteSpace(student.GuardianPhone))
            return Task.CompletedTask;
        return auth.EnsureParentLoginAsync(student.AdmissionNo, ct);
    }

    private async Task TrySyncStudentLoginEmailAsync(string admissionNo, string? email, CancellationToken ct)
    {
        try
        {
            await SyncStudentLoginEmailAsync(admissionNo, email, ct);
        }
        catch (Exception)
        {
            /* Student Users.Email sync is best-effort. Unique email vs a parent/staff
             * login must not fail SIS PATCH after the roster row saved. */
        }
    }

    /// Keep Users.Email in sync with the SIS form so OTP/login use the student address,
    /// not a parent/creator email that was copied onto the login row.
    private async Task SyncStudentLoginEmailAsync(string admissionNo, string? email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(admissionNo)) return;
        var accounts = await auth.ListByAdmissionIdAsync(admissionNo, ct);
        foreach (var account in accounts)
        {
            var roles = await auth.GetRolesAsync(account.Id, ct);
            if (roles.Any(r =>
                    r.Contains("parent", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("owner", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("admin", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("teacher", StringComparison.OrdinalIgnoreCase)
                    || r.Contains("principal", StringComparison.OrdinalIgnoreCase)
                    || r.Equals("staff", StringComparison.OrdinalIgnoreCase)))
                continue;
            await auth.SetEmailAsync(account.Id, email.Trim(), ct);
        }
    }

    public async Task<ApiResult<CursorPage<StudentResponse>>> ListClassStudentsAsync(
        Guid classId, int? limit, string? cursor, CancellationToken ct = default)
    {
        var page = new PageRequest(limit ?? 50, cursor);
        var (rows, next) = await repo.ListByClassPagedAsync(classId, page.SafeLimit, page.Cursor, ct);
        return ApiResult<CursorPage<StudentResponse>>.Ok(new CursorPage<StudentResponse>(rows, next));
    }
}

using Sms.Application.Common;
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
    Task<ApiResult<StudentResponse>> CreateStudentAsync(CreateStudentRequest req, CancellationToken ct = default);
    Task<ApiResult<StudentResponse>> UpdateStudentAsync(Guid id, UpdateStudentRequest req, CancellationToken ct = default);
    Task<ApiResult<CursorPage<StudentResponse>>> ListClassStudentsAsync(
        Guid classId, int? limit, string? cursor, CancellationToken ct = default);
}

public sealed class SisService(StudentRepository repo, ITenantContext tenant) : ISisService
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

    public async Task<ApiResult<StudentResponse>> CreateStudentAsync(CreateStudentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<StudentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var created = (await repo.CreateAsync(tid, req, ct))!;
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
        return ApiResult<StudentResponse>.Ok(updated);
    }

    public async Task<ApiResult<CursorPage<StudentResponse>>> ListClassStudentsAsync(
        Guid classId, int? limit, string? cursor, CancellationToken ct = default)
    {
        var page = new PageRequest(limit ?? 50, cursor);
        var (rows, next) = await repo.ListByClassPagedAsync(classId, page.SafeLimit, page.Cursor, ct);
        return ApiResult<CursorPage<StudentResponse>>.Ok(new CursorPage<StudentResponse>(rows, next));
    }
}

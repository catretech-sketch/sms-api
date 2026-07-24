using Sms.Modules.Staffing.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Staffing.Data;

public sealed class TeacherRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, TenantId, Name, Gender, Department, Designation, SubjectsCsv, ClassTeacher, Phone, Email, " +
        "Exp, Rating, AttendancePct, Result, [Load], Status, AvatarHue, [Top], EmployeeCode";

    public async Task<TeacherResponse?> CreateAsync(Guid tenantId, CreateTeacherRequest r, CancellationToken ct = default)
    {
        var row = await QuerySingleProcAsync<TeacherRow>("dbo.Teacher_Create", new
        {
            TenantId = tenantId, r.Name, r.Gender, r.Department, r.Designation,
            SubjectsCsv = r.Subjects is null || r.Subjects.Count == 0 ? null : string.Join(',', r.Subjects),
            r.ClassTeacher, r.Phone, r.Email, r.Exp, r.Rating, r.Result, r.Load, r.AvatarHue, r.Top,
            r.EmployeeCode,
        }, ct);
        return row?.ToResponse();
    }

    public async Task<TeacherResponse?> UpdateAsync(Guid id, UpdateTeacherRequest r, CancellationToken ct = default)
    {
        var row = await QuerySingleProcAsync<TeacherRow>("dbo.Teacher_Update", new
        {
            Id = id, r.Name, r.Department, r.Designation,
            SubjectsCsv = r.Subjects is null ? null : string.Join(',', r.Subjects),
            r.ClassTeacher, r.Phone, r.Email, r.Status
        }, ct);
        return row?.ToResponse();
    }

    public async Task<TeacherResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<TeacherRow>($"SELECT {Cols} FROM dbo.Teachers WHERE Id = @id", new { id }, ct))
        .FirstOrDefault()?.ToResponse();

    public async Task<IReadOnlyList<TeacherResponse>> ListAsync(
        string? q, string? dept, string? status, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<TeacherRow>(
            $"SELECT {Cols} FROM dbo.Teachers WHERE " +
            "(@q IS NULL OR Name LIKE '%' + @q + '%' OR Department LIKE '%' + @q + '%' OR EmployeeCode LIKE '%' + @q + '%') " +
            "AND (@dept IS NULL OR Department = @dept) AND (@status IS NULL OR Status = @status) ORDER BY Name",
            new { q, dept, status }, ct);
        return rows.Select(r => r.ToResponse()).ToList();
    }
}

public sealed class StaffRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, TenantId, Name, Gender, Role, Category, Department, Phone, Shift, Route, AttendancePct, Status, AvatarHue, EmployeeCode";

    public Task<StaffResponse?> CreateAsync(Guid tenantId, CreateStaffRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<StaffResponse>("dbo.Staff_Create", new
        {
            TenantId = tenantId, r.Name, r.Gender, r.Role, r.Category, r.Department, r.Phone, r.Shift, r.Route, r.AvatarHue,
            r.EmployeeCode,
        }, ct);

    public Task<StaffResponse?> UpdateAsync(Guid id, UpdateStaffRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<StaffResponse>("dbo.Staff_Update", new
        {
            Id = id, r.Name, r.Role, r.Category, r.Department, r.Phone, r.Shift, r.Route, r.Status
        }, ct);

    public async Task<StaffResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<StaffResponse>($"SELECT {Cols} FROM dbo.Staff WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<StaffResponse>> ListAsync(string? q, string? cat, CancellationToken ct = default) =>
        QueryInlineAsync<StaffResponse>(
            $"SELECT {Cols} FROM dbo.Staff WHERE " +
            "(@q IS NULL OR Name LIKE '%' + @q + '%' OR Role LIKE '%' + @q + '%' OR EmployeeCode LIKE '%' + @q + '%') " +
            "AND (@cat IS NULL OR Category = @cat) ORDER BY Name",
            new { q, cat }, ct);
}

using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Sis.Data;

public sealed class StudentRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private const string Cols =
        "Id, TenantId, AdmissionNo, Name, Gender, Grade, Section, ClassLabel, Roll, GuardianName, " +
        "GuardianPhone, AttendancePct, FeeStatus, FeeDue, Status, House, AvatarHue, Dob, Email, Address";

    public Task<StudentResponse?> CreateAsync(Guid tenantId, CreateStudentRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<StudentResponse>("dbo.Student_Create", new
        {
            TenantId = tenantId, r.AdmissionNo, r.Name, r.Gender, r.Grade, r.Section, r.Roll,
            r.GuardianName, r.GuardianPhone, r.House, r.AvatarHue, r.Dob, r.Email, r.Address
        }, ct);

    public Task<StudentResponse?> UpdateAsync(Guid id, UpdateStudentRequest r, CancellationToken ct = default) =>
        QuerySingleProcAsync<StudentResponse>("dbo.Student_Update", new
        {
            Id = id, r.Name, r.Grade, r.Section, r.Roll, r.GuardianName, r.GuardianPhone,
            r.House, r.FeeStatus, r.FeeDue, r.Status
        }, ct);

    public async Task<StudentResponse?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await QueryInlineAsync<StudentResponse>($"SELECT {Cols} FROM dbo.Students WHERE Id = @id", new { id }, ct))
        .FirstOrDefault();

    public Task<IReadOnlyList<StudentResponse>> ListAsync(
        string? q, string? grade, string? status, string? fee, CancellationToken ct = default) =>
        QueryInlineAsync<StudentResponse>(
            $"SELECT {Cols} FROM dbo.Students WHERE " +
            "(@q IS NULL OR Name LIKE '%' + @q + '%' OR AdmissionNo LIKE '%' + @q + '%' OR ClassLabel LIKE '%' + @q + '%') " +
            "AND (@grade IS NULL OR Grade = @grade) AND (@status IS NULL OR Status = @status) " +
            "AND (@fee IS NULL OR FeeStatus = @fee) ORDER BY Name",
            new { q, grade, status, fee }, ct);

    /// Students belonging to a class, matched by the class's Grade+Section (no ClassId exists).
    /// Keyset paginated on (Name, Id). Returns up to `limit` rows and a NextCursor when a full page returns.
    public async Task<(IReadOnlyList<StudentResponse> Rows, string? NextCursor)> ListByClassPagedAsync(
        Guid classId, int limit, string? cursor, CancellationToken ct = default)
    {
        string? lastName = null; Guid? lastId = null;
        var decoded = Sms.Shared.Kernel.Http.Cursor.Decode(cursor);
        if (decoded is not null)
        {
            var i = decoded.IndexOf('|');
            if (i > 0 && Guid.TryParse(decoded[(i + 1)..], out var g)) { lastName = decoded[..i]; lastId = g; }
        }

        var rows = await QueryInlineAsync<StudentResponse>(
            $@"SELECT TOP (@limit) {Cols} FROM dbo.Students s
               WHERE EXISTS (SELECT 1 FROM dbo.Classes c
                             WHERE c.Id = @classId AND c.Grade = s.Grade AND c.Section = s.Section)
                 AND (@lastName IS NULL OR s.Name > @lastName
                      OR (s.Name = @lastName AND s.Id > @lastId))
               ORDER BY s.Name, s.Id",
            new { classId, limit, lastName, lastId }, ct);

        string? next = rows.Count == limit
            ? Sms.Shared.Kernel.Http.Cursor.Encode($"{rows[^1].Name}|{rows[^1].Id}")
            : null;
        return (rows, next);
    }
}

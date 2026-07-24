namespace Sms.Modules.Staffing.Contracts;

// ---- Teacher ----
public sealed record TeacherResponse(
    Guid Id, Guid TenantId, string Name, string? Gender, string? Department, string? Designation,
    IReadOnlyList<string> Subjects, string? ClassTeacher, string? Phone, string? Email, int Exp, decimal Rating,
    decimal AttendancePct, decimal Result, int Load, string Status, int AvatarHue, bool Top,
    string? EmployeeCode = null);

public sealed record TeacherRow(
    Guid Id, Guid TenantId, string Name, string? Gender, string? Department, string? Designation,
    string? SubjectsCsv, string? ClassTeacher, string? Phone, string? Email, int Exp, decimal Rating,
    decimal AttendancePct, decimal Result, int Load, string Status, int AvatarHue, bool Top,
    string? EmployeeCode = null);

public sealed record CreateTeacherRequest(
    string Name, string? Gender, string? Department, string? Designation, IReadOnlyList<string>? Subjects,
    string? ClassTeacher, string? Phone, string? Email, int Exp, decimal Rating, decimal Result, int Load,
    int AvatarHue, bool Top, string? EmployeeCode = null);

public sealed record UpdateTeacherRequest(
    string? Name, string? Department, string? Designation, IReadOnlyList<string>? Subjects,
    string? ClassTeacher, string? Phone, string? Email, string? Status);

// ---- Staff ----
public sealed record StaffResponse(
    Guid Id, Guid TenantId, string Name, string? Gender, string? Role, string? Category, string? Department,
    string? Phone, string? Shift, string? Route, decimal AttendancePct, string Status, int AvatarHue,
    string? EmployeeCode = null);

public sealed record CreateStaffRequest(
    string Name, string? Gender, string? Role, string? Category, string? Department, string? Phone,
    string? Shift, string? Route, int AvatarHue, string? EmployeeCode = null);

public sealed record UpdateStaffRequest(
    string? Name, string? Role, string? Category, string? Department, string? Phone, string? Shift,
    string? Route, string? Status);

public static class StaffingMappers
{
    public static TeacherResponse ToResponse(this TeacherRow r) => new(
        r.Id, r.TenantId, r.Name, r.Gender, r.Department, r.Designation,
        string.IsNullOrEmpty(r.SubjectsCsv) ? [] : r.SubjectsCsv.Split(','),
        r.ClassTeacher, r.Phone, r.Email, r.Exp, r.Rating, r.AttendancePct, r.Result, r.Load,
        r.Status, r.AvatarHue, r.Top, r.EmployeeCode);
}

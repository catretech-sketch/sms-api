namespace Sms.Application.Interfaces.DAO;

public sealed record LinkedPersonProfile(
    string? Designation,
    string? ClassTeacher,
    string? Phone,
    string? Email,
    string? EmployeeCode,
    DateTime? JoinedAt,
    string? HomeroomClassName = null,
    /// <summary>
    /// Staff-only: the duty post to display (Staff.Route when set, else Staff.Department, else
    /// empty string). Always null on the linked-Teacher profile.
    /// </summary>
    string? DutyPost = null);

public interface IProfileDao
{
    Task<LinkedPersonProfile?> GetLinkedTeacherAsync(
        Guid userId, Guid? tenantId, string? email, string? name, CancellationToken ct = default);

    Task<LinkedPersonProfile?> GetLinkedStaffAsync(
        Guid userId, Guid? tenantId, string? email, string? name, CancellationToken ct = default);

    /// Phone from any Teachers/Staff row for this person (matched by email or name).
    Task<string?> GetSharedPhoneFromRosterAsync(string? email, string? name, CancellationToken ct = default);
}

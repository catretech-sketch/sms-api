namespace Sms.Modules.Sis.Contracts;

public sealed record StudentResponse(
    Guid Id, Guid TenantId, string AdmissionNo, string Name, string? Gender, string? Grade, string? Section,
    string? ClassLabel, int Roll, string? GuardianName, string? GuardianPhone, decimal AttendancePct,
    string FeeStatus, decimal FeeDue, string Status, string? House, int AvatarHue,
    DateTime? Dob, string? Email, string? Address);

public sealed record CreateStudentRequest(
    string? AdmissionNo, string Name, string? Gender, string? Grade, string? Section, int Roll,
    string? GuardianName, string? GuardianPhone, string? House, int AvatarHue,
    DateTime? Dob, string? Email, string? Address);

public sealed record UpdateStudentRequest(
    string? Name, string? Grade, string? Section, int? Roll, string? GuardianName, string? GuardianPhone,
    string? House, string? FeeStatus, decimal? FeeDue, string? Status);

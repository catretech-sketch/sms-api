namespace Sms.Shared.Kernel.Authz;

/// Canonical RBAC policy names mirroring the frontend permission matrices.
public static class Policies
{
    public const string PlatformOnly = "platform.only";          // Catre team
    public const string SchoolAdmin = "school.admin";
    public const string Principal = "school.principal";
    public const string Teacher = "school.teacher";
    public const string Staff = "staff";
    public const string StudentOrParent = "student.parent";

    public static readonly string[] All =
        [PlatformOnly, SchoolAdmin, Principal, Teacher, Staff, StudentOrParent];
}

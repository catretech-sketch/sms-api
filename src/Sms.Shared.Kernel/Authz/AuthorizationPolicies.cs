using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Sms.Shared.Kernel.Authz;

public static class AuthorizationPolicies
{
    /// Single place that maps policy names -> required roles, mirroring the frontend permission matrix.
    public const string TeacherApp = "teacher.app"; // teacher OR principal OR school admin

    public static IServiceCollection AddSmsAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy("platform", p => p.RequireClaim("is_platform", "1"))
            .AddPolicy(Policies.SchoolAdmin, p => p.RequireRole(Policies.SchoolAdmin, Policies.SchoolOwner))
            .AddPolicy(Policies.Principal, p => p.RequireRole(Policies.Principal, Policies.SchoolAdmin, Policies.SchoolOwner))
            .AddPolicy(TeacherApp, p => p.RequireRole(Policies.Teacher, Policies.Principal, Policies.SchoolAdmin, Policies.SchoolOwner))
            // Student & Parent app — the only role that resolves a linked student (e.g. child's live bus).
            .AddPolicy(Policies.StudentOrParent, p => p.RequireRole(Policies.StudentOrParent))
            .Services;
}

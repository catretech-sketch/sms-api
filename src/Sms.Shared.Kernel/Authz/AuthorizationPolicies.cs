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
            .AddPolicy(Policies.SchoolAdmin, p => p.RequireRole(Policies.SchoolAdmin))
            .AddPolicy(Policies.Principal, p => p.RequireRole(Policies.Principal, Policies.SchoolAdmin))
            .AddPolicy(TeacherApp, p => p.RequireRole(Policies.Teacher, Policies.Principal, Policies.SchoolAdmin))
            .Services;
}

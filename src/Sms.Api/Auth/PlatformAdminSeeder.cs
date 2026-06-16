using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Auth;

/// Ensures exactly one Catre platform admin exists. Idempotent: runs every boot,
/// no-ops once seeded. The admin logs in via the existing email OTP flow.
public static class PlatformAdminSeeder
{
    public static async Task RunAsync(WebApplication app)
    {
        var email = app.Configuration["Catre:AdminEmail"]?.Trim();
        var phone = app.Configuration["Catre:AdminPhone"]?.Trim();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PlatformAdminSeeder");

        if (string.IsNullOrWhiteSpace(email))
        {
            log.LogWarning("No Catre:AdminEmail configured; platform admin NOT seeded. " +
                "The Catre admin surface is unreachable until a platform admin exists.");
            return;
        }

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.Set(null, null, isPlatform: true); // platform context => RLS bypass for the seed write
            var repo = scope.ServiceProvider.GetRequiredService<UserProvisioningRepository>();

            if (await repo.PlatformAdminExistsAsync())
            {
                log.LogInformation("Platform admin present; bootstrap skipped.");
                return;
            }

            await repo.CreateUserAsync(
                tenantId: null,
                email: email,
                phone: string.IsNullOrWhiteSpace(phone) ? null : phone,
                isPlatform: true,
                roles: [Policies.PlatformOnly]);
            log.LogInformation("Seeded Catre platform admin {Email}.", email);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Platform admin bootstrap failed; continuing startup. " +
                "The Catre admin surface may be unreachable until this is resolved.");
            return;
        }
    }
}

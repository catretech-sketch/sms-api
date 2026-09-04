using Sms.Application.Interfaces.DAO;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Transport;

public sealed class TransportAuthorizationResolver(
    TripRepository trips,
    BusRepository buses,
    StudentBusRepository studentBus,
    IAuthDao users,
    ITenantContext tenant) : ITransportAuthorizationResolver
{
    public async Task<bool> CanViewBusAsync(
        Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles,
        Guid busId, CancellationToken ct = default)
    {
        // Hub method invocations don't flow through the HTTP-request middleware
        // that normally populates ITenantContext, so it must be set explicitly
        // here before any repository call relies on RLS session context —
        // same pattern as AbsenceAlertWorker's manual `tenant.Set(...)`.
        tenant.Set(callerTenantId, callerUserId, isPlatform: false);

        if (callerRoles.Contains(Policies.Principal) || callerRoles.Contains(Policies.SchoolAdmin) || callerRoles.Contains(Policies.SchoolOwner))
            return await studentBus.BusExistsAsync(busId, ct);

        if (callerRoles.Contains(Policies.Teacher))
            return await buses.IsDutyTeacherForBusAsync(callerUserId, busId, ct);

        if (callerRoles.Contains(Policies.Driver) || callerRoles.Contains("conductor"))
            return await trips.GetActiveDriverOrConductorRoleByBusAsync(busId, callerUserId, ct) is not null;

        if (callerRoles.Contains(Policies.StudentOrParent) || callerRoles.Contains("parent") || callerRoles.Contains("student"))
        {
            var me = await users.GetByIdAsync(callerUserId, ct);
            if (me?.StudentId is not { Length: > 0 } admissionNo) return false;
            return await studentBus.HasChildOnBusAsync(admissionNo, busId, ct);
        }

        return false;
    }
}

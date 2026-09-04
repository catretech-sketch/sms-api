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

        // NOTE on role gates below: the real staff auto-provisioning flow (Staff_EnsureLogin.sql)
        // assigns every staff member exactly one role, `Policies.Staff` ("staff") — it never inserts
        // "school.teacher", "driver", or "conductor" into dbo.UserRoles (those only appear if an
        // admin manually assigns them via UserService.ReplaceRolesAsync). So the teacher and
        // driver/conductor branches below must ALSO admit Policies.Staff, or a real teacher/driver/
        // conductor whose JWT only carries "staff" would be denied JoinBus on their own bus. Admitting
        // "staff" here grants nothing extra on its own: the actual narrowing to "this specific teacher
        // for this specific bus" / "this specific active driver for this specific bus" is done by the
        // DB checks (IsDutyTeacherForBusAsync / GetActiveDriverOrConductorRoleByBusAsync), so a staff
        // member who isn't the assigned duty teacher or active driver for this bus is still denied.
        //
        // Each branch below is tried independently and falls through on failure (rather than
        // returning false immediately) so that a caller holding multiple roles (e.g. a teacher who is
        // also a parent) gets the union of what each of their roles would grant them — not just
        // whichever role-check happens to run first.

        if (callerRoles.Contains(Policies.Principal) || callerRoles.Contains(Policies.SchoolAdmin) || callerRoles.Contains(Policies.SchoolOwner))
        {
            if (await studentBus.BusExistsAsync(busId, ct)) return true;
        }

        if (callerRoles.Contains(Policies.Teacher) || callerRoles.Contains(Policies.Staff))
        {
            if (await buses.IsDutyTeacherForBusAsync(callerUserId, busId, ct)) return true;
        }

        if (callerRoles.Contains(Policies.Driver) || callerRoles.Contains("conductor") || callerRoles.Contains(Policies.Staff))
        {
            if (await trips.GetActiveDriverOrConductorRoleByBusAsync(busId, callerUserId, ct) is not null) return true;
        }

        if (callerRoles.Contains(Policies.StudentOrParent) || callerRoles.Contains("parent") || callerRoles.Contains("student"))
        {
            var me = await users.GetByIdAsync(callerUserId, ct);
            if (me?.StudentId is { Length: > 0 } admissionNo && await studentBus.HasChildOnBusAsync(admissionNo, busId, ct))
                return true;
        }

        return false;
    }
}

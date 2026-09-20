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
            if (await buses.IsTravelingTeacherForBusAsync(callerUserId, busId, ct)) return true;
        }

        if (callerRoles.Contains(Policies.Driver) || callerRoles.Contains("conductor") || callerRoles.Contains(Policies.Staff))
        {
            if (await trips.GetActiveDriverOrConductorRoleByBusAsync(busId, callerUserId, ct) is not null) return true;
        }

        if (callerRoles.Contains(Policies.StudentOrParent) || callerRoles.Contains("parent") || callerRoles.Contains("student"))
        {
            if (await studentBus.HasLinkedChildOnBusAsync(callerUserId, busId, ct))
                return true;
            var me = await users.GetByIdAsync(callerUserId, ct);
            if (me?.StudentId is { Length: > 0 } admissionNo && await studentBus.HasChildOnBusAsync(admissionNo, busId, ct))
                return true;
        }

        return false;
    }

    /// Route-level equivalent of CanViewBusAsync: a route is visible to a caller if any bus
    /// currently assigned to that route would itself be visible to them under CanViewBusAsync.
    /// Deliberately reuses CanViewBusAsync per bus rather than re-implementing role checks here.
    ///
    /// Admin fast-path: mirrors CanViewBusAsync's own first branch (tenant-scoped existence check
    /// for Principal/SchoolAdmin/SchoolOwner) so a route with zero buses assigned yet — e.g. one an
    /// admin is still building out in the route-builder flow, before any bus has been attached — is
    /// still visible to that same admin, rather than being invisible to everyone including its owner.
    public async Task<bool> CanViewRouteAsync(
        Guid callerUserId, Guid callerTenantId, IReadOnlyCollection<string> callerRoles,
        Guid routeId, CancellationToken ct = default)
    {
        tenant.Set(callerTenantId, callerUserId, isPlatform: false);

        if (callerRoles.Contains(Policies.Principal) || callerRoles.Contains(Policies.SchoolAdmin) || callerRoles.Contains(Policies.SchoolOwner))
        {
            if (await buses.RouteExistsAsync(routeId, ct)) return true;
        }

        var busIds = await buses.ListBusIdsForRouteAsync(routeId, ct);
        foreach (var busId in busIds)
            if (await CanViewBusAsync(callerUserId, callerTenantId, callerRoles, busId, ct))
                return true;

        // A student's own StudentBusAssignments.RouteId can diverge from their assigned
        // bus's Buses.RouteId (the bus-side column is a separate, sometimes-stale value),
        // so a caller's OWN route assignment must be checked directly rather than only
        // via the bus-by-bus loop above — otherwise a route id sourced from the caller's
        // own assignment record (e.g. from /v1/me/children/bus) can be denied even though
        // it genuinely is their child's assigned route.
        if (callerRoles.Contains(Policies.StudentOrParent) || callerRoles.Contains("parent") || callerRoles.Contains("student"))
        {
            if (await studentBus.HasLinkedChildOnRouteAsync(callerUserId, routeId, ct))
                return true;
            var me = await users.GetByIdAsync(callerUserId, ct);
            if (me?.StudentId is { Length: > 0 } admissionNo && await studentBus.HasChildOnRouteAsync(admissionNo, routeId, ct))
                return true;
        }

        return false;
    }
}

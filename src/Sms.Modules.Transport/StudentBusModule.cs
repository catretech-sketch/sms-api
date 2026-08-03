using Microsoft.Extensions.DependencyInjection;
using Sms.Shared.Kernel.Data;

namespace Sms.Modules.Transport;

/// One student's bus assignment, as shown on the admin roster for a bus.
public sealed record StudentBusAssignmentResponse(
    Guid StudentId, string StudentName, string Initials, string AdmissionNo,
    Guid BusId, string BusNo, string? RouteName, Guid? StopId, string? StopName);

/// Live position of a parent's child bus (post status-derivation), for the parent app.
public sealed record ChildBusPositionResponse(
    Guid StudentId, string StudentName, string AdmissionNo,
    Guid BusId, string BusNo, string? RouteName, string Status,
    double? Lat, double? Lng, double? SpeedKmh, string? NextStopName, DateTime? LastPingAt);

/// Raw per-child live row before status derivation.
public sealed record ChildBusRow(
    Guid StudentId, string StudentName, string AdmissionNo,
    Guid BusId, string BusNo, string? RouteName,
    Guid? TripId, double? Lat, double? Lng, double? SpeedKmh, DateTime? LastPingAt);

public sealed class StudentBusRepository(IDbConnectionFactory factory) : BaseRepository(factory)
{
    private sealed record AssignmentRow(
        Guid StudentId, string StudentName, string AdmissionNo,
        Guid BusId, string BusNo, string? RouteName, Guid? StopId, string? StopName);

    public Task AssignAsync(Guid tenantId, Guid studentId, Guid busId, Guid? stopId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.StudentBus_Assign",
            new { TenantId = tenantId, StudentId = studentId, BusId = busId, StopId = stopId }, ct);

    public Task UnassignAsync(Guid tenantId, Guid studentId, CancellationToken ct = default) =>
        ExecuteProcAsync("dbo.StudentBus_Unassign",
            new { TenantId = tenantId, StudentId = studentId }, ct);

    // RLS-scoped existence guards: a caller can never see another tenant's bus/student,
    // so these double as cross-tenant reference protection before an upsert.
    public async Task<bool> BusExistsAsync(Guid busId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>(
            "SELECT COUNT(1) FROM dbo.Buses WHERE Id = @busId", new { busId }, ct)).First() > 0;

    public async Task<bool> StudentExistsAsync(Guid studentId, CancellationToken ct = default) =>
        (await QueryInlineAsync<int>(
            "SELECT COUNT(1) FROM dbo.Students WHERE Id = @studentId", new { studentId }, ct)).First() > 0;

    public async Task<IReadOnlyList<StudentBusAssignmentResponse>> ListByBusAsync(Guid busId, CancellationToken ct = default)
    {
        var rows = await QueryInlineAsync<AssignmentRow>(
            @"SELECT sba.StudentId, s.Name AS StudentName, s.AdmissionNo,
                     sba.BusId, b.BusNo, b.RouteName, sba.StopId,
                     COALESCE(rs.Name, bs.Name) AS StopName
              FROM dbo.StudentBusAssignments sba
              JOIN dbo.Students s ON s.Id = sba.StudentId
              JOIN dbo.Buses b ON b.Id = sba.BusId
              LEFT JOIN dbo.RouteStops rs ON rs.Id = sba.StopId
              LEFT JOIN dbo.BusStops bs ON bs.Id = sba.StopId
              WHERE sba.BusId = @busId ORDER BY s.Name", new { busId }, ct);
        return rows.Select(r => new StudentBusAssignmentResponse(
            r.StudentId, r.StudentName, BusRepository.Initials(r.StudentName), r.AdmissionNo,
            r.BusId, r.BusNo, r.RouteName, r.StopId, r.StopName)).ToList();
    }

    /// The live bus for each student whose AdmissionNo matches (RLS scopes this to the caller's tenant,
    /// so identical admission numbers in other schools are never returned).
    public Task<IReadOnlyList<ChildBusRow>> ChildrenBusByAdmissionAsync(string admissionNo, CancellationToken ct = default) =>
        QueryInlineAsync<ChildBusRow>(
            @"SELECT s.Id AS StudentId, s.Name AS StudentName, s.AdmissionNo,
                     b.Id AS BusId, b.BusNo, b.RouteName,
                     t.Id AS TripId, p.Lat, p.Lng, p.SpeedKmh, p.At AS LastPingAt
              FROM dbo.Students s
              JOIN dbo.StudentBusAssignments sba ON sba.StudentId = s.Id
              JOIN dbo.Buses b ON b.Id = sba.BusId
              OUTER APPLY (
                SELECT TOP 1 tt.Id, tt.StartedAt FROM dbo.Trips tt
                WHERE tt.BusId = b.Id AND tt.Status = 'live' ORDER BY tt.StartedAt DESC) t
              OUTER APPLY (
                SELECT TOP 1 pp.Lat, pp.Lng, pp.SpeedKmh, pp.At FROM dbo.TripPings pp
                WHERE pp.TripId = t.Id ORDER BY pp.At DESC) p
              WHERE s.AdmissionNo = @admissionNo
              ORDER BY s.Name", new { admissionNo }, ct);
}

public static class StudentBusModule
{
    public static IServiceCollection AddStudentBusModule(this IServiceCollection services)
    {
        services.AddScoped<StudentBusRepository>();
        return services;
    }
}

namespace Sms.Modules.Hostel.Contracts;

public sealed record HostelBlockResponse(Guid Id, Guid TenantId, string Name, string? Warden);
public sealed record CreateHostelBlockRequest(string Name, string? Warden);

public sealed record HostelRoomResponse(
    Guid Id, Guid TenantId, Guid BlockId, string? BlockName, string RoomNo, int Capacity, int Residents);
public sealed record CreateHostelRoomRequest(Guid BlockId, string RoomNo, int Capacity);

public sealed record HostelResidentResponse(
    Guid Id, Guid TenantId, Guid RoomId, string? RoomNo, string StudentName, Guid? StudentId);
public sealed record CreateHostelResidentRequest(Guid RoomId, string StudentName, Guid? StudentId);

/// Aggregate KPIs for the Operations · Hostel dashboard.
public sealed record HostelSummaryResponse(int Blocks, int Rooms, int Residents, int OccupancyPct);

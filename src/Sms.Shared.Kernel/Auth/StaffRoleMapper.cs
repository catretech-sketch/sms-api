namespace Sms.Shared.Kernel.Auth;

/// <summary>
/// Normalizes the CRM's free-text <c>dbo.Staff.Role</c> label into one of the six canonical
/// duty-role keys the staff mobile app understands. Pure/stateless — safe to unit test in
/// isolation and reuse from any layer (DAO row mapping, application services, etc.).
/// Unrecognized or missing input never throws; it simply yields no canonical key so callers
/// can omit the field rather than guess and mislabel someone.
/// </summary>
public static class StaffRoleMapper
{
    /// <summary>
    /// Maps a free-text staff role label (as stored on <c>dbo.Staff.Role</c>) to the canonical
    /// <c>role_key</c> the staff app expects: driver, conductor, guard, peon, sweeper, gardener.
    /// Matching is case-insensitive and tolerant of surrounding whitespace. Returns <c>null</c>
    /// for null/blank/unrecognized input.
    /// </summary>
    public static string? ToRoleKey(string? rawRole)
    {
        if (string.IsNullOrWhiteSpace(rawRole)) return null;

        return rawRole.Trim().ToLowerInvariant() switch
        {
            "driver" => "driver",
            "conductor" or "bus attendant" => "conductor",
            "watchman" or "security guard" => "guard",
            "peon" => "peon",
            "sweeper" => "sweeper",
            "gardener" => "gardener",
            _ => null,
        };
    }
}

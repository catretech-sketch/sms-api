using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Auth;

namespace Sms.Application.Services.Auth;

/// <summary>Phone is personal — shared across all schools for the same person.</summary>
internal static class SharedPhoneResolver
{
    /// Caller must set platform tenant context so Users RLS sees all peer rows.
    public static async Task<string?> ResolveAsync(
        IAuthDao auth, IProfileDao profiles, string? email, string? name, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            foreach (var peer in await auth.ListByEmailAsync(email, ct))
            {
                if (!string.IsNullOrWhiteSpace(peer.Phone))
                    return peer.Phone.Trim();
            }
        }

        return await profiles.GetSharedPhoneFromRosterAsync(email, name, ct);
    }
}

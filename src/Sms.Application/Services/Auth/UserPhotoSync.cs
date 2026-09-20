using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Auth;

namespace Sms.Application.Services.Auth;

/// <summary>When a user switches schools, copy a profile photo onto the target
/// tenant's Users row if it is missing but another linked account has one.</summary>
internal static class UserPhotoSync
{
    public static async Task EnsureTargetHasPhotoAsync(
        IAuthDao auth, UserRecord source, UserRecord target, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(target.PhotoUrl)) return;

        var photo = source.PhotoUrl;
        if (string.IsNullOrWhiteSpace(photo) && !string.IsNullOrWhiteSpace(source.Email))
        {
            var peers = await auth.ListByEmailAsync(source.Email, ct);
            photo = peers.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.PhotoUrl))?.PhotoUrl;
        }

        if (!string.IsNullOrWhiteSpace(photo))
            await auth.SetPhotoAsync(target.Id, photo, ct);
    }
}

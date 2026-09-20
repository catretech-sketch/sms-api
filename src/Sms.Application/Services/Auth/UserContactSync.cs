using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Auth;

namespace Sms.Application.Services.Auth;

/// <summary>When a user switches schools, copy personal contact fields onto the target
/// tenant's Users row if missing but another linked account has them.</summary>
internal static class UserContactSync
{
    public static async Task EnsureTargetHasContactAsync(
        IAuthDao auth, IProfileDao profiles, UserRecord source, UserRecord target, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(target.Phone)) return;

        var email = FirstNonEmpty(target.Email, source.Email);
        var name = FirstNonEmpty(target.Name, source.Name);
        var phone = await SharedPhoneResolver.ResolveAsync(auth, profiles, email, name, ct);

        if (!string.IsNullOrWhiteSpace(phone))
            await auth.SetPhoneAsync(target.Id, phone, ct);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }
}

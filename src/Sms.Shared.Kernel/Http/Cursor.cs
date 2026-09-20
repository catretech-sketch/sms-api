using System.Text;

namespace Sms.Shared.Kernel.Http;

/// Opaque keyset-pagination cursor. Encodes the last row's sort key as URL-safe base64 (no padding)
/// so clients treat it as a blob. Decode returns null for null/empty/malformed input (= start over).
public static class Cursor
{
    public static string Encode(string rawSortKey)
    {
        var b = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawSortKey));
        return b.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        var b = cursor.Replace('-', '+').Replace('_', '/');
        switch (b.Length % 4) { case 2: b += "=="; break; case 3: b += "="; break; }
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b)); }
        catch (FormatException) { return null; }
    }
}

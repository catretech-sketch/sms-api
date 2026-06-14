namespace Sms.Shared.Kernel.Http;

public sealed record PageRequest(int Limit = 50, string? Cursor = null)
{
    public int SafeLimit => Limit is < 1 or > 200 ? 50 : Limit;
}

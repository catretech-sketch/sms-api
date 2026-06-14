namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsPlatform { get; private set; }

    public void Set(Guid? tenantId, Guid? userId, bool isPlatform)
    {
        TenantId = tenantId;
        UserId = userId;
        IsPlatform = isPlatform;
    }
}

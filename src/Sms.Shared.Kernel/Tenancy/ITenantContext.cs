namespace Sms.Shared.Kernel.Tenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsPlatform { get; }
    void Set(Guid? tenantId, Guid? userId, bool isPlatform);
}

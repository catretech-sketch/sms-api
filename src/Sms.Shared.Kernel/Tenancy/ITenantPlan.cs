namespace Sms.Shared.Kernel.Tenancy;

public interface ITenantPlan
{
    Guid? TenantId { get; }
    string Tier { get; }
    string Status { get; }
    void Set(Guid? tenantId, string tier, string status);
}

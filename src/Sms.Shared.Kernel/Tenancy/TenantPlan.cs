namespace Sms.Shared.Kernel.Tenancy;

public sealed class TenantPlan : ITenantPlan
{
    public Guid? TenantId { get; private set; }
    public string Tier { get; private set; } = "";
    public string Status { get; private set; } = "";

    public void Set(Guid? tenantId, string tier, string status)
    {
        TenantId = tenantId;
        Tier = tier ?? "";
        Status = status ?? "";
    }
}

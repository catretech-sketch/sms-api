namespace Sms.Modules.Tenancy.Contracts;

public sealed record PlanUpgradeRequestResponse(
    Guid Id,
    Guid TenantId,
    string? TenantName,
    Guid? FromPlanId,
    string? FromPlanName,
    string? FromTier,
    Guid ToPlanId,
    string? ToPlanName,
    string? ToTier,
    decimal Amount,
    string Currency,
    string Mode,
    string Status,
    Guid? InvoiceId,
    string? RazorpayOrderId,
    string? RazorpayPaymentId,
    Guid? RequestedByUserId,
    Guid? ReviewedByUserId,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreatePlanUpgradeRequest(Guid PlanId, string Mode);

/// <summary>Platform (Catre) create for new-school activation or plan change with payment.</summary>
public sealed record CreatePlanPaymentRequest(Guid PlanId, string Mode);

public sealed record RejectPlanUpgradeRequest(string? Notes);

public sealed record ConfirmPlanUpgradePaymentRequest(
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature);

public sealed record RazorpayOrderResponse(
    string KeyId,
    string OrderId,
    long AmountPaise,
    string Currency,
    string Name,
    Guid UpgradeRequestId);

/// <summary>Whether online Razorpay checkout can start (keys present on the server).</summary>
public sealed record PaymentGatewayStatusResponse(bool RazorpayConfigured);

public static class PlanUpgradeModes
{
    public const string Online = "online";
    public const string Offline = "offline";
}

public static class PlanUpgradeStatuses
{
    public const string PendingPayment = "pending_payment";
    public const string PendingOffline = "pending_offline";
    public const string PaidPendingApproval = "paid_pending_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

public static class PlanTier
{
    public static int Rank(string? tier)
    {
        var t = (tier ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "silver" => 1,
            "gold" => 2,
            "platinum" => 3,
            _ => 0,
        };
    }

    public static bool IsUpgrade(string? fromTier, string? toTier) =>
        Rank(toTier) > Rank(fromTier);
}

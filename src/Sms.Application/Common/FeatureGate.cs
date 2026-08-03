using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Common;

/// <summary>Plan-tier feature checks for application services (mirrors sms-admin TierGate).</summary>
public static class FeatureGate
{
    public static bool Allowed(ITenantContext tenant, ITenantFeatureSet features, string featureKey) =>
        tenant.IsPlatform || features.Has(featureKey);

    public static ApiResult<T> Locked<T>(string featureKey) =>
        ApiResult<T>.Fail(new Error("feature_locked",
            $"This feature ({featureKey}) is not available on your plan."), 403);

    public static ApiResult Locked(string featureKey) =>
        ApiResult.Fail(new Error("feature_locked",
            $"This feature ({featureKey}) is not available on your plan."), 403);
}

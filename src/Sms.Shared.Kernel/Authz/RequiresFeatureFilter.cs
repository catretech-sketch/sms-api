using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Tenancy;
using KernelResults = Sms.Shared.Kernel.Results;

namespace Sms.Shared.Kernel.Authz;

/// Endpoint filter enforcing a RequiresFeatureAttribute on the endpoint. Opt in with
/// `.RequiresFeature("transport.gps")` (the route-builder helper below).
public sealed class RequiresFeatureFilter(string feature) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var features = ctx.HttpContext.RequestServices.GetService(typeof(ITenantFeatureSet)) as ITenantFeatureSet;
        var tenant = ctx.HttpContext.RequestServices.GetService(typeof(ITenantContext)) as ITenantContext;
        var code = Evaluate(features, feature, tenant?.IsPlatform ?? false);
        if (code == 0) return await next(ctx);
        return Microsoft.AspNetCore.Http.Results.Json(
            ErrorEnvelope.From(new KernelResults.Error("feature_locked",
                $"This feature ({feature}) is not available on your plan.")), statusCode: code);
    }

    /// 0 = allow; 403 = locked. Pure for testing.
    public static int Evaluate(ITenantFeatureSet? features, string feature, bool isPlatform)
    {
        if (isPlatform) return 0;
        return features is not null && features.Has(feature) ? 0 : StatusCodes.Status403Forbidden;
    }
}

public static class RequiresFeatureExtensions
{
    public static TBuilder RequiresFeature<TBuilder>(this TBuilder builder, string feature)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequiresFeatureFilter(feature));
        return builder;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Hostel.Data;

namespace Sms.Modules.Hostel;

public static class HostelModule
{
    public static IServiceCollection AddHostelModule(this IServiceCollection services)
    {
        services.AddScoped<HostelRepository>();
        return services;
    }
}

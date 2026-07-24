using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Sports.Data;

namespace Sms.Modules.Sports;

public static class SportsModule
{
    public static IServiceCollection AddSportsModule(this IServiceCollection services)
    {
        services.AddScoped<SportsRepository>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Sis.Data;

namespace Sms.Modules.Sis;

public static class SisModule
{
    public static IServiceCollection AddSisModule(this IServiceCollection services)
    {
        services.AddScoped<StudentRepository>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.AiSearch.Data;

namespace Sms.Modules.AiSearch;

public static class AiSearchModule
{
    public static IServiceCollection AddAiSearchModule(this IServiceCollection services)
    {
        services.AddScoped<AiSearchLogRepository>();
        services.AddScoped<AiSearchConversationRepository>();
        return services;
    }
}

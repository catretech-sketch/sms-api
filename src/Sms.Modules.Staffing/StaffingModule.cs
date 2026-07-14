using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Staffing.Data;

namespace Sms.Modules.Staffing;

public static class StaffingModule
{
    public static IServiceCollection AddStaffingModule(this IServiceCollection services)
    {
        services.AddScoped<TeacherRepository>();
        services.AddScoped<StaffRepository>();
        services.AddScoped<LeaveRepository>();
        return services;
    }
}

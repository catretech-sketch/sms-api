using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Staffing.Data;
using Sms.Modules.Staffing.Profile;

namespace Sms.Modules.Staffing;

public static class StaffingModule
{
    public static IServiceCollection AddStaffingModule(this IServiceCollection services)
    {
        services.AddScoped<TeacherRepository>();
        services.AddScoped<StaffRepository>();
        services.AddScoped<LeaveRepository>();
        services.AddScoped<ProfileRepository>();
        return services;
    }
}

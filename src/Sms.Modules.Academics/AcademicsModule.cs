using Microsoft.Extensions.DependencyInjection;
using Sms.Modules.Academics.Data;

namespace Sms.Modules.Academics;

public static class AcademicsModule
{
    public static IServiceCollection AddAcademicsModule(this IServiceCollection services)
    {
        services.AddScoped<ClassRepository>();
        services.AddScoped<SubjectRepository>();
        services.AddScoped<AttendanceRepository>();
        services.AddScoped<PeriodAttendanceQueryRepository>();
        services.AddScoped<StaffAttendanceRepository>();
        services.AddScoped<ExamRepository>();
        services.AddScoped<HomeworkRepository>();
        services.AddScoped<TimetableRepository>();
        services.AddScoped<CalendarRepository>();
        services.AddScoped<LibraryRepository>();
        services.AddScoped<AssignmentRepository>();
        return services;
    }
}

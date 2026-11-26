using HR.Core.Infrastructure.Services.Attendance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure;

/// <summary>
/// PLAN-57: the attendance period's services and settings (section <c>Attendance</c>; D-LVA-9, 11, 12, 13). A calendar value that
/// cannot be read (a day name, a holiday's date) stops the host at start rather than being skipped.
/// </summary>
public static class AttendanceDependencyInjection
{
    public static IServiceCollection AddHrAttendance(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var options = services.AddOptions<AttendanceOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(AttendanceOptions.SectionName));
        options
            .Validate(
                o =>
                {
                    _ = new AttendanceCalendar(o.Calendar);
                    return true;
                },
                "Attendance:Calendar holds a value that cannot be read.")
            .ValidateOnStart();

        services.AddSingleton(sp => new AttendanceCalendar(sp.GetRequiredService<IOptions<AttendanceOptions>>(), sp.GetService<TimeProvider>()));
        services.AddScoped<AttendanceLockService>();
        services.AddScoped<AttendanceEligibility>();
        services.AddScoped<AttendanceDayWriter>();
        services.AddScoped<AttendancePeriodService>();
        services.AddScoped<AttendanceCorrectionService>();
        return services;
    }
}

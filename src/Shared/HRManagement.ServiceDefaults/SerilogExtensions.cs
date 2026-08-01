using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace HRManagement.ServiceDefaults;

public static class SerilogExtensions
{
    public static IHostBuilder UseHrManagementSerilog(this IHostBuilder host)
    {
        host.UseSerilog((context, services, configuration) =>
        {
            configuration.ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
                .Enrich.With<ActivityTraceEnricher>()
                .WriteTo.Console();

            var filePath = context.Configuration["Serilog:FilePath"];
            if (!string.IsNullOrEmpty(filePath))
            {
                configuration.WriteTo.File(
                    filePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Application} TraceId={TraceId} {Message:lj}{NewLine}{Exception}");
            }

            var seqUrl = context.Configuration["Serilog:SeqUrl"];
            if (!string.IsNullOrEmpty(seqUrl))
            {
                configuration.WriteTo.Seq(seqUrl);
            }
        });
        return host;
    }
}

/// <summary>OBS-001: push W3C TraceId/SpanId into Serilog properties.</summary>
public sealed class ActivityTraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}

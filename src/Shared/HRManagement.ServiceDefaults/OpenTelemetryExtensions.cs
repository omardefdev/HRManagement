using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;

using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HRManagement.ServiceDefaults;

public static class OpenTelemetryExtensions
{
    public static bool IsOpenTelemetryEnabled(IConfiguration configuration) =>
        configuration.GetValue("OpenTelemetry:Enabled", false)
        || !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    public static WebApplicationBuilder AddHrOpenTelemetry(this WebApplicationBuilder builder)
    {
        if (!IsOpenTelemetryEnabled(builder.Configuration))
            return builder;

        var serviceName = builder.Environment.ApplicationName;
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var samplingRatio = builder.Configuration.GetValue("OpenTelemetry:SamplingRatio", 0.1);

        builder.Services.AddSingleton<OutboxMetrics>();
        builder.Services.AddHostedService<OutboxMetricsHostedService>();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(
                        InboxMetrics.MeterName,
                        SagaMetrics.MeterName,
                        OutboxMetrics.MeterName,
                        JobActivitySource.MeterName,
                        ExportJobMetrics.MeterName,
                        ApprovalMetrics.MeterName,
                        PaymentExportMetrics.MeterName,
                        MigCutover.MigCutoverMetrics.MeterName,
                        NationalScope.NationalScopeMetrics.MeterName,
                        "hrm.payroll.run",
                        "MassTransit")
                    .AddPrometheusExporter();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/metrics")
                            && !ctx.Request.Path.StartsWithSegments("/health");
                        options.EnrichWithHttpRequest = (_, _) => { };
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource("MassTransit")
                    .AddSource(JobActivitySource.Name)
                    .AddProcessor(new PiiScrubActivityProcessor());

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter();
            })
            .WithLogging(logging =>
            {
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    logging.AddOtlpExporter();
            });

        return builder;
    }

    /// <summary>
    /// Maps Prometheus scrape endpoint, restricted to loopback / private (RFC1918 / ULA) clients.
    /// Combined with closed host ports this keeps /metrics off the public surface (SEC-001).
    /// </summary>
    public static WebApplication MapHrOpenTelemetryEndpoints(this WebApplication app)
    {
        if (!IsOpenTelemetryEnabled(app.Configuration))
            return app;

        app.MapPrometheusScrapingEndpoint()
            .AddEndpointFilter(async (context, next) =>
            {
                var remote = context.HttpContext.Connection.RemoteIpAddress;
                if (remote is null || !IsInternalClient(remote))
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Results.Empty;
                }

                return await next(context);
            });

        return app;
    }

    private static bool IsInternalClient(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10)
                return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }
}

/// <summary>OBS-001: drop / redact sensitive Activity tags before export.</summary>
public sealed class PiiScrubActivityProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "national_id", "NationalId", "password", "Password", "access_token", "Authorization",
        "client_secret", "ClientSecret", "http.request.body", "db.statement", "refresh_token",
        "POSTGRES_PASSWORD", "RABBITMQ_DEFAULT_PASS", "cookie", "Set-Cookie"
    };

    public override void OnEnd(Activity activity)
    {
        foreach (var tag in activity.TagObjects.ToList())
        {
            if (ForbiddenKeys.Contains(tag.Key) || LooksLikeSecretKey(tag.Key))
                activity.SetTag(tag.Key, "[redacted]");
        }
    }

    private static bool LooksLikeSecretKey(string key) =>
        key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase);
}

/// <summary>OBS-001: ActivitySource + meter for in-process hosted jobs.</summary>
public static class JobActivitySource
{
    public const string Name = "HRManagement.Jobs";
    public const string MeterName = "HRManagement.Jobs";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Cycles =
        Meter.CreateCounter<long>("job_cycles_total");

    public static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("job_failures_total");
}

/// <summary>OBS-001: export-job meter name for registration.</summary>
public static class ExportJobMetrics
{
    public const string MeterName = "HRManagement.ExportJobs";
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Completed =
        Meter.CreateCounter<long>("export_jobs_completed");
    public static readonly Counter<long> Failed =
        Meter.CreateCounter<long>("export_jobs_failed");

    public static void RecordCompleted(string serviceKey, string exportType) =>
        Completed.Add(1,
            new KeyValuePair<string, object?>("service", serviceKey),
            new KeyValuePair<string, object?>("export_type", exportType));

    public static void RecordFailed(string serviceKey, string exportType) =>
        Failed.Add(1,
            new KeyValuePair<string, object?>("service", serviceKey),
            new KeyValuePair<string, object?>("export_type", exportType));
}

/// <summary>OBS-001: approval workflow counters (HR.Core).</summary>
public static class ApprovalMetrics
{
    public const string MeterName = "HRManagement.Approvals";
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Submitted =
        Meter.CreateCounter<long>("approval_requests_submitted");
    public static readonly Counter<long> Approved =
        Meter.CreateCounter<long>("approval_requests_approved");
    public static readonly Counter<long> Rejected =
        Meter.CreateCounter<long>("approval_requests_rejected");
    public static readonly Counter<long> Escalated =
        Meter.CreateCounter<long>("approval_steps_escalated");

    public static void RecordSubmitted(string entityType) =>
        Submitted.Add(1, new KeyValuePair<string, object?>("entity_type", entityType));

    public static void RecordApproved(string entityType, bool fullyApproved) =>
        Approved.Add(1,
            new KeyValuePair<string, object?>("entity_type", entityType),
            new KeyValuePair<string, object?>("final", fullyApproved));

    public static void RecordRejected(string entityType) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("entity_type", entityType));

    public static void RecordEscalated(string entityType) =>
        Escalated.Add(1, new KeyValuePair<string, object?>("entity_type", entityType));
}

/// <summary>OBS-001: treasury/disbursement payment export counters (Payroll).</summary>
public static class PaymentExportMetrics
{
    public const string MeterName = "HRManagement.PaymentExport";
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> DisbursementSubmitted =
        Meter.CreateCounter<long>("payment_disbursement_exports");
    public static readonly Counter<long> DisbursementFailed =
        Meter.CreateCounter<long>("payment_disbursement_export_failures");
    public static readonly Counter<long> InsuranceSubmitted =
        Meter.CreateCounter<long>("payment_insurance_exports");
    public static readonly Counter<long> InsuranceFailed =
        Meter.CreateCounter<long>("payment_insurance_export_failures");

    public static void RecordDisbursement(bool success) =>
        (success ? DisbursementSubmitted : DisbursementFailed).Add(1);

    public static void RecordInsurance(bool success) =>
        (success ? InsuranceSubmitted : InsuranceFailed).Add(1);
}

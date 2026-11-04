using System.Diagnostics.Metrics;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>Counts <c>OutboxMessage</c> rows that a request staged and nothing saved (<c>outbox_unsaved_messages_total</c>).</summary>
public sealed class UnsavedOutboxMetrics
{
    private readonly Counter<long> _unsaved;

    public UnsavedOutboxMetrics(IHostEnvironment environment, IMeterFactory? meterFactory = null)
    {
        var meter = meterFactory?.Create(OutboxMetrics.MeterName) ?? new Meter(OutboxMetrics.MeterName);
        _unsaved = meter.CreateCounter<long>("outbox_unsaved_messages", unit: "{message}");
        ServiceName = environment.ApplicationName;
        // A zero sample, so a scrape shows the series (and the guard is visibly on) before anything goes wrong.
        Add(0, "none");
    }

    public string ServiceName { get; }

    public void Add(long count, string route) =>
        _unsaved.Add(count, new KeyValuePair<string, object?>("service", ServiceName), new KeyValuePair<string, object?>("route", route));
}

/// <summary>
/// PLAN-40 (§5, D-40-5): the runtime half of the guards. After the request, an <c>OutboxMessage</c> still in the
/// <c>Added</c> state means a publish was never saved: the event is lost with the scope. The counter goes up and an error
/// is logged; with <c>Messaging:FailOnUnsavedOutbox=true</c> (the default in Development, Acceptance and tests, never
/// assumed in Production) the request fails as well, so the mistake cannot pass a test run.
/// </summary>
public sealed class UnsavedOutboxGuardMiddleware(
    RequestDelegate next,
    UnsavedOutboxMetrics metrics,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<UnsavedOutboxGuardMiddleware> logger)
{
    public const string FailOnUnsavedKey = "Messaging:FailOnUnsavedOutbox";

    private readonly bool _fail = configuration.GetValue(FailOnUnsavedKey, FailsByDefault(environment));

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        var db = context.RequestServices.GetService<DbContext>();
        var unsaved = db is null ? 0 : OutboxScope.CountUnsaved(db);
        if (unsaved == 0)
            return;

        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
        metrics.Add(unsaved, route);
        logger.LogError(
            "{Count} outbox message(s) staged but never saved in {Service} on route {Route}: the events are lost",
            unsaved, metrics.ServiceName, route);

        if (_fail)
            throw new InvalidOperationException(
                $"{unsaved} outbox message(s) were published but never saved on route {route} (PLAN-40: publish, then one save).");
    }

    private static bool FailsByDefault(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Acceptance") || environment.IsEnvironment("Testing");
}

public static class UnsavedOutboxGuardExtensions
{
    /// <summary>Call after <c>UseRouting</c>, inside the exception handler, in every service with an outbox.</summary>
    public static IApplicationBuilder UseHrOutboxFlushGuard(this IApplicationBuilder app)
    {
        // Built here so the zero sample exists from the start and services need no extra registration.
        var metrics = ActivatorUtilities.GetServiceOrCreateInstance<UnsavedOutboxMetrics>(app.ApplicationServices);
        return app.UseMiddleware<UnsavedOutboxGuardMiddleware>(metrics);
    }
}

using System.Net;
using System.Text.Json;
using HR.Contracts.Application.Abstractions;
using HRManagement.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HR.Contracts.Infrastructure.Services;

/// <summary>Allows everything. PLAN-42: Development and Testing only; <see cref="EmployeeLifecycleGateStartupCheck"/> refuses it elsewhere.</summary>
public sealed class PassThroughEmployeeLifecycleGate : IEmployeeLifecycleGate
{
    public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, null));

    public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, null));
}

/// <summary>
/// FUN-002-B, PLAN-42 (G-SEP-12, D-S2S-4): reads HR.Core's legal-hold flag with a service token. 200 is judged by <c>legalHold</c>, 404
/// means the employee is not in the central record, and anything else refuses: no answer is never an allow.
/// </summary>
public sealed class HttpEmployeeLifecycleGate(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpEmployeeLifecycleGate> logger) : IEmployeeLifecycleGate
{
    public const string HttpClientName = "hr-core-lifecycle";
    public const string NotFoundMessage = "الموظف غير موجود في السجل المركزي.";
    public const string UnverifiedMessage = "تعذّر التحقق من الحجز القانوني — لم يُنفَّذ الإجراء.";
    public const string LegalHoldMessage = "الموظف تحت حجز قانوني (Legal Hold) — لا يمكن تنفيذ الفصل.";

    public Task<(bool Allowed, string? Error)> AssertCanStartSeparationAsync(Guid employeeId, CancellationToken ct) =>
        CheckAsync(employeeId, ct);

    public Task<(bool Allowed, string? Error)> AssertCanPublishSeparationAsync(Guid employeeId, CancellationToken ct) =>
        CheckAsync(employeeId, ct);

    private async Task<(bool Allowed, string? Error)> CheckAsync(Guid employeeId, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync($"api/internal/employees/{employeeId}/lifecycle-flags", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (false, NotFoundMessage);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogError("Legal-hold check answered HTTP {Status} for {EmployeeId}; refused", (int)response.StatusCode, employeeId);
                return (false, UnverifiedMessage);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("legalHold", out var hold) || hold.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                logger.LogError("Legal-hold check gave no legalHold flag for {EmployeeId}; refused", employeeId);
                return (false, UnverifiedMessage);
            }

            return hold.GetBoolean() ? (false, LegalHoldMessage) : (true, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Legal-hold check failed for {EmployeeId}; refused", employeeId);
            return (false, UnverifiedMessage);
        }
    }
}

/// <summary>PLAN-42 (G-SEP-12): the pass-through gate never reaches a deployment, whatever the registration.</summary>
public sealed class EmployeeLifecycleGateStartupCheck(IServiceProvider services, IHostEnvironment environment) : IHrStartupCheck
{
    public string Name => "employee-lifecycle-gate";

    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            return Task.CompletedTask;

        using var scope = services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IEmployeeLifecycleGate>() is PassThroughEmployeeLifecycleGate)
            throw new InvalidOperationException($"The legal-hold gate has no HR.Core address: set {HRManagement.ServiceDefaults.Integration.HrCoreEndpoint.Key}.");
        return Task.CompletedTask;
    }
}

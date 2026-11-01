using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Integration;

/// <summary>HCM-027 (E2): outcome of a Payroll grade lookup.</summary>
public abstract record PayrollGradeLookupResult
{
    private PayrollGradeLookupResult() { }

    /// <summary>The grade exists in the salary scale effective at the requested date.</summary>
    public sealed record Found(decimal MinSalary, decimal MaxSalary, decimal BaseSalary, string? GradeNameAr)
        : PayrollGradeLookupResult;

    /// <summary>Payroll answered: no active salary scale defines this grade at the requested date.</summary>
    public sealed record NotFound : PayrollGradeLookupResult;

    /// <summary>Payroll could not be asked (not configured, transport error, timeout, 5xx, auth failure).</summary>
    public sealed record Unavailable(string Reason) : PayrollGradeLookupResult;
}

/// <summary>
/// HCM-027 (E2): reads the grade bounds of the Payroll salary scale effective at <c>asOf</c>
/// (<c>GET api/internal/pay-grades/{gradeCode}?asOf=yyyy-MM-dd</c>, scope <c>payroll.grades.read</c>).
/// </summary>
public interface IPayrollGradeLookup
{
    Task<PayrollGradeLookupResult> GetAsync(string gradeCode, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// HCM-027 (E2): S2S client for Payroll's internal pay-grade endpoint. The caller's tenant travels in
/// <c>X-Tenant-ID</c> (<see cref="ForwardTenantHandler"/>); Payroll applies its tenant query filters.
/// </summary>
public sealed class HttpPayrollGradeLookup(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpPayrollGradeLookup> logger) : IPayrollGradeLookup
{
    public const string HttpClientName = "PayrollGradeLookup";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<PayrollGradeLookupResult> GetAsync(string gradeCode, DateOnly asOf, CancellationToken ct = default)
    {
        var path = $"api/internal/pay-grades/{Uri.EscapeDataString(gradeCode.Trim())}"
            + $"?asOf={asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(path, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new PayrollGradeLookupResult.NotFound();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Payroll grade lookup for {GradeCode} returned HTTP {Status}.", gradeCode, (int)response.StatusCode);
                return new PayrollGradeLookupResult.Unavailable($"HTTP {(int)response.StatusCode}");
            }

            var grade = await response.Content.ReadFromJsonAsync<PayGradeDto>(JsonOptions, ct);
            if (grade is null)
                return new PayrollGradeLookupResult.Unavailable("Empty Payroll response");

            return new PayrollGradeLookupResult.Found(grade.MinSalary, grade.MaxSalary, grade.BaseSalary, grade.GradeNameAr);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Transport errors, timeouts (HttpClient raises TaskCanceledException) and token failures.
            logger.LogWarning(ex, "Payroll grade lookup for {GradeCode} failed.", gradeCode);
            return new PayrollGradeLookupResult.Unavailable(ex is OperationCanceledException ? "Timeout" : ex.GetType().Name);
        }
    }

    private sealed record PayGradeDto(
        string GradeCode,
        string? GradeNameAr,
        decimal MinSalary,
        decimal MaxSalary,
        decimal BaseSalary);
}

/// <summary>HCM-027 (E2): registered when <c>Integration:PayrollBaseUrl</c> is empty.</summary>
public sealed class UnavailablePayrollGradeLookup : IPayrollGradeLookup
{
    public const string Reason = "Payroll lookup not configured";

    public Task<PayrollGradeLookupResult> GetAsync(string gradeCode, DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult<PayrollGradeLookupResult>(new PayrollGradeLookupResult.Unavailable(Reason));
}

public static class PayrollGradeLookupExtensions
{
    public static IServiceCollection AddPayrollGradeLookup(this IServiceCollection services, IConfiguration configuration)
    {
        var payrollBase = ServiceEndpoint.Optional(configuration, "Integration:PayrollBaseUrl");
        if (string.IsNullOrWhiteSpace(payrollBase))
        {
            services.AddScoped<IPayrollGradeLookup, UnavailablePayrollGradeLookup>();
            return services;
        }

        services.AddServiceAuthenticatedClient(HttpPayrollGradeLookup.HttpClientName, new Uri(payrollBase + "/"))
            .ConfigureHttpClient(client => client.Timeout = HttpPayrollGradeLookup.Timeout);
        services.AddScoped<IPayrollGradeLookup, HttpPayrollGradeLookup>();
        return services;
    }
}

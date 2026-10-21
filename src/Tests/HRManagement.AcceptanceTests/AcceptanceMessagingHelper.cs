using System.Diagnostics;
using System.Text.Json;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>PLAN-05: publish integration events to the live acceptance RabbitMQ broker.</summary>
public static class AcceptanceMessagingHelper
{
    public static async Task<Guid> PublishEmployeeCreatedAsync(
        Guid employeeId,
        Guid tenantId,
        string correlationId,
        Guid? messageId = null,
        int duplicateCount = 1)
    {
        var (bus, provider) = await ConnectBusAsync();
        try
        {
            var publish = provider.GetRequiredService<IPublishEndpoint>();
            var mid = messageId ?? Guid.NewGuid();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(employeeId.ToString("N")))).ToLowerInvariant();

            for (var i = 0; i < duplicateCount; i++)
            {
                await publish.Publish<IEmployeeCreated>(new
                {
                    EmployeeId = employeeId,
                    FullName = "AC Acceptance Employee",
                    NationalIdHash = hash,
                    EmploymentType = "Permanent",
                    TenantId = tenantId,
                    PrimaryOrgUnitId = (Guid?)null,
                    CorrelationId = correlationId
                }, ctx =>
                {
                    ctx.MessageId = mid;
                    ctx.CorrelationId = mid;
                    ctx.Headers.Set(CorrelationIdMiddleware.HeaderName, correlationId);
                    ctx.Headers.Set(TenantIdMiddleware.HeaderName, tenantId.ToString("D"));
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
            return mid;
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (provider is IDisposable disposable)
                disposable.Dispose();
        }
    }

    public static async Task<(IBusControl Bus, IServiceProvider Provider)> ConnectBusAsync()
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
        var port = ushort.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5673");
        var user = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER")
            ?? Environment.GetEnvironmentVariable("RABBITMQ_USERNAME")
            ?? "guest";
        var pass = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS")
            ?? Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")
            ?? "guest";

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = host,
            ["RabbitMQ:Port"] = port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = user,
            ["RabbitMQ:Password"] = pass,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0"
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddServiceMassTransit(config, enableInboxFilter: false);

        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        return (bus, provider);
    }

    public static async Task<(int ExitCode, string Output)> RunScriptAsync(
        string scriptName,
        params (string Key, string Value)[] args)
    {
        var root = AcceptanceHarness.RepoRoot;
        var scriptPath = Path.Combine(root, "scripts", scriptName);
        var argList = string.Join(' ', args.Select(a => $"-{a.Key} \"{a.Value}\""));
        var psi = new ProcessStartInfo("pwsh", $"-File \"{scriptPath}\" {argList}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, output + err);
    }

    public static async Task<string> ResolveAcceptanceContainerAsync(string service)
    {
        var (exitCode, output) = await RunProcessAsync(
            "docker",
            "ps",
            "--filter", "label=com.docker.compose.project=hrm-acceptance",
            "--filter", $"label=com.docker.compose.service={service}",
            "--format", "{{.Names}}");
        Assert.Equal(0, exitCode);
        var container = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(container), $"Acceptance container is not running: {service}");
        return container!;
    }

    public static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string executable,
        params string[] args)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AcceptanceHarness.RepoRoot
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask + await errorTask);
    }

    public static async Task<long> QueryAcceptancePostgresScalarAsync(string database, string sql)
    {
        var output = await QueryAcceptancePostgresTextAsync(database, sql);
        Assert.True(long.TryParse(output, out var value), $"Expected scalar integer, got: {output}");
        return value;
    }

    public static async Task<string> QueryAcceptancePostgresTextAsync(string database, string sql)
    {
        var postgres = await ResolveAcceptanceContainerAsync("postgresql");
        var (exitCode, output) = await RunProcessAsync(
            "docker", "exec", postgres,
            "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hrm_ci", "-d", database,
            "-t", "-A", "-c", sql);
        Assert.Equal(0, exitCode);
        return output.Trim();
    }

    public static JsonDocument? TryParseJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text[start..]);
            var reader = new Utf8JsonReader(bytes);
            if (!reader.Read()) return null;
            return JsonDocument.ParseValue(ref reader);
        }
        catch
        {
            return null;
        }
    }
}

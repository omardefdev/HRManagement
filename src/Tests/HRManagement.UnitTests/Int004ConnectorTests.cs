using System.Diagnostics;
using HRManagement.ServiceDefaults.Connectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

public class Int004ConnectorTests
{
    private static string ResolveRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join("/", parts));
    }

    private static string RepoRoot
    {
        get
        {
            var marker = ResolveRepoFile("docs", "INT_004_CONNECTORS.md");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(marker)!, ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:EnforceOnStartup"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:ConnectorId"] = "treasury",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Mode"] = "File",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:RequiredInProduction"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:ConnectorId"] = "civil-registry",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Mode"] = "File",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:RequiredInProduction"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:ConnectorId"] = "federation",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:Mode"] = "File",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:RequiredInProduction"] = "false"
        };
        foreach (var kv in overrides)
            values[kv.Key] = kv.Value;

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = environmentName,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddGovernmentConnectors(config);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("File")]
    [InlineData("Stub")]
    [InlineData("Sandbox")]
    [InlineData("Live")]
    public void Mode_switching_resolves_connectors(string mode)
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:EnforceOnStartup"] = "false",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Mode"] = mode,
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Mode"] = mode,
            [$"{GovernmentConnectorOptions.SectionName}:Federation:Mode"] = mode
        });

        var registry = sp.GetRequiredService<LiveConnectorRegistry>();
        Assert.Equal(3, registry.All.Count);

        var treasury = registry.Get("treasury");
        Assert.NotNull(treasury);
        Assert.Equal(Enum.Parse<IntegrationConnectorMode>(mode), treasury!.Mode);

        var civil = registry.Get("civil-registry");
        Assert.NotNull(civil);
        Assert.Equal(Enum.Parse<IntegrationConnectorMode>(mode), civil!.Mode);

        var federation = registry.Get("federation");
        Assert.NotNull(federation);
        Assert.Equal(Enum.Parse<IntegrationConnectorMode>(mode), federation!.Mode);
    }

    [Fact]
    public void Production_guard_rejects_stub_for_required_connector()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Mode"] = "Stub"
        });

        var validator = sp.GetServices<IHostedService>()
            .OfType<GovernmentConnectorStartupValidator>()
            .Single();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());

        Assert.Contains("Stub", ex.Message);
        Assert.Contains("treasury", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_guard_rejects_sandbox_for_required_connector()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Mode"] = "Sandbox"
        });

        var validator = sp.GetServices<IHostedService>()
            .OfType<GovernmentConnectorStartupValidator>()
            .Single();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());

        Assert.Contains("Sandbox", ex.Message);
        Assert.Contains("civil-registry", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_guard_allows_live_for_required_connector()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Mode"] = "Live",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Mode"] = "Live",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:Mode"] = "Live"
        });

        var validator = sp.GetServices<IHostedService>()
            .OfType<GovernmentConnectorStartupValidator>()
            .Single();

        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Production_guard_allows_file_for_non_required_connector()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Mode"] = "Live",
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Mode"] = "Live",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Sandbox"] = "true",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:Mode"] = "File",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:RequiredInProduction"] = "false"
        });

        var validator = sp.GetServices<IHostedService>()
            .OfType<GovernmentConnectorStartupValidator>()
            .Single();

        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Development_allows_stub_mode()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:Treasury:Mode"] = "Stub",
            [$"{GovernmentConnectorOptions.SectionName}:CivilRegistry:Mode"] = "Stub",
            [$"{GovernmentConnectorOptions.SectionName}:Federation:Mode"] = "Stub"
        });

        var validator = sp.GetServices<IHostedService>()
            .OfType<GovernmentConnectorStartupValidator>()
            .Single();

        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Registry_returns_null_for_unknown_connector()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:EnforceOnStartup"] = "false"
        });

        var registry = sp.GetRequiredService<LiveConnectorRegistry>();
        Assert.Null(registry.Get("nonexistent"));
    }

    [Fact]
    public void Connector_descriptors_match_ext_cards()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            [$"{GovernmentConnectorOptions.SectionName}:EnforceOnStartup"] = "false"
        });

        var registry = sp.GetRequiredService<LiveConnectorRegistry>();

        var treasury = registry.Get("treasury")!;
        Assert.Equal("EXT-003", treasury.ExtCard);
        Assert.True(treasury.RequiredInProduction);

        var civil = registry.Get("civil-registry")!;
        Assert.Equal("EXT-002", civil.ExtCard);
        Assert.True(civil.RequiredInProduction);

        var federation = registry.Get("federation")!;
        Assert.Equal("EXT-001", federation.ExtCard);
        Assert.False(federation.RequiredInProduction);
    }

    [Fact]
    public void Enum_has_all_four_modes()
    {
        var names = Enum.GetNames<IntegrationConnectorMode>();
        Assert.Contains("File", names);
        Assert.Contains("Stub", names);
        Assert.Contains("Sandbox", names);
        Assert.Contains("Live", names);
        Assert.Equal(4, names.Length);
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = ResolveRepoFile("scripts", "int004-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"int004-validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Documentation_exists()
    {
        Assert.True(System.IO.File.Exists(ResolveRepoFile("docs", "INT_004_CONNECTORS.md")));
    }
}

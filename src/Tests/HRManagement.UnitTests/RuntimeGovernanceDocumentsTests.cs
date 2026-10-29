using System.Text.Json;
using HRManagement.ServiceDefaults.LegalControl;
using HRManagement.ServiceDefaults.ProgramGovernance;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

/// <summary>
/// QA-034 canary (AC-QA-34c): the STR-005 and STR-002 startup validators, fed the documents exactly as a service image
/// ships them (build output <c>ProgramGovernance/*</c>, <c>LegalControl/*</c>) and each service's own
/// <c>appsettings.{Production,Staging}.json</c>, must start <b>today</b>. This is the only unit test that reads the real
/// clock on purpose: it turns red on the same day Production would refuse to start. The dated-obligations CI step
/// warns 30 days and fails 7 days before that day.
/// </summary>
public class RuntimeGovernanceDocumentsTests
{
    public static TheoryData<string, string> ServiceEnvironments => new()
    {
        { "src/Gateway/HRManagement.Gateway", Environments.Production },
        { "src/Gateway/HRManagement.Gateway", Environments.Staging },
        { "src/Services/HRCore/HR.Core.Web", Environments.Production },
        { "src/Services/HRCore/HR.Core.Web", Environments.Staging },
        { "src/Services/Payroll/Payroll.Web", Environments.Production },
        { "src/Services/Payroll/Payroll.Web", Environments.Staging }
    };

    private static string OutputRoot => AppContext.BaseDirectory;

    private static IConfiguration ServiceConfiguration(string serviceDir, string environment, IDictionary<string, string?>? overrides = null)
    {
        var settings = Path.Combine(RepoPaths.Root, serviceDir.Replace('/', Path.DirectorySeparatorChar), $"appsettings.{environment}.json");
        Assert.True(System.IO.File.Exists(settings), $"missing {settings}");
        var builder = new ConfigurationBuilder().AddJsonFile(settings, optional: false);
        if (overrides is not null)
            builder.AddInMemoryCollection(overrides);
        return builder.Build();
    }

    private static ServiceProvider BuildProvider(IConfiguration config, string environment, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = environment,
            // A published service resolves "ProgramGovernance/..." against its content root = the build output.
            ContentRootPath = OutputRoot,
            ApplicationName = "canary",
            ContentRootFileProvider = new PhysicalFileProvider(OutputRoot)
        });
        services.AddProgramGovernance(config);
        services.AddLegalControl(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void StartValidators(ServiceProvider sp)
    {
        var hosted = sp.GetServices<IHostedService>().ToList();
        Assert.Contains(hosted, h => h is ProgramGovernanceStartupValidator);
        Assert.Contains(hosted, h => h is LegalControlStartupValidator);
        foreach (var h in hosted.Where(h => h is ProgramGovernanceStartupValidator or LegalControlStartupValidator))
            h.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Output_ships_the_governance_documents()
    {
        foreach (var rel in new[]
                 {
                     "ProgramGovernance/governance-matrix.json", "ProgramGovernance/decision-log.json",
                     "ProgramGovernance/risk-register.json", "ProgramGovernance/go-live-evidence.json",
                     "LegalControl/control-catalog.json", "LegalControl/legislative-watch-stamp.json"
                 })
        {
            Assert.True(System.IO.File.Exists(Path.Combine(OutputRoot, rel)), $"build output lacks {rel}");
        }
    }

    [Theory]
    [MemberData(nameof(ServiceEnvironments))]
    public void Governance_validators_start_today_with_the_service_configuration(string serviceDir, string environment)
    {
        var config = ServiceConfiguration(serviceDir, environment);
        Assert.True(config.GetValue<bool>("ProgramGovernance:EnforceOnStartup"),
            $"{serviceDir} {environment}: ProgramGovernance:EnforceOnStartup must stay true");

        using var sp = BuildProvider(config, environment, TimeProvider.System);
        StartValidators(sp);
    }

    // AC-QA-34c negative: the matrix as it was before PLAN-34 (both simulation exceptions Declared) refuses Production
    // startup the day after EX-STR005-SIM-INCIDENT-WINDOW expired, and the message names the expiry.
    [Theory]
    [InlineData("2026-09-15", false)]
    [InlineData("2026-09-16", true)]
    public void Pre_closure_matrix_refuses_startup_after_expiry(string asOf, bool expectThrow)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "qa034-canary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<GovernanceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(OutputRoot, "ProgramGovernance", "governance-matrix.json")))!;
            foreach (var ex in matrix.Exceptions.Where(e => e.ExceptionId.StartsWith("EX-STR005-SIM-", StringComparison.Ordinal)))
                ex.Status = "Declared";
            Assert.Equal(2, matrix.Exceptions.Count(e => e.Status == "Declared"));
            var matrixPath = Path.Combine(tmp, "governance-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            var config = ServiceConfiguration("src/Services/HRCore/HR.Core.Web", Environments.Production,
                new Dictionary<string, string?> { ["ProgramGovernance:MatrixPath"] = matrixPath });
            using var sp = BuildProvider(config, Environments.Production, FixedTimeProvider.AtUtcDate(asOf));
            var validator = sp.GetServices<IHostedService>().OfType<ProgramGovernanceStartupValidator>().Single();

            if (!expectThrow)
            {
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            var thrown = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("EX-STR005-SIM-INCIDENT-WINDOW", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("expired", thrown.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    // The next calendar bomb: STR-002 legislative watch nextDue. Production refuses to start the day after it.
    [Fact]
    public void Legislative_watch_refuses_production_startup_after_next_due()
    {
        using var stamp = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(OutputRoot, "LegalControl", "legislative-watch-stamp.json")));
        var nextDue = DateTime.ParseExact(stamp.RootElement.GetProperty("nextDue").GetString()!, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);

        var config = ServiceConfiguration("src/Services/Payroll/Payroll.Web", Environments.Production);

        using (var onDue = BuildProvider(config, Environments.Production, FixedTimeProvider.AtUtcDate(nextDue.Year, nextDue.Month, nextDue.Day)))
            onDue.GetServices<IHostedService>().OfType<LegalControlStartupValidator>().Single()
                .StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        var after = nextDue.AddDays(1);
        using var late = BuildProvider(config, Environments.Production, FixedTimeProvider.AtUtcDate(after.Year, after.Month, after.Day));
        var validator = late.GetServices<IHostedService>().OfType<LegalControlStartupValidator>().Single();
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("legislative watch overdue", thrown.Message, StringComparison.Ordinal);
    }
}

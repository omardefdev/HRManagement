using System.Diagnostics;
using System.Text.Json;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Rel004CompatibilityTests
{
    private static readonly string[] RequiredConsumerFields =
    [
        "id", "consumerType", "minSupportedApiVersion", "maxTestedApiVersion", "owner", "certificationStatus"
    ];

    private static readonly string[] RequiredContractFields =
    [
        "id", "kind", "schemaVersion", "namespace"
    ];


    [Fact]
    public void Api_client_inventory_has_gateway_inter_and_external_consumers()
    {
        var path = RepoPaths.Resolve("docs", "rel-compat", "api-client-inventory.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-004", root.GetProperty("card").GetString());

        Assert.True(root.GetProperty("semverPolicy").GetProperty("supportNMinus1").GetBoolean());
        Assert.True(root.GetProperty("semverPolicy").GetProperty("deprecationReleaseTrains").GetInt32() >= 2);

        var types = root.GetProperty("consumers").EnumerateArray()
            .Select(c => c.GetProperty("consumerType").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("gateway", types);
        Assert.Contains("inter-service", types);
        Assert.Contains("external", types);

        var consumers = root.GetProperty("consumers").EnumerateArray().ToList();
        Assert.True(consumers.Count >= 10);

        foreach (var c in consumers)
        {
            foreach (var field in RequiredConsumerFields)
            {
                Assert.True(c.TryGetProperty(field, out var prop),
                    $"consumer {c.GetProperty("id").GetString()} missing {field}");
                Assert.False(string.IsNullOrWhiteSpace(prop.GetString()),
                    $"consumer {c.GetProperty("id").GetString()} empty {field}");
            }
        }

        Assert.Contains(consumers, c => c.GetProperty("id").GetString() == "ext-treasury-disbursement");
        Assert.Contains(consumers, c => c.GetProperty("id").GetString() == "gateway-payroll");
    }

    [Fact]
    public void Event_schema_inventory_covers_mass_transit_contracts()
    {
        var path = RepoPaths.Resolve("docs", "rel-compat", "event-schema-inventory.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-004", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("additiveOnlyWithinMinor").GetBoolean());

        var ids = root.GetProperty("contracts").EnumerateArray()
            .Select(c => c.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var must in new[] { "IEmployeeCreated", "IContractActivated", "IPayrollProcessed", "ILeaveApproved", "IExportJobRequested" })
            Assert.Contains(must, ids);

        foreach (var c in root.GetProperty("contracts").EnumerateArray())
        {
            foreach (var field in RequiredContractFields)
            {
                Assert.True(c.TryGetProperty(field, out var prop),
                    $"contract {c.GetProperty("id").GetString()} missing {field}");
                if (field == "schemaVersion")
                    Assert.True(prop.GetInt32() >= 1);
                else
                    Assert.False(string.IsNullOrWhiteSpace(prop.GetString()));
            }
        }

        Assert.True(root.GetProperty("contracts").GetArrayLength() >= 15);
    }

    [Fact]
    public void Deprecation_register_enforces_two_release_trains()
    {
        var path = RepoPaths.Resolve("docs", "rel-compat", "deprecation-register.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("REL-004", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("policy").GetProperty("deprecationReleaseTrains").GetInt32() >= 2);
        Assert.True(root.GetProperty("policy").GetProperty("noticeDaysMin").GetInt32() >= 90);
        Assert.True(root.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public void Policy_docs_scripts_and_baseline_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_004_COMPATIBILITY_POLICY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_004_RELEASE_NOTES_TEMPLATE.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "REL_004_ROLLBACK_POLICY.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "rel-compat", "compatibility-matrix-template.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "rel-compat", "openapi-baseline", "README.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "rel-compat", "openapi-baseline", "hr-core-v1.openapi.json")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel004-openapi-diff.ps1")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "rel004-compat-validate.ps1")));
    }

    [Fact]
    public void Openapi_baseline_is_valid_json_with_paths()
    {
        var path = RepoPaths.Resolve("docs", "rel-compat", "openapi-baseline", "hr-core-v1.openapi.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("3.0.3", root.GetProperty("openapi").GetString());
        Assert.True(root.GetProperty("paths").EnumerateObject().Any());
    }

    [Fact]
    public void Compat_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "rel004-compat-validate.ps1");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{repoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"rel004-compat-validate failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }

    [Fact]
    public void Openapi_diff_self_compare_passes()
    {
        var script = RepoPaths.Resolve("scripts", "rel004-openapi-diff.ps1");
        var baseline = RepoPaths.Resolve("docs", "rel-compat", "openapi-baseline", "hr-core-v1.openapi.json");
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Baseline \"{baseline}\" -Candidate \"{baseline}\" -RepoRoot \"{repoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"rel004-openapi-diff self compare failed: {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout);
    }
}

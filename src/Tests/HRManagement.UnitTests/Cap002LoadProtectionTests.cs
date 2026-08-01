using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.Capacity;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Cap002LoadProtectionTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "cap002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void Default_quota_admits_requests()
    {
        var control = new TenantAdmissionControl();
        var tenantId = Guid.NewGuid();

        var decision = control.TryAdmit(tenantId);

        Assert.Equal(AdmissionDecision.Admit, decision);
        Assert.Equal(1, control.GetCurrentConcurrency(tenantId));
    }

    [Fact]
    public void Concurrency_limit_rejects_excess()
    {
        var control = new TenantAdmissionControl(new TenantQuota
        {
            MaxConcurrentRequests = 2,
            MaxRequestsPerMinute = 10000
        });
        var tenantId = Guid.NewGuid();

        Assert.Equal(AdmissionDecision.Admit, control.TryAdmit(tenantId));
        Assert.Equal(AdmissionDecision.Admit, control.TryAdmit(tenantId));
        Assert.Equal(AdmissionDecision.Reject, control.TryAdmit(tenantId));
    }

    [Fact]
    public void Release_allows_new_requests()
    {
        var control = new TenantAdmissionControl(new TenantQuota
        {
            MaxConcurrentRequests = 1,
            MaxRequestsPerMinute = 10000
        });
        var tenantId = Guid.NewGuid();

        control.TryAdmit(tenantId);
        Assert.Equal(AdmissionDecision.Reject, control.TryAdmit(tenantId));

        control.Release(tenantId);
        Assert.Equal(AdmissionDecision.Admit, control.TryAdmit(tenantId));
    }

    [Fact]
    public void Per_tenant_quota_override()
    {
        var control = new TenantAdmissionControl();
        var standardTenant = Guid.NewGuid();
        var priorityTenant = Guid.NewGuid();

        control.SetQuota(priorityTenant, new TenantQuota
        {
            MaxConcurrentRequests = 250,
            MaxRequestsPerMinute = 1500
        });

        Assert.Equal(100, control.GetQuota(standardTenant).MaxConcurrentRequests);
        Assert.Equal(250, control.GetQuota(priorityTenant).MaxConcurrentRequests);
    }

    [Fact]
    public void Noisy_neighbor_does_not_starve_others()
    {
        var control = new TenantAdmissionControl(new TenantQuota
        {
            MaxConcurrentRequests = 2,
            MaxRequestsPerMinute = 10000
        });
        var noisyTenant = Guid.NewGuid();
        var quietTenant = Guid.NewGuid();

        control.TryAdmit(noisyTenant);
        control.TryAdmit(noisyTenant);
        Assert.Equal(AdmissionDecision.Reject, control.TryAdmit(noisyTenant));

        Assert.Equal(AdmissionDecision.Admit, control.TryAdmit(quietTenant));
    }

    [Fact]
    public void Rate_limit_rejects_burst()
    {
        var control = new TenantAdmissionControl(new TenantQuota
        {
            MaxConcurrentRequests = 10000,
            MaxRequestsPerMinute = 5
        });
        var tenantId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(AdmissionDecision.Admit, control.TryAdmit(tenantId));
            control.Release(tenantId);
        }

        Assert.Equal(AdmissionDecision.Reject, control.TryAdmit(tenantId));
    }

    [Fact]
    public void Quota_policy_json_is_valid()
    {
        var path = RepoPaths.Resolve("docs", "cap", "tenant-quota-policy.json");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.Equal("CAP-002", root.GetProperty("card").GetString());
        Assert.True(root.GetProperty("defaultQuota").GetProperty("maxConcurrentRequests").GetInt32() > 0);

        var tiers = root.GetProperty("tiers").EnumerateArray().ToList();
        Assert.True(tiers.Count >= 2);

        var payrollTier = tiers.First(t => t.GetProperty("tier").GetString() == "payroll-critical");
        Assert.True(payrollTier.GetProperty("maxConcurrentRequests").GetInt32() >
                    root.GetProperty("defaultQuota").GetProperty("maxConcurrentRequests").GetInt32());
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "cap002-validate.ps1");
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
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "CAP_002_LOAD_PROTECTION.md")));
    }
}

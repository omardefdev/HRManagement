using System.Diagnostics;
using System.Text.Json;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-30 K3/K4: the ROPA lists the candidate OTP, documents and notifications activities under the recruitment DPIA, and
/// the extended public careers / candidate portal a11y scan passes (exit 0 = no finding).
/// </summary>
public class Hcm030PrivacyA11yScanTests
{
    [Theory]
    [InlineData("pa-hcm-candidate-otp")]
    [InlineData("pa-hcm-candidate-documents")]
    [InlineData("pa-hcm-candidate-notifications")]
    public void Ropa_lists_the_candidate_activity_under_the_recruitment_dpia(string id)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "prv-ropa", "processing-activities.json")));
        var activity = Assert.Single(doc.RootElement.GetProperty("activities").EnumerateArray(),
            a => a.GetProperty("id").GetString() == id);

        Assert.Equal("HR.Core", activity.GetProperty("service").GetString());
        Assert.False(string.IsNullOrWhiteSpace(activity.GetProperty("purpose").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(activity.GetProperty("legalBasis").GetString()));
        Assert.Equal("ret-recruitment-application", activity.GetProperty("retentionPolicy").GetString());
        Assert.NotEmpty(activity.GetProperty("dataCategories").EnumerateArray());
        Assert.NotEmpty(activity.GetProperty("recipients").EnumerateArray());
        Assert.True(activity.GetProperty("highRisk").GetBoolean());
        var dpia = activity.GetProperty("dpiaRef").GetString()!;
        Assert.Equal("PRV_001_DPIA_RECRUITMENT.md", dpia);
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", dpia)));
    }

    [Fact]
    public void Ropa_names_the_pending_smtp_sms_processor_for_messages_and_codes()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "prv-ropa", "processing-activities.json")));
        foreach (var id in new[] { "pa-hcm-candidate-otp", "pa-hcm-candidate-notifications" })
        {
            var activity = doc.RootElement.GetProperty("activities").EnumerateArray()
                .Single(a => a.GetProperty("id").GetString() == id);
            Assert.Contains(activity.GetProperty("recipients").EnumerateArray(),
                r => r.GetString()!.Contains("SMTP/SMS", StringComparison.Ordinal)
                    && r.GetString()!.Contains("يُحدَّد عند D-8", StringComparison.Ordinal));
        }
        Assert.Contains("يُحدَّد عند D-8", System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "PRV_001_SUBPROCESSOR_REGISTER.md")));
    }

    [Fact]
    public void A11y_scan_passes()
    {
        var script = RepoPaths.Resolve("scripts", "hcm006-a11y-scan.ps1");
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{root}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000);
        Assert.True(proc.ExitCode == 0, $"a11y scan failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.Ordinal);
    }
}

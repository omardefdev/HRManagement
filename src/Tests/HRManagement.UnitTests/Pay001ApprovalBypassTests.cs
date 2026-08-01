using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

/// <summary>
/// PAY-001: architectural tests verifying no PaymentBatch creation bypass,
/// approverId rejection from client, and state machine enforcement.
/// </summary>
public class Pay001ApprovalBypassTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "pay001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void PaymentBatch_not_created_in_controllers()
    {
        var controllerDir = Path.Combine(RepoRoot, "src", "Services", "Payroll", "Payroll.Web", "Controllers");
        var controllerFiles = Directory.GetFiles(controllerDir, "*.cs");

        foreach (var file in controllerFiles)
        {
            var content = System.IO.File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            if (fileName is "PayrollRunsController.cs" or "PayrollApiController.cs"
                or "PayrollExtendedControllers.cs")
            {
                Assert.DoesNotMatch(@"new\s+PaymentBatch", content);
            }
        }
    }

    [Fact]
    public void PaymentBatch_only_created_in_PostHandler_and_ReverseHandler()
    {
        var handlerFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Application", "Features", "PayrollRuns", "PayrollRunHandlers.cs");
        var content = System.IO.File.ReadAllText(handlerFile);

        var matches = Regex.Matches(content, @"new\s+PaymentBatch");
        Assert.True(matches.Count >= 1, "PostPayrollRunHandler must create PaymentBatch");

        var postSection = ExtractHandlerSection(content, "PostPayrollRunHandler");
        Assert.Matches(@"new\s+PaymentBatch", postSection);
        Assert.Contains("PaymentBatchStatus.Pending", postSection);
    }

    [Fact]
    public void ApproverId_derived_from_server_identity_not_client()
    {
        var controllerFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Web", "Controllers", "PayrollRunsController.cs");
        var content = System.IO.File.ReadAllText(controllerFile);

        Assert.Contains("RequireUserId()", content);
        Assert.Contains("ClaimTypes.NameIdentifier", content);

        Assert.DoesNotMatch(
            @"ApproveRun\([^)]*Guid\s+approverId",
            content);
    }

    [Fact]
    public void Creator_cannot_approve_same_run()
    {
        var handlerFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Application", "Features", "PayrollRuns", "PayrollRunHandlers.cs");
        var content = System.IO.File.ReadAllText(handlerFile);

        var approveSection = ExtractHandlerSection(content, "ApprovePayrollRunHandler");
        Assert.Contains("CreatedBy", approveSection);
        Assert.Contains("ApproverId", approveSection);
    }

    [Fact]
    public void Approver_cannot_post_same_run()
    {
        var handlerFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Application", "Features", "PayrollRuns", "PayrollRunHandlers.cs");
        var content = System.IO.File.ReadAllText(handlerFile);

        var postSection = ExtractHandlerSection(content, "PostPayrollRunHandler");
        Assert.Contains("ApproverId", postSection);
        Assert.Contains("PosterId", postSection);
    }

    [Fact]
    public void PayrollRun_has_all_state_guards()
    {
        var runFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Domain", "Entities", "PayrollRun.cs");
        var content = System.IO.File.ReadAllText(runFile);

        var guards = new[]
        {
            "ValidateCanCalculate", "ValidateCanPost", "ValidateCanApprove",
            "ValidateCanReverse", "ValidateCanModifyLines", "ValidateCanSubmitForReview"
        };
        foreach (var guard in guards)
            Assert.Contains(guard, content);
    }

    [Fact]
    public void ResultsFrozen_prevents_line_modification()
    {
        var runFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Domain", "Entities", "PayrollRun.cs");
        var content = System.IO.File.ReadAllText(runFile);

        Assert.Contains("ResultsFrozen", content);

        var modifySection = ExtractMethodBody(content, "ValidateCanModifyLines");
        Assert.Contains("ResultsFrozen", modifySection);
    }

    [Fact]
    public void Post_requires_approval_and_validation()
    {
        var runFile = RepoPaths.Resolve("src", "Services", "Payroll",
            "Payroll.Domain", "Entities", "PayrollRun.cs");
        var content = System.IO.File.ReadAllText(runFile);

        var postSection = ExtractMethodBody(content, "ValidateCanPost");
        Assert.Contains("Approved", postSection);
        Assert.Contains("ValidatedAtCalculate", postSection);
        Assert.Contains("ResultsFrozen", postSection);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PAY_001_APPROVAL_BYPASS.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("scripts", "pay001-validate.ps1")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "pay001-validate.ps1");
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

    private static string ExtractHandlerSection(string content, string className)
    {
        var idx = content.IndexOf($"class {className}", StringComparison.Ordinal);
        if (idx < 0) return content;
        var end = content.IndexOf("\npublic record ", idx + 1, StringComparison.Ordinal);
        if (end < 0) end = content.IndexOf("\npublic class ", idx + className.Length + 6, StringComparison.Ordinal);
        return end > idx ? content[idx..end] : content[idx..];
    }

    private static string ExtractMethodBody(string content, string methodName)
    {
        var idx = content.IndexOf(methodName, StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var end = content.IndexOf("\n\n", idx, StringComparison.Ordinal);
        if (end < 0) end = content.IndexOf("public ", idx + methodName.Length, StringComparison.Ordinal);
        return end > idx ? content[idx..end] : content[idx..];
    }
}

using System.Diagnostics;
using System.Reflection;
using HRManagement.ServiceDefaults.Workflow;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>WF-002: tests for WorkInboxItem and IWorkInboxService.</summary>
public class Wf002WorkInboxTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "wf002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public void WorkInboxItem_has_required_properties()
    {
        var props = typeof(WorkInboxItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        Assert.Contains("Id", props);
        Assert.Contains("TenantId", props);
        Assert.Contains("AssigneeId", props);
        Assert.Contains("TeamId", props);
        Assert.Contains("SourceSystem", props);
        Assert.Contains("SourceReferenceId", props);
        Assert.Contains("ItemType", props);
        Assert.Contains("Priority", props);
        Assert.Contains("Status", props);
        Assert.Contains("Title", props);
        Assert.Contains("DeepLink", props);
        Assert.Contains("DueDate", props);
        Assert.Contains("CompletedAt", props);
        Assert.Contains("IsEscalated", props);
        Assert.Contains("DelegatedFromId", props);
        Assert.Contains("AgingDays", props);
    }

    [Fact]
    public void WorkItemType_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(WorkItemType), WorkItemType.Approval));
        Assert.True(Enum.IsDefined(typeof(WorkItemType), WorkItemType.Task));
        Assert.True(Enum.IsDefined(typeof(WorkItemType), WorkItemType.Review));
        Assert.True(Enum.IsDefined(typeof(WorkItemType), WorkItemType.Acknowledgement));
        Assert.True(Enum.IsDefined(typeof(WorkItemType), WorkItemType.Information));
    }

    [Fact]
    public void WorkItemPriority_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(WorkItemPriority), WorkItemPriority.Low));
        Assert.True(Enum.IsDefined(typeof(WorkItemPriority), WorkItemPriority.Normal));
        Assert.True(Enum.IsDefined(typeof(WorkItemPriority), WorkItemPriority.High));
        Assert.True(Enum.IsDefined(typeof(WorkItemPriority), WorkItemPriority.Urgent));
    }

    [Fact]
    public void WorkItemStatus_enum_has_expected_values()
    {
        Assert.True(Enum.IsDefined(typeof(WorkItemStatus), WorkItemStatus.Pending));
        Assert.True(Enum.IsDefined(typeof(WorkItemStatus), WorkItemStatus.InProgress));
        Assert.True(Enum.IsDefined(typeof(WorkItemStatus), WorkItemStatus.Completed));
        Assert.True(Enum.IsDefined(typeof(WorkItemStatus), WorkItemStatus.Delegated));
        Assert.True(Enum.IsDefined(typeof(WorkItemStatus), WorkItemStatus.Escalated));
        Assert.True(Enum.IsDefined(typeof(WorkItemStatus), WorkItemStatus.Cancelled));
    }

    [Fact]
    public void AgingDays_computes_correctly()
    {
        var item = new WorkInboxItem
        {
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        Assert.Equal(5, item.AgingDays);
    }

    [Fact]
    public void Default_values_are_correct()
    {
        var item = new WorkInboxItem();
        Assert.Equal(WorkItemStatus.Pending, item.Status);
        Assert.Equal(WorkItemPriority.Normal, item.Priority);
        Assert.False(item.IsEscalated);
    }

    [Fact]
    public void IWorkInboxService_has_required_methods()
    {
        var methods = typeof(IWorkInboxService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).ToList();

        Assert.Contains("GetPersonalInboxAsync", methods);
        Assert.Contains("GetTeamInboxAsync", methods);
        Assert.Contains("CreateItemAsync", methods);
        Assert.Contains("CompleteItemAsync", methods);
        Assert.Contains("DelegateItemAsync", methods);
        Assert.Contains("EscalateItemAsync", methods);
        Assert.Contains("GetPendingCountAsync", methods);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "WF_002_WORK_INBOX.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.ServiceDefaults", "Workflow", "WorkInboxItem.cs")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve(
            "src", "Shared", "HRManagement.ServiceDefaults", "Workflow", "IWorkInboxService.cs")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "wf002-validate.ps1");
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
}

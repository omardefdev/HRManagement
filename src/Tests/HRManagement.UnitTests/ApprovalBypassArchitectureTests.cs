using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>
/// Workflow state must never be bound from a request. A form field for an approval status or an
/// approver identity lets a requester submit work that is already approved, skipping the approver
/// and every side effect the approval owns (balance debit, events, separation of duties).
/// <para>
/// This scans Razor views rather than asserting a document, so it fails the moment such a field is
/// reintroduced anywhere in the estate.
/// </para>
/// </summary>
public class ApprovalBypassArchitectureTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
        }
    }

    /// <summary>
    /// Identity fields the server alone may set. Accepting these from a request lets a caller
    /// attribute an approval to somebody else, or steer a write into another tenant.
    /// </summary>
    private static readonly Regex ServerAssignedIdentityBinding = new(
        @"asp-for\s*=\s*""(ApprovedBy|RequestedBy|IssuedBy|TenantId)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Model-bound selects over an approval lifecycle enum.
    /// <para>
    /// Two deliberate exclusions. Operational states an operator legitimately records (device
    /// retired, server stopped, backup failed, archive loan returned) are not listed, because there
    /// the status *is* the data being entered with no approval behind it. And a select posting to a
    /// dedicated transition action via <c>name=</c> is the correct pattern — the offence is binding
    /// the status onto the entity through a create or edit form.
    /// </para>
    /// </summary>
    private static readonly Regex WorkflowStatusBinding = new(
        @"asp-for\s*=\s*""Status""[^>]*GetEnumSelectList<\s*(?:[\w.]*\.)?(ApprovalStatus|ContractStatus|PayrollRunStatus|PaymentBatchStatus|ComplaintStatus|CaseStatus)\s*>",
        RegexOptions.Compiled);

    [Fact]
    public void No_view_binds_an_approval_workflow_status()
    {
        var violations = Scan(WorkflowStatusBinding);

        Assert.True(
            violations.Count == 0,
            "Approval status is server-assigned and changes only through its guarded command. "
            + "Offending bindings:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void No_view_binds_an_approver_or_tenant_identity()
    {
        var violations = Scan(ServerAssignedIdentityBinding);

        Assert.True(
            violations.Count == 0,
            "Approver and tenant identity come from the authenticated session, never from the form. "
            + "Offending bindings:\n" + string.Join("\n", violations));
    }

    private static List<string> Scan(Regex pattern)
    {
        var views = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cshtml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(views);

        var violations = new List<string>();
        foreach (var view in views)
        {
            var text = global::System.IO.File.ReadAllText(view);
            foreach (Match match in pattern.Matches(text))
            {
                var line = LineContaining(text, match.Index);

                // Showing the current state read-only is fine; an editable control is not.
                if (line.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("readonly", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                violations.Add($"{Path.GetRelativePath(RepoRoot, view)}: {line.Trim()}");
            }
        }

        return violations;
    }

    private static string LineContaining(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Min(index, text.Length - 1)) + 1;
        var end = text.IndexOf('\n', index);
        if (end < 0) end = text.Length;
        return text[start..end];
    }
}

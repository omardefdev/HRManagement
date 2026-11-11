namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-51 (§4, §6): a sensitive write carries an explicit policy that is neither its module's policy nor its module's write policy. A
/// write is sensitive when its action's whole name is one of <see cref="ExactNames"/>, starts with one of <see cref="Prefixes"/> (never a
/// substring elsewhere in the name), or is one of the plan's named actions (<see cref="NamedActions"/>). What does not is a row of
/// <c>tests/architecture/sensitive-actions-allowlist.txt</c> (<c>service:Controller.Action | reason</c>), which only shrinks.
/// </summary>
public sealed class SensitiveActionsHaveApprovalPolicyTests
{
    public const string ListPath = "tests/architecture/sensitive-actions-allowlist.txt";

    /// <summary>The most rows the list may hold. Lower it when the list shrinks; never raise it.</summary>
    public const int Ceiling = 39;

    public static readonly IReadOnlySet<string> ExactNames = new HashSet<string>(
        ["Approve", "Reject", "Revoke", "UrgentRevoke", "Post", "Reverse", "Finalize", "Rebuild", "Run"], StringComparer.Ordinal);

    public static readonly IReadOnlyList<string> Prefixes = ["Release", "Decide", "Publish", "Grant"];

    /// <summary>The plan's list (§4, «الإجراءات الحساسة في الخدمات الأخرى»), by service; <c>Controller.*</c> names every write of it.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> NamedActions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["idcard"] = ["IssuedCards.Revoke", "IssuedCards.UrgentRevoke", "AccessPermissions.*", "OfficialSealRecords.*"],
        ["records-archive"] = ["ArchiveItems.ReleaseLegalHold", "ArchiveItems.RecordDisposal", "ArchiveItems.UpdateRetention", "ArchiveItems.ApproveDisposal"],
        ["it-infra"] = ["SystemPermissionRecords.Create", "SystemPermissionRecords.Revoke", "BackupJobs.Run", "BackupJobs.Delete"],
        ["auth-directory"] = ["Roles.Edit", "Roles.Delete", "AuthUsers.Edit"],
        ["hr-training"] = ["TrainingPlans.Approve", "TrainingNominations.Approve", "TrainingNominations.Reject", "MissionFiles.Approve"],
        ["hr-movements"] = ["DistributionPlans.Approve"],
        ["compliance"] = ["DisciplinaryActions.DecideAppeal", "Complaints.Resolve"],
        ["dev-analytics"] = ["ReadModels.Rebuild", "EtlJobs.Run"],
        ["admin-logistics"] = ["Assets.MarkLost", "Assets.CancelAsset"],
    };

    /// <summary>The policies that are not an approval: the module's own and its write policy, and the gateway's coarse ones.</summary>
    public static IReadOnlySet<string> NonApprovalPolicies(string service)
    {
        var entry = WebAssemblyCatalog.Entry(service);
        var set = new HashSet<string>(StringComparer.Ordinal) { "Gateway.Dashboard", "Gateway.Authenticated" };
        if (entry.ModulePolicy is { } module)
        {
            set.Add(module);
            set.Add(module["Module.".Length..] + ".Write");
        }

        if (service == "hr-core")
        {
            set.Add("HrCore.ReadOnly");
            set.Add("HrCore.Lookup");
        }

        return set;
    }

    public static bool IsSensitive(ActionInfo action)
    {
        if (!action.IsWrite)
            return false;
        var name = action.Method.Name;
        if (ExactNames.Contains(name) || Prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            return true;
        return NamedActions.TryGetValue(action.Service, out var named)
               && named.Any(n => n.EndsWith(".*", StringComparison.Ordinal)
                   ? string.Equals(n[..^2], action.ControllerName, StringComparison.Ordinal)
                   : string.Equals(n, action.Key, StringComparison.Ordinal));
    }

    public static bool HasApprovalPolicy(ActionInfo action)
    {
        if (action.Anonymous)
            return false;
        var nonApproval = NonApprovalPolicies(action.Service);
        return action.AllPolicies.Any(p => !nonApproval.Contains(p));
    }

    public static IEnumerable<string> Offenders(IEnumerable<ActionInfo> actions) =>
        actions.Where(a => IsSensitive(a) && !HasApprovalPolicy(a)).Select(a => $"{a.Service}:{a.Key}").Distinct(StringComparer.Ordinal);

    [Fact]
    public void Every_sensitive_write_has_an_approval_policy_or_is_a_row_of_the_list()
    {
        var unlisted = CeilingChecks.Unlisted(Offenders(ActionCatalog.Actions), CeilingList.Load(ListPath));
        Assert.True(unlisted.Count == 0, "Sensitive writes without an approval policy: " + string.Join(", ", unlisted));
    }

    [Fact]
    public void The_list_only_shrinks()
    {
        var list = CeilingList.Load(ListPath);
        var stale = CeilingChecks.Stale(Offenders(ActionCatalog.Actions), list);
        Assert.True(stale.Count == 0, $"These actions have their approval policy now: remove them from {ListPath}: " + string.Join(", ", stale));
        Assert.True(list.Rows.Count <= Ceiling, $"{ListPath} holds {list.Rows.Count} rows, above its ceiling of {Ceiling}");
        Assert.True(list.Malformed.Count == 0, $"{ListPath}: " + string.Join("; ", list.Malformed));
    }

    [Fact]
    public void Every_named_action_of_the_plan_exists()
    {
        var missing = NamedActions
            .SelectMany(kv => kv.Value.Select(n => (Service: kv.Key, Name: n)))
            .Where(n => !ActionCatalog.Of(n.Service).Any(a => a.IsWrite && (n.Name.EndsWith(".*", StringComparison.Ordinal)
                ? a.ControllerName == n.Name[..^2]
                : a.Key == n.Name)))
            .Select(n => $"{n.Service}:{n.Name}")
            .ToList();
        Assert.True(missing.Count == 0, "Named sensitive actions that no longer exist (rename the row): " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("Approve", true)]
    [InlineData("ReleaseLegalHold", true)]
    [InlineData("DecideAppeal", true)]
    [InlineData("PublishFromUi", true)]
    [InlineData("GrantRole", true)]
    [InlineData("Run", true)]
    [InlineData("RunReport", false)]
    [InlineData("TogglePublish", false)]
    [InlineData("Approved", false)]
    [InlineData("Reposting", false)]
    [InlineData("Create", false)]
    public void Names_match_whole_or_by_the_plans_prefixes_never_by_substring(string name, bool sensitive) =>
        Assert.Equal(sensitive, ExactNames.Contains(name) || Prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)));
}

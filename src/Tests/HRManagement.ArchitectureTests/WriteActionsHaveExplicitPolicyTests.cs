namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-51 (§6, P-6, P-8): every POST, PUT, PATCH and DELETE action of every web assembly names its policy, on the action or its class, or is
/// <c>[AllowAnonymous]</c>. The module filter's default write policy is a safety net, never the only guard. What does not is a row of
/// <c>tests/architecture/write-policy-allowlist/{service}.txt</c>, a list that only shrinks: a protected action still listed fails, and
/// the ceiling of each list is pinned here. Replaces PLAN-25's <c>HrCorePostActionsHaveExplicitPolicyTests</c>, with its rule that no
/// recruitment controller is ever listed.
/// </summary>
public sealed class WriteActionsHaveExplicitPolicyTests
{
    public static string ListPath(string service) => $"tests/architecture/write-policy-allowlist/{service}.txt";

    /// <summary>The most rows each list may hold. Lower a ceiling when its list shrinks; never raise one.</summary>
    public static readonly IReadOnlyDictionary<string, int> Ceilings = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // PLAN-51 (P-6): every list is empty except Payroll's, which PLAN-53 empties with its sixteen policies.
        ["hr-core"] = 0,
        ["hr-contracts"] = 0,
        ["payroll"] = 16,
        ["hr-movements"] = 0,
        ["hr-training"] = 0,
        ["hr-health"] = 0,
        ["compliance"] = 0,
        ["idcard"] = 0,
        ["admin-logistics"] = 0,
        ["it-infra"] = 0,
        ["records-archive"] = 0,
        ["dev-analytics"] = 0,
        ["auth-directory"] = 0,
        ["file-storage"] = 0,
        ["gateway"] = 0,
        ["shared"] = 0,
    };

    /// <summary>PLAN-25 (G-15): recruitment controllers may never be listed.</summary>
    private static readonly string[] RecruitmentControllerPrefixes =
    [
        "Recruitment", "OfferLetters", "SelectionCommittees", "Competitions", "PublicJobPostings", "TalentMarketplace",
        "CandidatePortal", "PublicCareers", "CandidateLifecycle", "ExamMarking"
    ];

    public static IEnumerable<string> Offenders(IEnumerable<ActionInfo> actions) =>
        actions.Where(a => a.IsWrite && !a.HasExplicitPolicy).Select(a => a.Key).Distinct(StringComparer.Ordinal);

    public static TheoryData<string> Services() => new(WebAssemblyCatalog.Entries.Select(e => e.Service));

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_write_names_its_policy_or_is_a_row_of_its_list(string service)
    {
        var list = CeilingList.Load(ListPath(service));
        var unlisted = CeilingChecks.Unlisted(Offenders(ActionCatalog.Of(service)), list);

        Assert.True(unlisted.Count == 0,
            $"{service}: write actions without [Authorize(Policy=…)] or [AllowAnonymous] on the action or its class: " + string.Join(", ", unlisted));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void The_list_only_shrinks(string service)
    {
        var list = CeilingList.Load(ListPath(service));
        var stale = CeilingChecks.Stale(Offenders(ActionCatalog.Of(service)), list);

        Assert.True(stale.Count == 0, $"These actions name their policy now: remove them from {ListPath(service)}: " + string.Join(", ", stale));
        Assert.True(Ceilings.ContainsKey(service), $"{service} has no pinned ceiling");
        Assert.True(list.Rows.Count <= Ceilings[service],
            $"{ListPath(service)} holds {list.Rows.Count} rows, above its ceiling of {Ceilings[service]}: the list never grows");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_row_has_a_reason_signed_by_its_owning_plan(string service)
    {
        var list = CeilingList.Load(ListPath(service));
        Assert.True(list.Malformed.Count == 0, $"{ListPath(service)}: " + string.Join("; ", list.Malformed));
    }

    [Fact]
    public void No_recruitment_controller_is_on_the_hr_core_list()
    {
        var recruitment = CeilingList.Load(ListPath("hr-core")).Rows.Keys
            .Where(k => RecruitmentControllerPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(recruitment.Count == 0, "Recruitment actions must carry an explicit policy: " + string.Join(", ", recruitment));
    }

    [Fact]
    public void Every_list_on_disk_belongs_to_a_cataloged_service()
    {
        var dir = Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "tests", "architecture", "write-policy-allowlist");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.txt").Select(Path.GetFileNameWithoutExtension) : [];
        var unknown = files.Where(f => WebAssemblyCatalog.Entries.All(e => e.Service != f)).ToList();
        Assert.True(unknown.Count == 0, "Lists with no service in the catalog: " + string.Join(", ", unknown));
    }
}

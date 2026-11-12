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

    /// <summary>
    /// PLAN-51 (review M1): the policies that admit a module's readers. A write whose policies (action and class) are all of these lets a
    /// reader write in the service, so it must be one of <see cref="DeclaredReads"/>: a read served on POST.
    /// </summary>
    public static readonly IReadOnlySet<string> ReadPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        "Module.HrCore", "Module.HrContracts", "Module.Payroll", "Module.HrMovements", "Module.HrTraining", "Module.HrHealth", "Module.IdCard",
        "Module.RecordsArchive", "Module.AdminLogistics", "Module.ItInfra", "Module.DevAnalytics", "Module.Compliance", "Module.AuthDirectory",
        "Module.FileStorage", "HrCore.ReadOnly", "HrCore.Lookup", "HrCore.WorkInbox", "Policy.Reader", "Workflow.DefinitionRead",
        "HrContracts.HireView", "HrMovements.ApprovalAccess", "Gateway.Authenticated", "Gateway.Dashboard", "Gateway.Audit", "Evidence.Read",
    };

    /// <summary>The POST actions that read (an export, a lookup by ids, an evaluation, a check), or that the plan keeps as they are; each with its reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> DeclaredReads = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["hr-core:Reports.ExportEmployeesCsv"] = "an export of what the reader sees",
        ["hr-core:Reports.ExportEmployeesExcel"] = "an export of what the reader sees",
        ["hr-core:Reports.ExportEmployeesPdf"] = "an export of what the reader sees",
        ["hr-core:Reports.ExportEvaluationsCsv"] = "an export of what the reader sees",
        ["hr-core:Reports.ExportStatusHistoryCsv"] = "an export of what the reader sees",
        ["hr-core:CodeGeneration.Preview"] = "a preview of the next code, nothing issued",
        ["hr-core:LookupApi.GetEmployeesByIds"] = "the pickers' lookup by ids (HrCore.Lookup: staff and services)",
        ["hr-core:LookupApi.GetOrgUnitsByIds"] = "the pickers' lookup by ids (HrCore.Lookup: staff and services)",
        ["hr-core:Policies.Evaluate"] = "an evaluation of the policy engine, nothing recorded",
        ["hr-core:PolicyTemplates.Simulate"] = "a simulation of a draft, nothing recorded",
        ["hr-core:Signatures.Verify"] = "checks a signature (plan §4: HrCore.ReadOnly)",
        ["hr-core:ExportJobs.Cancel"] = "cancels the caller's own export job (plan §4: HrCore.ReadOnly with the owner check)",
        ["hr-contracts:ExportJobs.Cancel"] = "cancels the caller's own export job",
        ["hr-contracts:HireWizard.Step1"] = "the plan keeps HireWizard as it is (§4: HrContracts.HireView, PLAN-29)",
        ["hr-contracts:HireWizard.Step2Confirm"] = "the plan keeps HireWizard as it is (§4: HrContracts.HireView, PLAN-29)",
        ["compliance:ExportJobs.Cancel"] = "cancels the caller's own export job",
        ["dev-analytics:ExportJobs.Cancel"] = "cancels the caller's own export job",
        ["hr-movements:DistributionPlans.GapAnalysis"] = "redirects to the GET analysis, nothing written",
        ["hr-training:Certificates.Verify"] = "checks a certificate code, nothing written",
        ["file-storage:FilesApi.BatchMetadata"] = "reads the metadata of a batch of files",
        ["gateway:EvidenceArtifacts.ExportPack"] = "an export of evidence for its readers (EVD-003)",
        ["shared:Culture.SetCulture"] = "the language cookie, no record",
    };

    public static IEnumerable<string> ReadOnlyWrites(IEnumerable<ActionInfo> actions) =>
        actions.Where(a => a.IsWrite && !a.Anonymous && a.HasExplicitPolicy && a.AllPolicies.All(ReadPolicies.Contains))
            .Select(a => $"{a.Service}:{a.Key}")
            .Distinct(StringComparer.Ordinal);

    [Fact]
    public void Every_write_names_a_policy_that_is_not_only_a_read_policy_or_is_a_declared_read()
    {
        var undeclared = ReadOnlyWrites(ActionCatalog.Actions.Where(a => a.Service != "payroll"))
            .Where(k => !DeclaredReads.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(undeclared.Count == 0,
            "Writes whose only policies admit the module's readers (name the module's write or approval policy): " + string.Join(", ", undeclared));
    }

    [Fact]
    public void Every_declared_read_still_exists_with_a_read_policy_only()
    {
        var current = ReadOnlyWrites(ActionCatalog.Actions).ToHashSet(StringComparer.Ordinal);
        var stale = DeclaredReads.Keys.Where(k => !current.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Declared reads that no longer exist or now name a write policy (remove them): " + string.Join(", ", stale));
        Assert.True(DeclaredReads.Count <= 22, "The declared reads never grow: a new read on POST is reviewed with its reason");
    }

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

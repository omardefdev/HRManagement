using System.Security.Claims;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ArchitectureTests;

/// <summary>
/// PLAN-53 (§1, P-4, G-IAM-02, G-PAY-10): no payroll write works with the module role <c>payroll</c> alone. Payroll's write-policy list is
/// gone and takes no row; every write of Payroll.Web names a policy the module role does not pass; each of the sixteen writes PLAN-51
/// froze, and every action PLAN-53 adds, carries the policy the plan gives it; and each payroll persona reaches exactly the writes its
/// roles give it. The policies are evaluated by the real authorization service, the way the module filter and MVC evaluate them.
/// </summary>
public sealed class PayrollWriteActionsTests
{
    private const string Service = "payroll";

    /// <summary>The policies the module role alone passes: the module's read policy and the module filter's default write policy.</summary>
    private static readonly IReadOnlySet<string> ModuleRolePolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        HrManagementPolicies.ModulePayroll, HrManagementPolicies.PayrollWrite,
    };

    /// <summary>The one write the module role keeps: cancelling the caller's own export job (the handler checks the owner).</summary>
    private const string OwnExportJobCancel = "ExportJobs.Cancel";

    /// <summary>The sixteen writes PLAN-51 froze in Payroll's list, each with the policy PLAN-53 names on the action (P-4).</summary>
    public static readonly IReadOnlyDictionary<string, string> Sixteen = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CompensationTypes.Create"] = HrManagementPolicies.PayrollMasterData,
        ["CompensationTypes.Edit"] = HrManagementPolicies.PayrollMasterData,
        ["CompensationTypes.Delete"] = HrManagementPolicies.PayrollMasterData,
        ["DeductionTypes.Create"] = HrManagementPolicies.PayrollMasterData,
        ["DeductionTypes.Edit"] = HrManagementPolicies.PayrollMasterData,
        ["DeductionTypes.Delete"] = HrManagementPolicies.PayrollMasterData,
        ["SalaryScales.Create"] = HrManagementPolicies.PayrollMasterData,
        ["SalaryScales.Edit"] = HrManagementPolicies.PayrollMasterData,
        ["SalaryScales.Delete"] = HrManagementPolicies.PayrollMasterData,
        ["PayGrades.Create"] = HrManagementPolicies.PayrollMasterData,
        ["PayGrades.Edit"] = HrManagementPolicies.PayrollMasterData,
        ["PayGrades.Delete"] = HrManagementPolicies.PayrollMasterData,
        ["EmployeePayProfiles.Create"] = HrManagementPolicies.PayrollProcess,
        ["PayrollRuns.Create"] = HrManagementPolicies.PayrollProcess,
        ["PayrollReports.BulkPayslips"] = HrManagementPolicies.PayrollProcess,
        [OwnExportJobCancel] = HrManagementPolicies.ModulePayroll,
    };

    /// <summary>The write actions PLAN-53 adds, each with the policy it names on the action (P-4): submit with Payroll.Process, the
    /// decisions and the manual activation with Payroll.Approve, cancel with the policy that admits whoever may request or decide.</summary>
    public static readonly IReadOnlyDictionary<string, string> Added = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PayProfileChangesApi.Submit"] = HrManagementPolicies.PayrollProcess,
        ["PayrollApi.UpsertPayProfile"] = HrManagementPolicies.PayrollProcess,          // the PUT alias of Submit
        ["PayProfileChangesApi.Approve"] = HrManagementPolicies.PayrollApprove,
        ["PayProfileChangesApi.Reject"] = HrManagementPolicies.PayrollApprove,
        ["PayProfileChangesApi.ActivateDue"] = HrManagementPolicies.PayrollApprove,
        ["PayProfileChangesApi.Cancel"] = HrManagementPolicies.PayrollChangeCancel,
        ["PayProfileChanges.Approve"] = HrManagementPolicies.PayrollApprove,
        ["PayProfileChanges.Reject"] = HrManagementPolicies.PayrollApprove,
        ["PayProfileChanges.Cancel"] = HrManagementPolicies.PayrollChangeCancel,
        // «طلبات البيانات المرجعية»: the decision of a scale or grade change; its submission is the sixteen's Create, Edit and Delete.
        ["PayrollReferenceChanges.Approve"] = HrManagementPolicies.PayrollApprove,
        ["PayrollReferenceChanges.Reject"] = HrManagementPolicies.PayrollApprove,
        ["PayrollReferenceChanges.Cancel"] = HrManagementPolicies.PayrollChangeCancel,
    };

    /// <summary>
    /// The write actions PLAN-54 adds or renames a policy on (P-4): creating, importing and editing a framework draft and setting its
    /// date with Payroll.MasterData, on the screens and the API alike; the cycle's acts with Payroll.Approve.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pay054 = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PayrollConfigurations.Create"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollConfigurations.Import"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollConfigurations.Edit"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollConfigurations.Approve"] = HrManagementPolicies.PayrollApprove,
        ["PayrollConfigurations.LegalAttest"] = HrManagementPolicies.PayrollLegalAttest,
        ["PayrollConfigurations.Publish"] = HrManagementPolicies.PayrollApprove,
        ["PayrollConfigurations.Activate"] = HrManagementPolicies.PayrollApprove,
        ["PayrollFrameworksApi.Create"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollFrameworksApi.Import"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollFrameworksApi.SetEffectiveFrom"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollFrameworksApi.Approve"] = HrManagementPolicies.PayrollApprove,
        ["PayrollFrameworksApi.LegalAttest"] = HrManagementPolicies.PayrollLegalAttest,
        ["PayrollFrameworksApi.Publish"] = HrManagementPolicies.PayrollApprove,
        ["PayrollFrameworksApi.Activate"] = HrManagementPolicies.PayrollApprove,
    };

    /// <summary>The controllers PLAN-54 adds or completes: every write of theirs has a row in <see cref="Pay054"/>.</summary>
    private static readonly string[] Pay054Controllers = ["PayrollConfigurations", "PayrollFrameworksApi"];

    /// <summary>
    /// PLAN-54 (§4, D-PAY-6): a manual allowance, deduction or adjustment is entered with Payroll.Process and decided, by a second
    /// person, with Payroll.Approve.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pay054ManualInputs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AllowanceRecords.Create"] = HrManagementPolicies.PayrollProcess,
        ["AllowanceRecords.Approve"] = HrManagementPolicies.PayrollApprove,
        ["AllowanceRecords.Reject"] = HrManagementPolicies.PayrollApprove,
        ["DeductionRecords.Create"] = HrManagementPolicies.PayrollProcess,
        ["DeductionRecords.Approve"] = HrManagementPolicies.PayrollApprove,
        ["DeductionRecords.Reject"] = HrManagementPolicies.PayrollApprove,
        ["PayrollAdjustments.Create"] = HrManagementPolicies.PayrollProcess,
        ["PayrollAdjustments.Approve"] = HrManagementPolicies.PayrollApprove,
        ["PayrollAdjustments.Reject"] = HrManagementPolicies.PayrollApprove,
    };

    private static readonly string[] ManualInputControllers = ["AllowanceRecords", "DeductionRecords", "PayrollAdjustments"];

    /// <summary>
    /// Reads of the framework that are not open to the module role alone: the forms of a write (a person who cannot submit one does not
    /// open it) and the export of a version as a pack.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pay054GuardedReads = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PayrollConfigurations.Create"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollConfigurations.Import"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollConfigurations.Edit"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollConfigurations.Export"] = HrManagementPolicies.PayrollMasterData,
        ["PayrollFrameworksApi.Export"] = HrManagementPolicies.PayrollMasterData,
    };

    /// <summary>
    /// PLAN-55 (§1, D-PAY-14, P-4): an employee's bank account is registered with Payroll.Process; verified, rejected and retired with
    /// Payroll.Approve (retiring the account in force can leave an employee with no destination). On the screens and the API alike.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pay055 = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["EmployeeBankAccountsApi.Register"] = HrManagementPolicies.PayrollProcess,
        ["EmployeeBankAccountsApi.Verify"] = HrManagementPolicies.PayrollApprove,
        ["EmployeeBankAccountsApi.Reject"] = HrManagementPolicies.PayrollApprove,
        ["EmployeeBankAccountsApi.Retire"] = HrManagementPolicies.PayrollApprove,
        ["EmployeeBankAccounts.Create"] = HrManagementPolicies.PayrollProcess,
        ["EmployeeBankAccounts.Verify"] = HrManagementPolicies.PayrollApprove,
        ["EmployeeBankAccounts.Reject"] = HrManagementPolicies.PayrollApprove,
        ["EmployeeBankAccounts.Retire"] = HrManagementPolicies.PayrollApprove,
    };

    /// <summary>The controllers PLAN-55 adds: every write of theirs has a row in <see cref="Pay055"/>.</summary>
    private static readonly string[] Pay055Controllers = ["EmployeeBankAccounts", "EmployeeBankAccountsApi"];

    /// <summary>
    /// Reads of the bank accounts that are not open to the module role alone: the registration form (a person who cannot register does
    /// not open it), and the verification page, the one place an account number is shown whole.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Pay055GuardedReads = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["EmployeeBankAccounts.Create"] = HrManagementPolicies.PayrollProcess,
        ["EmployeeBankAccounts.Verify"] = HrManagementPolicies.PayrollApprove,
    };

    private static IEnumerable<ActionInfo> Writes => ActionCatalog.Of(Service).Where(a => a.IsWrite && !a.Anonymous);

    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Append(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")))
            .Append(new Claim("azp", "hr-gateway-client"))
            .Append(new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString())),
        "TestAuth"));

    private static IAuthorizationService Authorization(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// What MVC and the module filter ask of a caller: the module policy (or the action's override), every policy named on the action and
    /// its class, and, for a write that names none, the module's write policy.
    /// </summary>
    private static async Task<bool> ReachesAsync(IAuthorizationService authorization, ClaimsPrincipal user, ActionInfo action)
    {
        var policies = action.AllPolicies.Append(action.EffectiveOverride ?? HrManagementPolicies.ModulePayroll).ToList();
        if (!action.HasExplicitPolicy)
            policies.Add(HrManagementPolicies.PayrollWrite);
        foreach (var policy in policies.Distinct(StringComparer.Ordinal))
        {
            if (!(await authorization.AuthorizeAsync(user, policy)).Succeeded)
                return false;
        }
        return true;
    }

    private static async Task<List<string>> ReachedAsync(ClaimsPrincipal user, IEnumerable<ActionInfo> actions, IConfiguration? configuration = null)
    {
        var authorization = Authorization(configuration);
        var reached = new List<string>();
        foreach (var action in actions)
        {
            if (await ReachesAsync(authorization, user, action))
                reached.Add(action.Key);
        }
        return reached.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    // --- the list ---

    [Fact]
    public void Payroll_has_no_write_policy_list_and_no_list_takes_a_row()
    {
        var path = WriteActionsHaveExplicitPolicyTests.ListPath(Service);
        Assert.False(global::System.IO.File.Exists(Path.Combine(RepoPaths.Root, path)), $"{path} is back: Payroll's write-policy list was deleted by PLAN-53");
        Assert.Empty(CeilingList.Load(path).Rows);
        Assert.All(WriteActionsHaveExplicitPolicyTests.Ceilings, ceiling => Assert.Equal(0, ceiling.Value));
        Assert.DoesNotContain("PLAN-53", CeilingList.Owners);
    }

    [Fact]
    public void Every_payroll_write_names_its_policy()
    {
        var offenders = WriteActionsHaveExplicitPolicyTests.Offenders(ActionCatalog.Of(Service)).ToList();
        Assert.True(offenders.Count == 0, "Payroll writes without a named policy: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_payroll_write_names_a_policy_the_module_role_does_not_pass()
    {
        var offenders = Writes
            .Where(a => a.Key != OwnExportJobCancel)
            .Where(a => a.AllPolicies.Append(a.EffectiveOverride ?? HrManagementPolicies.ModulePayroll).All(ModuleRolePolicies.Contains))
            .Select(a => a.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(offenders.Count == 0,
            "Payroll writes the module role 'payroll' alone could reach (name Payroll.Process, Payroll.Approve, Payroll.Post, Payroll.Reverse, "
            + "Payroll.MasterData or Payroll.ChangeCancel on the action): " + string.Join(", ", offenders));
    }

    // --- the sixteen ---

    public static TheoryData<string, string> SixteenRows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, policy) in Sixteen)
            data.Add(key, policy);
        return data;
    }

    [Theory]
    [MemberData(nameof(SixteenRows))]
    public void Each_of_the_sixteen_names_the_policy_the_plan_gives_it_on_the_action(string key, string policy)
    {
        var actions = Writes.Where(a => a.Key == key).ToList();
        Assert.True(actions.Count > 0, $"{key} is no longer a write action of Payroll.Web");
        Assert.All(actions, a => Assert.Equal([policy], a.ActionPolicies));
    }

    public static TheoryData<string, string> AddedRows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, policy) in Added)
            data.Add(key, policy);
        return data;
    }

    [Theory]
    [MemberData(nameof(AddedRows))]
    public void Each_action_the_plan_adds_names_its_policy_on_the_action(string key, string policy)
    {
        var actions = Writes.Where(a => a.Key == key).ToList();
        Assert.True(actions.Count > 0, $"{key} is not a write action of Payroll.Web");
        Assert.All(actions, a => Assert.Equal([policy], a.ActionPolicies));
    }

    [Fact]
    public void Every_write_of_the_controllers_the_plan_adds_is_in_the_table()
    {
        // A write added later to a request controller needs its row here, with the policy it names.
        var controllers = new[] { "PayProfileChangesApi", "PayProfileChanges", "PayrollReferenceChanges" };
        var unlisted = Writes.Where(a => controllers.Contains(a.ControllerName)).Select(a => a.Key)
            .Where(k => !Added.ContainsKey(k)).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0, "Write actions of the request controllers with no row in PayrollWriteActionsTests.Added: " + string.Join(", ", unlisted));
    }

    // --- PLAN-54: the framework's actions ---

    public static TheoryData<string, string> Pay054Rows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, policy) in Pay054)
            data.Add(key, policy);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pay054Rows))]
    public void Each_action_plan_54_adds_names_its_policy_on_the_action(string key, string policy)
    {
        var actions = Writes.Where(a => a.Key == key).ToList();
        Assert.True(actions.Count > 0, $"{key} is not a write action of Payroll.Web");
        Assert.All(actions, a => Assert.Equal([policy], a.ActionPolicies));
    }

    [Fact]
    public void Every_write_of_the_controllers_plan_54_adds_or_completes_is_in_its_table()
    {
        var unlisted = Writes.Where(a => Pay054Controllers.Contains(a.ControllerName)).Select(a => a.Key)
            .Where(k => !Pay054.ContainsKey(k)).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0, "Write actions with no row in PayrollWriteActionsTests.Pay054: " + string.Join(", ", unlisted));
    }

    public static TheoryData<string, string> Pay054ManualInputRows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, policy) in Pay054ManualInputs)
            data.Add(key, policy);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pay054ManualInputRows))]
    public void Each_manual_input_write_names_its_policy_on_the_action(string key, string policy)
    {
        var actions = Writes.Where(a => a.Key == key).ToList();
        Assert.True(actions.Count > 0, $"{key} is not a write action of Payroll.Web");
        Assert.All(actions, a => Assert.Equal([policy], a.ActionPolicies));
    }

    [Fact]
    public async Task A_processor_enters_a_manual_input_and_only_an_approver_decides_it()
    {
        var writes = Writes.Where(a => ManualInputControllers.Contains(a.ControllerName)).ToList();
        var unlisted = writes.Select(a => a.Key).Where(k => !Pay054ManualInputs.ContainsKey(k)).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0, "Manual input writes with no row in PayrollWriteActionsTests.Pay054ManualInputs: " + string.Join(", ", unlisted));

        var processor = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor);
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);
        Assert.Equal(
            Pay054ManualInputs.Where(p => p.Value == HrManagementPolicies.PayrollProcess).Select(p => p.Key).Order(StringComparer.Ordinal),
            await ReachedAsync(processor, writes));
        Assert.Equal(
            Pay054ManualInputs.Where(p => p.Value == HrManagementPolicies.PayrollApprove).Select(p => p.Key).Order(StringComparer.Ordinal),
            await ReachedAsync(approver, writes));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll), writes));
    }

    [Fact]
    public void The_forms_of_a_framework_write_and_the_export_need_the_writes_policy_and_the_other_reads_are_open_to_the_module()
    {
        var reads = ActionCatalog.Of(Service).Where(a => Pay054Controllers.Contains(a.ControllerName) && !a.IsWrite).ToList();
        Assert.NotEmpty(reads);
        foreach (var read in reads)
        {
            var expected = Pay054GuardedReads.TryGetValue(read.Key, out var policy)
                ? new[] { HrManagementPolicies.ModulePayroll, policy }
                : [HrManagementPolicies.ModulePayroll];
            Assert.True(
                expected.Order(StringComparer.Ordinal).SequenceEqual(read.AllPolicies.Distinct().Order(StringComparer.Ordinal)),
                $"{read.Key} ({string.Join("/", read.Verbs)}): policies {string.Join(", ", read.AllPolicies.Distinct())}");
        }

        Assert.All(Pay054GuardedReads.Keys, key => Assert.Contains(reads, r => r.Key == key));
    }

    [Fact]
    public async Task A_processor_creates_imports_and_edits_a_framework_and_an_approver_runs_its_cycle_and_neither_does_the_others_part()
    {
        var framework = Writes.Where(a => Pay054Controllers.Contains(a.ControllerName)).ToList();
        var processor = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor);
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);

        Assert.Equal(
            Pay054.Where(p => p.Value == HrManagementPolicies.PayrollMasterData).Select(p => p.Key).Order(StringComparer.Ordinal),
            await ReachedAsync(processor, framework));
        Assert.Equal(
            Pay054.Where(p => p.Value != HrManagementPolicies.PayrollMasterData).Select(p => p.Key).Order(StringComparer.Ordinal),
            await ReachedAsync(approver, framework));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll), framework));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll, HrManagementRoles.PayrollPoster), framework));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll, HrManagementRoles.PayrollReverser), framework));
    }

    public static TheoryData<string, string> Pay055Rows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (key, policy) in Pay055)
            data.Add(key, policy);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pay055Rows))]
    public void Each_action_plan_55_adds_names_its_policy_on_the_action(string key, string policy)
    {
        var actions = Writes.Where(a => a.Key == key).ToList();
        Assert.True(actions.Count > 0, $"{key} is not a write action of Payroll.Web");
        Assert.All(actions, a => Assert.Equal([policy], a.ActionPolicies));
    }

    [Fact]
    public void Every_write_of_the_controllers_plan_55_adds_is_in_its_table()
    {
        var unlisted = Writes.Where(a => Pay055Controllers.Contains(a.ControllerName)).Select(a => a.Key)
            .Where(k => !Pay055.ContainsKey(k)).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0, "Write actions with no row in PayrollWriteActionsTests.Pay055: " + string.Join(", ", unlisted));
    }

    [Fact]
    public async Task A_processor_registers_a_bank_account_and_only_an_approver_verifies_rejects_or_retires_it()
    {
        var writes = Writes.Where(a => Pay055Controllers.Contains(a.ControllerName)).ToList();
        var processor = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor);
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);

        Assert.Equal(
            Pay055.Where(p => p.Value == HrManagementPolicies.PayrollProcess).Select(p => p.Key).Order(StringComparer.Ordinal),
            await ReachedAsync(processor, writes));
        Assert.Equal(
            Pay055.Where(p => p.Value == HrManagementPolicies.PayrollApprove).Select(p => p.Key).Order(StringComparer.Ordinal),
            await ReachedAsync(approver, writes));
        // The module role alone, the poster, the reverser and a branch manager reach none of them; hr-admin holds both policies (the
        // handler then refuses his verification of his own registration).
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll), writes));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll, HrManagementRoles.PayrollPoster), writes));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.Payroll, HrManagementRoles.PayrollReverser), writes));
        Assert.Empty(await ReachedAsync(User(HrManagementRoles.BranchManager, HrManagementRoles.HrCore), writes));
        Assert.Equal(Pay055.Keys.Order(StringComparer.Ordinal), await ReachedAsync(User(HrManagementRoles.Admin), writes));
    }

    [Fact]
    public void The_registration_form_and_the_verification_page_need_their_writes_policy_and_the_lists_are_open_to_the_module()
    {
        var reads = ActionCatalog.Of(Service).Where(a => Pay055Controllers.Contains(a.ControllerName) && !a.IsWrite).ToList();
        Assert.NotEmpty(reads);
        foreach (var read in reads)
        {
            var expected = Pay055GuardedReads.TryGetValue(read.Key, out var policy)
                ? new[] { HrManagementPolicies.ModulePayroll, policy }
                : [HrManagementPolicies.ModulePayroll];
            Assert.True(
                expected.Order(StringComparer.Ordinal).SequenceEqual(read.AllPolicies.Distinct().Order(StringComparer.Ordinal)),
                $"{read.Key} ({string.Join("/", read.Verbs)}): policies {string.Join(", ", read.AllPolicies.Distinct())}");
        }

        Assert.All(Pay055GuardedReads.Keys, key => Assert.Contains(reads, r => r.Key == key));
    }

    [Fact]
    public void The_bank_list_is_empty_in_production_and_staging_and_in_the_prodlike_mirror()
    {
        foreach (var environment in new[] { "Production", "Staging" })
        {
            var settings = Settings(environment);
            Assert.DoesNotContain(settings.AsEnumerable(), pair => pair.Key.StartsWith("Payroll:Bank:Banks:", StringComparison.OrdinalIgnoreCase));
        }

        var compose = global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docker-compose.acceptance-prodlike.yml"));
        Assert.Contains("Payroll__Bank__Banks: \"\"", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("Payroll__Bank__Banks__", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lists_and_the_details_of_the_requests_are_open_to_the_module_and_are_no_writes()
    {
        var reads = ActionCatalog.Of(Service)
            .Where(a => a.ControllerName is "PayProfileChangesApi" or "PayProfileChanges" or "PayrollReferenceChanges" && !a.IsWrite).ToList();
        Assert.NotEmpty(reads);
        Assert.All(reads, a => Assert.Equal([HrManagementPolicies.ModulePayroll], a.AllPolicies.Distinct()));
    }

    [Fact]
    public void The_sixteen_are_sixteen()
    {
        Assert.Equal(16, Sixteen.Count);
        Assert.Equal(12, Sixteen.Count(p => p.Value == HrManagementPolicies.PayrollMasterData));
        Assert.Equal(3, Sixteen.Count(p => p.Value == HrManagementPolicies.PayrollProcess));
    }

    // --- who reaches what ---

    private static IEnumerable<ActionInfo> SixteenActions => Writes.Where(a => Sixteen.ContainsKey(a.Key));

    [Fact]
    public async Task The_module_role_alone_reaches_no_payroll_write_but_cancelling_its_own_export_job()
    {
        Assert.Equal([OwnExportJobCancel], await ReachedAsync(User(HrManagementRoles.Payroll), Writes));
    }

    [Fact]
    public async Task A_processor_reaches_the_sixteen_and_an_approver_and_a_poster_reach_none_of_them_but_their_own_export_job()
    {
        var processor = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor);
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);
        var poster = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollPoster);
        var reverser = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollReverser);

        Assert.Equal(Sixteen.Keys.Order(StringComparer.Ordinal), await ReachedAsync(processor, SixteenActions));
        Assert.Equal([OwnExportJobCancel], await ReachedAsync(approver, SixteenActions));
        Assert.Equal([OwnExportJobCancel], await ReachedAsync(poster, SixteenActions));
        Assert.Equal([OwnExportJobCancel], await ReachedAsync(reverser, SixteenActions));
    }

    [Fact]
    public async Task Each_operational_role_reaches_exactly_the_writes_whose_policies_it_passes()
    {
        // The whole of Payroll.Web, new actions included: a role reaches a write only through a policy that names the role.
        var byRole = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [HrManagementRoles.PayrollProcessor] =
                [HrManagementPolicies.PayrollProcess, HrManagementPolicies.PayrollMasterData, HrManagementPolicies.PayrollChangeCancel],
            // PLAN-54 (D-PAY-12): the legal attestation is met through the policy the setting names, Payroll.Approve by default.
            [HrManagementRoles.PayrollApprover] =
                [HrManagementPolicies.PayrollApprove, HrManagementPolicies.PayrollChangeCancel, HrManagementPolicies.PayrollLegalAttest],
            [HrManagementRoles.PayrollPoster] = [HrManagementPolicies.PayrollPost, HrManagementPolicies.PayrollReverse],
            [HrManagementRoles.PayrollReverser] = [HrManagementPolicies.PayrollReverse],
        };

        foreach (var (role, passes) in byRole)
        {
            var allowed = passes.Concat(ModuleRolePolicies).ToHashSet(StringComparer.Ordinal);
            var expected = Writes
                .Where(a => a.AllPolicies.Append(a.EffectiveOverride ?? HrManagementPolicies.ModulePayroll).All(allowed.Contains))
                .Select(a => a.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            Assert.Equal(expected, await ReachedAsync(User(HrManagementRoles.Payroll, role), Writes));
        }
    }

    [Fact]
    public async Task Hr_admin_reaches_the_sixteen_in_development_and_with_payrolls_production_settings()
    {
        var admin = User(HrManagementRoles.Admin);
        Assert.Equal(Sixteen.Keys.Order(StringComparer.Ordinal), await ReachedAsync(admin, SixteenActions));
        // Payroll.MasterData is not one of Authorization:AdminExcludedPolicies (P-4): hr-admin keeps the sixteen outside demo mode too.
        Assert.Equal(Sixteen.Keys.Order(StringComparer.Ordinal), await ReachedAsync(admin, SixteenActions, Settings("Production")));
        Assert.Equal(Sixteen.Keys.Order(StringComparer.Ordinal), await ReachedAsync(admin, SixteenActions, Settings("Staging")));
    }

    // --- Payroll.MasterData: roles from configuration ---

    private static IConfiguration Settings(string? environment)
    {
        var name = environment is null ? "appsettings.json" : $"appsettings.{environment}.json";
        var path = Path.Combine(RepoPaths.Root, "src", "Services", "Payroll", "Payroll.Web", name);
        return new ConfigurationBuilder().AddJsonStream(new MemoryStream(global::System.IO.File.ReadAllBytes(path))).Build();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Payrolls_settings_name_the_master_data_roles(string? environment)
    {
        var roles = Settings(environment).GetSection(ConfiguredRolesHandler.PolicyRolesKey(HrManagementPolicies.PayrollMasterData)).Get<string[]>();
        Assert.Equal([HrManagementRoles.Admin, HrManagementRoles.PayrollProcessor], roles ?? []);
        Assert.DoesNotContain(HrManagementPolicies.PayrollMasterData,
            Settings(environment).GetSection(ConfiguredRolesHandler.AdminExcludedPoliciesKey).Get<string[]>() ?? []);
    }

    [Fact]
    public void The_prodlike_stack_mirrors_the_master_data_roles_of_production()
    {
        var compose = global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docker-compose.acceptance-prodlike.yml"));
        Assert.Contains("Authorization__Policies__Payroll.MasterData__Roles__0: \"hr-admin\"", compose, StringComparison.Ordinal);
        Assert.Contains("Authorization__Policies__Payroll.MasterData__Roles__1: \"payroll-processor\"", compose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_master_data_roles_are_read_from_configuration()
    {
        var key = ConfiguredRolesHandler.PolicyRolesKey(HrManagementPolicies.PayrollMasterData);
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{key}:0"] = HrManagementRoles.PayrollApprover,
        }).Build();
        var processor = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor);
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);

        Assert.True((await Authorization().AuthorizeAsync(processor, HrManagementPolicies.PayrollMasterData)).Succeeded);
        Assert.False((await Authorization().AuthorizeAsync(approver, HrManagementPolicies.PayrollMasterData)).Succeeded);
        Assert.False((await Authorization(configured).AuthorizeAsync(processor, HrManagementPolicies.PayrollMasterData)).Succeeded);
        Assert.True((await Authorization(configured).AuthorizeAsync(approver, HrManagementPolicies.PayrollMasterData)).Succeeded);
        // The module role alone never passes, whatever the default.
        Assert.False((await Authorization().AuthorizeAsync(User(HrManagementRoles.Payroll), HrManagementPolicies.PayrollMasterData)).Succeeded);
    }

    [Fact]
    public async Task Cancelling_a_change_admits_whoever_may_request_or_decide_one_and_nobody_else()
    {
        var authorization = Authorization();
        async Task<bool> PassesAsync(params string[] roles) =>
            (await authorization.AuthorizeAsync(User(roles), HrManagementPolicies.PayrollChangeCancel)).Succeeded;

        Assert.True(await PassesAsync(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor));
        Assert.True(await PassesAsync(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover));
        Assert.True(await PassesAsync(HrManagementRoles.Admin));
        Assert.False(await PassesAsync(HrManagementRoles.Payroll));
        Assert.False(await PassesAsync(HrManagementRoles.Payroll, HrManagementRoles.PayrollPoster));
        Assert.False(await PassesAsync(HrManagementRoles.Payroll, HrManagementRoles.PayrollReverser));
    }

    // --- PLAN-54 (D-PAY-12): Payroll.LegalAttest follows the setting ---

    private static IConfiguration AttestorPolicy(string? name) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [PayrollLegalAttestPolicy.SettingKey] = name }).Build();

    [Fact]
    public async Task The_legal_attestation_is_met_by_whoever_meets_the_policy_the_setting_names()
    {
        var processor = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollProcessor);
        var approver = User(HrManagementRoles.Payroll, HrManagementRoles.PayrollApprover);
        var module = User(HrManagementRoles.Payroll);
        async Task<bool> PassesAsync(ClaimsPrincipal user, IConfiguration? configuration = null) =>
            (await Authorization(configuration).AuthorizeAsync(user, HrManagementPolicies.PayrollLegalAttest)).Succeeded;

        // By default (no setting, and Payroll's own settings files) it is Payroll.Approve.
        Assert.True(await PassesAsync(approver));
        Assert.True(await PassesAsync(User(HrManagementRoles.Admin)));
        Assert.False(await PassesAsync(processor));
        Assert.False(await PassesAsync(module));
        foreach (var environment in new[] { null, "Production", "Staging" })
        {
            Assert.Equal(HrManagementPolicies.PayrollApprove, Settings(environment)[PayrollLegalAttestPolicy.SettingKey]);
            Assert.True(await PassesAsync(approver, Settings(environment)));
            Assert.False(await PassesAsync(processor, Settings(environment)));
        }

        // Another policy named by the setting takes its place without a code change.
        Assert.True(await PassesAsync(processor, AttestorPolicy(HrManagementPolicies.PayrollProcess)));
        Assert.False(await PassesAsync(approver, AttestorPolicy(HrManagementPolicies.PayrollProcess)));
        // A name that is unknown, or the policy itself, lets nobody through (and stops the service at start, below).
        Assert.False(await PassesAsync(approver, AttestorPolicy("Payroll.NoSuchPolicy")));
        Assert.False(await PassesAsync(approver, AttestorPolicy(HrManagementPolicies.PayrollLegalAttest)));
    }

    [Fact]
    public async Task The_service_refuses_to_start_on_an_attestor_policy_that_is_unknown_or_names_itself()
    {
        static IServiceProvider Services(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddLogging();
            services.AddAuthorization();
            services.AddHrManagementAuthorizationPolicies();
            return services.BuildServiceProvider();
        }

        await PayrollLegalAttestPolicy.ValidateAsync(Services(new ConfigurationBuilder().Build()));
        await PayrollLegalAttestPolicy.ValidateAsync(Services(AttestorPolicy(HrManagementPolicies.PayrollApprove)));
        await PayrollLegalAttestPolicy.ValidateAsync(Services(AttestorPolicy(HrManagementPolicies.PayrollProcess)));
        foreach (var environment in new[] { null, "Production", "Staging" })
            await PayrollLegalAttestPolicy.ValidateAsync(Services(Settings(environment)));

        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => PayrollLegalAttestPolicy.ValidateAsync(Services(AttestorPolicy("Payroll.NoSuchPolicy"))));
        Assert.Contains(PayrollLegalAttestPolicy.SettingKey, unknown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("NoSuchPolicy", unknown.Message, StringComparison.Ordinal);       // the configured value is not echoed
        var self = await Assert.ThrowsAsync<InvalidOperationException>(() => PayrollLegalAttestPolicy.ValidateAsync(Services(AttestorPolicy(HrManagementPolicies.PayrollLegalAttest))));
        Assert.Contains("itself", self.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prodlike_stack_mirrors_the_plan_54_settings_of_production()
    {
        var compose = global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docker-compose.acceptance-prodlike.yml"));
        Assert.Contains("Payroll__Legal__AttestorPolicy: \"Payroll.Approve\"", compose, StringComparison.Ordinal);
        Assert.Contains("Payroll__ManualInputs__RequireApproval: \"true\"", compose, StringComparison.Ordinal);
        Assert.Equal("True", Settings("Production")["Payroll:ManualInputs:RequireApproval"]);
    }

    [Fact]
    public void Payrolls_settings_files_parse()
    {
        foreach (var environment in new[] { null, "Production", "Staging" })
        {
            var name = environment is null ? "appsettings.json" : $"appsettings.{environment}.json";
            var bytes = global::System.IO.File.ReadAllBytes(Path.Combine(RepoPaths.Root, "src", "Services", "Payroll", "Payroll.Web", name));
            using var document = JsonDocument.Parse(bytes.AsSpan(bytes.Length >= 3 && bytes[0] == 0xEF ? 3 : 0).ToArray());
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }
}

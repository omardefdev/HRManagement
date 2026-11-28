using System.Reflection;
using System.Text.RegularExpressions;
using HR.Contracts.Web.Controllers;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-60 (P-10, D-51-2): every POST of the two separation controllers names its policy — PLAN-51's split kept: the maker's acts
/// <c>HrContracts.Write</c>, a second person's acts <c>HrContracts.Approve</c> (the execution among them, as the publication was) — validates
/// an antiforgery token, and sits under the controllers' org-scope stance; their reads take the module policy. And the guards of the
/// state machine: no path starts the saga but the execution, and no path puts a record in PendingApproval without an engine instance.
/// </summary>
public sealed class HrContractsSeparationPostPoliciesTests
{
    private const string Write = HrManagementPolicies.HrContractsWrite;
    private const string Approve = HrManagementPolicies.HrContractsApprove;

    public static TheoryData<Type, string, string> Posts() => new()
    {
        { typeof(SeparationRecordsController), "Create", Write },
        { typeof(SeparationRecordsController), "Edit", Write },
        { typeof(SeparationRecordsController), "Submit", Write },
        { typeof(SeparationRecordsController), "Withdraw", Write },
        { typeof(SeparationRecordsController), "Reopen", Write },
        { typeof(SeparationRecordsController), "CompleteManualStep", Write },
        { typeof(SeparationRecordsController), "RequestStepException", Write },
        { typeof(SeparationRecordsController), "ApproveStepException", Approve },
        { typeof(SeparationRecordsController), "IssueDecision", Approve },
        { typeof(SeparationRecordsController), "Execute", Approve },
        { typeof(SeparationRecordsController), "Cancel", Approve },
        { typeof(SeparationWizardController), "Step1", Write },
        { typeof(SeparationWizardController), "Step2Confirm", Write },
        { typeof(SeparationWizardController), "Step3Execute", Approve }
    };

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType) || m.ReturnType == typeof(Task<IActionResult>));

    private static bool IsPost(MethodInfo m) => m.GetCustomAttributes().OfType<HttpPostAttribute>().Any();

    private static string? Policy(MethodInfo m) => m.GetCustomAttributes<AuthorizeAttribute>().SingleOrDefault()?.Policy;

    [Theory]
    [MemberData(nameof(Posts))]
    public void Each_post_names_its_policy_and_validates_the_token(Type controller, string action, string policy)
    {
        var post = Assert.Single(Actions(controller), m => m.Name == action && IsPost(m));
        Assert.Equal(policy, Policy(post));
        Assert.True(post.GetCustomAttributes().OfType<ValidateAntiForgeryTokenAttribute>().Any(), $"{controller.Name}.{action} validates no token.");
    }

    [Theory]
    [InlineData(typeof(SeparationRecordsController))]
    [InlineData(typeof(SeparationWizardController))]
    public void The_map_is_complete_reads_take_the_module_policy_and_the_stance_is_the_controllers(Type controller)
    {
        var pinned = Posts().Select(row => ((Type)row[0], (string)row[1])).Where(row => row.Item1 == controller).Select(row => row.Item2).ToHashSet();
        var posts = Actions(controller).Where(IsPost).Select(m => m.Name).ToHashSet();
        Assert.Equal(pinned.Order(), posts.Order());

        Assert.Equal(HrManagementPolicies.ModuleHrContracts, controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        foreach (var read in Actions(controller).Where(m => !IsPost(m)))
            Assert.Null(Policy(read));

        var stance = controller.GetCustomAttribute<OrgScopedAttribute>();
        Assert.NotNull(stance);
        Assert.Equal(typeof(HR.Contracts.Domain.Entities.SeparationRecord), stance!.EntityType);
        Assert.Equal(OrgScopeAnswer.Forbid, stance.OutOfScope);
        Assert.All(Actions(controller), m => Assert.Null(m.GetCustomAttribute<OrgScopeNotApplicableAttribute>()));
    }

    [Fact]
    public void Publish_delete_override_and_the_wizards_hand_completion_are_gone()
    {
        Assert.DoesNotContain(Actions(typeof(SeparationRecordsController)), m => m.Name is "PublishSeparation" or "Delete" or "OverrideChecklistStep");
        Assert.DoesNotContain(Actions(typeof(SeparationWizardController)), m => m.Name == "Step2CompleteStep");
    }

    private static string Source(params string[] parts) =>
        System.IO.File.ReadAllText(Path.Combine([HRManagement.TestSupport.RepoPaths.Root, "src", "Services", "HRContracts", .. parts]));

    private static IEnumerable<string> HrContractsSources() =>
        Directory.GetFiles(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "src", "Services", "HRContracts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_path_starts_the_saga_but_the_execution()
    {
        var callers = HrContractsSources()
            .Where(f => Regex.IsMatch(System.IO.File.ReadAllText(f), @"\.StartSeparationSagaAsync\("))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["SeparationRecordHandlers.cs"], callers);

        // In that file, the one call sits in ExecuteSeparationHandler.
        var handlers = Source("HR.Contracts.Application", "Features", "SeparationRecords", "SeparationRecordHandlers.cs");
        var calls = Regex.Matches(handlers, @"\.StartSeparationSagaAsync\(");
        Assert.Single(calls);
        var owner = Regex.Matches(handlers[..calls[0].Index], @"public class (\w+)").Last().Groups[1].Value;
        Assert.Equal("ExecuteSeparationHandler", owner);
    }

    [Fact]
    public void No_path_puts_a_record_in_PendingApproval_without_an_engine_instance()
    {
        // Every transition to PendingApproval passes a SeparationSubmission (whose instance Transition requires): the submission only.
        var transitions = HrContractsSources()
            .SelectMany(f => Regex.Matches(System.IO.File.ReadAllText(f), @"Transition(?:Async)?\((?:[^;]*?)SeparationStatus\.PendingApproval[^;]*;", RegexOptions.Singleline)
                .Select(m => (File: Path.GetFileName(f), Call: m.Value)))
            .Where(x => x.File != "SeparationWorkflowRules.cs")
            .ToList();
        Assert.NotEmpty(transitions);
        Assert.All(transitions, x => Assert.Contains("new SeparationSubmission(", x.Call));
        Assert.Equal(["SeparationApprovalHandlers.cs"], transitions.Select(x => x.File).Distinct());
    }
}

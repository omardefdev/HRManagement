using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-25 (G-15): every <c>[HttpPost]</c> action in HR.Core.Web carries an explicit <c>[Authorize(Policy = …)]</c> (on the
/// action or its controller) or <c>[AllowAnonymous]</c>. The global <c>Module.HrCore</c> filter alone is not enough: it lets
/// branch managers and approvers through for writes. This is a ratchet — today's non-recruitment offenders are frozen in
/// <c>tests/architecture/hrcore-post-policy-allowlist.txt</c> with a reason each; a new offender fails, a fixed one must be
/// removed from the list, and no recruitment controller may be listed.
/// </summary>
public sealed class HrCorePostActionsHaveExplicitPolicyTests
{
    private const string AllowListPath = "tests/architecture/hrcore-post-policy-allowlist.txt";

    /// <summary>Recruitment controllers may never be on the allow-list.</summary>
    private static readonly string[] RecruitmentControllerPrefixes =
    [
        "Recruitment", "OfferLetters", "SelectionCommittees", "Competitions", "PublicJobPostings", "TalentMarketplace",
        "CandidatePortal", "PublicCareers", "CandidateLifecycle", "ExamMarking"
    ];

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    internal static IReadOnlyList<string> FindOffenders()
    {
        var assembly = typeof(HR.Core.Web.Controllers.PublicJobPostingsController).Assembly;
        var offenders = new List<string>();
        foreach (var controller in assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var classCovered = HasExplicitPolicy(controller);
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpPostAttribute>(inherit: true).Any())
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (classCovered || HasExplicitPolicy(action))
                    continue;
                var key = $"{controller.Name.Replace("Controller", string.Empty, StringComparison.Ordinal)}.{action.Name}";
                if (!offenders.Contains(key))
                    offenders.Add(key);
            }
        }

        return offenders;
    }

    private static bool HasExplicitPolicy(MemberInfo member) =>
        member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
        || member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(a => !string.IsNullOrWhiteSpace(a.Policy));

    private static Dictionary<string, string> LoadAllowList()
    {
        var path = Path.Combine(RepoRoot, AllowListPath);
        Assert.True(global::System.IO.File.Exists(path), $"Missing {AllowListPath}.");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in global::System.IO.File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            Assert.True(parts.Length == 2 && parts[1].Length > 0, $"Allow-list entry without a reason: «{line}».");
            entries[parts[0]] = parts[1];
        }

        return entries;
    }

    [Fact]
    public void Every_post_action_has_an_explicit_policy_or_is_frozen_in_the_allow_list()
    {
        var allowList = LoadAllowList();
        var unlisted = FindOffenders().Where(o => !allowList.ContainsKey(o)).ToList();

        Assert.True(unlisted.Count == 0,
            "HR.Core.Web [HttpPost] actions without [Authorize(Policy=…)] or [AllowAnonymous]: " + string.Join(", ", unlisted));
    }

    [Fact]
    public void The_allow_list_only_shrinks()
    {
        var offenders = FindOffenders().ToHashSet(StringComparer.Ordinal);
        var stale = LoadAllowList().Keys.Where(k => !offenders.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            $"These actions are protected now — remove them from {AllowListPath}: " + string.Join(", ", stale));
    }

    [Fact]
    public void No_recruitment_controller_is_on_the_allow_list()
    {
        var recruitment = LoadAllowList().Keys
            .Where(k => RecruitmentControllerPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(recruitment.Count == 0, "Recruitment actions must carry an explicit policy: " + string.Join(", ", recruitment));
    }
}

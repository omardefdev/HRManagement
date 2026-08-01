using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

/// <summary>
/// Controllers that publish integration events must flush the outbox in the same action (INT-001).
/// </summary>
public class OutboxPublishArchitectureTests
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

    private static readonly Regex PublishCall = new(
        @"\b(?:await\s+)?(?:_?publish|publish)\.(?:Publish(?:<[^>]+>)?\()",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void EssPortalController_flushes_outbox_after_publish()
    {
        var path = Path.Combine(RepoRoot, "src", "Services", "HRCore", "HR.Core.Web", "Controllers", "EssPortalController.cs");
        var text = global::System.IO.File.ReadAllText(path);
        Assert.Contains("SaveChangesAsync", text, StringComparison.Ordinal);
        Assert.True(PublishCall.IsMatch(text), "EssPortalController should publish integration events.");
    }

    [Fact]
    public void Controllers_with_IPublishEndpoint_must_not_grow_without_review()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EssPortalController.cs",
            "EssLearningController.cs",
            "MedicalRecommendationsController.cs",
            "MedicalCommitteeReportsController.cs",
            "HealthExtendedControllers.cs",
            "ComplianceExtendedControllers.cs",
            "ComplaintsController.cs",
            "TransferRequestsController.cs",
            "MovementsExtendedControllers.cs",
            "ProcessInterventionsController.cs",
            "AdministrativeDecisionsController.cs",
            "IssuedCardsController.cs",
            "TrainingExtendedControllers.cs",
            "TrainingEnrollmentsController.cs"
        };

        var controllers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*Controller*.cs", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}Web{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => global::System.IO.File.ReadAllText(p).Contains("IPublishEndpoint", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .ToList();

        var unexpected = controllers.Where(c => !allowed.Contains(c)).ToList();
        Assert.True(
            unexpected.Count == 0,
            "New controllers must not inject IPublishEndpoint directly; use IEventPublisher + outbox. Unexpected:\n"
            + string.Join("\n", unexpected));
    }
}

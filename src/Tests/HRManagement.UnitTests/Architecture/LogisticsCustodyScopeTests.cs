using System.Reflection;
using System.Text.RegularExpressions;

namespace HRManagement.UnitTests.Architecture;

/// <summary>AC-32: logistics custody POST actions must invoke org-scope guards.</summary>
public class LogisticsCustodyScopeTests
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

    private static readonly string AssetsControllerPath = Path.Combine(
        RepoRoot,
        "src",
        "Services",
        "AdminLogistics",
        "Admin.Logistics.Web",
        "Controllers",
        "AssetsController.cs");

    private static readonly (string Action, string[] RequiredMarkers)[] CustodyPostActions =
    [
        ("AssignToEmployee", ["ForbidIfAssetOutOfScopeAsync", "ForbidIfEmployeeOutOfScopeForCustodyAsync"]),
        ("ReturnAssignment", ["ForbidIfAssetOutOfScopeAsync"]),
        ("MarkLost", ["ForbidIfAssetOutOfScopeAsync"]),
        ("RecoverAsset", ["ForbidIfAssetOutOfScopeAsync"]),
        ("CancelAsset", ["ForbidIfAssetOutOfScopeAsync"]),
    ];

    private static readonly string[] GetGuardMarkers =
    [
        "ForbidIfAssetOutOfScopeAsync",
        "ForbidIfOrgScopeDenied",
        "GetAllowedOrgUnitIdsAsync",
        "WhereInOrgScope",
        "AllowedOrgUnitIds.Contains"
    ];

    [Fact]
    public void AssetsController_custody_posts_invoke_org_scope_guard()
    {
        var text = global::System.IO.File.ReadAllText(AssetsControllerPath);
        var violations = new List<string>();

        foreach (var (action, markers) in CustodyPostActions)
        {
            var match = Regex.Match(
                text,
                $@"public\s+async\s+Task<IActionResult>\s+{action}\s*\(",
                RegexOptions.Singleline);
            Assert.True(match.Success, $"Action {action} not found in AssetsController");

            var start = match.Index;
            var end = Math.Min(text.Length, start + 2500);
            var body = text[start..end];
            if (!markers.All(m => body.Contains(m, StringComparison.Ordinal)))
                violations.Add($"{action}: missing guard marker(s) [{string.Join(", ", markers)}]");
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void AssetsController_sensitive_gets_invoke_asset_org_scope_guard()
    {
        var text = global::System.IO.File.ReadAllText(AssetsControllerPath);
        foreach (var action in new[] { "Details", "Edit", "HandoverReceiptPdf" })
        {
            var match = Regex.Match(
                text,
                $@"public\s+async\s+Task<IActionResult>\s+{action}\s*\(",
                RegexOptions.Singleline);
            Assert.True(match.Success, $"Action {action} not found");

            var body = text[match.Index..Math.Min(text.Length, match.Index + 1200)];
            Assert.Contains("ForbidIfAssetOutOfScopeAsync", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AssetsController_Index_applies_org_scope_filter()
    {
        var text = global::System.IO.File.ReadAllText(AssetsControllerPath);
        var indexMatch = Regex.Match(text, @"public\s+async\s+Task<IActionResult>\s+Index\s*\(");
        Assert.True(indexMatch.Success);
        var body = text[indexMatch.Index..Math.Min(text.Length, indexMatch.Index + 4000)];
        Assert.True(GetGuardMarkers.Any(m => body.Contains(m, StringComparison.Ordinal)),
            "Index must filter by org scope when MustFilter");
    }

    [Fact]
    public void AssetAssignmentsController_Index_and_Details_apply_org_scope()
    {
        var path = Path.Combine(
            RepoRoot,
            "src",
            "Services",
            "AdminLogistics",
            "Admin.Logistics.Web",
            "Controllers",
            "AdminExtendedControllers.cs");
        var text = global::System.IO.File.ReadAllText(path);
        var classStart = text.IndexOf("public class AssetAssignmentsController", StringComparison.Ordinal);
        Assert.True(classStart >= 0);
        var segment = text[classStart..Math.Min(text.Length, classStart + 6000)];
        Assert.Contains("GetAllowedOrgUnitIdsAsync", segment, StringComparison.Ordinal);
        Assert.Contains("ForbidIfEmployeeOutOfScopeForCustodyAsync", segment, StringComparison.Ordinal);
    }
}

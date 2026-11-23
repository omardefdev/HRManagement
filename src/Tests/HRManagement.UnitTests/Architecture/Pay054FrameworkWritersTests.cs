using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-54 (§1 rule 1, P-7): a payroll framework version comes into being in the places named here and nowhere else, and only the
/// creation paths make one of fingerprint v2. A version created anywhere else would be a legacy version by default (its runs would
/// read the general tables), silently. The product source is read as text: a new <c>new PayrollConfiguration</c> or a new assignment of
/// <c>HashSchemaVersion</c> fails the test until it is named here with its reason.
/// </summary>
public sealed partial class Pay054FrameworkWritersTests
{
    /// <summary>The files that construct a framework version, each with why.</summary>
    public static readonly IReadOnlyDictionary<string, string> Constructors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Payroll.Domain/Services/PayrollRegimeSeed.cs"] = "the Demo and Acceptance seeds and a regime's template: legacy (v1) versions, as before PLAN-54",
        ["Payroll.Domain/Services/PayrollConfigurationVersioning.cs"] = "CloneAsDraft: always a v2 draft",
        ["Payroll.Domain/Services/PayrollEngine.cs"] = "BuildSalaryLines: an in-memory helper of the old tests, never saved",
        ["Payroll.Application/Features/PayrollConfigurations/PayrollFrameworkCommands.cs"] = "the importer: always a v2 draft",
    };

    /// <summary>The files that set a version's fingerprint schema, each with why.</summary>
    public static readonly IReadOnlyDictionary<string, string> SchemaSetters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Payroll.Domain/Entities/PayrollConfiguration.cs"] = "the property's default: legacy, until a creation path says v2",
        ["Payroll.Domain/Services/PayrollConfigurationVersioning.cs"] = "CloneAsDraft sets v2",
        ["Payroll.Application/Features/PayrollConfigurations/PayrollFrameworkCommands.cs"] = "the importer sets v2 (a new draft, and a draft whose content it replaces)",
    };

    [GeneratedRegex(@"new\s+PayrollConfiguration\s*(\(|\{|$)", RegexOptions.Multiline)]
    private static partial Regex Construction();

    [GeneratedRegex(@"\bHashSchemaVersion\s*(=(?!=)|\{\s*get;\s*set;\s*\}\s*=)")]
    private static partial Regex SchemaAssignment();

    private static IEnumerable<(string Relative, string Text)> ProductSources()
    {
        var root = Path.Combine(RepoPaths.Root, "src", "Services");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal))
                continue;
            yield return (relative[(relative.IndexOf('/') + 1)..], System.IO.File.ReadAllText(file));
        }
    }

    [Fact]
    public void A_framework_version_is_constructed_only_where_the_plan_names()
    {
        var found = ProductSources().Where(s => Construction().IsMatch(s.Text)).Select(s => s.Relative).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Constructors.Keys.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void A_versions_fingerprint_schema_is_set_only_where_the_plan_names()
    {
        var found = ProductSources().Where(s => SchemaAssignment().IsMatch(s.Text)).Select(s => s.Relative).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(SchemaSetters.Keys.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void The_scan_sees_a_planted_construction_and_a_planted_assignment()
    {
        Assert.Matches(Construction(), "var c = new PayrollConfiguration { Code = \"X\" };");
        Assert.Matches(Construction(), "return new PayrollConfiguration();");
        Assert.DoesNotMatch(Construction(), "new PayrollConfigurationEditor { }");
        Assert.DoesNotMatch(Construction(), "new PayrollConfigurationFieldSource()");
        Assert.Matches(SchemaAssignment(), "draft.HashSchemaVersion = 2;");
        Assert.Matches(SchemaAssignment(), "HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion,");
        Assert.DoesNotMatch(SchemaAssignment(), "if (c.HashSchemaVersion == 2) return;");
        Assert.DoesNotMatch(SchemaAssignment(), "HashSchemaVersion < CurrentHashSchemaVersion");
    }
}

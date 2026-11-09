using System.Reflection;
using System.Text.RegularExpressions;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§1, rules 2 and 3): one list of national shared tables, and one read path to them. <c>NationalTenant.TableNames</c>,
/// the tables of the entities marked <see cref="INationalShared"/> in the HR.Core model, and the tables of
/// <c>scripts/sql/ten048-national-shared-read.sql</c> are the same list; no production code reads a national table through a
/// general tenant-filter bypass.
/// </summary>
public class NationalSharedArchitectureTests
{
    private static readonly string[] Expected =
    [
        "CodePolicyRules", "CodePolicyTemplates", "CodePolicyVersions", "PolicyFactsSchemas", "PolicyRules", "PolicyTemplateVersions",
        "PolicyTemplates", "PolicyWorkflowBindings", "ReferenceCatalogEntries", "WorkflowDefinitions"
    ];

    private static string Root => HRManagement.TestSupport.RepoPaths.Root;

    private static HRCoreDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>().UseNpgsql("Host=model-only;Database=model").Options);

    private static IReadOnlyList<string> Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public void The_national_list_the_EF_model_and_the_SQL_script_are_one_list()
    {
        Assert.Equal(Expected, Sorted(NationalTenant.TableNames));

        using var db = ModelOnlyContext();
        var modelTables = db.Model.GetEntityTypes()
            .Where(e => typeof(INationalShared).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName()!)
            .ToList();
        Assert.Equal(Expected, Sorted(modelTables));

        var script = global::System.IO.File.ReadAllText(Path.Combine(Root, "scripts", "sql", "ten048-national-shared-read.sql"));
        var arrays = Regex.Matches(script, @"national_tables\s+text\[\]\s*:=\s*ARRAY\[(?<list>[^\]]*)\]");
        Assert.Equal(2, arrays.Count);
        foreach (Match array in arrays)
        {
            var names = Regex.Matches(array.Groups["list"].Value, "'([^']+)'").Select(m => m.Groups[1].Value);
            Assert.Equal(Expected, Sorted(names));
        }

        Assert.Contains($"'{NationalTenant.Id}'", script);
        Assert.Equal(TenantRlsMigrationHelper.DefaultTenantId, NationalTenant.Id.ToString());
        foreach (var table in Expected)
        {
            Assert.Contains($"'{table}'", TenantRlsMigrationHelper.NationalSharedReadSql);
            Assert.Contains($"'{table}'", TenantRlsMigrationHelper.VerifyNationalSharedReadSql);
        }
    }

    [Fact]
    public void Only_the_ten_entities_carry_the_marker_and_none_holds_personal_data()
    {
        var marked = new[] { typeof(HRCoreDbContext).Assembly, typeof(HR.Core.Domain.Entities.Policy.PolicyTemplate).Assembly, typeof(INationalShared).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(INationalShared).IsAssignableFrom(t))
            .Select(t => t.Name)
            .Distinct();
        Assert.Equal(
            Sorted(["CodePolicyRule", "CodePolicyTemplate", "CodePolicyVersion", "PolicyFactsSchemaEntity", "PolicyRule", "PolicyTemplate",
                "PolicyTemplateVersion", "PolicyWorkflowBinding", "ReferenceCatalogEntry", "WorkflowDefinition"]),
            Sorted(marked));

        // D-TEN-2: no entity whose name says it holds people's data may join the list.
        Assert.DoesNotContain(marked, n => Regex.IsMatch(n, "Employee|Person|Candidate|Citizen|Applicant|Contact|Pii", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void The_read_policy_is_select_only_and_the_hardening_never_drops_it()
    {
        var sql = TenantRlsMigrationHelper.NationalSharedReadSql;
        Assert.Contains("AS PERMISSIVE FOR SELECT", sql);
        Assert.DoesNotContain("WITH CHECK", sql);
        Assert.DoesNotContain("tenant_isolation", sql);
        Assert.DoesNotContain(TenantRlsMigrationHelper.NationalSharedReadPolicyName, TenantRlsMigrationHelper.HardenAllTenantIdTablesSql);

        var script = global::System.IO.File.ReadAllText(Path.Combine(Root, "scripts", "sql", "ten048-national-shared-read.sql"));
        Assert.Contains("AS PERMISSIVE FOR SELECT", script);
        Assert.DoesNotContain("tenant_isolation", Regex.Replace(script, "^--.*$", string.Empty, RegexOptions.Multiline));
        var hardening = global::System.IO.File.ReadAllText(Path.Combine(Root, "scripts", "sql", "ten002c-harden-tenant-rls.sql"));
        Assert.DoesNotContain("national_shared_read", hardening);
    }

    [Fact]
    public void Both_migration_paths_and_the_bootstrap_apply_the_read_policy_to_hr_core()
    {
        var runMigrations = global::System.IO.File.ReadAllText(Path.Combine(Root, "scripts", "run-migrations.sh"));
        Assert.Contains("ten048-national-shared-read.sql", runMigrations);
        Assert.Matches(new Regex("""if \[ "\$\{name\}" = "hr-core" \]; then\s+national_shared_read"""), runMigrations);

        var bootstrap = global::System.IO.File.ReadAllText(Path.Combine(Root, "deploy", "bootstrap", "bootstrap.sh"));
        Assert.Contains("ten048-national-shared-read.sql", bootstrap);
        Assert.Matches(new Regex("""if \[ "\$bundle" = "hr-core" \]; then\s+PGPASSWORD="\$\(secret "\$\{prefix\}_MIGRATOR_PASSWORD"\)" psql [^\n]*-f "\$NATIONAL_READ_SQL"""), bootstrap);

        using var db = ModelOnlyContext();
        Assert.True(DatabaseMigrationExtensions.HasNationalSharedEntities(db));
    }

    [Fact]
    public void The_read_path_methods_take_national_shared_entities_only()
    {
        var methods = typeof(NationalScopeQueryExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(5, methods.Length);
        foreach (var method in methods)
        {
            var constraints = method.GetGenericArguments().Single().GetGenericParameterConstraints();
            Assert.Contains(typeof(INationalShared), constraints);
        }
    }

    [Fact]
    public void No_production_code_reads_a_national_table_through_a_general_bypass()
    {
        var nationalSets = new[]
        {
            "PolicyTemplates", "PolicyTemplateVersions", "PolicyRules", "PolicyFactsSchemas", "PolicyWorkflowBindings", "WorkflowDefinitions",
            "CodePolicyTemplates", "CodePolicyVersions", "CodePolicyRules", "ReferenceCatalogEntries"
        };
        var nationalTypes = new[]
        {
            "PolicyTemplate", "PolicyTemplateVersion", "PolicyRule", "PolicyFactsSchemaEntity", "PolicyWorkflowBinding", "WorkflowDefinition",
            "CodePolicyTemplate", "CodePolicyVersion", "CodePolicyRule", "ReferenceCatalogEntry"
        };
        var nationalTable = new Regex(
            @"\.(" + string.Join("|", nationalSets) + @")\b|Set<(" + string.Join("|", nationalTypes) + @")>",
            RegexOptions.CultureInvariant);
        var bypassCall = new Regex(@"\.IgnoreTenantFilter(ForSystem|ForPlatformReconciliation)?\s*[<(]", RegexOptions.CultureInvariant);

        var violations = new List<string>();
        foreach (var file in ProductionSources())
        {
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            var text = global::System.IO.File.ReadAllText(file);

            if (Regex.IsMatch(text, @"SystemTenantBypassPurpose\.(PolicyResolution|NationalReferenceLookup)\b"))
                violations.Add($"{relative}: uses a retired national read purpose (PLAN-48: OwnOrNational or NationalOnly)");
            if (!relative.EndsWith("NationalScopeQueryExtensions.cs", StringComparison.Ordinal)
                && Regex.IsMatch(text, @"SystemTenantBypassPurpose\.NationalSharedRead\b"))
                violations.Add($"{relative}: NationalSharedRead is used outside NationalScopeQueryExtensions");

            if (relative.EndsWith("TenantFilterBypass.cs", StringComparison.Ordinal) || relative.EndsWith("NationalScopeQueryExtensions.cs", StringComparison.Ordinal))
                continue;

            foreach (Match call in bypassCall.Matches(text))
            {
                // The statement that holds the call: from the previous ';', '{' or '}' to the call.
                var start = text.LastIndexOfAny([';', '{', '}'], call.Index) + 1;
                var statement = text[start..call.Index];
                if (nationalTable.IsMatch(statement))
                    violations.Add($"{relative}:{LineOf(text, call.Index)}: a general bypass on a national table");
            }
        }

        Assert.True(violations.Count == 0, "PLAN-48 one national read path:\n" + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("var x = db.WorkflowDefinitions\n    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)", true)]
    [InlineData("context.Set<PolicyTemplateVersion>().IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)", true)]
    [InlineData("db.ReferenceCatalogEntries.AsNoTracking().IgnoreTenantFilter(tenantContext)", true)]
    [InlineData("db.Employees.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)", false)]
    public void The_scan_finds_a_bypass_on_a_national_table(string code, bool national)
    {
        var nationalTable = new Regex(@"\.(WorkflowDefinitions|ReferenceCatalogEntries)\b|Set<(PolicyTemplateVersion)>");
        var call = new Regex(@"\.IgnoreTenantFilter(ForSystem|ForPlatformReconciliation)?\s*[<(]").Match(code);
        Assert.True(call.Success);
        Assert.Equal(national, nationalTable.IsMatch(code[..call.Index]));
    }

    private static IEnumerable<string> ProductionSources()
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}Tests{sep}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase) && !f.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{sep}Migrations{sep}", StringComparison.OrdinalIgnoreCase));
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;
}

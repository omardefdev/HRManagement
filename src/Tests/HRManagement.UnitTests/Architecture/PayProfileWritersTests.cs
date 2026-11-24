using HRManagement.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-53 (§2 rule 1, P-5): nothing in Payroll.Web, Payroll.Application or Payroll.Infrastructure adds, changes or removes a pay profile
/// except the applier and the writers named here, each with the plan that owns it. A new writer fails the test; a named writer that no
/// longer writes fails it too (its name leaves when its consumer moves to <c>ApplySystemChangeAsync</c>). The source is read with
/// Roslyn: a class writes when it creates an <c>EmployeePayProfile</c>, assigns a field of a variable that holds one, removes one, or
/// calls the versioning rules.
/// <para>
/// PLAN-55 (§3, rule 1): a new pay profile is made in one place, <c>EmployeePayProfileVersioning.CreateNextVersion</c>. No
/// <c>new EmployeePayProfile</c> is left in the three projects outside the seed, the contract activation went through the applier and
/// left the list, and the list is one shorter.
/// </para>
/// </summary>
public sealed class PayProfileWritersTests
{
    /// <summary>The writers of pay profiles, each with its owner. The only way a salary or a grade changes by request is the first.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedWriters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PayProfileChangeApplier"] = "PLAN-53: the only creator of a pay profile version for a change (an approved request, an automatic source; since PLAN-55 the activation of a contract too)",
        ["HandleEmployeeCreatedHandler"] = "PLAN-55: a new employee's first, inactive, unpriced profile through CreateNextVersion; and a late event fills the empty unit and regime of the employee's current profile, never overwriting one",
        ["HandleTransferExecutedHandler"] = "PAY-G (wave 2): the transfer writes the unit, the regime and the salary difference in place",
        ["HandleSeparationCompletedHandler"] = "PLAN-64: the separation closes the profile at the end of service (it changes no salary)",
        ["HandleDisciplinaryActionHandler"] = "PLAN-64: a dismissal closes the profile (it changes no salary)",
        ["DemoPayrollSeeder"] = "seed: fills an empty database with profiles; it never changes one (the acceptance seeder calls it)",
    };

    private static readonly string[] Projects = ["Payroll.Web", "Payroll.Application", "Payroll.Infrastructure"];

    private static readonly HashSet<string> ProfileFields = new(StringComparer.Ordinal)
    {
        "EmployeeId", "PayGradeId", "BaseSalary", "EffectiveFrom", "EffectiveTo", "IsActive", "OrgUnitId", "BankAccountIban", "BankName",
        "RegimeCode", "BankAccountVerifiedAt", "PayGrade",
    };

    /// <summary>What makes a variable a pay profile, read from its declaration (no semantic model: the names are the repository's own).</summary>
    private static readonly string[] ProfileSources =
    [
        "GetActiveProfileAsync(", "EmployeePayProfiles", "new EmployeePayProfile", "EmployeePayProfileVersioning.NextVersion(",
        "EmployeePayProfileVersioning.PreviousOf(",
    ];

    public sealed record Finding(string Class, string File, int Line, string What);

    public static IReadOnlyList<Finding> Scan(string file, string source)
    {
        var findings = new List<Finding>();
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            // The members of this type only: a nested type is scanned on its own.
            foreach (var member in type.Members.Where(m => m is not TypeDeclarationSyntax))
            {
                var profiles = ProfileVariables(member);
                foreach (var node in member.DescendantNodes())
                {
                    var what = node switch
                    {
                        ObjectCreationExpressionSyntax creation when IsProfileType(creation.Type) => "creates a pay profile",
                        AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax left }
                            when ProfileFields.Contains(left.Name.Identifier.Text) && RootName(left.Expression) is { } receiver
                                 && profiles.Contains(receiver) => $"assigns {left.Name.Identifier.Text} of a pay profile",
                        InvocationExpressionSyntax invocation when IsVersioningCall(invocation) => "calls the versioning rules",
                        InvocationExpressionSyntax invocation when IsRemovalOrBulkWrite(invocation, profiles) => "removes or bulk-writes pay profiles",
                        _ => null,
                    };
                    if (what is not null)
                        findings.Add(new Finding(type.Identifier.Text, file, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, what));
                }
            }
        }

        return findings;
    }

    private static bool IsProfileType(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax name => name.Identifier.Text == "EmployeePayProfile",
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text == "EmployeePayProfile",
        NullableTypeSyntax nullable => IsProfileType(nullable.ElementType),
        _ => false,
    };

    private static string? RootName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax name => name.Identifier.Text,
        PostfixUnaryExpressionSyntax postfix => RootName(postfix.Operand),         // profile!.X
        ParenthesizedExpressionSyntax parenthesized => RootName(parenthesized.Expression),
        _ => null,
    };

    private static HashSet<string> ProfileVariables(SyntaxNode member)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in member.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null && IsProfileType(parameter.Type))
                names.Add(parameter.Identifier.Text);
        }

        foreach (var declaration in member.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            foreach (var variable in declaration.Variables)
            {
                var initializer = variable.Initializer?.Value.ToString() ?? string.Empty;
                // A projection or a no-tracking read is not a tracked profile: assigning to its result would not compile or not save.
                var reads = initializer.Contains(".Select(", StringComparison.Ordinal) || initializer.Contains("CountAsync", StringComparison.Ordinal)
                            || initializer.Contains("AnyAsync", StringComparison.Ordinal);
                if (IsProfileType(declaration.Type) || (!reads && ProfileSources.Any(s => initializer.Contains(s, StringComparison.Ordinal))))
                    names.Add(variable.Identifier.Text);
            }
        }

        foreach (var loop in member.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (IsProfileType(loop.Type) || (RootName(loop.Expression) is { } collection && names.Contains(collection)))
                names.Add(loop.Identifier.Text);
        }

        return names;
    }

    private static bool IsVersioningCall(InvocationExpressionSyntax invocation)
    {
        var text = invocation.Expression.ToString();
        return text.EndsWith("EmployeePayProfileVersioning.Close", StringComparison.Ordinal)
               || text.EndsWith("EmployeePayProfileVersioning.NextVersion", StringComparison.Ordinal)
               || text.EndsWith("EmployeePayProfileVersioning.CreateNextVersion", StringComparison.Ordinal);
    }

    private static bool IsRemovalOrBulkWrite(InvocationExpressionSyntax invocation, HashSet<string> profiles)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return false;
        var method = access.Name.Identifier.Text;
        var target = access.Expression.ToString();
        if (target.Contains("EmployeePayProfiles", StringComparison.Ordinal)
            && method is "Add" or "AddAsync" or "AddRange" or "AddRangeAsync" or "Remove" or "RemoveRange" or "Update" or "UpdateRange" or "Attach"
                or "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync")
            return true;
        return method is "Remove" or "RemoveRange"
               && invocation.ArgumentList.Arguments.Any(a => RootName(a.Expression) is { } name && profiles.Contains(name));
    }

    private static IReadOnlyList<Finding> ScanRepository()
    {
        var findings = new List<Finding>();
        foreach (var project in Projects)
        {
            var directory = Path.Combine(RepoPaths.Root, "src", "Services", "Payroll", project);
            foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/Migrations/", StringComparison.Ordinal))
                    continue;
                var source = System.IO.File.ReadAllText(path);
                if (!source.Contains("EmployeePayProfile", StringComparison.Ordinal) && !source.Contains("GetActiveProfileAsync", StringComparison.Ordinal))
                    continue;
                findings.AddRange(Scan(relative, source));
            }
        }

        return findings;
    }

    [Fact]
    public void Only_the_applier_and_the_named_writers_write_a_pay_profile()
    {
        var findings = ScanRepository();
        var unknown = findings.Where(f => !AllowedWriters.ContainsKey(f.Class))
            .Select(f => $"{f.Class} ({f.File}:{f.Line}) {f.What}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0,
            "A pay profile is written outside the applier and the named writers. A salary or a grade changes through a request another person "
            + "approves (IPayProfileChangeApplier), or through ApplySystemChangeAsync for an automatic source:\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void Every_named_writer_still_writes()
    {
        var writers = ScanRepository().Select(f => f.Class).ToHashSet(StringComparer.Ordinal);
        var stale = AllowedWriters.Keys.Where(k => !writers.Contains(k)).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0,
            "These classes no longer write a pay profile: remove them from PayProfileWritersTests.AllowedWriters: " + string.Join(", ", stale));
        Assert.True(AllowedWriters.Count <= 6, "The named writers never grow: a new writer goes through the applier");
        // PLAN-55: the contract activation makes its version through the applier and is no writer any more.
        Assert.DoesNotContain("HandleContractActivatedHandler", AllowedWriters.Keys);
        Assert.All(AllowedWriters.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void The_applier_and_the_new_employees_first_profile_are_the_only_callers_of_the_versioning_rules()
    {
        var callers = ScanRepository().Where(f => f.What == "calls the versioning rules").Select(f => f.Class).Distinct()
            .Order(StringComparer.Ordinal).ToList();
        // The applier for every change; HandleEmployeeCreatedHandler for the first, inactive, unpriced profile (PLAN-55).
        Assert.Equal(["HandleEmployeeCreatedHandler", "PayProfileChangeApplier"], callers);
    }

    /// <summary>The seeds: they fill an empty database, they never change a profile.</summary>
    private static readonly string[] Seeds = ["DemoPayrollSeeder"];

    [Fact]
    public void No_pay_profile_is_created_outside_CreateNextVersion_and_the_seeds()
    {
        // PLAN-55 (§3, rule 1). In Payroll.Web, Payroll.Application and Payroll.Infrastructure: no `new EmployeePayProfile` but the seed's.
        var creators = ScanRepository().Where(f => f.What == "creates a pay profile")
            .Select(f => $"{f.Class} ({f.File}:{f.Line})").Distinct(StringComparer.Ordinal).ToList();
        var outside = creators.Where(c => !Seeds.Any(seed => c.StartsWith(seed + " ", StringComparison.Ordinal))).ToList();
        Assert.True(outside.Count == 0,
            "A pay profile is created outside EmployeePayProfileVersioning.CreateNextVersion and the seeds:\n" + string.Join("\n", outside));
        Assert.Contains(creators, c => c.StartsWith("DemoPayrollSeeder ", StringComparison.Ordinal));

        // In Payroll.Domain, the only file that creates one is the versioning helper itself.
        var domain = Path.Combine(RepoPaths.Root, "src", "Services", "Payroll", "Payroll.Domain");
        var domainCreators = Directory.EnumerateFiles(domain, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Scan(f, System.IO.File.ReadAllText(f)).Any(x => x.What == "creates a pay profile"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["EmployeePayProfileVersioning.cs"], domainCreators);
    }

    [Fact]
    public void The_direct_write_command_is_gone()
    {
        var sources = Projects.SelectMany(p => Directory.EnumerateFiles(
                Path.Combine(RepoPaths.Root, "src", "Services", "Payroll", p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var mentions = sources.Where(f => System.IO.File.ReadAllText(f).Contains("UpsertEmployeePayProfileCommand", StringComparison.Ordinal)
                                          || System.IO.File.ReadAllText(f).Contains("UpsertEmployeePayProfileHandler", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.Root, f)).ToList();
        Assert.True(mentions.Count == 0, "UpsertEmployeePayProfileCommand was deleted by PLAN-53 and is back in: " + string.Join(", ", mentions));
    }

    // --- the scanner's own negative tests ---

    [Theory]
    [InlineData("var p = await repository.GetActiveProfileAsync(id, ct); p.BaseSalary = 1;", "assigns BaseSalary of a pay profile")]
    [InlineData("var p = await repository.GetActiveProfileAsync(id, ct); p!.IsActive = false;", "assigns IsActive of a pay profile")]
    [InlineData("var p = await db.EmployeePayProfiles.FirstAsync(x => x.Id == id); p.PayGradeId = null;", "assigns PayGradeId of a pay profile")]
    [InlineData("EmployeePayProfile p = Load(); p.BaseSalary += 5;", "assigns BaseSalary of a pay profile")]
    [InlineData("await repository.AddAsync(new EmployeePayProfile { EmployeeId = id }, ct);", "creates a pay profile")]
    [InlineData("db.EmployeePayProfiles.Remove(x);", "removes or bulk-writes pay profiles")]
    [InlineData("var p = await repository.GetActiveProfileAsync(id, ct); repository.Remove(p);", "removes or bulk-writes pay profiles")]
    [InlineData("await db.EmployeePayProfiles.Where(x => x.IsActive).ExecuteUpdateAsync(s => s.SetProperty(x => x.BaseSalary, 0m));", "removes or bulk-writes pay profiles")]
    [InlineData("foreach (EmployeePayProfile p in list) { p.OrgUnitId = unit; }", "assigns OrgUnitId of a pay profile")]
    [InlineData("EmployeePayProfileVersioning.Close(previous, date);", "calls the versioning rules")]
    [InlineData("var step = EmployeePayProfileVersioning.CreateNextVersion(id, profiles, change);", "calls the versioning rules")]
    [InlineData("var current = EmployeePayProfileVersioning.PreviousOf(profiles); current.RegimeCode = code;", "assigns RegimeCode of a pay profile")]
    public void A_planted_write_is_found(string body, string what)
    {
        var findings = Scan("Planted.cs", $"class PlantedWriter {{ async Task Run() {{ {body} }} }}");
        Assert.Contains(findings, f => f.Class == "PlantedWriter" && f.What == what);
    }

    [Theory]
    [InlineData("var p = await repository.GetActiveProfileAsync(id, ct); var salary = p.BaseSalary; other.BaseSalary = salary;")]
    [InlineData("var unit = await db.EmployeePayProfiles.Where(x => x.EmployeeId == id).Select(x => x.OrgUnitId).FirstOrDefaultAsync(); line.OrgUnitId = unit;")]
    [InlineData("var count = await db.EmployeePayProfiles.CountAsync(x => x.IsActive); model.IsActive = count > 0;")]
    [InlineData("await repository.AddAsync(new EmployeePayProfileHistory { BaseSalary = 1 }, ct);")]
    [InlineData("var request = new PayProfileChangeRequest { EmployeeId = id, EffectiveFrom = date };")]
    public void A_read_is_not_a_write(string body)
    {
        Assert.Empty(Scan("Planted.cs", $"class PlantedReader {{ async Task Run() {{ {body} }} }}"));
    }
}

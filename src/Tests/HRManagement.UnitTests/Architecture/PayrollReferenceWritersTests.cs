using HRManagement.TestSupport;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-53 (§4, D-53-4): nothing in Payroll.Web, Payroll.Application or Payroll.Infrastructure adds, changes or removes a salary scale
/// or a grade except the applier of an approved reference change request and the seed. The controllers used to write them in place;
/// a new writer fails the test, and a named writer that no longer writes fails it too.
/// </summary>
public sealed class PayrollReferenceWritersTests
{
    public static readonly IReadOnlyDictionary<string, string> AllowedWriters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PayrollReferenceChangeApplier"] = "PLAN-53: applies an approved request (or, in Development with the approval switched off, a submitted one)",
        ["PayGradeChange"] = "PLAN-53: the allowlisted fields of a grade a request proposes, written onto the grade by the applier",
        ["SalaryScaleChange"] = "PLAN-53: the allowlisted fields of a scale a request proposes, written onto the scale by the applier",
        ["DemoPayrollSeeder"] = "seed: fills an empty database with a scale and its grades; it never changes one",
    };

    private static readonly string[] Projects = ["Payroll.Web", "Payroll.Application", "Payroll.Infrastructure"];
    private static readonly string[] Types = ["PayGrade", "SalaryScale"];

    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "SalaryScaleId", "GradeCode", "GradeNameAr", "BaseSalary", "MinSalary", "MaxSalary", "Code", "NameAr", "EffectiveFrom", "EffectiveTo", "IsActive",
    };

    private static readonly string[] Sources = ["PayGrades", "SalaryScales", "new PayGrade", "new SalaryScale"];

    public sealed record Finding(string Class, string File, int Line, string What);

    public static IReadOnlyList<Finding> Scan(string file, string source)
    {
        var findings = new List<Finding>();
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            foreach (var member in type.Members.Where(m => m is not TypeDeclarationSyntax))
            {
                var records = Variables(member);
                foreach (var node in member.DescendantNodes())
                {
                    var what = node switch
                    {
                        ObjectCreationExpressionSyntax creation when IsReferenceType(creation.Type) => "creates a scale or a grade",
                        AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax left }
                            when Fields.Contains(left.Name.Identifier.Text) && left.Expression is IdentifierNameSyntax receiver
                                 && records.Contains(receiver.Identifier.Text) => $"assigns {left.Name.Identifier.Text} of a scale or a grade",
                        InvocationExpressionSyntax invocation when IsRemovalOrBulkWrite(invocation, records) => "removes or bulk-writes scales or grades",
                        _ => null,
                    };
                    if (what is not null)
                        findings.Add(new Finding(type.Identifier.Text, file, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, what));
                }
            }
        }

        return findings;
    }

    private static bool IsReferenceType(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax name => Types.Contains(name.Identifier.Text),
        QualifiedNameSyntax qualified => Types.Contains(qualified.Right.Identifier.Text),
        NullableTypeSyntax nullable => IsReferenceType(nullable.ElementType),
        _ => false,
    };

    private static HashSet<string> Variables(Microsoft.CodeAnalysis.SyntaxNode member)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in member.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null && IsReferenceType(parameter.Type))
                names.Add(parameter.Identifier.Text);
        }

        foreach (var declaration in member.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            foreach (var variable in declaration.Variables)
            {
                var initializer = variable.Initializer?.Value.ToString() ?? string.Empty;
                var reads = initializer.Contains("AsNoTracking", StringComparison.Ordinal) || initializer.Contains(".Select(", StringComparison.Ordinal)
                            || initializer.Contains("CountAsync", StringComparison.Ordinal) || initializer.Contains("AnyAsync", StringComparison.Ordinal)
                            || initializer.Contains("SelectList", StringComparison.Ordinal);
                if (IsReferenceType(declaration.Type) || (!reads && Sources.Any(s => initializer.Contains(s, StringComparison.Ordinal))))
                    names.Add(variable.Identifier.Text);
            }
        }

        return names;
    }

    private static bool IsRemovalOrBulkWrite(InvocationExpressionSyntax invocation, HashSet<string> records)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return false;
        var method = access.Name.Identifier.Text;
        var target = access.Expression.ToString();
        if ((target.EndsWith("PayGrades", StringComparison.Ordinal) || target.EndsWith("SalaryScales", StringComparison.Ordinal)
             || target.Contains("PayGrades.", StringComparison.Ordinal) || target.Contains("SalaryScales.", StringComparison.Ordinal))
            && method is "Add" or "AddAsync" or "AddRange" or "AddRangeAsync" or "Remove" or "RemoveRange" or "Update" or "UpdateRange" or "Attach"
                or "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync")
            return true;
        if (method is not ("Remove" or "RemoveRange"))
            return false;
        return invocation.ArgumentList.Arguments.Any(a =>
            (a.Expression is IdentifierNameSyntax name && records.Contains(name.Identifier.Text))
            || a.Expression.ToString().Contains("PayGrades", StringComparison.Ordinal)
            || a.Expression.ToString().Contains("SalaryScales", StringComparison.Ordinal));
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
                if (!source.Contains("PayGrade", StringComparison.Ordinal) && !source.Contains("SalaryScale", StringComparison.Ordinal))
                    continue;
                findings.AddRange(Scan(relative, source));
            }
        }

        return findings;
    }

    [Fact]
    public void Only_the_reference_change_applier_and_the_seed_write_a_scale_or_a_grade()
    {
        var unknown = ScanRepository().Where(f => !AllowedWriters.ContainsKey(f.Class))
            .Select(f => $"{f.Class} ({f.File}:{f.Line}) {f.What}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0,
            "A salary scale or a grade is written outside the reference change applier. It changes through a request another person approves "
            + "(SubmitPayrollReferenceChangeCommand):\n" + string.Join("\n", unknown));
    }

    [Fact]
    public void Every_named_writer_still_writes()
    {
        var writers = ScanRepository().Select(f => f.Class).ToHashSet(StringComparer.Ordinal);
        var stale = AllowedWriters.Keys.Where(k => !writers.Contains(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "These classes no longer write a scale or a grade: remove them from AllowedWriters: " + string.Join(", ", stale));
        Assert.True(AllowedWriters.Count <= 4, "The named writers never grow");
    }

    [Theory]
    [InlineData("var e = await _db.PayGrades.FindAsync(id); e.MaxSalary = model.MaxSalary;", "assigns MaxSalary of a scale or a grade")]
    [InlineData("var e = await _db.SalaryScales.FindAsync(id); e.IsActive = false;", "assigns IsActive of a scale or a grade")]
    [InlineData("_db.PayGrades.Add(model);", "removes or bulk-writes scales or grades")]
    [InlineData("_db.SalaryScales.Remove(e);", "removes or bulk-writes scales or grades")]
    [InlineData("db.Add(new PayGrade { GradeCode = code });", "creates a scale or a grade")]
    [InlineData("await db.PayGrades.Where(g => g.SalaryScaleId == id).ExecuteDeleteAsync();", "removes or bulk-writes scales or grades")]
    public void A_planted_write_is_found(string body, string what)
    {
        var findings = Scan("Planted.cs", $"class PlantedWriter {{ async Task Run() {{ {body} }} }}");
        Assert.Contains(findings, f => f.Class == "PlantedWriter" && f.What == what);
    }

    [Theory]
    [InlineData("var e = await _db.PayGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id); model.MaxSalary = e.MaxSalary;")]
    [InlineData("var scales = await _db.SalaryScales.AsNoTracking().Where(s => s.IsActive).ToListAsync(); ViewBag.Scales = new SelectList(scales, \"Id\", \"NameAr\");")]
    [InlineData("var request = new PayrollReferenceChangeRequest { EntityType = type, Reason = reason };")]
    [InlineData("var vm = new PayGradeChangeViewModel { GradeCode = e.GradeCode, MaxSalary = e.MaxSalary };")]
    public void A_read_is_not_a_write(string body)
    {
        Assert.Empty(Scan("Planted.cs", $"class PlantedReader {{ async Task Run() {{ {body} }} }}"));
    }
}

using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-56 (§1 rule 1, P-5): no code in Payroll computes a line's net but <c>PayrollNet</c>, and a line's net is written only where
/// this test names. The product source is read as text (Payroll's code and views, and the shared code that carries a line's figures):
/// a subtraction from a gross, a subtraction of the total deductions, or a new writer of <c>NetPay</c> fails the test. A second
/// formula is how the validation came to disagree with the engine, and how the posting came to rewrite what was approved (G-PAY-03).
/// </summary>
public sealed partial class Pay056NetComputationArchitectureTests
{
    /// <summary>The one file that may subtract deductions from a gross.</summary>
    public const string TheOneNet = "Services/Payroll/Payroll.Domain/Services/PayrollNet.cs";

    /// <summary>
    /// Files that subtract a «total deductions» of something that is not a salary line, each with why. The end-of-service settlement
    /// is a different figure of a different entity; its calculation is PLAN-64's (G-PAY-14).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotALinesNet = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs"] =
            "NetSettlement of an end-of-service settlement: gratuity + leave payout + compensation − advances, not a salary line (PLAN-64)",
    };

    /// <summary>The files that write a line's net, each with why. Every one takes it from <c>PayrollNet</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> NetWriters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs"] = "RoundingRule: the engine's net, PayrollNet under the framework's rounding",
        ["Services/Payroll/Payroll.Domain/Services/PayrollCalculation.cs"] = "GenerateSalaryLine: the legacy builder of the oldest tests, PayrollNet under the legacy rounding",
        ["Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs"] = "a seeded line, built like an engine line: PayrollNet",
    };

    /// <summary>A subtraction whose left side is a gross: <c>GrossPay - </c>, <c>gross - </c>, <c>totalGross - </c>, <c>l.GrossPay -</c>.</summary>
    [GeneratedRegex(@"\b\w*gross\w*\s*\)?\s*-(?![-=>])", RegexOptions.IgnoreCase)]
    private static partial Regex SubtractionFromAGross();

    /// <summary>A subtraction of the total deductions: <c>- line.TotalDeductions</c>, <c>- totalDeductions</c>.</summary>
    [GeneratedRegex(@"(?<![-=(,])\s-\s*[\w.]*totaldeductions\b", RegexOptions.IgnoreCase)]
    private static partial Regex SubtractionOfTheDeductions();

    /// <summary>An assignment of a line's net: <c>line.NetPay = …</c>, <c>NetPay = …,</c> in an initializer. Not the run's <c>TotalNetPay</c>.</summary>
    [GeneratedRegex(@"(?<![\w])NetPay\s*=(?!=)")]
    private static partial Regex NetAssignment();

    private static readonly string[] Roots =
    [
        "Services/Payroll",
        "Shared/HRManagement.Documents.QuestPdf",
        "Shared/HRManagement.Integration.Abstractions/Payroll",
        "Shared/HRManagement.Integration.FileAdapters/Payroll",
    ];

    private static IEnumerable<(string Relative, string Text)> ProductSources()
    {
        var src = Path.Combine(RepoPaths.Root, "src");
        foreach (var root in Roots)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(src, root), "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".cshtml", StringComparison.Ordinal))
                    continue;
                var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Contains("/Migrations/", StringComparison.Ordinal))
                    continue;
                yield return (relative, System.IO.File.ReadAllText(file));
            }
        }
    }

    /// <summary>A line of source without its comment, so that prose about a formula is not read as one.</summary>
    private static string Code(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("/*", StringComparison.Ordinal))
            return string.Empty;
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    /// <summary>Every place in <paramref name="sources"/> that computes a net beside the one definition.</summary>
    internal static List<string> Computations(IEnumerable<(string Relative, string Text)> sources)
    {
        var found = new List<string>();
        foreach (var (relative, text) in sources)
        {
            if (relative == TheOneNet)
                continue;
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var code = Code(lines[i]);
                if (SubtractionFromAGross().IsMatch(code)
                    || (SubtractionOfTheDeductions().IsMatch(code) && !NotALinesNet.ContainsKey(relative)))
                    found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        return found;
    }

    /// <summary>The files of <paramref name="sources"/> that assign a line's net.</summary>
    internal static List<string> Writers(IEnumerable<(string Relative, string Text)> sources) =>
        sources
            .Where(s => s.Text.Split('\n').Any(line => NetAssignment().IsMatch(Code(line))))
            .Select(s => s.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void No_code_in_Payroll_computes_a_net_but_PayrollNet()
    {
        var found = Computations(ProductSources());

        Assert.True(found.Count == 0, "A net is computed beside PayrollNet:\n" + string.Join("\n", found));
    }

    [Fact]
    public void A_lines_net_is_written_only_where_this_test_names_and_each_writer_takes_it_from_PayrollNet()
    {
        var sources = ProductSources().ToList();

        Assert.Equal(NetWriters.Keys.Order(StringComparer.Ordinal), Writers(sources));
        foreach (var writer in NetWriters.Keys)
            Assert.Contains("PayrollNet.Net(", sources.Single(s => s.Relative == writer).Text);
    }

    [Fact]
    public void The_one_net_exists_and_the_named_exceptions_exist()
    {
        var sources = ProductSources().ToDictionary(s => s.Relative, s => s.Text, StringComparer.Ordinal);

        Assert.Matches(SubtractionFromAGross(), sources[TheOneNet]);
        foreach (var exception in NotALinesNet.Keys)
            Assert.Matches(SubtractionOfTheDeductions(), sources[exception]);
    }

    [Theory]
    [InlineData("line.NetPay = line.GrossPay - line.TotalDeductions;")]
    [InlineData("var net = gross - deduction;")]
    [InlineData("var impliedNet = line.GrossPay - line.TotalDeductions + adjustments;")]
    [InlineData("return decimal.Round(Math.Max(0, gross - totalDeductions), 2);")]
    [InlineData("<td>@((l.GrossPay - l.TotalDeductions).ToString(\"N2\"))</td>")]
    [InlineData("var net = totalGross-totalDed;")]
    [InlineData("var x = (decimal)line.GrossPay - 5;")]
    [InlineData("var net = allowances + baseSalary - line.TotalDeductions;")]
    public void The_scan_sees_a_planted_computation(string planted)
    {
        var found = Computations([("Services/Payroll/Payroll.Application/Planted.cs", "namespace X;\n" + planted + "\n")]);

        Assert.Single(found);
        Assert.StartsWith("Services/Payroll/Payroll.Application/Planted.cs:2:", found[0]);
    }

    [Theory]
    [InlineData("result.NetPay = PayrollNet.Net(result.GrossPay, result.TotalDeductions, result.TotalAdjustments, framework);")]
    [InlineData("result.GrossPay += a.Amount;")]
    [InlineData("result.TotalDeductions += tax;")]
    [InlineData("// the old formula was gross - deductions")]
    [InlineData("    /// <c>Unclipped = gross − deductions + adjustments</c>")]
    [InlineData("csv.AppendLine($\"{line.EmployeeId},{line.GrossPay},{line.TotalDeductions},{line.NetPay}\");")]
    [InlineData("ws.Cell(row, 2).Value = line.GrossPay;")]
    [InlineData("Amount = -deduction,")]
    [InlineData("taxableBase -= result.TotalDeductions;")]
    [InlineData("labels: ['الإجمالي', 'الخصومات', 'الصافي'],")]
    public void The_scan_leaves_what_is_not_a_computation(string legitimate)
    {
        Assert.Empty(Computations([("Services/Payroll/Payroll.Application/Legitimate.cs", legitimate)]));
    }

    [Fact]
    public void The_scan_sees_a_planted_writer_of_a_lines_net()
    {
        Assert.Equal(
            ["Services/Payroll/Payroll.Application/Planted.cs"],
            Writers([("Services/Payroll/Payroll.Application/Planted.cs", "sl.NetPay = 5m;")]));
        Assert.Equal(
            ["Services/Payroll/Payroll.Infrastructure/Planted.cs"],
            Writers([("Services/Payroll/Payroll.Infrastructure/Planted.cs", "new SalaryLine { NetPay = net, }")]));
        // The run's total, a comparison and a read are not writes of a line's net.
        Assert.Empty(Writers([("X.cs", "run.TotalNetPay = total;\nif (a.NetPay == b.NetPay) return;\nvar n = line.NetPay;")]));
    }
}

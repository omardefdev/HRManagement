using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-58 (§1, P-5, G-PAY-08), extending <c>Pay001ApprovalBypassTests</c>: money leaves Payroll by one path. Payroll's product source is
/// read as text (code and views, comments left out):
/// <list type="bullet">
/// <item>no <c>new PaymentBatch</c> outside <c>PaymentBatchIssuer</c>, and the seed's named construction <c>PaymentBatch.Seeded</c>
/// called from the demo seeder only;</item>
/// <item>no call of the issuer's <c>IssueAsync</c> but from <c>PostPayrollRunHandler</c> (PLAN-64's final settlement will be the second);</item>
/// <item>no <c>EnsureAndSubmitOrdersAsync</c> outside <c>ProcessPaymentBatchHandler</c> and <c>SubmitHeldPaymentOrdersHandler</c>;</item>
/// <item>no settlement or rejection of a batch (<c>SettlePaymentBatchCommand</c>, <c>RejectPaymentBatchCommand</c>) but from the four-eyes
/// decision (<c>PayrollDualControlService</c>);</item>
/// <item>no partial disbursement left (<c>ExecutePartialDisbursementCommand</c>, <c>partial-disburse</c>).</item>
/// </list>
/// Each rule has a planted defect that the scan must catch.
/// </summary>
public sealed partial class Pay058SinglePaymentPathArchitectureTests
{
    public const string Issuer = "Services/Payroll/Payroll.Application/Services/PaymentBatchIssuer.cs";
    public const string BatchEntity = "Services/Payroll/Payroll.Domain/Entities/PaymentBatch.cs";
    public const string Seeder = "Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs";
    public const string BatchHandlers = "Services/Payroll/Payroll.Application/Features/PayrollRuns/PaymentBatchHandlers.cs";
    public const string RunHandlers = "Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs";
    public const string DualControl = "Services/Payroll/Payroll.Application/Services/PayrollDualControlService.cs";

    /// <summary>The declarations of the sending method (its interface and its one implementation), not calls.</summary>
    public static readonly IReadOnlySet<string> SendingDeclarations = new HashSet<string>(StringComparer.Ordinal)
    {
        "Services/Payroll/Payroll.Application/Abstractions/IPaymentOrderCoordinator.cs",
        "Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderCoordinator.cs",
    };

    [GeneratedRegex(@"\bnew\s+PaymentBatch\s*(\(|\{|$)", RegexOptions.Multiline)]
    private static partial Regex NewBatch();

    [GeneratedRegex(@"\bPaymentBatch\.Seeded\s*\(")]
    private static partial Regex SeededBatch();

    [GeneratedRegex(@"\.IssueAsync\s*\(")]
    private static partial Regex IssuerCall();

    [GeneratedRegex(@"\bEnsureAndSubmitOrdersAsync\s*\(")]
    private static partial Regex Sending();

    [GeneratedRegex(@"\bnew\s+(SettlePaymentBatchCommand|RejectPaymentBatchCommand)\s*\(")]
    private static partial Regex ConfirmationCommand();

    [GeneratedRegex(@"\b(ExecutePartialDisbursementCommand|partial-disburse)\b")]
    private static partial Regex RemovedPath();

    [GeneratedRegex(@"^\s*(public|internal|file)\s+(sealed\s+|static\s+|abstract\s+|partial\s+)*(class|record|interface)\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex TopLevelType();

    /// <summary>One source file: its path under <c>src/</c> and its text.</summary>
    public sealed record Source(string Relative, string Text);

    public static IEnumerable<Source> ProductSources()
    {
        var src = Path.Combine(RepoPaths.Root, "src");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(src, "Services", "Payroll"), "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".cshtml", StringComparison.Ordinal))
                continue;
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal))
                continue;
            yield return new Source(relative, System.IO.File.ReadAllText(file));
        }
    }

    /// <summary>The source without its comments, line for line (so a line number still points at the code).</summary>
    public static string Code(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("@*", StringComparison.Ordinal))
            {
                lines[i] = string.Empty;
                continue;
            }

            var comment = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0 && !lines[i][..comment].Contains("http", StringComparison.Ordinal))
                lines[i] = lines[i][..comment];
        }

        return string.Join('\n', lines);
    }

    /// <summary>The top-level type that encloses a position of the source (the last type declared before it).</summary>
    public static string? EnclosingType(string code, int index) =>
        TopLevelType().Matches(code[..index]).LastOrDefault()?.Groups["name"].Value;

    /// <summary>Every violation of the five rules in <paramref name="sources"/>.</summary>
    public static IReadOnlyList<string> Violations(IEnumerable<Source> sources)
    {
        var violations = new List<string>();
        foreach (var source in sources)
        {
            var code = Code(source.Text);
            foreach (Match m in NewBatch().Matches(code))
            {
                if (source.Relative is not (Issuer or BatchEntity))
                    violations.Add($"{source.Relative}:{Line(code, m.Index)}: new PaymentBatch outside PaymentBatchIssuer");
            }

            foreach (Match m in SeededBatch().Matches(code))
            {
                if (source.Relative != Seeder)
                    violations.Add($"{source.Relative}:{Line(code, m.Index)}: PaymentBatch.Seeded outside the demo seeder");
            }

            foreach (Match m in IssuerCall().Matches(code))
            {
                if (source.Relative == Issuer)
                    continue;
                if (!(source.Relative == RunHandlers && EnclosingType(code, m.Index) == "PostPayrollRunHandler"))
                    violations.Add($"{source.Relative}:{Line(code, m.Index)}: the issuer called outside PostPayrollRunHandler");
            }

            foreach (Match m in Sending().Matches(code))
            {
                if (SendingDeclarations.Contains(source.Relative))
                    continue;
                var type = EnclosingType(code, m.Index);
                if (!(source.Relative == BatchHandlers && type is "ProcessPaymentBatchHandler" or "SubmitHeldPaymentOrdersHandler"))
                    violations.Add($"{source.Relative}:{Line(code, m.Index)}: orders sent outside process and submit-held ({type})");
            }

            foreach (Match m in ConfirmationCommand().Matches(code))
            {
                if (source.Relative != DualControl)
                    violations.Add($"{source.Relative}:{Line(code, m.Index)}: {m.Groups[1].Value} outside the four-eyes decision");
            }

            foreach (Match m in RemovedPath().Matches(code))
                violations.Add($"{source.Relative}:{Line(code, m.Index)}: {m.Value} — a payment path PLAN-58 removed");
        }

        return violations;
    }

    private static int Line(string code, int index) => code.AsSpan(0, index).Count('\n') + 1;

    [Fact]
    public void Money_leaves_Payroll_by_one_path()
    {
        var violations = Violations(ProductSources());
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void The_one_path_exists_where_this_test_names_it()
    {
        var sources = ProductSources().ToDictionary(s => s.Relative, s => Code(s.Text), StringComparer.Ordinal);
        Assert.Matches(NewBatch(), sources[Issuer]);
        Assert.Matches(SeededBatch(), sources[Seeder]);
        var post = sources[RunHandlers];
        Assert.Contains(IssuerCall().Matches(post), m => EnclosingType(post, m.Index) == "PostPayrollRunHandler");
        var handlers = sources[BatchHandlers];
        Assert.Contains(Sending().Matches(handlers), m => EnclosingType(handlers, m.Index) == "ProcessPaymentBatchHandler");
        Assert.Contains(Sending().Matches(handlers), m => EnclosingType(handlers, m.Index) == "SubmitHeldPaymentOrdersHandler");
    }

    [Fact]
    public void Planted_a_batch_made_by_a_controller_is_caught() =>
        Assert.Contains(Violations([new Source("Services/Payroll/Payroll.Web/Controllers/Fake.cs",
            "public class FakeController\n{\n    void Pay() { var b = new PaymentBatch { TotalAmount = 1 }; }\n}\n")]),
            v => v.Contains("new PaymentBatch outside PaymentBatchIssuer", StringComparison.Ordinal));

    [Fact]
    public void Planted_a_seeded_batch_outside_the_seeder_is_caught() =>
        Assert.Contains(Violations([new Source("Services/Payroll/Payroll.Application/Services/Fake.cs",
            "public class Fake\n{\n    void Pay() { var b = PaymentBatch.Seeded(a, b, c); }\n}\n")]),
            v => v.Contains("PaymentBatch.Seeded outside", StringComparison.Ordinal));

    [Fact]
    public void Planted_an_issuer_called_by_another_handler_is_caught() =>
        Assert.Contains(Violations([new Source(RunHandlers,
            "public class PostPayrollRunHandler\n{\n    async Task A() { await issuer.IssueAsync(run, lines, s, ct); }\n}\n" +
            "public class ApprovePayrollRunHandler\n{\n    async Task B() { await issuer.IssueAsync(run, lines, s, ct); }\n}\n")]),
            v => v.Contains("issuer called outside PostPayrollRunHandler", StringComparison.Ordinal));

    [Fact]
    public void Planted_a_second_sending_path_is_caught()
    {
        var violations = Violations([new Source(BatchHandlers,
            "public class ProcessPaymentBatchHandler\n{\n    async Task A() { await c.EnsureAndSubmitOrdersAsync(b, g, s, ct); }\n}\n" +
            "public class SneakyDisbursementHandler\n{\n    async Task B() { await c.EnsureAndSubmitOrdersAsync(b, g, s, ct); }\n}\n")]);
        Assert.Single(violations);
        Assert.Contains("SneakyDisbursementHandler", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Planted_a_settlement_by_one_person_is_caught() =>
        Assert.Contains(Violations([new Source("Services/Payroll/Payroll.Web/Controllers/Fake.cs",
            "public class FakeController\n{\n    async Task A() { await m.Send(new SettlePaymentBatchCommand(id, r, a)); }\n}\n")]),
            v => v.Contains("SettlePaymentBatchCommand outside the four-eyes decision", StringComparison.Ordinal));

    [Fact]
    public void Planted_a_partial_disbursement_is_caught() =>
        Assert.Contains(Violations([new Source("Services/Payroll/Payroll.Web/Controllers/Fake.cs",
            "public class FakeController\n{\n    [HttpPost(\"runs/{id}/partial-disburse\")] public IActionResult A() => Ok();\n}\n")]),
            v => v.Contains("a payment path PLAN-58 removed", StringComparison.Ordinal));

    [Fact]
    public void A_comment_about_the_path_is_not_the_path() =>
        Assert.Empty(Violations([new Source("Services/Payroll/Payroll.Web/Controllers/Fake.cs",
            "public class FakeController\n{\n    // new PaymentBatch { } and EnsureAndSubmitOrdersAsync( are the issuer's and the sending's\n}\n")]));
}

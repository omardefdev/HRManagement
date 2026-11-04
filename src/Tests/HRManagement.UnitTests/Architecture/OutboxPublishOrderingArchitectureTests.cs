using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-40 (§5): the static half of the guards, on Roslyn syntax trees of <c>src/Services</c> and <c>src/Shared</c>.
/// <list type="bullet">
/// <item><b>R1</b> a publish (or a call of a <c>[StagesOutboxMessages]</c> method) is followed by a save in the same method,
/// or the method is marked, or it runs inside <c>OutboxUnitOfWork.ExecuteAsync</c>.</item>
/// <item><b>R2</b> no save or <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> before a publish that a later save writes, outside
/// the unit of work or an explicit transaction.</item>
/// <item><b>R3</b> <c>context.Publish</c> in a consumer or saga only in a service registered with <c>consumerOutbox: true</c>.</item>
/// <item><b>R4</b> every published type has a row in <c>docs/EVENT_PUBLISHER_INVENTORY.md</c>.</item>
/// </list>
/// Exceptions live in <c>tests/architecture/outbox-publish-allowlist.txt</c>, each with a written reason. Saves are matched
/// by name and order, not by control flow; a publish hidden behind a method that is neither a publish nor marked is outside
/// what a syntax check can see, which is what <c>UnsavedOutboxGuardMiddleware</c> is for.
/// </summary>
public class OutboxPublishOrderingArchitectureTests
{
    /// <summary>The allow-list may only shrink: raising this needs a reason in the same commit.</summary>
    private const int MaxAllowlistEntries = 0;

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

    private static string AllowlistPath => Path.Combine(RepoRoot, "tests", "architecture", "outbox-publish-allowlist.txt");

    private static IEnumerable<SourceFile> RepositorySources()
    {
        foreach (var root in new[] { "src/Services", "src/Shared" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Contains("/Migrations/", StringComparison.Ordinal))
                    continue;
                yield return new SourceFile(relative, global::System.IO.File.ReadAllText(path));
            }
        }
    }

    private static HashSet<string> ServicesWithConsumerOutbox() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Services"), "Program.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => global::System.IO.File.ReadAllText(p).Contains("consumerOutbox: true", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(Path.Combine(RepoRoot, "src", "Services"), p).Split(Path.DirectorySeparatorChar)[0])
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<AllowlistEntry> ReadAllowlist() =>
        global::System.IO.File.ReadAllLines(AllowlistPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(AllowlistEntry.Parse)
            .ToList();

    [Fact]
    public void Every_publish_in_the_repository_is_atomic_and_documented()
    {
        var inventory = global::System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "EVENT_PUBLISHER_INVENTORY.md"));
        var allowed = ReadAllowlist().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var report = OutboxPublishScanner.Scan(RepositorySources(), ServicesWithConsumerOutbox(), inventory);
        var violations = report.Violations.Where(v => !allowed.Contains(v.Key)).Select(v => v.ToString()).ToList();

        Assert.True(report.PublishSites > 100, $"The scanner found only {report.PublishSites} publish sites: it no longer sees the code.");
        Assert.True(violations.Count == 0,
            "Publish with the patterns in docs/EVENT_PUBLISHER_INVENTORY.md (publish then one save, OutboxUnitOfWork, consumer outbox):\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void Allowlist_lines_have_a_reason_and_it_does_not_grow()
    {
        var entries = ReadAllowlist();
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason), $"No written reason: {e.Key}"));
        Assert.True(entries.Count <= MaxAllowlistEntries, $"The allow-list grew to {entries.Count} (limit {MaxAllowlistEntries}).");
    }

    [Fact]
    public void Allowlist_lines_still_match_a_violation()
    {
        var inventory = global::System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "EVENT_PUBLISHER_INVENTORY.md"));
        var found = OutboxPublishScanner.Scan(RepositorySources(), ServicesWithConsumerOutbox(), inventory)
            .Violations.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(ReadAllowlist(), e => Assert.Contains(e.Key, found));
    }

    [Fact]
    public void All_thirteen_outbox_services_run_the_consumer_outbox_and_the_runtime_guard()
    {
        var programs = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Services"), "Program.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(p => (Path: p, Text: global::System.IO.File.ReadAllText(p)))
            .Where(p => p.Text.Contains("AddPostgresOutbox<", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(13, programs.Count);
        Assert.All(programs, p =>
        {
            Assert.True(p.Text.Contains("consumerOutbox: true", StringComparison.Ordinal), $"{p.Path}: consumerOutbox");
            Assert.True(p.Text.Contains("UseHrOutboxFlushGuard()", StringComparison.Ordinal), $"{p.Path}: runtime guard");
        });
    }

    // ---------------------------------------------------------------------------- the guard on broken samples

    private const string Documented = "`IThingHappened` `IOtherThing`";

    private static IReadOnlyList<Violation> Check(string code, string path = "src/Services/Sample/Sample.Web/Sample.cs", string inventory = Documented, params string[] consumerOutboxServices) =>
        OutboxPublishScanner.Scan(
            [new SourceFile(path, "using System.Threading.Tasks;\nclass Sample {\n" + code + "\n}")],
            consumerOutboxServices.ToHashSet(StringComparer.Ordinal),
            inventory).Violations;

    [Fact]
    public void R1_fails_on_a_publish_with_no_save_after_it()
    {
        var violations = Check("""
            async Task Run() {
                await db.SaveChangesAsync();
                await publish.Publish<IThingHappened>(new { });
            }
            """);
        Assert.Equal("R1", Assert.Single(violations).Rule);
    }

    [Fact]
    public void R1_sees_a_publish_endpoint_under_any_name()
    {
        var violations = Check("""
            MassTransit.IPublishEndpoint renamed;
            async Task Run() { await renamed.Publish<IThingHappened>(new { }); }
            """);
        Assert.Equal("R1", Assert.Single(violations).Rule);
    }

    [Fact]
    public void R1_treats_a_marked_method_as_a_publish_in_its_caller()
    {
        var violations = Check("""
            [StagesOutboxMessages] async Task Stage() { await publish.Publish<IThingHappened>(new { }); }
            async Task Caller() { await Stage(); }
            async Task GoodCaller() { await Stage(); await db.SaveChangesAsync(); }
            """);
        var violation = Assert.Single(violations);
        Assert.Equal("R1", violation.Rule);
        Assert.Equal("Caller", violation.Method);
    }

    [Fact]
    public void R1_accepts_publish_then_save_a_marked_method_and_the_unit_of_work()
    {
        Assert.Empty(Check("""
            async Task Single() { await publish.Publish<IThingHappened>(new { }); await db.SaveChangesAsync(); }
            [StagesOutboxMessages] async Task Marked() { await publish.Publish<IThingHappened>(new { }); }
            async Task Unit() { await OutboxUnitOfWork.ExecuteAsync(db, async ct => { await publish.Publish<IThingHappened>(new { }, ct); }, ct); }
            async Task Repo() { await repository.ExecuteAtomicAsync(ct => Inner(ct), ct); }
            """));
    }

    [Fact]
    public void R2_fails_on_save_publish_save()
    {
        var violations = Check("""
            async Task Run() {
                await db.SaveChangesAsync();
                await publish.Publish<IThingHappened>(new { });
                await db.SaveChangesAsync();
            }
            """);
        Assert.Equal("R2", Assert.Single(violations).Rule);
    }

    [Fact]
    public void R2_fails_on_ExecuteUpdate_before_a_publish_outside_a_transaction()
    {
        var violations = Check("""
            async Task Run() {
                await db.Things.ExecuteUpdateAsync(s => s);
                await publish.Publish<IThingHappened>(new { });
                await db.SaveChangesAsync();
            }
            """);
        Assert.Equal("R2", Assert.Single(violations).Rule);
    }

    [Fact]
    public void R2_accepts_the_sequence_inside_the_unit_of_work_or_an_explicit_transaction()
    {
        Assert.Empty(Check("""
            async Task Unit() {
                await OutboxUnitOfWork.ExecuteAsync(db, async ct => {
                    await db.SaveChangesAsync(ct);
                    await publish.Publish<IThingHappened>(new { }, ct);
                    await db.SaveChangesAsync(ct);
                }, ct);
            }
            async Task Explicit() {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.SaveChangesAsync();
                await publish.Publish<IThingHappened>(new { });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            """));
    }

    [Fact]
    public void R2_accepts_a_body_run_by_the_unit_of_work_and_an_earlier_branch_that_returned()
    {
        Assert.Empty(Check("""
            Task<bool> Handle() => repository.ExecuteAtomicAsync(token => Inner(token), ct);
            private async Task<bool> Inner(CancellationToken ct) {
                await db.Things.ExecuteUpdateAsync(s => s, ct);
                await publish.Publish<IThingHappened>(new { }, ct);
                await db.SaveChangesAsync(ct);
                return true;
            }
            async Task Cancel() {
                if (draft) {
                    await db.SaveChangesAsync();
                    return;
                }
                await publish.Publish<IThingHappened>(new { });
                await db.SaveChangesAsync();
            }
            """));
    }

    [Fact]
    public void R2_still_fails_when_the_earlier_branch_falls_through()
    {
        var violations = Check("""
            async Task Run() {
                if (first) {
                    await db.SaveChangesAsync();
                }
                await publish.Publish<IThingHappened>(new { });
                await db.SaveChangesAsync();
            }
            """);
        Assert.Equal("R2", Assert.Single(violations).Rule);
    }

    [Fact]
    public void R3_fails_on_context_publish_in_a_service_without_the_consumer_outbox()
    {
        const string consumer = "async Task Consume(ConsumeContext<IOtherThing> context) { await context.Publish<IThingHappened>(new { }); }";
        Assert.Equal("R3", Assert.Single(Check(consumer)).Rule);
        Assert.Empty(Check(consumer, consumerOutboxServices: "Sample"));
    }

    [Fact]
    public void R4_fails_on_an_event_missing_from_the_inventory()
    {
        var violations = Check(
            "async Task Run() { await publish.Publish<INewEvent>(new { }); await db.SaveChangesAsync(); }");
        var violation = Assert.Single(violations);
        Assert.Equal("R4", violation.Rule);
        Assert.Contains("INewEvent", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mediator_and_http_sends_are_not_publishes()
    {
        Assert.Empty(Check("""
            async Task Run() {
                await mediator.Send(new Command());
                await sender.Send(new Command());
                await client.SendAsync(request);
            }
            """));
    }

    [Fact]
    public void An_allowlist_line_without_a_reason_is_seen_as_such()
    {
        Assert.True(string.IsNullOrEmpty(AllowlistEntry.Parse("R1 src/Sample.cs#Run").Reason));
        var entry = AllowlistEntry.Parse("R1 src/Sample.cs#Run # the consumer is gone");
        Assert.Equal("R1 src/Sample.cs#Run", entry.Key);
        Assert.Equal("the consumer is gone", entry.Reason);
    }
}

internal sealed record SourceFile(string Path, string Text);

internal sealed record Violation(string Rule, string Path, string Method, int Line, string Message)
{
    public string Key => $"{Rule} {Path}#{Method}";

    public override string ToString() => $"{Rule} {Path}:{Line} {Method}: {Message}";
}

internal sealed record AllowlistEntry(string Key, string? Reason)
{
    public static AllowlistEntry Parse(string line)
    {
        var mark = line.IndexOf(" # ", StringComparison.Ordinal);
        return mark < 0
            ? new AllowlistEntry(line.Trim(), null)
            : new AllowlistEntry(line[..mark].Trim(), line[(mark + 3)..].Trim());
    }
}

internal sealed record ScanReport(IReadOnlyList<Violation> Violations, int PublishSites);

internal static class OutboxPublishScanner
{
    private const string StagesAttribute = "StagesOutboxMessages";

    private static readonly HashSet<string> PublishMethods = new(StringComparer.Ordinal) { "Publish", "PublishBatch", "Send", "SendBatch" };

    private static readonly HashSet<string> SetBasedWrites = new(StringComparer.Ordinal) { "ExecuteUpdateAsync", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteDelete" };

    private static readonly HashSet<string> UnitOfWorkMethods = new(StringComparer.Ordinal) { "ExecuteAtomicAsync" };

    private static readonly Regex EndpointName = new("^_?(publish|publishEndpoint|bus|sendEndpoint|sendEndpointProvider)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ContextName = new("^_?(context|ctx|consumeContext)$", RegexOptions.Compiled);

    private static readonly Regex EndpointType = new(@"^(IPublishEndpoint|IBus|IBusControl|ISendEndpointProvider|ISendEndpoint)\??$", RegexOptions.Compiled);

    private static readonly Regex ContextType = new(@"^(ConsumeContext|BehaviorContext|SagaConsumeContext)\b", RegexOptions.Compiled);

    public static ScanReport Scan(IEnumerable<SourceFile> sources, IReadOnlySet<string> consumerOutboxServices, string inventory)
    {
        var trees = sources
            .Where(s => s.Text.Contains("Publish", StringComparison.Ordinal) || s.Text.Contains("Send", StringComparison.Ordinal))
            .Select(s => (s.Path, Root: CSharpSyntaxTree.ParseText(s.Text).GetCompilationUnitRoot()))
            .ToList();

        var stagers = trees
            .SelectMany(t => t.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(IsMarked)
            .Select(m => m.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<Violation>();
        var sites = 0;
        foreach (var (path, root) in trees)
        {
            var endpoints = NamesDeclaredAs(root, EndpointType);
            var contexts = NamesDeclaredAs(root, ContextType);
            var unitOfWorkBodies = MethodsCalledInsideUnitOfWork(root);
            foreach (var body in Bodies(root))
            {
                var calls = body.Node.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .OrderBy(c => c.Span.End)
                    .Select(c => Classify(c, stagers, endpoints, contexts))
                    .Where(c => c.Kind != CallKind.Other)
                    .ToList();

                foreach (var publish in calls.Where(c => c.Kind is CallKind.Publish or CallKind.ContextPublish))
                {
                    sites++;
                    var line = publish.Node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    CheckDocumented(publish, inventory, path, body.Name, line, violations);

                    if (publish.Kind == CallKind.ContextPublish)
                    {
                        var service = ServiceOf(path);
                        if (service is not null && !consumerOutboxServices.Contains(service))
                            violations.Add(new Violation("R3", path, body.Name, line, $"{publish.Name} from a consumer in {service}, which has no consumerOutbox: true"));
                        continue;
                    }

                    if (body.Marked || InsideUnitOfWork(publish.Node) || unitOfWorkBodies.Contains(body.Name))
                        continue;

                    var transactional = calls.Any(c => c.Kind == CallKind.BeginTransaction && c.Node.Span.End <= publish.Node.SpanStart);
                    var saveAfter = calls.Any(c => c.Kind == CallKind.Save && c.Node.SpanStart >= publish.Node.Span.End);
                    var writeBefore = calls.Any(c => c.Kind is CallKind.Save or CallKind.SetBasedWrite
                                                     && c.Node.Span.End <= publish.Node.SpanStart
                                                     && !InReturningSiblingBranch(c.Node, publish.Node));

                    if (!saveAfter)
                        violations.Add(new Violation("R1", path, body.Name, line, $"{publish.Name} has no SaveChanges after it and the method is not [{StagesAttribute}]"));
                    else if (writeBefore && !transactional)
                        violations.Add(new Violation("R2", path, body.Name, line, $"{publish.Name} sits between two writes outside OutboxUnitOfWork"));
                }
            }
        }

        return new ScanReport(violations, sites);
    }

    private static void CheckDocumented(Call publish, string inventory, string path, string method, int line, List<Violation> violations)
    {
        var type = publish.MessageType;
        if (type is null || publish.IsStager)
            return;
        // A concrete message documents itself through its contract (PolicyPublished : IPolicyPublished).
        if (!inventory.Contains($"`{type}`", StringComparison.Ordinal) && !inventory.Contains($"`I{type}`", StringComparison.Ordinal))
            violations.Add(new Violation("R4", path, method, line, $"{type} has no row in docs/EVENT_PUBLISHER_INVENTORY.md"));
    }

    private static string? ServiceOf(string path)
    {
        const string prefix = "src/Services/";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..].Split('/')[0] : null;
    }

    private static bool IsMarked(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains(StagesAttribute, StringComparison.Ordinal));

    private static IEnumerable<(SyntaxNode Node, string Name, bool Marked)> Bodies(CompilationUnitSyntax root)
    {
        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    yield return (method, method.Identifier.Text, IsMarked(method));
                    break;
                case ConstructorDeclarationSyntax ctor:
                    yield return (ctor, ctor.Identifier.Text, false);
                    break;
            }
        }

        // Top-level statements (Program.cs).
        foreach (var global in root.Members.OfType<GlobalStatementSyntax>())
            yield return (global, "<top-level>", false);
    }

    private static HashSet<string> NamesDeclaredAs(CompilationUnitSyntax root, Regex type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Type is not null && type.IsMatch(LastSegment(parameter.Type)))
                names.Add(parameter.Identifier.Text);
        }

        foreach (var field in root.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            if (type.IsMatch(LastSegment(field.Type)))
            {
                foreach (var variable in field.Variables)
                    names.Add(variable.Identifier.Text);
            }
        }

        return names;
    }

    private static string LastSegment(TypeSyntax type)
    {
        var text = type.ToString();
        var generic = text.IndexOf('<');
        var head = generic < 0 ? text : text[..generic];
        var dot = head.LastIndexOf('.');
        return (dot < 0 ? text : text[(dot + 1)..]).Trim();
    }

    /// <summary>A write in an earlier branch that ends the method never runs before this publish.</summary>
    private static bool InReturningSiblingBranch(SyntaxNode write, SyntaxNode publish) =>
        write.Ancestors().OfType<BlockSyntax>()
            .TakeWhile(b => !b.Span.Contains(publish.Span))
            .Any(b => b.Statements.LastOrDefault() is ReturnStatementSyntax or ThrowStatementSyntax);

    /// <summary>Private bodies a unit of work runs, as in <c>repository.ExecuteAtomicAsync(token => ApproveAsync(request, token), ct)</c>.</summary>
    private static HashSet<string> MethodsCalledInsideUnitOfWork(CompilationUnitSyntax root) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(IsUnitOfWorkCall)
            .SelectMany(c => c.ArgumentList.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Select(c => (c.Expression as SimpleNameSyntax)?.Identifier.Text)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static bool InsideUnitOfWork(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().Any(IsUnitOfWorkCall);

    private static bool IsUnitOfWorkCall(InvocationExpressionSyntax call)
    {
        if (call.Expression is not MemberAccessExpressionSyntax access)
            return false;
        return UnitOfWorkMethods.Contains(access.Name.Identifier.Text)
               || (access.Name.Identifier.Text == "ExecuteAsync" && access.Expression.ToString().EndsWith("OutboxUnitOfWork", StringComparison.Ordinal));
    }

    private static Call Classify(InvocationExpressionSyntax call, HashSet<string> stagers, HashSet<string> endpoints, HashSet<string> contexts)
    {
        var (receiver, simple) = call.Expression switch
        {
            MemberAccessExpressionSyntax access => (access.Expression.ToString(), access.Name),
            SimpleNameSyntax bare => (string.Empty, bare),
            _ => (string.Empty, (SimpleNameSyntax?)null)
        };
        if (simple is null)
            return new Call(call, CallKind.Other, string.Empty, null, false);

        var name = simple.Identifier.Text;
        var last = receiver[(receiver.LastIndexOf('.') + 1)..];

        if (name.StartsWith("SaveChanges", StringComparison.Ordinal))
            return new Call(call, CallKind.Save, name, null, false);
        if (SetBasedWrites.Contains(name))
            return new Call(call, CallKind.SetBasedWrite, name, null, false);
        if (name is "BeginTransactionAsync" or "BeginTransaction")
            return new Call(call, CallKind.BeginTransaction, name, null, false);
        if (stagers.Contains(name))
            return new Call(call, CallKind.Publish, name, null, true);

        if (PublishMethods.Contains(name))
        {
            var messageType = MessageTypeOf(call, simple);
            if (ContextName.IsMatch(last) || contexts.Contains(last))
                return new Call(call, CallKind.ContextPublish, $"{last}.{name}", messageType, false);
            if (EndpointName.IsMatch(last) || endpoints.Contains(last))
                return new Call(call, CallKind.Publish, $"{last}.{name}", messageType, false);
        }

        return new Call(call, CallKind.Other, name, null, false);
    }

    private static string? MessageTypeOf(InvocationExpressionSyntax call, SimpleNameSyntax name)
    {
        if (name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0)
            return LastSegment(generic.TypeArgumentList.Arguments[0]);

        return call.ArgumentList.Arguments.FirstOrDefault()?.Expression is ObjectCreationExpressionSyntax created
            ? LastSegment(created.Type)
            : null;
    }

    private enum CallKind
    {
        Other,
        Publish,
        ContextPublish,
        Save,
        SetBasedWrite,
        BeginTransaction
    }

    private sealed record Call(InvocationExpressionSyntax Node, CallKind Kind, string Name, string? MessageType, bool IsStager);
}

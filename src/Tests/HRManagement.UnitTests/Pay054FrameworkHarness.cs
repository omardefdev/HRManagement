using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54: the framework commands on a relational in-memory database (SQLite: real transactions and unique indexes, which the EF
/// in-memory provider does not have), as the handlers run them: every send uses a fresh context, as a request does. The clock is
/// 15 November 2026, noon UTC, unless a test moves it.
/// </summary>
public sealed class Pay054FrameworkHarness : IDisposable
{
    public static readonly Guid Editor = Guid.Parse("54000000-0000-4000-8000-0000000000c1");
    public static readonly Guid OtherEditor = Guid.Parse("54000000-0000-4000-8000-0000000000c2");
    public static readonly Guid Approver = Guid.Parse("54000000-0000-4000-8000-0000000000c3");
    public static readonly Guid Attestor = Guid.Parse("54000000-0000-4000-8000-0000000000c4");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public Pay054FrameworkHarness(DateTimeOffset? now = null)
    {
        Clock = new Iam053Clock(now ?? new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        _connection.Open();
        using var db = Db();
        db.Database.EnsureCreated();
    }

    public Iam053Clock Clock { get; }

    public IPayrollCalendar Calendar => new PayrollCalendar(Clock);

    public PayrollDbContext Db() => new(new DbContextOptionsBuilder<PayrollDbContext>().UseSqlite(_connection).Options);

    public PayrollRepository Repository(PayrollDbContext db) => new(db, Calendar);

    public void Dispose() => _connection.Dispose();

    // ------------------------------------------------------------------ the pack

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
        }
    }

    public static string PackPath => Path.Combine(RepoRoot, "docs", "syr", "payroll-framework-packs", "law50-interim.json");

    public static string SchemaPath => Path.Combine(RepoRoot, "docs", "syr", "payroll-framework-packs", "schema.v1.json");

    public static string InterimPackJson() => System.IO.File.ReadAllText(PackPath);

    /// <summary>The interim pack with one change, as JSON: the way a test plants a defect.</summary>
    public static string PackWith(Action<JsonObject> change)
    {
        var node = JsonNode.Parse(InterimPackJson())!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    // ------------------------------------------------------------------ the commands

    public async Task<PayrollFrameworkResult> ImportAsync(
        string? json = null,
        string? code = null,
        DateOnly? effectiveFrom = null,
        Guid? targetDraftId = null,
        Guid? actor = null)
    {
        await using var db = Db();
        return await new ImportPayrollFrameworkPackHandler(Repository(db), Calendar).Handle(
            new ImportPayrollFrameworkPackCommand(json ?? InterimPackJson(), code, effectiveFrom, targetDraftId, actor ?? Editor),
            CancellationToken.None);
    }

    public async Task<PayrollFrameworkResult> CreateAsync(
        string? regimeCode = null,
        Guid? cloneFromId = null,
        string? code = null,
        string? nameAr = null,
        DateOnly? effectiveFrom = null,
        Guid? actor = null)
    {
        await using var db = Db();
        return await new CreatePayrollFrameworkHandler(Repository(db), Calendar).Handle(
            new CreatePayrollFrameworkCommand(regimeCode, cloneFromId, code, nameAr, effectiveFrom, actor ?? Editor),
            CancellationToken.None);
    }

    public async Task<PayrollFrameworkResult> SetEffectiveFromAsync(Guid id, DateOnly? effectiveFrom, Guid? actor = null)
    {
        await using var db = Db();
        return await new SetPayrollFrameworkEffectiveFromHandler(Repository(db), Calendar).Handle(
            new SetPayrollFrameworkEffectiveFromCommand(id, effectiveFrom, actor ?? Editor), CancellationToken.None);
    }

    public async Task<PayrollFrameworkExport?> ExportAsync(Guid id)
    {
        await using var db = Db();
        return await new ExportPayrollFrameworkHandler(Repository(db)).Handle(new ExportPayrollFrameworkQuery(id), CancellationToken.None);
    }

    // ------------------------------------------------------------------ reading

    /// <summary>A version with all of its tables and its editors, read fresh.</summary>
    public async Task<PayrollConfiguration> VersionAsync(Guid id)
    {
        await using var db = Db();
        return await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .Include(c => c.StatutoryDeductions)
            .Include(c => c.Holidays)
            .Include(c => c.PriorityRules)
            .Include(c => c.FieldSources)
            .Include(c => c.Editors)
            .AsNoTracking()
            .SingleAsync(c => c.Id == id);
    }

    /// <summary>The number of rows in every table a framework command may write.</summary>
    public async Task<Dictionary<string, int>> RowCountsAsync()
    {
        await using var db = Db();
        return new Dictionary<string, int>
        {
            ["PayrollConfigurations"] = await db.PayrollConfigurations.CountAsync(),
            ["TaxBrackets"] = await db.TaxBrackets.CountAsync(),
            ["StatutoryDeductionRules"] = await db.StatutoryDeductionRules.CountAsync(),
            ["OfficialHolidays"] = await db.OfficialHolidays.CountAsync(),
            ["DisbursementPriorityRules"] = await db.DisbursementPriorityRules.CountAsync(),
            ["PayrollConfigurationFieldSources"] = await db.PayrollConfigurationFieldSources.CountAsync(),
            ["PayrollConfigurationEditors"] = await db.PayrollConfigurationEditors.CountAsync(),
            ["LegalDecisionRecords"] = await db.LegalDecisionRecords.CountAsync(),
            ["LegalAttestations"] = await db.LegalAttestations.CountAsync(),
            ["PayrollConfigChangeNotices"] = await db.PayrollConfigChangeNotices.CountAsync(),
        };
    }

    public async Task AssertNothingWrittenAsync(Dictionary<string, int> before)
    {
        var after = await RowCountsAsync();
        foreach (var (table, count) in before)
            Assert.True(count == after[table], $"{table}: {after[table]} rows after a refusal, {count} before it");
    }
}

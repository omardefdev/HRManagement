using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>L10N-002: Arabic name normalization interceptor + normalized lookup query.</summary>
public class L10n002NormalizationIntegrationTests : IAsyncDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<HRCoreDbContext> _options;

    public L10n002NormalizationIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new ArabicNameNormalizationInterceptor())
            .Options;

        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Interceptor_stamps_normalized_names_on_save()
    {
        await using var db = CreateDb();
        var employee = NewEmployee(
            nationalId: "1000000001",
            firstName: "\u0623\u062D\u0645\u062F", // أحمد
            lastName: "\u0639\u0644\u064A");      // علي

        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var saved = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(ArabicTextNormalizer.Normalize("\u0623\u062D\u0645\u062F"), saved.FirstNameNormalized);
        Assert.Equal(ArabicTextNormalizer.Normalize("\u0639\u0644\u064A"), saved.LastNameNormalized);
        Assert.Equal(ArabicTextNormalizer.Normalize(employee.FullName), saved.FullNameNormalized);
        Assert.Equal(ArabicTextNormalizer.RuleVersion, saved.NormalizationVersion);
        Assert.Equal("\u0623\u062D\u0645\u062F", saved.FirstName); // original preserved
    }

    [Fact]
    public async Task Ahmed_variants_normalize_equal_on_save()
    {
        await using var db = CreateDb();
        var withHamza = NewEmployee("1000000002", "\u0623\u062D\u0645\u062F", "\u0645\u062D\u0645\u062F"); // أحمد محمد
        var bareAlef = NewEmployee("1000000003", "\u0627\u062D\u0645\u062F", "\u0645\u062D\u0645\u062F"); // احمد محمد

        db.Employees.AddRange(withHamza, bareAlef);
        await db.SaveChangesAsync();

        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.NationalId == "1000000002" || e.NationalId == "1000000003")
            .OrderBy(e => e.NationalId)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].FirstNameNormalized, rows[1].FirstNameNormalized);
        Assert.Equal("\u0627\u062D\u0645\u062F", rows[0].FirstNameNormalized);
        Assert.True(ArabicTextNormalizer.AreEquivalent(rows[0].FirstName, rows[1].FirstName));
    }

    [Fact]
    public async Task Lookup_style_query_finds_both_via_normalized_column()
    {
        await using var db = CreateDb();
        db.Employees.Add(NewEmployee("1000000004", "\u0623\u062D\u0645\u062F", "\u0627\u0644\u062E\u0637\u064A\u0628")); // أحمد الخطيب
        db.Employees.Add(NewEmployee("1000000005", "\u0627\u062D\u0645\u062F", "\u0627\u0644\u062E\u0637\u064A\u0628")); // احمد الخطيب
        await db.SaveChangesAsync();

        var term = "\u0623\u062D\u0645\u062F"; // search with hamza form
        var normalized = ArabicTextNormalizer.Normalize(term);

        var matches = await db.Employees.AsNoTracking()
            .Where(e =>
                e.FullNameNormalized.Contains(normalized) ||
                e.FullName.Contains(term))
            .OrderBy(e => e.FullNameNormalized)
            .ThenBy(e => e.FullName)
            .Select(e => e.NationalId)
            .ToListAsync();

        Assert.Equal(2, matches.Count);
        Assert.Contains("1000000004", matches);
        Assert.Contains("1000000005", matches);
    }

    private HRCoreDbContext CreateDb() => new(_options, new FixedTenant(TenantId));

    private static Employee NewEmployee(string nationalId, string firstName, string lastName) =>
        new()
        {
            TenantId = TenantId,
            NationalId = nationalId,
            FirstName = firstName,
            LastName = lastName,
            FullName = $"{firstName} {lastName}",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Male,
            CurrentStatus = EmploymentStatus.Active
        };

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}

using HRManagement.SharedKernel.MasterData;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class Mdm001ReferenceDataTests
{
    [Fact]
    public void CatalogKind_has_twelve_values()
    {
        var values = Enum.GetValues<CatalogKind>();
        Assert.Equal(12, values.Length);
    }

    [Theory]
    [InlineData("JobClassification")]
    [InlineData("Grade")]
    [InlineData("Geography")]
    [InlineData("Agency")]
    [InlineData("Qualification")]
    [InlineData("Bank")]
    [InlineData("Currency")]
    [InlineData("SalaryComponent")]
    [InlineData("DocumentType")]
    [InlineData("ContractType")]
    [InlineData("MovementType")]
    [InlineData("ServiceType")]
    public void CatalogKind_parses_all_expected_kinds(string name)
    {
        Assert.True(Enum.TryParse<CatalogKind>(name, out _), $"CatalogKind should contain {name}");
    }

    [Fact]
    public void ReferenceCatalogEntry_defaults_are_correct()
    {
        var entry = new ReferenceCatalogEntry();
        Assert.True(entry.IsActive);
        Assert.Equal(1, entry.Version);
        Assert.Equal(string.Empty, entry.Code);
        Assert.Equal(string.Empty, entry.NameAr);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    [Fact]
    public void Lifecycle_deactivate_sets_fields()
    {
        var entry = new ReferenceCatalogEntry
        {
            CatalogKind = CatalogKind.Grade,
            Code = "GR-01",
            NameAr = "الدرجة الأولى",
            IsActive = true,
            Version = 1
        };

        var (success, error) = ReferenceDataLifecycleService.TryDeactivate(entry, "GR-02");

        Assert.True(success);
        Assert.Null(error);
        Assert.False(entry.IsActive);
        Assert.Equal("GR-02", entry.ReplacedByCode);
        Assert.NotNull(entry.ValidTo);
        Assert.Equal(2, entry.Version);
    }

    [Fact]
    public void Lifecycle_deactivate_already_inactive_fails()
    {
        var entry = new ReferenceCatalogEntry { IsActive = false };

        var (success, error) = ReferenceDataLifecycleService.TryDeactivate(entry, null);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void Lifecycle_reactivate_sets_fields()
    {
        var entry = new ReferenceCatalogEntry
        {
            IsActive = false,
            ValidTo = DateTime.UtcNow.AddDays(-1),
            ReplacedByCode = "OLD",
            Version = 2
        };

        var (success, error) = ReferenceDataLifecycleService.TryReactivate(entry);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(entry.IsActive);
        Assert.Null(entry.ValidTo);
        Assert.Null(entry.ReplacedByCode);
        Assert.Equal(3, entry.Version);
    }

    [Fact]
    public void Lifecycle_reactivate_already_active_fails()
    {
        var entry = new ReferenceCatalogEntry { IsActive = true };

        var (success, error) = ReferenceDataLifecycleService.TryReactivate(entry);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void Lifecycle_validate_dates_rejects_inverted_range()
    {
        var entry = new ReferenceCatalogEntry
        {
            ValidFrom = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var (success, error) = ReferenceDataLifecycleService.ValidateEffectiveDates(entry);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void Lifecycle_validate_dates_accepts_valid_range()
    {
        var entry = new ReferenceCatalogEntry
        {
            ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        var (success, _) = ReferenceDataLifecycleService.ValidateEffectiveDates(entry);

        Assert.True(success);
    }

    [Fact]
    public void IsEffectiveAt_returns_true_when_within_range()
    {
        var entry = new ReferenceCatalogEntry
        {
            IsActive = true,
            ValidFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(ReferenceDataLifecycleService.IsEffectiveAt(entry, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void IsEffectiveAt_returns_false_when_inactive()
    {
        var entry = new ReferenceCatalogEntry { IsActive = false };
        Assert.False(ReferenceDataLifecycleService.IsEffectiveAt(entry, DateTime.UtcNow));
    }

    [Fact]
    public void IsEffectiveAt_returns_false_before_valid_from()
    {
        var entry = new ReferenceCatalogEntry
        {
            IsActive = true,
            ValidFrom = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.False(ReferenceDataLifecycleService.IsEffectiveAt(entry, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void IsEffectiveAt_returns_false_after_valid_to()
    {
        var entry = new ReferenceCatalogEntry
        {
            IsActive = true,
            ValidTo = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.False(ReferenceDataLifecycleService.IsEffectiveAt(entry, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Mdm001_docs_and_scripts_exist()
    {
        var repoRoot = FindRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(repoRoot, "docs", "MDM_001_REFERENCE_DATA.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(repoRoot, "docker", "seed", "mdm-reference-seed.sql")));
        Assert.True(System.IO.File.Exists(Path.Combine(repoRoot, "scripts", "mdm001-migrate-freetext-to-codes.sql")));
        Assert.True(System.IO.File.Exists(Path.Combine(repoRoot, "scripts", "mdm001-reference-validate.ps1")));
    }

    [Fact]
    public void EmploymentRecord_has_JobClassificationCode_property()
    {
        var prop = typeof(HR.Core.Domain.Entities.EmploymentRecord)
            .GetProperty("JobClassificationCode");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
    }

    [Fact]
    public void CareerPathStep_has_JobClassificationCode_property()
    {
        var prop = typeof(HR.Core.Domain.Entities.CareerPathStep)
            .GetProperty("JobClassificationCode");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "MDM_001_REFERENCE_DATA.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}

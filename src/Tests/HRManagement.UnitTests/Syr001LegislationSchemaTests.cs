using System.Reflection;
using HR.Core.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Syr001LegislationSchemaTests
{
    [Fact]
    public void LegislationSource_has_expected_properties()
    {
        var props = typeof(LegislationSource).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Contains("LegalSourceId", props);
        Assert.Contains("InstrumentType", props);
        Assert.Contains("Number", props);
        Assert.Contains("TitleAr", props);
        Assert.Contains("TitleEn", props);
        Assert.Contains("IssueDate", props);
        Assert.Contains("EffectiveDate", props);
        Assert.Contains("PublicationDate", props);
        Assert.Contains("Status", props);
        Assert.Contains("Scope", props);
        Assert.Contains("Owner", props);
        Assert.Contains("CertifiedTextHash", props);
        Assert.Contains("RegisterVersion", props);
        Assert.Contains("KeyArticles", props);
        Assert.Contains("OutgoingLinks", props);
        Assert.Contains("IncomingLinks", props);
        Assert.Contains("Aliases", props);
    }

    [Fact]
    public void RecruitmentRequisition_has_LegalSource_navigation()
    {
        var prop = typeof(RecruitmentRequisition).GetProperty(nameof(RecruitmentRequisition.LegalSource));
        Assert.NotNull(prop);
        Assert.Equal(typeof(LegislationSource), prop!.PropertyType);
    }

    [Fact]
    public void OfferLetter_has_LegalSource_navigation()
    {
        var prop = typeof(OfferLetter).GetProperty(nameof(OfferLetter.LegalSource));
        Assert.NotNull(prop);
        Assert.Equal(typeof(LegislationSource), prop!.PropertyType);
    }

    [Fact]
    public void TenantEntityClassification_treats_legislation_as_global()
    {
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(LegislationSource)));
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(LegislationKeyArticle)));
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(LegislationInstrumentLink)));
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(LegislationSourceAlias)));
        Assert.True(TenantEntityClassification.IsTenantOwned(typeof(RecruitmentRequisition)));
    }

    [Fact]
    public void HRCoreDbContextModelSnapshot_contains_LegislationSources()
    {
        var snapshotPath = RepoPaths.Resolve(
            "src", "Services", "HRCore", "HR.Core.Infrastructure", "Migrations", "HRCoreDbContextModelSnapshot.cs");
        var content = System.IO.File.ReadAllText(snapshotPath);

        Assert.Contains("LegislationSources", content);
        Assert.Contains("LegislationKeyArticles", content);
        Assert.Contains("LegislationInstrumentLinks", content);
        Assert.Contains("LegislationSourceAliases", content);
        Assert.Contains("HRManagement.SharedKernel.Legal.LegislationSource", content);
        Assert.Contains(".HasForeignKey(\"LegalSourceId\")", content);
    }

    [Fact]
    public void Syr001_migration_file_exists()
    {
        var migrationsDir = Path.Combine(
            RepoPaths.Root,
            "src", "Services", "HRCore", "HR.Core.Infrastructure", "Migrations");
        var migration = Directory.GetFiles(migrationsDir, "*SYR001_LegislationRegister.cs")
            .SingleOrDefault();

        Assert.NotNull(migration);
        var content = System.IO.File.ReadAllText(migration!);
        Assert.Contains("CreateTable", content);
        Assert.Contains("LegislationSources", content);
        Assert.Contains("FK_RecruitmentRequisitions_LegislationSources_LegalSourceId", content);
    }
}

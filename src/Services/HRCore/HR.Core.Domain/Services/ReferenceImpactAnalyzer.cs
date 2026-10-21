using HRManagement.SharedKernel.MasterData;

namespace HR.Core.Domain.Services;

/// <summary>MDM-001: analyses downstream impact before publishing a reference data change.</summary>
public static class ReferenceImpactAnalyzer
{
    public record ImpactResult(string Entity, int AffectedCount);

    public delegate Task<int> CountByCodeAsync(string entityName, string code, CancellationToken ct);

    /// <summary>
    /// Counts rows in dependent tables that reference the given entry's code.
    /// The <paramref name="countByCode"/> delegate is supplied by the infrastructure layer.
    /// </summary>
    public static async Task<IReadOnlyList<ImpactResult>> AnalyzeAsync(
        ReferenceCatalogEntry entry, CountByCodeAsync countByCode, CancellationToken ct = default)
    {
        var results = new List<ImpactResult>();

        if (entry.CatalogKind == CatalogKind.Grade)
        {
            var jobCatalogCount = await countByCode("JobCatalogEntry.GradeCode", entry.Code, ct);
            if (jobCatalogCount > 0)
                results.Add(new ImpactResult("JobCatalogEntry", jobCatalogCount));
        }

        if (entry.CatalogKind == CatalogKind.JobClassification)
        {
            var jobCatalogCount = await countByCode("JobCatalogEntry.OccupationalGroup", entry.Code, ct);
            if (jobCatalogCount > 0)
                results.Add(new ImpactResult("JobCatalogEntry", jobCatalogCount));

            var employmentCount = await countByCode("EmploymentRecord", entry.Code, ct);
            if (employmentCount > 0)
                results.Add(new ImpactResult("EmploymentRecord", employmentCount));

            var careerCount = await countByCode("CareerPathStep", entry.Code, ct);
            if (careerCount > 0)
                results.Add(new ImpactResult("CareerPathStep", careerCount));
        }

        return results;
    }
}

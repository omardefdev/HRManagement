using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Core.Infrastructure;

/// <summary>L10N-002: stamps Arabic normalized name columns on Employee save.</summary>
public sealed class ArabicNameNormalizationInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampNormalizedNames(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampNormalizedNames(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void StampNormalizedNames(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<Employee>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var employee = entry.Entity;
            employee.FirstNameNormalized = ArabicTextNormalizer.Normalize(employee.FirstName);
            employee.MiddleNameNormalized = string.IsNullOrWhiteSpace(employee.MiddleName)
                ? null
                : ArabicTextNormalizer.Normalize(employee.MiddleName);
            employee.LastNameNormalized = ArabicTextNormalizer.Normalize(employee.LastName);
            employee.FullNameNormalized = ArabicTextNormalizer.Normalize(employee.FullName);
            employee.NormalizationVersion = ArabicTextNormalizer.RuleVersion;
        }
    }
}

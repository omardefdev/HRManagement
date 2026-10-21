using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Mvc;

public enum SaveChangesOutcome
{
    Success,
    UniqueViolation,
    ConcurrencyStale,
}

/// <summary>UX-004: catch DB conflicts in MVC controllers and map to ModelState before middleware.</summary>
public static class HrSaveChanges
{
    public static async Task<SaveChangesOutcome> TryAsync(
        DbContext db,
        ModelStateDictionary modelState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return SaveChangesOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            modelState.AddModelError(string.Empty, ConcurrencyConflictMessages.StaleWrite);
            return SaveChangesOutcome.ConcurrencyStale;
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            ApplyUniqueViolation(modelState, ex);
            return SaveChangesOutcome.UniqueViolation;
        }
    }

    public static void ApplyUniqueViolation(ModelStateDictionary modelState, DbUpdateException ex)
    {
        var resolution = UniqueConstraintMessageRegistry.Resolve(ex);
        if (!string.IsNullOrWhiteSpace(resolution.ModelStateKey))
            modelState.AddModelError(resolution.ModelStateKey, resolution.Message);
        else
            modelState.AddModelError(string.Empty, resolution.Message);
    }
}

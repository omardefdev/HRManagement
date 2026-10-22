using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>HCM-029 (A1): result of matching a hire candidate against existing employees.</summary>
/// <param name="NationalIdMatch">Employee with the same national id (by hash) — the hire must link to it.</param>
/// <param name="PossibleMatch">Same normalized full name and birth date — a warning only, never a block.</param>
public sealed record HirePersonMatch(Employee? NationalIdMatch, Employee? PossibleMatch);

/// <summary>
/// HCM-029 (A1): finds an existing employee for a hire. <c>Employee.NationalId</c> is encrypted at rest, so the
/// lookup is by <c>NationalIdHash</c> only (maintained by <see cref="EmployeePiiProtectionInterceptor"/>).
/// </summary>
public sealed class HirePersonMatchService(HRCoreDbContext db)
{
    /// <summary>Query tag on the national-id lookup (lets tests and diagnostics find the command).</summary>
    public const string NationalIdQueryTag = "HCM029:HirePersonMatch";

    public Task<Employee?> FindByNationalIdAsync(string nationalId, CancellationToken ct = default) =>
        FindByNationalIdHashAsync(HireNationalIdRules.HashOf(nationalId), ct);

    public Task<Employee?> FindByNationalIdHashAsync(string? hash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Task.FromResult<Employee?>(null);

        return db.Employees.AsNoTracking()
            .TagWith(NationalIdQueryTag)
            .Where(e => e.NationalIdHash == hash)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Same normalized full name and birth date (calendar day). A possible match only.</summary>
    public Task<Employee?> FindPossibleMatchAsync(string fullName, DateTime birthDate, CancellationToken ct = default)
    {
        var normalized = ArabicTextNormalizer.Normalize(fullName);
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.FromResult<Employee?>(null);

        var day = DateTime.SpecifyKind(birthDate.Date, DateTimeKind.Utc);
        var next = day.AddDays(1);
        return db.Employees.AsNoTracking()
            .Where(e => e.FullNameNormalized == normalized && e.BirthDate >= day && e.BirthDate < next)
            .OrderBy(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// National-id match first; the name + birth-date signal is only evaluated when a birth date is known and is
    /// dropped when it points to the national-id match.
    /// </summary>
    public async Task<HirePersonMatch> MatchAsync(
        string? nationalIdHash,
        string? fullName,
        DateTime? birthDate,
        CancellationToken ct = default)
    {
        var byNationalId = await FindByNationalIdHashAsync(nationalIdHash, ct);

        Employee? possible = null;
        if (!string.IsNullOrWhiteSpace(fullName) && birthDate is DateTime dob)
        {
            possible = await FindPossibleMatchAsync(fullName, dob, ct);
            if (possible is not null && possible.Id == byNationalId?.Id)
                possible = null;
        }

        return new HirePersonMatch(byNationalId, possible);
    }
}

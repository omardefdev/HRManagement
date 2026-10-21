using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.MasterData;

public sealed record ReferenceCatalogListItem(
    ReferenceCatalogEntry Entry,
    bool IsPlatformOwned,
    bool CanMutate);

public interface IReferenceCatalogAdminService
{
    Task<IReadOnlyList<ReferenceCatalogListItem>> ListAsync(
        CatalogKind kind,
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ReferenceCatalogEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(ReferenceCatalogEntry? Entry, string? Error)> CreateAsync(
        CatalogKind kind,
        string code,
        string nameAr,
        string? nameEn,
        DateTime? validFrom,
        DateTime? validTo,
        CancellationToken ct = default);

    Task<(ReferenceCatalogEntry? Entry, string? Error)> UpdateAsync(
        Guid id,
        string nameAr,
        string? nameEn,
        DateTime? validFrom,
        DateTime? validTo,
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> DeactivateAsync(
        Guid id,
        string? replacedByCode,
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> ReactivateAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ReferenceImpactAnalyzer.ImpactResult>> AnalyzeImpactAsync(
        Guid id,
        CancellationToken ct = default);

    bool IsPlatformOwned(ReferenceCatalogEntry entry);

    bool CanMutate(ReferenceCatalogEntry entry);
}

public sealed class ReferenceCatalogAdminService(HRCoreDbContext db) : IReferenceCatalogAdminService
{
    public async Task<IReadOnlyList<ReferenceCatalogListItem>> ListAsync(
        CatalogKind kind,
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.MaxPageSize);

        var merged = await LoadMergedEntriesAsync(kind, ct);
        var filtered = merged.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(e =>
                e.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.NameAr.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (e.NameEn != null && e.NameEn.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        if (isActive is bool active)
            filtered = filtered.Where(e => e.IsActive == active);

        return filtered
            .OrderBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListItem)
            .ToList();
    }

    public async Task<ReferenceCatalogEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = CurrentTenantId();
        var platformId = TenantDefaults.DefaultTenantId;

        return await db.ReferenceCatalogEntries
            .AsNoTracking()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalReferenceLookup)
            .FirstOrDefaultAsync(e =>
                e.Id == id &&
                (e.TenantId == tenantId || (tenantId != platformId && e.TenantId == platformId)),
                ct);
    }

    public async Task<(ReferenceCatalogEntry? Entry, string? Error)> CreateAsync(
        CatalogKind kind,
        string code,
        string nameAr,
        string? nameEn,
        DateTime? validFrom,
        DateTime? validTo,
        CancellationToken ct = default)
    {
        code = code.Trim();
        nameAr = nameAr.Trim();
        nameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();

        if (string.IsNullOrWhiteSpace(code))
            return (null, "رمز المرجع مطلوب.");
        if (string.IsNullOrWhiteSpace(nameAr))
            return (null, "الاسم بالعربية مطلوب.");

        var tenantId = CurrentTenantId();
        var exists = await db.ReferenceCatalogEntries
            .AnyAsync(e => e.TenantId == tenantId && e.CatalogKind == kind && e.Code == code, ct);
        if (exists)
            return (null, $"الرمز '{code}' موجود مسبقاً لهذا النوع.");

        var entry = new ReferenceCatalogEntry
        {
            TenantId = tenantId,
            CatalogKind = kind,
            Code = code,
            NameAr = nameAr,
            NameEn = nameEn,
            ValidFrom = validFrom,
            ValidTo = validTo,
            IsActive = true
        };

        var dateError = ReferenceDataLifecycleService.ValidateEffectiveDates(entry);
        if (!dateError.Success)
            return (null, dateError.Error);

        db.ReferenceCatalogEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return (entry, null);
    }

    public async Task<(ReferenceCatalogEntry? Entry, string? Error)> UpdateAsync(
        Guid id,
        string nameAr,
        string? nameEn,
        DateTime? validFrom,
        DateTime? validTo,
        CancellationToken ct = default)
    {
        var entry = await db.ReferenceCatalogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return (null, "السجل غير موجود.");
        if (!CanMutate(entry))
            return (null, "لا يمكن تعديل سجل المرجع الوطني (المنصة) — أنشئ نسخة محلية برمز جديد.");

        nameAr = nameAr.Trim();
        nameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
        if (string.IsNullOrWhiteSpace(nameAr))
            return (null, "الاسم بالعربية مطلوب.");

        entry.NameAr = nameAr;
        entry.NameEn = nameEn;
        entry.ValidFrom = validFrom;
        entry.ValidTo = validTo;
        entry.Version++;

        var dateError = ReferenceDataLifecycleService.ValidateEffectiveDates(entry);
        if (!dateError.Success)
            return (null, dateError.Error);

        await db.SaveChangesAsync(ct);
        return (entry, null);
    }

    public async Task<(bool Success, string? Error)> DeactivateAsync(
        Guid id,
        string? replacedByCode,
        CancellationToken ct = default)
    {
        var entry = await db.ReferenceCatalogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return (false, "السجل غير موجود.");
        if (!CanMutate(entry))
            return (false, "لا يمكن تعطيل سجل المرجع الوطني (المنصة).");

        var impact = await AnalyzeImpactAsync(id, ct);
        if (impact.Any(i => i.AffectedCount > 0))
        {
            var summary = string.Join("، ", impact.Select(i => $"{i.Entity}: {i.AffectedCount}"));
            return (false, $"لا يمكن التعطيل — السجل مستخدم في: {summary}.");
        }

        replacedByCode = string.IsNullOrWhiteSpace(replacedByCode) ? null : replacedByCode.Trim();
        if (replacedByCode is not null)
        {
            if (string.Equals(replacedByCode, entry.Code, StringComparison.OrdinalIgnoreCase))
                return (false, "لا يمكن استبدال الرمز بنفسه.");

            if (!await ActiveReplacementExistsAsync(entry.CatalogKind, replacedByCode, ct))
                return (false, "رمز الاستبدال غير موجود أو غير نشط.");
        }

        var result = ReferenceDataLifecycleService.TryDeactivate(entry, replacedByCode);
        if (!result.Success)
            return result;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReactivateAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await db.ReferenceCatalogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return (false, "السجل غير موجود.");
        if (!CanMutate(entry))
            return (false, "لا يمكن إعادة تفعيل سجل المرجع الوطني (المنصة).");

        var result = ReferenceDataLifecycleService.TryReactivate(entry);
        if (!result.Success)
            return result;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<IReadOnlyList<ReferenceImpactAnalyzer.ImpactResult>> AnalyzeImpactAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var entry = await db.ReferenceCatalogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
            return [];

        return await ReferenceImpactAnalyzer.AnalyzeAsync(entry, CountByCodeAsync, ct);
    }

    public bool IsPlatformOwned(ReferenceCatalogEntry entry) =>
        entry.TenantId == TenantDefaults.DefaultTenantId;

    public bool CanMutate(ReferenceCatalogEntry entry) =>
        !IsPlatformOwned(entry) && entry.TenantId == CurrentTenantId();

    private ReferenceCatalogListItem ToListItem(ReferenceCatalogEntry entry) =>
        new(entry, IsPlatformOwned(entry), CanMutate(entry));

    private async Task<IReadOnlyList<ReferenceCatalogEntry>> LoadMergedEntriesAsync(
        CatalogKind kind,
        CancellationToken ct)
    {
        var tenantId = CurrentTenantId();
        var platformId = TenantDefaults.DefaultTenantId;

        var query = db.ReferenceCatalogEntries
            .AsNoTracking()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalReferenceLookup)
            .Where(e => e.CatalogKind == kind);

        if (tenantId != platformId)
            query = query.Where(e => e.TenantId == tenantId || e.TenantId == platformId);
        else
            query = query.Where(e => e.TenantId == platformId);

        var all = await query.ToListAsync(ct);
        return all
            .GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(e => e.TenantId == tenantId ? 0 : 1).First())
            .ToList();
    }

    private Guid CurrentTenantId() => db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;

    private async Task<bool> ActiveReplacementExistsAsync(
        CatalogKind kind,
        string code,
        CancellationToken ct)
    {
        var tenantId = CurrentTenantId();
        var platformId = TenantDefaults.DefaultTenantId;

        return await db.ReferenceCatalogEntries
            .AsNoTracking()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalReferenceLookup)
            .AnyAsync(e =>
                e.CatalogKind == kind &&
                e.Code == code &&
                e.IsActive &&
                (e.TenantId == tenantId || (tenantId != platformId && e.TenantId == platformId)),
                ct);
    }

    private Task<int> CountByCodeAsync(string entity, string code, CancellationToken ct) => entity switch
    {
        "JobCatalogEntry.GradeCode" => db.JobCatalogEntries.CountAsync(e => e.GradeCode == code, ct),
        "JobCatalogEntry.OccupationalGroup" => db.JobCatalogEntries.CountAsync(
            e => e.OccupationalGroup == code, ct),
        "EmploymentRecord" => db.EmploymentRecords.CountAsync(r => r.JobClassificationCode == code, ct),
        "CareerPathStep" => db.CareerPathSteps.CountAsync(s => s.JobClassificationCode == code, ct),
        _ => Task.FromResult(0)
    };
}

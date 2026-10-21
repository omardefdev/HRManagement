using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using OrgUnitPathService = HR.Core.Infrastructure.OrgUnitPathService;

namespace HR.Core.Infrastructure.Onboarding;

public sealed class AgencyOrgStructureProvisioner(
    HRCoreDbContext db,
    OrgUnitPathService pathService,
    OrgUnitIntegrationService integration)
{
    public async Task<IReadOnlyList<string>> ApplyAsync(
        Guid tenantId,
        AgencyOrgStructureSection orgStructure,
        IList<string> warnings,
        CancellationToken ct = default)
    {
        if (orgStructure.Units.Count == 0)
            return Array.Empty<string>();

        TenantIdFlow.Set(tenantId);
        try
        {
            var ordered = TopologicalSort(orgStructure.Units);
            var idByCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var def in ordered)
            {
                var code = def.Code.Trim();
                var existing = await db.OrgUnits
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Code == code, ct);

                if (existing is not null)
                {
                    idByCode[code] = existing.Id;
                    warnings.Add($"Org unit {code} already exists — skipped");
                    continue;
                }

                Guid? parentId = null;
                if (!string.IsNullOrWhiteSpace(def.ParentCode))
                {
                    var parentCode = def.ParentCode.Trim();
                    if (!idByCode.TryGetValue(parentCode, out var pid))
                    {
                        var parent = await db.OrgUnits
                            .AsNoTracking()
                            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Code == parentCode, ct);
                        if (parent is null)
                            throw new InvalidOperationException($"Parent org unit {parentCode} not found for {code}");
                        pid = parent.Id;
                    }

                    parentId = pid;
                }

                var unit = new OrgUnit
                {
                    TenantId = tenantId,
                    Code = code,
                    NameAr = def.NameAr,
                    NameEn = def.NameEn,
                    ParentId = parentId
                };
                db.OrgUnits.Add(unit);
                await db.SaveChangesAsync(ct);
                await pathService.AssignPathOnCreateAsync(unit, ct);
                idByCode[code] = unit.Id;
            }

            await pathService.RecomputeAllAsync(ct);
            await integration.PublishAllOrgUnitsAsync(ct);

            return ordered.Select(u => u.Code.Trim()).ToList();
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static List<AgencyOrgUnitDef> TopologicalSort(IReadOnlyList<AgencyOrgUnitDef> units)
    {
        var byCode = units.ToDictionary(u => u.Code.Trim(), StringComparer.OrdinalIgnoreCase);
        var result = new List<AgencyOrgUnitDef>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string code)
        {
            if (visited.Contains(code))
                return;
            visited.Add(code);
            if (!byCode.TryGetValue(code, out var unit))
                return;

            if (!string.IsNullOrWhiteSpace(unit.ParentCode))
                Visit(unit.ParentCode.Trim());

            result.Add(unit);
        }

        foreach (var unit in units)
            Visit(unit.Code.Trim());

        return result;
    }
}

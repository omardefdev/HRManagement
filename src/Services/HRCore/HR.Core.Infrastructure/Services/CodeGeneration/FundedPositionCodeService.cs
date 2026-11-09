using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

public interface IFundedPositionCodeService
{
    Task<FundedPosition> CreateAsync(
        FundedPosition draft,
        CancellationToken ct = default);
}

/// <summary>
/// COD-001 for funded positions. PLAN-48: the position code keeps one counter per (unit, grade) across budget years (D-TEN-6),
/// and the cost centre is the unit's derived code, the same for every position of the unit, issued once (D-TEN-7). A draft built
/// without a tenant (Guid.Empty) is numbered in the session's tenant, never in an empty one.
/// </summary>
public sealed class FundedPositionCodeService(
    HRCoreDbContext db,
    ICodeGenerator generator) : IFundedPositionCodeService
{
    public async Task<FundedPosition> CreateAsync(FundedPosition draft, CancellationToken ct = default)
    {
        var org = await db.OrgUnits.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == draft.OrgUnitId, ct)
            ?? throw new InvalidOperationException("OrgUnit not found");

        Guid? tenantId = draft.TenantId == Guid.Empty ? null : draft.TenantId;
        var positionResult = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.FundedPosition,
            TenantId = tenantId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OrgUnitCode"] = org.Code,
                ["GradeCode"] = draft.GradeCode,
                ["BudgetYear"] = draft.BudgetYear.ToString(CultureInfo.InvariantCulture)
            }
        }, ct);

        var costCenterResult = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.CostCenter,
            TenantId = tenantId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OrgUnitCode"] = org.Code
            }
        }, ct);

        draft.PositionCode = positionResult.Code;
        draft.CostCenter = costCenterResult.Code;
        db.FundedPositions.Add(draft);
        await db.SaveChangesAsync(ct);
        return draft;
    }
}

using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

public interface IFundedPositionCodeService
{
    Task<FundedPosition> CreateAsync(
        FundedPosition draft,
        CancellationToken ct = default);
}

public sealed class FundedPositionCodeService(
    HRCoreDbContext db,
    ICodeGenerator generator) : IFundedPositionCodeService
{
    public async Task<FundedPosition> CreateAsync(FundedPosition draft, CancellationToken ct = default)
    {
        var org = await db.OrgUnits.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == draft.OrgUnitId, ct)
            ?? throw new InvalidOperationException("OrgUnit not found");

        var positionResult = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.FundedPosition,
            TenantId = draft.TenantId,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OrgUnitCode"] = org.Code,
                ["GradeCode"] = draft.GradeCode,
                ["BudgetYear"] = draft.BudgetYear.ToString()
            }
        }, ct);

        var costCenterResult = await generator.GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = CodeEntityKind.CostCenter,
            TenantId = draft.TenantId,
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

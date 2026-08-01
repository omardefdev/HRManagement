using HR.Contracts.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Infrastructure.Data;

public static class ContractsDataSeeder
{
    private static readonly Guid DefaultTypeId = DemoSeedIds.DefaultContractTypeId;
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    public static async Task SeedAsync(ContractsDbContext db, CancellationToken ct = default)
    {
        if (await db.ContractTypes.AnyAsync(t => t.Id == DefaultTypeId, ct))
            return;

        var now = DateTime.UtcNow;
        db.ContractTypes.Add(new ContractType
        {
            Id = DefaultTypeId,
            TenantId = TenantId,
            Code = "DEFAULT",
            NameAr = "عقد افتراضي",
            NameEn = "Default contract",
            Description = "يُنشأ تلقائياً عند تسجيل موظف جديد",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(ct);
    }
}

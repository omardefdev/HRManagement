using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>Two-tenant seed helpers for TEN-003 cross-tenant isolation tests (MOF / MOH).</summary>
public static class TenantTestFixtures
{
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");

    public const string TenantACode = "MOF";
    public const string TenantBCode = "MOH";

    public static Tenant CreateTenantA() => new()
    {
        Id = TenantA,
        Code = TenantACode,
        NameAr = "وزارة المالية",
        NameEn = "Ministry of Finance",
        IsActive = true
    };

    public static Tenant CreateTenantB() => new()
    {
        Id = TenantB,
        Code = TenantBCode,
        NameAr = "وزارة الصحة",
        NameEn = "Ministry of Health",
        IsActive = true
    };

    public static OrgUnit CreateRootOrgUnit(Guid tenantId, string code, string nameAr)
    {
        var id = Guid.NewGuid();
        return new OrgUnit
        {
            Id = id,
            TenantId = tenantId,
            Code = code,
            NameAr = nameAr,
            MaterializedPath = $"/{id:N}/"
        };
    }

    public static OrgUnit CreateChildOrgUnit(Guid tenantId, OrgUnit parent, string code, string nameAr)
    {
        var id = Guid.NewGuid();
        return new OrgUnit
        {
            Id = id,
            TenantId = tenantId,
            ParentId = parent.Id,
            Code = code,
            NameAr = nameAr,
            MaterializedPath = $"{parent.MaterializedPath}{id:N}/"
        };
    }

    public static Employee CreateEmployee(Guid tenantId, Guid? orgUnitId, string nationalSuffix, string fullName)
    {
        return new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PrimaryOrgUnitId = orgUnitId,
            FullName = fullName,
            FirstName = fullName.Split(' ').FirstOrDefault() ?? fullName,
            LastName = fullName.Split(' ').LastOrDefault() ?? "X",
            NationalId = $"N{nationalSuffix}"[..Math.Min(20, $"N{nationalSuffix}".Length)],
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = Gender.Male,
            CurrentStatus = EmploymentStatus.Active,
            HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    public static LeaveRequest CreateLeaveRequest(Guid tenantId, Guid employeeId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        LeaveType = LeaveType.Annual,
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 1, 5),
        DurationDays = 5,
        Status = ApprovalStatus.Pending
    };

    public static PayrollRun CreatePayrollRun(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
        RunDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        Status = PayrollRunStatus.Draft,
        RunType = PayrollRunType.Regular
    };

    /// <summary>
    /// Seeds overlapping branch shapes under each tenant (root + nested branch)
    /// to exercise nested-branch + tenant isolation together.
    /// </summary>
    public static (OrgUnit RootA, OrgUnit BranchA, OrgUnit RootB, OrgUnit BranchB, Employee EmpA, Employee EmpB)
        CreateOverlappingBranchPair()
    {
        var rootA = CreateRootOrgUnit(TenantA, TenantACode, "وزارة المالية");
        var branchA = CreateChildOrgUnit(TenantA, rootA, "MOF-FIN", "مديرية المالية");
        var rootB = CreateRootOrgUnit(TenantB, TenantBCode, "وزارة الصحة");
        var branchB = CreateChildOrgUnit(TenantB, rootB, "MOH-FIN", "مديرية المالية"); // overlapping branch name/code pattern

        var empA = CreateEmployee(TenantA, branchA.Id, Guid.NewGuid().ToString("N"), "موظف مالية");
        var empB = CreateEmployee(TenantB, branchB.Id, Guid.NewGuid().ToString("N"), "موظف صحة");

        return (rootA, branchA, rootB, branchB, empA, empB);
    }
}

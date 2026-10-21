namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Default SoD matrix and conflicting role pairs used for seed data and in-memory validation (IAM-003).
/// CreatedAt values are fixed so EF HasData does not churn pending model changes on every build (DB-001).
/// </summary>
public static class SodMatrixDefaults
{
    public const decimal DefaultPayrollThreshold = 50_000_000m;
    public const decimal DefaultAdvanceThreshold = 5_000_000m;
    public const int MaxDelegationDays = 90;

    /// <summary>Stable seed timestamp aligned with IAM-003 / FILE-001 model snapshot.</summary>
    public static readonly DateTime SeedCreatedAt =
        new DateTime(2026, 7, 28, 16, 26, 11, 642, DateTimeKind.Utc).AddTicks(5675);

    public static readonly string[] PrivilegedRoles =
    [
        "hr-admin",
        "platform-admin",
        "payroll-poster"
    ];

    public static IReadOnlyList<SodRule> CreateDefaultRules() =>
    [
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000001"),
            EntityType = "PayrollRun",
            MakerRole = "payroll-processor",
            CheckerRole = "payroll-approver",
            PosterRole = "payroll-poster",
            ReverserRole = "payroll-poster",
            RequireDifferentActors = true,
            FinancialThreshold = DefaultPayrollThreshold,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000002"),
            EntityType = "LeaveRequest",
            MakerRole = "hr-contracts",
            CheckerRole = "hr-contracts",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5770)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000003"),
            EntityType = "AdvanceRequest",
            MakerRole = "hr-contracts",
            CheckerRole = "hr-contracts",
            RequireDifferentActors = true,
            FinancialThreshold = DefaultAdvanceThreshold,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5798)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000004"),
            EntityType = "TransferRequest",
            MakerRole = "hr-movements",
            CheckerRole = "hr-management-approver",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5801)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000014"),
            EntityType = "SecondmentRequest",
            MakerRole = "hr-movements",
            CheckerRole = "hr-management-approver",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5810)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000015"),
            EntityType = "LoanAssignmentRequest",
            MakerRole = "hr-movements",
            CheckerRole = "hr-management-approver",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5813)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000005"),
            EntityType = "ApprovalRequest",
            MakerRole = "hr-core",
            CheckerRole = "hr-management-approver",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5804)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000006"),
            EntityType = "RoleAssignment",
            MakerRole = "auth-directory",
            CheckerRole = "hr-admin",
            RequireDifferentActors = true,
            RequiredApprovalCount = 2,
            CreatedAt = SeedCreatedAt.AddTicks(5807)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000007"),
            EntityType = "LegalDataChange",
            MakerRole = "hr-core",
            CheckerRole = "hr-management-approver",
            RequireDifferentActors = true,
            RequiredApprovalCount = 2,
            CreatedAt = SeedCreatedAt.AddTicks(5810)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000008"),
            EntityType = "EosSettlement",
            MakerRole = "payroll-approver",
            CheckerRole = "payroll-poster",
            RequireDifferentActors = true,
            FinancialThreshold = DefaultPayrollThreshold,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5813)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000016"),
            EntityType = "AssetAssignment",
            MakerRole = "admin-logistics",
            CheckerRole = "hr-management-approver",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5816)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000017"),
            EntityType = "SupplyOrder",
            MakerRole = "admin-logistics",
            CheckerRole = "admin-logistics",
            RequireDifferentActors = true,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5819)
        },
        new SodRule
        {
            Id = Guid.Parse("a1000001-0001-4000-8000-000000000018"),
            EntityType = "PaymentBatch",
            MakerRole = "payroll-poster",
            CheckerRole = "payroll-approver",
            PosterRole = "payroll-poster",
            RequireDifferentActors = true,
            FinancialThreshold = DefaultPayrollThreshold,
            RequiredApprovalCount = 1,
            CreatedAt = SeedCreatedAt.AddTicks(5822)
        }
    ];

    public static IReadOnlyList<ConflictingRolePair> CreateDefaultConflicts() =>
    [
        new ConflictingRolePair
        {
            Id = Guid.Parse("b2000002-0002-4000-8000-000000000001"),
            RoleA = "payroll-processor",
            RoleB = "payroll-approver",
            Reason = "لا يجوز الجمع بين إنشاء تشغيل الرواتب واعتماده",
            CreatedAt = new DateTime(2026, 7, 28, 16, 26, 11, 644, DateTimeKind.Utc).AddTicks(7373)
        },
        new ConflictingRolePair
        {
            Id = Guid.Parse("b2000002-0002-4000-8000-000000000002"),
            RoleA = "payroll-approver",
            RoleB = "payroll-poster",
            Reason = "لا يجوز الجمع بين اعتماد التشغيل وترحيله",
            CreatedAt = new DateTime(2026, 7, 28, 16, 26, 11, 644, DateTimeKind.Utc).AddTicks(8030)
        },
        new ConflictingRolePair
        {
            Id = Guid.Parse("b2000002-0002-4000-8000-000000000003"),
            RoleA = "payroll-processor",
            RoleB = "payroll-poster",
            Reason = "لا يجوز الجمع بين إنشاء التشغيل وترحيله",
            CreatedAt = new DateTime(2026, 7, 28, 16, 26, 11, 644, DateTimeKind.Utc).AddTicks(8033)
        },
        new ConflictingRolePair
        {
            Id = Guid.Parse("b2000002-0002-4000-8000-000000000004"),
            RoleA = "hr-core",
            RoleB = "hr-management-approver",
            Reason = "كاتب بيانات الموارد البشرية لا يعتمد قرارات الإدارة العليا لنفس الكيان",
            CreatedAt = new DateTime(2026, 7, 28, 16, 26, 11, 644, DateTimeKind.Utc).AddTicks(8035)
        }
    ];
}

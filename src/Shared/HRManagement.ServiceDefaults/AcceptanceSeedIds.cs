using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Deterministic IDs for acceptance seed (PLAN-00). Shared across HR.Core, Payroll, and acceptance tests.
/// </summary>
public static class AcceptanceSeedIds
{
    public const string AgencyACode = "AGENCY-A";
    public const string AgencyBCode = "AGENCY-B";
    public const string AgencyCCode = "AGENCY-C";

    public static readonly Guid AgencyAId = AcceptanceSeedCatalog.AgencyAId;
    public static readonly Guid AgencyBId = AcceptanceSeedCatalog.AgencyBId;
    public static readonly Guid AgencyCId = AcceptanceSeedCatalog.AgencyCId;

    public static readonly Guid HqOrgUnitId = AcceptanceSeedCatalog.AgencyAHqId;
    public static readonly Guid Br1OrgUnitId = AcceptanceSeedCatalog.AgencyARifDimashqId;
    public static readonly Guid Br1D1OrgUnitId = AcceptanceSeedCatalog.AgencyARifDimashqHrId;
    public static readonly Guid Br2OrgUnitId = AcceptanceSeedCatalog.AgencyAAleppoId;
    public static readonly Guid HqbOrgUnitId = AcceptanceSeedCatalog.AgencyBHqId;

    public static readonly Guid EmpBr1Id = AcceptanceSeedCatalog.LegacyEmployeeBr1Id;
    public static readonly Guid EmpD1Id = AcceptanceSeedCatalog.LegacyEmployeeBr1ChildId;
    public static readonly Guid EmpBr2Id = AcceptanceSeedCatalog.LegacyEmployeeBr2Id;
    public static readonly Guid EmpBId = AcceptanceSeedCatalog.LegacyEmployeeBId;

    public static readonly Guid PolicyTemplateId = Guid.Parse("a0000001-0000-4000-8000-000000000301");
    public static readonly Guid PolicyVersion1Id = Guid.Parse("a0000001-0000-4000-8000-000000000302");
    public static readonly Guid PolicyVersion2Id = Guid.Parse("a0000001-0000-4000-8000-000000000303");
    public static readonly Guid PolicyVersion3DraftId = Guid.Parse("a0000001-0000-4000-8000-000000000304");

    public static readonly Guid AcceptancePayrollRunId = Guid.Parse("a0000001-0000-4000-8000-000000000401");
    public static readonly Guid Br1FundedPositionId = Guid.Parse("a0000001-0000-4000-8000-000000000501");
    public static readonly Guid Br1RecruitmentRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000502");
    public static readonly Guid Br1PublishedJobPostingId = Guid.Parse("a0000001-0000-4000-8000-000000000503");
    public static readonly Guid Br1InternalVacancyId = Guid.Parse("a0000001-0000-4000-8000-000000000504");
    public static readonly Guid Br1InternalApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000505");

    public static readonly Guid AcceptanceAssetBr1Id = Guid.Parse("a0000001-0000-4000-8000-000000000601");
    public static readonly Guid AcceptanceAssetBr1AssignmentId = Guid.Parse("a0000001-0000-4000-8000-000000000602");
    public const string AcceptanceAssetBr1Tag = "ACCEPT-ASSET-BR1";

    /// <summary>PLAN-17 AC-EST-10..12: approved BR1 workforce plan.</summary>
    public static readonly Guid Br1WorkforcePlanId = Guid.Parse("a0000001-0000-4000-8000-000000000701");

    /// <summary>Optional BR2 plan for negative scope scenarios.</summary>
    public static readonly Guid Br2WorkforcePlanId = Guid.Parse("a0000001-0000-4000-8000-000000000702");

    public static readonly Guid Br1FinanceBudgetBatchId = Guid.Parse("a0000001-0000-4000-8000-000000000703");

    public const string Br1SampleCostCenter = "CC-BR1-01";

    public const string NationalIdBr1 = "99100000327";
    public const string NationalIdD1 = "99100000328";
    public const string NationalIdBr2 = "99100000598";
    public const string NationalIdB = "99200000001";
}

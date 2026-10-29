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

    /// <summary>PLAN-28: Agency A published requisition with RequiresCommitteeApproval = true.</summary>
    public static readonly Guid CommitteeRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000510");

    /// <summary>PLAN-28: its unsubmitted Ranking committee session (chair + 3 members, no votes/decisions).</summary>
    public static readonly Guid CommitteeSessionId = Guid.Parse("a0000001-0000-4000-8000-000000000511");

    public const string CommitteeRequisitionNumber = "REQ-2026-A-CMT1";

    /// <summary>PLAN-30 (AC-HCM-30a–e/h): agency A published requisition with eligibility criteria and required documents.</summary>
    public static readonly Guid CandidateExperienceRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000512");
    public static readonly Guid CandidateExperiencePostingId = Guid.Parse("a0000001-0000-4000-8000-000000000513");
    public const string CandidateExperienceRequisitionNumber = "REQ-2026-A-CX01";

    /// <summary>PLAN-30 (AC-HCM-30j): unlinked citizen rows on that requisition (Received, UnderReview) for the backfill.</summary>
    public static readonly Guid CandidateBackfillCitizenReceivedId = Guid.Parse("a0000001-0000-4000-8000-000000000514");
    public static readonly Guid CandidateBackfillCitizenUnderReviewId = Guid.Parse("a0000001-0000-4000-8000-000000000515");

    /// <summary>PLAN-30 (AC-HCM-30i): cancelled requisition with a due pair (one document) and a legal-hold twin.</summary>
    public static readonly Guid CandidateRetentionRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000516");
    public static readonly Guid CandidateRetentionDueApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000517");
    public static readonly Guid CandidateRetentionDueCitizenId = Guid.Parse("a0000001-0000-4000-8000-000000000518");
    public static readonly Guid CandidateRetentionHeldApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000519");
    public static readonly Guid CandidateRetentionHeldCitizenId = Guid.Parse("a0000001-0000-4000-8000-000000000520");
    public static readonly Guid CandidateRetentionDueDocumentId = Guid.Parse("a0000001-0000-4000-8000-000000000521");
    public static readonly Guid CandidateRetentionDueFileId = Guid.Parse("a0000001-0000-4000-8000-000000000522");
    public const string CandidateRetentionRequisitionNumber = "REQ-2022-A-RET1";

    /// <summary>PLAN-31 (AC-HCM-31a): three approved, unpublished requisitions (exam criteria) to build a competition in the UI.</summary>
    public static readonly Guid CompetitionDraftRequisition1Id = Guid.Parse("a0000001-0000-4000-8000-000000000523");
    public static readonly Guid CompetitionDraftRequisition2Id = Guid.Parse("a0000001-0000-4000-8000-000000000524");
    public static readonly Guid CompetitionDraftRequisition3Id = Guid.Parse("a0000001-0000-4000-8000-000000000525");

    /// <summary>
    /// PLAN-31 (AC-HCM-31b–g): competition at IntakeClosed — lines 3 + 2 (published requisitions …530–…534), 120 admitted
    /// applicants, two planned written sessions, a posting, and two applicants linked to citizen rows for /careers/my.
    /// </summary>
    public static readonly Guid CompetitionId = Guid.Parse("a0000001-0000-4000-8000-000000000526");
    public static readonly Guid CompetitionLine1Id = Guid.Parse("a0000001-0000-4000-8000-000000000527");
    public static readonly Guid CompetitionLine2Id = Guid.Parse("a0000001-0000-4000-8000-000000000528");
    public static readonly Guid CompetitionPostingId = Guid.Parse("a0000001-0000-4000-8000-000000000529");
    public static readonly Guid CompetitionPortalCitizen1Id = Guid.Parse("a0000001-0000-4000-8000-000000000535");
    public static readonly Guid CompetitionPortalCitizen2Id = Guid.Parse("a0000001-0000-4000-8000-000000000536");
    public static readonly Guid CompetitionSession1Id = Guid.Parse("a0000001-0000-4000-8000-000000000537");
    public static readonly Guid CompetitionSession2Id = Guid.Parse("a0000001-0000-4000-8000-000000000538");
    public const string CompetitionNumber = "CMP-2026-A-AC31";
    public const int CompetitionApplicantCount = 120;

    /// <summary>The <paramref name="index"/>-th (0-based) seeded applicant of the AC-HCM-31 competition (0 and 1 have citizen rows).</summary>
    public static Guid CompetitionApplicantId(int index) =>
        Seeding.AcceptanceSeedCatalog.DeterministicGuid(AgencyACode, "competition-applicant-" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The <paramref name="index"/>-th (0-based) published requisition of the AC-HCM-31 competition (…530–…534).</summary>
    public static Guid CompetitionRequisitionId(int index) =>
        Guid.Parse("a0000001-0000-4000-8000-" + (530 + index).ToString("D12", System.Globalization.CultureInfo.InvariantCulture));

    public static readonly Guid AcceptanceAssetBr1Id = Guid.Parse("a0000001-0000-4000-8000-000000000601");
    public static readonly Guid AcceptanceAssetBr1AssignmentId = Guid.Parse("a0000001-0000-4000-8000-000000000602");
    public const string AcceptanceAssetBr1Tag = "ACCEPT-ASSET-BR1";

    /// <summary>PLAN-17 AC-EST-10..12: approved BR1 workforce plan.</summary>
    public static readonly Guid Br1WorkforcePlanId = Guid.Parse("a0000001-0000-4000-8000-000000000701");

    /// <summary>Optional BR2 plan for negative scope scenarios.</summary>
    public static readonly Guid Br2WorkforcePlanId = Guid.Parse("a0000001-0000-4000-8000-000000000702");

    public static readonly Guid Br1FinanceBudgetBatchId = Guid.Parse("a0000001-0000-4000-8000-000000000703");

    public const string Br1SampleCostCenter = "CC-BR1-01";

    /// <summary>PLAN-32 AC-HCM-32: RIF requisition closed by a hire (funnel / time-to-fill fixture).</summary>
    public static readonly Guid RecruitmentReportsRequisition1Id = Guid.Parse("a0000001-0000-4000-8000-000000000801");
    /// <summary>PLAN-32 AC-HCM-32: Aleppo requisition (out of br1-mgr's scope).</summary>
    public static readonly Guid RecruitmentReportsRequisition2Id = Guid.Parse("a0000001-0000-4000-8000-000000000802");
    /// <summary>PLAN-32 AC-HCM-32d: RIF requisition whose approval step is past its SLA.</summary>
    public static readonly Guid RecruitmentReportsRequisition3Id = Guid.Parse("a0000001-0000-4000-8000-000000000803");
    public static readonly Guid RecruitmentReportsWorkflowInstanceId = Guid.Parse("a0000001-0000-4000-8000-000000000804");
    public const string RecruitmentReportsRequisition1Number = "REQ-2026-A-AC32-1";
    public const string RecruitmentReportsRequisition2Number = "REQ-2026-A-AC32-2";
    public const string RecruitmentReportsRequisition3Number = "REQ-2026-A-AC32-3";
    public const string RecruitmentReportsApplicantPrefix = "مرشح تقرير AC32";
    /// <summary>11-digit national ids of the AC-HCM-32 applicants (…01–…09); exports must never contain them.</summary>
    public const string RecruitmentReportsNationalIdPrefix = "320320000";

    /// <summary>
    /// PLAN-33 (AC-HCM-21/22, AC-WF-23): deterministic recruitment states in RIF (br1-mgr's scope), created by
    /// <c>AcceptanceRecruitmentStatesPatch</c> and listed in <c>testdata/acceptance/recruitment-states.json</c>. The
    /// scenarios only read them or are refused on them, so they survive repeated runs.
    /// </summary>
    public static readonly Guid RecruitmentStatesDraftRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000901");
    public static readonly Guid RecruitmentStatesPendingStep1RequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000902");
    public static readonly Guid RecruitmentStatesPendingStep2RequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000903");
    public static readonly Guid RecruitmentStatesApprovedRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000904");
    public static readonly Guid RecruitmentStatesPublishedRequisitionId = Guid.Parse("a0000001-0000-4000-8000-000000000905");
    public static readonly Guid RecruitmentStatesPendingStep1WorkflowId = Guid.Parse("a0000001-0000-4000-8000-000000000906");
    public static readonly Guid RecruitmentStatesPendingStep2WorkflowId = Guid.Parse("a0000001-0000-4000-8000-000000000907");
    public static readonly Guid RecruitmentStatesPublishedPostingId = Guid.Parse("a0000001-0000-4000-8000-000000000908");
    public static readonly Guid RecruitmentStatesCriteriaId = Guid.Parse("a0000001-0000-4000-8000-000000000909");
    public static readonly Guid RecruitmentStatesSubmittedApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000910");
    public static readonly Guid RecruitmentStatesShortlistedApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000911");
    public static readonly Guid RecruitmentStatesAssessedApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000912");
    public static readonly Guid RecruitmentStatesRanked1ApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000913");
    public static readonly Guid RecruitmentStatesRanked2ApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000914");
    public static readonly Guid RecruitmentStatesRanked3ApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000915");
    public static readonly Guid RecruitmentStatesOfferPendingApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000916");
    public static readonly Guid RecruitmentStatesOfferIssuedApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000917");
    public static readonly Guid RecruitmentStatesAppealApplicationId = Guid.Parse("a0000001-0000-4000-8000-000000000918");
    public static readonly Guid RecruitmentStatesPendingOfferId = Guid.Parse("a0000001-0000-4000-8000-000000000919");
    public static readonly Guid RecruitmentStatesPendingOfferWorkflowId = Guid.Parse("a0000001-0000-4000-8000-000000000920");
    public static readonly Guid RecruitmentStatesIssuedOfferId = Guid.Parse("a0000001-0000-4000-8000-000000000921");
    public static readonly Guid RecruitmentStatesPendingAppealId = Guid.Parse("a0000001-0000-4000-8000-000000000922");
    public static readonly Guid RecruitmentStatesPendingAppealWorkflowId = Guid.Parse("a0000001-0000-4000-8000-000000000923");
    public static readonly Guid RecruitmentStatesCommitteeReadySessionId = Guid.Parse("a0000001-0000-4000-8000-000000000924");
    public static readonly Guid RecruitmentStatesCommitteeShortSessionId = Guid.Parse("a0000001-0000-4000-8000-000000000925");
    public static readonly Guid RecruitmentStatesLegacyApprovalRequestId = Guid.Parse("a0000001-0000-4000-8000-000000000926");
    public const string RecruitmentStatesDraftRequisitionNumber = "REQ-2026-A-AC33-DRAFT";
    public const string RecruitmentStatesPendingStep1RequisitionNumber = "REQ-2026-A-AC33-PEND1";
    public const string RecruitmentStatesPendingStep2RequisitionNumber = "REQ-2026-A-AC33-PEND2";
    public const string RecruitmentStatesApprovedRequisitionNumber = "REQ-2026-A-AC33-APPR";
    public const string RecruitmentStatesPublishedRequisitionNumber = "REQ-2026-A-AC33-PUB";
    public const string RecruitmentStatesPendingOfferNumber = "OFF-2026-A-AC33-PEND";
    public const string RecruitmentStatesIssuedOfferNumber = "OFF-2026-A-AC33-ISS";
    public const int RecruitmentStatesPoolSize = 40;

    /// <summary>
    /// The <paramref name="index"/>-th (0-based) vacant RIF position (<c>AC33-POOL-01…40</c>, grade G5) reserved for the
    /// PLAN-33 scenarios that create their own requisition at run time (21a/c, 22a/c/d/e, the E2E journey).
    /// </summary>
    public static Guid RecruitmentStatesPoolPositionId(int index) =>
        Guid.Parse("a0000001-0000-4000-8000-" + (940 + index).ToString("D12", System.Globalization.CultureInfo.InvariantCulture));

    public const string NationalIdBr1 = "99100000327";
    public const string NationalIdD1 = "99100000328";
    public const string NationalIdBr2 = "99100000598";
    public const string NationalIdB = "99200000001";
}

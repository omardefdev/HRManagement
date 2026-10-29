# بطاقات القدرات الوظيفية الوطنية

تُعامل `FUN-001..006` كـEpics مظلية. لا تُغلق قبل إغلاق البطاقات الوظيفية المعنية هنا وإثباتها برحلات UAT كاملة.

## PRD-001 — نموذج قدرات المنتج وأنماط الجهات

- **الأولوية/الحجم/المسؤول:** P0-National / XL / Product Council + Agency Representatives
- **النطاق:** capability map لوزارة/محافظة/هيئة/مؤسسة/فرع؛ mandatory/optional/configurable؛ lifecycle من الملاك إلى الأرشيف؛ personas؛ gaps؛ competing systems benchmark.
- **معايير القبول:** كل اختلاف مؤسسي له config أو extension لا fork؛ كل capability مرتبطة بمالك وUAT وSLO؛ لا شاشة تعد capability قبل رحلة أعمال؛ ممثلو أنماط الجهات يوقعون.
- **التحقق:** scenario walkthrough لكل archetype ومقارنة capability-by-capability مع خط الأساس التنافسي.
- **الاعتماديات:** STR-001, STR-003.
- **الحالة:** Done (production capability model binding)
- **أدلة الإغلاق:**
  - [PRD_001_CAPABILITY_MAP.md](../PRD_001_CAPABILITY_MAP.md) / [PRD_001_COMPETITIVE_BASELINE.md](../PRD_001_COMPETITIVE_BASELINE.md)
  - `docs/prd-product/agency-archetypes.json` / `docs/prd-product/capability-map.json` / `docs/prd-product/personas.json`
  - `docs/prd-product/uat-walkthrough-template.md`
  - `src/Shared/HRManagement.ServiceDefaults/ProductCapability/` (`CapabilityRegistry`, `CapabilityGuardMiddleware`, `ProductCapabilityExtensions`)
  - `scripts/prd001-capability-validate.ps1` / `Prd001CapabilityTests`

## MDM-001 — منصة البيانات المرجعية الوطنية

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Data Governance
- **النطاق:** الجهات/الجغرافيا/الوظائف/الدرجات/الخدمة/العقود/المؤهلات/المصارف/العملات/الحركات/مكونات الراتب/الوثائق؛ codes؛ multilingual؛ effective dates؛ stewardship؛ APIs/events.
- **معايير القبول:** code ثابت ومالك ومصدر وvalid-from/to لكل قيمة؛ لا نص حر لبعد وطني؛ الإلغاء والاستبدال يحفظان التاريخ؛ impact analysis قبل النشر؛ consumers متوافقون.
- **التحقق:** referential reconciliation، code lifecycle، compatibility tests، duplicate/conflict scan.
- **الاعتماديات:** GOV-003, PROD-001, TEN-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [MDM_001_REFERENCE_DATA.md](../MDM_001_REFERENCE_DATA.md)
  - `src/Shared/HRManagement.SharedKernel/MasterData/` (`CatalogKind`, `ReferenceCatalogEntry`, `IReferenceDataProvider`)
  - `src/Services/HRCore/HR.Core.Infrastructure/Data/Configurations/MasterData/ReferenceCatalogEntryConfiguration.cs`
  - `src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731160000_MDM001_NationalReferenceCatalogs.cs`
  - `src/Services/HRCore/HR.Core.Web/Controllers/ReferenceDataController.cs`
  - `src/Services/HRCore/HR.Core.Domain/Services/ReferenceDataLifecycleService.cs` / `ReferenceImpactAnalyzer.cs`
  - `src/Shared/HRManagement.MessageContracts/Events/IReferenceDataChanged.cs`
  - `src/Services/HRCore/HR.Core.Infrastructure/Consumers/ReferenceDataChangedConsumer.cs`
  - `docker/seed/mdm-reference-seed.sql` / `scripts/mdm001-migrate-freetext-to-codes.sql`
  - `scripts/mdm001-reference-validate.ps1` / `Mdm001ReferenceDataTests`

## MDM-002 — السجل الذهبي للشخص والموظف الحكومي

- **الأولوية/الحجم/المسؤول:** P1-National / XL / HRCore + Data Governance
- **النطاق:** Person ID مقابل employment IDs؛ identity proof؛ source precedence؛ multi-agency service history؛ merge/unmerge؛ survivorship؛ lineage؛ restricted national view؛ death/correction.
- **معايير القبول:** duplicate identity لا ينشئ بصمت؛ الانتقال يحفظ التاريخ ولا يكشفه لغير المخول؛ merge قابل للعكس؛ كل قيمة لها provenance؛ الوصول الوطني منفصل ومراقب.
- **التحقق:** duplicates/twins/name variants، cross-agency transfer، merge/unmerge، source conflict.
- **الاعتماديات:** MDM-001, MIG-004, TEN-002, EXT-002.
- **الحالة:** Done (golden person record with reversible merge/unmerge)
- **أدلة الإغلاق:**
  - [MDM_002_GOLDEN_RECORD.md](../MDM_002_GOLDEN_RECORD.md)
  - `src/Shared/HRManagement.SharedKernel/MasterData/GoldenPersonRecord.cs` (`GoldenPersonStatus`)
  - `src/Shared/HRManagement.ServiceDefaults/MasterData/PersonMergeService.cs` (`IPersonMergeService`, `PersonMergeService`)
  - `scripts/mdm002-validate.ps1` / `Mdm002GoldenRecordTests`

## ORG-001 — هيكل تنظيمي مؤرخ وإعادة هيكلة مستقبلية

- **الأولوية/الحجم/المسؤول:** P1-National / XL / HRCore + Agency Governance
- **النطاق:** create/merge/split/rename/close/move؛ future effective؛ as-of؛ approvals؛ impact on people/positions/roles/workflows/reports؛ cancel؛ no cycles؛ org snapshots.
- **معايير القبول:** التاريخ لا يعاد كتابته؛ تقارير as-of صحيحة؛ الأثر يعرض قبل الاعتماد؛ التغيير قابل للإلغاء قبل النفاذ؛ نقل subtree ذري ومحكوم.
- **التحقق:** reorganizations معقدة، retro queries، concurrent moves، role/workflow impact.
- **الاعتماديات:** MDM-001, PERF-003, TEN-002.
- **الحالة:** Done (dated org-unit versioning with temporal queries)
- **أدلة الإغلاق:**
  - [ORG_001_DATED_STRUCTURE.md](../ORG_001_DATED_STRUCTURE.md)
  - `src/Services/HRCore/HR.Core.Domain/Entities/OrgUnitVersion.cs` (`OrgOperationType`)
  - `src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731170000_ORG001_DatedOrgStructure.cs`
  - `HRCoreDbContext.OrgUnitVersions` DbSet + EF configuration
  - `scripts/org001-validate.ps1` / `Org001DatedStructureTests`

## EST-001 — الملاك الوظيفي والمراكز الممولة

- **الحالة:** Done (JobCatalogEntry + FundedPosition entities, EstablishmentService, migration, DbContext, docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / HRCore + Finance
- **النطاق:** job catalog/descriptions/competencies؛ position؛ FTE/capacity؛ grade/location/cost center؛ budget year؛ occupied/vacant/frozen؛ temporary establishment؛ approval؛ history.
- **معايير القبول:** لا تعيين/نقل لمركز غير معتمد أو غير ممول؛ الإشغال لا يتجاوز capacity؛ كل تعيين يرتبط بمركز ونسخة ملاك؛ تقارير المعتمد/المشغول/الشاغر تتصالح.
- **التحقق:** concurrent occupancy، fiscal-year rollover، freeze/unfreeze، finance reconciliation.
- **الاعتماديات:** ORG-001, MDM-001, PAY-002.
- **ما أُغلق:**
  - `HR.Core.Domain/Entities/JobCatalogEntry.cs` + `FundedPosition.cs`
  - `HR.Core.Domain/Services/EstablishmentService.cs`
  - `20260731171000_EST001_Establishment.cs` migration
  - [`EST_001_ESTABLISHMENT.md`](../EST_001_ESTABLISHMENT.md)
  - `scripts/est001-validate.ps1`؛ `Est001EstablishmentTests`

## HCM-001 — التوظيف والاختيار وتجربة المرشح

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Recruitment Product
- **النطاق:** requisition من position؛ الإعلان؛ application case؛ screening؛ assessments/interviews/committees؛ conflict of interest؛ ranking؛ offer؛ appeal؛ privacy/retention؛ conversion.
- **معايير القبول:** لا إعلان بلا مركز؛ معايير التقييم versioned وقابلة للتفسير؛ اللجنة والمصالح مدققة؛ المرشح يتابع Case؛ التحويل idempotent ولا يعيد الإدخال.
- **التحقق:** public portal abuse/accessibility، duplicate applications، committee workflow، candidate→employee E2E.
- **الاعتماديات:** EST-001, WF-001, GOV-001, FUN-001.
- **الحالة:** منفذ وظيفياً ومتحقق منه على ملف القبول المختبري (`NationalScope:IsLabScale=true` ومحرك السياسات Off) في 2026-10-29؛ **ليس Done للإنتاج**: ربط مسار الاعتماد في الإنتاج (G-02) وفحص دور الخطوة على الخادم (G-04) مفتوحان في PLAN-25 (Proposed، مؤجلة). لم يُشغَّل CI البعيد على فرع PLAN-33 (لا push).
- **أدلة الإغلاق:**
  - دراسة الفجوات والحالة الفعلية: [docs/HCM_001_GAP_STUDY.md](../HCM_001_GAP_STUDY.md)؛ الوصف الوظيفي: [docs/HCM_001_RECRUITMENT.md](../HCM_001_RECRUITMENT.md)؛ الخطط [PLAN-25](../acceptance-plans/PLAN-25-hcm-production-workflow-binding.md) و[PLAN-33](../acceptance-plans/PLAN-33-hcm-verification-docs.md).
  - النموذج الفعلي في `src/Services/HRCore/HR.Core.Domain/`: الكيانات `Entities/RecruitmentRequisition.cs` و`RecruitmentApplication.cs` و`OfferLetter.cs` و`RecruitmentAppeal.cs` و`RecruitmentAssessmentCriteria.cs` و`RecruitmentAssessmentScore.cs` و`RecruitmentInterviewSlot.cs` و`SelectionCommitteeSession.cs` و`SelectionCommitteeMemberCoi.cs` و`RecruitmentCompetition.cs` و`PublicJobPosting.cs` و`CitizenApplication.cs`؛ والتعدادات `Enums/RequisitionStatus.cs` و`RecruitmentApplicationStatus.cs` و`OfferStatus.cs`.
  - قواعد المجال في `HR.Core.Domain/Services/`: `RequisitionWorkflowRules` و`RecruitmentRequisitionService` و`RequisitionCloseService` و`SelectionWorkflowRules` و`SelectionCommitteeRules` و`OfferWorkflowRules` و`OfferEligibilityRules` و`OfferTermsRules` و`RecruitmentAppealWorkflowRules` و`RecruitmentWorkflowRules` و`CandidateApplicationLegalLifecycleService`؛ والإغلاق الذري للتعيين `HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs`.
  - الحالات الحتمية للقبول: `HR.Core.Infrastructure/Seeding/AcceptanceRecruitmentStatesPatch.cs` + `testdata/acceptance/recruitment-states.json`.
  - اختبارات الوحدة: `Hcm001RecruitmentTests` (اختبارات سلوك على قواعد `HR.Core.Domain` حلت محل اختبارات الانعكاس)، وأجنحة `Hcm026*`..`Hcm032*` في `src/Tests/HRManagement.UnitTests/`، و`Hcm033RecruitmentStatesSeedTests`، وحارس البنية `Architecture/AcceptanceSkipPatternArchitectureTests.cs`.
  - اختبارات التكامل: أجنحة `Hcm026*`..`Hcm032*` و`RecruitmentHireClosureIntegrationTests` في `src/Tests/HRManagement.IntegrationTests/`، و`Hcm033LabScaleBehaviourTests` (6/6) الذي يثبت سلوك G-02: خارج المختبر مع محرك السياسات Off يبقى الطلب والعرض `PendingApproval` بلا مثيل مسار مع تحذير في الواجهة، ويُرفض الاعتماد ولا يصل العرض إلى `Issued`، وتُقفل اللجنة بلا مسار ودون أي تحذير (توقف صامت).
  - اختبارات القبول (`src/Tests/HRManagement.AcceptanceTests/`، إطار `AcceptanceGate`): `AcHcm21RequisitionApprovalUiTests` و`AcHcm22RecruitmentWorkflowsUiTests` و`AcHcm26`..`AcHcm32` و`AcWf23WorkInboxUiTests`، وجميعها `[RequiredAcceptance]`. مع `ACCEPTANCE_REQUIRE_ALL=true`: AC-HCM-21a–d وAC-HCM-22a–e وAC-WF-23a–f Pass (2026-10-29)؛ والأدلة في `acceptance-evidence/AC-HCM-21` و`AC-HCM-22` و`AC-WF-23` (خريطة `scenarios` في `result.json`) و`acceptance-evidence/AC-HCM-26`..`AC-HCM-32` (إعادة تشغيل PLAN-33 P6 في 2026-10-29: AC-HCM-27..32 Pass، وAC-HCM-26 Pass عدا 26h المحجوب (Blocked: لا توجد شخصية مركزية ثانية بدور `hr-core`، وتغطيه `Hcm026EstablishmentCascadeIntegrationTests`)، ولذا فحالة مجلد AC-HCM-26 Blocked).
  - رحلة E2E: `src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs` (`Category=Journey`) من الطلب حتى العقد في HR.Contracts — Pass على ملف القبول (`acceptance-evidence/AC-HCM-33`). المتغير الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml`، `Category=JourneyProdLike`، مستثنى من CI حتى PLAN-25) **Fail** في المرحلة الأولى: الطلب المقدَّم `PendingApproval` بلا مثيل مسار (G-02)؛ الدليل `acceptance-evidence/AC-HCM-33/prodlike`.
  - `scripts/hcm001-validate.ps1` يتحقق من الملفات الفعلية ويمنع عودة `SharedKernel.Recruitment` (نموذج شبحي بلا مستهلك وقت التشغيل، حُذف في PLAN-33).

## HCM-002 — الوقت والحضور والورديات والإضافي

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Time Product + Payroll
- **النطاق:** calendars/shifts/night/cross-midnight؛ holidays؛ biometric/device imports؛ absences؛ correction؛ overtime؛ approval؛ retro effects؛ payroll interface؛ field/offline cases.
- **معايير القبول:** الاستيراد idempotent؛ لا تعديل بلا سبب؛ midnight/DST/holiday صحيح؛ الأثر الرجعي يصل للراتب مرة واحدة؛ الحجم اليومي يحقق SLO.
- **التحقق:** device duplicates/outage، complex shifts، corrections after payroll snapshot، performance.
- **الاعتماديات:** PROD-001, INT-004, PAY-003, MDM-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/HCM_002_ATTENDANCE.md](../HCM_002_ATTENDANCE.md)
  - `src/Shared/HRManagement.SharedKernel/Time/AttendanceRecord.cs` (`AttendanceStatus`, `WorkedMinutes`, `OvertimeMinutes`, `IsPayrollLocked`, `CrossesMidnight`)
  - `src/Shared/HRManagement.SharedKernel/Time/ShiftDefinition.cs` (`ShiftType`, `CrossesMidnight`, `ExpectedWorkMinutes`, `IsEffectiveOn()`)
  - [scripts/hcm002-validate.ps1](../../scripts/hcm002-validate.ps1)
  - `Hcm002AttendanceTests`

## HCM-003 — الأداء والأهداف والكفاءات والتظلم

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Performance Product
- **النطاق:** cycles/templates؛ objectives/competencies؛ self/manager/matrix؛ calibration؛ rating policy؛ appeal؛ PIP؛ confidentiality؛ historical versions؛ analytics fairness.
- **معايير القبول:** manager scoped تاريخياً؛ النموذج/الأهداف محفوظان؛ calibration والاعتراض مدققان؛ لا أثر مالي آلي دون قاعدة معتمدة؛ bias/quality reviews.
- **التحقق:** manager change mid-cycle، delegation/conflict، appeal، historical report.
- **الاعتماديات:** EST-001, WF-001, MDM-001, IAM-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/HCM_003_PERFORMANCE.md](../HCM_003_PERFORMANCE.md)
  - `src/Shared/HRManagement.SharedKernel/Performance/PerformanceCycle.cs` (`CycleStatus`, `CyclePhase`, `IsInPhase()`, `RatingScaleMax`)
  - `src/Shared/HRManagement.SharedKernel/Performance/PerformanceReview.cs` (`ReviewType`, `ReviewStatus`, `FinalRating`, `CalibratedRating`, `HasAppeal`)
  - [scripts/hcm003-validate.ps1](../../scripts/hcm003-validate.ps1)
  - `Hcm003PerformanceTests`

## HCM-004 — المنافع والتقاعد والتأمينات

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Benefits + Payroll + Legal
- **النطاق:** eligibility؛ plans/dependents/life events؛ employee/employer contributions؛ pension/service credit؛ provider exchange؛ retroactivity؛ annual enrollment؛ statements؛ appeals.
- **معايير القبول:** الحساب التاريخي قابل للإعادة؛ totals تتصالح مع Payroll والمزود؛ لا تغطية مزدوجة؛ كل قانون/config versioned؛ البيان قابل للتفسير.
- **التحقق:** life events، retro law، provider rejects، payroll/provider reconciliation.
- **الاعتماديات:** PAY-002, PROD-001, EXT-005, GOV-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/HCM_004_BENEFITS.md](../HCM_004_BENEFITS.md)
  - `src/Shared/HRManagement.SharedKernel/Benefits/BenefitPlan.cs` (`BenefitPlanType`, `IsEffectiveOn()`, `LegalSourceId`, effective date range)
  - `src/Shared/HRManagement.SharedKernel/Benefits/PensionEnrollment.cs` (`PensionEnrollmentStatus`, `ServiceCreditMonths`, `ContributionBalance`, `LegalRegimeId`)
  - [scripts/hcm004-validate.ps1](../../scripts/hcm004-validate.ps1)
  - `Hcm004BenefitsTests`

## HCM-005 — التعلم والمواهب والتعاقب والمسار المهني

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Talent + Training
- **النطاق:** skills taxonomy؛ gaps؛ learning plans/catalog/providers/budgets؛ enrollment/attendance/completion؛ verifiable certificates؛ careers/internal opportunities؛ talent/succession pools؛ privacy.
- **معايير القبول:** skill مرتبط بوظيفة؛ شهادة قابلة للتحقق؛ الترشيح والتكلفة مدققان؛ succession access محدود؛ النتائج تتصالح مع الحضور والميزانية.
- **التحقق:** certificate fraud، provider import، nomination conflicts، role/skill gap reports.
- **الاعتماديات:** EST-001, MDM-001, DOC-001, FUN-004.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/HCM_005_LEARNING.md](../HCM_005_LEARNING.md)
  - `src/Shared/HRManagement.SharedKernel/Talent/SkillRecord.cs` (`SkillCategory`, `ProficiencyLevel`, `RequiredLevel`, `Gap`, `HasGap`, `IsCertificateValid()`)
  - `src/Shared/HRManagement.SharedKernel/Talent/LearningEnrollment.cs` (`LearningType`, `EnrollmentStatus`, `BudgetAmount`, `IsWithinBudget`, `ApprovedByManagerId`)
  - [scripts/hcm005-validate.ps1](../../scripts/hcm005-validate.ps1)
  - `Hcm005LearningTests`

## HCM-006 — علاقات الموظفين والصحة والسلامة والقضايا

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Employee Relations + Compliance + Health
- **النطاق:** grievance/complaint/investigation/discipline/appeal؛ whistleblower؛ conflict؛ workplace incident؛ medical restrictions/accommodation؛ evidence/custody؛ deadlines؛ confidentiality walls.
- **معايير القبول:** case access منفصل؛ حماية المبلغ؛ لا disciplinary action خارج workflow؛ المواعيد تنبه وتصعّد؛ medical details لا تظهر لHR العام؛ aggregates تمنع re-identification.
- **التحقق:** persona walls، conflict-of-interest، appeal، legal hold، privacy attacks.
- **الاعتماديات:** WF-001, FILE-002, GOV-001, EVD-001, FUN-004.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/HCM_006_EMPLOYEE_RELATIONS.md](../HCM_006_EMPLOYEE_RELATIONS.md)
  - `src/Shared/HRManagement.SharedKernel/Cases/EmployeeRelationCase.cs` (`RelationCaseType`: Grievance/Discipline/Investigation/Whistleblower, `RelationCaseStatus`, `IsConfidential`, `LegalHoldReference`)
  - `scripts/hcm006-validate.ps1` / `Hcm006EmployeeRelationsTests`

## WF-001 — محرك Workflow قابل للإعداد والإصدار

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Workflow Platform
- **النطاق:** sequential/parallel/quorum؛ conditions؛ correction/appeal/cancel؛ timeouts/escalation؛ delegation/out-of-office؛ SLA؛ subflows؛ version/effective date؛ simulation؛ migration؛ domain guardrails.
- **معايير القبول:** instance يحتفظ بنسخته؛ لا dead ends؛ تغيير التعريف لا يغيّر الجاري دون migration؛ config لا يتجاوز domain invariant؛ اختلاف الجهة والقيمة والدرجة دون code fork.
- **التحقق:** model validation، long-running cases، definition upgrades، hostile config، failure/retry.
- **الاعتماديات:** PROD-001, IAM-003, DATA-001.
- **الحالة:** Done (configurable workflow engine with version pinning)
- **أدلة الإغلاق:**
  - [ADR-WF-001-workflow-engine.md](../ADR-WF-001-workflow-engine.md) / [WF_001_WORKFLOW_ENGINE.md](../WF_001_WORKFLOW_ENGINE.md)
  - `src/Shared/HRManagement.SharedKernel/Workflow/` (enums, DTOs, WorkflowCondition evaluator)
  - `src/Services/HRCore/HR.Core.Domain/Entities/Workflow/` (WorkflowDefinition, WorkflowInstance, WorkflowDelegation)
  - `src/Shared/HRManagement.ServiceDefaults/Workflow/` (IWorkflowEngine, WorkflowEngine, WorkflowEngineExtensions)
  - `src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs` / `WorkflowInstancesController.cs`
  - `src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731161000_WF001_WorkflowEngine.cs`
  - `ApprovalWorkflowService.CreateViaEngineOrLegacy` bridge method
  - `scripts/wf001-workflow-validate.ps1` / `Wf001WorkflowEngineTests`

## WF-002 — صندوق عمل وطني موحد

- **الأولوية/الحجم/المسؤول:** P2-National / L / Workflow + UX
- **النطاق:** personal/team inbox؛ tasks/approvals؛ filters/aging/SLA؛ delegation/reassign/escalation؛ source status؛ priority؛ bulk controls؛ mobile؛ audit.
- **معايير القبول:** المستخدم يرى مهامه فقط؛ العدد يتصالح مع المصادر؛ تعطل خدمة لا يخفي task؛ bulk approval محظور أو محكوم؛ كل تحويل مدقق.
- **التحقق:** source outage/rebuild، cross-tenant search، delegation expiry، task race.
- **الاعتماديات:** WF-001, PROD-005, COM-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/WF_002_WORK_INBOX.md](../WF_002_WORK_INBOX.md)
  - `src/Shared/HRManagement.ServiceDefaults/Workflow/WorkInboxItem.cs` (`WorkItemType`, `WorkItemPriority`, `WorkItemStatus`, `AgingDays`, `DelegatedFromId`)
  - `src/Shared/HRManagement.ServiceDefaults/Workflow/IWorkInboxService.cs` (`GetPersonalInboxAsync`, `DelegateItemAsync`, `EscalateItemAsync`)
  - `scripts/wf002-validate.ps1` / `Wf002WorkInboxTests`

## SIG-001 — توقيع إلكتروني قانوني طويل الأجل

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Legal + PKI + Security
- **النطاق:** decisions requiring person signature/organizational seal؛ canonical payload؛ WYSIWYS؛ signer role/intent؛ timestamp؛ revocation material؛ LTV؛ independent verification؛ no delete.
- **معايير القبول:** actor/time من هوية موثقة لا form؛ تغيير byte/amount/employee يبطل؛ operator لا يركب signature؛ التحقق يعمل بعد انتهاء certificate وفق السياسة.
- **التحقق:** altered payload، expired/revoked cert، archive restore verification، repudiation case.
- **الاعتماديات:** EXT-004, SOV-003, STR-002, OBS-003.
- **الحالة:** Done (software X.509 with HSM abstraction)
- **أدلة الإغلاق:**
  - [SIG_001_SIGNATURE_POLICY.md](../SIG_001_SIGNATURE_POLICY.md) / [SIG_001_VERIFICATION.md](../SIG_001_VERIFICATION.md)
  - `src/Shared/HRManagement.SharedKernel/Signatures/` (`SignatureRecord`, `SignaturePolicy`, `CanonicalPayloadBuilder`)
  - `src/Shared/HRManagement.ServiceDefaults/Signatures/` (`ISignatureService`, `SoftwareSignatureService`, `SignatureVerifier`, `SignatureImmutabilityInterceptor`, `SignatureEntityConfiguration`, `LegalSignatureExtensions`, `SignatureRequiredAttribute`)
  - `src/Services/HRCore/HR.Core.Web/Controllers/SignaturesController.cs`
  - `scripts/sig001-signature-validate.ps1` / `Sig001SignatureTests`

## DOC-001 — النماذج والقرارات والوثائق الرسمية

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Document Platform + Records Authority
- **النطاق:** multilingual templates؛ official numbering؛ data binding؛ preview؛ PDF/A أو صيغة معتمدة؛ QR verification؛ signature/seal/time؛ accessibility؛ archive؛ correction/supersede.
- **معايير القبول:** الوثيقة مربوطة بنسخة template/data/rule؛ النسخة الرسمية immutable؛ التصحيح يصدر replacement لا overwrite؛ verification مستقل؛ printable/accessibility صحيحة.
- **التحقق:** template upgrades، tamper، multilingual PDF، archive retrieval، offline verifier.
- **الاعتماديات:** SIG-001, FILE-001, LEG-001, L10N-001.
- **الحالة:** Done (versioned template model with QR verification, signature, and PDF/A defaults)
- **أدلة الإغلاق:**
  - [DOC_001_OFFICIAL_DOCUMENTS.md](../DOC_001_OFFICIAL_DOCUMENTS.md)
  - `src/Shared/HRManagement.SharedKernel/Documents/OfficialDocumentTemplate.cs` (`OfficialDocumentFormat`, `TemplateStatus`, `OfficialDocumentTemplate`)
  - `scripts/doc001-validate.ps1`؛ `Doc001DocumentsTests`

## ESS-001 — مركز خدمة الموظف وحالات دائمة

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Employee Experience
- **النطاق:** profile/correction؛ requests/leave/advances/documents/payslips/benefits/learning/cards؛ durable Case ID؛ timeline/status/owner/SLA؛ withdraw/correct؛ evidence؛ notifications.
- **معايير القبول:** كل submit يولد Case دائم؛ الفشل اللاحق لا يظهر نجاحاً نهائياً؛ الموظف لا يرى غيره؛ الحالة تتصالح مع workflow؛ high-volume journeys self-service كاملة.
- **التحقق:** downstream failure، duplicate submit، cross-user IDOR، mobile/low bandwidth UAT.
- **الاعتماديات:** WF-001, TEN-002, INT-002, PROD-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/ESS_001_SERVICE_CENTER.md](../ESS_001_SERVICE_CENTER.md)
  - `src/Shared/HRManagement.SharedKernel/Ess/EmployeeServiceCase.cs` (`ServiceCaseType`, `ServiceCaseStatus`, durable `CaseId`, `SlaDeadline`, `SlaBreached`, `IsSlaMet`)
  - `scripts/ess001-validate.ps1` / `Ess001ServiceCenterTests`

## MSS-001 — الخدمة الذاتية للمدير

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Manager Experience
- **النطاق:** dated team؛ calendar/absence؛ approvals؛ position/vacancy؛ performance؛ changes؛ training؛ dashboards؛ delegation؛ conflicts؛ acting manager.
- **معايير القبول:** team مشتق من علاقات مؤرخة؛ لا عمل خارج السلسلة؛ delegation مؤقت؛ conflict يمنع القرار؛ metrics تتصالح؛ sensitive details minimized.
- **التحقق:** reorganization/manager change، acting/delegation، conflict، cross-branch access.
- **الاعتماديات:** ORG-001, EST-001, WF-002, IAM-003, PROD-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/MSS_001_MANAGER_SELF_SERVICE.md](../MSS_001_MANAGER_SELF_SERVICE.md)
  - [`docs/mss/manager-capabilities.json`](../mss/manager-capabilities.json) — 12 capabilities, conflict rules, delegation policy
  - [scripts/mss001-validate.ps1](../../scripts/mss001-validate.ps1)
  - `Mss001ManagerTests`

## COM-001 — إشعارات دائمة ومتعددة القنوات

- **الأولوية/الحجم/المسؤول:** P2-National / L / Communications Platform
- **النطاق:** in-app/email/SMS/push؛ recipients/tenant؛ templates/locales؛ preferences/mandatory؛ quiet hours؛ retries/dedup؛ delivery receipts؛ expiration؛ escalation؛ no broadcast-all.
- **معايير القبول:** قطع SignalR لا يفقد؛ duplicate event لا يكرر؛ delivery/failure مدقق؛ لا PII حساس في channel غير آمن؛ deep link يعيد التفويض؛ المستخدم يرى history.
- **التحقق:** channel outage، duplicate/out-of-order، wrong recipient/tenant، template injection.
- **الاعتماديات:** INT-002, IAM-004, L10N-001, EXT-007.
- **الحالة:** Done (durable multi-channel notifications with dedup and delivery tracking)
- **أدلة الإغلاق:**
  - [COM_001_NOTIFICATIONS.md](../COM_001_NOTIFICATIONS.md)
  - `src/Shared/HRManagement.SharedKernel/Notifications/NotificationMessage.cs` (`NotificationChannel`, `NotificationStatus`)
  - `src/Shared/HRManagement.ServiceDefaults/Notifications/INotificationService.cs` / `InAppNotificationService.cs`
  - `scripts/com001-validate.ps1` / `Com001NotificationTests`

## L10N-001 — التدويل والتعريب والتقاويم والمناطق الزمنية

- **الأولوية/الحجم/المسؤول:** P1-National / L / UX Platform
- **النطاق:** resource localization؛ Arabic/English + local decision؛ RTL/LTR؛ numbers/currency/date/calendar/timezone؛ names/addresses؛ validation؛ reports/email/PDF؛ content governance؛ fallback.
- **معايير القبول:** لا user-facing hardcode في scope؛ canonical storage؛ locale per user/agency؛ PDF/email مطابق؛ pseudo-localization؛ لا truncation/mixed direction blockers.
- **التحقق:** automated resource scan، visual regression، calendar/timezone boundaries، bilingual UAT.
- **الاعتماديات:** PROD-001, UX-002.
- **الحالة:** Done (shared localization infrastructure with ar-SY default, Hijri calendar, language toggle)
- **أدلة الإغلاق:**
  - [L10N_001_LOCALIZATION.md](../L10N_001_LOCALIZATION.md)
  - `src/Shared/HRManagement.ServiceDefaults/Localization/` (`HrLocalizationExtensions`, `ICalendarService`, `DamascusCalendarService`, `CultureController`)
  - `src/Shared/HRManagement.ServiceDefaults/Localization/Resources/` (`SharedResources.cs`, `SharedResources.ar.resx`, `SharedResources.en.resx`)
  - `src/Shared/HRManagement.Web.Shared/Resources/` (`SharedResources.cs`, `SharedResources.resx`, `SharedResources.en.resx`)
  - Gateway + HR.Core `Program.cs` wire `AddHrLocalization()` / `UseHrLocalization()`
  - `scripts/l10n001-validate.ps1` / `L10n001LocalizationTests`

## MOB-001 — الهاتف والاتصال المنخفض والانقطاع

- **الحالة:** Done (mobile-first PWA policy; performance budgets; resumable upload; secure cache/retry; docs, tests)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Mobile/PWA + Security
- **النطاق:** mobile-first/PWA decision؛ performance budgets؛ resumable upload؛ drafts؛ safe retry/idempotency؛ secure cache؛ offline field cases only after threat model؛ conflict resolution؛ device loss.
- **معايير القبول:** الرحلات الأساسية تعمل على device/network profile؛ interruption لا يكرر؛ PII/token cache وفق policy؛ remote logout/revocation؛ accessible touch UX.
- **التحقق:** real devices، throttled/intermittent network، lost device، concurrent offline conflict.
- **الاعتماديات:** ESS-001, MSS-001, PROD-004, SEC-003.
- **أدلة الإغلاق:**
  - [MOB_001_MOBILE.md](../MOB_001_MOBILE.md)
  - [`docs/mob/performance-budgets.json`](../mob/performance-budgets.json) — PWA strategy, network profiles, performance budgets, retry/cache/offline policy
  - `scripts/mob001-validate.ps1` / `Mob001MobileTests`

## ANA-001 — منصة تحليل وطنية وطبقة دلالية محكومة

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Data & Analytics
- **النطاق:** warehouse/lakehouse؛ conformed/SCD dimensions؛ event/source ingestion؛ late data؛ lineage؛ quality/freshness؛ tenant/RLS؛ rebuild؛ semantic measures؛ national authority؛ privacy.
- **معايير القبول:** headcount/positions/payroll لا تحسب بعداد تقريبي؛ KPI definition/owner/grain/as-of؛ totals تتصالح؛ rebuild مطابق؛ national aggregate permission منفصل.
- **التحقق:** golden datasets، projection rebuild، late/out-of-order، cross-tenant BI، source reconciliation.
- **الاعتماديات:** MDM-001, GOV-003, INT-002, TEN-003, PROD-006.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/ANA_001_ANALYTICS.md](../ANA_001_ANALYTICS.md)
  - [`docs/ana/semantic-layer.json`](../ana/semantic-layer.json) — 6 measures (headcount, FTE, vacancy_rate, payroll_total, turnover_rate, avg_tenure), 7 dimensions, snapshot policy, RLS, national aggregate separation
  - [scripts/ana001-validate.ps1](../../scripts/ana001-validate.ps1)
  - `Ana001AnalyticsTests`

## REP-001 — التقارير النظامية وSelf-Service BI المحكوم

- **الحالة:** Done (statutory report catalog; golden datasets; as-of/version metadata; access/retention; docs, tests)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Reporting + Legal/Finance
- **النطاق:** statutory catalog؛ versions/effective dates؛ as-of؛ schedule/distribution؛ drill-through؛ export؛ governed ad-hoc؛ report access/retention؛ background execution.
- **معايير القبول:** golden dataset لكل تقرير ملزم؛ historic reproducibility؛ figures drill to source؛ لا cross-tenant؛ report يحمل version/as-of/source؛ large reports ضمن jobs.
- **التحقق:** legal report regression، access matrix، scheduled failure/retry، reconciliation.
- **الاعتماديات:** ANA-001, PERF-004, GOV-002.
- **أدلة الإغلاق:**
  - [REP_001_REPORTS.md](../REP_001_REPORTS.md)
  - [`docs/rep/statutory-catalog.json`](../rep/statutory-catalog.json) — statutory reports, golden datasets, as-of, retention
  - `scripts/rep001-validate.ps1` / `Rep001ReportsTests`

## INT-004 — موصلات حكومية Live حقيقية

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Integration Engineering
- **النطاق:** استبدال سقوط Live إلى file adapter؛ implement connectors حسب EXT؛ production config validation؛ auth/mTLS/signature؛ retry/DLQ/reconciliation/replay؛ rate/SLA؛ sandbox؛ observability.
- **معايير القبول:** `Live` لا يستخدم File/Stub؛ startup production يفشل عند missing connector؛ outage لا يفسد؛ كل connector conformance-certified؛ reconciliation 100%.
- **التحقق:** connector-specific sandbox، missing config، provider outage، replay/duplicate، key rollover.
- **الاعتماديات:** EXT-001..007, IAM-004, INT-002.
- **الحالة:** Done (mode-switched government connectors with production guard)
- **أدلة الإغلاق:**
  - [INT_004_CONNECTORS.md](../INT_004_CONNECTORS.md)
  - `src/Shared/HRManagement.ServiceDefaults/Connectors/` (`IntegrationConnectorMode`, `IGovernmentConnector`, `GovernmentConnectorOptions`, `ConnectorBindingOptions`, `LiveConnectorRegistry`, `GovernmentConnectorStartupValidator`, `TreasuryLiveConnector`, `CivilRegistryLiveConnector`, `FederationConnectorDescriptor`, `GovernmentConnectorExtensions`)
  - `builder.AddGovernmentConnectors()` wired in Gateway and HR.Core `Program.cs`
  - `scripts/int004-validate.ps1` / `Int004ConnectorTests`

## ONB-001 — توفير جهة آلياً من حزمة إعداد موقعة

- **الحالة:** Done (signed agency manifest schema; four-eyes publish; rollback; no secrets in pack; docs, tests)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Agency Platform
- **النطاق:** tenant/data/identity/roles/SoD/policies/workflows/reference subset/language/calendar/branding/integrations/retention/monitoring/backup/support؛ manifest؛ validation؛ sign؛ promote/rollback.
- **معايير القبول:** جهة من الصفر آلياً؛ no secret in pack؛ national mandatory controls محفوظة؛ observability/backup/access tests آلية؛ rollback؛ no manual DB edits.
- **التحقق:** provision/destroy-safe/reprovision in staging، malicious config، key rotation.
- **الاعتماديات:** PROD-002, TEN-003, OPS-005, EXT-006.
- **أدلة الإغلاق:**
  - [ONB_001_PROVISIONING.md](../ONB_001_PROVISIONING.md)
  - [`docs/onb/agency-manifest-schema.json`](../onb/agency-manifest-schema.json) — manifest schema, example, provisioning rules
  - `scripts/onb001-validate.ps1` / `Onb001ProvisioningTests`

## ONB-002 — اعتماد الجهة والتوسع بموجات

- **الأولوية/الحجم/المسؤول:** P2-National / L / Onboarding Office
- **النطاق:** readiness/network/identity/config/data/integration/UAT/training/support/cutover/hypercare؛ archetype؛ entry/exit/stop؛ evidence؛ lessons into template.
- **معايير القبول:** لا wave بلا checklist موقع؛ rehearsal/rollback؛ no Sev1/2؛ support/on-call؛ payroll/data reconciliation؛ success measured by outcomes/adoption لا page availability.
- **التحقق:** full onboarding of diverse agencies، stop/rollback exercise، 30-day scorecard.
- **الاعتماديات:** ONB-001, MIG-007, UAT-001, TRN-001, REL-007.

## UXR-001 — بحث المستخدم وتصميم الخدمة الميداني

- **الأولوية/الحجم/المسؤول:** P1-National / L / Service Design + Agencies
- **النطاق:** employees/managers/HR/payroll/audit/support؛ central/remote؛ disability؛ low bandwidth؛ journey blueprints؛ pain/baseline؛ prototypes؛ metrics؛ continuous research ethics/privacy.
- **معايير القبول:** عينات من كل archetype؛ task success/time/error/SUS أو بديل؛ critical findings تتحول لبطاقات؛ التصميم يثبت قبل وبعد البناء؛ لا اعتماد على رأي الفريق.
- **التحقق:** moderated field studies، accessibility participation، report traceability.
- **الاعتماديات:** PRD-001, STR-007.
- **الحالة:** Done (field research framework + journey blueprints + metrics baseline + ethics protocol)
- **أدلة الإغلاق:**
  - [UXR_001_RESEARCH_FRAMEWORK.md](../UXR_001_RESEARCH_FRAMEWORK.md)
  - `docs/uxr/journey-blueprints.md` / `docs/uxr/usability-metrics-baseline.json` / `docs/uxr/research-ethics-protocol.md`
  - `scripts/uxr001-validate.ps1` / `Uxr001ResearchTests`

## UAT-001 — قبول أعمال وقانون رسمي قابل للتتبع

- **الأولوية/الحجم/المسؤول:** National Gate / XL / Agency Business + Legal + QA
- **النطاق:** القانون والعمليات؛ personas؛ positive/failure/appeal/retro/concurrency؛ representative data؛ evidence؛ defects/waivers؛ payroll parallel؛ signatures؛ migration.
- **معايير القبول:** كل mandatory requirement→test→evidence؛ no Sev1/2؛ Sev3 waiver مؤقت؛ sign-off متعدد الاختصاصات؛ الاختبار على release digest المرشح.
- **التحقق:** witnessed execution، independent evidence sample، defect retest.
- **الاعتماديات:** QA-004, UXR-001, capabilities in scope.

## TRN-001 — التدريب وإدارة التغيير والتبنّي الوطني

- **الأولوية/الحجم/المسؤول:** P2-National / XL / Change Management + Agencies
- **النطاق:** stakeholder/change impact؛ persona curricula؛ train-the-trainer؛ sandbox؛ practical certification for privileged roles؛ contextual help/knowledge؛ communications؛ adoption/support feedback؛ release updates.
- **معايير القبول:** لا privileged role قبل competency pass؛ materials multilingual/accessible؛ trainers per agency؛ completion/adoption/task metrics؛ learning updates with release؛ resistance/gaps managed.
- **التحقق:** practical exams، training accessibility، adoption pilot، support-ticket correlation.
- **الاعتماديات:** UXR-001, UAT-001, SUP-002, L10N-001.

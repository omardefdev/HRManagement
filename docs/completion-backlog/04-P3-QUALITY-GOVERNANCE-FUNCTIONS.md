# P3 — بطاقات الجودة والحوكمة واكتمال الوظائف

هذه المرحلة لا تعني “تجميل المشروع”. هي التي تثبت أن الوظائف تخدم أصحابها فعلياً، وأن البيانات والقوانين والتشغيل قابلة للإدارة لسنوات.

## QA-002 — استراتيجية تغطية مبنية على المخاطر

- **الأولوية/الحجم/المسؤول:** P3 / L / QA + Tech Leads
- **النطاق:** خريطة مخاطر إلى test pyramid؛ coverage per critical assembly/diff لا رقم كلي فقط؛ mutation testing لقواعد الرواتب والحالات؛ منع استثناء generated migrations منطق الأعمال؛ test ownership؛ flaky test policy.
- **معايير القبول:** 100% من invariants P0/P1 لها اختبارات فشل ونجاح؛ حدود تغطية تدريجية مع منع الانخفاض؛ mutation score معتمد للمحركات الحساسة؛ لا flaky test مخفي بإعادة لا نهائية.
- **التحقق:** تقرير coverage/mutation ومصفوفة requirement→test.
- **الاعتماديات:** QA-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-QA-002-risk-based-coverage.md](../ADR-QA-002-risk-based-coverage.md) + [QA_RISK_TO_TEST_MATRIX.md](../QA_RISK_TO_TEST_MATRIX.md)
  - بوابة 60% على `gateAssemblies` + floors في [`coverage-floors.json`](../../coverage-floors.json) عبر `scripts/check-coverage.py`
  - Stryker: `tests/stryker-payroll.json` + `.github/workflows/mutation.yml` (break ≥ 60%)
  - [QA_FLAKY_TEST_POLICY.md](../QA_FLAKY_TEST_POLICY.md) + [QA_COVERAGE_EXCLUSIONS.md](../QA_COVERAGE_EXCLUSIONS.md) + `.github/CODEOWNERS`
  - [QA_COVERAGE_VERIFICATION.md](../QA_COVERAGE_VERIFICATION.md) + `Qa002InvariantCoverageTests`

## QA-003 — تكامل حقيقي للعقود والبنية التحتية

- **الأولوية/الحجم/المسؤول:** P3 / XL / QA + Platform
- **النطاق:** Testcontainers لـPostgreSQL/RabbitMQ/Keycloak/MinIO؛ migrations الحقيقية؛ outbox/inbox؛ auth policies؛ file scanning؛ schema compatibility؛ parallel test isolation؛ CI execution إلزامي.
- **معايير القبول:** لا SQLite/InMemory كدليل وحيد لمسار إنتاجي؛ failure modes الأساسية مغطاة؛ tests تعمل من clone نظيف؛ artifacts وتشخيص الفشل محفوظان بأمان.
- **التحقق:** إطفاء كل dependency، duplicate messages، migration upgrade، token matrix.
- **الاعتماديات:** DB-001, INT-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-QA-003-real-infra-tests.md](../ADR-QA-003-real-infra-tests.md) + [QA_REAL_INFRA_TOPOLOGY.md](../QA_REAL_INFRA_TOPOLOGY.md)
  - Fixtures: `TestFixtures.cs` (PG/Rabbit/Keycloak/MinIO)
  - Suites: `Qa003MigrationUpgradeTests`, `Qa003OutboxInboxRealInfraTests`, `Qa003KeycloakTokenMatrixTests`, `Qa003MinioFileScanTests`
  - CI: `.github/workflows/ci.yml` — Testcontainers إلزامي
  - [QA_REAL_INFRA_VERIFICATION.md](../QA_REAL_INFRA_VERIFICATION.md)

## QA-004 — E2E لرحلات الأعمال والصلاحيات

- **الأولوية/الحجم/المسؤول:** P3 / XL / QA + Product
- **النطاق:** Playwright/API للـhire/separation/payroll/leave/transfer/files/role change؛ personas لعدة tenants وفروع؛ assertions على DB/events/audit؛ no silent return؛ seed اصطناعي versioned؛ screenshots/traces عند الفشل.
- **معايير القبول:** كل رحلة حرجة لها happy path وثلاثة failure/authorization paths على الأقل؛ 503 ليس نجاحاً؛ اختبارات على image release نفسها؛ نتائج مستقرة وقابلة للإعادة.
- **التحقق:** nightly + pre-release suite ونتائج UAT مربوطة.
- **الاعتماديات:** IAM-003, INT-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-QA-004-e2e-journeys.md](../ADR-QA-004-e2e-journeys.md) + [QA_E2E_JOURNEY_MATRIX.md](../QA_E2E_JOURNEY_MATRIX.md)
  - Harness: `E2eHarness.cs` / `PlaywrightFixture.cs`؛ seed `testdata/e2e/` + `realm-qa004-e2e.json`
  - Suites: `HireSeparationJourneyE2ETests`, `PayrollLeaveTransferFilesRoleJourneyE2ETests`, `E2eSeedPackTests`
  - CI: `.github/workflows/e2e-journeys.yml` (nightly + workflow_dispatch / release images)
  - [QA_E2E_VERIFICATION.md](../QA_E2E_VERIFICATION.md)

## SUP-001 — أمن سلسلة التوريد وSBOM والتوقيع

- **الأولوية/الحجم/المسؤول:** P3 / L / AppSec + Platform
- **النطاق:** pinned packages/images/digests؛ SCA/SAST/secret/IaC/container scan؛ SBOM لكل image؛ provenance/signing؛ license policy؛ dependency update SLA؛ private registry retention؛ build isolation.
- **معايير القبول:** لا Critical/High دون استثناء زمني موقع؛ deploy يتحقق من signature؛ SBOM مرتبط بالrelease؛ الحزم العائمة ممنوعة؛ advisory جديد يفتح بطاقة تلقائياً.
- **التحقق:** محاولة نشر image غير موقعة، dependency vulnerable fixture، ومراجعة license report.
- **الاعتماديات:** QA-001, DEP-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-SUP-001-supply-chain.md](../ADR-SUP-001-supply-chain.md) + [SUPPLY_CHAIN_INVENTORY.md](../SUPPLY_CHAIN_INVENTORY.md)
  - Pins: [`docker/base-image-pins.json`](../../docker/base-image-pins.json) + [GHCR_RETENTION.md](../GHCR_RETENTION.md)
  - Gates: Trivy/CodeQL/Gitleaks + `supply-chain-exceptions.json` + NuGet SCA مع استثناءات
  - SBOM + Cosign + verify قبل deploy في `.github/workflows/deploy.yml`
  - License/Dependabot/SLA: [LICENSE_POLICY.md](../LICENSE_POLICY.md), `.github/dependabot.yml`, [DEPENDENCY_UPDATE_SLA.md](../DEPENDENCY_UPDATE_SLA.md)
  - [SUP_001_VERIFICATION.md](../SUP_001_VERIFICATION.md)

## UX-001 — معالجة XSS وCSP وCDN/SRI

- **الأولوية/الحجم/المسؤول:** P3 / M / Frontend + AppSec
- **النطاق:** إزالة `innerHTML` غير المرمز؛ safe DOM APIs؛ output encoding؛ CSP nonces؛ local assets أو SRI ثابت؛ no inline unsafe scripts تدريجياً؛ sanitize المحتوى الغني؛ security headers موحدة.
- **معايير القبول:** payloads المخزنة والمنعكسة لا تنفذ؛ CSP report-only ثم enforce دون كسر؛ لا CDN غير مثبت؛ automated DOM/static scan.
- **التحقق:** XSS corpus، browser CSP reports، وDAST.
- **الاعتماديات:** SEC-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-UX-001-xss-csp-sri.md](../ADR-UX-001-xss-csp-sri.md) + [UX_XSS_INVENTORY.md](../UX_XSS_INVENTORY.md)
  - DOM: `shared-ui.js` / `entity-pickers.js` / `live-notifications.js` / `file-upload.js`
  - أصول محلية: `HRManagement.Web.Shared/wwwroot/lib/` + `check-no-external-cdn.sh`
  - CSP/Headers: `HrSecurityHeadersExtensions` + `CspNonceTagHelper`
  - [UX_001_VERIFICATION.md](../UX_001_VERIFICATION.md) + `Ux001XssCorpusTests`

## UX-002 — الوصولية والاستجابة واتساق النماذج

- **الأولوية/الحجم/المسؤول:** P3 / XL / UX + Frontend
- **النطاق:** WCAG 2.2 AA؛ keyboard/focus/labels/aria/contrast؛ RTL؛ responsive tables؛ error summary؛ date/number locale؛ slow network؛ shared components؛ supported browsers؛ print/PDF accessibility.
- **معايير القبول:** journeys الأساسية بلا فأرة؛ zero serious/critical axe findings؛ كل icon button له اسم؛ الجداول تعمل على الهاتف؛ رسائل الخطأ تربط الحقول ولا تكشف معلومات.
- **التحقق:** axe/Playwright، قارئ شاشة يدوي، keyboard audit، وأجهزة فعلية.
- **الاعتماديات:** UX-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-UX-002-a11y.md](../ADR-UX-002-a11y.md) + [UX_A11Y_INVENTORY.md](../UX_A11Y_INVENTORY.md)
  - Chrome: skip-link، landmarks، `:focus-visible`، أسماء أزرار الأيقونة
  - Forms: `_HrSearchBar`، `a11y-forms.js`، validation `role="alert"`
  - Tables: `table-responsive` + CSS overflow؛ Indexes حرجة
  - [UX_002_VERIFICATION.md](../UX_002_VERIFICATION.md) + `A11yAxeE2ETests` + `check-icon-button-names.py`

## GOV-001 — تصنيف البيانات والخصوصية والاحتفاظ

- **الأولوية/الحجم/المسؤول:** P3 / L / Legal + Privacy + Data Governance
- **النطاق:** inventory وتصنيف PII/medical/payroll/complaints؛ purpose/legal basis؛ minimization؛ retention/deletion/legal hold؛ subject rights؛ residency؛ data sharing؛ masking؛ non-production data؛ breach response.
- **معايير القبول:** كل حقل حساس له مالك وغرض ومدة؛ retention jobs قابلة للتدقيق؛ لا production copy غير مقنعة في test؛ privacy impact assessment معتمد؛ طلبات الوصول/التصحيح لها workflow.
- **التحقق:** data-flow sampling، retention dry run، DSAR exercise، وbreach tabletop.
- **الاعتماديات:** TEN-001, DATA-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-GOV-001-data-classification-privacy.md](../ADR-GOV-001-data-classification-privacy.md) + [DATA_CLASSIFICATION_INVENTORY.md](../DATA_CLASSIFICATION_INVENTORY.md) + [GOV_001_DPIA.md](../GOV_001_DPIA.md)
  - تقليل: `PiiMask` + `[AuditRedact]`؛ `Gov001PiiMaskAndRedactTests`
  - احتفاظ: [GOV_001_RETENTION_SCHEDULE.md](../GOV_001_RETENTION_SCHEDULE.md) + `gov001-retention-dry-run.py` + [GOV_001_RETENTION_RUNBOOK.md](../GOV_001_RETENTION_RUNBOOK.md)
  - Non-prod: [NON_PROD_DATA_POLICY.md](../NON_PROD_DATA_POLICY.md) + `check-non-prod-data-policy.py`
  - DSAR: `DsarRequest` / `api/dsar`؛ [GOV_001_BREACH_TABLETOP.md](../GOV_001_BREACH_TABLETOP.md)
  - [GOV_001_VERIFICATION.md](../GOV_001_VERIFICATION.md)

## GOV-002 — سجل القرارات القانونية للرواتب والخدمة

- **الأولوية/الحجم/المسؤول:** P3 / L / Legal + Payroll + HR
- **النطاق:** كل rule يربط بمصدر رسمي وإصدار وتاريخ نفاذ؛ إزالة `LEGAL_TBD`؛ owner وapproval؛ أمثلة حدود؛ تضارب القوانين؛ retroactivity؛ rounding؛ نشر change notice؛ legal regression pack.
- **معايير القبول:** لا قاعدة مالية hard-coded دون مرجع؛ القانون والإعداد والاختبارات متطابقة؛ تغيير القانون لا يعدل الماضي؛ موافقة قانونية إلكترونية مؤرخة.
- **التحقق:** golden cases من مختصين، boundary/retroactive scenarios، وإعادة تشغيل تاريخي.
- **الاعتماديات:** PAY-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-GOV-002-legal-decision-register.md](../ADR-GOV-002-legal-decision-register.md) + [GOV_002_LEGAL_DECISION_REGISTER.md](../GOV_002_LEGAL_DECISION_REGISTER.md) + `legal-decisions/register.json`
  - `LegalDecisionRecord` / `LegalAttestation` / `PayrollConfigChangeNotice` + `PayrollLegalGovernance`
  - إزالة LEGAL_TBD: `check-no-legal-tbd.py`؛ [PAYROLL_RULES.md](../PAYROLL_RULES.md) مربوط بـdecisionId
  - [GOV_002_CONFLICT_RETRO_ROUNDING.md](../GOV_002_CONFLICT_RETRO_ROUNDING.md) + [GOV_002_CHANGE_NOTICE_RUNBOOK.md](../GOV_002_CHANGE_NOTICE_RUNBOOK.md)
  - `Gov002LegalGovernanceTests` / `Gov002LegalGoldenPackTests` + `testdata/gov002/golden-cases.json`
  - [GOV_002_VERIFICATION.md](../GOV_002_VERIFICATION.md)

## GOV-003 — حوكمة البيانات الرئيسية وجودتها

- **الأولوية/الحجم/المسؤول:** P3 / L / Data Governance
- **النطاق:** system of record للموظف والهيكل والمسميات والمراكز؛ data contracts؛ quality dimensions؛ duplicate/merge؛ reference data versioning؛ stewardship؛ reconciliation dashboards؛ lineage.
- **معايير القبول:** لا خدمة تدعي الملكية على نفس الحقل بلا قرار؛ duplicate national ID/employee merge له workflow؛ quality thresholds وتنبيهات؛ lineage من المصدر إلى التقرير.
- **التحقق:** profiling، reconciliation عبر الخدمات، وmerge/unmerge tests.
- **الاعتماديات:** TEN-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-GOV-003-master-data-governance.md](../ADR-GOV-003-master-data-governance.md) + [GOV_003_SOR_MATRIX.md](../GOV_003_SOR_MATRIX.md) + `master-data/field-ownership.json`
  - عقود: [master-employee.v1.md](../data-contracts/master-employee.v1.md) / [master-orgunit.v1.md](../data-contracts/master-orgunit.v1.md)
  - تكرار/دمج: `MasterDataDuplicateCase` + merge/unmerge؛ [GOV_003_MERGE_RUNBOOK.md](../GOV_003_MERGE_RUNBOOK.md)
  - جودة: [GOV_003_DATA_QUALITY.md](../GOV_003_DATA_QUALITY.md) + `gov003-data-quality-report.py`
  - نسب: [GOV_003_LINEAGE.md](../GOV_003_LINEAGE.md) + `master-data/lineage.json`
  - `Gov003OwnershipContractTests` / `Gov003MergeUnmergeTests` / `Gov003DataQualityThresholdTests`
  - [GOV_003_VERIFICATION.md](../GOV_003_VERIFICATION.md)

## FUN-001 — إغلاق رحلة التعيين من طرف إلى طرف

- **الأولوية/الحجم/المسؤول:** P3 / L / Product HR + الخدمات المعنية
- **النطاق:** candidate acceptance→employee→contract→pay profile→directory account→IT provisioning→card→archive؛ mandatory/optional steps؛ timeout/compensation؛ duplicate candidate؛ rejection/correction؛ notifications.
- **معايير القبول:** لا تعلن مكتملة قبل الخطوات الإلزامية؛ كل خطوة لها مالك وSLA ودليل؛ فشل خدمة يظهر ويستأنف؛ duplicate event لا ينشئ موارد مكررة؛ UAT موقع.
- **التحقق:** QA-004 journey، outage لكل خدمة، وreconciliation checklist.
- **الاعتماديات:** INT-003, QA-004.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-FUN-001-hire-journey-closure.md](../ADR-FUN-001-hire-journey-closure.md) + [FUN_001_JOURNEY_MATRIX.md](../FUN_001_JOURNEY_MATRIX.md)
  - HireChecklist: OwnerRole/SlaHours/EvidenceRef/CompletedBy؛ Auth.Directory إلزامي في Hire saga
  - Cancel/Compensate: `ICancelHireSaga`؛ [FUN_001_RECONCILIATION_CHECKLIST.md](../FUN_001_RECONCILIATION_CHECKLIST.md)
  - [FUN_001_EXCEPTION_PATHS.md](../FUN_001_EXCEPTION_PATHS.md) + `Fun001*` tests
  - [FUN_001_UAT_PACK.md](../FUN_001_UAT_PACK.md) ReadyForSign-off؛ [FUN_001_VERIFICATION.md](../FUN_001_VERIFICATION.md)

## FUN-002 — إغلاق رحلة الفصل والتسوية

- **الأولوية/الحجم/المسؤول:** P3 / L / Product HR + Payroll + IT + Assets
- **النطاق:** قرار الفصل→checklist→assets/account/card/access→final payroll/benefits→archive→notifications؛ exceptions/appeal؛ future effective date؛ rehire؛ legal hold.
- **معايير القبول:** لا access أو أصل أو راتب دوري يبقى بلا قرار استثناء؛ final settlement قابلة للتفسير؛ الإلغاء قبل النفاذ يعكس الخطوات؛ التاريخ لا يحذف؛ UAT موقع.
- **التحقق:** happy/future/cancel/failure/rehire scenarios، وأثر عبر كل خدمة.
- **الاعتماديات:** INT-003, PAY-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [ADR-FUN-002-separation-journey-closure.md](../ADR-FUN-002-separation-journey-closure.md) + [FUN_002_JOURNEY_MATRIX.md](../FUN_002_JOURNEY_MATRIX.md)
  - SeparationChecklist: OwnerRole/SlaHours؛ soft-cancel (`IsCancelled`)؛ Auth compensate + EOS draft void
  - [FUN_002_RECONCILIATION_CHECKLIST.md](../FUN_002_RECONCILIATION_CHECKLIST.md) + [FUN_002_EXCEPTION_PATHS.md](../FUN_002_EXCEPTION_PATHS.md)
  - [FUN_002_UAT_PACK.md](../FUN_002_UAT_PACK.md) ReadyForSign-off؛ [FUN_002_VERIFICATION.md](../FUN_002_VERIFICATION.md)؛ `Fun002*` tests

## FUN-003 — إغلاق النقل والانتداب والإعارة

- **الأولوية/الحجم/المسؤول:** P3 / L / HR Movements + Payroll
- **الحالة:** **Done**
- **النطاق:** source/target authority؛ effective date؛ checklist؛ soft-cancel/compensate؛ SoD انتداب/إعارة؛ إنهاء restore؛ conflict gate؛ Loan event-hardened.
- **معايير القبول:** لا نقل دون صلاحية على المصدر والهدف؛ الأثر المالي والوظيفي متزامنان أو حالة معلقة واضحة؛ لا forged approver؛ انتهاء الانتداب يعيد الحالة الصحيحة؛ تقارير الجهة صحيحة.
- **التحقق:** cross-tenant/branch tests، concurrent movement، effective-date cases.
- **الاعتماديات:** INT-003, IAM-003.
- **ما أُغلق:**
  - ADR + مصفوفة رحلات Transfer/Secondment/Loan (`docs/fun-movements/`)
  - `MovementChecklist` + سلطة مصدر/هدف AND + SoD Secondment/Loan
  - soft-cancel + `ICancel*Saga` + Compensate + interventions Cancel
  - EffectiveDate/future gate + تعارض حركة + Close/`ILoanAssignmentEnded`
  - [FUN_003_RECONCILIATION_CHECKLIST.md](../FUN_003_RECONCILIATION_CHECKLIST.md) + [FUN_003_EXCEPTION_PATHS.md](../FUN_003_EXCEPTION_PATHS.md)
  - [FUN_003_UAT_PACK.md](../FUN_003_UAT_PACK.md) ReadyForSign-off؛ [FUN_003_VERIFICATION.md](../FUN_003_VERIFICATION.md)؛ `Fun003*` tests

## FUN-004 — إغلاق الصحة والتدريب والالتزام

- **الأولوية/الحجم/المسؤول:** P3 / L / أصحاب الوحدات + Privacy
- **الحالة:** **Done**
- **النطاق:** medical least-access/consent؛ insurance lifecycle؛ training enrollment/completion/certification؛ compliance complaint/investigation/discipline؛ attachments؛ retention؛ notifications؛ appeals؛ reporting aggregates.
- **معايير القبول:** السجل الطبي لا يظهر لأدوار HR العامة؛ الشكوى تحمي المبلغ عنه؛ الإجراءات التأديبية لا تتجاوز workflow؛ الشهادة قابلة للتحقق؛ كل ملف يخضع FILE-002.
- **التحقق:** persona/privacy tests، workflow UAT، retention and redaction tests.
- **الاعتماديات:** TEN-003, FILE-002.
- **ما أُغلق:**
  - ADR + مصفوفة Health/Training/Compliance (`docs/fun-htc/`)
  - `HrHealthClinical` + consent + insurance lifecycle + soft-cancel
  - `TrainingChecklist` + `/api/certificates/verify` + FILE-002 شهادات
  - whistleblower walls + `InvestigationChecklist` + `CanIssue` + أدلة FILE-002
  - [FUN_004_RECONCILIATION_CHECKLIST.md](../FUN_004_RECONCILIATION_CHECKLIST.md) + [FUN_004_EXCEPTION_PATHS.md](../FUN_004_EXCEPTION_PATHS.md)
  - [FUN_004_UAT_PACK.md](../FUN_004_UAT_PACK.md) ReadyForSign-off؛ [FUN_004_VERIFICATION.md](../FUN_004_VERIFICATION.md)؛ `Fun004*` tests

## FUN-005 — إغلاق الهوية والأصول وتقنية المعلومات

- **الأولوية/الحجم/المسؤول:** P3 / L / IDCard + Logistics + IT
- **الحالة:** **Done**
- **النطاق:** card request/design/issue/print/deliver/revoke؛ asset assign/transfer/return/loss؛ directory/account/device provisioning؛ expiry/renewal؛ separation reactions؛ inventory reconciliation؛ privileged assets/access.
- **معايير القبول:** لا بطاقة فعالة بعد الفصل؛ التسليم يثبت المستلم؛ الأصل لا يملك حائزين متزامنين؛ account provisioning failure مرئي ويعاد؛ revoke عاجل يعمل ضمن SLA.
- **التحقق:** lost card/asset, reissue, failed provisioning, separation E2E.
- **الاعتماديات:** INT-003, FILE-002.
- **ما أُغلق:**
  - ADR + مصفوفة IDCard/Assets/ITProvision (`docs/fun-identity/`)
  - `CardChecklist` + إثبات تسليم + soft-cancel + Lost/Renew + party revoke
  - أصل: حائز واحد + Lost/IsPrivileged + Transfer→InStorage
  - `ProvisioningStatus` Failed/Retry + فصل أجهزة؛ Auth SyncFromKeycloak كمسار Retry
  - [FUN_005_RECONCILIATION_CHECKLIST.md](../FUN_005_RECONCILIATION_CHECKLIST.md) + [FUN_005_EXCEPTION_PATHS.md](../FUN_005_EXCEPTION_PATHS.md)
  - [FUN_005_UAT_PACK.md](../FUN_005_UAT_PACK.md) ReadyForSign-off؛ [FUN_005_VERIFICATION.md](../FUN_005_VERIFICATION.md)؛ `Fun005*` tests

## FUN-006 — إغلاق الأرشيف والتحليلات والتقارير

- **الأولوية/الحجم/المسؤول:** P3 / L / Archive + Analytics + Reporting
- **الحالة:** **Done**
- **النطاق:** file plan/retention/legal hold/disposal؛ immutable archival evidence؛ analytics contracts تحفظ tenant/org attributes؛ late events/rebuild؛ source freshness؛ report definitions؛ row-level security؛ exports.
- **معايير القبول:** تقرير الفرع لا يعرض الإجمالي الوطني؛ الأرقام تتصالح مع المصادر؛ الأرشيف يثبت completeness/hash/custody؛ إعادة بناء projection تعطي النتيجة نفسها؛ حذف retention معتمد.
- **التحقق:** golden reports، cross-tenant BI test، replay rebuild، archive retrieval/disposal drill.
- **الاعتماديات:** GOV-001, PERF-004.
- **ما أُغلق:**
  - ADR + مصفوفة ArchiveLifecycle/AnalyticsProjection/BranchReportExport (`docs/fun-archive/`)
  - LegalHold + ArchiveChecklist + disposal Request/Approve (SoD) + custody/hash/completeness
  - `OrgUnitId` على AnalyticsEvent + Rebuild حتمي + freshness
  - Branch RLS على gap-analysis/headcount + ExportJob PERF-004 (Analytics + Archive)
  - [FUN_006_RECONCILIATION_CHECKLIST.md](../FUN_006_RECONCILIATION_CHECKLIST.md) + [FUN_006_EXCEPTION_PATHS.md](../FUN_006_EXCEPTION_PATHS.md)
  - [FUN_006_UAT_PACK.md](../FUN_006_UAT_PACK.md) ReadyForSign-off؛ [FUN_006_VERIFICATION.md](../FUN_006_VERIFICATION.md)؛ `Fun006*` tests

## REL-001 — Staging مماثل للإنتاج وبيانات اصطناعية

- **الأولوية/الحجم/المسؤول:** P3 / L / Release Engineering
- **الحالة:** **Done**
- **النطاق:** topology/config parity؛ masked/synthetic scale data؛ Keycloak roles؛ TLS/DNS؛ migrations؛ observability؛ scanners؛ feature flags؛ release candidate promotion؛ environment ownership.
- **معايير القبول:** لا اختلاف غير موثق عن الإنتاج؛ deploy من الصفر؛ جميع E2E/NFR/security tests تعمل؛ لا سر أو PII إنتاجي؛ reset قابل للتكرار.
- **التحقق:** parity diff، clean rebuild، release rehearsal.
- **الاعتماديات:** OPS-005, QA-004.
- **ما أُغلق:**
  - ADR + مصفوفة TopologyParity/SyntheticReset/ReleaseRehearsal (`docs/rel-staging/`)
  - TLS على staging ingress + tls-stub/backup-retention؛ `rel001-parity-diff`
  - `testdata/rel001/` + seed/reset؛ `realm-rel001-staging.json`؛ حظر prod dump
  - Feature flags + RACI + rehearsal checklist (يمدّ DEP-001)
  - [REL_001_RECONCILIATION_CHECKLIST.md](../REL_001_RECONCILIATION_CHECKLIST.md) + [REL_001_EXCEPTION_PATHS.md](../REL_001_EXCEPTION_PATHS.md)
  - [REL_001_UAT_PACK.md](../REL_001_UAT_PACK.md) ReadyForSign-off؛ [REL_001_VERIFICATION.md](../REL_001_VERIFICATION.md)؛ `Rel001*` tests

## REL-002 — Pilot مضبوط وقرار Go/No-Go للـPilot فقط

- **الأولوية/الحجم/المسؤول:** P3 / XL / Program Management
- **الحالة:** **Done (gate pack)** — ليس اعتماداً وطنياً؛ blockers وطنية موثّقة
- **النطاق:** جهة واحدة ونطاق بيانات/مستخدمين واضح؛ success/failure metrics؛ support؛ migration rehearsal؛ parallel run خاصة للرواتب؛ exit/rollback؛ incident/privacy contacts؛ daily reconciliation؛ independent security assessment. هذه البطاقة لا تمنح اعتماد إنتاج وطني.
- **معايير القبول:** جميع P0–P2 والقدرات الوطنية الداخلة في نطاق الـPilot مغلقة بلا استثناء غير معتمد؛ UAT وتدريب وتشغيل جاهزة؛ ثلاث دورات رواتب متوازية متطابقة أو فروقها مفسرة؛ restore وDR وpenetration tests ناجحة؛ قرار موقع متعدد الاختصاصات.
- **التحقق:** Pilot report، defect leakage، SLO، reconciliation، user acceptance، وGo/No-Go minutes.
- **الاعتماديات:** جميع P0–P2 الداخلة في النطاق, REL-001, UAT-001, TRN-001, MIG-006, ONB-001, SRE-001..003, SUP-002..004.
- **ما أُغلق:**
  - ADR + scope matrix + blocker register (`docs/rel-pilot/scope-matrix.json`)
  - قوالب أدلة: report / defect leakage / daily recon / SLO / readiness
  - harness رواتب متوازية 3 دورات اصطناعية + `rel002-payroll-parallel-compare.ps1`
  - Support contacts + DR/security binder + migration stub → MIG-006
  - [REL_002_PILOT_GO_NO_GO.md](../REL_002_PILOT_GO_NO_GO.md) + EXCEPTION / RECONCILIATION / UAT ReadyForSign-off / VERIFICATION
  - `Rel002*` unit tests
- **Blockers المتبقية (متعمدة):** UAT-001, TRN-001, MIG-006, ONB-001, SRE-001..003, SUP-002..004 = Blocked؛ REL-001 = Satisfied

## REL-003 — التشغيل والدعم وإدارة الحوادث والتغيير

- **الأولوية/الحجم/المسؤول:** P3 / L / Service Management + SRE
- **الحالة:** **Done** (Ops SM baseline pack — ليس مكتب 24×7 وطني)
- **النطاق:** service catalog/owners/on-call؛ L1-L3؛ incident/problem/change؛ severity/SLA؛ runbooks؛ status communication؛ maintenance؛ access reviews؛ patch cadence؛ postmortems بلا لوم؛ training؛ vendor dependencies.
- **معايير القبول:** لكل خدمة مالك وrunbook وescalation؛ مناوبة تستجيب لاختبار؛ التغييرات الحرجة بموافقة وتراجع؛ postmortem actions متتبعة؛ تدريب المستخدم والدعم مكتمل.
- **التحقق:** incident game day، access review، change rollback، وقياس MTTA/MTTR.
- **الاعتماديات:** OBS-002, OPS-004.
- **ما أُغلق:**
  - ADR + `docs/rel-ops/service-catalog.json` + severity/SLA + journey matrix
  - Incident / problem / change / status-maintenance processes؛ روابط تصعيد OBS → REL-003
  - On-call roster + `rel003-incident-game-day.ps1` + MTTA/MTTR ledger؛ صف `Q+REL003` في NFR calendar
  - Access review + patch cadence + postmortem template/actions + readiness (`blockedOn` TRN/SUP/SRE/OPS-007)
  - EXCEPTION / RECONCILIATION / UAT ReadyForSign-off / VERIFICATION؛ `Rel003*` tests
- **خارج العمق (موثّق):** SUP-002..004، SRE-001..003، OPS-007 وطني، status page وطني

## مخرجات بوابة P3

- مصفوفة متطلبات واختبارات واعتمادات قانونية كاملة.
- UAT موقع لكل رحلة أعمال، وليس لكل شاشة منفردة فقط.
- Staging مماثل للإنتاج ونتائج Pilot قابلة للتدقيق.
- قرار Go/No-Go للـPilot جماعي؛ الاعتماد الوطني لاحق عبر `NAT-001..008` ولا يملكه فريق التطوير منفرداً.

# بطاقات المنتج والهجرة والتكامل الوطني

هذه البطاقات تحول المشروع من تطبيق مُصلح إلى منتج حكومي قابل لاعتماد جهات متعددة دون نسخ الشيفرة لكل جهة.

## STR-001 — اعتماد نطاق الدولة ومغلف الحجم والخدمة

- **الأولوية/الحجم/المسؤول:** P0-National / L / Program Board + Product + Architecture
- **الحالة:** **Done (production scope binding)** — ليس CAP-001 ولا تعداد SYR-002
- **النطاق:** الدولة والجهات والفئات الوظيفية؛ active/history؛ مواسم الذروة؛ العملات والتقاويم واللغات؛ critical journeys؛ availability/RPO/RTO/performance؛ exclusions؛ توقع 5 سنوات.
- **معايير القبول:** قيم كمية معتمدة لكل مؤشر في نموذج الجاهزية؛ dataset/workload موصوفان؛ كل افتراض له مالك وتاريخ؛ لا تستخدم عبارة “national scale” دون هذه الوثيقة.
- **التحقق:** مراجعة مستقلة للسعة، مقارنة بأرقام المصادر الرسمية، وscenario review للنمو 2x.
- **الاعتماديات:** لا يوجد.
- **ما أُغلق:**
  - مصدر الحقيقة [`str-scope/envelope-matrix.json`](../str-scope/envelope-matrix.json) + [ADR-STR-001-production-scope-binding.md](../ADR-STR-001-production-scope-binding.md)
  - تشغيل: `NationalScopeOptions` / `INationalScopeProvider` / `INationalScopeGuard` في ServiceDefaults
  - تهيئة Gateway + Payroll + HR.Core (Staging/Production = `syria-interim`؛ منع national بلا flag)
  - حراسة سقف المستأجرين (seed) وسقف نافذة الرواتب؛ مقاييس `HRManagement.NationalScope`
  - `str001-envelope-runtime-validate.ps1` + [STR_001_ADOPTION_MINUTES.md](../STR_001_ADOPTION_MINUTES.md) + [STR_001_INDEPENDENT_REVIEW_WAIVER.md](../STR_001_INDEPENDENT_REVIEW_WAIVER.md)
  - `Str001ProductionBindingTests`
- **Blockers المتبقية:** SYR-002 (تعداد)، CAP-001 (إثبات حمل)

## STR-002 — كتالوج القوانين والضوابط للدولة المستهدفة

- **الأولوية/الحجم/المسؤول:** P0-National / L / Legal + Privacy + CISO + HR/Finance
- **الحالة:** **Done (production control binding)** — ليس SYR-001 ولا SOV-001 ATO حي
- **النطاق:** الخدمة المدنية والعمل والرواتب والضرائب والتقاعد والخصوصية والأرشيف والتوقيع والإثبات والوصولية والسيادة؛ control owner/evidence/frequency/exceptions.
- **معايير القبول:** كل متطلب محلي مربوط ببطاقة/ضابط/اختبار؛ التعارض أو الغموض له قرار موقع؛ آلية متابعة التغيير التشريعي؛ لا `LEGAL_TBD` في release.
- **التحقق:** legal control walkthrough وعينة trace من نص قانوني إلى config واختبار وتقرير.
- **الاعتماديات:** STR-001, GOV-001, GOV-002.
- **ما أُغلق (تشغيل):**
  - [`LegalControl/*`](../../src/Shared/HRManagement.ServiceDefaults/LegalControl/) — Options/Provider/Guard/StartupValidator/Metrics
  - نسخ [`control-catalog.json`](../str-legal/control-catalog.json) + [`legislative-watch-stamp.json`](../str-legal/legislative-watch-stamp.json) إلى خرج التطبيق
  - `AddLegalControl()` في Gateway / Payroll.Web / HR.Core.Web؛ Staging/Production `ReleaseProfile=Interim` + `EnforceOnStartup=true`
  - حراسة بدء دورة الرواتب عبر `IPayrollLegalControlGuard`
  - [`str002-control-runtime-validate.ps1`](../../scripts/str002-control-runtime-validate.ps1) + توسيع `check-no-legal-tbd.py` + خطوة CI
  - [ADR-STR-002-production-control-binding.md](../ADR-STR-002-production-control-binding.md)؛ [STR_002_ADOPTION_MINUTES.md](../STR_002_ADOPTION_MINUTES.md)؛ [STR_002_INDEPENDENT_REVIEW_WAIVER.md](../STR_002_INDEPENDENT_REVIEW_WAIVER.md)
  - `Str002ProductionBindingTests`
- **ما أُغلق سابقاً (pack):** ADR كتالوج + domain matrix + traces + `str002-control-catalog-validate.ps1` + UAT/VERIFICATION pack
- **Blockers المتبقية:** SYR-001 = Blocked؛ SOV-001 = Satisfied (production control-baseline binding — ليس ATO حي)؛ عيّنة SYD-001/004/008/009/011 = Blocked-Legal / OPEN-NO-GO؛ `CTRL-*` Blocked-Legal تبقى
## STR-003 — كتالوج الخدمة والوحدات ومستوياتها

- **الأولوية/الحجم/المسؤول:** P1-National / M / Product Management
- **الحالة:** **Done (production module binding)** — بلا سوق تراخيص ولا ONB آلي
- **النطاق:** modules/capabilities/personas؛ mandatory/optional؛ service tier؛ owner/SLA/support؛ dependency؛ licensing إن وجد؛ sunset؛ readiness per module.
- **معايير القبول:** الجهة تعرف ما تحصل عليه وما لا تحصل عليه؛ لا وحدة “متاحة” قبل UAT وrunbook وSLO؛ feature flags لا تكشف وظائف غير معتمدة.
- **التحقق:** agency contract walkthrough، module readiness evidence، وservice catalog audit.
- **الاعتماديات:** STR-001, REL-003.
- **ما أُغلق (تشغيل):**
  - [`ModuleCatalog/*`](../../src/Shared/HRManagement.ServiceDefaults/ModuleCatalog/) — Options/Provider/Guard/StartupValidator/Metrics
  - `AddModuleCatalog()` في Gateway / Payroll.Web / HR.Core.Web؛ Gateway `EnforcePathEntitlement=true` في Staging/Production
  - [`GatewayModuleCatalog.cs`](../../src/Gateway/HRManagement.Gateway/Services/GatewayModuleCatalog.cs) يصفّي عبر `IModuleEntitlementGuard`
  - Path entitlement في `GatewayPathAuthorizationMiddleware`
  - [`str003-module-runtime-validate.ps1`](../../scripts/str003-module-runtime-validate.ps1) + خطوة CI
  - [ADR-STR-003-production-module-binding.md](../ADR-STR-003-production-module-binding.md)؛ [STR_003_ADOPTION_MINUTES.md](../STR_003_ADOPTION_MINUTES.md)؛ [STR_003_INDEPENDENT_REVIEW_WAIVER.md](../STR_003_INDEPENDENT_REVIEW_WAIVER.md)
  - `Str003ProductionBindingTests`
- **ما أُغلق سابقاً (pack):** ADR كتالوج + MODULE_READINESS / AGENCY_CONTRACT / FEATURE_FLAG_GATE + `str003-module-catalog-validate.ps1` + UAT/VERIFICATION
- **Blockers المتبقية:** ONB-001، سوق تراخيص (soft)

## STR-004 — سياسة منتج واحد بلا Fork لكل جهة

- **الأولوية/الحجم/المسؤول:** P1-National / L / Product Architecture
- **الحالة:** **Done (production nofork binding)** — PROD-001 Satisfied؛ ONB-001 Blocked موثّق
- **النطاق:** تحديد ما يُضبط كبيانات/سياسة/template/workflow/branding/integration؛ extension points؛ منع tenant branches؛ configuration promotion؛ versioning؛ compatibility؛ limits.
- **معايير القبول:** إعداد جهتين مختلفتين من release واحد؛ لا conditional code باسم جهة؛ config موقعة ومراجعة وقابلة للتراجع؛ upgrade لا يمحو التخصيص.
- **التحقق:** onboarding لجهتين متعارضتي السياسات، upgrade rehearsal، وrepository rule يمنع forks.
- **الاعتماديات:** STR-002, TEN-003, PROD-001.
- **ما أُغلق (تشغيل):**
  - [`NoFork/*`](../../src/Shared/HRManagement.ServiceDefaults/NoFork/) — Options/Provider/Resolver/StartupValidator/Metrics/Branding
  - حزم [`testdata/str004/agencies`](../../testdata/str004/agencies) بنفس `releaseDigest` + `rollbackRef`
  - تركيب agency pack مع [`ModuleEntitlementGuard`](../../src/Shared/HRManagement.ServiceDefaults/ModuleCatalog/ModuleCatalogGuard.cs)
  - `AddNoForkPolicy()` في Gateway / Payroll / HR.Core؛ Staging/Production `EnforceOnStartup=true`
  - [`str004-nofork-runtime-validate.ps1`](../../scripts/str004-nofork-runtime-validate.ps1) + خطوة CI
  - [ADR-STR-004-production-nofork-binding.md](../ADR-STR-004-production-nofork-binding.md)؛ [STR_004_ADOPTION_MINUTES.md](../STR_004_ADOPTION_MINUTES.md)؛ [STR_004_INDEPENDENT_REVIEW_WAIVER.md](../STR_004_INDEPENDENT_REVIEW_WAIVER.md)
  - `Str004ProductionBindingTests`
- **ما أُغلق سابقاً (pack):** ADR + matrix + CONFIG_TAXONOMY / EXTENSION_POINTS / dual-agency / promotion stub + `str004-nofork-validate.ps1`
- **Blockers المتبقية:** PROD-001 = Satisfied؛ ONB-001 = Blocked؛ STR-002/TEN-003 = Satisfied

## STR-005 — حوكمة البرنامج ومجلس المعمارية والقرار

- **الحالة:** Done (production governance binding)
- **الأولوية/الحجم/المسؤول:** P0-National / M / Program Sponsor
- **النطاق:** decision rights؛ architecture/security/data/change boards؛ ADR؛ risk register؛ exception expiry؛ dependency management؛ milestone evidence؛ conflict escalation؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** لكل قرار حرج مالك ومراجع؛ الاستثناءات مؤقتة ومعلنة؛ لا Go-Live بقرار فريق التطوير منفرداً؛ القرارات قابلة للتدقيق.
- **التحقق:** محاكاة قرار تغيير قانون/حادث/تأخير، ومراجعة سجل القرارات والمخاطر؛ فشل إقلاع على استثناء منتهٍ أو eng-only عند `GoLiveDeclared`.
- **الاعتماديات:** لا يوجد.
- **ما أُغلق:**
  - ADR + [`str-governance/governance-matrix.json`](../str-governance/governance-matrix.json) + DECISION_RIGHTS + JOURNEY_MATRIX
  - decision-log + risk-register + EXCEPTION / CONFLICT / MILESTONE
  - محاكيات law/incident/delay + GO_LIVE_GATE (eng-only FAIL)
  - `ProgramGovernance/` في ServiceDefaults + `AddProgramGovernance` على Gateway/Payroll/HR.Core
  - `str005-governance-validate.ps1` (شكل) + `str005-governance-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-STR-005-production-governance-binding.md](../ADR-STR-005-production-governance-binding.md)؛ [STR_005_ADOPTION_MINUTES.md](../STR_005_ADOPTION_MINUTES.md)؛ [STR_005_INDEPENDENT_REVIEW_WAIVER.md](../STR_005_INDEPENDENT_REVIEW_WAIVER.md)
  - [STR_005_UAT_PACK.md](../STR_005_UAT_PACK.md) ReadyForSign-off؛ [STR_005_VERIFICATION.md](../STR_005_VERIFICATION.md)؛ `Str005ProductionBindingTests`
- **Blockers المتبقية:** SYD-017 = Blocked (أسماء الموقعين)؛ PGM-001 = NotStarted (مستهلك لاحق)

## STR-006 — ملكية دائمة وتمويل ومهارات وخطة موردين

- **الحالة:** Done (production ownership binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Sponsor + Finance + HR + Procurement
- **النطاق:** product/service/data/security owners؛ staffing 24/7؛ skill matrix؛ succession؛ budget 5 سنوات؛ vendor/SLA؛ IP/source ownership؛ knowledge transfer؛ end-of-life؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** لا مكون حرج بلا بديل لشخص واحد؛ تمويل التشغيل والأمن والنسخ لا التطوير فقط؛ عقود الموردين تدعم RTO والإفصاح عن الحوادث والخروج.
- **التحقق:** bus-factor exercise، staffing/on-call simulation، وTCO approval؛ فشل إقلاع على busFactor منخفض أو مورد بلا Exit أو ادعاء 24×7 وطني مع SRE-001 Blocked.
- **الاعتماديات:** STR-005, SOV-007, FIN-001, SRE-001.
- **ما أُغلق:**
  - ADR + [`str-ownership/ownership-matrix.json`](../str-ownership/ownership-matrix.json) + JOURNEY + OWNERSHIP_MATRIX
  - skill/succession/staffing + budget 5y stub + TCO role approval + vendor clauses + IP/KT/EOL
  - bus-factor / staffing / TCO / vendor fixtures؛ `str006-ownership-validate.ps1` (شكل)
  - `Ownership/` في ServiceDefaults + `AddOwnership` على Gateway/Payroll/HR.Core
  - `str006-ownership-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-STR-006-production-ownership-binding.md](../ADR-STR-006-production-ownership-binding.md)؛ [STR_006_ADOPTION_MINUTES.md](../STR_006_ADOPTION_MINUTES.md)؛ [STR_006_INDEPENDENT_REVIEW_WAIVER.md](../STR_006_INDEPENDENT_REVIEW_WAIVER.md)
  - [STR_006_UAT_PACK.md](../STR_006_UAT_PACK.md) ReadyForSign-off؛ [STR_006_VERIFICATION.md](../STR_006_VERIFICATION.md)؛ `Str006ProductionBindingTests`
- **Blockers المتبقية:** STR-005 = Satisfied؛ SOV-007 = Satisfied (vendor-exit binding Done؛ live exit drill = VEN-001)؛ FIN-001 = NotStarted؛ SRE-001 = Blocked

## STR-007 — مؤشرات القيمة والمنافسة وخط الأساس

- **الحالة:** Done (production value binding)
- **الأولوية/الحجم/المسؤول:** P1-National / M / Product + Audit
- **النطاق:** زمن التعيين والفصل والراتب؛ نسبة self-service؛ أخطاء/تكرار البيانات؛ رضا المستخدم؛ uptime؛ cost/employee؛ onboarding time؛ adoption؛ baseline من الأنظمة الحالية؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** KPI له تعريف ومصدر ومالك وهدف ومنع تلاعب؛ baseline قبل pilot؛ benefits لا تقاس بعدد الشاشات؛ dashboard يحفظ tenant/privacy.
- **التحقق:** independent measurement design، sample reconciliation، وquarterly benefit review؛ فشل إقلاع على owner==auditor أو baseline بلا freeze أو LiveDashboardClaim.
- **الاعتماديات:** STR-001, STR-003, GOV-003.
- **ما أُغلق:**
  - ADR + [`str-value/kpi-matrix.json`](../str-value/kpi-matrix.json) + JOURNEY + KPI_DEFINITIONS
  - baseline / anti-gaming / screen-count ban / dashboard privacy + measurement/recon/quarterly protocols
  - fixtures؛ `str007-value-kpi-validate.ps1` (شكل)
  - `ValueKpi/` في ServiceDefaults + `AddValueKpi` على Gateway/Payroll/HR.Core
  - `str007-value-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-STR-007-production-value-binding.md](../ADR-STR-007-production-value-binding.md)؛ [STR_007_ADOPTION_MINUTES.md](../STR_007_ADOPTION_MINUTES.md)؛ [STR_007_INDEPENDENT_REVIEW_WAIVER.md](../STR_007_INDEPENDENT_REVIEW_WAIVER.md)
  - [STR_007_UAT_PACK.md](../STR_007_UAT_PACK.md) ReadyForSign-off؛ [STR_007_VERIFICATION.md](../STR_007_VERIFICATION.md)؛ `Str007ProductionBindingTests`
- **Blockers المتبقية:** STR-001/STR-003/GOV-003 = Satisfied؛ FIN-001 = NotStarted؛ PROD-003 = Satisfied

## MIG-001 — جرد الأنظمة القديمة ومصادر البيانات وملكية الحقول

- **الحالة:** Done (production inventory binding)
- **الأولوية/الحجم/المسؤول:** P0-National / L / Migration Office + Agencies
- **النطاق:** قواعد/ملفات/ورق/APIs؛ owner؛ volume/quality/encoding؛ keys؛ history؛ attachments؛ legal retention؛ availability؛ freeze constraints؛ unsupported systems؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** 100% من المصادر في inventory أو استثناء موقع؛ كل حقل مستهدف له source of truth؛ أحجام وجودة فعلية لا تقديرات فقط؛ مخاطر الوصول موثقة.
- **التحقق:** source sampling، row/file counts، profiling report موقع من الجهة؛ فشل إقلاع على estimateOnly Inventoried أو SoT مفقود أو LiveAgencyExtractClaim.
- **الاعتماديات:** STR-001, GOV-003.
- **ما أُغلق:**
  - ADR + [`mig-inventory/source-inventory.json`](../mig-inventory/source-inventory.json) + JOURNEY + SOURCE_CATALOG + FIELD_SOT
  - ACCESS_RISKS / FREEZE / UNSUPPORTED / PROFILING_PROTOCOL
  - sampling/counts/profiling fixtures؛ `mig001-source-inventory-validate.ps1` (شكل)
  - `MigInventory/` في ServiceDefaults + `AddMigInventory` على Gateway/Payroll/HR.Core
  - `mig001-inventory-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-001-production-inventory-binding.md](../ADR-MIG-001-production-inventory-binding.md)؛ [MIG_001_ADOPTION_MINUTES.md](../MIG_001_ADOPTION_MINUTES.md)؛ [MIG_001_INDEPENDENT_REVIEW_WAIVER.md](../MIG_001_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_001_UAT_PACK.md](../MIG_001_UAT_PACK.md) ReadyForSign-off؛ [MIG_001_VERIFICATION.md](../MIG_001_VERIFICATION.md)؛ `Mig001ProductionBindingTests`
- **Blockers المتبقية:** STR-001/GOV-003 = Satisfied؛ agency-extract-access = Blocked؛ SYR-002 = Blocked

## MIG-002 — النموذج القانوني الموحد وخرائط الأكواد

- **الحالة:** Done (production codemap binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Data Architecture + HR/Finance
- **النطاق:** canonical employee/org/job/payroll/document model؛ code mappings؛ calendar/timezone/locale؛ identifiers؛ effective dates؛ provenance؛ unmapped values؛ versioning؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** كل قيمة قديمة map/transform/quarantine؛ لا “other” يخفي خطأ جماعياً؛ mapping موقع وقابل للتكرار؛ المعرف القديم محفوظ كمرجع لا كمفتاح أمني.
- **التحقق:** mapping coverage، round-trip samples، legal/business sign-off، فشل إقلاع على mass Other / disposition ناقص / legacy-id كـ auth / LiveCodeMapEngineClaim.
- **الاعتماديات:** MIG-001, TEN-001, GOV-003.
- **ما أُغلق:**
  - ADR + [`mig-canonical/code-map-matrix.json`](../mig-canonical/code-map-matrix.json) + JOURNEY + CANONICAL_MODEL + CODE_MAPS
  - UNMAPPED_QUARANTINE / LEGACY_ID_POLICY / LOCALE_CALENDAR
  - coverage/roundtrip/fail/signoff fixtures؛ `mig002-code-map-validate.ps1`؛ CODEOWNERS
  - `MigCanonical/` في ServiceDefaults + `AddMigCanonical` على Gateway/Payroll/HR.Core
  - `mig002-codemap-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-002-production-codemap-binding.md](../ADR-MIG-002-production-codemap-binding.md)؛ [MIG_002_ADOPTION_MINUTES.md](../MIG_002_ADOPTION_MINUTES.md)؛ [MIG_002_INDEPENDENT_REVIEW_WAIVER.md](../MIG_002_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_002_UAT_PACK.md](../MIG_002_UAT_PACK.md) ReadyForSign-off؛ [MIG_002_VERIFICATION.md](../MIG_002_VERIFICATION.md)؛ `Mig002ProductionBindingTests`
- **Blockers المتبقية:** MIG-001/TEN-001/GOV-003 = Satisfied؛ agency-extract-access / SYR-003 / MDM-001 = Blocked

## MIG-003 — مصنع هجرة آمن قابل للإعادة

- **الحالة:** Done (production factory binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Migration Engineering
- **النطاق:** extract staging zones؛ encryption؛ schema validation؛ transform؛ quarantine؛ idempotent load؛ checkpoint/restart؛ observability؛ secrets؛ versioned pipelines؛ purge staging؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** إعادة نفس الدفعة لا تكرر؛ فشل المنتصف يستأنف؛ كل record له lineage؛ لا PII في logs؛ throughput يحقق نافذة cutover.
- **التحقق:** failure injection، rerun، corrupted input، performance، ومراجعة secure staging، فشل إقلاع على zone غير مشفّرة / idempotency ضعيفة / LiveEtlWorkersClaim.
- **الاعتماديات:** MIG-002, TEN-002, OPS-005.
- **ما أُغلق:**
  - ADR + [`mig-factory/pipeline-matrix.json`](../mig-factory/pipeline-matrix.json) + JOURNEY + STAGING_ZONES
  - IDEMPOTENT_LOAD / CHECKPOINT_RESUME / RECORD_LINEAGE / PII_LOGGING / SECRETS_AND_PURGE
  - scenarios/perf/review/harness fixtures؛ `mig003-factory-validate.ps1`؛ CODEOWNERS
  - `MigFactory/` في ServiceDefaults + `AddMigFactory` على Gateway/Payroll/HR.Core
  - `mig003-factory-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-003-production-factory-binding.md](../ADR-MIG-003-production-factory-binding.md)؛ [MIG_003_ADOPTION_MINUTES.md](../MIG_003_ADOPTION_MINUTES.md)؛ [MIG_003_INDEPENDENT_REVIEW_WAIVER.md](../MIG_003_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_003_UAT_PACK.md](../MIG_003_UAT_PACK.md) ReadyForSign-off؛ [MIG_003_VERIFICATION.md](../MIG_003_VERIFICATION.md)؛ `Mig003ProductionBindingTests`
- **Blockers المتبقية:** MIG-002/TEN-002/OPS-005 = Satisfied؛ agency-extract-access / live-cutover-perf = Blocked
- **لاحقاً (خارج هذا الربط):** MIG-003a–d عمال ETL حيّة

## MIG-004 — مطابقة الهوية وإزالة التكرار والدمج القابل للعكس

- **الحالة:** Done (production match binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Data Quality + Privacy
- **النطاق:** deterministic/probabilistic matching؛ national ID exceptions؛ twins/name variants؛ false positive controls؛ human review؛ merge/unmerge؛ survivor rules؛ fraud indicators؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** thresholds مقاسة وموقعة؛ لا auto-merge عالي المخاطر بلا دليل؛ كل merge مؤرخ وقابل للعكس؛ عدم كشف بيانات جهة للمراجع غير المخول.
- **التحقق:** labelled dataset، precision/recall targets، adversarial identity cases، فشل إقلاع على high-risk AutoMerge / merge غير reversible / LiveIdentityMatchClaim.
- **الاعتماديات:** MIG-002, DATA-002, GOV-001.
- **ما أُغلق:**
  - ADR + [`mig-match/match-matrix.json`](../mig-match/match-matrix.json) + JOURNEY + MATCH_STRATEGIES
  - THRESHOLDS / NATIONAL_ID_EXCEPTIONS / HUMAN_REVIEW / MERGE_UNMERGE / FRAUD_INDICATORS / REVIEW_PRIVACY
  - labelled/metrics/adversarial/scenarios fixtures؛ `mig004-match-validate.ps1`؛ CODEOWNERS
  - `MigMatch/` في ServiceDefaults + `AddMigMatch` على Gateway/Payroll/HR.Core
  - `mig004-match-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-004-production-match-binding.md](../ADR-MIG-004-production-match-binding.md)؛ [MIG_004_ADOPTION_MINUTES.md](../MIG_004_ADOPTION_MINUTES.md)؛ [MIG_004_INDEPENDENT_REVIEW_WAIVER.md](../MIG_004_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_004_UAT_PACK.md](../MIG_004_UAT_PACK.md) ReadyForSign-off؛ [MIG_004_VERIFICATION.md](../MIG_004_VERIFICATION.md)؛ `Mig004ProductionBindingTests`
- **Blockers المتبقية:** MIG-002/DATA-002/GOV-001/GOV-003-merge = Satisfied؛ agency-extract-access / live-labelled-metrics = Blocked؛ L10N-002 = NotStarted
- **لاحقاً (خارج هذا الربط):** MIG-004a–d matcher حي

## MIG-005 — المصالحة وسلسلة إثبات الهجرة

- **الحالة:** Done (production evidence binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Migration QA + Internal Audit
- **النطاق:** counts/sums/hashes؛ payroll/control totals؛ attachments؛ relationships؛ rejects؛ lineage؛ dual control؛ evidence package؛ sign-off per agency/wave؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** 100% accounted as migrated/rejected/deferred بسبب؛ totals المالية بلا فرق غير مفسر؛ عينات أعمال معتمدة؛ الأدلة غير قابلة للتعديل.
- **التحقق:** independent SQL/reports، random/statistical samples، file checksum reconciliation، فشل إقلاع على variance / dual-control / immutable / LiveAgencyReconClaim.
- **الاعتماديات:** MIG-003, OBS-003.
- **ما أُغلق:**
  - ADR + [`mig-evidence/recon-matrix.json`](../mig-evidence/recon-matrix.json) + JOURNEY + ACCOUNTED_BUCKETS
  - FINANCIAL_CONTROLS / LINEAGE_AND_REJECTS / EVIDENCE_PACKAGE / DUAL_CONTROL / SAMPLING / AGENCY_WAVE_SIGNOFF
  - scenarios/recon/samples/harness fixtures؛ `mig005-evidence-validate.ps1`؛ CODEOWNERS
  - `MigEvidence/` في ServiceDefaults + `AddMigEvidence` على Gateway/Payroll/HR.Core
  - `mig005-evidence-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-005-production-evidence-binding.md](../ADR-MIG-005-production-evidence-binding.md)؛ [MIG_005_ADOPTION_MINUTES.md](../MIG_005_ADOPTION_MINUTES.md)؛ [MIG_005_INDEPENDENT_REVIEW_WAIVER.md](../MIG_005_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_005_UAT_PACK.md](../MIG_005_UAT_PACK.md) ReadyForSign-off؛ [MIG_005_VERIFICATION.md](../MIG_005_VERIFICATION.md)؛ `Mig005ProductionBindingTests`
- **Blockers المتبقية:** MIG-003/OBS-003 = Satisfied؛ agency-extract-access / live-agency-recon / live-independent-sql = Blocked
- **لاحقاً (خارج هذا الربط):** MIG-005a–d recon حي / vault / SQL مستقل

## MIG-006 — تدريبات الهجرة والتعايش والقطع والتراجع

- **الحالة:** Done (production cutover binding)
- **الأولوية/الحجم/المسؤول:** P2-National / XL / Cutover Command
- **النطاق:** ثلاث rehearsals على الأقل؛ change freeze؛ delta capture؛ parallel read/run؛ user lock؛ DNS؛ communication؛ rollback window؛ legacy read-only؛ post-cutover reconciliation؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** rehearsal ضمن النافذة وبنفس الحجم؛ rollback مجرب لا مكتوب فقط؛ نقطة قرار Go/No-Go واضحة؛ لا فقد تغييرات أثناء delta؛ legacy access مؤمّن.
- **التحقق:** timed rehearsal، surprise rollback، reconciliation T+0/T+1/T+30، فشل إقلاع على rehearsals ناقصة / paper-only rollback / delta loss / LiveAgencyCutoverClaim.
- **الاعتماديات:** MIG-005, OPS-003, REL-001.
- **ما أُغلق:**
  - ADR + [`mig-cutover/cutover-matrix.json`](../mig-cutover/cutover-matrix.json) + JOURNEY + REHEARSAL_PLAYBOOK
  - CHANGE_FREEZE / DELTA / PARALLEL_AND_LOCK / ROLLBACK / GO_NOGO / POST_CUTOVER_RECON
  - rehearsals/scenarios/decision/access/recon fixtures؛ `mig006-cutover-validate.ps1`؛ CODEOWNERS؛ join REL-002
  - `MigCutover/` في ServiceDefaults + `AddMigCutover` على Gateway/Payroll/HR.Core
  - `mig006-cutover-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-006-production-cutover-binding.md](../ADR-MIG-006-production-cutover-binding.md)؛ [MIG_006_ADOPTION_MINUTES.md](../MIG_006_ADOPTION_MINUTES.md)؛ [MIG_006_INDEPENDENT_REVIEW_WAIVER.md](../MIG_006_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_006_UAT_PACK.md](../MIG_006_UAT_PACK.md) ReadyForSign-off؛ [MIG_006_VERIFICATION.md](../MIG_006_VERIFICATION.md)؛ `Mig006ProductionBindingTests`
- **Blockers المتبقية:** MIG-005/OPS-003/REL-001 = Satisfied؛ agency-extract-access / live-cutover-perf / live-agency-recon / live-rehearsal-at-scale = Blocked
- **لاحقاً (خارج هذا الربط):** MIG-006a–d قطع جهة حي / DNS إنتاجي / rehearsal بحجم جهة

## MIG-007 — مصنع إدخال الجهات واعتماد جاهزيتها

- **الحالة:** Done (production onboarding binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Agency Onboarding Office
- **النطاق:** readiness assessment؛ tenant/config/roles/integrations/data/training/support؛ standard playbook؛ sandbox؛ certification؛ wave criteria؛ onboarding telemetry؛ exit/rollback؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** جهة جديدة تدخل دون fork وبزمن مستهدف؛ checklist آلية؛ لا wave قبل sign-offs؛ مسؤول بيانات وأمن وتشغيل في كل جهة.
- **التحقق:** onboarding جهتين مختلفتين end-to-end، وقياس lead time/defects، فشل إقلاع على nofork / wave بلا sign-off / gate ناقص / LiveDualAgencyOnboardClaim.
- **الاعتماديات:** MIG-006, STR-004, EXT-006, PROD-002.
- **ما أُغلق:**
  - ADR + [`mig-onboarding/onboarding-matrix.json`](../mig-onboarding/onboarding-matrix.json) + JOURNEY + READINESS_ASSESSMENT
  - PLAYBOOK / SANDBOX_CERT / WAVE_CRITERIA / OWNERS_AND_ROLES / TELEMETRY / EXIT_ROLLBACK
  - checklist/agencies/metrics/scenarios fixtures؛ `mig007-onboarding-validate.ps1`؛ CODEOWNERS
  - `MigOnboarding/` في ServiceDefaults + `AddMigOnboarding` على Gateway/Payroll/HR.Core
  - `mig007-onboarding-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-MIG-007-production-onboarding-binding.md](../ADR-MIG-007-production-onboarding-binding.md)؛ [MIG_007_ADOPTION_MINUTES.md](../MIG_007_ADOPTION_MINUTES.md)؛ [MIG_007_INDEPENDENT_REVIEW_WAIVER.md](../MIG_007_INDEPENDENT_REVIEW_WAIVER.md)
  - [MIG_007_UAT_PACK.md](../MIG_007_UAT_PACK.md) ReadyForSign-off؛ [MIG_007_VERIFICATION.md](../MIG_007_VERIFICATION.md)؛ `Mig007ProductionBindingTests`
- **Blockers المتبقية:** MIG-006/STR-004 = Satisfied؛ EXT-006 = NotStarted؛ PROD-002 = Satisfied؛ ONB-001 / agency-extract-access / live-dual-agency-onboard = Blocked
- **لاحقاً (خارج هذا الربط):** MIG-007a–d dual-agency حي / EXT-006 portal / PROD-002 self-admin / ONB-001 provision

## EXT-001 — اتحاد الهوية الحكومية ومستويات الضمان

- **الحالة:** Done (production federation binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / National IAM + Security
- **النطاق:** identity proofing/AAL/FAL؛ federation trust؛ MFA phishing-resistant للأدوار الحساسة؛ agency IdPs؛ lifecycle؛ revocation؛ break-glass؛ citizen/employee separation؛ attribute authority؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** مستوى ضمان لكل persona/risk؛ interop مع IdP وطني وIdP جهة؛ revocation ضمن SLA؛ لا account linking بالاسم فقط؛ privacy-preserving identifiers.
- **التحقق:** federation conformance، lost/compromised credential، IdP outage/failover، وfraud cases؛ فشل إقلاع على IdP حي / name-only linking / High@AAL1 / LiveFederationClaim.
- **الاعتماديات:** IAM-001, IAM-003, IAM-004, STR-002.
- **ما أُغلق:**
  - ADR + [`ext-federation/assurance-matrix.json`](../ext-federation/assurance-matrix.json) + JOURNEY + ASSURANCE_LEVELS
  - FEDERATION_TRUST / MFA_POLICY / REVOCATION_AND_LIFECYCLE / BREAK_GLASS / CITIZEN_EMPLOYEE_SEPARATION / ATTRIBUTE_AUTHORITY
  - signoff/interop/scenarios/adversarial fixtures؛ `ext001-federation-validate.ps1`؛ CODEOWNERS
  - `ExtFederation/` في ServiceDefaults + `AddExtFederation` على Gateway/Payroll/HR.Core
  - `ext001-federation-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-001-production-federation-binding.md](../ADR-EXT-001-production-federation-binding.md)؛ [EXT_001_ADOPTION_MINUTES.md](../EXT_001_ADOPTION_MINUTES.md)؛ [EXT_001_INDEPENDENT_REVIEW_WAIVER.md](../EXT_001_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_001_UAT_PACK.md](../EXT_001_UAT_PACK.md) ReadyForSign-off؛ [EXT_001_VERIFICATION.md](../EXT_001_VERIFICATION.md)؛ `Ext001ProductionBindingTests`
- **Blockers المتبقية:** IAM-003/IAM-004/STR-002 = Satisfied؛ IAM-001 / national-idp-live / agency-idp-live / phishing-mfa-fleet / live-federation-conformance = Blocked
- **لاحقاً (خارج هذا الربط):** EXT-001a–d IdP حي / phishing-MFA fleet / conformance حي

## EXT-002 — تكامل السجل المدني أو مصدر إثبات الشخص

- **الحالة:** Done (production civil-registry binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Integration + Privacy
- **النطاق:** consent/legal purpose؛ minimal query؛ match response؛ caching/expiry؛ corrections؛ unavailable mode؛ rate limits؛ audit؛ no bulk harvesting؛ mock/sandbox؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** لا تخزن حقول غير لازمة؛ الاستخدام مرتبط بالغرض؛ mismatch له workflow بشري؛ outage لا يحول بيانات غير موثقة إلى حقيقة؛ كل query مدقق.
- **التحقق:** contract/conformance، abuse enumeration، stale/correction، outage tests؛ فشل إقلاع على consent/liveMoi/bulkHarvest/forbidUnverified / LiveMoiClaim.
- **الاعتماديات:** EXT-001, GOV-001, MIG-004.
- **ما أُغلق:**
  - ADR + [`ext-civil/civil-registry-matrix.json`](../ext-civil/civil-registry-matrix.json) + JOURNEY + PURPOSE_AND_CONSENT
  - MINIMAL_QUERY / MATCH_AND_MISMATCH / CACHE_AND_CORRECTIONS / UNAVAILABLE_MODE / RATE_LIMIT_AND_AUDIT / MOCK_SANDBOX
  - scenarios/audit/harness fixtures؛ `ext002-civil-registry-validate.ps1`؛ CODEOWNERS
  - `ExtCivil/` في ServiceDefaults + `AddExtCivil` على Gateway/Payroll/HR.Core
  - `ext002-civil-registry-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-002-production-civil-binding.md](../ADR-EXT-002-production-civil-binding.md)؛ [EXT_002_ADOPTION_MINUTES.md](../EXT_002_ADOPTION_MINUTES.md)؛ [EXT_002_INDEPENDENT_REVIEW_WAIVER.md](../EXT_002_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_002_UAT_PACK.md](../EXT_002_UAT_PACK.md) ReadyForSign-off؛ [EXT_002_VERIFICATION.md](../EXT_002_VERIFICATION.md)؛ `Ext002ProductionBindingTests`
- **Blockers المتبقية:** EXT-001/GOV-001/MIG-004 = Satisfied؛ moi-civil-registry-live / live-sandbox-cert / national-idp-live = Blocked؛ L10N-002 = NotStarted
- **لاحقاً (خارج هذا الربط):** EXT-002a–d MOI حي / sandbox معتمد / rate-audit sink / correction UI

## EXT-003 — تكامل الخزانة والبنوك والدفع والمصالحة

- **الحالة:** Done (production treasury-payment binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Payroll + Finance Integration
- **النطاق:** payment orders؛ signatures؛ files/APIs؛ acknowledgments؛ return/reject؛ bank account verification؛ settlement؛ reconciliation؛ duplicate prevention؛ cutoff/calendar؛ contingency؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** كل أمر دفع مرتبط بتشغيل معتمد؛ exactly-once business effect؛ totals end-to-end؛ rejection يعاد بمسار تصحيح؛ لا ملف دفع يدوي غير مدقق.
- **التحقق:** certification sandbox، duplicate/delay/out-of-order، partial rejection، parallel payroll run؛ فشل إقلاع على approvedRun/exactlyOnce/recon/manual/cutoff / LiveTreasuryClaim.
- **الاعتماديات:** PAY-003, IAM-003, EXT-004, INT-003.
- **ما أُغلق:**
  - ADR + [`ext-treasury/treasury-payment-matrix.json`](../ext-treasury/treasury-payment-matrix.json) + JOURNEY + PAYMENT_ORDER_BINDING
  - SIGNATURES_AND_CHANNEL / ACK_REJECT_RETURN / BANK_VERIFY_AND_SETTLEMENT / RECONCILIATION_AND_IDEMPOTENCY / CUTOFF_AND_CONTINGENCY / MOCK_SANDBOX
  - scenarios/audit/harness fixtures؛ `ext003-treasury-payment-validate.ps1`؛ CODEOWNERS
  - `ExtTreasury/` في ServiceDefaults + `AddExtTreasury` على Gateway/Payroll/HR.Core
  - `ext003-treasury-payment-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-003-production-treasury-binding.md](../ADR-EXT-003-production-treasury-binding.md)؛ [EXT_003_ADOPTION_MINUTES.md](../EXT_003_ADOPTION_MINUTES.md)؛ [EXT_003_INDEPENDENT_REVIEW_WAIVER.md](../EXT_003_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_003_UAT_PACK.md](../EXT_003_UAT_PACK.md) ReadyForSign-off؛ [EXT_003_VERIFICATION.md](../EXT_003_VERIFICATION.md)؛ `Ext003ProductionBindingTests`
- **Blockers المتبقية:** PAY-003/IAM-003/INT-003 = Satisfied؛ EXT-004 = NotStarted؛ treasury-live / bank-sandbox-cert / postal-channel-live = Blocked
- **لاحقاً (خارج هذا الربط):** EXT-003a–e MOF/بنك/بريد حي / ProcessPaymentBatch / SYR-011/PAY-004 / PKI حي

## EXT-004 — PKI والتوقيع والختم الزمني الوطني

- **الحالة:** Done (production pki-signature binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / PKI + Legal + Security
- **النطاق:** document/approval signatures؛ organizational seals؛ trusted timestamp؛ certificate validation/revocation؛ long-term validation؛ HSM؛ signer intent؛ archive evidence؛ algorithm agility؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** التوقيع صالح قانونياً وتقنياً بعد انتهاء الشهادة؛ هوية وصفة الموقع مثبتتان؛ revocation وclock failure معالجان؛ المفاتيح الخاصة لا تخرج من HSM حيث يلزم.
- **التحقق:** PKI conformance، expired/revoked cert، timestamp outage، archive verification بعد الاستعادة؛ فشل إقلاع على signerIntent/livePki/HSM/formSignature / LivePkiClaim.
- **الاعتماديات:** SOV-003, STR-002, OBS-003.
- **ما أُغلق:**
  - ADR + [`ext-pki/pki-signature-matrix.json`](../ext-pki/pki-signature-matrix.json) + JOURNEY + SIGNER_INTENT_AND_SEALS
  - CERT_VALIDATION_AND_REVOCATION / TRUSTED_TIMESTAMP / HSM_AND_KEY_CUSTODY / LTV_AND_ARCHIVE_EVIDENCE / ALGORITHM_AGILITY / MOCK_SANDBOX
  - scenarios/audit/harness fixtures؛ `ext004-pki-signature-validate.ps1`؛ CODEOWNERS
  - `ExtPki/` في ServiceDefaults + `AddExtPki` على Gateway/Payroll/HR.Core
  - `ext004-pki-signature-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-004-production-pki-binding.md](../ADR-EXT-004-production-pki-binding.md)؛ [EXT_004_ADOPTION_MINUTES.md](../EXT_004_ADOPTION_MINUTES.md)؛ [EXT_004_INDEPENDENT_REVIEW_WAIVER.md](../EXT_004_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_004_UAT_PACK.md](../EXT_004_UAT_PACK.md) ReadyForSign-off؛ [EXT_004_VERIFICATION.md](../EXT_004_VERIFICATION.md)؛ `Ext004ProductionBindingTests`
- **Blockers المتبقية:** STR-002/OBS-003 = Satisfied؛ SOV-003 = Satisfied (production key-sovereignty binding — ليس HSM حي)؛ national-pki-live / hsm-prod / tsa-live / SYD-011 = Blocked
- **لاحقاً (خارج هذا الربط):** EXT-004a–f CA/HSM/TSA حي / SIG-001 / EXT-003e / SYD-011

## EXT-005 — بوابة تبادل البيانات والعقود الحكومية

- **الحالة:** Done (production api-exchange binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / API Governance
- **النطاق:** API/event standards؛ versioning؛ schemas؛ metadata/catalog؛ consent/purpose؛ throttling؛ non-repudiation؛ tenant؛ error model؛ correlation؛ deprecation؛ data minimization؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** كل تكامل منشور في catalog بعقد ومالك وSLA؛ backward compatibility مقاسة؛ لا مشاركة DB؛ contract change لا يكسر مستهلكاً معتمداً؛ export يخضع للغرض.
- **التحقق:** consumer-driven contracts، schema registry compatibility، interoperability tests؛ فشل إقلاع على catalog/sharedDb/consumer/liveBus / LiveExchangeClaim.
- **الاعتماديات:** IAM-004, TEN-003, INT-002, PROD-007.
- **ما أُغلق:**
  - ADR + [`ext-api/api-exchange-matrix.json`](../ext-api/api-exchange-matrix.json) + JOURNEY + CATALOG_AND_METADATA
  - VERSIONING_AND_COMPAT / SCHEMA_AND_EVENTS / PURPOSE_EXPORT_AND_MINIMIZATION / THROTTLE_TENANT_ERRORS / DEPRECATION_AND_NON_REPUDIATION / MOCK_SANDBOX
  - scenarios/audit/harness fixtures؛ `ext005-api-exchange-validate.ps1`؛ CODEOWNERS
  - `ExtApi/` في ServiceDefaults + `AddExtApi` على Gateway/Payroll/HR.Core
  - `ext005-api-exchange-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-005-production-api-exchange-binding.md](../ADR-EXT-005-production-api-exchange-binding.md)؛ [EXT_005_ADOPTION_MINUTES.md](../EXT_005_ADOPTION_MINUTES.md)؛ [EXT_005_INDEPENDENT_REVIEW_WAIVER.md](../EXT_005_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_005_UAT_PACK.md](../EXT_005_UAT_PACK.md) ReadyForSign-off؛ [EXT_005_VERIFICATION.md](../EXT_005_VERIFICATION.md)؛ `Ext005ProductionBindingTests`
- **Blockers المتبقية:** IAM-004/TEN-003/INT-002 = Satisfied؛ PROD-007 = Satisfied (production extensibility-platform binding)؛ gov-exchange-bus-live / schema-registry-live / partner-cdc-cert / national-openapi-publish = Blocked
- **لاحقاً (خارج هذا الربط):** EXT-005a–e bus/registry حي / EXT-006 / IOP-SYR

## EXT-006 — Sandbox وشهادة تكامل وبوابة مطورين

- **الحالة:** Done (production sandbox-cert binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / API Product
- **النطاق:** synthetic sandbox؛ onboarding credentials؛ docs/examples/SDK policy؛ conformance suites؛ rate/error simulation؛ certification؛ revocation؛ support؛ usage analytics؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** شريك جديد ينجح دون وصول إنتاجي أو أسرار يدوية؛ لا بيانات حقيقية في sandbox؛ certification تمنع client غير متوافق؛ docs تطابق العقد المنشور.
- **التحقق:** external-team onboarding exercise، negative conformance، key rotation؛ فشل إقلاع على synthetic/livePortal/incompatible / LiveSandboxPortalClaim.
- **الاعتماديات:** EXT-005, REL-001.
- **ما أُغلق:**
  - ADR pack + [`ext-sandbox/sandbox-cert-matrix.json`](../ext-sandbox/sandbox-cert-matrix.json) + JOURNEY / CREDENTIALS / CONFORMANCE / CERTIFICATION / DOCS_MATCH / MOCK_SANDBOX
  - scenarios/audit/harness fixtures؛ `ext006-sandbox-cert-validate.ps1`؛ CODEOWNERS
  - `ExtSandbox/` في ServiceDefaults + `AddExtSandbox` على Gateway/Payroll/HR.Core
  - `ext006-sandbox-cert-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-006-production-sandbox-cert-binding.md](../ADR-EXT-006-production-sandbox-cert-binding.md)؛ [EXT_006_ADOPTION_MINUTES.md](../EXT_006_ADOPTION_MINUTES.md)؛ [EXT_006_INDEPENDENT_REVIEW_WAIVER.md](../EXT_006_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_006_UAT_PACK.md](../EXT_006_UAT_PACK.md) ReadyForSign-off؛ [EXT_006_VERIFICATION.md](../EXT_006_VERIFICATION.md)؛ `Ext006SandboxCertPackTests` + `Ext006ProductionBindingTests`
- **Blockers المتبقية:** EXT-005/REL-001 = Satisfied؛ live-developer-portal / real-pii-in-sandbox / partner-cert-fleet-live = Blocked؛ EXT-007 = Satisfied
- **لاحقاً (خارج هذا الربط):** EXT-006a–e بوابة حية / أسطول شركاء / key rotation / analytics / MIG-007 flip

## EXT-007 — كتالوج تكاملات الأنظمة القطاعية وإدارتها

- **الحالة:** Done (production sector-catalog binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Enterprise Architecture
- **النطاق:** التقاعد/التأمين/الضرائب/الحضور/ERP/الميزانية/البريد والإشعارات؛ priority؛ source of truth؛ latency؛ ownership؛ fallback؛ adapter lifecycle؛ reconciliation؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** لا تكامل حرج ضمنياً؛ لكل adapter contract/SLO/retry/reconciliation/exit؛ بيانات الإشعار لا تحتوي PII حساساً؛ الإخفاق ظاهر ولا يبتلع.
- **التحقق:** adapter certification، dependency outage، reconciliation، وdelivery receipts حيث تنطبق؛ فشل إقلاع على noImplicitCritical / liveAdapters / notify-pii / LiveSectorCatalogClaim.
- **الاعتماديات:** EXT-005, STR-003.
- **ما أُغلق:**
  - ADR pack + [`ext-sector/sector-catalog-matrix.json`](../ext-sector/sector-catalog-matrix.json) + JOURNEY / ADAPTER_CONTRACT / SLO_RETRY_EXIT / NOTIFICATION_PII / FAILURE_SURFACING / RECON / CERTIFICATION
  - scenarios/audit/harness fixtures؛ `ext007-sector-catalog-validate.ps1`؛ CODEOWNERS
  - `ExtSector/` في ServiceDefaults + `AddExtSector` على Gateway/Payroll/HR.Core
  - `ext007-sector-catalog-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-EXT-007-production-sector-catalog-binding.md](../ADR-EXT-007-production-sector-catalog-binding.md)؛ [EXT_007_ADOPTION_MINUTES.md](../EXT_007_ADOPTION_MINUTES.md)؛ [EXT_007_INDEPENDENT_REVIEW_WAIVER.md](../EXT_007_INDEPENDENT_REVIEW_WAIVER.md)
  - [EXT_007_UAT_PACK.md](../EXT_007_UAT_PACK.md) ReadyForSign-off؛ [EXT_007_VERIFICATION.md](../EXT_007_VERIFICATION.md)؛ `Ext007SectorCatalogPackTests` + `Ext007ProductionBindingTests`
  - مزامنة blocker EXT-007 في مصفوفة EXT-006 → Satisfied
- **Blockers المتبقية:** EXT-005/STR-003 = Satisfied؛ live-sector-adapters / live-notification-channel / adapter-fleet-certified-live = Blocked؛ PROD-005 = Satisfied؛ COM-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** EXT-007a–e adapters حية / إشعار حي / أسطول شهادة / COM-001

## PROD-001 — منصة سياسات وسير عمل ونماذج مؤرخة

- **الحالة:** Done (production policy-platform binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Product Platform + Legal
- **النطاق:** policy/rule/workflow/form templates كبيانات versioned؛ effective dates؛ four-eyes publication؛ simulation؛ rollback؛ tenant inheritance/override؛ no arbitrary code؛ audit/diff؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** تغيير قانون/مسار معتمد دون deploy أو fork؛ الماضي يستخدم نسخته؛ simulation على بيانات اصطناعية؛ invalid policy لا تنشر؛ override لا يتجاوز national mandatory controls.
- **التحقق:** conflicting agency policies، future/retroactive rules، rollback، golden cases؛ فشل إقلاع على invalidPublish / liveEngine / override-bypass / real-data-sim / LivePolicyEngineClaim.
- **الاعتماديات:** PAY-002, GOV-002, STR-002.
- **ما أُغلق:**
  - ADR pack + [`prod-policy/policy-platform-matrix.json`](../prod-policy/policy-platform-matrix.json) + JOURNEY / VERSIONING / EFFECTIVE_DATING / FOUR_EYES_PUBLISH / SIMULATION / ROLLBACK / TENANT_OVERRIDE / NO_ARBITRARY_CODE
  - scenarios/audit/harness fixtures؛ `prod001-policy-platform-validate.ps1`؛ CODEOWNERS
  - `ProdPolicy/` في ServiceDefaults + `AddProdPolicy` على Gateway/Payroll/HR.Core
  - `prod001-policy-platform-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-001-production-policy-platform-binding.md](../ADR-PROD-001-production-policy-platform-binding.md)؛ [PROD_001_ADOPTION_MINUTES.md](../PROD_001_ADOPTION_MINUTES.md)؛ [PROD_001_INDEPENDENT_REVIEW_WAIVER.md](../PROD_001_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_001_UAT_PACK.md](../PROD_001_UAT_PACK.md) ReadyForSign-off؛ [PROD_001_VERIFICATION.md](../PROD_001_VERIFICATION.md)؛ `Prod001PolicyPlatformPackTests` + `Prod001ProductionBindingTests`
  - مزامنة blocker PROD-001 في مصفوفة STR-004 → Satisfied؛ SoftBlockedIds = ONB-001 فقط
- **Blockers المتبقية:** PAY-002/GOV-002/STR-002 = Satisfied؛ live-policy-engine / live-workflow-runtime / live-form-designer = Blocked؛ PROD-002 = Satisfied؛ ONB-001 = Blocked
- **لاحقاً (خارج هذا الربط):** PROD-001a–e محرك حي / workflow / forms / override / WF-001

## PROD-002 — إدارة الجهة ذاتياً ضمن حدود الحوكمة

- **الحالة:** Done (production agency-selfadmin binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Product + IAM
- **النطاق:** delegated administration للهيكل/roles/templates/calendars/notifications؛ approval؛ quotas؛ preview؛ separation between national and agency config؛ audit؛ support escalation؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** الجهة تنجز التغييرات اليومية دون DBA/مطور؛ لا تستطيع تغيير ضوابط وطنية أو tenant آخر؛ كل تغيير قابل للتراجع والمساءلة؛ four-eyes للتغييرات الحساسة.
- **التحقق:** agency-admin persona suite، hostile config، concurrent change، rollback؛ فشل إقلاع على national-mutate / cross-tenant / liveSelfAdmin / same-role-sensitive / LiveAgencySelfAdminClaim.
- **الاعتماديات:** IAM-003, TEN-003, PROD-001.
- **ما أُغلق:**
  - ADR pack + [`prod-agency/agency-selfadmin-matrix.json`](../prod-agency/agency-selfadmin-matrix.json) + JOURNEY / DELEGATED_ADMIN / NATIONAL_CEILING / CROSS_TENANT / FOUR_EYES / ROLLBACK_AUDIT / HOSTILE_CONFIG
  - scenarios/audit/harness fixtures؛ `prod002-agency-selfadmin-validate.ps1`؛ CODEOWNERS
  - `ProdAgency/` في ServiceDefaults + `AddProdAgency` على Gateway/Payroll/HR.Core
  - `prod002-agency-selfadmin-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-002-production-agency-selfadmin-binding.md](../ADR-PROD-002-production-agency-selfadmin-binding.md)؛ [PROD_002_ADOPTION_MINUTES.md](../PROD_002_ADOPTION_MINUTES.md)؛ [PROD_002_INDEPENDENT_REVIEW_WAIVER.md](../PROD_002_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_002_UAT_PACK.md](../PROD_002_UAT_PACK.md) ReadyForSign-off؛ [PROD_002_VERIFICATION.md](../PROD_002_VERIFICATION.md)؛ `Prod002AgencySelfAdminPackTests` + `Prod002ProductionBindingTests`
  - مزامنة blocker PROD-002 في مصفوفتي PROD-001 و MIG-007 → Satisfied
- **Blockers المتبقية:** IAM-003/TEN-003/PROD-001 = Satisfied؛ live-agency-selfadmin / live-delegated-admin-ui / cross-tenant-admin = Blocked؛ ONB-001 = Blocked؛ PROD-003 = Satisfied
- **لاحقاً (خارج هذا الربط):** PROD-002a–e UI حي / تفويض حي / ONB-001

## PROD-003 — الخدمة الذاتية للموظف والمدير

- **الحالة:** Done (production employee-selfservice binding)
- **الأولوية/الحجم/المسؤول:** P2-National / XL / Product UX
- **النطاق:** profile/correction؛ leave/requests؛ payslips/tax documents؛ documents؛ manager approvals/team؛ delegation؛ inbox؛ status/SLA؛ privacy؛ device/session security؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** الرحلات الأعلى حجماً تنجز end-to-end دون مكتب خلفي إلا الاستثناء؛ الموظف يرى مصدر وحالة التصحيح؛ المدير لا يرى خارج نطاقه؛ الاستخدام مقاس.
- **التحقق:** persona/UAT لعدة جهات وفروع، mobile/desktop، lost session/device؛ فشل إقلاع على manager-outside-scope / correction-hidden / usage-unmeasured / livePortal / LiveEmployeeSelfServiceClaim.
- **الاعتماديات:** IAM-002 (NotStarted صادق), IAM-003, FUN-001..006, UX-002.
- **ما أُغلق:**
  - ADR pack + [`prod-ess/employee-selfservice-matrix.json`](../prod-ess/employee-selfservice-matrix.json) + JOURNEY / CORRECTION_VISIBILITY / MANAGER_SCOPE / USAGE_TELEMETRY / PRIVACY_SESSION / PERSONA_UAT
  - scenarios/audit/harness fixtures؛ `prod003-employee-selfservice-validate.ps1`؛ CODEOWNERS
  - `ProdEss/` في ServiceDefaults + `AddProdEss` على Gateway/Payroll/HR.Core
  - `prod003-employee-selfservice-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-003-production-employee-selfservice-binding.md](../ADR-PROD-003-production-employee-selfservice-binding.md)؛ [PROD_003_ADOPTION_MINUTES.md](../PROD_003_ADOPTION_MINUTES.md)؛ [PROD_003_INDEPENDENT_REVIEW_WAIVER.md](../PROD_003_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_003_UAT_PACK.md](../PROD_003_UAT_PACK.md) ReadyForSign-off؛ [PROD_003_VERIFICATION.md](../PROD_003_VERIFICATION.md)؛ `Prod003EmployeeSelfServicePackTests` + `Prod003ProductionBindingTests`
  - مزامنة blocker PROD-003 في مصفوفتي PROD-002 و STR-007 → Satisfied
- **Blockers المتبقية:** IAM-003/FUN-001..006/UX-002 = Satisfied؛ IAM-002 = NotStarted؛ live-employee-portal / live-manager-portal / live-usage-telemetry = Blocked؛ ESS-001/MSS-001 = NotStarted؛ PROD-004 = Satisfied
- **لاحقاً (خارج هذا الربط):** PROD-003a–e بوابات حية / قياس استخدام حي / ESS-001 / MSS-001

## PROD-004 — تجربة الفروع البعيدة والاتصال الضعيف واللغات

- **الحالة:** Done (production remote-branch binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / UX + Architecture
- **النطاق:** performance budgets؛ responsive/PWA؛ resumable uploads؛ save draft؛ retry/idempotency؛ Arabic/English/local languages؛ timezone/calendar؛ printable fallback؛ offline محدود مع threat model؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** الرحلات الأساسية تعمل على profile شبكة معتمد منخفض السرعة؛ لا فقد عند انقطاع؛ النصوص قابلة للترجمة؛ offline data مشفرة ومحدودة؛ WCAG 2.2 AA.
- **التحقق:** throttled-network labs، interrupted upload/submit، RTL/LTR، accessibility audit؛ فشل إقلاع على below-budget / data-loss / hardcode-locale / plaintext-offline / liveRemoteBranch / LiveRemoteBranchClaim.
- **الاعتماديات:** UX-002, DATA-001, FILE-001.
- **ما أُغلق:**
  - ADR pack + [`prod-branch/remote-branch-matrix.json`](../prod-branch/remote-branch-matrix.json) + JOURNEY / PERFORMANCE_BUDGETS / NETWORK_PROFILE / RESUMABLE_UPLOAD / DRAFT_RETRY / LOCALIZATION / OFFLINE_THREAT / ACCESSIBILITY
  - scenarios/audit/harness fixtures؛ `prod004-remote-branch-validate.ps1`؛ CODEOWNERS
  - `ProdBranch/` في ServiceDefaults + `AddProdBranch` على Gateway/Payroll/HR.Core
  - `prod004-remote-branch-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-004-production-remote-branch-binding.md](../ADR-PROD-004-production-remote-branch-binding.md)؛ [PROD_004_ADOPTION_MINUTES.md](../PROD_004_ADOPTION_MINUTES.md)؛ [PROD_004_INDEPENDENT_REVIEW_WAIVER.md](../PROD_004_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_004_UAT_PACK.md](../PROD_004_UAT_PACK.md) ReadyForSign-off؛ [PROD_004_VERIFICATION.md](../PROD_004_VERIFICATION.md)؛ `Prod004RemoteBranchPackTests` + `Prod004ProductionBindingTests`
  - مزامنة blocker PROD-004 في مصفوفة PROD-003 → Satisfied
- **Blockers المتبقية:** UX-002/DATA-001/FILE-001 = Satisfied؛ live-remote-branch-ux / live-pwa-fleet / live-offline-field = Blocked؛ L10N-001/MOB-001/SYR-013 = NotStarted؛ PROD-005 = Satisfied
- **لاحقاً (خارج هذا الربط):** PROD-004a–e أسطول PWA حي / offline ميداني / L10N-001 / MOB-001 / SYR-013

## PROD-005 — صندوق عمل موحد وبحث وإشعارات قابلة للتتبع

- **الحالة:** Done (production workbox binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Product Platform
- **النطاق:** tasks/cases/approvals؛ priority/SLA/escalation؛ safe search tenant/org-scoped؛ notification preferences/channels/templates؛ delivery receipts؛ deep links مع إعادة تفويض؛ bulk action controls؛ ربط تشغيلي عند الإقلاع/CI.
- **معايير القبول:** لا معاملة تضيع بين الوحدات؛ البحث لا يكشف وجود كائن غير مخول؛ bulk approve يخضع limits/four-eyes؛ الإشعار لا يحتوي سراً ويقود لحالة صحيحة.
- **التحقق:** queue backlog/escalation، cross-tenant search، expired deep link، notification failure؛ فشل إقلاع على lost-transaction / cross-tenant-search / bulk-without-limit / secret-in-notification / liveWorkbox / LiveWorkboxClaim.
- **الاعتماديات:** INT-003, IAM-002 (NotStarted صادق), EXT-007.
- **ما أُغلق:**
  - ADR pack + [`prod-workbox/workbox-matrix.json`](../prod-workbox/workbox-matrix.json) + JOURNEY / QUEUE_COMPLETENESS / SAFE_SEARCH / BULK_FOUR_EYES / NOTIFICATIONS / DEEP_LINKS
  - scenarios/audit/harness fixtures؛ `prod005-workbox-validate.ps1`؛ CODEOWNERS
  - `ProdWorkbox/` في ServiceDefaults + `AddProdWorkbox` على Gateway/Payroll/HR.Core
  - `prod005-workbox-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-005-production-workbox-binding.md](../ADR-PROD-005-production-workbox-binding.md)؛ [PROD_005_ADOPTION_MINUTES.md](../PROD_005_ADOPTION_MINUTES.md)؛ [PROD_005_INDEPENDENT_REVIEW_WAIVER.md](../PROD_005_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_005_UAT_PACK.md](../PROD_005_UAT_PACK.md) ReadyForSign-off؛ [PROD_005_VERIFICATION.md](../PROD_005_VERIFICATION.md)؛ `Prod005WorkboxPackTests` + `Prod005ProductionBindingTests`
  - مزامنة blocker PROD-005 في مصفوفتي PROD-004 و EXT-007 → Satisfied
- **Blockers المتبقية:** INT-003/EXT-007 = Satisfied؛ IAM-002 = NotStarted؛ live-workbox-ui / live-notification-delivery / live-safe-search = Blocked؛ COM-001/WF-002/NAT-005 = NotStarted؛ PROD-006 = Satisfied (production semantic-layer binding)
- **لاحقاً (خارج هذا الربط):** PROD-005a–e UI حي / إشعارات حية / بحث حي / COM-001 / WF-002 / NAT-005

## PROD-006 — طبقة دلالية وطنية وتقارير نظامية

- **الحالة:** Done (production semantic-layer binding)
- **الأولوية/الحجم/المسؤول:** P2-National / XL / Data & Analytics
- **النطاق:** warehouse/lakehouse قرار؛ semantic metrics؛ slowly changing dimensions؛ tenant/region rollups؛ statutory reports؛ freshness؛ lineage؛ privacy thresholds؛ open-data aggregates؛ self-service governed BI؛ ربط تشغيلي عند الإقلاع/CI مع `LiveSemanticLayerClaim=false`.
- **معايير القبول:** تعريف موحد لكل KPI؛ الأرقام تتصالح مع الأنظمة المصدرية؛ مدير الجهة لا يرى غيرها؛ التجميع العام يمنع إعادة التعرف؛ التقرير يحمل version/as-of/source.
- **التحقق:** golden reports، reconciliation، row-level security، small-cell/privacy attacks؛ فشل إقلاع على undefined-kpi / reconciliation / cross-tenant / small-cell / missing-provenance / LiveSemanticLayerClaim.
- **الاعتماديات:** GOV-003, FUN-006, PERF-004, TEN-003.
- **ما أُغلق:**
  - ADR pack + [`prod-semantic/semantic-layer-matrix.json`](../prod-semantic/semantic-layer-matrix.json) + JOURNEY / KPI_DEFINITIONS / RECONCILIATION / TENANT_RLS / PRIVACY_THRESHOLDS / REPORT_PROVENANCE
  - scenarios/audit/harness fixtures؛ `prod006-semantic-layer-validate.ps1`؛ CODEOWNERS
  - `ProdSemantic/` في ServiceDefaults + `AddProdSemantic` على Gateway/Payroll/HR.Core
  - `prod006-semantic-layer-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-006-production-semantic-layer-binding.md](../ADR-PROD-006-production-semantic-layer-binding.md)؛ [PROD_006_ADOPTION_MINUTES.md](../PROD_006_ADOPTION_MINUTES.md)؛ [PROD_006_INDEPENDENT_REVIEW_WAIVER.md](../PROD_006_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_006_UAT_PACK.md](../PROD_006_UAT_PACK.md) ReadyForSign-off؛ [PROD_006_VERIFICATION.md](../PROD_006_VERIFICATION.md)؛ `Prod006SemanticLayerPackTests` + `Prod006ProductionBindingTests`
  - مزامنة blocker PROD-006 في مصفوفة PROD-005 → Satisfied؛ إعادة استهداف STR-007 `LiveDashboardClaim` إلى PROD-006c / live-bi-dashboard؛ مزامنة blocker PROD-007 → Satisfied عند Done منصة التوسعة
- **Blockers المتبقية:** GOV-003/FUN-006/PERF-004/TEN-003 = Satisfied؛ live-warehouse / live-bi-dashboard / live-open-data-feed = Blocked؛ ANA-001/REP-001 = NotStarted؛ PROD-007 = Satisfied (production extensibility-platform binding)
- **لاحقاً (خارج هذا الربط):** PROD-006a–e warehouse حي / محرك metrics حي / لوحة BI حية / ANA-001 / REP-001 / open-data feed حي

## PROD-007 — منصة توسعة وAPI مستقرة دون كسر المنتج

- **الحالة:** Done (production extensibility-platform binding)
- **الأولوية/الحجم/المسؤول:** P2-National / L / Product Architecture
- **النطاق:** supported APIs/events/webhooks؛ semantic versioning؛ compatibility windows؛ plugin/adapter isolation؛ quotas؛ certification؛ deprecation؛ SDK policy؛ no direct DB/custom fork؛ ربط تشغيلي عند الإقلاع/CI مع `LiveExtensibilityClaim=false`.
- **معايير القبول:** تكامل جهة يعيش ترقية مدعومة؛ deprecated contract يقاس ويعلن؛ extension لا يصل tenant آخر أو يعطل core؛ upgrade path موثق.
- **التحقق:** N-2 compatibility، malicious/slow extension، consumer inventory، upgrade rehearsal؛ فشل إقلاع على n2-break / cross-tenant / direct-db / undeprecated-breaking / LiveExtensibilityClaim.
- **الاعتماديات:** INT-002, SUP-001, STR-004.
- **ما أُغلق:**
  - ADR pack + [`prod-extensibility/extensibility-matrix.json`](../prod-extensibility/extensibility-matrix.json) + JOURNEY / SUPPORTED_CONTRACTS / VERSIONING_COMPAT / PLUGIN_ISOLATION / QUOTAS / DEPRECATION / SDK_POLICY
  - scenarios/audit/harness fixtures؛ `prod007-extensibility-validate.ps1`؛ CODEOWNERS
  - `ProdExtensibility/` في ServiceDefaults + `AddProdExtensibility` على Gateway/Payroll/HR.Core
  - `prod007-extensibility-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-PROD-007-production-extensibility-platform-binding.md](../ADR-PROD-007-production-extensibility-platform-binding.md)؛ [PROD_007_ADOPTION_MINUTES.md](../PROD_007_ADOPTION_MINUTES.md)؛ [PROD_007_INDEPENDENT_REVIEW_WAIVER.md](../PROD_007_INDEPENDENT_REVIEW_WAIVER.md)
  - [PROD_007_UAT_PACK.md](../PROD_007_UAT_PACK.md) ReadyForSign-off؛ [PROD_007_VERIFICATION.md](../PROD_007_VERIFICATION.md)؛ `Prod007ExtensibilityPackTests` + `Prod007ProductionBindingTests`
  - مزامنة blocker PROD-007 في مصفوفتي EXT-005 و PROD-006 → Satisfied
- **Blockers المتبقية:** INT-002/SUP-001/STR-004 = Satisfied؛ live-extension-runtime / live-partner-sdk / live-webhook-delivery = Blocked؛ EXT-006/IOP-001/REL-004 = NotStarted
- **لاحقاً (خارج هذا الربط):** PROD-007a–e runtime حي / SDK حي / webhooks حية / EXT-006 / REL-004
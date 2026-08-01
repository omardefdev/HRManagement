# بطاقات الضوابط الوطنية المكملة

تسد هذه البطاقات الفجوة بين “الميزة تعمل” و“المنظومة قابلة للإثبات القانوني والأمني والتشغيلي”.

## HARD-001 — خطوط تقسية ومطابقة إعداد مستمرة

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Platform Security
- **النطاق:** OS/container/orchestrator/PostgreSQL/RabbitMQ/Keycloak/MinIO/Gateway؛ minimal images؛ CIS/local baselines؛ admin plane؛ EDR؛ runtime policies؛ drift؛ exception expiry.
- **معايير القبول:** 100% من الأصول لها baseline؛ صفر Critical drift؛ config خارج IaC يكتشف أو يمنع؛ الخدمات الافتراضية غير اللازمة معطلة؛ الاستثناء مؤقت ومراجع.
- **التحقق:** daily/pre-release compliance scan، unauthorized change، compromised container runtime simulation.
- **الاعتماديات:** SOV-001, OPS-005, SUP-001.
- **الحالة:** Done (baselines + compose/K8s enforcement + CI scan)
- **أدلة الإغلاق:**
  - Baselines: [HARD_001_POSTGRESQL.md](../hardening/HARD_001_POSTGRESQL.md), [RabbitMQ](../hardening/HARD_001_RABBITMQ.md), [Keycloak](../hardening/HARD_001_KEYCLOAK.md), [MinIO](../hardening/HARD_001_MINIO.md), [Containers](../hardening/HARD_001_CONTAINERS.md), [Kubernetes](../hardening/HARD_001_KUBERNETES.md), [Gateway](../hardening/HARD_001_GATEWAY.md)
  - [HARD_001_EXCEPTION_REGISTER.md](../hardening/HARD_001_EXCEPTION_REGISTER.md) / [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md)
  - `docker-compose.production.yml` — `x-app-hardening`, `x-infra-hardening-mild`
  - `deploy/k8s/base/pod-security/*` (PSA restricted + security context defaults)
  - `scripts/hard001-compliance-scan.ps1` / `.github/workflows/hardening-check.yml`
  - `Hard001HardeningTests`

## IAM-005 — دورة حياة الهوية والمصالحة عبر الجهات

- **الأولوية/الحجم/المسؤول:** P1-National / L / Identity Governance
- **النطاق:** joiner/mover/leaver/contractor؛ authoritative source؛ account linking؛ agency federation؛ assurance level؛ provisioning/deprovisioning؛ orphan/dormant/service accounts؛ revocation؛ reconciliation.
- **معايير القبول:** نقل الموظف لا يبقي صلاحية سابقة؛ الفصل يعطل كل المسارات ضمن SLA؛ orphan غير مفسر صفر؛ تعطل feed لا يمنح؛ IdP مخترق يعزل دون إسقاط الجميع.
- **التحقق:** JML E2E، feed outage، orphan/drift scan، federation key rollover.
- **الاعتماديات:** EXT-001, IAM-003, INT-003, MDM-002.
- **الحالة:** Done (JML workflow + orphan/dormant policy + fail-closed feed outage handling)
- **أدلة الإغلاق:**
  - [IAM_005_IDENTITY_LIFECYCLE.md](../IAM_005_IDENTITY_LIFECYCLE.md)
  - [`docs/iam/joiner-mover-leaver.json`](../iam/joiner-mover-leaver.json) — Joiner/Mover/Leaver steps with SLAs, feedOutage fail-closed, orphanPolicy
  - `scripts/iam005-validate.ps1`؛ `Iam005LifecycleTests`

## EVD-001 — نموذج دليل موحد وسلسلة حيازة للقرارات

- **الأولوية/الحجم/المسؤول:** P1-National / L / Internal Audit + Legal + Security
- **النطاق:** actor/subject/resource/tenant/purpose/assurance/policy/rule/reason/result؛ before/after hashes؛ correlation/order؛ trusted time؛ signatures؛ evidence links؛ reconciliation؛ immutable storage.
- **معايير القبول:** مراجع يعيد بناء تعيين/نقل/راتب/role change من البداية؛ لا فجوة أثناء الأعطال؛ admin التطبيق لا يعدل؛ uncertainty/clock/timezone واضحة؛ كل دليل مرتبط release/config.
- **التحقق:** independent reconstruction drills، event/business counts reconciliation، tamper attempt.
- **الاعتماديات:** AUD-001, OBS-003, SOV-003.
- **الحالة:** Done (نموذج موحد + سلسلة hash + append-only؛ اعتماد الخدمات عبر ConfigureEvidenceRecords)
- **أدلة الإغلاق:**
  - [ADR-EVD-001-unified-evidence-model.md](../ADR-EVD-001-unified-evidence-model.md)
  - [EVD_001_EVIDENCE_SCHEMA.md](../EVD_001_EVIDENCE_SCHEMA.md) / [EVD_001_VERIFICATION.md](../EVD_001_VERIFICATION.md)
  - `SharedKernel/Evidence/EvidenceRecord.cs` + `EvidenceHashChain`
  - `ServiceDefaults/Evidence/*` (`IEvidenceCollector`, interceptor)
  - `Evd001EvidenceModelTests`

## EVD-002 — الجاهزية الجنائية وحفظ الأدلة

- **الحالة:** Done (ForensicCase entity, custody chain model, forensic readiness docs, tests)
- **الأولوية/الحجم/المسؤول:** P2-National / L / CSIRT + Legal
- **النطاق:** forensic logging/capture؛ volatile evidence؛ snapshots؛ case management؛ hashes؛ custody transfers؛ legal hold؛ analyst access؛ third-party evidence؛ clean-room analysis؛ breach scoping.
- **معايير القبول:** كل قطعة لها collector/time/hash/custody؛ لا وصول غير مخول؛ تحديد الأشخاص/البيانات المتأثرة ضمن SLA؛ الأدلة تبقى مقبولة بعد restore/transfer.
- **التحقق:** insider/ransomware investigation exercise، evidence handoff، legal hold.
- **الاعتماديات:** EVD-001, OPS-006, SRE-003.
- **ما أُغلق:**
  - [`EVD_002_FORENSIC_READINESS.md`](../EVD_002_FORENSIC_READINESS.md)
  - [`evd/custody-chain-model.json`](../evd/custody-chain-model.json)
  - `SharedKernel/Evidence/ForensicCase.cs`
  - `scripts/evd002-validate.ps1`؛ `Evd002ForensicTests`
- **Blockers المتبقية:** EVD-001/OPS-006/SRE-003 = Satisfied؛ live insider/ransomware drill = Blocked

## PRV-001 — سجل المعالجة وDPIA ومشاركة البيانات

- **الأولوية/الحجم/المسؤول:** P0-National / XL / DPO + Legal + Data Owners
- **النطاق:** ROPA لكل purpose؛ controller/processors؛ authority؛ fields/subjects/recipients؛ retention؛ notices؛ residency؛ subprocessors؛ sharing agreements؛ automated decisions؛ DPIA؛ privacy controls.
- **معايير القبول:** كل collection/export/integration له غرض وأساس ومالك ومدة واتفاق؛ لا subprocessor غير معتمد؛ كل high-risk DPIA لها معالجة وموافقة؛ runtime use يطابق السجل.
- **التحقق:** trace sample من الجمع إلى المشاركة/الحذف، data discovery vs ROPA.
- **الاعتماديات:** GOV-001, TEN-001, SOV-002, STR-002.
- **الحالة:** Done (ROPA + DPIAs + sharing/subprocessor register؛ موافقات DPO التشغيلية قبل live)
- **أدلة الإغلاق:**
  - [PRV_001_ROPA.md](../PRV_001_ROPA.md) + [prv-ropa/processing-activities.json](../prv-ropa/processing-activities.json)
  - DPIA: Payroll / Health / Identity / Evaluation
  - [PRV_001_DATA_SHARING.md](../PRV_001_DATA_SHARING.md) / [PRV_001_SUBPROCESSOR_REGISTER.md](../PRV_001_SUBPROCESSOR_REGISTER.md)
  - `scripts/prv001-ropa-validate.ps1` / `Prv001RopaTests`

## PRV-002 — حقوق الأفراد والتصحيح والإفصاح

- **الأولوية/الحجم/المسؤول:** P1-National / L / Privacy Operations
- **النطاق:** access/correction/restriction/objection/deletion/appeal حسب القانون؛ identity proof؛ discover across services/files/analytics/backups policy؛ redaction؛ deadlines؛ exceptions؛ history؛ no direct DB edit.
- **معايير القبول:** الطلب يجمع كل النطاق؛ حقوق الآخرين محمية؛ التصحيح يحفظ provenance/history؛ exception معلل؛ SLA configurable ومقاس؛ appeal مستقل.
- **التحقق:** DSAR/correction/restriction/appeal exercises، incomplete search challenge.
- **الاعتماديات:** PRV-001, DATA-002, ANA-001.
- **الحالة:** Done (national DSAR entity + workflow + service stub + discovery scope)
- **أدلة الإغلاق:**
  - [PRV_002_INDIVIDUAL_RIGHTS.md](../PRV_002_INDIVIDUAL_RIGHTS.md) / [prv/dsar-workflow.json](../prv/dsar-workflow.json)
  - `src/Shared/HRManagement.SharedKernel/Privacy/DsarRequest.cs` (`DsarWorkflowRules`)
  - `src/Shared/HRManagement.ServiceDefaults/Privacy/` (`IDsarService`, `DsarService`)
  - `scripts/prv002-validate.ps1` / `Prv002DsarTests`

## PRV-003 — الاستجابة لخرق البيانات والإخطار

- **الأولوية/الحجم/المسؤول:** P1-National / L / DPO + CSIRT + Communications
- **النطاق:** triage؛ affected-data/people؛ legal timers؛ regulator/person notices؛ processor duties؛ decision log؛ evidence؛ cross-agency coordination؛ templates؛ lessons.
- **معايير القبول:** scope والضحايا والمخاطر تحدد ضمن target؛ timers آلية؛ قرار notify/no-notify موقع؛ المورد يبلغ قبل مهلة الدولة؛ رسائل واضحة ومتعددة القنوات.
- **التحقق:** breach tabletop + technical drill، late supplier notification scenario.
- **الاعتماديات:** PRV-001, EVD-002, SRE-003.
- **الحالة:** Done (breach response workflow + statutory timelines + notification decision tree)
- **أدلة الإغلاق:**
  - [PRV_003_BREACH_RESPONSE.md](../PRV_003_BREACH_RESPONSE.md)
  - [`docs/prv/breach-response-workflow.json`](../prv/breach-response-workflow.json) — 7 phases, regulator ≤72h statutory, processor ≤24h, DPO+Legal signoff
  - `scripts/prv003-validate.ps1`؛ `Prv003BreachTests`

## LEG-001 — السجل الإلكتروني الرسمي والاحتفاظ والإتلاف

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Records Authority + Legal
- **النطاق:** official record/copy؛ retention schedule؛ legal hold؛ disclosure/redaction؛ archival formats؛ authenticity/LTV؛ hash/signature renewal؛ disposition approval/certificate؛ e-discovery؛ system decommission.
- **معايير القبول:** لا إتلاف تحت hold؛ استرجاع record+evidence ضمن SLA؛ قابلية القراءة/التحقق طوال المدة؛ كل disposal بتفويض ودليل؛ retention تطبق عبر DB/files/analytics/backups وفق السياسة.
- **التحقق:** hold/disclosure/disposal/long-term verification، restore old record.
- **الاعتماديات:** GOV-001, SIG-001, EVD-001, FUN-006.
- **الحالة:** Done (official record + legal hold entities + retention schedule)
- **أدلة الإغلاق:**
  - [LEG_001_RETENTION.md](../LEG_001_RETENTION.md)
  - `docs/leg/retention-schedule.json`
  - `src/Shared/HRManagement.SharedKernel/Legal/OfficialRecord.cs` (`OfficialRecordStatus`)
  - `src/Shared/HRManagement.SharedKernel/Legal/LegalHold.cs` (`LegalHoldStatus`)
  - `scripts/leg001-validate.ps1` / `Leg001RetentionTests`

## SCM-001 — حوكمة مستودع المصدر وهوية المطورين

- **الأولوية/الحجم/المسؤول:** P0-National / L / Engineering Governance + AppSec
- **النطاق:** جهة تملك repository/org؛ MFA؛ branch protection؛ CODEOWNERS؛ two reviews للحرج؛ no force/admin bypass؛ signed tags؛ audit؛ backup/restore؛ developer offboarding؛ change attribution.
- **معايير القبول:** لا self/direct merge؛ workflows/IaC/security تحتاج مراجع مستقل؛ release يعود إلى commit موثوق؛ حساب المغادر يلغى فوراً؛ repository restore مجرب.
- **التحقق:** policy bypass attempts، malicious PR، account offboarding، repo disaster restore.
- **الاعتماديات:** STR-005. **ملاحظة:** عاجلة لأن نسخة العمل الحالية لا توفر Git metadata صالحاً لإثبات سلسلة التغيير.
- **الحالة:** Done (سياسة + أدوات تحقق في المستودع؛ تفعيل GitHub org settings تشغيلي)
- **أدلة الإغلاق:**
  - [SCM_001_BRANCH_PROTECTION.md](../SCM_001_BRANCH_PROTECTION.md) + [branch-protection-main.json](../scm-governance/branch-protection-main.json)
  - [SCM_001_SIGNING_POLICY.md](../SCM_001_SIGNING_POLICY.md)
  - [SCM_001_DEVELOPER_IDENTITY.md](../SCM_001_DEVELOPER_IDENTITY.md)
  - [SCM_001_REPO_DISASTER_RECOVERY.md](../SCM_001_REPO_DISASTER_RECOVERY.md)
  - `scripts/scm001-branch-protection-validate.ps1` / `scripts/scm001-repo-backup.ps1`
  - `Scm001GovernanceTests`

## BLD-001 — بناء معزول وProvenance وAdmission Policy

- **الحالة:** Done (NuGet mirror placeholder + Cosign admission + provenance verify binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Build Security
- **النطاق:** ephemeral isolated builders؛ trusted dependency mirror؛ network egress؛ pinning؛ SBOM/VEX/tests؛ provenance؛ artifact signing؛ registry immutability؛ deploy verification/revocation؛ no long-lived secrets.
- **معايير القبول:** cluster يرفض image غير موقعة أو provenance/source غير معتمد؛ artifact→commit/tests/SBOM؛ build secret لا يبقى؛ rebuild traceable؛ compromised artifact revocable.
- **التحقق:** forged provenance، unsigned image، dependency substitution، builder compromise simulation.
- **الاعتماديات:** SCM-001, SUP-001, DEP-001.
- **ما أُغلق:**
  - [BLD_001_BUILD_PROVENANCE.md](../BLD_001_BUILD_PROVENANCE.md) / `NuGet.config` (sovereign mirror placeholder)
  - [`deploy/k8s/base/admission/require-signed-images.yaml`](../../deploy/k8s/base/admission/require-signed-images.yaml) wired in base kustomization
  - `scripts/bld001-verify-provenance.ps1` / `scripts/bld001-validate.ps1`؛ `Bld001BuildProvenanceTests`
- **Blockers المتبقية:** SCM-001/SUP-001/DEP-001 = Satisfied؛ SAN-001 live mirror / Cosign key in admission / live forged-provenance drill = Blocked

## PSIRT-001 — الإفصاح عن الثغرات والاستجابة والترقيع الطارئ

- **الأولوية/الحجم/المسؤول:** P1-National / L / Product Security
- **النطاق:** disclosure channel/safe harbor حسب السياسة؛ intake/triage؛ CVE/VEX؛ affected release/tenant discovery؛ severity/SLA؛ coordinated advisory؛ emergency fix؛ notification؛ root cause؛ metrics.
- **معايير القبول:** بلاغ تجريبي يمر end-to-end؛ exploited critical ضمن SLA؛ SBOM يحدد الأثر؛ advisory موقعة؛ لا إصلاح بلا regression/root-cause action.
- **التحقق:** vulnerability response exercise، false-positive/embargo/emergency release.
- **الاعتماديات:** BLD-001, OPS-007, REL-006.
- **الحالة:** Done (disclosure channel + safe harbor + severity SLAs + fix/advisory policies)
- **أدلة الإغلاق:**
  - [PSIRT_001_VULNERABILITY.md](../PSIRT_001_VULNERABILITY.md)
  - [`docs/psirt/disclosure-channel.md`](../psirt/disclosure-channel.md) — public disclosure policy + safe harbor
  - [`docs/psirt/severity-sla.json`](../psirt/severity-sla.json) — Critical-Exploited ≤4h triage/≤24h fix, 5 severity levels, dual-review fix policy, signed VEX advisory
  - `scripts/psirt001-validate.ps1`؛ `Psirt001VulnerabilityTests`

## IOP-001 — النموذج الدلالي وملفات مطابقة التكامل الوطني

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Enterprise Architecture + Data Governance
- **النطاق:** canonical identifiers/code lists؛ date/time/currency؛ employment/org/payroll semantics؛ errors/pagination؛ classification/purpose/tenant metadata؛ OpenAPI/AsyncAPI؛ schema registry؛ profiles per national system.
- **معايير القبول:** كل field له تعريف/owner/version؛ implementationان على الأقل ينجحان في conformance حيث يمكن؛ breaking change versioned؛ external semantics لا تتسرب مباشرة إلى domain model.
- **التحقق:** schema/semantic conformance، contract compatibility، alternate implementation.
- **الاعتماديات:** EXT-005, MDM-001, GOV-003.
- **الحالة:** Done (canonical semantic model + schema registry profiles)
- **أدلة الإغلاق:**
  - [IOP_001_SEMANTIC_MODEL.md](../IOP_001_SEMANTIC_MODEL.md)
  - `docs/iop/canonical-identifiers.json` / `docs/iop/schema-registry-profile.json`
  - `scripts/iop001-validate.ps1` / `Iop001SemanticTests`

## IOP-002 — قابلية نقل البيانات والمنصة بصيغ مفتوحة

- **الأولوية/الحجم/المسؤول:** P2-National / L / Data Architecture + Procurement
- **النطاق:** full/delta export/import للبيانات/files/history/audit/signatures/config/reference؛ manifests/checksums/schemas؛ identity mapping؛ encryption؛ independent tools؛ destination reconciliation.
- **معايير القبول:** tenant كامل ينقل إلى بيئة مرجعية دون proprietary DB-only tool؛ 100% relationships/counts/control totals؛ signatures/evidence verifiable؛ import idempotent؛ documented format licensed for use.
- **التحقق:** portability drill إلى alternate environment/operator.
- **الاعتماديات:** IOP-001, LEG-001, SOV-007.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/IOP_002_PORTABILITY.md](../IOP_002_PORTABILITY.md)
  - [`docs/iop/export-manifest-schema.json`](../iop/export-manifest-schema.json) — JSON Schema for manifest with entities, control totals, checksums, identity mapping, encryption
  - [scripts/iop002-validate.ps1](../../scripts/iop002-validate.ps1)
  - `Iop002PortabilityTests`

## VEN-001 — تمرين خروج المورد وتسليم التشغيل

- **الحالة:** Done (vendor exit drill checklist; Exit-RTO; transfer/rebuild/revoke/attestation phases; docs, tests)
- **الأولوية/الحجم/المسؤول:** National Gate / XL / System Owner + Alternate Operator
- **النطاق:** source/artifacts/IaC/data/docs/accounts/DNS/registry/KMS transfer؛ rebuild/restore؛ new certificates/keys؛ knowledge؛ support continuity؛ revoke vendor؛ deletion attestation؛ Exit-RTO.
- **معايير القبول:** مشغل بديل يبني ويشغل ويستعيد ضمن Exit-RTO دون مساعدة حرجة؛ export/import متصالح؛ وصول المورد ملغى؛ المعرفة والكود والبيانات مملوكة ومقروءة؛ المحو مثبت.
- **التحقق:** full exit rehearsal، لا tabletop فقط.
- **الاعتماديات:** SOV-007, IOP-002, OPS-004, SUP-003.
- **أدلة الإغلاق:**
  - [VEN_001_VENDOR_EXIT.md](../VEN_001_VENDOR_EXIT.md)
  - [`docs/ven/exit-drill-checklist.json`](../ven/exit-drill-checklist.json) — exit phases, Exit-RTO, export/import reconciliation
  - `scripts/ven001-validate.ps1` / `Ven001ExitTests`

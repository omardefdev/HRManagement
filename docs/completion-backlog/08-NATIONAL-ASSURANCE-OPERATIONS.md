# بطاقات الضمان والسيادة والتشغيل الوطني

هذه البطاقات إلزامية بعد الـPilot وقبل وصف المنصة بأنها إنتاج وطني. هي توسع بطاقات P0–P3 ولا تستبدلها.

## SOV-001 — خط أساس الضوابط والاعتماد الأمني الوطني

- **الحالة:** Done (production control-baseline binding)
- **الأولوية/الحجم/المسؤول:** P0-National / XL / CISO + Compliance
- **النطاق:** tailoring للقانون المحلي مع crosswalk إلى NIST CSF/800-53B وISO 27001 وASVS 5 وSSDF؛ impact categorization؛ control owner/evidence/test/frequency؛ POA&M؛ exceptions؛ authorization boundary؛ ربط تشغيلي عند الإقلاع/CI مع `LiveAtoClaim=false`.
- **معايير القبول:** 100% من الضوابط applicable/implemented/partially/not-applicable بسبب موقع؛ لا control بلا owner/evidence؛ الثغرات تتحول لخطة مؤرخة؛ سلطة مستقلة تمنح ATO محدود المدة (ادعاء حي مؤجّل).
- **التحقق:** control assessment مستقل، evidence sampling، وإعادة تقييم بعد تغيير جوهري؛ فشل إقلاع على missing-owner / coverage-gap / undated-poam / LiveAtoClaim.
- **الاعتماديات:** STR-002, SUP-001, GOV-001.
- **ما أُغلق:**
  - ADR pack + [`sov-baseline/control-baseline-matrix.json`](../sov-baseline/control-baseline-matrix.json) + JOURNEY / CROSSWALK / IMPACT / COVERAGE / OWNERSHIP_EVIDENCE / POAM / ATO_BOUNDARY
  - scenarios/audit/harness fixtures؛ `sov001-control-baseline-validate.ps1`؛ CODEOWNERS
  - `SovBaseline/` في ServiceDefaults + `AddSovBaseline` على Gateway/Payroll/HR.Core
  - `sov001-control-baseline-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-001-production-control-baseline-binding.md](../ADR-SOV-001-production-control-baseline-binding.md)؛ [SOV_001_ADOPTION_MINUTES.md](../SOV_001_ADOPTION_MINUTES.md)؛ [SOV_001_INDEPENDENT_REVIEW_WAIVER.md](../SOV_001_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_001_UAT_PACK.md](../SOV_001_UAT_PACK.md) ReadyForSign-off؛ [SOV_001_VERIFICATION.md](../SOV_001_VERIFICATION.md)؛ `Sov001ControlBaselinePackTests` + `Sov001ProductionBindingTests`
  - مزامنة blocker SOV-001 في [`control-catalog.json`](../str-legal/control-catalog.json) → Satisfied دون قلب `CTRL-*` Blocked-Legal
- **Blockers المتبقية:** STR-002/SUP-001/GOV-001 = Satisfied؛ live-independent-ato / continuous-assessment-workers / live-poam-system = Blocked؛ SYR-001/NAT-001 = NotStarted؛ SOV-002 = Satisfied (production data-sovereignty binding)
- **لاحقاً (خارج هذا الربط):** SOV-001a–e ATO حي / تقييم مستمر / POA&M حي / SYR-001؛ egress/JIT حي = SOV-002a–c

## SOV-002 — سيادة البيانات والإقامة ووصول الدعم

- **الحالة:** Done (production data-sovereignty binding)
- **الأولوية/الحجم/المسؤول:** P0-National / L / Data Sovereignty + Legal
- **النطاق:** DB/object/backups/logs/traces/CI artifacts/support dumps؛ residency؛ cross-border egress؛ remote support؛ subcontractors؛ telemetry؛ destruction؛ lawful disclosure؛ data flow map؛ ربط تشغيلي عند الإقلاع/CI مع `LiveSovereigntyClaim=false`.
- **معايير القبول:** كل نسخة وممر بيانات مصنف وموقعه معتمد؛ egress deny-by-default؛ وصول الدعم JIT ومسجل وموافق عليه؛ لا نسخة خارج النطاق القانوني؛ إثبات محو عند الانتهاء (ادعاءات حية مؤجّلة).
- **التحقق:** network/storage egress tests، supplier audit، data discovery، deletion certificate exercise؛ فشل إقلاع على unclassified / egress-allow-default / support-without-jit / missing-deletion-cert / LiveSovereigntyClaim.
- **الاعتماديات:** GOV-001, OPS-003, STR-002.
- **ما أُغلق:**
  - ADR pack + [`sov-data/data-sovereignty-matrix.json`](../sov-data/data-sovereignty-matrix.json) + JOURNEY / INVENTORY / FLOW_MAP / RESIDENCY / EGRESS / SUPPORT_JIT / DESTRUCTION
  - scenarios/audit/harness fixtures؛ `sov002-data-sovereignty-validate.ps1`؛ CODEOWNERS
  - `SovData/` في ServiceDefaults + `AddSovData` على Gateway/Payroll/HR.Core
  - `sov002-data-sovereignty-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-002-production-data-sovereignty-binding.md](../ADR-SOV-002-production-data-sovereignty-binding.md)؛ [SOV_002_ADOPTION_MINUTES.md](../SOV_002_ADOPTION_MINUTES.md)؛ [SOV_002_INDEPENDENT_REVIEW_WAIVER.md](../SOV_002_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_002_UAT_PACK.md](../SOV_002_UAT_PACK.md) ReadyForSign-off؛ [SOV_002_VERIFICATION.md](../SOV_002_VERIFICATION.md)؛ `Sov002DataSovereigntyPackTests` + `Sov002ProductionBindingTests`
  - مزامنة blocker SOV-002 في [`control-baseline-matrix.json`](../sov-baseline/control-baseline-matrix.json) → Satisfied دون رفع `LiveAtoClaim`
- **Blockers المتبقية:** GOV-001/OPS-003/STR-002/SOV-001 = Satisfied؛ live-egress-enforcement / live-support-jit-workers / live-deletion-certificate-system = Blocked؛ SYR-008/NAT-001 = NotStarted؛ SOV-003 = Satisfied (production key-sovereignty binding)
- **لاحقاً (خارج هذا الربط):** SOV-002a–e egress حي / JIT حي / محو حي / SYR-008؛ HSM حي = SOV-003a

## SOV-003 — سيادة المفاتيح وHSM والخوارزميات والوقت

- **الحالة:** Done (production key-sovereignty binding)
- **الأولوية/الحجم/المسؤول:** P0-National / XL / Cryptography Authority
- **النطاق:** key hierarchy؛ HSM/KMS؛ BYOK/HYOK حيث يلزم؛ separation env/tenant/purpose؛ dual control؛ rotation/revocation/backup/destruction؛ algorithm agility؛ TLS/certificates؛ trusted time؛ ربط تشغيلي عند الإقلاع/CI مع `LiveKeyCustodyClaim=false`.
- **معايير القبول:** 100% من مفاتيح الإنتاج الحساسة تحت HSM/KMS معتمد (عقود custody الآن)؛ المورد لا يفك البيانات منفرداً؛ تدوير واستعادة وفقد key custodian مجربة؛ clock drift ضمن حد موقع؛ inventory وexpiry alerts.
- **التحقق:** cryptographic inventory، key ceremony، rotation/failover، expired/revoked cert، time-source loss؛ فشل إقلاع على vendor-alone / no-dual-control / missing-rotation / clock-drift-unbounded / LiveKeyCustodyClaim.
- **الاعتماديات:** SEC-004, OPS-002, STR-002.
- **ما أُغلق:**
  - ADR pack + [`sov-keys/key-sovereignty-matrix.json`](../sov-keys/key-sovereignty-matrix.json) + JOURNEY / HIERARCHY / HSM_KMS / SEPARATION / DUAL_CONTROL / ROTATION / ALGORITHM_AGILITY / TRUSTED_TIME
  - scenarios/audit/harness fixtures؛ `sov003-key-sovereignty-validate.ps1`؛ CODEOWNERS
  - `SovKeys/` في ServiceDefaults + `AddSovKeys` على Gateway/Payroll/HR.Core
  - `sov003-key-sovereignty-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-003-production-key-sovereignty-binding.md](../ADR-SOV-003-production-key-sovereignty-binding.md)؛ [SOV_003_ADOPTION_MINUTES.md](../SOV_003_ADOPTION_MINUTES.md)؛ [SOV_003_INDEPENDENT_REVIEW_WAIVER.md](../SOV_003_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_003_UAT_PACK.md](../SOV_003_UAT_PACK.md) ReadyForSign-off؛ [SOV_003_VERIFICATION.md](../SOV_003_VERIFICATION.md)؛ `Sov003KeySovereigntyPackTests` + `Sov003ProductionBindingTests`
  - مزامنة blocker SOV-003 في مصفوفتي SOV-002 وEXT-004 → Satisfied دون قلب `hsm-prod` أو إغلاق SEC-004
- **Blockers المتبقية:** OPS-002/STR-002/SOV-002/EXT-004 = Satisfied؛ SEC-004 / live-hsm-prod / live-key-ceremony-workers / live-trusted-time = Blocked؛ SOV-004 = Satisfied (production zero-trust binding)؛ SIG-001/NAT-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** SOV-003a–e HSM حي / مراسم حية / تدوير حي / trusted time حي؛ إغلاق SEC-004؛ PAM حي = SOV-004a

## SOV-004 — Zero Trust وPAM وJIT للإدارة

- **الحالة:** Done (production zero-trust binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Security Architecture
- **النطاق:** no standing privilege؛ PAM/JIT/JEA؛ phishing-resistant MFA؛ admin workstations؛ session record؛ approvals؛ break-glass؛ service admin separation؛ database/cloud/K8s/Keycloak access؛ quarterly reviews؛ ربط تشغيلي عند الإقلاع/CI مع `LivePamClaim=false`.
- **معايير القبول:** لا حساب root مشترك أو دائم؛ كل privilege مؤقت ومسبب؛ break-glass ينبه فوراً ويراجع؛ admin لا يصل من جهاز عادي؛ dormant/orphan access صفر.
- **التحقق:** privilege escalation attempts، compromised admin simulation، access recertification؛ فشل إقلاع على standing-privilege / jit-without-approval / admin-from-ordinary-device / break-glass-no-alert / LivePamClaim.
- **الاعتماديات:** IAM-003, IAM-004, SOV-003.
- **ما أُغلق:**
  - ADR pack + [`sov-zt/zero-trust-matrix.json`](../sov-zt/zero-trust-matrix.json) + JOURNEY / NO_STANDING / PAM_JIT / MFA / ADMIN_WORKSTATION / BREAK_GLASS / RECERTIFICATION
  - scenarios/audit/harness fixtures؛ `sov004-zero-trust-validate.ps1`؛ CODEOWNERS
  - `SovZt/` في ServiceDefaults + `AddSovZt` على Gateway/Payroll/HR.Core
  - `sov004-zero-trust-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-004-production-zero-trust-binding.md](../ADR-SOV-004-production-zero-trust-binding.md)؛ [SOV_004_ADOPTION_MINUTES.md](../SOV_004_ADOPTION_MINUTES.md)؛ [SOV_004_INDEPENDENT_REVIEW_WAIVER.md](../SOV_004_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_004_UAT_PACK.md](../SOV_004_UAT_PACK.md)؛ [SOV_004_VERIFICATION.md](../SOV_004_VERIFICATION.md)؛ `Sov004ZeroTrustPackTests` + `Sov004ProductionBindingTests`
  - مزامنة blocker SOV-004 في مصفوفة SOV-003 → Satisfied دون رفع `LiveKeyCustodyClaim`
- **Blockers المتبقية:** IAM-003/IAM-004/SOV-003 = Satisfied؛ live-pam-workers / live-admin-workstation-enforcement / live-break-glass-soc / live-access-recertification = Blocked؛ SOV-005 = Satisfied (production threat-modeling binding)؛ NAT-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** SOV-004a–d PAM حي / محطات إدارة حية / SOC break-glass حي / recertification حي؛ مراجعة تهديد حية = SOV-005a

## SOV-005 — Threat Modeling وهندسة إساءة الاستخدام

- **الحالة:** Done (production threat-modeling binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Product Security + Domain Teams
- **النطاق:** trust boundaries؛ assets/actors؛ STRIDE أو منهج معتمد؛ payroll fraud؛ insider؛ mass export؛ tenant escape؛ message replay؛ supply chain؛ migration؛ recovery؛ abuse cases لكل رحلة؛ ربط تشغيلي عند الإقلاع/CI مع `LiveThreatModelClaim=false`.
- **معايير القبول:** كل تهديد high له control/test/owner؛ النموذج يتحدث مع كل تغيير جوهري؛ misuse stories ضمن backlog؛ المخاطر المتبقية موقعة.
- **التحقق:** threat-model review مستقل، attack-tree walkthrough، وربط automated tests؛ فشل إقلاع على missing-stride / high-without-control / residual-unsigned / LiveThreatModelClaim.
- **الاعتماديات:** STR-001, TEN-001, INT-003, SOV-004.
- **ما أُغلق:**
  - ADR pack + [`sov-threat/threat-model-matrix.json`](../sov-threat/threat-model-matrix.json) + JOURNEY / STRIDE / TRUST_BOUNDARIES / ABUSE_CASES / HIGH_COVERAGE / CHANGE_TRIGGER / MISUSE_BACKLOG / RESIDUAL_RISK
  - scenarios/audit/harness fixtures؛ `sov005-threat-model-validate.ps1`؛ CODEOWNERS
  - `SovThreat/` في ServiceDefaults + `AddSovThreat` على Gateway/Payroll/HR.Core
  - `sov005-threat-model-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-005-production-threat-modeling-binding.md](../ADR-SOV-005-production-threat-modeling-binding.md)؛ [SOV_005_ADOPTION_MINUTES.md](../SOV_005_ADOPTION_MINUTES.md)؛ [SOV_005_INDEPENDENT_REVIEW_WAIVER.md](../SOV_005_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_005_UAT_PACK.md](../SOV_005_UAT_PACK.md)؛ [SOV_005_VERIFICATION.md](../SOV_005_VERIFICATION.md)؛ `Sov005ThreatModelPackTests` + `Sov005ProductionBindingTests`
  - مزامنة blocker SOV-005 في مصفوفة SOV-004 → Satisfied دون رفع `LivePamClaim`
- **Blockers المتبقية:** STR-001/TEN-001/INT-003/SOV-004 = Satisfied؛ live-independent-tm-review / live-attack-tree-walkthrough / live-misuse-backlog-sync / live-residual-risk-signoff = Blocked؛ SOV-006 = Satisfied (production pen-red binding)؛ NAT-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** SOV-005a–d مراجعة حية / attack-tree حي / misuse sync حي / residual signoff حي؛ اختراق حي = SOV-006a؛ FRD-001 احتيال مستمر

## SOV-006 — اختبار اختراق وRed/Purple Team ومكافحة الاحتيال الداخلي

- **الحالة:** Done (production pen-red binding)
- **الأولوية/الحجم/المسؤول:** P2-National / XL / Independent Security Assessor + SOC
- **النطاق:** web/API/mobile؛ identity/federation؛ infrastructure/cloud/container؛ tenant escape؛ files؛ social/admin paths؛ red team؛ purple detection؛ payroll fraud/insider scenarios؛ remediation/retest؛ ربط تشغيلي عند الإقلاع/CI مع `LivePenClaim=false`.
- **معايير القبول:** صفر Critical/High مفتوح دون قبول محدود رسمياً؛ كل finding معاد اختباره؛ SOC يكتشف سيناريوهات متفقاً عليها؛ assessor مستقل عن المنفذ والمورد.
- **التحقق:** signed reports، raw evidence محمي، detection coverage، remediation closure؛ فشل إقلاع على open-critical / assessor-not-independent / finding-without-retest / purple-undetected / LivePenClaim.
- **الاعتماديات:** SOV-001, SOV-004, OBS-003, REL-001, SOV-005.
- **ما أُغلق:**
  - ADR pack + [`sov-pentest/pen-red-matrix.json`](../sov-pentest/pen-red-matrix.json) + JOURNEY / SCOPE / INDEPENDENCE / FINDING_CLOSURE / PURPLE / FRAUD_INSIDER / EVIDENCE / REMEDIATION
  - scenarios/audit/harness fixtures؛ `sov006-pen-red-validate.ps1`؛ CODEOWNERS
  - `SovPentest/` في ServiceDefaults + `AddSovPentest` على Gateway/Payroll/HR.Core
  - `sov006-pen-red-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-006-production-pen-red-binding.md](../ADR-SOV-006-production-pen-red-binding.md)؛ [SOV_006_ADOPTION_MINUTES.md](../SOV_006_ADOPTION_MINUTES.md)؛ [SOV_006_INDEPENDENT_REVIEW_WAIVER.md](../SOV_006_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_006_UAT_PACK.md](../SOV_006_UAT_PACK.md)؛ [SOV_006_VERIFICATION.md](../SOV_006_VERIFICATION.md)؛ `Sov006PenRedPackTests` + `Sov006ProductionBindingTests`
  - مزامنة blocker SOV-006 في مصفوفة SOV-005 → Satisfied دون رفع `LiveThreatModelClaim` أو إغلاق NAT-001/NAT-002/FRD-001
- **Blockers المتبقية:** SOV-001/SOV-004/OBS-003/REL-001/SOV-005 = Satisfied؛ live-independent-pen-assessment / live-red-team-exercise / live-purple-detection-validation / live-remediation-retest-closure = Blocked؛ NAT-001/NAT-002 = NotStarted
- **لاحقاً (خارج هذا الربط):** SOV-006a–e تقييم اختراق حي / red حي / purple حي / remediation حي / انضمام NAT-002؛ FRD-001 احتيال مستمر؛ SOV-007 = Done كـ production vendor-exit binding (ليس drill خروج حي)

## SOV-007 — استقلالية المورد والملكية وخطة الخروج

- **الحالة:** Done (production vendor-exit binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Procurement + Legal + Architecture
- **النطاق:** IP/source/build/IaC/SBOM/docs ownership؛ open export؛ escrow؛ license rights؛ subcontractors؛ knowledge transfer؛ alternative operators/providers/components؛ exit assistance؛ data destruction؛ ربط تشغيلي عند الإقلاع/CI مع `LiveVendorClaim=false`.
- **معايير القبول:** clean-room rebuild في موقع بديل من artifacts المملوكة؛ تصدير 100% من البيانات والمرفقات والعقود بصيغ موثقة؛ لا مكون حرج بلا بديل/استثناء؛ المورد السابق يثبت المحو.
- **التحقق:** vendor-exit drill، independent build، restore/export/import إلى بيئة بديلة؛ فشل إقلاع على rebuild-unowned / incomplete-export / critical-no-alternate / destruction-unattested / LiveVendorClaim.
- **الاعتماديات:** SUP-001, OPS-005, DB-001, SOV-006.
- **ما أُغلق:**
  - ADR pack + [`sov-vendor/vendor-exit-matrix.json`](../sov-vendor/vendor-exit-matrix.json) + JOURNEY / OWNERSHIP / ESCROW / EXPORT / ALTERNATES / CLEAN_ROOM / KNOWLEDGE / DESTRUCTION
  - scenarios/audit/harness fixtures؛ `sov007-vendor-exit-validate.ps1`؛ CODEOWNERS
  - `SovVendor/` في ServiceDefaults + `AddSovVendor` على Gateway/Payroll/HR.Core
  - `sov007-vendor-exit-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-SOV-007-production-vendor-exit-binding.md](../ADR-SOV-007-production-vendor-exit-binding.md)؛ [SOV_007_ADOPTION_MINUTES.md](../SOV_007_ADOPTION_MINUTES.md)؛ [SOV_007_INDEPENDENT_REVIEW_WAIVER.md](../SOV_007_INDEPENDENT_REVIEW_WAIVER.md)
  - [SOV_007_UAT_PACK.md](../SOV_007_UAT_PACK.md)؛ [SOV_007_VERIFICATION.md](../SOV_007_VERIFICATION.md)؛ `Sov007VendorExitPackTests` + `Sov007ProductionBindingTests`
  - مزامنة blocker SOV-007 في مصفوفة STR-006 → Satisfied دون رفع `National24x7Claim` أو إغلاق VEN-001/NAT-001/SAN-001
- **Blockers المتبقية:** SUP-001/OPS-005/DB-001/SOV-006 = Satisfied؛ live-clean-room-rebuild / live-vendor-exit-drill / live-full-export-import / live-vendor-destruction-attestation = Blocked؛ VEN-001/NAT-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** SOV-007a–e clean-room حي / vendor-exit drill حي / export-import حي / destruction حي / انضمام VEN-001/NAT-002

## ARC-001 — تصنيف الحرجية ومناطق الفشل وخريطة التبعيات

- **الحالة:** Done (production criticality binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Enterprise Architecture + SRE
- **النطاق:** Tier-0/1/2؛ journey dependency map؛ shared DNS/PKI/KMS/registry/CI/telemetry؛ failure domains؛ SLO/RPO/RTO؛ capacity؛ graceful degradation؛ SPOFs؛ recovery order؛ ربط تشغيلي عند الإقلاع/CI مع `LiveCriticalityClaim=false`.
- **معايير القبول:** 100% من الخدمات والتبعيات مصنفة؛ لا SPOF غير مقبول في Tier-0/1؛ كل هدف رقمي وله telemetry؛ ترتيب الاستعادة يثبت بالتمرين.
- **التحقق:** dependency discovery، architecture failure walkthrough، controlled dependency shutdown؛ فشل إقلاع على unclassified / tier01-spof / target-without-telemetry / recovery-order-missing / LiveCriticalityClaim.
- **الاعتماديات:** STR-001, OBS-001, OPS-001, SOV-007.
- **ما أُغلق:**
  - ADR pack + [`arc-criticality/criticality-matrix.json`](../arc-criticality/criticality-matrix.json) + JOURNEY / TIERS / DEPENDENCIES / FAILURE_DOMAINS / TELEMETRY / SPOF / RECOVERY
  - scenarios/audit/harness fixtures؛ `arc001-criticality-validate.ps1`؛ CODEOWNERS
  - `ArcCriticality/` في ServiceDefaults + `AddArcCriticality` على Gateway/Payroll/HR.Core
  - `arc001-criticality-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-ARC-001-production-criticality-binding.md](../ADR-ARC-001-production-criticality-binding.md)؛ [ARC_001_ADOPTION_MINUTES.md](../ARC_001_ADOPTION_MINUTES.md)؛ [ARC_001_INDEPENDENT_REVIEW_WAIVER.md](../ARC_001_INDEPENDENT_REVIEW_WAIVER.md)
  - [ARC_001_UAT_PACK.md](../ARC_001_UAT_PACK.md)؛ [ARC_001_VERIFICATION.md](../ARC_001_VERIFICATION.md)؛ `Arc001CriticalityPackTests` + `Arc001ProductionBindingTests`
- **Blockers المتبقية:** STR-001/OBS-001/OPS-001/SOV-007 = Satisfied؛ live-architecture-failure-walkthrough / live-controlled-dependency-shutdown / live-recovery-order-drill / live-full-dependency-discovery = Blocked؛ ARC-002 = Satisfied (production reference-architecture binding؛ live two-site = ARC-002a)؛ PHY-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** ARC-001a–e walkthrough حي / shutdown حي / recovery drill حي / discovery حي؛ ARC-002 Done كـ reference binding منفصل؛ PHY-001 لاحقاً

## ARC-002 — معمارية مرجعية وطنية قابلة للنشر في أكثر من موقع

- **الحالة:** Done (production reference-architecture binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Architecture Board
- **النطاق:** حسم bounded contexts التي تبقى خدمات أو تندمج؛ synchronous vs event؛ data ownership؛ multi-region consistency؛ deployment profiles؛ tenancy؛ control/data planes؛ unsupported topology؛ ADRs؛ ربط تشغيلي عند الإقلاع/CI مع `LiveReferenceClaim=false`.
- **معايير القبول:** لا distributed complexity بلا سبب ومالك؛ نفس artifact يدعم موقعاً رئيسياً وDR؛ topology تحقق المغلف؛ الحدود تمنع direct DB/domain coupling؛ reference implementation تعمل.
- **التحقق:** architecture fitness tests، deployment في موقعين، dependency/coupling scan؛ فشل إقلاع على unjustified-distribution / artifact-drift / unsupported-topology / direct-db / LiveReferenceClaim.
- **الاعتماديات:** ARC-001, TEN-001, INT-001, OPS-005.
- **ما أُغلق:**
  - ADR pack + [`arc-reference/reference-architecture-matrix.json`](../arc-reference/reference-architecture-matrix.json) + JOURNEY / CONTEXTS / SYNC_EVENT / DATA_OWNERSHIP / MULTI_SITE / TENANCY / TOPOLOGY / COUPLING / FITNESS
  - scenarios/audit/harness fixtures؛ `arc002-reference-validate.ps1`؛ CODEOWNERS
  - `ArcReference/` في ServiceDefaults + `AddArcReference` على Gateway/Payroll/HR.Core
  - `arc002-reference-runtime-validate.ps1` (تشغيل/CI)
  - [ADR-ARC-002-production-reference-architecture-binding.md](../ADR-ARC-002-production-reference-architecture-binding.md)؛ [ARC_002_ADOPTION_MINUTES.md](../ARC_002_ADOPTION_MINUTES.md)؛ [ARC_002_INDEPENDENT_REVIEW_WAIVER.md](../ARC_002_INDEPENDENT_REVIEW_WAIVER.md)
  - [ARC_002_UAT_PACK.md](../ARC_002_UAT_PACK.md)؛ [ARC_002_VERIFICATION.md](../ARC_002_VERIFICATION.md)؛ `Arc002ReferencePackTests` + `Arc002ProductionBindingTests`
  - مزامنة blocker ARC-002 في مصفوفة ARC-001 → Satisfied دون رفع `LiveCriticalityClaim` أو إغلاق QA-005/PHY-001
- **Blockers المتبقية:** ARC-001/TEN-001/INT-001/OPS-005 = Satisfied؛ live-two-site-deployment / live-architecture-fitness-envelope / live-coupling-boundary-scan / live-reference-implementation-signoff = Blocked؛ QA-005/PHY-001 = NotStarted
- **لاحقاً (خارج هذا الربط):** ARC-002a–e نشر موقعين حي / fitness حي / coupling scan حي / signoff حي / انضمام QA-005/PHY

## CAP-001 — اعتماد National Workload Envelope واختباراته

- **الحالة:** Done (national envelope harness + results schema + headroom tooling؛ إثبات حي production-like = تشغيل مستقل قبل ORR)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Performance + Product + Finance
- **النطاق:** تحويل STR-001 إلى scenarios وdatasets؛ 100% peak soak 72h؛ 150% stress 4h؛ 200% spike 15m؛ payroll/report/file bursts؛ regional mix؛ failure during load؛ 30% headroom.
- **معايير القبول:** تحقيق SLO وعدم فقد/ازدواج عند 100%؛ تدهور آمن عند 150/200%؛ headroom ≥30% في الموارد الحرجة؛ نتائج خام قابلة للإعادة وموقعة.
- **التحقق:** independent performance run على production-like، query/queue/resource profiling.
- **الاعتماديات:** STR-001, NFR-001, PERF-001..004, OPS-002.
- **أدلة الإغلاق:**
  - [CAP_001_NATIONAL_ENVELOPE.md](../CAP_001_NATIONAL_ENVELOPE.md)
  - [`perf/scenarios/cap001-national-envelope.json`](../../perf/scenarios/cap001-national-envelope.json) — soak 72h@100%، stress 4h@150%، spike 15m@200%، failure-during-load، regional mix (14 محافظة)، payroll window، headroom 30%
  - k6: `cap001-soak-72h.js` / `cap001-stress-150.js` / `cap001-spike-200.js` / `cap001-failure-during-load.js` + `lib/cap001.js`
  - [`perf/cap001-results-schema.json`](../../perf/cap001-results-schema.json) — SLO pass/fail، latencies، error rates، headroom
  - `scripts/cap001-headroom-calculator.ps1` / `scripts/cap001-envelope-validate.ps1`
  - [`perf/docker-compose.cap001.yml`](../../perf/docker-compose.cap001.yml) — runners + resource limit notes
  - `Cap001EnvelopeTests` (CI harness)
- **Blockers المتبقية:** signed independent national run على production-like (خارج إغلاق المستودع)؛ SYR-002 census قد يرفع syria-interim دون إعادة بناء الهارنس
- **مدخلات جاهزة من NFR-001:** نموذج الحمل + `perf/k6` + seed الاصطناعي + خطة 24م + bottleneck budget — يشغّل CAP المغلف الوطني (72h/150%/200%) دون إعادة بناء الهارنس.

## CAP-002 — حماية الحمل والتدهور العادل بين الجهات

- **الأولوية/الحجم/المسؤول:** P2-National / L / Platform + SRE
- **النطاق:** quotas؛ tenant fairness؛ admission control؛ backpressure؛ load shedding؛ bounded queues؛ priority للرواتب/الهوية؛ retry budgets؛ cache stampede؛ expensive query guard؛ overload UX.
- **معايير القبول:** جهة واحدة لا تجوّع غيرها؛ 200% load لا يسبب cascading failure/OOM؛ critical journeys تبقى ضمن tier objective؛ recovery backlog ضمن زمن محدد؛ الرفض صريح وقابل لإعادة آمنة.
- **التحقق:** noisy-neighbor، thundering herd، queue saturation، retry storm.
- **الاعتماديات:** CAP-001, TEN-003, OBS-002.
- **الحالة:** Done (in-memory tenant admission control with per-tenant quotas)
- **أدلة الإغلاق:**
  - [CAP_002_LOAD_PROTECTION.md](../CAP_002_LOAD_PROTECTION.md)
  - `docs/cap/tenant-quota-policy.json`
  - `src/Shared/HRManagement.ServiceDefaults/Capacity/TenantAdmissionControl.cs` (`TenantQuota`, `AdmissionDecision`)
  - `scripts/cap002-validate.ps1` / `Cap002LoadProtectionTests`

## CAP-003 — تخطيط السعة والتوريد لثلاث سنوات

- **الأولوية/الحجم/المسؤول:** P2-National / M / Capacity + Finance + Procurement
- **النطاق:** forecasts 12/24/36m؛ seasonal growth؛ storage/backup/DR/logs؛ license limits؛ procurement lead time؛ scale triggers؛ cost؛ reserve؛ hardware/cloud quotas.
- **معايير القبول:** لا مورد متوقع بلوغه 70% قبل وصول التوسعة؛ DR يتحمل الحمل الحرج الفعلي؛ مراجعة ربع سنوية؛ trigger مرتبط بمالك وطلب تمويل.
- **التحقق:** forecast backtest، capacity dashboard، procurement simulation.
- **الاعتماديات:** CAP-001, FIN-001, OPS-004.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/CAP_003_CAPACITY_PLAN.md](../CAP_003_CAPACITY_PLAN.md)
  - [`docs/cap/capacity-forecast.json`](../cap/capacity-forecast.json) — 7 resources × 3 horizons, trigger owners + FIN-001 funding links, DR capacity, seasonal peaks, quarterly review
  - [scripts/cap003-validate.ps1](../../scripts/cap003-validate.ps1)
  - `Cap003CapacityTests`

## SRE-001 — نموذج تشغيل ومناوبة 24x7

- **الأولوية/الحجم/المسؤول:** P1-National / L / Service Owner + SRE + SOC
- **النطاق:** NOC/SOC/on-call rosters؛ handover؛ access؛ tools؛ fatigue؛ follow-the-sun إن وجد؛ escalation؛ vendor contacts؛ coverage holidays؛ compensation؛ training.
- **معايير القبول:** جدول فعلي بلا فجوة؛ بديلان لكل مهارة Tier-0؛ P1 acknowledge ≤5m وIncident Commander ≤10m في الاختبار؛ لا مناوبة بلا وصول وأدوات.
- **التحقق:** unannounced page drills، roster/access audit، handover exercise.
- **الاعتماديات:** STR-006, REL-003, OBS-002.
- **الحالة:** Done (نموذج + سياسات + أدوات تحقق؛ تعبئة الأسماء وتمارين الصفحة الحية قبل ORR الوطني)
- **أدلة الإغلاق:**
  - [ON_CALL_ROSTER.md](../ON_CALL_ROSTER.md)
  - [SRE_001_ESCALATION_POLICY.md](../SRE_001_ESCALATION_POLICY.md)
  - [SRE_001_ACCESS_MATRIX.md](../SRE_001_ACCESS_MATRIX.md)
  - [SRE_001_DRILL_PLAYBOOK.md](../SRE_001_DRILL_PLAYBOOK.md) / [SRE_001_VERIFICATION.md](../SRE_001_VERIFICATION.md)
  - `scripts/sre001-oncall-validate.ps1` / `Sre001OnCallTests`

## SRE-002 — Operational Readiness Review وRunbooks قابلة للتنفيذ

- **الحالة:** Done (ORR checklist + Tier-0 runbook templates binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / SRE + Service Teams
- **النطاق:** owner؛ SLO/dashboard/alerts؛ dependency؛ capacity؛ deploy/rollback؛ backup/restore؛ common failures؛ security؛ maintenance؛ support؛ known errors؛ ORR checklist لكل service.
- **معايير القبول:** 100% Tier-0/1 يعبر ORR؛ مناوب لم يكتب الخدمة ينفذ runbook؛ لا alert بلا runbook؛ service جديدة لا تستقبل traffic قبل توقيع ORR.
- **التحقق:** shadow on-call، random runbook drill، ORR audit.
- **الاعتماديات:** ARC-001 (Satisfied — production criticality binding؛ drills لاحقاً), SRE-001, REL-003.
- **ما أُغلق:**
  - [SRE_002_ORR.md](../SRE_002_ORR.md) + [`sre/orr-checklist-template.json`](../sre/orr-checklist-template.json)
  - [`runbooks/db-connection-failure.md`](../runbooks/db-connection-failure.md) / [`runbooks/queue-backup.md`](../runbooks/queue-backup.md) / [`runbooks/oom-kill.md`](../runbooks/oom-kill.md) / [`runbooks/cert-expiry.md`](../runbooks/cert-expiry.md)
  - `scripts/sre002-validate.ps1`؛ `Sre002OrrTests`
- **Blockers المتبقية:** ARC-001/SRE-001 = Satisfied؛ live shadow on-call drill / live random runbook drill = Blocked

## SRE-003 — قيادة الحوادث والتواصل وصفحة الحالة

- **الحالة:** Done (incident command framework + severity model + status page + postmortem template)
- **الأولوية/الحجم/المسؤول:** P1-National / M / Incident Management
- **النطاق:** severity؛ command roles؛ war room؛ status page؛ agency/public templates؛ legal/privacy/SOC escalation؛ evidence؛ postmortem؛ action tracking؛ regulator timelines.
- **معايير القبول:** P1 communication ≤15m ثم cadence محدد؛ تمرين فصلي؛ timeline محفوظ؛ postmortem بلا لوم خلال SLA؛ 100% actions الحرجة لها مالك وموعد.
- **التحقق:** major-incident game day، communication timing، action closure audit.
- **الاعتماديات:** SRE-001, OBS-002, GOV-001.
- **ما أُغلق:**
  - [SRE_003_INCIDENT_COMMAND.md](../SRE_003_INCIDENT_COMMAND.md)
  - [`sre/incident-severity-model.json`](../sre/incident-severity-model.json) / [`sre/status-page-templates.md`](../sre/status-page-templates.md) / [`sre/postmortem-template.md`](../sre/postmortem-template.md)
  - `scripts/sre003-validate.ps1`؛ `Sre003IncidentTests`
- **Blockers المتبقية:** SRE-001/OBS-002/GOV-001 = Satisfied؛ live major-incident game day / live communication timing audit = Blocked

## OPS-006 — التعافي السيبراني وClean-Room Rebuild

- **الحالة:** Done (clean-room runbook + immutable pipeline + credential reset binding)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Cyber Recovery + Platform
- **النطاق:** ransomware/compromised keys/poisoned images/long-lived corruption؛ offline/immutable artifacts؛ trusted bootstrap؛ forensics؛ clean room؛ credential/key reset؛ data validation؛ business resumption.
- **معايير القبول:** clean rebuild من مصادر موثقة ضمن Cyber-RTO؛ artifacts موقعة ومفحوصة؛ تدوير كل الثقة؛ النسخة المستخدمة خارج blast radius؛ تمرين كامل مرتان سنوياً.
- **التحقق:** assumed-breach exercise، backup compromise، registry/KMS loss، post-restore integrity.
- **الاعتماديات:** OPS-003, OPS-004, SOV-003, SUP-001.
- **ما أُغلق:**
  - [OPS_006_CYBER_RECOVERY.md](../OPS_006_CYBER_RECOVERY.md) / [OPS_006_CLEANROOM_RUNBOOK.md](../OPS_006_CLEANROOM_RUNBOOK.md) / [OPS_006_CREDENTIAL_RESET.md](../OPS_006_CREDENTIAL_RESET.md) / [OPS_006_IMMUTABLE_ARTIFACTS.md](../OPS_006_IMMUTABLE_ARTIFACTS.md)
  - [`ops006/immutable-artifact-pipeline.json`](../ops006/immutable-artifact-pipeline.json) / [`ops006/assumed-breach-scenarios.json`](../ops006/assumed-breach-scenarios.json)
  - `scripts/ops006-validate.ps1`؛ `Ops006CyberRecoveryTests`
- **Blockers المتبقية:** OPS-003/OPS-004/SOV-003/SUP-001 = Satisfied؛ `live-clean-room-drill` / `live-assumed-breach-game-day` = Blocked

## OPS-007 — دورة حياة المكونات والترقيع والشهادات

- **الحالة:** Done (component inventory + patch SLA policy + certificate alert windows)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Platform Security
- **النطاق:** inventory/EOL؛ patch SLAs؛ emergency vuln؛ compatibility lab؛ certificate/secret expiry؛ OS/DB/MQ/IdP/object upgrades؛ firmware؛ rollback؛ maintenance windows.
- **معايير القبول:** صفر EOL غير مستثنى؛ exploited Critical ≤24h أو تعويض معتمد؛ تنبيه cert 60/30/15/7؛ ترقية كل مكون حرج دون كسر SLO؛ inventory دقيق.
- **التحقق:** emergency patch drill، expired cert drill، N-1→N upgrade/rollback.
- **الاعتماديات:** SUP-001, DB-001, REL-004.
- **ما أُغلق:**
  - [OPS_007_COMPONENT_LIFECYCLE.md](../OPS_007_COMPONENT_LIFECYCLE.md)
  - [`ops007/component-inventory.json`](../ops007/component-inventory.json) / [`ops007/patch-sla-policy.json`](../ops007/patch-sla-policy.json)
  - `scripts/ops007-validate.ps1`؛ `Ops007LifecycleTests`
- **Blockers المتبقية:** SUP-001/DB-001/REL-004 = Satisfied؛ live emergency patch drill / live expired cert drill = Blocked

## BCP-001 — استمرارية الأعمال والوضع اليدوي أو المتدهور

- **الحالة:** Done (Tier-0/1 manual procedures + degraded matrix + 8h drill + re-entry binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Business Continuity + HR/Finance
- **النطاق:** تعطل منصة/هوية/شبكة/دفع؛ priority journeys؛ manual forms/approvals؛ limits/fraud controls؛ local continuity؛ communication؛ capture backlog؛ re-entry/reconciliation؛ exercise.
- **معايير القبول:** إجراء معتمد لكل رحلة Tier-0/1؛ واضح ما يمنع يدوياً؛ تمرين ≥8h؛ 100% من العمليات اليدوية تعاد وتُصالح دون ازدواج؛ المواد متاحة أثناء الانقطاع.
- **التحقق:** business continuity drill مع فروع، recovery backlog reconciliation.
- **الاعتماديات:** ARC-001 (Satisfied — production criticality binding؛ drills لاحقاً), OPS-004, IAM-003.
- **ما أُغلق:**
  - [BCP_001_MANUAL_PROCEDURES.md](../BCP_001_MANUAL_PROCEDURES.md) / [BCP_001_REENTRY_RECONCILIATION.md](../BCP_001_REENTRY_RECONCILIATION.md) / [BCP_001_DRILL_PLAYBOOK.md](../BCP_001_DRILL_PLAYBOOK.md)
  - [`bcp/degraded-mode-matrix.json`](../bcp/degraded-mode-matrix.json) / [`bcp/degraded-mode-triggers.json`](../bcp/degraded-mode-triggers.json)
  - `scripts/bcp001-validate.ps1`؛ `Bcp001ContinuityTests`
- **Blockers المتبقية:** ARC-001/OPS-004/IAM-003 = Satisfied؛ live 8h branch drill = Blocked

## REL-004 — سياسة الإصدار وتوافق API/Event/Schema

- **الحالة:** Done (production release-compatibility binding)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Release + API/Data Governance
- **النطاق:** N/N-1 support؛ semantic contracts؛ expand/migrate/contract؛ event schema؛ client inventory؛ deprecation؛ feature flags؛ release notes؛ compatibility matrix؛ data rollback limits.
- **معايير القبول:** لا breaking removal قبل نافذتين وإشعار؛ contract tests إلزامية؛ mixed-version cluster يعمل؛ schema migration لا تمنع rollback ضمن النافذة؛ كل release له matrix.
- **التحقق:** N-1 clients/consumers، rolling mixed-version، rollback after migration.
- **الاعتماديات:** DB-001, PROD-007, EXT-005.
- **ما أُغلق:**
  - [REL_004_COMPATIBILITY_POLICY.md](../REL_004_COMPATIBILITY_POLICY.md) — N/N-1، deprecation نافذتين، expand/migrate/contract، event additive-only ضمن minor
  - [`rel-compat/api-client-inventory.json`](../rel-compat/api-client-inventory.json) — Gateway routes + inter-service + external (EXT-005 purposes)
  - [`rel-compat/event-schema-inventory.json`](../rel-compat/event-schema-inventory.json) — MassTransit contracts + SchemaVersion
  - [`rel-compat/compatibility-matrix-template.md`](../rel-compat/compatibility-matrix-template.md)； [`rel-compat/deprecation-register.json`](../rel-compat/deprecation-register.json)
  - [`rel-compat/openapi-baseline/`](../rel-compat/openapi-baseline/) — README + `hr-core-v1.openapi.json` stub
  - [REL_004_RELEASE_NOTES_TEMPLATE.md](../REL_004_RELEASE_NOTES_TEMPLATE.md)； [REL_004_ROLLBACK_POLICY.md](../REL_004_ROLLBACK_POLICY.md)
  - `scripts/rel004-openapi-diff.ps1`؛ `scripts/rel004-compat-validate.ps1`
  - `Rel004CompatibilityTests` — inventories, docs, baselines, validate/diff scripts
- **Blockers المتبقية:** DB-001/PROD-007/EXT-005 = Satisfied؛ live mixed-version cluster drill / national-openapi-publish = Blocked أو مؤجّل لـ REL-005/EXT-005a
- **لاحقاً (خارج هذا الربط):** REL-005 progressive delivery + canary rollback ≤15m؛ EXT-005a governed OpenAPI publish

## REL-005 — Progressive Delivery والتراجع الآمن

- **الحالة:** Done (canary gates + rollback policy + progressive delivery framework)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Release Engineering + SRE
- **النطاق:** canary/blue-green؛ automated health/business gates؛ feature flags؛ tenant/region cohorts؛ abort؛ database/event compatibility؛ rollback؛ audit؛ approval.
- **معايير القبول:** rollout على مراحل؛ تجاوز error budget أو business invariant يوقف آلياً؛ rollback ≤15m للخدمة؛ لا فساد schema/event/payroll؛ digest معلوم لكل cohort.
- **التحقق:** bad-release injection، failed migration، canary signal validation، rollback under load.
- **الاعتماديات:** DEP-001, REL-004, OBS-002.
- **ما أُغلق:**
  - [REL_005_PROGRESSIVE_DELIVERY.md](../REL_005_PROGRESSIVE_DELIVERY.md)
  - [`rel/canary-gates.json`](../rel/canary-gates.json)
  - `scripts/rel005-validate.ps1`؛ `Rel005ProgressiveDeliveryTests`
- **Blockers المتبقية:** DEP-001/REL-004/OBS-002 = Satisfied؛ live bad-release injection / live canary signal validation = Blocked

## REL-006 — Release Train وفترات الحظر وHotfix

- **الأولوية/الحجم/المسؤول:** P2-National / M / Change Management
- **النطاق:** cadence؛ payroll/legal freeze windows؛ risk classification؛ approvals؛ emergency path؛ security patches؛ comms؛ release calendar؛ post-implementation review؛ evidence.
- **معايير القبول:** لا deploy أثناء close بلا exception؛ hotfix بمراجعتين وتدقيق ثم مراجعة ≤24h؛ 100% releases مسجلة؛ failed change rate وlead time مقاسان.
- **التحقق:** emergency hotfix drill، blackout enforcement، change record audit.
- **الاعتماديات:** REL-005, REL-003.
- **الحالة:** Done (release cadence + freeze windows + emergency hotfix path + risk classification)
- **أدلة الإغلاق:**
  - [REL_006_RELEASE_TRAIN.md](../REL_006_RELEASE_TRAIN.md)
  - [`docs/rel/freeze-windows.json`](../rel/freeze-windows.json) — payroll-close, fiscal-year-end, national-holidays, pilot-stabilization
  - `scripts/rel006-validate.ps1`؛ `Rel006ReleaseTrainTests`
- **Blockers المتبقية:** REL-005/REL-003 = Satisfied؛ live emergency hotfix drill / live blackout enforcement = Blocked

## REL-007 — التوسع الوطني بالموجات وHypercare

- **الأولوية/الحجم/المسؤول:** P2-National / XL / National Rollout Office
- **النطاق:** موجة صغيرة/كبيرة/موزعة؛ entry/exit/stop؛ migration/integration/training/support؛ command center؛ daily reconciliation؛ hypercare؛ rollback؛ lessons؛ capacity before next wave.
- **معايير القبول:** ≥3 دورات رواتب متتالية ناجحة لكل موجة؛ صفر Severity-1 data defect؛ SLO ≥30 يوماً؛ adoption/support thresholds؛ rollback مجرب؛ مجلس مستقل يفتح الموجة التالية.
- **التحقق:** wave scorecard، payroll/data reconciliation، incident/adoption metrics.
- **الاعتماديات:** REL-002, MIG-007, CAP-001, SRE-002, SUP-002.
- **أدلة الإغلاق:**
  - [REL_007_WAVE_ROLLOUT.md](../REL_007_WAVE_ROLLOUT.md)
  - [`docs/rel/wave-entry-exit-criteria.json`](../rel/wave-entry-exit-criteria.json) — pilot/sector/national waves; entry/exit/stop; 30-day hypercare
  - `scripts/rel007-validate.ps1` / `Rel007WaveTests`

## SUP-002 — Service Desk وL1–L3 واتفاقيات SLA/OLA

- **الحالة:** Done (service desk framework + SLA/OLA matrix + knowledge base structure)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Service Management
- **النطاق:** 24x7 critical intake؛ channels/ticketing؛ categorization؛ routing؛ escalation؛ knowledge؛ agency coordinators؛ major incident؛ request fulfillment؛ CSAT؛ trend/problem management.
- **معايير القبول:** SLA ≥95% بعد الاستقرار؛ أكثر 20 مشكلة لها knowledge؛ tickets لا تضيع بين teams؛ FCR/backlog/age/CSAT مقاسة؛ privacy في support attachments.
- **التحقق:** ticket flood، after-hours P1، misroute/escalation، support data audit.
- **الاعتماديات:** REL-003, SRE-001.
- **ما أُغلق:**
  - [SUP_002_SERVICE_DESK.md](../SUP_002_SERVICE_DESK.md)
  - [`sup/sla-ola-matrix.json`](../sup/sla-ola-matrix.json) / [`sup/knowledge-base-structure.md`](../sup/knowledge-base-structure.md)
  - `scripts/sup002-validate.ps1`؛ `Sup002ServiceDeskTests`
- **Blockers المتبقية:** REL-003/SRE-001 = Satisfied؛ live ticket flood drill / live after-hours P1 drill = Blocked

## SUP-003 — الكفاءات ونقل المعرفة واستمرارية الفريق

- **الحالة:** Done (competency matrix with Tier-0 skills and two-deep coverage, knowledge transfer protocol, docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / L / Workforce + Service Owners
- **النطاق:** competency matrix؛ two-deep coverage؛ training/labs؛ certification حيث يلزم؛ shadow/rotation؛ documentation ownership؛ supplier transfer؛ succession؛ exercises.
- **معايير القبول:** بديلان مؤهلان لكل Tier-0 skill؛ لا runbook يعرفه شخص واحد؛ الدعم/DBA/SOC/SRE ينجحون في اختبار عملي؛ onboarding/offboarding معرفة مقاس.
- **التحقق:** practical exams، bus-factor absence exercise، knowledge freshness audit.
- **الاعتماديات:** STR-006, SRE-002.
- **ما أُغلق:**
  - [`SUP_003_COMPETENCY.md`](../SUP_003_COMPETENCY.md)
  - [`sup/competency-matrix.json`](../sup/competency-matrix.json) — Tier-0 skills with two-deep coverage
  - `scripts/sup003-validate.ps1`؛ `Sup003CompetencyTests`
- **Blockers المتبقية:** STR-006/SRE-002 = Satisfied؛ live bus-factor absence exercise / practical exams = Blocked

## SUP-004 — عقود الدعم والتراخيص والتزامات الموردين

- **الحالة:** Done (vendor contract registry + escalation drill + license audit binding)
- **الأولوية/الحجم/المسؤول:** P1-National / M / Procurement + Service Management
- **النطاق:** entitlements؛ 24x7 escalation؛ response/resolution؛ security disclosure؛ EOL؛ audit rights؛ data/support locality؛ subcontractors؛ license compliance؛ penalties؛ exit.
- **معايير القبول:** كل مكون حرج له دعم وتصعيد مجرب؛ صفر license expiry/violation؛ المورد ملزم بالـRTO/security؛ بديل أو قبول مخاطر لكل single supplier.
- **التحقق:** vendor escalation drill، entitlement/license audit، supplier incident tabletop.
- **الاعتماديات:** SOV-007, STR-006.
- **ما أُغلق:**
  - [`sup-contracts/vendor-contract-registry.json`](../sup-contracts/vendor-contract-registry.json) / [SUP_004_ESCALATION_DRILL.md](../SUP_004_ESCALATION_DRILL.md)
  - `scripts/sup004-license-audit.ps1` / `scripts/sup004-validate.ps1`؛ `Sup004ContractTests`
- **Blockers المتبقية:** SOV-007/STR-006 = Satisfied؛ live vendor bridge test = Blocked

## FIN-001 — TCO لخمس سنوات وتكلفة الوحدة

- **الأولوية/الحجم/المسؤول:** P1-National / L / Finance + FinOps
- **النطاق:** build/run/people/hosting/network/storage/backup/DR/security/licenses/support/migration/training/decommission؛ growth scenarios؛ cost per employee/agency/payroll/TB؛ contingency؛ alternatives.
- **معايير القبول:** TCO base/peak/failure scenarios معتمد؛ تكلفة الوحدة تقارن بالبدائل؛ التمويل يغطي التشغيل والتحديث لا الإطلاق فقط؛ لا تكلفة مخفية بلا مالك.
- **التحقق:** independent finance review، sensitivity ±50% demand/cost، vendor quote reconciliation.
- **الاعتماديات:** STR-001, CAP-003, STR-006.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/FIN_001_TCO.md](../FIN_001_TCO.md)
  - [`docs/fin/tco-model.json`](../fin/tco-model.json) — 5-year structured cost model with base/peak/failure scenarios, unit metrics, sensitivity ±50%, alternative comparison
  - [scripts/fin001-validate.ps1](../../scripts/fin001-validate.ps1)
  - `Fin001TcoTests`

## FIN-002 — تخصيص التكلفة والتنبؤ ومنع الانفلات

- **الحالة:** Done (cost allocation rules; 70/85/100% budgets; daily anomaly; forecast guardrails; docs, tests)
- **الأولوية/الحجم/المسؤول:** P2-National / M / FinOps + Platform
- **النطاق:** tagging/allocation؛ budgets؛ anomaly؛ unit metrics؛ forecast؛ idle/waste؛ storage/log retention cost؛ charge/showback؛ optimization guardrails؛ SLO trade-off.
- **معايير القبول:** 100% cost allocated environment/service/agency أو shared rule؛ alerts 70/85/100%؛ anomaly يومي؛ forecast variance <10% أو مفسر؛ لا optimization يكسر SLO/DR.
- **التحقق:** billing reconciliation، anomaly injection، cost-vs-SLO review.
- **الاعتماديات:** FIN-001, OBS-001.
- **أدلة الإغلاق:**
  - [FIN_002_COST_ALLOCATION.md](../FIN_002_COST_ALLOCATION.md)
  - [`docs/fin/cost-allocation-rules.json`](../fin/cost-allocation-rules.json) — tagging, budgets, anomaly, optimization guardrails
  - `scripts/fin002-validate.ps1` / `Fin002CostTests`

## NAT-001 — حزمة أدلة Control-by-Control قابلة للتدقيق

- **الأولوية/الحجم/المسؤول:** National Gate / L / Assurance Office
- **النطاق:** requirements→cards→PRs→tests→deploys→controls→owners→exceptions؛ evidence integrity/expiry؛ version؛ scope؛ assessor access؛ automated collection.
- **معايير القبول:** كل شرط Go/No-Go له دليل حديث ومحدد release/environment؛ لا screenshot بلا مصدر؛ exception مؤقت ومالك؛ الحزمة تعاد من artifact IDs.
- **التحقق:** independent sample إعادة إنتاج 10% على الأقل من الأدلة.
- **الاعتماديات:** SOV-001, REL-005, 05-TRACEABILITY.

## NAT-002 — شهادة الأمن والخصوصية المستقلة

- **الحالة:** Blocked (reopened — دليل اختباري غير سلوكي؛ لا ينفّذ كود المنتج)
- **الأولوية/الحجم/المسؤول:** National Gate / L / Independent Assessor + DPO/CISO
- **النطاق:** control assessment؛ pen/red team؛ privacy impact؛ sovereignty؛ supplier؛ incident؛ remediation؛ residual risk؛ ATO expiry/conditions.
- **معايير القبول:** SOV-006 مغلقة؛ صفر blocker؛ privacy/legal approvals؛ residual risks موقعة من السلطة لا المطور؛ certification محددة الإصدار والنطاق والمدة.
- **التحقق:** assessor-issued report وretest evidence.
- **الاعتماديات:** SOV-001..007, GOV-001, OPS-006.
- **ما أُغلق:**
  - [NAT_002_SECURITY_CERT.md](../NAT_002_SECURITY_CERT.md) + [`nat/nat002-checklist.json`](../nat/nat002-checklist.json)
  - `scripts/nat002-validate.ps1`؛ `Nat002SecurityCertTests`
- **Blockers المتبقية:** live-independent-assessor-report / live-ato-grant = Blocked؛ NAT-001 = NotStarted

## NAT-003 — شهادة الرواتب والخدمة القانونية

- **الحالة:** Blocked (reopened — دليل اختباري غير سلوكي؛ لا ينفّذ كود المنتج)
- **الأولوية/الحجم/المسؤول:** National Gate / L / Finance + Legal + External Audit
- **النطاق:** golden cases؛ boundary/retroactive؛ parallel runs؛ reconciliation؛ approvals؛ payments؛ reversals؛ separation benefits؛ law/config/version evidence.
- **معايير القبول:** ≥3 دورات متتالية بالحجم/التنوع المستهدف؛ صفر فرق غير مفسر؛ كل rule له سند؛ maker/checker مثبت؛ replay تاريخي مطابق.
- **التحقق:** independent recalculation/sample، bank/treasury reconciliation.
- **الاعتماديات:** PAY-001..003, GOV-002, EXT-003, CAP-001.
- **ما أُغلق:**
  - [NAT_003_PAYROLL_CERT.md](../NAT_003_PAYROLL_CERT.md) + [`nat/nat003-checklist.json`](../nat/nat003-checklist.json)
  - `scripts/nat003-validate.ps1`؛ `Nat003PayrollCertTests`
- **Blockers المتبقية:** live-three-cycle-payroll / live-bank-treasury-recon = Blocked

## NAT-004 — شهادة الهجرة وجودة البيانات

- **الحالة:** Blocked (reopened — دليل اختباري غير سلوكي؛ لا ينفّذ كود المنتج)
- **الأولوية/الحجم/المسؤول:** National Gate / L / Internal Audit + Agency Data Owners
- **النطاق:** inventory/mapping/lineage؛ counts/totals/files؛ duplicates؛ quarantine؛ security؛ cutover/rollback؛ legacy archive؛ sign-offs per wave.
- **معايير القبول:** MIG-005/006 evidence؛ صفر record/financial gap حرج؛ 100% accounted؛ rollback مجرب؛ كل جهة توقع جودة بياناتها.
- **التحقق:** independent reconciliation and statistical samples.
- **الاعتماديات:** MIG-001..007.
- **ما أُغلق:**
  - [NAT_004_MIGRATION_CERT.md](../NAT_004_MIGRATION_CERT.md) + [`nat/nat004-checklist.json`](../nat/nat004-checklist.json)
  - `scripts/nat004-validate.ps1`؛ `Nat004MigrationCertTests`
- **Blockers المتبقية:** live-wave-signoffs / live-statistical-sample = Blocked

## NAT-005 — شهادة الوصولية وقابلية الاستخدام والقبول الميداني

- **الحالة:** Blocked (reopened — دليل اختباري غير سلوكي؛ لا ينفّذ كود المنتج)
- **الأولوية/الحجم/المسؤول:** National Gate / L / Independent Accessibility + User Research
- **النطاق:** WCAG 2.2 AA؛ assistive tech؛ Arabic/RTL/languages؛ low bandwidth؛ regional/role diversity؛ task success؛ error recovery؛ training burden؛ satisfaction.
- **معايير القبول:** صفر blocker وصولية؛ الرحلات الحرجة AA؛ معدلات إنجاز وأخطاء وزمن تحقق أهداف STR-007؛ تمثيل الفروع وذوي الإعاقة؛ remediation/retest.
- **التحقق:** automated+manual audit، moderated field studies، network labs.
- **الاعتماديات:** UX-002, PROD-003..005, STR-007.
- **ما أُغلق:**
  - [NAT_005_ACCESSIBILITY_CERT.md](../NAT_005_ACCESSIBILITY_CERT.md) + [`nat/nat005-checklist.json`](../nat/nat005-checklist.json)
  - `scripts/nat005-validate.ps1`؛ `Nat005AccessibilityCertTests`
- **Blockers المتبقية:** live-field-study / live-network-lab = Blocked

## NAT-006 — شهادة الأداء والمرونة والتعافي

- **الحالة:** Blocked (reopened — دليل اختباري غير سلوكي؛ لا ينفّذ كود المنتج)
- **الأولوية/الحجم/المسؤول:** National Gate / L / Independent Performance + SRE Audit
- **النطاق:** workload envelope؛ soak/stress/spike؛ payroll؛ noisy neighbor؛ HA/failover؛ DR/cyber recovery؛ RPO/RTO؛ observability؛ capacity headroom.
- **معايير القبول:** CAP-001 targets جميعها؛ DR/Cyber-RTO؛ zero loss/corruption؛ 30% headroom؛ نتائج قابلة للإعادة؛ لا tuning خاص يخالف production.
- **التحقق:** witnessed tests وraw telemetry/results.
- **الاعتماديات:** CAP-001..003, OPS-002..006, NFR-002.
- **مدخلات NFR-002:** كتالوج أعطال + Toxiproxy harness + game-day calendar + jitter/CB — جاهزة للشهادة دون إعادة بناء سيناريوهات الفوضى.
- **ما أُغلق:**
  - [NAT_006_PERFORMANCE_CERT.md](../NAT_006_PERFORMANCE_CERT.md) + [`nat/nat006-checklist.json`](../nat/nat006-checklist.json)
  - `scripts/nat006-validate.ps1`؛ `Nat006PerformanceCertTests`
- **Blockers المتبقية:** live-witnessed-performance-run / live-dr-cyber-rto-drill = Blocked

## NAT-007 — شهادة الموجة التجريبية واستعداد التشغيل

- **الحالة:** Blocked (reopened — دليل اختباري غير سلوكي؛ لا ينفّذ كود المنتج)
- **الأولوية/الحجم/المسؤول:** National Gate / L / Pilot Board + Service Management
- **النطاق:** agency UAT؛ adoption؛ support؛ SLO؛ incidents؛ payroll/data؛ integrations؛ training؛ rollback؛ lessons؛ ORR؛ staffing؛ vendor readiness.
- **معايير القبول:** REL-002 وREL-003 مكتملتان؛ ≥3 دورات ناجحة؛ ≥30 يوماً SLO؛ لا Severity-1؛ support SLA؛ كل lesson حرج مغلق قبل التوسع.
- **التحقق:** pilot scorecard، user/agency sign-offs، operational drill.
- **الاعتماديات:** REL-002, SRE-001..003, SUP-002..004, MIG-007.
- **ما أُغلق:**
  - [NAT_007_PILOT_CERT.md](../NAT_007_PILOT_CERT.md) + [`nat/nat007-checklist.json`](../nat/nat007-checklist.json)
  - `scripts/nat007-validate.ps1`؛ `Nat007PilotCertTests`
- **Blockers المتبقية:** live-pilot-scorecard / live-operational-drill = Blocked

## NAT-008 — قرار Go/No-Go الوطني وتجديد الاعتماد

- **الأولوية/الحجم/المسؤول:** National Gate / M / National Authorization Board
- **النطاق:** مراجعة NAT-001..007؛ open risks؛ funding؛ sovereignty؛ legal؛ capacity؛ wave plan؛ stop/rollback؛ public accountability؛ certification expiry؛ continuous monitoring.
- **معايير القبول:** لا P0/P1 أو Severity-1/2 مفتوحة؛ جميع سلطات الأعمال والمالية والقانون والخصوصية والأمن والتشغيل والتدقيق توقع؛ scope/release/مدة واضحة؛ إعادة اعتماد سنوية وبعد تغيير جوهري.
- **التحقق:** minutes وsigned decision، conditions tracked، continuous control monitoring.
- **الاعتماديات:** NAT-001..007, REL-007, FIN-001.

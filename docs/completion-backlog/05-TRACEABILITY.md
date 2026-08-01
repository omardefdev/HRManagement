# مصفوفة التتبع والتغطية

تربط هذه المصفوفة البطاقات بأدلة من الشيفرة الحالية وبأبعاد جاهزية المشروع. الغرض أن لا تتحول البطاقات إلى رغبات عامة منفصلة عن الواقع.

## الأدلة الحالية إلى البطاقات

| الدليل في المشروع | الخطر/الفجوة | البطاقات المالكة |
|---|---|---|
| `HR.Core.Web/Controllers/LookupApiController.cs:12` | Lookup محمي (SEC-002 Done)؛ بقي تطبيع عربي = L10N | SEC-002 ✓, L10N-002 |
| `FilesApiController` download/delete/refs | قراءة/حذف — دورة حياة FILE-001 + فحص FILE-002 Done؛ تفويض كائني يبقى SEC-003 | SEC-003 |
| `FilePlatformService` RegisterReference / Scan | عدّاد ذري + PendingPurge + ScanStatus/quarantine (DATA-001/FILE-001/FILE-002 Done) | DATA-001, FILE-001, FILE-002 |
| `docker-compose.production.yml:321` | MinIO ومنافذ إدارة مكشوفة وافتراضات أسرار | SEC-001, SEC-004, OPS-001 |
| `.dockerignore:1` وDockerfiles `COPY . .` | أسرار تدخل build context — `.dockerignore` + Gitleaks (SUP-001 Done)؛ تعزيز SEC-004 | SEC-004, SUP-001 ✓ |
| `ServiceDefaults/KeycloakExtensions.cs:83` | تعطيل JWT audience validation | IAM-001 |
| `SharedKernel/Entities/BaseEntity.cs` | TenantId إلزامي مع EF filters + RLS (TEN-002) | TEN-001, TEN-002, TEN-003 |
| `HrOrgScopeVisibility.cs:18` | نطاق مفقود قد يفشل إلى وصول مفتوح | IAM-002 |
| `TransferRequestsController` AND scope + Cancel | IDOR مصدر/هدف مغلق (FUN-003 Done) | IAM-002 ✓, IAM-003 ✓, FUN-003 ✓ |
| `SeparationRecordHandlers` + process sagas | DOM-001 Done؛ INT-003 process managers | DOM-001 ✓, INT-003 ✓ |
| `PayrollExtendedControllers.cs:118` | إنشاء دفعة خارج مسار الاعتماد | PAY-001, IAM-003 |
| `PayrollRunsController.cs:89` | تعديل سطور تشغيل راتب خارج state machine | PAY-001, DATA-001 |
| `PayrollRunHandlers` / Job handlers + `PayrollRunParallelCalculator` | bulk/parallel batch (PERF-001 Done)؛ أساس PAY-003 ✓ | PAY-003 ✓, PERF-001 ✓ |
| `SearchFilterBase` + `ExportQueryLimits` + Gateway audit cursor | pagination/export caps (PERF-002 Done)؛ jobs الثقيلة = PERF-004 | PERF-002 ✓, PERF-004 |
| `OrgUnit.MaterializedPath` + `OrgScopeService` / `OrgUnitPathService` | subtree path + cycle/move + tenant cache (PERF-003 Done) | PERF-003 ✓, TEN-003 |
| `ExportJob` + `IExportJobRequested` + artifact store | background exports (PERF-004 Done)؛ sync caps = PERF-002 | PERF-004 ✓, PERF-002 ✓, FILE-001 |
| `deploy/k8s/` Kustomize + ADR/OPS_HA_* | HA multi-AZ apps (OPS-001 Done)؛ data HA = OPS-002 | OPS-001 ✓, OPS-002, DEP-001 ✓ |
| `deploy/k8s/data/` CNPG+Rabbit+KC+MinIO + MassTransit quorum | data-plane HA (OPS-002 Done)؛ PITR = OPS-003 | OPS-002 ✓, OPS-003 |
| `backup-postgres.sh` + CNPG ScheduledBackup + catalog/restore drills | backup/PITR (OPS-003 Done)؛ DR site = OPS-004 | OPS-003 ✓, OPS-004 |
| `OPS_DR_*` + `dr-secondary` overlays + `dr-*-drill.sh` | site DR warm standby (OPS-004 Done)؛ IaC site = OPS-005 ✓ | OPS-004 ✓, OPS-005 ✓ |
| `terraform/` + ExternalSecrets + `policy/iac` + `iac-*.sh` | IaC platform (OPS-005 Done)؛ REL-001 يستهلك staging ✓ | OPS-005 ✓, REL-001 ✓ |
| `PayrollRun` + `PayrollConfiguration` version/hash pin | نسخة قانونية immutable مربوطة بالتشغيل (PAY-002 Done) | PAY-002 ✓, GOV-002 ✓ |
| `LegalDecisionRecord` + `LegalAttestation` + golden pack | سجل قرارات قانونية + تصديق قبل Publish (GOV-002 Done) | GOV-002 ✓ |
| `MassTransitExtensions` + `InboxConsumeFilter` + Outbox | Inbox/Outbox ذريان (INT-001/002 Done) | INT-001 ✓, INT-002 ✓ |
| `EmployeesController.Delete` | hard delete مرفوض (DATA-002 Done) — استخدم فصل/تصحيح/anonymize | DATA-002 ✓, GOV-001 ✓ |
| `PiiMask` + `DsarRequest`/`api/dsar` + retention dry-run + non-prod gate | تصنيف/خصوصية/احتفاظ/DSAR (GOV-001 Done) | GOV-001 ✓ |
| `GatewayAuditQueue` + hash chain + overflow/DLQ | متانة تدقيق + سلامة سلسلة (AUD-001/OBS-003 Done) | AUD-001 ✓, OBS-003 ✓ |
| `.github/workflows/deploy.yml` + `docker-compose.images.yml` + DEP gate | immutable SHA deploy + rollback (DEP-001 Done)؛ rehearsal = REL-001 ✓ | DEP-001 ✓, REL-001 ✓ |
| `scripts/backup-postgres.sh` + catalog/PITR/restore drills | نسخ كامل + PITR (OPS-003 Done)؛ DR = OPS-004 | OPS-003 ✓, OPS-004 ✓ |
| `scripts/check-coverage.py` + `coverage-floors.json` | بوابة تغطية مخاطر: 60% على gateAssemblies + floors + ratchet (QA-001/002 Done) | QA-001 ✓, QA-002 ✓ |
| `TestFixtures.cs` + `Qa003*RealInfra*` + `ci.yml` Testcontainers | تكامل حقيقي PG/Rabbit/Keycloak/MinIO إلزامي (QA-003 Done) | QA-003 ✓ |
| `HRManagement.E2E` journeys + `e2e-journeys.yml` + `testdata/e2e` | رحلات أعمال/صلاحيات + nightly/pre-release (QA-004 Done)؛ smoke بوسم `e2e` | QA-004 ✓ |
| `GatewayE2ETests.cs` | smoke بوابة؛ Skip عند إطفاء العلم (لا Pass صامت) | QA-001 ✓, QA-004 ✓ |
| `Directory.Packages.props` + `packages.lock.json` + `dotnet tool restore` | CPM/locks/dotnet-ef مثبت (DB-001 Done)؛ مرآة سيادية تبقى SAN-001 | DB-001 ✓, SAN-001 |
| `deploy.yml` SBOM+Cosign+Trivy + `supply-chain-exceptions.json` + Dependabot | أمن توريد/SBOM/توقيع (SUP-001 Done)؛ مرآة سيادية = SAN-001؛ admission = BLD-001 | SUP-001 ✓, SAN-001, BLD-001 |
| `entity-pickers.js` + `UseHrSecurityHeaders` + `wwwroot/lib` + a11y chrome | XSS/CSP (UX-001 ✓)؛ WCAG/جداول/axe (UX-002 ✓) | UX-001 ✓, UX-002 ✓ |
| `prometheus.yml:13` و`alertmanager.yml:8` | مراقبة جزئية دون مستقبل تنبيه فعلي | OBS-001 ✓ (stack/dashboards)؛ مستقبل تنبيه = OBS-002 |
| `prometheus.yml` + `slo-burn-alerts.yml` + `alert-sink` + blackbox | SLO/burn + قناة تنبيه (OBS-002 Done)؛ SIEM rules lite + WORM (OBS-003 Done) | OBS-002 ✓, OBS-003 ✓ |
| `docker-compose.observability.yml` + Tempo/Grafana + ServiceDefaults OTel | telemetry كاملة (OBS-001 Done)؛ SLO/burn = OBS-002 ✓ | OBS-001 ✓, OBS-002 ✓ |
| `Dev.Analytics` consumers/projections | OrgUnitId + rebuild + branch RLS (FUN-006 ✓)؛ lineage = GOV-003 ✓ | GOV-003 ✓, FUN-006 ✓ |
| `master-data/field-ownership.json` + merge/unmerge + DQ report | حوكمة بيانات رئيسية (GOV-003 Done) | GOV-003 ✓ |
| `HR.Core.Domain/Entities/ApprovalStep.cs:7` | Workflow ثابت وفقير في الإصدار والتفويض وSLA | PROD-001, WF-001, WF-002 |
| `HR.Core.Web/Controllers/EssPortalController.cs:101` | ESS يرسل حدثاً بلا Case دائم أو تتبع فشل لاحق | ESS-001, WF-001, COM-001 |
| `ServiceDefaults/NotificationHub.cs:7` | بث SignalR إلى `all` بلا تخزين أو مستلمين أو إيصال | COM-001 لاحقاً (PROD-005 workbox binding Done — لا يغلق COM-001) |
| `HR.Core.Web/Controllers/DataImportController.cs:34` | CSV بدائي يتجاوز الأخطاء ويعالج صفاً صفاً | MIG-001..006, MDM-002 |
| `Integration.FileAdapters/ExternalIntegrationServiceCollectionExtensions.cs:21` | وضع Live يقع عملياً على File adapters | INT-004, EXT-001..007 |
| `Dev.Analytics.Web/Controllers/WorkforcePlanningController.cs:21` | headcount تقريبي وبحث نصي داخل JSON | ANA-001 لاحقاً, REP-001 لاحقاً؛ PROD-006 ✓ (production semantic-layer binding — لا يغلق ANA-001) |
| `HireCaseStateMachine` + Auth.Directory party + checklist evidence | إغلاق رحلة التعيين (FUN-001 Done) | FUN-001 ✓ |
| `SeparationStateMachine` + soft-cancel + party Compensate | إغلاق رحلة الفصل (FUN-002 Done) | FUN-002 ✓ |
| `HR.Core.Domain/Entities/EmploymentRecord.cs:16` | الوظيفة والمركز نصوص لا ملاك رئيسياً معتمداً | MDM-001, ORG-001, EST-001 |
| `Web.Shared/Views/Shared/_HrSharedLayout.cshtml:3` | العربية/RTL مثبتة بلا بنية localization كاملة | L10N-001, MOB-001, NAT-005 |
| `HR.Contracts.Web/Controllers/ContractSignaturesController.cs:30` | توقيع مدخل من النموذج قابل للحذف وليس دليلاً قانونياً | SOV-003, SIG-001, DOC-001, LEG-001 |
| `global.json` — SDK pin + `rollForward: latestPatch` (DB-001 Done) | تثبيت SDK؛ مرآة/عقوبات تبقى SAN-001 | DB-001 ✓, SAN-001 |
| غياب `NuGet.config` أو أي مصدر حزم داخلي | لا مرآة سيادية؛ البناء يتوقف عند حجب أو تقييد المصدر العام | SAN-001, BLD-001 |
| `docker-compose.observability.yml:6,21,31,46,57,72` | ست صور من سجلات عامة دون mirror أو digest مثبت | SAN-001, BLD-001 |
| لا أثر لمتطلبات موقع أو طاقة أو اتصال في IaC وملفات compose | HA على مستوى الحاويات فوق موقع أحادي الطاقة والاتصال | PHY-001, OPS-001, ARC-001 |
| ظهور الاحتيال في `SOV-005`/`SOV-006`/`MIG-004` فقط | لا ضابط احتيال مستمر بعد الإطلاق ولا إثبات قيد حياة | FRD-001, MDM-002, SYR-006 |
| `Payroll.Domain/Entities/PaymentBatch.cs:17` | `TotalAmount` فقط: لا صرف جزئي ولا متأخرات ولا حالة نهائية للسطر | PAY-004, SYR-011 |
| `HR.Core.Domain/Entities/Employee.cs:17,30` | أسماء عربية نصاً خاماً بلا عمود مطبّع أو قيد على المطبّع | L10N-002, MDM-002 |
| `HR.Core.Web/Controllers/LookupApiController.cs:35` | `Contains(term)` على نص عربي خام: «أحمد» ≠ «احمد» في البحث والمطابقة | L10N-002, MIG-004, SEC-002 |
| 67 بطاقة `XL` غير مقسّمة في الملفات 01–13 مع منع الـREADME تنفيذها | الحزمة غير قابلة للتنفيذ بمعاييرها الخاصة ولا مسار حرج | PGM-001, STR-005 |
| `system decommission` مذكور مرة واحدة داخل نطاق `LEG-001` | لا مالك لإخراج القديم من الخدمة؛ خطر المسار الموازي | DEC-001, MIG-006, SYR-012 |
| `NAT-001` يطلب أدلة قابلة لإعادة الإنتاج دون بطاقة تبني المخزن | حزمة الاعتماد بلا نظام أدلة غير قابل للتعديل | EVD-003, NAT-001 |

## تغطية جوانب المشروع

| البعد | البطاقات الأساسية | دليل الإغلاق النهائي |
|---|---|---|
| أمن الشبكة والأسطح | SEC-001, SEC-004, OPS-001 | external attack-surface scan |
| الهوية والصلاحيات | IAM-001..004 | authorization matrix + penetration test |
| تعدد الجهات والفروع | TEN-001..003, IAM-002 | cross-tenant negative suite + RLS proof |
| خصوصية البيانات | SEC-002 ✓, GOV-001 ✓, OBS-003 | Lookup محمي؛ DPIA/retention/DSAR = GOV-001 ✓ |
| الملفات والمرفقات | SEC-003, FILE-001..002 | BOLA + malware + lifecycle tests |
| قواعد الخدمة | DOM-001, DATA-002, FUN-001 ✓, FUN-002 ✓ ..003 | signed UAT packs (ReadyForSign-off) + state-machine tests |
| الرواتب | PAY-001..003, PERF-001, GOV-002 ✓ | golden cases + replay + parallel run |
| الرسائل والاتساق | INT-001..003 | failure/duplicate (INT) + reorder projection (NFR-002 `Nfr002OutOfOrderTests`) + chaos harness |
| قواعد البيانات | DATA-001, DB-001, OPS-002..003 | constraints + migration + restore evidence؛ OPS-002 HA + OPS-003 backup/PITR Done |
| الأداء والحجم | PERF-001..004, NFR-001 | NFR-001 Done: نموذج+k6+seed+خطة 24م+bottlenecks؛ national full envelope → CAP-001؛ PERF-002 inventory+caps Done |
| الاعتمادية والكوارث | OPS-001..005, NFR-002 | OPS-001..005 Done؛ NFR-002 chaos/Toxiproxy/game-days Done؛ failover/DR ضمن RTO/RPO |
| التدقيق والمراقبة | AUD-001, OBS-001..003 | OBS-001 telemetry Done؛ OBS-002 SLO/alerts Done؛ OBS-003 SIEM/hash/WORM Done؛ AUD-001 Done |
| CI/CD والتوريد | QA-001, DEP-001, DB-001, SUP-001 | reproducible signed release؛ SUP-001 ✓ |
| الاختبارات | QA-001..004 | QA-001..004 Done: risk matrix + 60% gate + real infra TC + E2E journeys (nightly/pre-release) |
| الواجهة والوصولية | UX-001 ✓, UX-002 ✓ | CSP/XSS + WCAG 2.2 AA أساسي؛ شهادة = NAT-005 |
| الحوكمة والقانون | GOV-001 ✓, GOV-002 ✓, GOV-003 ✓ | classification + legal register + SoR/DQ/lineage Done؛ MDM وطني = MDM-001 |
| اكتمال الوحدات | FUN-001 ✓, FUN-002 ✓, FUN-003 ✓, FUN-004 ✓, FUN-005 ✓, FUN-006 ✓ | hire→identity + archive/BI closed؛ UAT/REL لاحقاً |
| الإطلاق والدعم | REL-001 ✓, REL-002 ✓ (gate pack), REL-003 ✓ | staging + Pilot gate pack + Ops SM baseline؛ وطني 24×7/desk = SRE/SUP/NAT |
| تعريف المنتج والمنافسة | STR-001 ✓ (production scope binding), STR-002 ✓ (production control binding), STR-003 ✓ (production module binding), STR-004 ✓ (production nofork binding), STR-005 ✓ (production governance binding), STR-006 ✓ (production ownership binding), STR-007 ✓ (production value binding), PRD-001 | مغلف وطني مربوط بالتشغيل؛ كتالوج ضوابط مربوط بالإقلاع/الرواتب/CI؛ كتالوج وحدات مربوط بالبوابة/المسارات؛ no-fork مربوط بحزم جهات بنفس digest؛ حوكمة برنامج مربوطة بالإقلاع/CI؛ ملكية/تمويل/موردون مربوطون بالإقلاع/CI؛ مؤشرات قيمة وخط أساس مربوطان بالإقلاع/CI؛ سلسلة STR مكتملة حتى خط الأساس؛ PRD-001 لاحقاً؛ PROD-001 ✓ (production policy-platform binding) — محرك حي لاحقاً عبر PROD-001a–e؛ PROD-002 ✓ (production agency-selfadmin binding) — UI حي لاحقاً عبر PROD-002a–e؛ PROD-003 ✓ (production employee-selfservice binding) — بوابات ESS/MSS حية لاحقاً عبر PROD-003a–e؛ PROD-004 ✓ (production remote-branch binding) — أسطول PWA/offline حي لاحقاً عبر PROD-004a–e؛ PROD-005 ✓ (production workbox binding) — inbox/إشعارات حية لاحقاً عبر PROD-005a–e؛ PROD-006 ✓ (production semantic-layer binding) — warehouse/BI حي لاحقاً عبر PROD-006a–e؛ `LiveDashboardClaim` ممنوع حتى PROD-006c؛ PROD-007 ✓ (production extensibility-platform binding) — runtime/SDK/webhooks حية لاحقاً عبر PROD-007a–e؛ ANA-001/REP-001 لاحقاً؛ EXT-006/IOP-001/REL-004 لاحقاً؛ COM-001/WF-002/NAT-005 لاحقاً؛ L10N-001/MOB-001/SYR-013 لاحقاً؛ IAM-002 يبقى مفتوحاً؛ ESS-001/MSS-001 لاحقاً |
| هجرة الأنظمة القديمة | MIG-001 ✓ (production inventory binding), MIG-002 ✓ (production codemap binding), MIG-003 ✓ (production factory binding), MIG-004 ✓ (production match binding), MIG-005 ✓ (production evidence binding), MIG-006 ✓ (production cutover binding), MIG-007 ✓ (production onboarding binding) | MIG-001..007 مربوطان بالإقلاع/CI؛ التنفيذ الحي لاحقاً عبر MIG-003a–d / MIG-004a–d / MIG-005a–d / MIG-006a–d / MIG-007a–d |
| التكامل الحكومي | EXT-001 ✓ (production federation binding), EXT-002 ✓ (production civil-registry binding), EXT-003 ✓ (production treasury-payment binding), EXT-004 ✓ (production pki-signature binding), EXT-005 ✓ (production api-exchange binding), EXT-006 ✓ (production sandbox-cert binding), EXT-007 ✓ (production sector-catalog binding), INT-004, IOP-001 | EXT-001..007 مربوطان بالإقلاع/CI؛ MOI/IdP حي لاحقاً عبر EXT-001a–d / EXT-002a–d؛ خزينة حية لاحقاً عبر EXT-003a–e؛ CA/HSM/TSA لاحقاً عبر EXT-004a–f؛ bus/registry حي لاحقاً عبر EXT-005a–e؛ بوابة مطورين حية لاحقاً عبر EXT-006a–e؛ adapters قطاعية حية لاحقاً عبر EXT-007a–e؛ INT-004 / IOP-001 لاحقاً |
| القدرات الوطنية | MDM-001..002, ORG-001, EST-001, HCM-001..006 | service-journey UAT and statutory evidence |
| الخدمة الذاتية والتبنّي | ESS-001, MSS-001, COM-001, UXR-001, TRN-001 | measured task success/adoption and field acceptance |
| السيادة والاستقلال | SOV-001 ✓ (production control-baseline binding), SOV-002 ✓ (production data-sovereignty binding), SOV-003 ✓ (production key-sovereignty binding), SOV-004 ✓ (production zero-trust binding), SOV-005 ✓ (production threat-modeling binding), SOV-006 ✓ (production pen-red binding), SOV-007 ✓ (production vendor-exit binding), VEN-001 | خط أساس مربوط بالإقلاع/CI مع `LiveAtoClaim=false`؛ سيادة بيانات مربوطة مع `LiveSovereigntyClaim=false`؛ سيادة مفاتيح مربوطة مع `LiveKeyCustodyClaim=false`؛ Zero Trust مربوط مع `LivePamClaim=false`؛ Threat modeling مربوط مع `LiveThreatModelClaim=false`؛ Pen/red مربوط مع `LivePenClaim=false`؛ Vendor-exit مربوط مع `LiveVendorClaim=false`؛ ATO حي = SOV-001a؛ egress/JIT/محو حي = SOV-002a–c؛ HSM حي = SOV-003a؛ PAM حي = SOV-004a؛ مراجعة تهديد حية = SOV-005a؛ اختراق حي = SOV-006a؛ clean-room/exit drill حي = SOV-007a–b / VEN-001؛ SEC-004/NAT-002/FRD-001 لاحقاً؛ key/exit/rebuild drills |
| التشغيل الوطني | ARC-001 ✓ (production criticality binding), ARC-002 ✓ (production reference-architecture binding), CAP-001..003, SRE-001..003, OPS-006..007 | تصنيف حرجية مربوط بالإقلاع/CI مع `LiveCriticalityClaim=false`؛ معمارية مرجعية مربوطة مع `LiveReferenceClaim=false`؛ walkthrough/shutdown/recovery حي = ARC-001a–c؛ نشر موقعين/fitness حي = ARC-002a–b؛ 24x7 ORR + national envelope + cyber recovery |
| الخصوصية والإثبات | PRV-001..003, EVD-001..002, LEG-001, SIG-001 | DPIA/ROPA + evidence reconstruction + legal validation |
| الاعتماد الوطني | NAT-001..008 | independent signed National Go/No-Go for exact digest |
| التشريع والرواتب السورية | SYR-001, SYR-003..006, SYR-014..015 | نصوص مصدقة + rule packs + golden payroll + سجل قرار قابل لإعادة البناء |
| الهوية والتكامل السوري | SYR-009..012 | عقود الجهات + conformance + replay/reconciliation + provenance للهجرة |
| السيادة والخصوصية السورية | SYR-007..008, SYR-016 | ترخيص/قرار الهيئة + اعتماد الاستضافة والمفاتيح + تقييم سيبراني مستقل |
| الانتشار والاعتماد السوري | SYR-002, SYR-013, SYR-017 | تعداد مصدق + UAT المحافظات + ثلاث دورات ظل + قرار موجة محدود |
| سلسلة التوريد والقيود القانونية | SAN-001, SUP-001 ✓, BLD-001 | air-gapped build + سجل تبعيات قانوني؛ SBOM/توقيع GHCR مكتمل (SUP-001) |
| البنية المادية والطاقة | PHY-001, OPS-001..004, BCP-001 | قطع تيار واتصال مقاس + مدة استقلال محققة فعلياً |
| مكافحة الاحتيال والهدر | FRD-001, MDM-002, MIG-004 | ثلاث دورات + تقرير شذوذ مستقل + حقن حالات وهمية |
| التمويل والصرف الجزئي | PAY-004, EXT-003, SYR-011 | مصالحة «المصروف + المتأخر = المستحق» + منع الدفع المزدوج |
| إدارة البرنامج والجدول | PGM-001, STR-005 ✓ (production governance binding), STR-006 ✓ (production ownership binding), FIN-001 | مسار حرج بخط أساس + صفر بطاقة XL غير مقسّمة؛ حوكمة وملكية Done؛ TCO كامل = FIN-001 |
| إخراج الأنظمة القديمة | DEC-001, MIG-005..006, LEG-001 | قديم read-only + استرجاع أرشيف + صفر عملية ظل |
| قابلية الصيانة والدين التقني | QA-005, ARC-002, SCM-001 | خط أساس مفروض في CI + رفض الدمج المُدهور |
| التمثيل النقابي والمؤسسي | TRN-002, TRN-001, PRV-001 | إشراك موثق لكل موجة + رد مكتوب على كل تحفظ |
| النص العربي والمطابقة | L10N-002, MDM-002, L10N-001 | corpus أسماء مرجعي + قيود فريدة على المطبّع |
| مستودع الأدلة | EVD-003, NAT-001, EVD-001 | إعادة إنتاج مستقلة ≥10% + استحالة التعديل |

## تغطية الخدمات الوظيفية

| الخدمة | بطاقات الاكتمال | بطاقات مشتركة لا يجوز تجاوزها |
|---|---|---|
| HRCore | FUN-001 ✓, FUN-002 ✓, GOV-003 ✓ | TEN-002, DATA-002, IAM-002, PERF-003 |
| HRContracts | FUN-001 ✓, FUN-002 ✓ | DOM-001, INT-003, GOV-002 |
| Payroll | FUN-001 ✓, FUN-002 ✓, FUN-003 ✓ | PAY-001..003, PERF-001, IAM-003 |
| HRMovements | FUN-003 ✓ | IAM-002, INT-003, DATA-001 |
| HRHealth | FUN-004 ✓ | GOV-001, FILE-002, TEN-002 |
| HRTraining | FUN-004 ✓ | TEN-002, QA-004 |
| Compliance | FUN-004 ✓ | GOV-001, IAM-003, OBS-003 |
| IDCard | FUN-005 ✓ | INT-003, FILE-002, IAM-004 |
| AdminLogistics | FUN-005 ✓ | DATA-001, INT-003 |
| ITInfrastructure | FUN-005 ✓ | IAM-004, INT-003 |
| AuthDirectory | FUN-001 ✓, FUN-002 ✓, FUN-005 ✓ | IAM-001, IAM-003, IAM-004 |
| RecordsArchive | FUN-006 ✓ | GOV-001, FILE-001, OBS-003 |
| DevAnalytics | FUN-006 ✓ | GOV-003, TEN-003, PERF-004 |
| FileStorage | جميع الرحلات | SEC-003, FILE-001..002, OPS-003 |
| Gateway | جميع الرحلات | SEC-001, IAM-001..002, AUD-001 |

## قالب بطاقة إضافية

يستخدم هذا القالب فقط لفجوة جديدة لا تغطيها البطاقات الحالية:

```markdown
## ABC-000 — عنوان نتيجة قابلة للقياس

- **الأولوية/الحجم/المسؤول:** P? / S|M|L / Role
- **السبب:** ما الضرر أو الحاجة؟ اربطه بدليل أو متطلب.
- **النطاق:** تغييرات محددة، وحدود ما لن يتغير.
- **معايير القبول:**
  - [ ] شرط قابل للإثبات بنعم/لا.
  - [ ] حالة فشل أو صلاحية أو تعافٍ، لا happy path فقط.
- **التحقق:** اختبارات أو قياسات أو تمرين تشغيلي محدد.
- **الاعتماديات:** معرفات بطاقات، أو لا يوجد.
```

## قواعد سلامة التتبع

- لا تُغلق بطاقة `FUN` إذا بقيت بطاقة مشتركة مرتبطة بها مفتوحة.
- لا يُعد نجاح unit tests إثباتاً لبطاقة تتعلق بـPostgreSQL أو RabbitMQ أو Keycloak أو MinIO.
- لا تعتمد وثيقة قانونية وحدها لإغلاق GOV-002؛ يجب ربطها بإعداد immutable واختبارات ذهبية. (GOV-002 Done: register + LegalAttest + golden pack)
- لا يعتمد تقرير اختراق قبل إغلاق النتائج الحرجة والعالية أو قبولها زمنياً من صاحب المخاطر.
- عند تغيير عقد حدث أو schema، تُحدّث البطاقة ومصفوفة التتبع واختبارات التوافق معاً.
- لا تُغلق بطاقة تعتمد على مكون خارجي قبل إثبات توافره تحت قيد التوريد في `SAN-001`.
- لا يُقبل دليل HA أو DR على مستوى الحاويات إذا كان الموقع أحادي الطاقة أو الاتصال في `PHY-001`.
- لا تُغلق بطاقة مطابقة أو إزالة تكرار على أسماء عربية قبل إغلاق `L10N-002`؛ نجاح البحث على نص خام ليس دليلاً.
- لا يُقبل دليل في أي بوابة `NAT` أو `SYR-017` إلا من مستودع `EVD-003` بمعرّف قابل لإعادة الإنتاج.

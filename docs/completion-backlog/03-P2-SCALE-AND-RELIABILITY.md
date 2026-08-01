# P2 — بطاقات التوسع والاعتمادية

بوابة هذه المرحلة: المنصة تتحمل الحجم المستهدف والأعطال المتوقعة دون فقد بيانات أو توقف يتجاوز SLO، ويمكن استعادتها ضمن RPO/RTO مثبتين بالاختبار.

## PERF-001 — Batch payroll متوازٍ ومحدود الموارد

- **الأولوية/الحجم/المسؤول:** P2 / XL / Payroll + Performance
- **النطاق:** bulk read بدلاً من N+1؛ partition keys؛ worker pool bounded؛ bulk writes؛ snapshot inputs؛ backpressure؛ progress/ETA؛ منع تشغيلين متداخلين؛ قياسات CPU/IO/DB locks.
- **معايير القبول:** حجم السعة المعتمد يكتمل ضمن نافذة الرواتب؛ الذاكرة والاستعلامات لا تنموان خطياً بلا حد؛ النتائج مطابقة للمحرك المرجعي؛ إعادة التوزيع لا تكرر الأثر.
- **التحقق:** benchmark على أحجام 10k/100k/الحجم الوطني، query counts، وsoak كامل.
- **الاعتماديات:** PAY-003.
- **الحالة:** Done (قصص A–D)
- **أدلة الإغلاق:**
  - `PayrollPartitionInputLoader` + `InputFingerprint` على `PayrollRunInputSnapshot`؛ migration `PERF001_ParallelPayrollBatch`
  - `PayrollRunParallelCalculator` (Channel + DOP + claim + bulk write) + `PayrollBatchOptions`
  - تقدّم/ETA على الـjob وواجهة `PayrollRuns/Details`؛ meter `hrm.payroll.run`
  - `docs/PAYROLL_BATCH_PERFORMANCE.md` + `Perf001PayrollBatchTests`
  - الحجم الوطني الكامل يبقى تحت CAP/SYR/NFR للبوابة الوطنية

## PERF-002 — Pagination وحدود الاستعلام والتصدير

- **الأولوية/الحجم/المسؤول:** P2 / L / جميع الخدمات
- **النطاق:** inventory لكل `ToList`/export؛ server-side pagination بحد أقصى؛ stable cursor للبيانات الكبيرة؛ فهارس وفق query plans؛ filters إلزامية؛ timeout/cancellation؛ exports كjobs؛ حماية expensive queries.
- **معايير القبول:** لا endpoint عام يعيد مجموعة غير محدودة؛ pagination مستقرة دون تكرار/فقد؛ p95 ضمن SLO على بيانات ممثلة؛ خطة الفهرسة موثقة.
- **التحقق:** automated route/query inventory، EXPLAIN ANALYZE، performance regression tests.
- **الاعتماديات:** DATA-001.
- **الحالة:** Done (قصص A–D)
- **أدلة الإغلاق:**
  - `SearchFilterBase` (`MaxPageSize=100` / `MaxExportRows=10000` / `NormalizePaging`) + `CursorPaging` + `ToPaginatedListAsync` / `ExportQueryLimits`
  - موجة 1: HR.Core + Payroll قوائم/تصدير؛ Gateway audit نطاق إلزامي (رفض `all`) + keyset `(OccurredAtUtc, Id)` + migration فهرس مركّب
  - موجة 2: Contracts/Compliance/Movements/Health/Training/IDCard/Archive/Admin/Auth/Analytics/IT — Index/Export بحدود
  - `scripts/inventory-unbounded-queries.ps1` → `docs/QUERY_EXPORT_INVENTORY.md` (صفر unbounded على ToListAsync)
  - `docs/QUERY_INDEX_PLAN.md` + `Perf002PagingTests` / تحديثات `GatewayAuditQueryServiceTests`
  - التصدير الثقيل كـjob خلفي = **PERF-004** (PERF-002 يرفض المتزامن فوق السقف برسالة عربية)

## PERF-003 — تحسين واستدامة شجرة المؤسسة

- **الأولوية/الحجم/المسؤول:** P2 / L / HRCore + Data
- **النطاق:** materialized path/closure table أو بديل معتمد؛ استعلام subtree مباشر؛ tenant-scoped cache مع invalidation؛ cycle prevention؛ نقل subtree ذري؛ عدم تحميل كل الوحدات لكل طلب.
- **معايير القبول:** lookup وscope يعملان O(log n) أو استعلاماً محدوداً عملياً؛ لا تسرب cache بين tenants؛ منع الدورات؛ تحديث الهيكل ينعكس بمدة محددة.
- **التحقق:** أشجار عميقة وعريضة، concurrent move، cache invalidation، وquery budget.
- **الاعتماديات:** TEN-003.
- **الحالة:** Done (قصص A–D)
- **أدلة الإغلاق:**
  - `OrgUnitPathService`: cycle checks، `AssignPathOnCreateAsync`، `MoveSubtreeAsync`؛ `RecomputeAll` للإصلاح فقط
  - `OrgScopeService` / `OrgScopeResolver` عبر `MaterializedPath` prefix + TenantId (بدون BFS كامل)
  - `IOrgUnitHierarchyCache` tenant-scoped + إبطال عند الكتابة؛ Tree/Lookup بحد 500 عبر المسار
  - فهارس path على OrgUnits + OrgUnitSnapshots (Payroll/Contracts/Movements)
  - `docs/ORG_UNIT_HIERARCHY.md` + `Perf003OrgUnitHierarchyTests`

## PERF-004 — وظائف تقارير وتصدير خلفية

- **الأولوية/الحجم/المسؤول:** P2 / L / Reporting + FileStorage
- **النطاق:** queue للتقارير الثقيلة وZIP/PDF؛ streaming؛ chunking؛ progress/cancel/expiry؛ authorization snapshot وإعادة تحقق عند download؛ limits؛ templates versioning؛ منع الاحتفاظ بالملف كله في RAM.
- **معايير القبول:** timeout HTTP لا يقتل job؛ تقرير كبير لا يتجاوز memory budget؛ المستخدم يرى حالته ويحصل على ملف مخول قصير العمر؛ الحذف وفق retention.
- **التحقق:** تقارير رواتب كبيرة، cancel/retry، worker restart، وcross-tenant download denial.
- **الاعتماديات:** PERF-001, FILE-001.
- **الحالة:** Done (قصص A–D)
- **أدلة الإغلاق:**
  - `ExportJob` + `IExportJobRequested` + `ExportJobAuth` / `IExportArtifactStore`
  - Workers: Payroll / HR.Core / Contracts / Compliance (chunked CSV → artifact store)
  - `202 Accepted` عند تجاوز MaxExportRows أو `async=true`؛ `export-jobs` status/cancel/download مع إعادة تحقق
  - `docs/EXPORT_JOBS.md` + `Perf004ExportJobTests`

## OPS-001 — بيئة تشغيل عالية التوافر متعددة المناطق

- **الأولوية/الحجم/المسؤول:** P2 / XL / Platform Architecture
- **النطاق:** اختيار orchestrator مدعوم؛ multi-AZ topology؛ replicas/autoscaling؛ anti-affinity؛ ingress/WAF/load balancer؛ readiness/startup/graceful shutdown؛ disruption budgets؛ capacity reservations؛ إزالة `container_name` والاعتماد على مضيف واحد.
- **معايير القبول:** فقد node أو instance لا يوقف الخدمة فوق SLO؛ النشر rolling دون جلسات مكسورة؛ autoscaling لا يكرر jobs؛ topology موثقة وكودية.
- **التحقق:** node drain/failure، rolling deploy، load أثناء failure، وتحقق zone distribution.
- **الاعتماديات:** DEP-001, DB-001.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OPS-001-orchestrator.md](../ADR-OPS-001-orchestrator.md) — Kubernetes + multi-AZ؛ Compose = non-HA
  - طوبولوجيا: [OPS_HA_TOPOLOGY.md](../OPS_HA_TOPOLOGY.md)؛ بوابة DEP: [DEP_001_RELEASE_GATE.md](../DEP_001_RELEASE_GATE.md)
  - Kustomize: `deploy/k8s/base` + overlays `staging-ha` / `production-ha` (Gateway+Web؛ لا container_name)
  - replicas/HPA/PDB/anti-affinity؛ Ingress TLS + WAF stubs؛ startup/live/ready؛ `ShutdownTimeout` 30s
  - YARP Service DNS + `PowerOfTwoChoices`؛ migrate Job مرة واحدة؛ workers بـlease (لا تكرار تحت HPA)
  - تحقق: [OPS_HA_VERIFICATION.md](../OPS_HA_VERIFICATION.md) + `scripts/verify-k8s-ha.sh`
  - **HA للبيانات (Postgres/RabbitMQ/Keycloak/MinIO) = OPS-002**

## OPS-002 — HA لـPostgreSQL وRabbitMQ وKeycloak وMinIO

- **الأولوية/الحجم/المسؤول:** P2 / XL / Platform + DBA
- **النطاق:** PostgreSQL replication/failover/connection pooling؛ Rabbit quorum queues وpublisher confirms؛ Keycloak cluster/cache؛ MinIO distributed erasure coding/versioning؛ health؛ TLS؛ upgrades؛ no single shared superuser.
- **معايير القبول:** failover لكل مكون ضمن الزمن المعتمد ودون data loss فوق RPO؛ clients يعيدون الاتصال؛ split-brain محمي؛ upgrades مجربة.
- **التحقق:** kill primary/leader/node، network partition controlled، consistency reconciliation.
- **الاعتماديات:** OPS-001.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OPS-002-data-ha.md](../ADR-OPS-002-data-ha.md)؛ طوبولوجيا: [OPS_DATA_HA_TOPOLOGY.md](../OPS_DATA_HA_TOPOLOGY.md)
  - Kustomize: `deploy/k8s/data/` (`staging-data-ha` / `production-data-ha`) — CNPG Cluster+Pooler، RabbitMQ 3، Keycloak ISPN، MinIO 4
  - `scripts/apply-cnpg-operator.sh`؛ MassTransit quorum + `PublisherConfirmation` في `MassTransitExtensions`
  - ConfigMap التطبيقات → `*.hrmanagement-data.svc`؛ لا shared superuser في مسار التطبيقات
  - تحقق: [OPS_DATA_HA_VERIFICATION.md](../OPS_DATA_HA_VERIFICATION.md) + `scripts/verify-k8s-data-ha.sh`
  - **النسخ / PITR / الاستعادة الكاملة = OPS-003**

## OPS-003 — نسخ احتياطي، PITR واختبار استعادة

- **الأولوية/الحجم/المسؤول:** P2 / L / DBA + Security
- **النطاق:** كل قواعد البيانات وMinIO وKeycloak config؛ WAL/PITR؛ نسخ مشفرة خارج الموقع وغير قابلة للتغيير؛ مفاتيح منفصلة؛ retention tiers؛ backup catalog؛ restore automation؛ reconciliation عبر الخدمات.
- **معايير القبول:** restore كامل ونقطي ناجح في بيئة معزولة؛ FileStorage ضمن النطاق؛ قياس RPO/RTO الفعلي؛ تنبيه عند تأخر/فساد النسخ؛ وصول backup بأقل صلاحية.
- **التحقق:** restore شهري مؤتمت، checksum، حذف/تلف متعمد، ومقارنة business counts.
- **الاعتماديات:** OPS-002, FILE-001.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OPS-003-backup-pitr.md](../ADR-OPS-003-backup-pitr.md)؛ طوبولوجيا: [OPS_BACKUP_TOPOLOGY.md](../OPS_BACKUP_TOPOLOGY.md)
  - جرد dump كامل + `file_storage_db` + checksum/catalog؛ دور `hrm_backup` (`scripts/sql/create-hrm-backup-role.sql`)
  - CNPG `barmanObjectStore` + `ScheduledBackup`؛ CronJobs MinIO/Keycloak؛ `backup-offsite-sync.sh`
  - استعادة: `restore-postgres.sh` / `restore-pitr-drill.sh` / `restore-minio-drill.sh`
  - IT.Infra `BackupJobs.Run` يزامن الكتالوج (لا stub صامت)
  - تحقق: [OPS_BACKUP_VERIFICATION.md](../OPS_BACKUP_VERIFICATION.md) + `verify-backup-catalog.sh`
  - **DR الإقليم الثانوي = OPS-004**

## OPS-004 — خطة DR وتمارين RTO/RPO

- **الأولوية/الحجم/المسؤول:** P2 / L / SRE + Business Continuity
- **النطاق:** BIA؛ RTO/RPO لكل خدمة ورحلة؛ secondary region/site؛ DNS/failover؛ communication tree؛ degraded modes؛ data reconciliation عند العودة؛ جدول تمارين؛ سجل نتائج وإجراءات.
- **معايير القبول:** tabletop ثم technical failover ناجحان؛ أصحاب القرار معروفون؛ لا RTO/RPO تخميني؛ العودة للموقع الأساسي مجربة؛ الفجوات تتحول لبطاقات.
- **التحقق:** DR drill دون خطوات يدوية غير موثقة، وقياس الزمن والفقد.
- **الاعتماديات:** OPS-003.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OPS-004-dr-rto-rpo.md](../ADR-OPS-004-dr-rto-rpo.md)؛ BIA: [OPS_DR_BIA_RTO_RPO.md](../OPS_DR_BIA_RTO_RPO.md)
  - طوبولوجيا: [OPS_DR_TOPOLOGY.md](../OPS_DR_TOPOLOGY.md)؛ اتصال: [OPS_DR_COMMUNICATION_TREE.md](../OPS_DR_COMMUNICATION_TREE.md)
  - stubs: `deploy/k8s/overlays/dr-secondary` + `data/overlays/dr-secondary-data`
  - سكربتات: `dr-failover-drill.sh` / `dr-failback-reconcile.sh` / `dr-dns-cutover-check.sh`
  - تحقق: [OPS_DR_VERIFICATION.md](../OPS_DR_VERIFICATION.md)؛ runbook § DR
  - **توفير الموقع الثانوي كـIaC = OPS-005**؛ BCP الورقي = BCP-001؛ منشآت = PHY-001

## OPS-005 — Infrastructure as Code وإدارة الإعداد

- **الأولوية/الحجم/المسؤول:** P2 / XL / Platform
- **النطاق:** networks/compute/data/identity/observability/secrets ككود؛ environments منفصلة؛ policy as code؛ drift detection؛ immutable config؛ change review؛ bootstrap؛ state protection؛ no manual console changes.
- **معايير القبول:** إنشاء staging من الصفر آلياً؛ drift يكتشف ويمنع؛ الأسرار مراجع لا قيم؛ production changes عبر PR وموافقة؛ destroy protections للبيانات.
- **التحقق:** clean-room provision، drift injection، disaster bootstrap.
- **الاعتماديات:** OPS-001, IAM-004.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OPS-005-iac.md](../ADR-OPS-005-iac.md)؛ طوبولوجيا: [OPS_IAC_TOPOLOGY.md](../OPS_IAC_TOPOLOGY.md)
  - Terraform: `terraform/modules/*` + `envs/{staging,production,dr-secondary}` + remote state example
  - أسرار: External Secrets platform + overlays `*/external-secrets/` (IAM-004 refs)
  - Policy/CI: `policy/iac/` + `iac-plan.yml` / `iac-apply.yml` / `iac-drift.yml`
  - سكربتات: `iac-bootstrap.sh` / `iac-apply-env.sh` / `iac-kustomize-apply.sh` / `iac-drift-check.sh`
  - تحقق: [OPS_IAC_VERIFICATION.md](../OPS_IAC_VERIFICATION.md)
  - **REL-001 يستهلك staging الناتج**؛ apply سحابي كامل عند توفر حساب AWS

## OBS-001 — Telemetry كاملة لكل الخدمات

- **الأولوية/الحجم/المسؤول:** P2 / L / Observability
- **النطاق:** traces/metrics/logs لكل الخدمات والworkers؛ trace propagation عبر RabbitMQ/HTTP/jobs؛ dashboards للـRED/USE؛ DB/MQ/outbox/inbox/job metrics؛ sampling وسياسة PII؛ retention/cost.
- **معايير القبول:** رحلة مستخدم تتبع end-to-end؛ جميع الخدمات تظهر في service map؛ لا PII خام؛ dashboards تكتشف توقف consumer أو تراكم outbox.
- **التحقق:** synthetic journey، trace completeness، PII scan، ومقارنة inventory.
- **الاعتماديات:** OPS-001.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OBS-001-telemetry.md](../ADR-OBS-001-telemetry.md)؛ طوبولوجيا: [OBS_TELEMETRY_TOPOLOGY.md](../OBS_TELEMETRY_TOPOLOGY.md)
  - جرد: [OBS_SERVICE_INVENTORY.md](../OBS_SERVICE_INVENTORY.md)؛ PII/sampling: [OBS_TELEMETRY_PII_AND_SAMPLING.md](../OBS_TELEMETRY_PII_AND_SAMPLING.md)
  - Tempo + collector exporters + scrape كامل + postgres-exporter؛ Grafana RED/USE/messaging
  - Instrumentation: AddMeter / MassTransit source / EF / job Activities / outbox metrics / PII scrub
  - سكربتات: `obs-synthetic-journey.sh` / `obs-inventory-compare.sh` / `obs-pii-scan.sh`
  - K8s: `deploy/k8s/platform/observability/` + OTEL env في configmap
  - تحقق: [OBS_TELEMETRY_VERIFICATION.md](../OBS_TELEMETRY_VERIFICATION.md)
  - **مستقبل Alertmanager + burn SLO = OBS-002**

## OBS-002 — SLI/SLO وتنبيهات قابلة للتنفيذ

- **الأولوية/الحجم/المسؤول:** P2 / L / SRE + Product
- **النطاق:** availability/latency/correctness/freshness لكل رحلة حرجة؛ error budgets؛ multi-window burn alerts؛ notification integrations؛ ownership/escalation؛ runbook links؛ maintenance handling.
- **معايير القبول:** كل alert له مالك وسبب وفعل؛ Alertmanager يصل قناة حقيقية؛ alerts تختبر؛ SLO يعكس رحلة لا مجرد process up؛ تقارير شهرية.
- **التحقق:** alert fire drills، black-box probes، ومراجعة false positives.
- **الاعتماديات:** OBS-001.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - ADR: [ADR-OBS-002-slo-alerts.md](../ADR-OBS-002-slo-alerts.md)؛ كتالوج: [OBS_SLO_CATALOG.md](../OBS_SLO_CATALOG.md)
  - طوبولوجيا: [OBS_ALERTING_TOPOLOGY.md](../OBS_ALERTING_TOPOLOGY.md)؛ runbooks: [OBS_ALERT_RUNBOOKS.md](../OBS_ALERT_RUNBOOKS.md)
  - قواعد: `recording-rules.yml` + `slo-burn-alerts.yml`؛ blackbox + `alert-sink`
  - Grafana: `obs002-slo`؛ سكربتات fire-drill / monthly-report / silence / labels-check
  - تحقق: [OBS_SLO_VERIFICATION.md](../OBS_SLO_VERIFICATION.md)
  - **SIEM = OBS-003**

## OBS-003 — SIEM وتدقيق غير قابل للتلاعب

- **الأولوية/الحجم/المسؤول:** P2 / XL / Security Operations
- **النطاق:** append-only/WORM أو خدمة مستقلة؛ توقيع/hash chain؛ time sync؛ privileged access events؛ correlation؛ SIEM rules؛ export/legal hold؛ separation from app admins؛ integrity verification.
- **معايير القبول:** تعديل/حذف حدث مكتشف أو ممنوع؛ privileged actions قابلة للمساءلة؛ قواعد كشف للـbulk lookup/file access/role changes/payroll override؛ retention معتمد.
- **التحقق:** tamper test، detection drills، chain verification، واسترجاع تحقيق كامل.
- **الاعتماديات:** AUD-001, OPS-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - ADR: [ADR-OBS-003-siem-tamper-audit.md](../ADR-OBS-003-siem-tamper-audit.md)؛ طوبولوجيا: [OBS_SIEM_TOPOLOGY.md](../OBS_SIEM_TOPOLOGY.md)؛ تهديد: [OBS_AUDIT_THREAT_MODEL.md](../OBS_AUDIT_THREAT_MODEL.md)
  - Hash chain على `gateway_audit_entries` + migration OBS003؛ `AuditChainVerifier`؛ `scripts/obs-audit-chain-verify.sh`
  - `SecurityAuditEvent` + hooks (Lookup/Auth/File/Payroll/Privileged) + `policy/siem/*.yaml` + `SiemDetectorService` → alert-sink
  - WORM export + catalog؛ legal hold API؛ retention `AllowPurge`؛ أدوار DB `scripts/sql/obs003-audit-db-roles.sql`
  - تحقق: [OBS_SIEM_VERIFICATION.md](../OBS_SIEM_VERIFICATION.md)؛ drills؛ `Obs003AuditHashChainTests`
  - **AUD-001 Done** كأثر جانبي موثّق

## NFR-001 — نموذج سعة واختبارات load/soak/stress

- **الأولوية/الحجم/المسؤول:** P2 / L / Performance + Product
- **النطاق:** أعداد الموظفين والجهات والمستخدمين والتزامن والمواسم وحجم الملفات؛ workload model؛ thresholds؛ datasets اصطناعية؛ load/soak/spike/stress؛ bottleneck budget؛ نتائج قابلة للمقارنة.
- **معايير القبول:** حجم الهدف وheadroom موثقان؛ SLO محقق تحت peak وfailure؛ لا تسرب ذاكرة/connection/queue؛ degradation معلوم وآمن؛ capacity plan لمدة 24 شهراً.
- **التحقق:** تقارير موقعة، raw results، repeatability، وprofiling لأعلى ثلاثة اختناقات.
- **الاعتماديات:** PERF-001, OPS-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - ADR: [ADR-NFR-001-capacity-load.md](../ADR-NFR-001-capacity-load.md)؛ نموذج: [NFR_CAPACITY_MODEL.md](../NFR_CAPACITY_MODEL.md)؛ طوبولوجيا: [NFR_LOAD_TOPOLOGY.md](../NFR_LOAD_TOPOLOGY.md)
  - Seed: `scripts/nfr-seed-synthetic.ps1` / `.sh`؛ harness: `perf/k6/*` + `scripts/nfr-k6-run.*` + `docker-compose.load.yml`
  - سيناريوهات load/soak/spike/stress مربوطة بـJ1–J4؛ تحقق: [NFR_CAPACITY_VERIFICATION.md](../NFR_CAPACITY_VERIFICATION.md)
  - اختناقات: [NFR_BOTTLENECK_BUDGET.md](../NFR_BOTTLENECK_BUDGET.md)؛ نتائج منهجية: `perf/results/baseline-lab/`
  - خطة 24م: [NFR_CAPACITY_PLAN_24M.md](../NFR_CAPACITY_PLAN_24M.md)؛ تسليم مغلف 72h/150%/200% → **CAP-001**

## NFR-002 — اختبارات الفشل والفوضى والتعافي

- **الأولوية/الحجم/المسؤول:** P2 / L / SRE + QA
- **النطاق:** dependency latency/down؛ packet loss؛ duplicate/out-of-order events؛ disk full؛ clock skew؛ expired cert؛ secret rotation؛ worker crash؛ partial region loss؛ safe abort conditions.
- **معايير القبول:** لا فساد صامت؛ retries bounded مع jitter؛ circuit breakers لا تخفي الخطأ؛ alarms تسبق SLO breach حيث أمكن؛ runbooks محدثة من النتائج.
- **التحقق:** جدول game days، فرضيات ونتائج ومتابعات، وإعادة الاختبار بعد الإصلاح.
- **الاعتماديات:** OPS-004, OBS-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - ADR: [ADR-NFR-002-chaos-recovery.md](../ADR-NFR-002-chaos-recovery.md)؛ كتالوج: [NFR_CHAOS_CATALOG.md](../NFR_CHAOS_CATALOG.md)؛ طوبولوجيا: [NFR_CHAOS_TOPOLOGY.md](../NFR_CHAOS_TOPOLOGY.md)
  - Harness: `docker-compose.chaos.yml` + `scripts/nfr-chaos-inject|restore.*`
  - Jitter retry MassTransit؛ CB ظاهر على `gateway-health-probe`؛ اختبارات `Nfr002*`
  - Drills: disk/secret/cert/worker/safe-abort؛ runbooks: [NFR_CHAOS_RUNBOOKS.md](../NFR_CHAOS_RUNBOOKS.md)
  - تحقق + تقويم: [NFR_CHAOS_VERIFICATION.md](../NFR_CHAOS_VERIFICATION.md)، [NFR_GAME_DAY_CALENDAR.md](../NFR_GAME_DAY_CALENDAR.md)؛ `chaos/results/baseline-lab/`
  - partial region → OPS-004 drills؛ تنبيهات → OBS-002

## مخرجات بوابة P2

- تقرير سعة على الحجم الوطني المتفق عليه، لا على جهاز مطور فقط.
- نتائج failover وDR وrestore موثقة بأزمنة فعلية.
- SLO وتنبيهات وon-call يعملون في Staging production-like.
- إثبات عدم فقد أحداث أو رواتب أو ملفات خلال اختبارات الفشل.

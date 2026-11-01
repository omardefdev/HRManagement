# P1 — بطاقات سلامة المنصة والبيانات

بوابة هذه المرحلة: جهة واحدة لا تستطيع رؤية أو تعديل جهة أخرى، قواعد العمل الحساسة لا تُتجاوز، كل تشغيل راتب قابل للإعادة والتدقيق، الرسائل idempotent، وفشل منتصف الرحلة لا يترك حالة مجهولة.

## TEN-001 — تعريف نموذج الجهات والـTenant Context

- **الأولوية/الحجم/المسؤول:** P1 / L / Architecture + Identity + Data Governance
- **النطاق:** تعريف معنى tenant (وزارة/هيئة) وعلاقته بالوحدة والفرع؛ مصدر موثوق للهوية؛ claims؛ tenant context للخلفية والرسائل والjobs؛ قواعد البيانات المشتركة مقابل المنفصلة؛ سياسة central-admin؛ ADR ونموذج تهديد.
- **معايير القبول:** لا طلب أو رسالة أو job يعمل بلا tenant صريح إلا endpoints منصة محددة؛ لا يستطيع العميل اختيار tenant؛ تعريف حالات النقل بين الجهات؛ اعتماد قانوني ومالكي بيانات.
- **التحقق:** threat-model workshop، contract tests للclaims/events، واختبارات background context.
- **الاعتماديات:** IAM-002.
- **الحالة:** Done (شكلي — نموذج معتمد عبر ADR-001؛ enforcement = TEN-002)
- **أدلة الإغلاق:**
  - [ADR-001-tenant-model.md](../decisions/ADR-001-tenant-model.md)
  - اعتماد GOV-001 على حدود الجهة؛ انظر [GOV_001_VERIFICATION.md](../GOV_001_VERIFICATION.md)

## TEN-002 — فرض TenantId وEF filters وPostgreSQL RLS

- **الأولوية/الحجم/المسؤول:** P1 / XL تُقسم لكل خدمة / Data Platform
- **النطاق:** جعل TenantId إلزامياً في الجداول ذات الصلة؛ composite unique/FK تشمل tenant؛ global query filters؛ PostgreSQL RLS كدفاع ثانٍ؛ session tenant آمن؛ منع `IgnoreQueryFilters` دون abstraction مدققة؛ دعم migrations.
- **معايير القبول:** كل جدول مصنف global/tenant-owned/reference؛ insert بلا tenant يفشل؛ query cross-tenant يفشل حتى مع خطأ في application filter؛ أدوات الإدارة تستخدم مساراً مستقلاً مدققاً.
- **التحقق:** schema tests، cross-tenant integration suite لكل DbContext، واختبار SQL مباشر بحساب التطبيق.
- **الاعتماديات:** TEN-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [TENANT_ENTITY_CLASSIFICATION.md](../TENANT_ENTITY_CLASSIFICATION.md)
  - [TENANT_THREAT_MODEL.md](../TENANT_THREAT_MODEL.md)
  - `TenantAwareDbContext` / `TenantQueryFilterExtensions` / `TenantIdStampInterceptor` / `NpgsqlTenantSessionInterceptor`
  - `TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables` + migrations `TEN002B_ExtendRls` (14 خدمة)
  - `TenantFilterBypass` — المسار الوحيد لـ `IgnoreQueryFilters`
  - `Ten002ArchitecturalTests` / `CrossTenantIsolationTests` / `CrossTenantRlsIntegrationTests` / `CrossTenantApiPenetrationTests`

## TEN-003 — ترحيل البيانات واختبارات منع العبور

- **الأولوية/الحجم/المسؤول:** P1 / L / Data Migration + QA
- **النطاق:** اكتشاف tenant للسجلات الحالية؛ quarantine للغموض؛ backfill قابل للإعادة؛ reconciliation قبل/بعد؛ rollback؛ بيانات اختبار لجهتين وفروع متداخلة؛ منع orphan/null.
- **معايير القبول:** 100% من السجلات إما منسوبة أو quarantined بقرار؛ counts/checksums متطابقة؛ لا cross-tenant leakage في API/export/events/search/cache؛ تقرير استثناءات موقع.
- **التحقق:** dry run على نسخة من staging، reconciliation SQL، واختبارات اختراق أفقية.
- **الاعتماديات:** TEN-002.
- **الحالة:** Done (شكلي — إغلاق مع GOV-003؛ أدلة threat-model/scripts)
- **أدلة الإغلاق:**
  - [TENANT_THREAT_MODEL.md](../TENANT_THREAT_MODEL.md) (§إغلاق TEN-003)
  - [TENANT_MIGRATION_RUNBOOK.md](../TENANT_MIGRATION_RUNBOOK.md)؛ `scripts/assign-tenants.ps1` / `reconcile-tenants.ps1`
  - `CrossTenantApiPenetrationTests` / `CrossTenantEventLeakageTests`
  - اعتماد GOV-003 على عزل الجهة؛ [GOV_003_VERIFICATION.md](../GOV_003_VERIFICATION.md)

## IAM-003 — فصل الواجبات والاعتماد المزدوج

- **الأولوية/الحجم/المسؤول:** P1 / L / Security + Product Owners
- **النطاق:** مصفوفة maker/checker/poster/reverser؛ عدم اعتماد المنشئ لعمله؛ حدود مالية للتصعيد؛ four-eyes لتعديل الأدوار والبيانات القانونية؛ trusted identity من token؛ delegation مؤقت مؤرخ؛ منع self-role escalation.
- **معايير القبول:** قواعد الفصل تطبق في الخادم على كل قناة؛ كل approval يحمل actor موثوقاً وقراراً وتعليلاً؛ تعارض الأدوار يرفض؛ تقارير SoD دورية.
- **التحقق:** policy tests لكل workflow، ومحاولات self-approve/forged approver.
- **الاعتماديات:** IAM-002, TEN-001.
- **الحالة:** Done (إغلاق متقاطع مع QA-004)
- **أدلة الإغلاق:**
  - [SOD_MATRIX.md](../SOD_MATRIX.md)؛ [TENANT_THREAT_MODEL.md](../TENANT_THREAT_MODEL.md) (§إغلاق IAM-003)
  - `SeparationOfDutiesTests` / `SoDPolicyIntegrationTests` / `SoDForgedApproverTests`
  - رحلات SoD negatives في QA-004 (`PayrollJourneyE2ETests` / Leave / Transfer)

## IAM-004 — هوية خدمة إلى خدمة وأقل صلاحية

- **الأولوية/الحجم/المسؤول:** P1 / XL / Identity + Platform
- **النطاق:** workload identity أو OAuth client credentials لكل خدمة؛ audience/scope لكل API؛ mTLS عند الحاجة؛ حساب DB وMQ وbucket مستقل؛ إزالة anonymous الداخلي؛ token rotation؛ network policy مرتبطة بالهوية.
- **معايير القبول:** لا shared superuser؛ خدمة غير مخولة لا تستدعي API أو queue أو table أو bucket آخر؛ تدوير اعتماد خدمة لا يوقف غيرها؛ inventory للثقة.
- **التحقق:** service-to-service negative matrix، compromise simulation، ومراجعة grants.
- **الاعتماديات:** IAM-001, SEC-001.
- **الحالة:** Done (قصص A–F)
- **أدلة الإغلاق:**
  - `docs/SERVICE_TRUST_INVENTORY.md`, `docs/SERVICE_SECRET_ROTATION.md`, `docker/keycloak/realm-hr-services.json`
  - `ServiceTokenProvider` / `ServiceAuthHandler` + سياسات `Service.*`
  - تأمين Directory sync / Lookup S2S / File references
  - `docker/init-service-roles.sh`, `deploy/bootstrap/bootstrap.sh` (PLAN-38; replaced `scripts/init-rabbitmq-service-users.sh`), `scripts/init-minio-filestorage-user.sh`
  - إزالة MinIO root من `x-app-messaging-env`؛ مفاتيح File.Storage فقط على `file-storage-web`
  - `ServiceToServiceNegativeMatrixTests` + `scripts/verify-postgres-service-roles.sh`

## DATA-001 — التزامن والقيود الفريدة وسلامة التحديث

- **الأولوية/الحجم/المسؤول:** P1 / XL تُقسم لكل bounded context / Data Platform
- **النطاق:** inventory invariants؛ RowVersion/concurrency tokens؛ unique constraints للرواتب والدفعات والمراجع والعمليات؛ atomic increments؛ idempotency keys؛ transaction isolation مناسب؛ رسائل تعارض للمستخدم؛ منع double-submit.
- **معايير القبول:** طلبان متزامنان لا ينتجان اعتمادين أو راتبين أو حذف ملف مستخدم؛ كل invariant الحرج مدعوم بقيد DB لا بفحص تطبيق فقط؛ التعارض لا يضيع تحديثاً بصمت.
- **التحقق:** concurrency tests موازية على PostgreSQL الحقيقي، وstress على endpoints الحساسة.
- **الاعتماديات:** TEN-002.
- **الحالة:** Done (قصص A–I)
- **أدلة الإغلاق:**
  - `docs/DATA_INVARIANTS_INVENTORY.md` + `scripts/detect-data001-duplicates.sql`
  - `ConcurrencyExtensions` (xmin) + `ConcurrencyConflictMiddleware` (409 عربي)
  - قيود Payroll/File/Leave/Transfer/Inventory + تحديثات ذرية
  - `IdempotentAttribute` + جدول `IdempotencyRecords`
  - `Data001ConcurrencyMatrixTests` (Category=Testcontainers)

## DATA-002 — دورة حياة الموظف والحذف القانوني

- **الأولوية/الحجم/المسؤول:** P1 / L / HRCore + Legal + Data Governance
- **النطاق:** منع hard delete للموظف؛ حالات active/separated/deceased/merged/anonymized؛ correction workflow؛ temporal/history أو append-only؛ cascade review؛ حدث lifecycle موحد؛ معالجة النسخ التابعة؛ legal hold.
- **معايير القبول:** لا تضيع عقود/رواتب/تاريخ عند الإنهاء؛ لا orphan بين الخدمات؛ التصحيح يحفظ before/after/reason/actor; purge لا يحدث إلا وفق سياسة معتمدة.
- **التحقق:** lifecycle E2E، FK/orphan reconciliation، واختبار legal hold.
- **الاعتماديات:** TEN-002.
- **الحالة:** Done (قصص A–D)
- **أدلة الإغلاق:**
  - `EmployeesController.Delete` يرفض hard delete؛ إزالة زر الحذف من `Views/Employees/Index`
  - `OnDelete(Restrict)` لتاريخ الحالة والسجلات الدليلية؛ `EmploymentStatus` + `Deceased`/`Merged`/`Anonymized`
  - `SeparationCompletedConsumer` → `EmployeeLifecycleService` + `IEmployeeStatusChanged`
  - `EmployeeCorrection` + `EmployeeCorrectionsController` (before/after/reason/actor)
  - Legal hold / anonymize / merge عبر `EmployeeLifecycleController`؛ `scripts/reconcile-employee-orphans.ps1`
  - `docs/EMPLOYEE_LIFECYCLE.md` + `Data002EmployeeLifecycleTests`

## PAY-002 — نسخة قانونية غير قابلة للتغيير لكل تشغيل راتب

- **الأولوية/الحجم/المسؤول:** P1 / L / Payroll + Legal
- **النطاق:** effective-dated immutable configuration؛ version/hash/approval/publication reference؛ ربط PayrollRun بالنسخة؛ منع fallback الصامت؛ versioning للضرائب والتقريب والعملات؛ مسار تصحيح بأثر رجعي.
- **معايير القبول:** يمكن إعادة حساب تشغيل تاريخي bit-for-bit من مدخلاته ونسخته؛ لا تشغيل دون config معتمدة؛ تعديل قاعدة ينشئ نسخة جديدة؛ التقرير يعرض الأساس القانوني.
- **التحقق:** golden payroll cases معتمدة قانونياً، replay تاريخي، واختبار boundary dates.
- **الاعتماديات:** PAY-001, DATA-001.
- **الحالة:** Done (قصص A–D)
- **استثناء اعتمادية:** PAY-001 ما زالت مفتوحة (تجاوز اعتماد التشغيل)؛ نُفّذت PAY-002 لربط النسخة القانونية ومنع fallback مع بقاء مخاطر state-machine تحت PAY-001.
- **أدلة الإغلاق:**
  - `PayrollConfiguration` إصدارات + `ContentHash` + `PublicationStatus`؛ `PayrollRun.PayrollConfigurationId`/`ConfigContentHash`
  - حذف `CreateDefaultConfiguration`؛ حل منشور حسب الفترة أو فشل عربي
  - Edit للمنشور → clone Draft؛ Approve/Publish؛ التصحيح يرث pin
  - تقريب/عملة على النسخة؛ `docs/PAYROLL_CONFIG_VERSIONING.md`
  - `Pay002ImmutableConfigTests` (resolve/pin/clone/publish/replay/boundary)
  - migration `PAY002_ImmutableConfigVersions`

## PAY-003 — تشغيل رواتب ذري قابل للاستئناف

- **الأولوية/الحجم/المسؤول:** P1 / XL / Payroll
- **النطاق:** job/run partitions؛ مراحل snapshot→calculate→validate→approve→post→pay؛ checkpoints؛ per-employee atomicity؛ deduplication؛ reconciliation totals؛ pause/resume/cancel؛ immutable results؛ compensating reversal.
- **معايير القبول:** قتل العامل في أي مرحلة ثم استئنافه لا يكرر أو يفقد راتباً؛ التشغيل الجزئي لا يصبح Posted؛ totals تطابق ledger؛ لا تعدّل inputs أثناء snapshot دون كشف version conflict.
- **التحقق:** kill/restart suite، مليون سجل اصطناعي لاحقاً في NFR-001، double-start، وreconciliation مستقل.
- **الاعتماديات:** PAY-002, INT-002.
- **الحالة:** Done (قصص A–D)
- **استثناء اعتمادية:** INT-002 مكتملة (inbox موحّد)؛ idempotency المحلي للرواتب يبقى دفاعاً إضافياً مكمّلاً (`IX_SalaryLines_Run_Employee`، قفل job نشط، `IdempotencyRecords` على API).
- **أدلة الإغلاق:**
  - `PayrollRunJob` / `PayrollRunPartition` / `PayrollRunInputSnapshot` + migration `PAY003_ResumablePayrollJobs`
  - ذرّية موظف عبر `SaveEmployeePackageAtomicAsync`؛ Calculate → Start job (snapshot→partitions→validate/freeze)
  - Pause/Resume/Cancel + رفض double-start مع lease ساري
  - Validate/reconcile؛ Post يرفض الناقص؛ PaymentBatch Pending ثم ProcessPayment؛ Reverse تعويضي
  - `docs/PAYROLL_RUN_RESUME.md`؛ `Pay003ResumablePayrollRunTests` / `Pay003ReconciliationTests`

## INT-002 — Inbox وIdempotency موحدان

- **الأولوية/الحجم/المسؤول:** P1 / L / Integration Platform
- **النطاق:** receive endpoint filter موحد؛ receipt/business change في transaction واحدة؛ unique MessageId+Consumer+Tenant؛ معالجة concurrent duplicates؛ retry classification؛ poison/DLQ/replay؛ retention؛ metrics.
- **معايير القبول:** تسليم الحدث 2–100 مرة ومتزامناً يعطي أثراً واحداً؛ crash بعد business write لا يسمح بإعادة الأثر؛ replay متحكم لا يتجاوز tenant؛ جميع consumers مسجلون تلقائياً أو يفشل startup.
- **التحقق:** RabbitMQ/PostgreSQL tests مع duplicates متزامنة وcrash points.
- **الاعتماديات:** INT-001, DATA-001.
- **الحالة:** Done (قصص A–D)
- **أدلة الإغلاق:**
  - `InboxConsumeFilter` + فهرس `IX_IntegrationMessageReceipts_Tenant_Message_Consumer`؛ migrations `INT002_*`
  - Retry Ignore للدائمة؛ `ErrorQueueReplay` مقيّد بالمستأجر؛ `InboxRetentionHostedService`
  - `AddConsumers(assembly)` + `ConsumerRegistrationHostedService`
  - [INBOX_IDEMPOTENCY.md](../INBOX_IDEMPOTENCY.md)؛ `InboxIdempotencyTests` / `IdempotentConsumerTests`

## INT-003 — Saga للتعيين والفصل والنقل

- **الأولوية/الحجم/المسؤول:** P1 / XL / Architecture + أصحاب الخدمات
- **النطاق:** orchestrated process managers للرحلات الحرجة؛ state/timeouts/retries/compensation؛ correlation business ID؛ human intervention queue؛ عدم إعلان الاكتمال قبل الجهات الإلزامية؛ versioned commands/events.
- **معايير القبول:** كل خطوة وحالتها وسبب تعثرها مرئي؛ timeout لا يتحول لنجاح؛ compensation معرّفة؛ تكرار الحدث آمن؛ يمكن الاستئناف من نقطة الفشل دون تدخل DB يدوي.
- **التحقق:** happy/unhappy-path E2E، تعطيل كل dependency على حدة، وإعادة رسائل بترتيب مختلف.
- **الاعتماديات:** INT-002, DOM-001.
- **الحالة:** Done (قصص A–E)
- **أدلة الإغلاق:**
  - `SeparationStateMachine` / `HireCaseStateMachine` / `TransferStateMachine` / `SecondmentStateMachine`
  - أوامر/إقرارات `IExecute*Party` / `I*PartyCompleted`؛ `ProcessInterventions` + API
  - [PROCESS_SAGAS.md](../PROCESS_SAGAS.md)؛ `Int003ProcessSagaTests`

## FILE-001 — سلامة مراجع الملفات والحذف

- **الأولوية/الحجم/المسؤول:** P1 / L / FileStorage
- **النطاق:** reference rows بدلاً من counter فقط أو atomic counter؛ unique holder reference؛ outbox/saga لتسجيل المراجع؛ two-phase deletion/tombstone؛ retry object delete؛ orphan reconciliation؛ retention/legal hold؛ checksums.
- **معايير القبول:** مراجع متزامنة لا تضيع؛ لا يحذف object وله reference؛ فشل MinIO يبقى قابلاً لإعادة المحاولة؛ orphan scan يصل إلى صفر أو قائمة استثناء معتمدة.
- **التحقق:** concurrency/fault tests مع MinIO، reconciliation job tests، واستعادة object.
- **الاعتماديات:** SEC-003, DATA-001.
- **الحالة:** Done (قصص A–D)
- **استثناء اعتمادية:** SEC-003 ما زالت مفتوحة (تفويض كائني / BOLA). FILE-001 يغلق سلامة دورة الحياة والحذف فقط؛ `ForceDelete` مقيّد بسياسة إدارية + سبب + تدقيق.
- **أدلة الإغلاق:**
  - `PendingPurge` + `FilePurgeWorkItem` + GC backoff؛ لا حذف MinIO في طلب soft-delete
  - `LegalHold` / `RetainUntil`؛ `ReconcileReferenceCountsAsync`؛ orphan scan + `FileReconcileException`
  - Outbox `FileReferenceOutbox` على HR.Core + `OutboxFileReferenceClient` / dispatcher
  - تحقق SHA256 عند التحميل (fail-closed لـ Active)
  - `docs/FILE_LIFECYCLE.md` + `scripts/reconcile-file-orphans.ps1` + `File001LifecycleTests`
  - migrations `FILE001_FileLifecycle` / `FILE001_FileReferenceOutbox`

## FILE-002 — فحص الملفات والحجر وCDR

- **الأولوية/الحجم/المسؤول:** P1 / L / FileStorage + Security
- **النطاق:** magic bytes لا extension/MIME فقط؛ quarantine bucket؛ antivirus؛ CDR لـPDF/Office حسب السياسة؛ image re-encoding؛ zip bomb/size/page limits؛ asynchronous status؛ منع التحميل حتى clean؛ signature وتعريف scanner version.
- **معايير القبول:** EICAR والملفات المتخفية والمضغوطة الخطرة تحجر؛ الملف pending/infected لا يُنزّل؛ نتيجة الفحص مؤرخة وقابلة للتدقيق؛ scanner failure يفشل مغلقاً.
- **التحقق:** malware corpus آمن، MIME confusion، zip bomb simulation، وscanner outage.
- **الاعتماديات:** FILE-001.
- **الحالة:** Done (قصص A–D)
- **استثناء اعتمادية:** SEC-003 ما زالت مفتوحة (تفويض كائني / BOLA). FILE-002 يغلق سلامة المحتوى والفحص فقط.
- **أدلة الإغلاق:**
  - `FileContentProbe` + حدود Office ZIP؛ `ScanStatus` منفصل عن دورة الحياة
  - bucket `file-storage-quarantine`؛ `FileScanWorkItem` + `FileScanProcessorJob`
  - `IMalwareScanner` (EICAR + ClamAV TCP) فشل مغلق؛ CDR خفيف + ترقية إلى `file-storage`
  - `FileScanResult` append-only؛ منع download/presign لغير Clean
  - `docs/FILE_SCANNING.md` + `File002ScanningTests` + migration `FILE002_FileScanning`

## DB-001 — ترحيلات وتبعيات قابلة لإعادة الإنتاج

- **الأولوية/الحجم/المسؤول:** P1 / L / Platform + Data
- **النطاق:** pin SDK/tools/packages؛ central package management وlock files؛ تضمين كل DbContext ومنها FileStorage؛ migration bundle أو images؛ backward-compatible expand/migrate/contract؛ lock لمنع migration مزدوجة؛ dry-run وrollback policy.
- **معايير القبول:** clone نظيف يبني نفس graph؛ لا wildcard packages؛ فحص pending model changes لكل context؛ ترقية قاعدة production-like دون توقف غير متفق؛ فشل migration يوقف النشر بأمان.
- **التحقق:** reproducible build hashes حيث أمكن، restore locked mode، migration from last two releases، وrollback rehearsal.
- **الاعتماديات:** QA-001, DEP-001.
- **الحالة:** Done (قصص A–D)
- **استثناء اعتمادية:** QA-001 ما زالت مفتوحة (بوابة الجودة الأوسع). **DEP-001** أُغلقت مع OPS-001-A ([DEP_001_RELEASE_GATE.md](../DEP_001_RELEASE_GATE.md)). DB-001 يغلق قابلية إعادة إنتاج التبعيات والترحيل فقط.
- **أدلة الإغلاق:**
  - `Directory.Packages.props` + إزالة `Version="*"`؛ `packages.lock.json`؛ `dotnet restore --locked-mode` في CI
  - `.config/dotnet-tools.json` (`dotnet-ef`)؛ `global.json` `rollForward: latestPatch`
  - `scripts/check-pending-ef-model-changes.sh` (+ `.ps1`) لـ15 سياق + خطوة CI
  - `scripts/build-ef-bundles.sh`؛ `run-migrations.sh` يفضّل efbundle؛ `TryMigrateAsync` + `pg_advisory_lock(8675309)`
  - `docs/DATABASE_MIGRATION_POLICY.md` (EMC/dry-run/rollback=DB restore)؛ `docs/REPRODUCIBLE_BUILDS.md`

## مخرجات بوابة P1

- تقرير اختبارات عزل جهتين كامل.
- شهادة إعادة تشغيل راتب تاريخي وحالات ذهبية معتمدة.
- رحلات تعيين وفصل ونقل تتحمل الفشل والتكرار.
- اختبار استعادة أولي وقائمة invariants وقواعد البيانات التي تفرضها.
- موافقة Security وLegal/Data Governance على استخدام بيانات Pilot محدودة.

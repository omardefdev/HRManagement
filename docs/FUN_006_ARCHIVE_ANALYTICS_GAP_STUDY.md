# دراسة الأرشيف والمراسلات والوثائق والتحليلات والتقارير (ARC) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** خدمة Records.Archive (عناصر الأرشيف، الملفات الرقمية والورقية، الفهرس، التصنيفات وخطة الملفات، الإعارة، الحجز القانوني، طلب الإتلاف واعتماده، الاحتفاظ وفق [GOV_001_RETENTION_RUNBOOK.md](GOV_001_RETENTION_RUNBOOK.md))، ومراسلات الديوان في HR.Core، وسجل الأختام في IDCard، ومستندات الموظف (`Documents`) واكتمال ملفه، والتوقيع القانوني (`Signatures`/`SignaturesApi`، SIG-001)، ومكتبة PDF العربية `HRManagement.Documents.QuestPdf` (AC-36)، وخدمة Dev.Analytics (الأحداث، نماذج القراءة، `AnalyticsReconciliationApi`، ETL وجداول التحديث، تخطيط القوى العاملة، `AnalyticsEventsApi`، خطافات الاختبار)، وتقارير HR.Core (`Reports`/`StatsApi`/`HrStatisticReports`) وتقارير الرواتب، والتصدير الخلفي ([EXPORT_JOBS.md](EXPORT_JOBS.md))، ومسائل عابرة: انحراف نماذج القراءة، البيانات الشخصية في التصدير، وحجب الخلايا الصغيرة.  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والإعدادات (`appsettings*`، `docker-compose*`، `deploy/k8s`، `docker/keycloak`) والوثائق عند `f9eab1a` (`main`، 2026-10-29)، ومقارنتها بما تدّعيه الوثائق وبطاقات الإكمال.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK؛ النسخة «lite» بلا `wwwroot/lib` ولا `*.Designer.cs` ولا ModelSnapshot ولا الصور والخطوط). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [ADR-FUN-006](ADR-FUN-006-archive-analytics-reporting-closure.md) · [FUN_006_JOURNEY_MATRIX.md](FUN_006_JOURNEY_MATRIX.md) · [FUN_006_EXCEPTION_PATHS.md](FUN_006_EXCEPTION_PATHS.md) · [FUN_006_RECONCILIATION_CHECKLIST.md](FUN_006_RECONCILIATION_CHECKLIST.md) · [FUN_006_UAT_PACK.md](FUN_006_UAT_PACK.md) · [FUN_006_VERIFICATION.md](FUN_006_VERIFICATION.md) · [ANA_001_ANALYTICS.md](ANA_001_ANALYTICS.md) · [SIG_001_SIGNATURE_POLICY.md](SIG_001_SIGNATURE_POLICY.md) · [SYR_015_RECORDS_MATRIX.md](SYR_015_RECORDS_MATRIX.md) · [GOV_001_RETENTION_SCHEDULE.md](GOV_001_RETENTION_SCHEDULE.md) · [EXPORT_JOBS.md](EXPORT_JOBS.md) · [completion-backlog/04 (FUN-006)](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [completion-backlog/13 (SYR-015)](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md) · [PLAN-09](acceptance-plans/PLAN-09-reconciliation-db-isolation.md) (AC-19) · [PLAN-13](acceptance-plans/PLAN-13-ops-quality.md) (AC-36)

---

## 1. الخلاصة

1. **المسار موجود كهيكل لا كخدمة أرشيف وتحليلات تعمل في الإنتاج.** توجد كيانات وشاشات CRUD للأرشيف (عنصر، ملف رقمي وورقي، فهرس، إعارة، تصنيف)، وقواعد حجز قانوني وطلب إتلاف باعتماد شخص ثانٍ، وسجل حيازة، و7 مستهلكات أحداث للأرشفة الآلية. وفي التحليلات: 6 مستهلكات عبر Inbox ثابت، وبطاقة توظيف تحجب الخلايا الصغيرة، وواجهتا عدد وفجوة بنطاق الفرع. وفي الوثائق: 8 مستندات PDF عربية بخط مضمّن، وسجل توقيع غير قابل للتعديل. لكن معظم ما تَعِد به FUN-006 لا يعمل خارج اختبارات الوحدة.
2. **عبارة «Done» في [FUN_006_VERIFICATION.md](FUN_006_VERIFICATION.md) و`completion-backlog/04` و`05-TRACEABILITY` لا تطابق الواقع.** وجدنا عائقين P0:
   - **(أ) الأرشفة الآلية لا تحدث في أي بيئة منشورة.** مستهلكو Records.Archive يطلبون رقماً مرجعياً من HR.Core، لكن الخدمة لا تملك عنوان HR.Core ولا نطاق `hr-core.workflow`. يفشل الطلب، فيُبتلع الحدث بصمت. ومع ذلك تعلّم HR.Contracts معاملة التعيين والفصل «مؤرشفة» (G-ARC-01).
   - **(ب) لا استرجاع للأرشيف بعد 100 عنصر.** القائمة تعرض آخر 100 عنصر فقط، بلا بحث ولا ترقيم. ومنتقيات ربط الملفات تعرض 50 عنصراً بلا ترتيب. ولا رابط بين عنصر الأرشيف والموظف (G-ARC-02).
3. **ثغرات سلامة وحوكمة (P1):**
   - الحجز القانوني يمنع طلب الإتلاف فقط. يمكن حذف الملف الرقمي أو تبديله أو تقصير الاحتفاظ تحت الحجز.
   - «الإتلاف» سجلّ بلا إتلاف فعلي ولا شهادة.
   - مدد الاحتفاظ ثابتة في الشيفرة (10 سنوات من تاريخ الإدخال) وتخالف SYR-015 (75 سنة من الفصل لملف العامل).
   - المجموع الاختباري يُكتب يدوياً، وإثبات الاكتمال ينكسر عند ملفين.
   - نتائج اللجنة الطبية ونصوص الشكاوى تدخل عناوين الأرشيف، والأسماء تدخل حمولات التحليلات.
   - عدد الموظفين في التحليلات لا ينقص بالفصل أبداً.
   - المصالحة تقارن نموذج القراءة بنفسه.
   - حد «الفرع لا يرى الإجمالي» مطبّق في واجهتين فقط.
   - ملفات التصدير الخلفي على قرص كل نسخة ولا تُحذف.
   - ملخص الرواتب الشهري يجمع المسيّرات المعكوسة والمسودات.
   - مفتاح التوقيع القانوني يتولد عشوائياً عند كل إقلاع.
   - مستندات الموظف تُحذف وتُوثَّق بلا فصل مهام ولا احترام للحجز.
4. **فجوات قدرات (P2):** اكتمال ملف الموظف، وديوان حقيقي (ترقيم، إحالة، مرفقات)، ووثائق رسمية للقرارات بترويسة الجهة وختمها وتحقق عام، وقدرات مكتب أرشيف (مواقع، إعارة لموظف، نقل للأرشيف التاريخي، بُعد الجهة)، وحجب الخلايا الصغيرة خارج التوظيف، ومنصة تقارير ومؤشرات غير يدوية.
5. **فجوة تحقق:**
   - اختبارات `Fun006*` اختبارات وحدة لقواعد ثابتة، ومنها اختبار يبني حدث فصل بجهة لا يصنعه الإنتاج.
   - اختبار E2E الوحيد يفحص رفض الدخول المجهول.
   - AC-18 وAC-19 لم يُشغَّلا. AC-36 «Pass» بلا عينات ولا توقيع مراجع.
   - اختبار التكامل يستبدل عميل الترميز بمزيّف، فيخفي G-ARC-01.
   - SYR-015 «Done» في بطاقته و«NotStarted» في البطاقة التي تعتمد عليه.

**التقدير الإجمالي:** نحو 65–83 يوم عمل لمطوّر واحد (≈ 4–5 sprints لفريق من مطوّرَين)، منها 7–9 أيام للمرحلة P0 (ARC-A).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | التصنيف وخطة الملفات | `DocumentClassification` + `RetentionYears`/`RetentionCode`؛ المسار الرقمي اليدوي يشترط تصنيفاً | ⚠️ رمز عام واحد `ret-archive-docs` بلا ربط بفئات SYR-015 (G-ARC-05) |
| 2 | الإدخال اليدوي | `ArchiveItemsController.Create`: قائمة تحقق 7 خطوات + حدث حيازة `Ingested` | ✅ لحجم صغير، بلا موظف ولا جهة (G-ARC-02) |
| 3 | الإدخال الآلي من الأحداث | 7 مستهلكات (تعيين، فصل، إجازة، سلفة، شكوى، توصية وقرار طبيان) | ❌ لا يحدث في الإنتاج (G-ARC-01) |
| 4 | الملف الرقمي وإثبات السلامة | `DigitalFileRecord.Checksum` + `CompletenessFingerprint` | ❌ المجموع يُكتب يدوياً، والإثبات ينكسر عند ملفين (G-ARC-06) |
| 5 | البحث والاسترجاع | `Index` = آخر 100 عنصر | ❌ (G-ARC-02) |
| 6 | الإعارة | `StartLoan` يرفض تحت الحجز؛ `LoanRecords/Create` لا يرفض | ⚠️ (G-ARC-03، G-ARC-19) |
| 7 | الحجز القانوني | `PlaceLegalHold`/`ReleaseLegalHold` | ⚠️ يمنع طلب الإتلاف فقط (G-ARC-03) |
| 8 | الإتلاف | `RequestDisposal` ثم `ApproveDisposal` بمستخدم مختلف | ⚠️ سجل بلا إتلاف ولا شهادة (G-ARC-04) |
| 9 | الاحتفاظ | `RetentionPeriodYears` من `CreatedAt` | ❌ قيم ثابتة تخالف SYR-015 (G-ARC-05) |
| 10 | الديوان | `DiwanCorrespondences` (وارد/صادر) | ⚠️ سجل نصي بلا ترقيم ولا إحالة (G-ARC-17) |
| 11 | مستندات الموظف | `DocumentsController` | ⚠️ لا فصل مهام ولا حجز (G-ARC-14)، لا اكتمال ملف (G-ARC-16) |
| 12 | الوثائق الرسمية PDF | 8 مستندات QuestPDF بخط Noto Sans Arabic | ⚠️ ترويسة ثابتة، لا قرارات، لا ختم (G-ARC-18) |
| 13 | التوقيع القانوني | `SoftwareSignatureService` على خطوات مسار العمل فقط | ❌ مفتاح مؤقت لكل نسخة (G-ARC-13) |
| 14 | أحداث التحليلات | 6 مستهلكات + `InboxConsumeFilter` | ⚠️ بيانات شخصية في الحمولة (G-ARC-07) |
| 15 | العدد الفعلي والفجوة | `WorkforcePlanningController` | ❌ الفصل لا يُنسب لأي جهة (G-ARC-08) |
| 16 | المصالحة وإعادة البناء | `ReadModelReconciliationService` يدوي | ❌ يقارن النموذج بنفسه وقد يفسده (G-ARC-09) |
| 17 | ETL وجداول التحديث | `EtlJobs.Run`، `DataRefreshSchedules.Trigger` | ❌ لا منفّذ مجدول (G-ARC-09) |
| 18 | بطاقة التوظيف | `RecruitmentRequisitionFact` + حجب `<5` | ✅ |
| 19 | الفرع لا يرى الإجمالي | فحص المطالبة في `gap-analysis` و`headcount` | ⚠️ التصدير والأحداث واللوحات تتجاوزه (G-ARC-10) |
| 20 | التصدير الخلفي | `ExportJobs` + `LocalExportArtifactStore` | ⚠️ قرص النسخة، بلا حذف (G-ARC-11) |
| 21 | تقارير HR.Core | `ReportsController` | ✅ نطاق + إخفاء الرقم الوطني + تدقيق |
| 22 | تقارير الرواتب | `PayrollReportsController` | ⚠️ يجمع المعكوس والمسودات (G-ARC-12) |
| 23 | خطافات الاختبار | `AnalyticsTestHooksController` | ✅ يعيد 404 خارج Development |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **قواعد الأرشيف موحّدة في مكان واحد:** `ArchiveRetentionRules` (حجز، طلب إتلاف، اعتماد بمستخدم مختلف، تحقق المجموع) و`ArchiveChecklistDefaults` (7 خطوات بمالك وSLA). والخادم يفرض فصل الطالب عن المعتمد: [ArchiveRetentionRules.cs:78-84](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs) و[ArchiveItemsController.cs:254-259](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs).
- **لا يوجد حذف لعنصر أرشيف ولا لسجل إتلاف** في أي متحكم، وسجل الحيازة `CustodyEvent` يُضاف ولا يُعدَّل من الواجهة.
- **عزل الجهات:** RLS على كل جداول الأرشيف والتحليلات (`TEN002`، `TEN002B_ExtendRls`).
- **الاستهلاك ثابت (idempotent):** `InboxConsumeFilter` يلف كل مستهلك بمعاملة وإيصال (`MessageId+Consumer+Tenant`) ([IdempotentConsumerFilter.cs:47-101](../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs)).
- **بطاقة التوظيف في Dev.Analytics (PLAN-32) سليمة:**
  - تخزّن مجاميع فقط (`RecruitmentRequisitionFact`) بفهرس فريد لكل جهة وطلب.
  - تحجب العدّ من 1 إلى 4 والوسيط ([RecruitmentFactRules.cs:22-62](../src/Services/DevAnalytics/Dev.Analytics.Domain/Services/RecruitmentFactRules.cs)).
- **تقارير الموظفين في HR.Core سليمة:**
  - تطبّق نطاق الوحدة على العرض والتصدير، وتخفي الرقم الوطني، وتسجّل التصدير الجماعي.
  - التصدير الخلفي يحمل لقطة النطاق ويتحقق من الانتهاء عند التنزيل ([ReportsController.cs:37-113](../src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs)، [HRCoreExportJobService.cs:55-70, 191-199](../src/Services/HRCore/HR.Core.Infrastructure/ExportJobs/HRCoreExportJobService.cs)).
- **خطاف حقن الانحراف** يعيد `NotFound` خارج Development ([AnalyticsTestHooksController.cs:20-21](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/AnalyticsTestHooksController.cs)). واختبار AC-19 لا يعتمد عليه، بل يحقن الانحراف عبر SQL.
- **وثائق PDF العربية:**
  - تُسجَّل خطوط Noto Sans Arabic المضمّنة مرة واحدة، ويُضبط اتجاه RTL ([HrQuestPdfBootstrap.cs](../src/Shared/HRManagement.Documents.QuestPdf/HrQuestPdfBootstrap.cs)، [HrArabicDocumentExtensions.cs](../src/Shared/HRManagement.Documents.QuestPdf/HrArabicDocumentExtensions.cs)).
  - خطاب العرض وبطاقة الامتحان يطبعان رمز تحقق من بصمة مخزنة، وينبّهان إذا لم تطابق البيانات البصمة ([OfferLetterDocument.cs:59-69](../src/Services/HRCore/HR.Core.Web/Services/OfferLetterDocument.cs)).
- **سجل التوقيع غير قابل للتعديل ولا الحذف** عبر `SignatureImmutabilityInterceptor`، والحمولة القانونية تُجزّأ بشكل حتمي (`CanonicalPayloadBuilder`).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال أو الدليل · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-ARC-01 — الأرشفة الآلية من الأحداث لا تحدث في الإنتاج، وتُبتلع بصمت
- **الدليل:**
  - كل مستهلك أرشفة يطلب رقماً مرجعياً من HR.Core، ثم يخرج دون خطأ إذا لم يحصل عليه:
    - [ArchiveReferenceFactory.cs:12-22](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ArchiveReferenceFactory.cs) يعيد `null` عند الفشل.
    - [HireCaseCompletedConsumer.cs:23-28](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/HireCaseCompletedConsumer.cs) (`LogWarning` ثم `return`)، و[SeparationCompletedConsumer.cs:26-27](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/SeparationCompletedConsumer.cs)، و[LeaveAndAdvanceApprovedConsumer.cs:20-21, 51-52](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs)، و[ComplaintResolvedArchiveConsumer.cs:21-22](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ComplaintResolvedArchiveConsumer.cs)، و[MedicalArchiveConsumers.cs:25-26, 54-55](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/MedicalArchiveConsumers.cs).
    - النتيجة: تُحفظ رسالة الإيصال في Inbox، فلا إعادة محاولة.
  - العميل يبتلع كل استثناء ويعيد `null` ([CodeGenerationClient.cs:31-56](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)). ودون `Integration:HrCoreBaseUrl` يُسجَّل عميل HTTP بلا عنوان أساس ([CodeGenerationClient.cs:76-87](../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs)).
  - لا يوجد `Integration__HrCoreBaseUrl` لخدمة الأرشيف في أي إعداد:
    - ملفات `appsettings*.json` الثلاثة لـ Records.Archive.
    - [docker-compose.services.yml:199-229](../docker-compose.services.yml).
    - [docker-compose.production.yml:407-426](../docker-compose.production.yml).
    - مرساة `acceptance-service` في [docker-compose.acceptance.yml:2-7](../docker-compose.acceptance.yml).
    - `hr-app-config` في [configmap-app.yaml](../deploy/k8s/base/configmap-app.yaml). المفتاح موجود فقط لبوابة YARP: [configmap-yarp.yaml:39](../deploy/k8s/base/configmap-yarp.yaml).
  - حتى مع العنوان، `generate` يتطلب `HrCoreWrite` ([CodeGenerationController.cs:12-21](../src/Services/HRCore/HR.Core.Web/Controllers/CodeGenerationController.cs))، أي `hr-core.workflow` لهوية خدمة ([HrManagementAuthorizationExtensions.cs:73-77](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). لكن نطاق `svc-records-archive` هو `file-storage.references` فقط ([docker-compose.production.yml:420](../docker-compose.production.yml)، [SERVICE_TRUST_INVENTORY.md:17](SERVICE_TRUST_INVENTORY.md)).
  - العقود تعلن الأرشفة دون انتظار الأرشيف: `HireCaseCompletedConsumer` ينقل معاملة التعيين إلى `Archived` («Hire archived after saga completed») ([SagaSupportConsumers.cs:183-197](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs))، والفصل كذلك (`:89`). أما الإجازة والسلفة فتنتظر `ITransactionArchived` (`:166-176`) الذي لن يصل.
  - حتى لو نجح الإدخال الآلي، فهو ناقص:
    - العنصر الناتج «رقمي» بلا تصنيف ولا ملف ولا قائمة تحقق ولا حدث حيازة ([ArchiveReferenceFactory.cs:30-38](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ArchiveReferenceFactory.cs))، وهذا يخالف قاعدة FUN-006 «عنصر رقمي بلا تصنيف ← رفض» ([FUN_006_EXCEPTION_PATHS.md:9](FUN_006_EXCEPTION_PATHS.md)).
    - قائمة تحقق التعيين تُفتح بمفتاح `ServiceTransactionId` لا بمعرّف العنصر ([PartyAckConsumers.cs:152-182](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/PartyAckConsumers.cs))، فلا تظهر في صفحة العنصر ([ArchiveItemsController.cs:37-38](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs)).
  - الاختبار الوحيد للمستهلك يستبدل عميل الترميز بمزيّف ([IntegrationTestHelpers.cs:40](../src/Tests/HRManagement.IntegrationTests/IntegrationTestHelpers.cs)، عبر [DfdGapClosureConsumerTests.cs:25](../src/Tests/HRManagement.IntegrationTests/DfdGapClosureConsumerTests.cs))، فينجح دائماً.
  - **يحتاج تحقق:** صيغة المرجع `ARC-{Prefix}-{yyyy}-{seq}` بلا مقطع جهة ([CodeGenerationSeeder.cs:94-100](../src/Services/HRCore/HR.Core.Infrastructure/Data/CodeGenerationSeeder.cs))، والفهرس الفريد على `ReferenceNumber` عام لا لكل جهة ([ArchiveDbContext.cs:33](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs)). إن كان التسلسل لكل جهة فستتصادم جهتان على المرجع نفسه.
- **الأثر:** في الإنتاج لا يُؤرشف أي ملف تعيين أو فصل أو قرار إجازة أو ملف طبي، ولا يظهر أي خطأ. وتقول العقود إن المعاملة «مؤرشفة». أي جهة ستكتشف ذلك عند أول طلب استرجاع.
- **الإصلاح المقترح:**
  - إضافة `Integration__HrCoreBaseUrl` ونطاق مخصص للترميز إلى هوية `svc-records-archive` (compose وk8s وrealm).
  - فشل الترميز يرمي استثناءً (إعادة محاولة ثم طابور أخطاء) لا `return`، مع فحص جاهزية عند الإقلاع في Production.
  - فهرس فريد `(TenantId, SourceType, SourceId)` بدل التحقق بالعنوان.
  - إنشاء قائمة التحقق وحدث الحيازة للعنصر الآلي، وتصنيف حسب فئة SYR-015 (G-ARC-05).
  - لا تنقل العقود معاملة التعيين أو الفصل إلى `Archived` إلا بعد `ITransactionArchived`.
- **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (جزئياً: `Integration__HrCoreBaseUrl` ونطاق الترميز لـ `svc-records-archive` في compose وk8s وrealm، وفشل الترميز يرمي فيُعاد ثم يذهب إلى `_error`؛ والفهرس الفريد وقائمة التحقق وحدث الحيازة والتصنيف وانتظار `ITransactionArchived` «الموجة 2»)
- **التحقق المطلوب:** على `docker-compose.acceptance-prodlike.yml` وبإعدادات الإنتاج (دون مزيّف):
  - موجب: إكمال تعيين ← عنصر أرشيف بمرجع `ARC-HIRE-…` وموظف وتصنيف وقائمة تحقق وحدث حيازة، والمعاملة `Archived` بعد ذلك فقط.
  - سالب: إيقاف HR.Core أثناء الحدث ← الرسالة تُعاد ثم تذهب إلى طابور الأخطاء، ولا تصبح المعاملة `Archived`.

#### G-ARC-02 — لا استرجاع للأرشيف بعد 100 عنصر، ولا ربط بالموظف
- **الدليل:**
  - [ArchiveItemsController.cs:21-24](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs): `Index()` بلا معاملات، `Take(SearchFilterBase.MaxPageSize)` = 100 ([SearchFilterBase.cs:7-8](../src/Shared/HRManagement.SharedKernel/ViewModels/SearchFilterBase.cs)). والعرض بلا نموذج بحث ([Index.cshtml](../src/Services/RecordsArchive/Records.Archive.Web/Views/ArchiveItems/Index.cshtml)).
  - إضافة ملف رقمي أو ورقي أو فهرس أو إعارة تتم من منتقٍ يعرض `Take(LookupMaxPageSize)` = 50 عنصراً، غالباً بلا ترتيب ([ArchiveExtendedControllers.cs:217-221, 154-158, 325-329, 77](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveExtendedControllers.cs)). ولا يوجد زر «إضافة ملف» من صفحة العنصر. العنصر رقم 51 لا يمكن إرفاق ملف به من الواجهة.
  - `ArchiveItem` بلا `EmployeeId` ولا `OrgUnitId` ([ArchiveItem.cs:7-58](../src/Services/RecordsArchive/Records.Archive.Domain/Entities/ArchiveItem.cs)). معرّف الموظف موجود فقط نصاً داخل العنوان ([HireCaseCompletedConsumer.cs:18](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/HireCaseCompletedConsumer.cs)).
  - `ArchiveIndexEntry` (كلمات مفتاحية وملخص) لا يستعمله أي بحث.
  - تصدير جرد الأرشيف `ArchiveExportJobService.EnqueueAsync` ([ArchiveExportJobService.cs:36-69](../src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs)) لا يستدعيه أي متحكم، مع أن سيناريو UAT U12 يدّعيه ([FUN_006_UAT_PACK.md:16](FUN_006_UAT_PACK.md)). وحتى لو استُدعي فيأخذ أول 10000 عنصر فقط (`:91-94`).
  - [QUERY_EXPORT_INVENTORY.md:351-356](QUERY_EXPORT_INVENTORY.md) يعدّ هذا «capped» نجاحاً: السقف وُضع لحماية الأداء دون بحث يعوّضه.
- **الأثر:** بعد أسابيع من التشغيل لا يجد موظف الأرشيف ملفاً أقدم من آخر 100. ولا يستطيع جمع ملف موظف واحد، ولا إرفاق صورة مستند بأغلب العناصر. لا يمكن تنفيذ «تمرين الاسترجاع» المطلوب في بطاقة FUN-006 ([04-P3:228](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md)).
- **الإصلاح المقترح:**
  - بحث وترقيم في الخادم على `Index`: المرجع، العنوان، الكلمات المفتاحية، الموظف، التصنيف، التاريخ، حالة الحجز والإتلاف.
  - منتقيات بالبحث (autocomplete) بدل `Take(50)`، وأزرار إضافة ملف وفهرس وإعارة من صفحة العنصر.
  - حقول `EmployeeId` و`OrgUnitId` و`SourceType/SourceId` تُملأ من الأحداث ومن الإدخال اليدوي، مع ترحيل يستخرج المعرّف من العناوين القديمة.
  - صفحة «ملف الموظف الأرشيفي».
  - نقطة HTTP لتصدير الجرد عبر `ExportJobs` بترقيم.
- **التحقق المطلوب:**
  - موجب: 500 عنصر مزروعة ← البحث بالمرجع والموظف يجد العنصر رقم 1. إرفاق ملف بالعنصر رقم 400 من صفحته ينجح. تصدير الجرد يُرجع 202 ثم ملف 500 سطر.
  - سالب: البحث بموظف من جهة أخرى لا يعيد شيئاً.

### 4.2 ثغرات سلامة القرار والحوكمة والدليل (P1)

#### G-ARC-03 — الحجز القانوني والاحتفاظ قابلان للتجاوز من الدور نفسه
- **الدليل:**
  - الحجز يدخل فقط في `CanRequestDispose`/`CanApproveDispose`/`CanDispose` و`StartLoan` ([ArchiveRetentionRules.cs:65-91](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)، [ArchiveItemsController.cs:132-136](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs)).
  - **حذف المحتوى تحت الحجز:** `DigitalFileRecords/Delete` يحذف السجل ويحرّر مرجع الملف في File.Storage، بلا فحص حجز ولا حدث حيازة ([ArchiveExtendedControllers.cs:293-310](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveExtendedControllers.cs)). وبعدها يصبح الملف مرشحاً للإتلاف بعد 24 ساعة، ما لم يكن عليه `LegalHold` أو `RetainUntil` في File.Storage ([FilePlatformService.cs:310-320](../src/Services/FileStorage/File.Storage.Infrastructure/Services/FilePlatformService.cs)). والأرشيف لا يضبط أياً منهما أبداً.
  - **تبديل المحتوى:** `DigitalFileRecords/Edit` يبدّل `FileId` و`Checksum` (حتى إلى قيمة فارغة) دون حجز ولا حيازة ولا `HasChecksum` (`:263-291`).
  - **تعديل العنصر تحت الحجز أو بعد الإتلاف:** `ArchiveItems/Edit` يغيّر `Format` و`ClassificationId` و`RetentionPeriodYears` ([ArchiveItemsController.cs:102-124](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs)). وتغيير `Format` إلى ورقي يُسقط شرط التصنيف. و`UpdateRetention` يقبل أي رقم، حتى 1، دون حد أدنى من التصنيف ولا موافقة ولا حدث حيازة (`:320-329`). والتغيير يُسجَّل فقط في `EntityAuditLog` العام.
  - **إعارة خارج القاعدة:** `LoanRecords/Create` و`Edit` العامّان لا يفحصان الحجز ولا الإتلاف ([ArchiveExtendedControllers.cs:81-125](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveExtendedControllers.cs)).
  - **الوضع والرفع بلا مرجع ولا معتمد:** السبب الافتراضي «Legal hold» ([ArchiveRetentionRules.cs:103-115](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)). بينما يشترط SYR-015 «مرجعاً ومعتمداً» وانتشار الحجز إلى قاعدة البيانات ومخزن الملفات والتحليلات والنسخ ([SYR_015_RECORDS_MATRIX.md:38-42](SYR_015_RECORDS_MATRIX.md)).
  - **حجوزات معزولة:** `Employee.LegalHold` في HR.Core ([Employee.cs:88](../src/Services/HRCore/HR.Core.Domain/Entities/Employee.cs)) لا يصل إلى عناصر أرشيف الموظف ولا إلى ملفاته في File.Storage، فلا حدث ولا ربط. والكيانان `LegalHold`/`OfficialRecord` في SharedKernel (LEG-001) لا يستعملهما أي `DbContext`، بل يفحصهما فقط `Leg001RetentionTests`.
  - [GOV_001_RETENTION_RUNBOOK.md:5-7](GOV_001_RETENTION_RUNBOOK.md) يطلب «تأكيد لا حجز نشط» ثم تشغيل `gov001-retention-dry-run.py`. لكن السكربت يقرأ إعداد البوابة فقط، ولا يغطي `ret-archive-docs` ([gov001-retention-dry-run.py:23-67](../scripts/gov001-retention-dry-run.py)).
- **الأثر:** الضابط الذي تعلنه FUN-006 («لا إتلاف تحت Hold») يتجاوزه أي مستخدم `records-archive` بنقرة حذف أو بتقصير الاحتفاظ ثم طلب إتلاف يعتمده زميل. وهذا إتلاف دليل في قضية قائمة.
- **الإصلاح المقترح:**
  - حارس حجز موحّد في كل مسار تعديل وحذف (العنصر، الملفات، الإعارة، الاحتفاظ).
  - الحذف يصبح «سحباً» بسبب وموافقة شخص ثانٍ وحدث حيازة.
  - الحجز يتطلب مرجع قضية، ورفعه يتطلب معتمداً مختلفاً.
  - نشر الحجز إلى File.Storage (`LegalHold` على الملفات المرتبطة)، واستهلاك حدث حجز الموظف من HR.Core لحجز عناصره.
  - تقصير الاحتفاظ دون الحد الأدنى للفئة ممنوع، أو بموافقة موثقة.
  - إدخال الأرشيف في تقرير التجربة الجافة.
- **التحقق المطلوب:**
  - سالب: تحت الحجز ← حذف ملف رقمي، أو تبديل `FileId`، أو `UpdateRetention`، أو `Edit` للصيغة، أو إعارة عامة، كلها مرفوضة.
  - موجب: حجز الموظف في HR.Core ← عناصره وملفاته محجوزة. رفع الحجز بنفس الشخص مرفوض، وبمعتمد ثانٍ مقبول.

#### G-ARC-04 — «الإتلاف» سجلّ بلا إتلاف فعلي ولا شهادة، والإلغاء يقفل العنصر للأبد
- **الدليل:**
  - `ApproveDisposal` يعتمد ويضع `DisposeApproved` و`Disposed` معاً، ويضيف `DisposalRecord` («تم اعتماد وتنفيذ الإتلاف») ([ArchiveItemsController.cs:248-294](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs)):
    - الملفات الرقمية تبقى مرتبطة ومحفوظة في File.Storage، فلا حذف ولا تحرير مرجع.
    - لا خطوة تنفيذ ورقي بشاهد.
    - `disposalDate` يُدخله المستخدم، فيمكن إرجاع التاريخ.
  - SYR-015 يطلب شهادة بحقول: الفئة، الحجم، الطريقة، المعتمد، بصمة الشاهد، وقت الإكمال ([SYR_015_RECORDS_MATRIX.md:50-54](SYR_015_RECORDS_MATRIX.md)). والـ ADR يعد بـ«شهادة إتلاف أرشيف» ([ADR-FUN-006:24](ADR-FUN-006-archive-analytics-reporting-closure.md)). لا يوجد أي منهما.
  - `CancelDisposalRequest` يضع `IsDisposeCancelled = true` ولا يعيده إلا `RequestDisposal`، و`RequestDisposal` نفسه يرفض عند `IsDisposeCancelled` ([ArchiveRetentionRules.cs:70-76, 117-123](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)). أي إلغاء واحد يمنع إتلاف العنصر إلى الأبد.
  - فجوة في فصل المهام: إن لم يكن `sub` للطالب GUID يُحفظ `DisposeRequestedBy = null` ([ArchiveItemsController.cs:232-233](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs))، و`CanApproveDispose` يقبل حينها أي معتمد آخر دون أي مقارنة (`!requesterId.HasValue ||` في [ArchiveRetentionRules.cs:84](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs))، فيضيع أثر الطالب من فحص فصل المهام. أما الطالب نفسه فلا يستطيع الاعتماد، لأن معرّفه يُقرأ بالطريقة نفسها فيصبح `Guid.Empty` ويُرفض (ArchiveItemsController.cs:254-255، ArchiveRetentionRules.cs:83).
  - المعتمد لا يحتاج دوراً أعلى ولا لجنة: الدور الوحيد `records-archive` ([HrManagementAuthorizationExtensions.cs:43](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
  - لا رمز تزامن على `ArchiveItem` ولا فهرس فريد على `DisposalRecords.ArchiveItemId` ([ArchiveDbContext.cs:69-75](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs)). اعتمادان متزامنان ينتجان سجلّي إتلاف (**يحتاج تحقق**).
- **الأثر:** *(صُحّح بعد التحقق المستقل: الطالب الذي لا يحمل معرّف GUID لا يستطيع اعتماد طلبه، لأن `approverId` يصبح `Guid.Empty` فيُرفض؛ الثغرة هي ضياع هوية الطالب من الفحص فقط)*
  - البيانات الشخصية تبقى بعد انتهاء مدتها، خلافاً للقانون 12/2024 وجدول الاحتفاظ.
  - سجل يقول «أُتلف» والملف ما زال قابلاً للتنزيل.
  - لا دليل إتلاف يُقدَّم للرقابة.
- **الإصلاح المقترح:**
  - فصل «اعتماد» (لجنة حسب D-ARC-2) عن «تنفيذ»:
    - رقمي: حذف قسري عبر File.Storage بسبب، يحترم حجز الملف.
    - ورقي: تأكيد بشاهد.
  - توليد شهادة PDF بالحقول المطلوبة وتخزينها كملف غير قابل للحذف.
  - إلغاء الطلب يعيد العنصر قابلاً للطلب مجدداً.
  - رفض الاعتماد إذا كان الطالب مجهولاً.
  - رمز تزامن وفهرس فريد.
- **التحقق المطلوب:**
  - موجب: طلب ← اعتماد اللجنة ← تنفيذ ← الملف `PendingPurge` ثم محذوف، وشهادة PDF مرفقة.
  - سالب: الاعتماد بلا طالب معروف مرفوض. اعتمادان متوازيان ينتجان سجلاً واحداً. إلغاء ثم طلب جديد ممكن.

#### G-ARC-05 — مدد الاحتفاظ ثابتة في الشيفرة وتبدأ من تاريخ الإدخال، خلافاً لـ SYR-015
- **الدليل:**
  - الإدخال الآلي يضع `retentionYears = 10` لكل الملفات ([ArchiveReferenceFactory.cs:30-38](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ArchiveReferenceFactory.cs))، و15 للملف الطبي ([MedicalArchiveConsumers.cs:33, 62](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/MedicalArchiveConsumers.cs)).
  - الساعة تبدأ من `CreatedAt` للعنصر ([ArchiveRetentionRules.cs:49-53](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs))، لا من الحدث القانوني.
  - مصفوفة SYR-015 ([SYR_015_RECORDS_MATRIX.md:23-34](SYR_015_RECORDS_MATRIX.md)) تحدد:
    - ملف العامل: 75 سنة من تاريخ الفصل.
    - القرار الإداري: 30 سنة من تاريخ القرار.
    - الشكوى: 10 سنوات من الإغلاق.
  - وجدول GOV-001 يقول `ret-medical` = 3650 يوماً ([GOV_001_RETENTION_SCHEDULE.md:6](GOV_001_RETENTION_SCHEDULE.md)). ثلاثة مصادر وثلاث قيم.
  - النتيجة: ملف تعيين موظف ما زال في الخدمة يصبح قابلاً للإتلاف بعد 10 سنوات من تعيينه.
  - خطة الملفات رمز واحد عام `ret-archive-docs` ([ArchiveRetentionRules.cs:8](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)):
    - `HasValidFilePlan` يقبل أي `RetentionCode` غير فارغ (`:58-63`).
    - تعديل التصنيف لا يحدّث `RetentionCode` ([ArchiveExtendedControllers.cs:53-56](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveExtendedControllers.cs)).
    - `records-retention-matrix.json` لا يقرؤه إلا `Syr015RecordsTests`.
  - رمز التصنيف فريد عالمياً لا لكل جهة ([ArchiveDbContext.cs:79](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs)). إذا رُبطت التصنيفات برموز SYR-015 الموحدة فلن تستطيع جهة ثانية إنشاء «employee-file».
  - بطاقة SYR-015 «Done» ([13-SYRIA:184](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md))، بينما بطاقة الإطلاق تعدّها «NotStarted» (`:219`). والجدول يتطلب «جدولاً مصدقاً من كل سلطة مختصة» (`:187`).
- **الأثر:** إتلاف ملفات خدمة لموظفين على رأس عملهم أو متقاعدين حديثاً، أي فقدان دليل الخدمة والمعاش. والمدد لم يعتمدها مرجع قانوني.
- **الإصلاح المقترح:**
  - كيان «فئة سجل» لكل جهة مربوط برموز SYR-015: مدة، ونوع حدث البدء، وإجراء التصرف.
  - `RetentionStartAt` يُملأ من الحدث: الفصل، القرار، الإغلاق. وملف العامل لا يبدأ عدّه حتى `ISeparationCompleted`.
  - إزالة القيم الثابتة من المستهلكين.
  - فهارس فريدة لكل جهة.
  - استخدام `OfficialRecord` في SharedKernel أو حذفه.
  - القيم نفسها تحتاج قراراً (D-ARC-1).
- **التحقق المطلوب:**
  - موجب: تعيين ← عنصر بفئة `employee-file` بلا تاريخ تصرف. فصل ← تاريخ التصرف = تاريخ الفصل + المدة المعتمدة.
  - سالب: طلب إتلاف ملف عامل لم يُفصل بعد، أو بعد 10 سنوات من التعيين، مرفوض. جهتان تنشئان الرمز نفسه دون تعارض.

#### G-ARC-06 — المجموع الاختباري يُكتب يدوياً، وإثبات الاكتمال ينكسر بملفين
- **الدليل:**
  - حقل `Checksum` نص يكتبه المستخدم بعد رفع الملف ([DigitalFileRecords/Create.cshtml:17](../src/Services/RecordsArchive/Records.Archive.Web/Views/DigitalFileRecords/Create.cshtml)). والتحقق الوحيد طوله ≥ 8 ([ArchiveRetentionRules.cs:125-126](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)).
  - File.Storage يخزّن `Sha256` ويملك `VerifyChecksumAsync` ([FileRecord.cs:25-27](../src/Services/FileStorage/File.Storage.Domain/Entities/FileRecord.cs)، [FilePlatformService.cs:540-551](../src/Services/FileStorage/File.Storage.Infrastructure/Services/FilePlatformService.cs))، والأرشيف لا يستعملهما.
  - `CompletenessFingerprint` سلسلة نصية لا بصمة ([ArchiveRetentionRules.cs:128-134](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)):
    - كل ملف بمجموع SHA-256 يضيف 130 حرفاً (`Id:Checksum:FileId`).
    - العمود `CompletenessAttestation` ‏varchar(512)، و`CustodyEvent.EvidenceRef` ‏varchar(256) ([ArchiveDbContext.cs:36, 93](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs)).
    - لذلك `AttestCompleteness` لعنصر بملفين يكتب 261 حرفاً في حدث الحيازة ([ArchiveItemsController.cs:333-344](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs))، وإضافة الملف الرابع تكتب 523 حرفاً في الإثبات ([ArchiveExtendedControllers.cs:247-250](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveExtendedControllers.cs)). كلاهما خطأ `22001` من PostgreSQL أي HTTP 500 (**يحتاج تحقق** بالتشغيل).
  - الإثبات يُعاد حسابه آلياً مع كل ملف يُضاف، فلا يمثّل «شهادة» شخص في وقت محدد. واختبار الوحدة يستعمل مجاميع من 8 أحرف فلا يكشف الطول ([Fun006ArchiveAnalyticsTests.cs:69-82](../src/Tests/HRManagement.UnitTests/Fun006ArchiveAnalyticsTests.cs)).
- **الأثر:** «إثبات السلامة والاكتمال» الذي تعدّه FUN-006 منجزاً لا يثبت شيئاً، وينهار على أول ملف موظف حقيقي.
- **الإصلاح المقترح:**
  - المجموع يُقرأ من File.Storage عند الربط، ويُتحقق منه عند الطلب وفي فحص دوري.
  - الإثبات = SHA-256 لقائمة مرتبة (64 حرفاً) مع المُثبِت والوقت في حدث الحيازة.
  - منع تعديل المجموع يدوياً.
- **التحقق المطلوب:**
  - موجب: عنصر بعشرة ملفات ← إثبات ناجح ببصمة 64 حرفاً. تغيير ملف في المخزن ← فحص السلامة يُبلغ عن عدم التطابق.
  - سالب: إدخال مجموع يدوي مخالف مرفوض.

#### G-ARC-07 — نتائج اللجنة الطبية ونصوص الشكاوى والأسماء تتسرب إلى الأرشيف والتحليلات
- **الدليل:**
  - عنوان عنصر الأرشيف الطبي يتضمن `m.Details`، وعنوان قرار اللجنة يتضمن `m.Notes` ([MedicalArchiveConsumers.cs:21, 50](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/MedicalArchiveConsumers.cs)). و`Notes` = «النتائج» السريرية للجنة ([HealthExtendedControllers.cs:90](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
  - عنوان الشكوى يتضمن موضوعها وقرارها ([ComplaintResolvedArchiveConsumer.cs:17](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/ComplaintResolvedArchiveConsumer.cs)).
  - العناوين تظهر لكل مستخدم `records-archive` في القائمة، وتُكتب في جرد CSV بلا تهريب ([ArchiveExportJobService.cs:98-101](../src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs)).
  - حدث `EmployeeCreated` في التحليلات يخزّن `FullName` و`NationalIdHash` ([EmployeeCreatedConsumer.cs:35-46](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)). وحدث الشكوى يخزّن الموضوع والقرار ([ComplaintResolvedConsumer.cs:35-44](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/ComplaintResolvedConsumer.cs)).
  - `AnalyticsEventsApi` يعيد `Payload` حتى 2000 صف لكل مستخدم في الوحدة، دون نطاق ([AnalyticsEventsApiController.cs:11-21](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/AnalyticsEventsApiController.cs)). ولا حاجة لأي من هذه الحقول في حساب العدد.
- **الأثر:**
  - بيانات صحية حساسة تخرج من حدّ «الوصول السريري الأدنى» (FUN-004) إلى دور الأرشيف وملفات التصدير.
  - أسماء الموظفين في قاعدة رابعة.
  - مخالفة مبدأ التقليل في القانون 12/2024.
- **الإصلاح المقترح:**
  - عناوين الأرشيف من قالب لا نص حر: «ملف طبي — قرار لجنة — {مرجع}». وتقرير الجنة نفسه ملف مرتبط بصلاحية سريرية (D-ARC-10).
  - حمولة التحليلات تقتصر على المعرّفات والأبعاد (الجهة، النوع، التاريخ).
  - ترحيل يُنظف الصفوف القائمة.
  - `AnalyticsEventsApi` بلا حمولة أو للمسؤول فقط.
  - تهريب CSV ومنع حقن الصيغ.
- **التحقق المطلوب:**
  - سالب: قرار لجنة بنتائج نصية ← لا يظهر النص في عنوان الأرشيف ولا في الجرد. `EmployeeCreated` جديد ← الحمولة بلا اسم.
  - موجب: موظف سريري يرى التقرير من رابطه.

#### G-ARC-08 — عدد الموظفين في التحليلات لا ينقص بالفصل ولا يتبع النقل
- **الدليل:**
  - مستهلك الفصل يضع `OrgUnitId = null` ([SeparationCompletedConsumer.cs:47-54](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/SeparationCompletedConsumer.cs))، لأن العقد `ISeparationCompleted` بلا جهة ([ISeparationCompleted.cs:3-23](../src/Shared/HRManagement.MessageContracts/Events/ISeparationCompleted.cs)).
  - `headcount` و`gap-analysis` يرشّحان أحداث الفصل بالجهة ([WorkforcePlanningController.cs:46-50, 91-95](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/WorkforcePlanningController.cs))، فيكون عدد المنفصلين لأي فرع صفراً دائماً.
  - اختبار الوحدة يبني حدث فصل بجهة `orgA` يدوياً ([Fun006ArchiveAnalyticsTests.cs:135](../src/Tests/HRManagement.UnitTests/Fun006ArchiveAnalyticsTests.cs))، أي حالة لا ينتجها الإنتاج.
  - لا مستهلك في Dev.Analytics لـ `ITransferExecuted` ولا `IEmployeeStatusChanged` ولا `IOrgUnitChanged`. الموظف المنقول يبقى في جهته الأولى، والموقوف يُعدّ نشطاً.
  - `OccurredAt = DateTime.UtcNow` (وقت الوصول لا وقت الحدث) ([EmployeeCreatedConsumer.cs:52](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)، [PayrollProcessedConsumer.cs:47](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/PayrollProcessedConsumer.cs))، خلافاً لـ [ANA_001_ANALYTICS.md:38](ANA_001_ANALYTICS.md). و`AcceptLateEvent` صحيحة دائماً وغير مستعملة ([AnalyticsProjectionRules.cs:13-14](../src/Services/DevAnalytics/Dev.Analytics.Domain/Services/AnalyticsProjectionRules.cs)).
  - لا تحميل أولي للموظفين القائمين قبل تشغيل الخدمة أو المرحَّلين.
  - في HR.Core تقرير فجوة موثوق مربوط بالخطة المعتمدة ونطاق الوحدة ([WorkforcePlansController.cs:189-215](../src/Services/HRCore/HR.Core.Web/Controllers/WorkforcePlansController.cs)). فتعطي الشاشتان رقمين مختلفين للفجوة نفسها. وبند المصالحة 9 يقارن التحليلات بأحداثها هي ([FUN_006_RECONCILIATION_CHECKLIST.md:13](FUN_006_RECONCILIATION_CHECKLIST.md)).
- **الأثر:** «العدد الفعلي» لفرع يساوي كل من عُيّن فيه منذ تشغيل الخدمة. والفجوة تظهر فائضاً وهمياً، فتُبنى قرارات تخطيط وتعيين على أرقام خاطئة.
- **الإصلاح المقترح:** حسب D-ARC-3:
  - إما سحب `headcount`/`gap-analysis` من Dev.Analytics وتوجيهها إلى تقرير HR.Core.
  - أو إسقاط حقيقي: جهة في عقد الفصل (v4)، واستهلاك النقل وتغيير الحالة، ووقت الحدث، وتحميل أولي من HR.Core، ومطابقة يومية معه (G-ARC-09).
- **التحقق المطلوب:**
  - موجب: تعيين 3 في الفرع A، ونقل 1 إلى B، وفصل 1 من A ← A = 1 وB = 1، ومطابق لعدّ HR.Core للموظفين النشطين.
  - سالب: حدث متأخر بوقت قديم لا يغيّر لقطة مؤرخة سابقة.

#### G-ARC-09 — المصالحة تقارن النموذج بنفسه وقد تفسده، وETL والجداول بلا منفّذ
- **الدليل:**
  - `ReconcileAsync` يحمّل كل أحداث التحليلات إلى الذاكرة ويقارن النموذج بها، لا بـ HR.Core ([ReadModelReconciliationService.cs:29-43](../src/Services/DevAnalytics/Dev.Analytics.Application/Services/ReadModelReconciliationService.cs)). الحدث المفقود (G-ARC-01/08) لا يُكتشف أبداً. وAC-19 يطلب «مهمة آلية تكشف الاختلاف» ([PLAN-09:13, 17](acceptance-plans/PLAN-09-reconciliation-db-isolation.md)) ولم يُشغَّل.
  - الإسقاط المتوقع «عدد موظفين» لأي نموذج قراءة كان (`:38`). ومع `orgUnitId` يُكتب إسقاط الفرع فوق النموذج العام (`:52-56`)، وكذلك زر `Rebuild` ([DevExtendedControllers.cs:201-205](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/DevExtendedControllers.cs)).
  - `EtlJobs/Run` يكتب آخر 500 حدث فوق نموذج قراءة يختاره بالاسم، وإن لم يجد فأول نموذج أبجدياً ([DevExtendedControllers.cs:116-147](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/DevExtendedControllers.cs)). أي تشغيل ETL يُفسد نموذجاً.
  - `DataRefreshSchedules/Trigger` يحدّث الطوابع فقط، و`NextRunAt` = الآن + يوم، أياً كان `CronExpression` (`:287-296`). ولا توجد في Dev.Analytics ولا Records.Archive مهمة خلفية خاصة بالمجال (الموجود فقط مهام MassTransit العامة: احتفاظ Inbox وتسجيل المستهلكين). ومؤشر الحداثة `IsStale` لا يستعمله إلا اختبار.
  - حالة آخر مصالحة في `Dictionary` غير آمن للتزامن داخل Singleton ([IReadModelReconciliationStatusStore.cs:12-20](../src/Services/DevAnalytics/Dev.Analytics.Application/Services/IReadModelReconciliationStatusStore.cs)، [Program.cs:40](../src/Services/DevAnalytics/Dev.Analytics.Web/Program.cs)). تضيع بإعادة التشغيل وتختلف بين النسختين (`minReplicas: 2` في [hpa.yaml:243](../deploy/k8s/overlays/production-ha/hpa.yaml)).
- **الأثر:** «المصالحة» تثبت أن النموذج يساوي نفسه، و«ETL» و«الجداول» شاشات بلا تشغيل. والمستخدم الذي يضغط «تشغيل» أو «إعادة بناء بفرع» يفسد البيانات.
- **الإصلاح المقترح:**
  - مهمة مجدولة (قفل موزع) تقارن الإسقاط بعدّ HR.Core لكل جهة عبر S2S، وتحفظ التقرير في جدول، وتصحح عند الطلب.
  - إسقاط لكل نوع نموذج.
  - إزالة الكتابة فوق النماذج من ETL، أو تنفيذ ETL حقيقي.
  - منفّذ للجداول أو إزالتها من الواجهة.
  - مؤشر حداثة ظاهر.
- **التحقق المطلوب:**
  - موجب: حذف حدث فصل من التحليلات ← المهمة المجدولة تكشف فرق 1 مع HR.Core وتصححه، وتقرير قبل/بعد محفوظ في القاعدة، وإعادة التشغيل لا تجد فرقاً.
  - سالب: إعادة البناء بفرع لا تغيّر النموذج العام.

#### G-ARC-10 — قاعدة «الفرع لا يرى الإجمالي» مطبّقة في واجهتين فقط
- **الدليل:**
  - الفحص موجود في `gap-analysis` و`headcount` فقط ([WorkforcePlanningController.cs:24-39, 77-97](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/WorkforcePlanningController.cs)).
  - تصدير القوى العاملة يأخذ `orgUnitId` من النموذج بلا فحص مطالبة النطاق ([ExportJobsController.cs:13-24](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/ExportJobsController.cs)). وبلا قيمة يُصدَّر إجمالي الجهة ([AnalyticsExportJobService.cs:108-126](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/ExportJobs/AnalyticsExportJobService.cs)). هذا يناقض U9/U10/U11 ([FUN_006_UAT_PACK.md:13-15](FUN_006_UAT_PACK.md)).
  - `AnalyticsEventsApi` و`Dashboards` (بطاقة التوظيف) و`Reports` و`reconcile?orgUnitId=` بلا نطاق ([AnalyticsEventsApiController.cs:11-21](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/AnalyticsEventsApiController.cs)، [DashboardsController.cs:11-25](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/DashboardsController.cs)، [AnalyticsReconciliationApiController.cs:17-25](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/AnalyticsReconciliationApiController.cs)).
  - الطبقة الدلالية تشترط `national_analytics_role` للإجمالي الوطني ([semantic-layer.json:196](ana/semantic-layer.json)). هذا الدور غير موجود في [HrManagementRoles.cs:4-57](../src/Shared/HRManagement.ServiceDefaults/HrManagementRoles.cs)، ومع ذلك [ANA_001_ANALYTICS.md:73](ANA_001_ANALYTICS.md) يعلّم البند منجزاً.
- **الأثر:** محلل بنطاق فرع يحصل على أرقام الجهة كلها وأسماء موظفيها بطلب تصدير أو استدعاء API، رغم أن معيار القبول الأول في FUN-006 هو منع ذلك.
- **الإصلاح المقترح:**
  - مرشّح نطاق موحد لكل متحكمات Dev.Analytics: القراءة والتصدير والمصالحة. وتسجيل النطاق في لقطة التصدير والتحقق منه عند التنزيل.
  - إنشاء دور الإجمالي الوطني (أو إزالة الادعاء).
- **التحقق المطلوب:**
  - سالب: مستخدم فرع ← تصدير بلا `orgUnitId` أو بجهة أخرى مرفوض، و`events` و`Dashboards` تعرضان جهته فقط.
  - موجب: مستخدم مركزي يرى الإجمالي.

#### G-ARC-11 — ملفات التصدير الخلفي على قرص النسخة ولا تُحذف
- **الدليل:**
  - `LocalExportArtifactStore` يكتب في `ContentRootPath/export-artifacts`، ويرمي `FileNotFoundException` إن لم يجد الملف ([LocalExportArtifactStore.cs:26-56](../src/Shared/HRManagement.ServiceDefaults/LocalExportArtifactStore.cs)).
  - في `production-ha` كل خدمة `minReplicas: 2` ([hpa.yaml:33, 138, 180, 243](../deploy/k8s/overlays/production-ha/hpa.yaml)). ونشر k8s بلا `volumeMounts` لهذا المسار ([dev-analytics-web/deployment.yaml:41-104](../deploy/k8s/base/dev-analytics-web/deployment.yaml)).
  - النتيجة: المهمة تُعالَج في نسخة والتنزيل قد يصل إلى أخرى، فيظهر خطأ 500 (**يحتاج تحقق**). والملف يضيع عند إعادة تشغيل النسخة.
  - `DeleteAsync` لا يستدعيه أحد. ملفات الرواتب وأسماء الموظفين تبقى على القرص بلا حد، بينما [EXPORT_JOBS.md:9](EXPORT_JOBS.md) يعد بـ 24 ساعة. وتنزيل Analytics والأرشيف لا يفحص `ExpiresAt` ([AnalyticsExportJobService.cs:164-178](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/ExportJobs/AnalyticsExportJobService.cs)، [ArchiveExportJobService.cs:129-143](../src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs)). وحده HR.Core يفحصه.
  - [EXPORT_JOBS.md:47-49](EXPORT_JOBS.md) يعالج تعدد النسخ للمعالجة (claim/lease) فقط، لا لموقع الملف.
- **الأثر:**
  - كل تصدير فوق 10000 صف (سجل رواتب جهة كبيرة، موظفون) غير موثوق في الإنتاج.
  - نسخ بيانات شخصية ومالية تتراكم خارج أي جدول احتفاظ.
- **الإصلاح المقترح:**
  - حفظ الملف الناتج في File.Storage (أو حجم مشترك حسب D-ARC-6) مع مدة صلاحية.
  - مهمة تنظيف تحذف الملف وتضع الحالة `Expired`.
  - فحص `ExpiresAt` في كل خدمة.
- **التحقق المطلوب:**
  - موجب: نسختان ← 20 تنزيلاً متتالياً تنجح كلها.
  - سالب: بعد 24 ساعة يُرفض التنزيل والملف غير موجود في المخزن.

#### G-ARC-12 — ملخص الرواتب وسجلها الخلفي يجمعان المسيّرات غير النهائية
- **الدليل:**
  - `MonthlySummary` يجمع رواتب كل مسيّرات الشهر بلا فلتر حالة ([PayrollReportsController.cs:199-220](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)).
  - العكس يغيّر الحالة فقط ويُبقي السطور موجبة ([PayrollRun.cs:173-178](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)). فمسيّر معكوس ثم مسيّر مصحح في الشهر نفسه يُحسبان مرتين، وتدخل المسودة والملغى أيضاً ([PayrollRunStatus.cs:5-27](../src/Services/Payroll/Payroll.Domain/Enums/PayrollRunStatus.cs)).
  - سجل الرواتب المتزامن يصدّر المسيّر المختار. أما الخلفي فيرسل `{Year, Month}` ([PayrollReportsController.cs:156-163](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs))، فيصدّر سطور كل مسيّرات الشهر بلا فلتر حالة ([PayrollExportJobService.cs:251-284](../src/Services/Payroll/Payroll.Infrastructure/ExportJobs/PayrollExportJobService.cs)). التقرير نفسه يعطي رقمين حسب عدد الصفوف.
  - التصدير الجماعي لبيانات الرواتب يُسجَّل في `SecurityAudit` فقط لقسائم ZIP (`:108`)، لا لسجل الرواتب ولا CSV المسيّر ولا نهاية الخدمة ولا الفروقات (`:147, 222, 326, 377`).
- **الأثر:** إجمالي رواتب شهري مضاعف في التقرير الذي تُبنى عليه الموازنة والمطابقة مع المالية، وسجل رواتب خلفي لا يطابق المسيّر المعتمد.
- **الإصلاح المقترح:**
  - الملخص والسجل يأخذان المسيّرات `Posted` (أو `Approved` بخيار ظاهر) فقط، ويعرضان المعكوس منفصلاً.
  - السجل الخلفي يحمل `RunId`.
  - تسجيل تدقيق لكل تصدير جماعي.
- **التحقق المطلوب:**
  - موجب: مسيّر مرحَّل ثم معكوس ثم مسيّر مصحح ← الملخص يساوي المصحح وحده، والسجل الخلفي والمتزامن متطابقان سطراً بسطر.
  - سالب: المسودة لا تظهر في الملخص.

#### G-ARC-13 — مفتاح التوقيع القانوني مؤقت لكل نسخة، والتحقق لا يقارن المحتوى
- **الدليل:**
  - دون `LegalSignatures:CertificatePath` تولّد الخدمة شهادة ذاتية جديدة في الذاكرة عند كل إقلاع ([SoftwareSignatureService.cs:18-27, 66-74](../src/Shared/HRManagement.ServiceDefaults/Signatures/SoftwareSignatureService.cs)). المفتاح غير موجود في أي `appsettings` لـ HR.Core ولا في `docker-compose.production.yml` ولا في `hr-app-secrets` لـ production-ha. وHR.Core يعمل بنسختين ([hpa.yaml:33](../deploy/k8s/overlays/production-ha/hpa.yaml)).
  - `Verify` يرفض كل توقيع بغير بصمة الشهادة الحالية ([SoftwareSignatureService.cs:56-62](../src/Shared/HRManagement.ServiceDefaults/Signatures/SoftwareSignatureService.cs)). شاشة التحقق تقول «التوقيع غير صالح — فشل التحقق» ([SignaturesController.cs:38-50](../src/Services/HRCore/HR.Core.Web/Controllers/SignaturesController.cs)) لكل اعتماد وقّعته نسخة أخرى أو تشغيل سابق.
  - الشهادة العامة لا تُحفظ، فالتحقق المستقل الذي تعد به [SIG_001_SIGNATURE_POLICY.md:58-63](SIG_001_SIGNATURE_POLICY.md) مستحيل.
  - التحقق يتأكد من التوقيع على البصمة المخزنة، ولا يعيد حساب البصمة من سجل مسار العمل. `VerifyWithPayload` موجود ولا يُستدعى ([SignatureVerifier.cs:30-37](../src/Shared/HRManagement.ServiceDefaults/Signatures/SignatureVerifier.cs)).
  - لا حماية تمنع الإقلاع في Production بشهادة ذاتية، مع أن SIG-001 يحدد شهادة CA في Staging وHSM في Production (`:40-48`).
  - المستدعي الوحيد لـ `SignAsync` هو توقيع خطوات مسار العمل ([WorkflowSignatureService.cs:41-74](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowSignatureService.cs)). وسياسة التوقيع (عقد بتوقيعين، قرار بختم) ([SignaturePolicy.cs:8-16](../src/Shared/HRManagement.SharedKernel/Signatures/SignaturePolicy.cs)) و`SignatureRequiredAttribute` غير مستعملين. أما «توقيع العقد» فنموذج يُدخَل تاريخه يدوياً ويُحذف ([ContractSignaturesController.cs:44-85](../src/Services/HRContracts/HR.Contracts.Web/Controllers/ContractSignaturesController.cs)).
- **الأثر:** سجل «التوقيع القانوني» ينتج أدلة سلبية كاذبة («غير صالح») لاعتمادات سليمة بعد أول إعادة تشغيل، ولا يمكن لجهة رقابية التحقق منه. والوثائق تصفه دليلاً قانونياً.
- **الإصلاح المقترح:**
  - رفض الإقلاع في Production دون شهادة مُعدّة (أو وسم السجلات «غير موقّعة قانونياً» صراحة حتى D-ARC-4).
  - جدول للشهادات العامة بالبصمة يتيح التحقق من أي نسخة وبعد التدوير.
  - التحقق يعيد بناء الحمولة من `WorkflowStepHistoryEntry`.
  - ربط سياسة التوقيع بالعقد والقرار مؤجل إلى ما بعد قرار PKI.
- **التحقق المطلوب:**
  - موجب: اعتماد خطوة ← إعادة تشغيل HR.Core ← التحقق «صالح» من النسخة الأخرى.
  - سالب: تعديل تعليق الخطوة في القاعدة ← «غير صالح». الإقلاع في Production بلا شهادة يفشل.

#### G-ARC-14 — مستندات الموظف: توثيق بلا فصل مهام، وحذف يتجاهل الحجز، وطلبات بطاقة تضيع
- **الدليل:**
  - `Edit` يضبط `Verified` بالمستخدم نفسه الذي رفع المستند، بلا `VerifiedBy`/`VerifiedAt` ([DocumentsController.cs:217-275](../src/Services/HRCore/HR.Core.Web/Controllers/DocumentsController.cs)، [EmployeeDocument.cs:36-37](../src/Services/HRCore/HR.Core.Domain/Entities/EmployeeDocument.cs)). و«جهة الإصدار» تُملأ باسم المستخدم الذي رفع المستند (`:121`).
  - `Delete` حذف نهائي مع تحرير الملف، ولا يفحص `Employee.LegalHold` (`:283-321`؛ [Employee.cs:88](../src/Services/HRCore/HR.Core.Domain/Entities/Employee.cs)). وملف العامل محفوظ 75 سنة حسب SYR-015.
  - طلب البطاقة من الخدمة الذاتية يُحفظ «مستنداً» من نوع `Other` في ملف الموظف، مع رسالة «سيتم معالجته من قسم الهويات» ([EssPortalController.cs:173-192](../src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs)). لكن لا شيء يصل إلى `CardRequest` في IDCard. والمرفق لا يُسجَّل مرجعه في File.Storage، فيُعدّ يتيماً ويُجمع بعد 24 ساعة (**يحتاج تحقق**).
- **الأثر:**
  - مستند «موثَّق» لا يُعرف من وثّقه.
  - إمكانية حذف شهادة أو هوية من ملف موظف محجوز قانونياً.
  - طلبات بطاقات لا يراها قسم الهويات أبداً، ومرفقاتها تُحذف.
- **الإصلاح المقترح:**
  - توثيق بمستخدم ثانٍ مع تسجيل `VerifiedBy`/`VerifiedAt`.
  - سحب بسبب بدل الحذف، ممنوع تحت الحجز.
  - فصل «جهة الإصدار» عن «من رفع».
  - طلب البطاقة ينشئ `CardRequest` عبر حدث مع تسجيل مرجع المرفق، وإزالة الصفوف المزيفة بترحيل.
- **التحقق المطلوب:**
  - سالب: الرافع لا يوثّق مستنده. الحذف تحت الحجز مرفوض.
  - موجب: طلب بطاقة من ESS يظهر في قائمة طلبات IDCard، ومرفقه ما زال موجوداً بعد 48 ساعة.

### 4.3 فجوات التحقق والتوثيق

#### G-ARC-15 — ادعاءات الإغلاق بلا أدلة قبول، وأدلة AC-36 شكلية
- **الدليل:**
  - [FUN_006_VERIFICATION.md:13](FUN_006_VERIFICATION.md) «Done»، و[FUN_006_UAT_PACK.md:18](FUN_006_UAT_PACK.md) «ReadyForSign-off»، وبطاقة FUN-006 «Done» ([04-P3:225](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md))، و`05-TRACEABILITY` ✓ (`:48, 93, 134-135`).
  - الأدلة المذكورة `Fun006*` نوعان:
    - اختبارات وحدة لقواعد ثابتة ([Fun006ArchiveAnalyticsTests.cs](../src/Tests/HRManagement.UnitTests/Fun006ArchiveAnalyticsTests.cs)).
    - اختبار E2E يفحص رفض الدخول المجهول فقط ([Fun006ArchiveAnalyticsE2ETests.cs:12-26](../src/Tests/HRManagement.E2E/Fun006ArchiveAnalyticsE2ETests.cs)).
  - لا سيناريو قبول واحد لـ U1–U12، ولا مجلد `acceptance-evidence` لـ FUN-006.
  - AC-18 (لحاق مستهلك الأرشيف) وAC-19 (مصالحة التحليلات) «لم يُنفّذا» ([summary.md:40-63](../acceptance-evidence/summary.md)). واختبار AC-19 يحقن الانحراف في النموذج نفسه، فلا يثبت المصالحة مع المصدر ([Ac19AnalyticsReconciliationTests.cs:19-62](../src/Tests/HRManagement.AcceptanceTests/Ac19AnalyticsReconciliationTests.cs)).
  - AC-36:
    - «Pass» ([summary.md:19](../acceptance-evidence/summary.md))، لكن المجلد يحوي `metadata.json` و`result.json` («Run acceptance-pdf-arabic.ps1 for sample PDFs») و`timing.json` فارغاً فقط، بلا عينات ولا قائمة مراجعة. وهو من `gitSha 9cb3705` (2026-08-24).
    - الاختبار الحالي يكتب «Pass» مع `humanVisualSignOffRequired = true` ([Ac36ArabicPdfLiveTests.cs:45-55](../src/Tests/HRManagement.AcceptanceTests/Ac36ArabicPdfLiveTests.cs)). وقائمة المراجعة البصرية غير موقعة ([AC-36-arabic-pdf-visual.md](acceptance-plans/checklists/AC-36-arabic-pdf-visual.md)).
  - [ANA_001_ANALYTICS.md:69-73](ANA_001_ANALYTICS.md) يعلّم «إعادة بناء حتمية» و«الإجمالي الوطني منفصل» و«الإجماليات تتصالح» منجزة، بأدلة هي ملف JSON. والبطاقة نفسها `ANA-001 = NotStarted` في [07-NATIONAL:506](completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md).
  - [SYR_015_RECORDS_MATRIX.md:58-64](SYR_015_RECORDS_MATRIX.md) سبعة بنود `[x]` لا يقابلها تنفيذ (G-ARC-03/04/05).
- **الأثر:** قرار الإصدار يستند إلى «Done» لا يغطي أي سلوك حي. وكل الفجوات أعلاه تمر في CI.
- **الإصلاح المقترح:**
  - سيناريوهات `AC-ARC-*` بـ `[RequiredAcceptance]` على الملف الشبيه بالإنتاج، وتشغيل AC-18/19/36 فعلياً.
  - AC-36 لا يكتب «Pass» قبل توقيع المراجع (بل `Blocked`/`AwaitingReview`).
  - تصحيح `FUN_006_*` وANA-001 وSYR-015 و`05-TRACEABILITY`.
- **التحقق المطلوب:**
  - موجب: كل سيناريو موجب/سالب في §6 له مجلد أدلة كامل.
  - سالب: إزالة إعداد `Integration__HrCoreBaseUrl` من الملف الشبيه بالإنتاج يُفشل AC-ARC-Aa.

### 4.4 قدرات ناقصة (P2)

#### G-ARC-16 — لا مفهوم لاكتمال ملف الموظف
- **الدليل:**
  - `DocumentType` ستة أنواع عامة: شهادة، صورة، بصمة، هوية، جواز، أخرى ([DocumentType.cs:5-19](../src/Shared/HRManagement.SharedKernel/Enums/DocumentType.cs)).
  - لا قائمة مستندات مطلوبة لكل نظام خدمة أو فئة: لا قرار تعيين، ولا لا حكم عليه، ولا شهادة صحية، ولا بيان خدمة.
  - الاكتمال موجود للمرشحين فقط (`CandidateDocumentTypes`)، ومستندات المرشح لا تنتقل إلى ملف الموظف عند التعيين (لا `EmployeeDocuments.Add` خارج `DocumentsController` و`EssPortalController` والبذور).
  - لا تنبيه انتهاء لـ `ExpiryDate`.
  - عنصر الأرشيف غير مربوط بالموظف (G-ARC-02).
- **الأثر:** لا تستطيع الجهة أن تقول أي ملفات ناقصة، وهو أول سؤال في التفتيش وقبل الترفيع والتقاعد.
- **الإصلاح المقترح:**
  - قالب «ملف العامل» لكل نظام خدمة: أنواع مطلوبة، اختيارية، ذات صلاحية.
  - مؤشر اكتمال وقائمة نواقص لكل موظف ووحدة.
  - نقل مستندات المرشح المعتمدة عند التعيين.
  - تنبيهات الانتهاء.
- **التحقق المطلوب:**
  - موجب: موظف بلا شهادة صحية ← يظهر ناقصاً.
  - سالب: رفع مستند منتهٍ يُعلَّم منتهياً.

#### G-ARC-17 — الديوان سجل نصي، لا ديوان
- **الدليل:**
  - `DiwanCorrespondence` بلا حالة ولا إحالة ولا مكلَّف ولا موعد رد ولا مرفق ولا ربط وارد بصادر ولا جهة ([DiwanCorrespondence.cs:7-36](../src/Services/HRCore/HR.Core.Domain/Entities/DiwanCorrespondence.cs)).
  - الرقم يُكتب يدوياً بلا فهرس فريد ([HRCoreDbContext.cs:395-401](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
  - `RegisteredAt` = الآن دائماً، فلا يُسجَّل رقم الكتاب الوارد وتاريخه لدى المرسل ([OrgBranchControllers.cs:396-405](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
  - منتقي الموظف يعرض أول 50 موظفاً أبجدياً (`:382-383, 393`).
  - تسجيل التعيين الآلي يضع مرجعاً `DIWAN-HIRE-{guid}` لا رقم ديوان ([HireCaseCompletedDiwanConsumer.cs:20](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/HireCaseCompletedDiwanConsumer.cs)).
- **الأثر:** لا يمكن تشغيل ديوان جهة حكومية: الترقيم السنوي، والإحالة إلى الشعب، ومتابعة الرد، وصورة الكتاب.
- **الإصلاح المقترح:**
  - ترقيم آلي عبر خدمة الترميز (D-ARC-9) فريد لكل جهة وسنة.
  - حقول المرسل ورقم الكتاب وتاريخه، وإحالة بحالة ومهلة، ومرفق إلزامي للوارد، وربط الرد.
  - أرشفة الكتاب عند الإغلاق.
- **التحقق المطلوب:**
  - موجب: كتاب وارد يُحال ويُرد عليه برقم صادر مربوط.
  - سالب: رقم مكرر في السنة نفسها مرفوض.

#### G-ARC-18 — الوثائق الرسمية: لا قرارات، وترويسة ثابتة، وختم غير مستعمل، ولا تحقق عام
- **الدليل:**
  - 8 مستندات PDF فقط: عرض، محضر لجنة، بطاقة امتحان، نتائج مسابقة، تقرير توظيف، تقرير موظفين، قسيمة، محضر عهدة. ولا PDF لقرار إداري، ولا قرار تعيين أو نقل أو فصل، ولا بيان خدمة.
  - الترويسة ثابتة «شعبة التنظيم والموارد البشرية» لكل الجهات ([HrOfficialDocumentDefaults.cs:5](../src/Shared/HRManagement.Documents.QuestPdf/HrOfficialDocumentDefaults.cs)، [HrOfficialDocumentLayout.cs:18-20](../src/Shared/HRManagement.Documents.QuestPdf/HrOfficialDocumentLayout.cs))، ولا مستدعٍ يمرر `organizationName`.
  - سجل الأختام يُدار ويُبدَّل بدور واحد، ولا تستعمله أي وثيقة ([OfficialSealRecord.cs](../src/Services/IDCard/IDCard.Domain/Entities/OfficialSealRecord.cs)، [IDCardExtendedControllers.cs:80-160](../src/Services/IDCard/IDCard.Web/Controllers/IDCardExtendedControllers.cs)).
  - رمز التحقق في خطاب العرض لا تقابله صفحة تحقق عامة، ولا QR ([OfferLetterDocument.cs:8-11](../src/Services/HRCore/HR.Core.Web/Services/OfferLetterDocument.cs)).
  - ترخيص QuestPDF مضبوط على `Community` ([HrQuestPdfBootstrap.cs:16](../src/Shared/HRManagement.Documents.QuestPdf/HrQuestPdfBootstrap.cs)). أهلية جهة حكومية لهذا الترخيص تحتاج تحققاً قانونياً (D-ARC-7).
- **الأثر:** الجهة تبقى تطبع قراراتها خارج النظام، والوثيقة التي يُصدرها النظام تحمل اسم جهة غير جهتها.
- **الإصلاح المقترح:**
  - ترويسة من بيانات الجهة والوحدة.
  - قوالب للقرار الإداري وبيان الخدمة برمز تحقق وختم مُعتمد بإجراء شخصين.
  - صفحة تحقق عامة برمز.
  - حسم الترخيص.
- **التحقق المطلوب:**
  - موجب: قرار تعيين PDF لجهتين يحمل اسم كل جهة وختمها، والرمز يُتحقق منه من الصفحة العامة.
  - سالب: وثيقة معدّلة تفشل التحقق.

#### G-ARC-19 — قدرات مكتب الأرشيف الحكومي
- **الدليل:**
  - المستعير نص حر لا موظف ([LoanRecord.cs:14-16](../src/Services/RecordsArchive/Records.Archive.Domain/Entities/LoanRecord.cs)).
  - الموقع والصندوق نص حر بلا هرمية (مبنى، غرفة، رف، صندوق).
  - تحديث التأخير بزر POST يدوي ([ArchiveExtendedControllers.cs:127-139](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveExtendedControllers.cs)) بلا تذكير.
  - لا نقل إلى أرشيف مركزي أو تاريخي، ولا مراجعة دورية للمستحق.
  - SLA قائمة التحقق لا يراقبها أحد، و`OwnerRole = "archive"` ليس دوراً موجوداً ([ArchiveRetentionRules.cs:10-20](../src/Services/RecordsArchive/Records.Archive.Domain/Services/ArchiveRetentionRules.cs)).
  - لا بُعد جهة أو مديرية لعناصر الأرشيف: كل الجهة لدور واحد.
- **الأثر:** مكتب أرشيف وزارة بمديريات لا يستطيع تقسيم العمل ولا متابعة الإعارات ولا نقل السجلات المنتهية فعالياً.
- **الإصلاح المقترح:**
  - مواقع هرمية.
  - إعارة لموظف مع تذكير آلي.
  - مهمة مراجعة دورية تُعدّ قائمة المستحق للتصرف دون إتلاف.
  - بُعد `OrgUnitId` مع نطاق.
  - نقل إلى أرشيف تاريخي.
- **التحقق المطلوب:**
  - موجب: إعارة متأخرة تُنبّه المستعير ومسؤول الأرشيف آلياً.
  - سالب: موظف أرشيف مديرية لا يرى عناصر مديرية أخرى.

#### G-ARC-20 — حجب الخلايا الصغيرة مطبّق على التوظيف فقط
- **الدليل:**
  - الطبقة الدلالية تعلن `suppress_small_cells: true` لكل المقاييس ([semantic-layer.json:74-135, 196-199](ana/semantic-layer.json)).
  - التنفيذ موجود فقط في التوظيف (`SmallCellSuppression` في HR.Core و`RecruitmentFactRules`).
  - `headcount` وتصدير القوى العاملة في Dev.Analytics يعيدان أعداداً دقيقة لكل وحدة مهما صغرت ([WorkforcePlanningController.cs:99-108](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/WorkforcePlanningController.cs)).
- **الأثر:** حين تُستعمل التحليلات للتقارير الوطنية أو المفتوحة، يمكن استنتاج حالة فرد (فصل أو إجازة طبية) في وحدة من شخصين.
- **الإصلاح المقترح:** سياسة إفصاح (D-ARC-5) تحدد المخرجات «المنشورة» (التحليلات، الوطني، البيانات المفتوحة) وتطبق `<5` والحجب التكميلي فيها، وتُعفى التقارير التشغيلية لمن يملك الوصول للأسماء.
- **التحقق المطلوب:**
  - موجب: وحدة بموظفَين ← «<5» في المخرج المنشور.
  - سالب: التقرير التشغيلي لمدير الفرع غير متأثر.

#### G-ARC-21 — التقارير والمؤشرات واللوحات في Dev.Analytics بيانات يدوية
- **الدليل:**
  - `Report` سجل وصفي: `GeneratedAt` و`GeneratedBy` و`Url` يُدخلها المستخدم ([ReportsController.cs:42-59](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/ReportsController.cs)).
  - `KPI.CurrentValue` يُكتب يدوياً ([KPI.cs:20-27](../src/Services/DevAnalytics/Dev.Analytics.Domain/Entities/KPI.cs)).
  - `Dashboard.Widgets` نص حر.
  - الحساب الحقيقي الوحيد هو بطاقة التوظيف والعدد (G-ARC-08).
  - البطاقات الوطنية تعدّ ANA-001/REP-001 «لاحقاً» ([07-NATIONAL:506-507](completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md)).
- **الأثر:** لوحة «مؤشرات أداء» يكتب المستخدم قيمها، فلا يُبنى عليها قرار.
- **الإصلاح المقترح:** إما وسم الشاشات «تجريبية» وإخفاؤها في Production حتى ANA-001، أو ربط المؤشرات بمقاييس STR-007 المحسوبة (D-ARC-3).
- **التحقق المطلوب:**
  - موجب: مؤشر محسوب يطابق مصدره.
  - سالب: لا حقل قيمة قابل للتعديل يدوياً.

### 4.5 تحسينات (P3)

#### G-ARC-22 — تصحيحات صغيرة
- **الدليل:**
  - `RequestDisposal` يحفظ ملاحظة الطلب في `DisposeCancelReason` ([ArchiveItemsController.cs:236](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs)).
  - CSV بلا تهريب في جرد الأرشيف وتصدير القوى العاملة ([ArchiveExportJobService.cs:100-101](../src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs)، [AnalyticsExportJobService.cs:132-133](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/ExportJobs/AnalyticsExportJobService.cs)).
  - `ITransactionArchived` يُنشر من مستهلك داخل معاملة Inbox دون Outbox، ولا `AddPostgresOutbox` في Records.Archive ([LeaveAndAdvanceApprovedConsumer.cs:28-36](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs)، [Program.cs:42-44](../src/Services/RecordsArchive/Records.Archive.Web/Program.cs)).
  - أرشفة الفصل مفتاحها الموظف ونوع الفصل، ففصل ثانٍ بعد إعادة تعيين بالنوع نفسه يُتخطى ([SeparationCompletedConsumer.cs:17-24](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/SeparationCompletedConsumer.cs)).
  - تفعيل الموظف عند تسجيل الديوان يعتمد على مطابقة نص `Notes` ([HireCaseCompletedDiwanConsumer.cs:22-31](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/HireCaseCompletedDiwanConsumer.cs)).
  - رمز KPI فريد عالمياً لا لكل جهة ([AnalyticsDbContext.cs:31-34](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Data/AnalyticsDbContext.cs)).
  - فهرس `IdempotencyKey` غير فريد، والتحقق «تحقق ثم إدراج» (`:46-48`)، مع أن Inbox يغطي إعادة التسليم نفسها.
- **الإصلاح المقترح:** إصلاحات موضعية مع اختبار لكل منها.
- **الخطة:** [PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md) (جزئياً: outbox لـ Records.Archive ونشر `ITransactionArchived` عبر outbox المستهلك؛ وبقية البنود «الموجة 2»)

### 4.6 تقاطعات مع الدراسات الأخرى

- → **IAM:** دور `records-archive` واحد لكل الإجراءات: إنشاء، حجز، رفع حجز، اعتماد إتلاف. ولا سياسة دقيقة للأرشيف في البوابة ([HrManagementAuthorizationExtensions.cs:43](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)، [GatewayPathAuthorizationMiddleware.cs:134-194](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
- → **IAM:** الديوان والتقارير الإحصائية بلا نطاق وحدة على القراءة والإنشاء ([OrgBranchControllers.cs:14-67, 338-409](../src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs)).
- → **IAM:** `Documents` يفحص النطاق في `Index` فقط، لا في `Create`/`Edit`/`Delete` ([DocumentsController.cs:75-79, 101, 217, 283](../src/Services/HRCore/HR.Core.Web/Controllers/DocumentsController.cs)). وقائمة السماح [hrcore-post-policy-allowlist.txt:26-29, 33, 47](../tests/architecture/hrcore-post-policy-allowlist.txt).
- → **IAM:** `Signatures/Index` يعرض كل توقيعات الجهة بلا نطاق ([SignaturesController.cs:12-29](../src/Services/HRCore/HR.Core.Web/Controllers/SignaturesController.cs)). و`ExportJobs/{id}` في Dev.Analytics بلا فحص ملكية ([ExportJobsController.cs:26-44](../src/Services/DevAnalytics/Dev.Analytics.Web/Controllers/ExportJobsController.cs)).
- → **OPS:** موقع ملفات التصدير على قرص النسخة مع `minReplicas: 2` ([LocalExportArtifactStore.cs:26-48](../src/Shared/HRManagement.ServiceDefaults/LocalExportArtifactStore.cs)، [hpa.yaml:243](../deploy/k8s/overlays/production-ha/hpa.yaml)). وحالة المصالحة في الذاكرة ([Program.cs:40](../src/Services/DevAnalytics/Dev.Analytics.Web/Program.cs)).
- → **OPS:** لا سر لشهادة التوقيع القانوني في `hr-app-secrets` الإنتاجي، والحفظ والتدوير غير محددين ([SoftwareSignatureService.cs:21-26](../src/Shared/HRManagement.ServiceDefaults/Signatures/SoftwareSignatureService.cs)).
- → **QA:** `Ac36ArabicPdfLiveTests` يكتب `Pass` مع `humanVisualSignOffRequired = true` ([Ac36ArabicPdfLiveTests.cs:45-55](../src/Tests/HRManagement.AcceptanceTests/Ac36ArabicPdfLiveTests.cs)).
- → **QA:** `ReplaceCodeGenerationClientWithFake` في كل اختبارات التكامل يخفي غياب الإعداد الحقيقي ([IntegrationTestHelpers.cs:40](../src/Tests/HRManagement.IntegrationTests/IntegrationTestHelpers.cs)). يلزم اختبار «توصيل الإنتاج» للإعدادات.
- → **WF:** اعتماد الإتلاف ورفع الحجز يتمّان محلياً لا عبر `WorkflowInstanceActionService` ([ArchiveItemsController.cs:186-294](../src/Services/RecordsArchive/Records.Archive.Web/Controllers/ArchiveItemsController.cs)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-ARC-1 | مدد الاحتفاظ وأحداث بدئها لكل فئة سجل (ملف العامل، القرار، الطبي، الشكوى…)، ومن يصدّقها | اعتماد مصفوفة SYR-015 بعد تصديقها من سلطة السجلات والجهة القانونية، وتوحيد الطبي بين GOV-001 وSYR-015. وحتى التصديق: لا تصرّف آلي (عرض المستحق فقط) | ARC-B، ARC-C |
| D-ARC-2 | سلطة اعتماد الإتلاف: لجنة إتلاف (عدد وصفات)، وشاهد، وحاجة لموافقة جهة الوثائق المركزية | لجنة من ثلاثة على الأقل بدور «معتمد سجلات» مستقل عن الطالب، وشاهد للورقي، وشهادة PDF | ARC-C |
| D-ARC-3 | مصدر الحقيقة لعدد الموظفين والفجوة | سحب `headcount`/`gap-analysis` من Dev.Analytics لصالح تقرير HR.Core في v1، وإبقاء Dev.Analytics للمجاميع المنشورة فقط | ARC-E |
| D-ARC-4 | البنية الموثوقة للتوقيع القانوني (شهادة CA وطنية، HSM، أو PFX مؤقت) | PFX من مركز التصديق الحكومي مؤقتاً مع حفظ الشهادات العامة، وHSM قبل الإطلاق الوطني. وحتى ذلك: وسم السجلات «غير موقّعة قانونياً» | ARC-F |
| D-ARC-5 | سياسة الإفصاح الإحصائي: ما المخرجات «المنشورة»، والعتبة | عتبة 5 مع حجب تكميلي على التحليلات والوطني والبيانات المفتوحة، وإعفاء التقارير التشغيلية | ARC-D |
| D-ARC-6 | مخزن ملفات التصدير الخلفي | File.Storage (MinIO) بحاوية مخصصة وقاعدة دورة حياة 24 ساعة | ARC-D (→ OPS) |
| D-ARC-7 | ترخيص QuestPDF للاستعمال الحكومي | تحقق قانوني من شروط Community، وإلا شراء ترخيص مناسب أو بديل | ARC-I |
| D-ARC-8 | ترويسة الوثائق الرسمية والختم: من بيانات الجهة أم قالب لكل جهة، ومن يعتمد صورة الختم | ترويسة من الجهة والوحدة، وختم معتمد بإجراء شخصين، وسجل الأختام في الأرشيف لا IDCard | ARC-I |
| D-ARC-9 | صيغة ترقيم الديوان (سنوي، لكل ديوان أو جهة) | تسلسل سنوي لكل جهة وديوان عبر خدمة الترميز: `{جهة}/{سنة}/{تسلسل}` | ARC-H |
| D-ARC-10 | هل تُؤرشف التقارير الطبية في Records.Archive | لا. يُؤرشف مرجع وتصنيف فقط، ويبقى المحتوى في HR.Health بصلاحية سريرية | ARC-D |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   ARC-A (الأرشفة الآلية + الاسترجاع)
الموجة 2 (P1)   ARC-B (الحجز + الاحتفاظ) ║ ARC-D (الخصوصية + النطاق + التصدير) ║ ARC-F (التوقيع + المستندات) ║ ARC-G (تقارير الرواتب)
الموجة 3 (P1)   ARC-C (الإتلاف + السلامة) ║ ARC-E (صحة التحليلات + المصالحة)
الموجة 4 (P2)   ARC-H (ملف الموظف + الديوان) ║ ARC-I (الوثائق الرسمية + مكتب الأرشيف)
مستمر           ARC-J (القبول الحي + تصحيح التوثيق)
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|---------|---------|----------|-----------|-------------------|
| ARC-A | ربط الأرشفة الآلية والاسترجاع | G-ARC-01، G-ARC-02 | P0 | — | 7–9 |
| ARC-B | الحجز القانوني وربط الاحتفاظ | G-ARC-03، G-ARC-05 | P1 | ARC-A، D-ARC-1 | 7–9 |
| ARC-C | الإتلاف الفعلي وأدلة السلامة | G-ARC-04، G-ARC-06 | P1 | ARC-B، D-ARC-2 | 5–7 |
| ARC-D | الخصوصية ونطاق الفرع والتصدير | G-ARC-07، G-ARC-10، G-ARC-11، G-ARC-20 | P1 | D-ARC-5، D-ARC-6، D-ARC-10 | 7–9 |
| ARC-E | صحة التحليلات والمصالحة مع المصدر | G-ARC-08، G-ARC-09، G-ARC-21 | P1 | D-ARC-3 | 8–10 |
| ARC-F | التوقيع القانوني ومستندات الموظف | G-ARC-13، G-ARC-14 | P1 | D-ARC-4 | 6–8 |
| ARC-G | صحة تقارير الرواتب وتصحيحات صغيرة | G-ARC-12، G-ARC-22 | P1/P3 | — | 4–5 |
| ARC-H | اكتمال ملف الموظف والديوان | G-ARC-16، G-ARC-17 | P2 | ARC-A، ARC-F، D-ARC-9 | 8–10 |
| ARC-I | الوثائق الرسمية وقدرات مكتب الأرشيف | G-ARC-18، G-ARC-19 | P2 | ARC-B، D-ARC-7، D-ARC-8 | 8–10 |
| ARC-J | التحقق الحي وتصحيح التوثيق | G-ARC-15 | P1 → مستمر | كل الخطط | 5–6 |
| **المجموع** | | | | | **≈ 65–83** |

**ARC-A — ربط الأرشفة الآلية والاسترجاع (P0).**
- **النطاق:**
  - `Integration__HrCoreBaseUrl` ونطاق ترميز مخصص لـ `svc-records-archive` في compose وk8s وrealm و`SERVICE_TRUST_INVENTORY`. وفحص جاهزية في Production يفشل دون الإعداد.
  - مستهلكات الأرشفة ترمي عند فشل الترميز (إعادة محاولة ثم طابور أخطاء).
  - حقول `EmployeeId`/`OrgUnitId`/`SourceType`/`SourceId` بفهرس فريد لكل جهة، وترحيل يستخرج المعرّف من العناوين القديمة.
  - قائمة تحقق وحدث حيازة لكل عنصر آلي.
  - HR.Contracts لا ينقل التعيين والفصل إلى `Archived` قبل `ITransactionArchived`.
  - بحث وترقيم في `Index`، ومنتقيات بالبحث، وأزرار الإضافة من صفحة العنصر، وصفحة «الملف الأرشيفي للموظف»، ونقطة تصدير الجرد.
- **القبول:**
  - AC-ARC-Aa: تعيين على الملف الشبيه بالإنتاج ← عنصر مربوط بالموظف بقائمة تحقق وحيازة، ثم `Archived`.
  - AC-ARC-Ab: HR.Core متوقف ← لا ابتلاع، بل طابور أخطاء، والمعاملة ليست `Archived`.
  - AC-ARC-Ac: 500 عنصر ← البحث بالمرجع والموظف يجد الأقدم.
  - AC-ARC-Ad: إرفاق ملف بالعنصر 400.
  - AC-ARC-Ae: تصدير الجرد 202 ثم تنزيل.

**ARC-B — الحجز القانوني وربط الاحتفاظ (P1).**
- **النطاق:**
  - حارس حجز في كل مسارات التعديل والحذف والإعارة والاحتفاظ.
  - الحجز بمرجع، والرفع بمعتمد ثانٍ.
  - نشر الحجز إلى File.Storage، واستهلاك حجز الموظف من HR.Core.
  - كيان فئة سجل لكل جهة مربوط بـ SYR-015، و`RetentionStartAt` من الحدث، وإزالة القيم الثابتة، ومنع تقصير المدة دون الحد.
  - فهارس فريدة لكل جهة.
  - إدخال الأرشيف في تقرير التجربة الجافة لـ GOV-001.
- **القبول:**
  - AC-ARC-Ba: الحذف والتبديل والتقصير والإعارة العامة تحت الحجز مرفوضة.
  - AC-ARC-Bb: حجز الموظف يحجز عناصره وملفاته.
  - AC-ARC-Bc: ملف عامل غير منفصل لا يستحق التصرف، والمنفصل يستحقه بعد المدة المعتمدة.
  - AC-ARC-Bd: رفع الحجز بنفس الشخص مرفوض.

**ARC-C — الإتلاف الفعلي وأدلة السلامة (P1).**
- **النطاق:**
  - فصل الاعتماد (لجنة D-ARC-2) عن التنفيذ: حذف قسري عبر File.Storage للرقمي، وتأكيد بشاهد للورقي.
  - شهادة إتلاف PDF بحقول SYR-015.
  - إلغاء يعيد القابلية.
  - رفض الطالب المجهول، ورمز تزامن.
  - المجموع من File.Storage، والإثبات SHA-256 بالمُثبِت، وفحص سلامة عند الطلب.
- **القبول:**
  - AC-ARC-Ca: دورة إتلاف كاملة تحذف الملف وتنتج شهادة.
  - AC-ARC-Cb: اعتمادان متوازيان ← سجل واحد.
  - AC-ARC-Cc: عنصر بعشرة ملفات ← إثبات 64 حرفاً.
  - AC-ARC-Cd: ملف معدّل في المخزن ← فحص السلامة يفشل.

**ARC-D — الخصوصية ونطاق الفرع والتصدير (P1).**
- **النطاق:**
  - عناوين أرشيف من قوالب بلا نص حر، وترحيل تنظيف.
  - حمولة تحليلات بلا اسم ولا نص شكوى، و`AnalyticsEventsApi` بلا حمولة.
  - مرشّح نطاق موحد لكل Dev.Analytics، ودور الإجمالي الوطني.
  - مخزن تصدير مشترك بصلاحية 24 ساعة ومهمة تنظيف، وفحص `ExpiresAt` في كل خدمة.
  - تهريب CSV.
  - حجب الخلايا الصغيرة في المخرجات المنشورة حسب D-ARC-5.
- **القبول:**
  - AC-ARC-Da: نتائج لجنة طبية لا تظهر في عنوان الأرشيف ولا الجرد.
  - AC-ARC-Db: مستخدم فرع لا يصدّر إجمالي الجهة ولا يقرأ أحداثها.
  - AC-ARC-Dc: نسختان ← 20 تنزيلاً ناجحاً.
  - AC-ARC-Dd: بعد الانتهاء يُرفض التنزيل والملف محذوف.
  - AC-ARC-De: وحدة بموظفَين ← «<5».

**ARC-E — صحة التحليلات والمصالحة مع المصدر (P1).**
- **النطاق:**
  - حسب D-ARC-3: سحب العدّ والفجوة لصالح HR.Core، أو إسقاط حقيقي (جهة في `ISeparationCompleted` v4، واستهلاك النقل والحالة، ووقت الحدث، وتحميل أولي).
  - مهمة مصالحة مجدولة مع HR.Core بتقارير محفوظة في القاعدة.
  - إسقاط لكل نوع نموذج.
  - إزالة كتابة ETL فوق النماذج، ومنفّذ للجداول أو إزالتها.
  - مؤشر حداثة.
  - شاشات التقارير والمؤشرات اليدوية توسم «تجريبية» أو تُخفى في Production.
- **القبول:**
  - AC-ARC-Ea: تعيين ونقل وفصل ← عدّ A وB يطابق HR.Core.
  - AC-ARC-Eb: حدث مفقود ← المهمة المجدولة تكشفه وتصححه، والتشغيل الثاني نظيف.
  - AC-ARC-Ec: إعادة البناء بفرع لا تمس النموذج العام.
  - AC-ARC-Ed: تشغيل ETL لا يغيّر نموذجاً غير هدفه.

**ARC-F — التوقيع القانوني ومستندات الموظف (P1).**
- **النطاق:**
  - رفض الإقلاع في Production دون شهادة (أو وسم صريح حتى D-ARC-4).
  - جدول الشهادات العامة، وتحقق يعيد بناء الحمولة.
  - توثيق المستند بمستخدم ثانٍ مع `VerifiedBy/At`، وسحب بسبب بدل الحذف، واحترام `Employee.LegalHold`.
  - طلب بطاقة ESS ← `CardRequest` في IDCard مع تسجيل مرجع المرفق، وترحيل الصفوف المزيفة.
- **القبول:**
  - AC-ARC-Fa: توقيع ← إعادة تشغيل ← «صالح» من النسخة الأخرى.
  - AC-ARC-Fb: تعديل الحمولة ← «غير صالح».
  - AC-ARC-Fc: الرافع لا يوثّق، والحذف تحت الحجز مرفوض.
  - AC-ARC-Fd: طلب ESS يظهر في IDCard ومرفقه باقٍ بعد 48 ساعة.

**ARC-G — صحة تقارير الرواتب وتصحيحات صغيرة (P1/P3).**
- **النطاق:**
  - الملخص والسجل على المسيّرات النهائية فقط، والسجل الخلفي يحمل `RunId`.
  - تدقيق التصدير الجماعي في كل تقارير الرواتب.
  - بنود G-ARC-22.
- **القبول:**
  - AC-ARC-Ga: مرحَّل ← معكوس ← مصحح، والملخص = المصحح.
  - AC-ARC-Gb: السجل الخلفي = المتزامن سطراً بسطر.
  - AC-ARC-Gc: كل تصدير رواتب له حدث `BulkExport`.

**ARC-H — اكتمال ملف الموظف والديوان (P2).**
- **النطاق:**
  - قالب ملف العامل لكل نظام خدمة، ومؤشر اكتمال وقائمة نواقص، ونقل مستندات المرشح عند التعيين، وتنبيهات الانتهاء.
  - ديوان بترقيم آلي (D-ARC-9)، ورقم الوارد وتاريخه، وإحالة بحالة ومهلة، ومرفقات، وربط الرد، وأرشفة عند الإغلاق.
- **القبول:**
  - AC-ARC-Ha: موظف ينقصه مستند مطلوب يظهر في قائمة النواقص.
  - AC-ARC-Hb: وارد ← إحالة ← صادر مربوط.
  - AC-ARC-Hc: رقم مكرر مرفوض.

**ARC-I — الوثائق الرسمية وقدرات مكتب الأرشيف (P2).**
- **النطاق:**
  - ترويسة من الجهة، وقوالب القرار الإداري وبيان الخدمة، وختم معتمد بإجراء شخصين، وصفحة تحقق عامة، وحسم الترخيص.
  - مواقع هرمية، وإعارة لموظف بتذكير، ومراجعة دورية للمستحق، وبُعد الجهة مع النطاق، ونقل للأرشيف التاريخي.
- **القبول:**
  - AC-ARC-Ia: قرار PDF لجهتين بترويستين وختمين، والرمز يُتحقق منه.
  - AC-ARC-Ib: وثيقة معدّلة ← فشل التحقق.
  - AC-ARC-Ic: إعارة متأخرة ← تنبيه آلي.
  - AC-ARC-Id: مديرية لا ترى أرشيف أخرى.

**ARC-J — التحقق الحي وتصحيح التوثيق (P1 → مستمر).**
- **النطاق:**
  - مجموعة `AcArc*` بـ `[RequiredAcceptance]` على الملف الشبيه بالإنتاج.
  - تشغيل AC-18 بإيقاف مستهلك الأرشيف فعلياً، وAC-19 بعد ARC-E، وAC-36 مع عينات وتوقيع مراجع (لا `Pass` قبله).
  - اختبار «توصيل الإنتاج» يتأكد من وجود الإعدادات الحرجة (`HrCoreBaseUrl`، نطاقات الخدمات، شهادة التوقيع) في `docker-compose.production.yml` وk8s.
  - تصحيح `FUN_006_*` وANA-001 وSYR-015 و`05-TRACEABILITY` لتطابق الشيفرة.
- **القبول:**
  - AC-ARC-Ja: كل مجلد AC-ARC يحوي العناصر الثمانية.
  - AC-ARC-Jb: إزالة إعداد حرج تُفشل اختبار التوصيل في CI.

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين:
- قرار D-ARC-3: السحب أرخص بنحو 4 أيام من الإسقاط الحقيقي.
- قرار D-ARC-4: HSM خارج هذا التقدير.
- حجم ترحيل العناوين القديمة.

---

## 7. معيار «اكتمال الأرشيف والتحليلات والتقارير» المقترح

يُعدّ المجال مكتملاً لإصدار v1 عندما تتحقق البنود الخمسة الآتية:

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-ARC-01…G-ARC-15)، عدا ما يؤجَّل بقرار موثّق (D-ARC-1/2/4).
- [ ] **2. تنجح رحلة أرشيف كاملة بإعدادات الإنتاج** (`docker-compose.acceptance-prodlike.yml`، دون عميل ترميز مزيّف):
  - تعيين ← عنصر أرشيف مربوط بالموظف بفئة `employee-file` وقائمة تحقق وحيازة.
  - البحث عنه بين 500 عنصر.
  - الحجز يمنع الحذف والتبديل والتقصير.
  - الفصل يبدأ عدّ الاحتفاظ.
  - بعد المدة: إتلاف بلجنة يحذف الملف من File.Storage ويُصدر شهادة.
- [ ] **3. أرقام التحليلات تتصالح مع المصدر:**
  - بعد تعيين ونقل وفصل يطابق عدّ كل فرع في Dev.Analytics (أو التقرير البديل حسب D-ARC-3) عدّ HR.Core.
  - مهمة المصالحة المجدولة تكشف حدثاً مفقوداً وتصححه بتقرير محفوظ.
  - ملخص الرواتب لا يجمع المعكوس.
- [ ] **4. لا تسرب ولا تجاوز في المخرجات:**
  - مستخدم الفرع لا يحصل على إجمالي الجهة من أي واجهة أو تصدير.
  - لا نص طبي ولا اسم في عناوين الأرشيف وحمولات التحليلات.
  - ملفات التصدير تُنزَّل من أي نسخة وتُحذف بعد 24 ساعة.
  - المخرجات المنشورة تحجب الخلايا الصغيرة.
- [ ] **5. توجد أدلة `acceptance-evidence/AC-ARC-*` لكل سيناريو موجب وسالب في §6، وAC-18/19/36 منفذة فعلياً** (AC-36 بتوقيع مراجع)، وتُصحَّح `FUN_006_*` وANA-001 وSYR-015 و`05-TRACEABILITY` لتطابق الشيفرة.

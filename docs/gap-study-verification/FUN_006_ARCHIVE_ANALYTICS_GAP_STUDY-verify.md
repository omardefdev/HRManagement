# تحقق مستقل — FUN_006_ARCHIVE_ANALYTICS_GAP_STUDY

**النطاق:** كل فجوات P0 (G-ARC-01..02) وأول ثلاث فجوات P1 (G-ARC-03..05). قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-ARC-01 | P0 | مؤكد | فحصت المفاتيح البديلة الثلاثة التي يقرؤها `AddHrCodeGeneration` (`Integration:HrCoreBaseUrl` و`Services:HRCore:BaseUrl` و`HrCore:BaseUrl`)، ولا يوجد أي منها لـ records-archive في appsettings ولا compose (services/production/acceptance) ولا k8s. نطاق `svc-records-archive` في `sync-keycloak-dev-realm.ps1:527` هو `file-storage.references` فقط. كل المستهلكات تعيد `return` عند `null`. طرف الأرشيف في الـ saga يُقرّ دون عنصر. والاختبار يستبدل العميل بمزيّف. |
| G-ARC-02 | P0 | مؤكد | `Index()` بلا معاملات و`Take(100)`، وكل المنتقيات `Take(50)`، وصفحة التفاصيل بلا روابط إضافة ملف أو فهرس. `ArchiveItem` بلا `EmployeeId`. و`IArchiveExportJobService` مسجَّل ولا يستدعي `EnqueueAsync` أي متحكم (لا متحكم تصدير في Records.Archive.Web). |
| G-ARC-03 | P1 | مؤكد | حذف `DigitalFileRecords` وتعديله بلا فحص حجز. `UpdateRetention` يقبل أي قيمة. `LoanRecords` Create/Edit بلا فحص. لا شيء في الأرشيف يضبط `LegalHold` أو `RetainUntil` في File.Storage، ومهلة GC الافتراضية 24 ساعة (FilePlatformService.cs:924). `LegalHold` و`OfficialRecord` في SharedKernel بلا أي `DbSet`، والسكربت لا يقرأ الأرشيف. |
| G-ARC-04 | P1 | مؤكد جزئياً | الإتلاف الشكلي، وغياب الشهادة، والقفل الأبدي بعد الإلغاء كلها صحيحة: `IsDisposeCancelled=false` في :235 لا يُبلغ لأن `CanRequestDispose` يرفض قبله. وغياب رمز التزامن صحيح. لكن عبارة «بما فيه الطالب نفسه» خاطئة: الطالب الذي لا يحمل معرّف GUID يُقرأ معرّفه `Guid.Empty` عند الاعتماد أيضاً، و`approverId != Guid.Empty` يرفضه. الثغرة هي ضياع هوية الطالب فقط. |
| G-ARC-05 | P1 | مؤكد | 10 سنوات افتراضاً و15 للطبي، والعدّ من `CreatedAt`. `HasValidFilePlan` يقبل أي رمز، وتعديل التصنيف لا يمس `RetentionCode`، والفهرس الفريد على `Code` عام. وأسطر المصفوفة وGOV-001 والبطاقة صحيحة. |

## التعديلات على الدراسة
- G-ARC-04: صُحّح بند فصل المهام (لا اعتماد ذاتي؛ تضيع هوية الطالب فقط)، وأُضيفت ملاحظة التصحيح إلى سطر الأثر.

## ملاحظات جديدة
- **سياق لـ G-ARC-01 (مغطى في COD_ORG وFUN_002):** خلل الترميز نفسه يصيب HR.Contracts. فـ`hr-contracts-web` في compose الإنتاج وفي k8s بلا `Integration__HrCoreBaseUrl`، ونطاقه `file-storage.references` فقط، مع أن compose القبول يضيف الاثنين صراحةً (docker-compose.acceptance.yml:99-105، بتعليق PLAN-29). و`ServiceTransactionReferenceSupport.ApplyReferenceAsync` يرمي عند الفشل، فلا تُنشأ في الإنتاج أي معاملة خدمة: لا طلب إجازة ولا سلفة ولا تعيين ولا انفصال وارد.
- `ArchiveItems/Edit` (ArchiveItemsController.cs:116) يسمح أيضاً بتغيير `ReferenceNumber`، وهو المرجع الذي يولّده COD-001. لا تذكره الدراسة.

# تحقق مستقل — FUN_005_IDENTITY_ASSETS_IT_GAP_STUDY

**النطاق:** كل فجوات P0 (G-IDA-01..04) وأول ثلاث فجوات P1 (G-IDA-05..07). قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-IDA-01 | P0 | مؤكد | الطرف يكتب `IsActive=false` محلياً فقط. الناشر الوحيد لـ `ISeparationCompleted` هو الـ saga بعد اكتمال الأطراف (`PublishSeparationCompletedAsync` معرَّف ولا يُستدعى)، فشبكة الأمان تخرج عند :87 دائماً. `DirectorySync:Enabled=false` غير مُتجاوَز في appsettings.Production ولا compose ولا `deploy/k8s` (configmap فيه `Keycloak__Authority` فقط). لا حذف أدوار، ولا فحص `AuthUser.IsActive` في البوابة أو ServiceDefaults. |
| G-IDA-02 | P0 | مؤكد | لا مفتاح `PolicyEngine` في Admin.Logistics، فالـ shim يطلب المسار لكل أصل مميز أو قيمته فوق 10,000. `PublishAssetAssignmentOutcomeAsync` و`PublishAssetLossOutcomeAsync` بلا `SaveChanges`، والمستدعي الوحيد للأصول هو API `WorkflowInstancesController.Approve`، ولا يحفظ بعد الاستدعاء. وHR.Core يستخدم `AddPostgresOutbox` (Program.cs:262). صندوق العمل بلا النوعين، و`Review(Guid id)` لا يقرأ `workflowInstanceId`. ملاحظة دقة: رابط «عرض الموافقة» (Details.cshtml:290) يظهر في قسم تقرير الفقد فقط. الحسم النهائي: تشغيل لرؤية صف outbox. |
| G-IDA-03 | P0 | مؤكد | `ProcessRequest` بلا `Publish`، والناشران الوحيدان IssuedCardsController.cs:92 و310. `CompleteHireChecklistStepCommand` يُستدعى للفحص الطبي والسيرة فقط، و`MarkIdentityIssuedAsync` بلا مستدعٍ. |
| G-IDA-04 | P0 | مؤكد | النموذج يرسل `deliveredTo` فقط والخادم يشترط إثباتاً. لا أزرار لـ `MarkLost`/`Renew`/`UrgentRevoke`/`MarkExpired` في أي View. `CardRequests/Details.cshtml:20` يرسل إلى `Delete` غير الموجود. |
| G-IDA-05 | P1 | مؤكد | الطرف يضبط `ReturnedAt` دون `Status`، ويشمل العهد `PendingApproval`. شبكة الأمان :71-75 تضع `InStorage` دون استثناء Lost/Disposed. المستهلك المعتمد يفعّل العهدة اعتماداً على `Status` وحده. |
| G-IDA-06 | P1 | مؤكد | تعويض الأطراف الثلاثة يعيد كل التاريخ، ويصطدم بالفهرس الفريد المشروط `ReturnedAt IS NULL`. |
| G-IDA-07 | P1 | مؤكد | إقرارات IDCard وIT في التعيين، وDirectory وLogistics في النقل، فارغة. `EmployeeCreatedConsumer` يسجّل فشل Keycloak تحذيراً ويخرج عند وجود `AuthUser`. `SyncFromKeycloak` يعرض «تمت المزامنة» مع `Disabled()`. و`RetryProvision` يمرر `IssueSummary` اسماً. |

## التعديلات على الدراسة
- لا تعديلات؛ كل الفجوات المفحوصة مؤكدة بأدلتها وأسطرها.

## ملاحظات جديدة
- داخل G-IDA-06: طرف IDCard يحوّل عند الفصل البطاقات `Lost` أيضاً إلى `Revoked + UrgentRevoke` (PartyAckConsumers.cs:18, 23-25)، ثم يعيدها التعويض إلى `Active` (:45). فإلغاء الفصل يُحيي بطاقة كانت مفقودة.

# تحقق مستقل — HCM_002_LEAVE_ATTENDANCE_GAP_STUDY

**النطاق:** كل فجوات P0 (G-LVA-01..03) وأول ثلاث فجوات P1 (G-LVA-04..06). قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-LVA-01 | P0 | مؤكد | كل الأسطر صحيحة. `GetLeaveRequestAsync` متتبَّع، و`xmin` مُعرَّف `IsConcurrencyToken` على Npgsql، والتتبّع الافتراضي غير معطَّل. تعديل `entity.Status` بعد `ExecuteUpdate` يولّد UPDATE بشرط `xmin` القديم فيرمي `DbUpdateConcurrencyException` حتماً وفق دلالات EF Core. لا اختبار PostgreSQL للمعالج (كلها SQLite)، ولا `Reload`/`Detach` بعد الحجز. ما يثبته نهائياً: تشغيل `ApproveLeaveRequestCommand` على Testcontainers PostgreSQL. |
| G-LVA-02 | P0 | مؤكد | `ValidateLeaveBalance` يرفض عند غياب السجل لأي نوع. لا `InitializeYear.cshtml` (المجلد فيه `Create`/`Index` فقط) ولا رابط إليه. لا إنشاء رصيد خارج المتحكم والبذرة (بحثت في `src` و`tools` و`seed-packs` و`scripts`). القائمة `Take(100)` ثم ترشيح في الذاكرة. |
| G-LVA-03 | P0 | مؤكد | لا كيان فترة. `ClosePeriodAndPublishAsync` ينشر فقط. المستهلك الوحيد في الرواتب يتجاهل `AbsentDays<=0` والتكرار. المحمّل يقصر الغياب على فترة التشغيل، والتصحيح يمرر `Array.Empty<AbsenceDeduction>()`. الإدخال الجماعي يكتب كل أيام الشهر. سطرا الوثيقتين صحيحان. |
| G-LVA-04 | P1 | مؤكد جزئياً | المدة التقويمية وغياب التحقق وتحوّل `Emergency` إلى `Other` وغياب شاشة الحالة كلها صحيحة. لكن شاشة ESS لا تعرض «بدون أجر»، فالخصم المالي الزائد لا يقع إلا بطلب POST معدَّل. |
| G-LVA-05 | P1 | مؤكد جزئياً | السياسة والـ SoD الشكلي (`CreatedBy` فارغ في المستهلك، و`ValidateAction` يسمح عند `null`) والرفض بلا سياسة صريحة كلها صحيحة. لكن عبارة «لا علاقة مدير في `OrgUnit`» خاطئة: `OrgUnit.HeadEmployeeId` (رئيس الوحدة) موجود ومؤرخ في `OrgUnitVersion`، وإن كانت الإجازة لا تستعمله. |
| G-LVA-06 | P1 | مؤكد | التداخل مع المعتمد فقط، ولا إعادة فحص عند الاعتماد، ولا قيد في القاعدة. لا ذكر للإجازة في HR.Training ولا HR.Movements. والفصل يقرأ الرصيد فقط. |

## التعديلات على الدراسة
- G-LVA-04: صُحّح بند ESS/بدون أجر (الشاشة لا تعرض النوع، والخطر عبر POST معدَّل)، وأُضيفت ملاحظة التصحيح إلى سطر الأثر.
- G-LVA-05: استُبدلت عبارة «لا علاقة مدير» بذكر `OrgUnit.HeadEmployeeId` (OrgUnit.cs:26-27) غير المستعمل في الاعتماد، وأُضيفت ملاحظة التصحيح إلى سطر الأثر.

## ملاحظات جديدة (خارج الفجوات المكلَّف بها)
- `DailyAttendanceRecordsController.Create` (POST، OrgBranchControllers.cs:126-145) لا يطبّق نطاق الفرع على `vm.EmployeeId`، بخلاف ما يذكره القسم 3 من أن «القائمة والتفاصيل والتعديل والحذف والإغلاق» تطبّقه. لذلك يستطيع مدير الفرع إنشاء سجل حضور لموظف خارج فرعه، ويدخل هذا السجل في إغلاق الفترة وخصم الرواتب.
- **ملاحظة خطيرة تناقض قول الدراسة «يعمل بإعدادات الإنتاج دون تعليق» (القسم 1 و2.1)؛ السبب الجذري موثّق في COD_ORG وFUN_002 دون ربطه بالإجازة:**
  - `CreateLeaveRequestHandler` (LeaveRequestHandlers.cs:91) و`ProcessLeaveRequestedHandler` (InboundEventCommands.cs:269) يستدعيان `ServiceTransactionReferenceSupport.ApplyReferenceAsync`، وهو يرمي `ValidationException` إذا فشل الترميز.
  - `hr-contracts-web` في docker-compose.production.yml:279-292 وفي k8s (`hr-app-config`) بلا `Integration__HrCoreBaseUrl`، ونطاقه `file-storage.references` فقط. أما compose القبول فيضيف المفتاح والنطاق `hr-core.workflow` صراحةً (docker-compose.acceptance.yml:99-105).
  - النتيجة: بإعدادات الإنتاج الحالية لا يُنشأ أي طلب إجازة، لا من HR ولا من ESS، قبل أن يُطرح سؤال الاعتماد (G-LVA-01).
  - يجب أن تتضمن خطة LVA-A إصلاح الإعداد أو أن تعتمد عليه صراحةً.

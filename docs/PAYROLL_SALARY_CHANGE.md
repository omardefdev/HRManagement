# معاملة تغيير الراتب والدرجة (PLAN-53) — العقد

هذه الوثيقة هي العقد المستقر لتغيير الراتب الأساسي أو الدرجة لموظف. **PLAN-54..59 تضيف إليه ولا تعيد تعريفه.**
مصدرها [PLAN-53](acceptance-plans/PLAN-53-iam-payroll-salary-change.md) (القرارات D-IAM-2، D-PAY-6، D-53-1..5).

القاعدة: لا يتغيّر راتب ولا درجة إلا **بطلب** يقدّمه معالج رواتب (`Payroll.Process`) و**يعتمده شخص آخر** يحمل
`Payroll.Approve`، ويدخل ملف الأجر من **مكان واحد** هو المطبِّق، في تاريخ سريانه أو بعده. والقرار الإداري
(عقد، ترفيع، نقل) يُسجَّل في الكيان نفسه مطبَّقاً فوراً.

## 1. الكيان `PayProfileChangeRequest`

`Payroll.Domain/Entities/PayProfileChangeRequest.cs` — جدول `PayProfileChangeRequests` (ترحيل
`PAY053_SalaryChangeRequests`)، بجهة (`TenantId`) وأمان صفوف مفروض، وتزامن `xmin`.

| الحقل | النوع | المعنى |
|-------|-------|--------|
| `EmployeeId` | Guid | الموظف |
| `OrgUnitId` | Guid? | وحدة ملف الأجر الفعّال وقت الطلب؛ **مفتاح نطاق الفرع**. فارغ = مركزي |
| `ChangeKind` | `PayProfileChangeKind` | `BaseSalary` \| `PayGrade` \| `BaseSalaryAndGrade` (يُحسب مما يختلف عن الحالي) |
| `Source` | `PayProfileChangeSource` | `Manual` (النموذج) \| `Api` \| `ContractActivated` \| `Promotion` \| `Transfer` \| `Correction` |
| `SourceReference` | string? (100) | معرّف العقد أو رقم القرار |
| `CurrentProfileId`، `CurrentBaseSalary`، `CurrentPayGradeId` | لقطة وقت الطلب | للعرض والتدقيق |
| `ProposedBaseSalary`، `ProposedPayGradeId` | decimal?، Guid? | المقترح. **يُخزَّن ما يختلف عن الحالي فقط**؛ الباقي ينسخه المطبِّق من النسخة النافذة وقت التطبيق |
| `EffectiveFrom` | DateOnly | تاريخ السريان (D-53-1) |
| `Reason` (500، إلزامي)، `LegalReference` (200) | string | D-PAY-6 |
| `State` | `PayProfileChangeState` | الجدول في §2. يُخزَّن رقماً |
| `RequestedBy`، `RequestedByName`، `RequestedAt` | — | الطالب. للمصدر الآلي: `Guid.Empty` و«النظام» |
| `DecidedBy`، `DecidedByName`، `DecidedAt`، `DecisionNote` | — | المعتمد أو الرافض. فارغة للمصدر الآلي |
| `CancelledBy`، `CancelledByName`، `CancelledAt`، `CancelReason` | — | من ألغى طلباً مفتوحاً. **مفصولة عن القرار** كي يحتفظ الطلب المجدول الملغى بمعتمده (إضافة على جدول الخطة) |
| `AppliedAt`، `AppliedProfileId`، `LastApplyError` | — | نتيجة التطبيق. `LastApplyError` سبب عربي لتعذّر تفعيل طلب مجدول |
| `SupersededByRequestId` | Guid? | التغيير الآلي الذي أبطل هذا الطلب |

كل المبالغ تحمل `[AuditRedact]`: لا تدخل سجل التدقيق، ولا تُكتب في أي سجل (log) مع السبب والمرجع القانوني.

**الفهارس:** `IX_PayProfileChangeRequests_OneOpen` فريد على `(TenantId, EmployeeId)` حين `State IN (0, 1)` (طلب
مفتوح واحد لكل موظف، D-53-3)؛ و`(State, EffectiveFrom)` لمهمة التفعيل؛ و`OrgUnitId` لنطاق القوائم.

## 2. الحالات والانتقالات

`PendingApproval` (0)، `Scheduled` (1)، `Applied` (2)، `Rejected` (3)، `Cancelled` (4)، `Superseded` (5).
المفتوح = `PendingApproval` أو `Scheduled`.

| من | الفعل | من يملكه | إلى | الشروط |
|----|-------|----------|-----|--------|
| — | `Submit` | `Payroll.Process` | `PendingApproval` | النطاق، ملف فعّال، السبب والمرجع، التاريخ (§5)، حدود الدرجة (§6)، لا طلب مفتوح |
| `PendingApproval` | `Approve` | `Payroll.Approve`، وليس الطالب | `Applied` إن كان `EffectiveFrom` ≤ اليوم، وإلا `Scheduled` | إعادة فحص الحدود والملف، ولا تشغيل يحسم الشهر للموظف (§5) |
| `PendingApproval` | `Reject` | `Payroll.Approve`، وليس الطالب | `Rejected` | ملاحظة إلزامية |
| `PendingApproval`، `Scheduled` | `Cancel` | الطالب، أو أي حامل `Payroll.Approve` | `Cancelled` | سبب إلزامي |
| `Scheduled` | تفعيل آلي | `PayProfileChangeActivationJob` (النظام) | `Applied` | شروط الاعتماد نفسها. إن رُفض يبقى `Scheduled` مع `LastApplyError` |
| `PendingApproval`، `Scheduled` | تغيير آلي للموظف نفسه طُبّق | `ApplySystemChangeAsync` | `Superseded` | `SupersededByRequestId` |
| — | تغيير آلي | مستهلك حدث (PLAN-55 وما بعده) | `Applied` مباشرة | `Source` آلي، `DecidedBy` فارغ، القرار الأصلي في `SourceReference` |

- **فصل المهام في المعالج لا في السياسة:** معرّف صاحب القرار يجب أن يختلف عن `RequestedBy`، للاعتماد وللرفض.
  المدير `hr-admin` يحمل السياستين: مديران يطلبان ويعتمدان، ومدير واحد لا يقرر في طلبه.
- **الرفض لا يغيّر شيئاً:** لا الحالة ولا ملف الأجر ولا رسالة في الـ outbox.

## 3. الأوامر والإجابات

`Payroll.Application/Features/PayProfileChanges/`:
`SubmitPayProfileChangeCommand`، `DecidePayProfileChangeCommand` (`Approved`/`Rejected`)،
`CancelPayProfileChangeCommand`. تعيد كلها `PayProfileChangeResult(Success, RequestId, State, Error, Failure)`:
ثلاثية الخطة ومعها نوع الرفض الذي يحدد جواب HTTP. الرسالة عربية دائماً.

| `Failure` | HTTP | متى |
|-----------|------|-----|
| `Invalid` | 400 | قاعدة إدخال: الحدود، التاريخ، السبب، المرجع، لا ملف فعّال، لا تغيير مقترح |
| `Conflict` | 409 | تعارض حالة: طلب مفتوح ثانٍ، قرار متزامن، شهر محسوم، حالة لا تقبل الفعل |
| `SelfDecision` | 403 | «لا يجوز اعتماد الطلب من طالبه» |
| `Forbidden` | 403 | إلغاء طلب غيره دون `Payroll.Approve` |
| `NotFound` | 404 | خارج نطاق الفرع أو الجهة، أو غير موجود |

الجواب واحد في النموذج والـ API والاسم المستعار: النموذج يعيد صفحته بالرسالة العربية وبالحالة نفسها (400/409).

## 4. المطبِّق `IPayProfileChangeApplier`

**المكان الوحيد** الذي ينشئ نسخة ملف أجر لتغيير (`PayProfileWritersTests` يسمّي بقية الكتّاب ومالك كل منهم،
ويفشل عند كاتب جديد). في حفظ واحد:

1. يغلق النسخة السابقة: `IsActive = false`، و`EffectiveTo` = **اليوم السابق** لـ`EffectiveFrom` (لا يوم في نسختين).
2. يحفظ لقطتها في `EmployeePayProfileHistory`.
3. ينشئ النسخة الجديدة بـ**كل** حقول السابقة (الوحدة، النظام، الحساب المصرفي وتحققه…) ثم يطبّق المقترح.
   القواعد في `EmployeePayProfileVersioning`، و`Iam053ApplierCopyForwardTests` يقرأ الكيان بالانعكاس: حقل جديد
   يُنسخ أو يُسمّى في `DeliberatelyNotCopied` وإلا فشل الاختبار.
4. يضع الطلب `Applied` مع `AppliedAt` و`AppliedProfileId`، وينشر `IPayProfileChangeApplied`.

| الدالة | الاستعمال |
|--------|-----------|
| `CheckAsync(request)` | هل يطبَّق الآن؟ قراءة فقط |
| `ApplyAsync(request, approval)` | اعتماد حان موعده (`approval` معبّأ: يسجّل القرار وينشر `Decided` في الحفظ نفسه)، أو طلب مجدول مستحق (`approval = null`) |
| `ApplySystemChangeAsync(employeeId, kind, source, sourceReference, proposedBaseSalary, proposedPayGradeId, effectiveFrom)` | مصدر آلي: يسجّل طلباً `Applied` فوراً، ويبطل الطلب المفتوح للموظف |

**للمصادر الآلية:** لا تُفحص حدود الدرجة ولا قاعدة أول الشهر ولا الشهر المحسوم (التشغيل المجمّد يقرأ لقطته،
`Pay023FrozenRunIsolationTests`). يبقى شرطان: ملف فعّال، وألا تبدأ النسخة قبل النسخة التي تستبدلها.
`kind` يحدد أي جزء من المقترح يُعتدّ به. **لا يستدعيها أي مستهلك في PLAN-53:** ربط `HandleContractActivatedHandler`
و`HandleEmployeeCreatedHandler` في PLAN-55، و`HandleTransferExecutedHandler` في PAY-G؛ وعند ربط كل واحد يُحذف
اسمه من `PayProfileWritersTests.AllowedWriters`.

## 5. التواريخ والشهور (D-53-1)

- «اليوم» و«الشهر الجاري» **بتوقيت دمشق** (`Localization:TimeZone`) من `TimeProvider` محقون، عبر `IPayrollCalendar`.
  لا يُستعمل `DateTime.UtcNow.Date`: بعد 21:00 UTC في آخر يوم من الشهر يكون الشهر التالي قد بدأ في دمشق.
- `EffectiveFrom` تاريخ تقويمي (`DateOnly`). يُخزَّن في ملف الأجر عند 00:00 UTC من اليوم نفسه بلا إزاحة.
- **عند الطلب:** أول يوم من شهر (`RequireMonthStart`)، وليس قبل الشهر الجاري، وليس قبل بداية النسخة الحالية.
- **عند الاعتماد والتفعيل — «شهر فيه تشغيل مجمّد أو معتمد أو مرحّل»** يعني تشغيلاً:
  - فترته تحوي `EffectiveFrom`، وليس `Reversed` ولا `Cancelled`؛
  - **ويشمل هذا الموظف:** التشغيل الاعتيادي (`Regular`) يشمل كل موظف، وغيره يشمل الموظفين الذين لهم سطر راتب فيه؛
  - وهو مجمّد (`ResultsFrozen`) أو `Approved` أو `Posted`، أو اعتيادي قيد الحساب أو التحقق.

  السبب: التغيير لا يناقض إلا نتيجة محسومة تضم هذا الموظف. والرفض 409 برسالة عربية تذكر الشهر، والحالة باقية.

## 6. حدود الدرجة (D-53-2)

الراتب الناتج مقابل حدود الدرجة الناتجة (المقترحة وإلا الحالية)، بقاعدة مستهلك العقد
(`salary < min || salary > max`)، والحد غير المضبوط (صفر) لا يُطبَّق. الرسالة تذكر الحدين. تُفحص عند الطلب، وعند
الاعتماد، وعند التفعيل. `AllowOutOfGradeBounds=true` يعطّلها.

## 7. الأحداث

`src/Shared/HRManagement.MessageContracts/Events/`، `SchemaVersion = 1`، تُنشر عبر الـ outbox قبل الحفظ الواحد
(نمط `SingleSave`، صفوفها في `EVENT_PUBLISHER_INVENTORY.md` و`MESSAGE_EVENT_MATRIX.md`). لا مستهلك لها بعد.

| الحدث | الحقول | متى |
|-------|--------|-----|
| `IPayProfileChangeSubmitted` | `RequestId`، `EmployeeId`، `ChangeKind`، `Source`، `EffectiveFrom`، `RequestedBy`، `OccurredAt` — **بلا مبالغ** | `Submit` |
| `IPayProfileChangeDecided` | `RequestId`، `EmployeeId`، `Decision` (`Approved`\|`Rejected`)، `ResultingState` (`Applied`\|`Scheduled`\|`Rejected`)، `DecidedBy`، `OccurredAt` | `Approve`/`Reject` |
| `IPayProfileChangeApplied` | `RequestId`، `EmployeeId`، `NewProfileId`، `PreviousProfileId`، `EffectiveFrom`، `Source`، `SourceReference`، `PayGradeCode`، `BaseSalary`، `PreviousBaseSalary`، `OccurredAt` | كل تطبيق: اعتماد حان موعده، تفعيل مجدول، مصدر آلي |

الإلغاء والإبطال لا ينشران حدثاً. التعدادات تُرسل نصاً باسمها.

## 8. مهمة التفعيل

`PayProfileChangeActivationJob` (`Payroll.Infrastructure/Services`):

- دورة كل `Payroll:SalaryChange:Activation:IntervalMinutes` (60 افتراضياً) بعد دقيقة من الإقلاع. **انحراف عن الخطة:**
  `ActivationCron` صار فاصلاً زمنياً لأن المستودع بلا مكتبة cron.
- نسخة واحدة تنفذ الدورة: قفل استشاري `AdvisoryLockKeys.PayProfileChangeActivation = 4_001_013`.
- الجهات تُعرف من سجل التدقيق (خارج أمان الصفوف، وصف الطلب يُكتب فيه في حفظ الطلب نفسه)، وكل جهة في نطاقها
  وتحت جهتها (`TenantIdFlow`)؛ فشل جهة يُسجَّل ولا يحجب غيرها.
- كل طلب `Scheduled` حان موعده يمر بالمطبِّق بحفظه الخاص. المرفوض يبقى `Scheduled` مع `LastApplyError`، ويعدّه
  المقياس `payroll_salary_change_apply_blocked` (العدّاد `HRManagement.PayrollSalaryChange`)، ويظهر في القائمة واللوحة.
- متكرر بأمان: المطبَّق لم يعد مجدولاً، و`xmin` يرفض تطبيقاً ثانياً متزامناً.
- `POST api/payroll/pay-profile-changes/activate-due` (`Payroll.Approve`، مركزي فقط) يشغّل الشيفرة نفسها لجهة المنادي.

## 9. الواجهة البرمجية والشاشات

| المسار | السياسة | الجواب |
|--------|---------|--------|
| `GET api/payroll/pay-profile-changes?state=&employeeId=` | `Module.Payroll` | قائمة ضمن النطاق |
| `GET api/payroll/pay-profile-changes/{id}` | `Module.Payroll` | 200، أو 404 خارج النطاق |
| `POST api/payroll/pay-profile-changes` | `Payroll.Process` | 201 مع `requestId` و`state` |
| `POST .../{id}/approve` \| `reject` | `Payroll.Approve` | 200 مع `state` |
| `POST .../{id}/cancel` | `Payroll.ChangeCancel` | 200 |
| `POST .../activate-due` | `Payroll.Approve` | `{ due, applied, blocked, blockedTotal }` |
| `PUT api/payroll/employees/{employeeId}/pay-profile` | `Payroll.Process` | **اسم مستعار لـ`Submit`: 202 مع `requestId` و`state`** |

**تغيير لعملاء الـ API (مسار Tier-0):** `PUT .../pay-profile` لم يعد يكتب ملف الأجر ولا يعيد `profileId`؛ يقدّم
طلباً بمصدر `Api` ويعيد 202. تحويل الجسم القديم: `changeReason` ← السبب، `effectiveFrom` ← تاريخه التقويمي،
`effectiveTo` و`isActive` يُتجاهلان، و`legalReference` حقل اختياري جديد (إلزامي ما دام `RequireLegalReference=true`).

الشاشات: «طلبات تغيير الراتب» (`PayProfileChangesController`: قائمة بتصفية الحالة والموظف، والمجدول المتعذّر تطبيقه
بارز؛ تفاصيل تقارن الحالي بالمقترح مع سجل القرارات)، ونموذج الطلب (`EmployeePayProfiles/Create` بنموذج العرض
`PayProfileChangeRequestViewModel`)، وعدّادا اللوحة (بانتظار الاعتماد، وتعذّر التطبيق) ضمن نطاق المنادي.

## 10. نطاق الفرع

`PayrollOrgScopeKeys` (PLAN-52): الطلب مفتاحه `OrgUnitId` المنسوخ؛ والموظف (`employeeId`) مفتاحه وحدة **ملف
أجره الفعّال** (لا لقطة موظفين في الرواتب). خارج النطاق بالمعرّف = 404 قبل تنفيذ الإجراء، والقوائم تُصفّى، والمعالج
يعيد الفحص. موظف بلا ملف فعّال أو ملف بلا وحدة = مركزي لا يصل إليه مستخدم مقيَّد.

## 11. الإعداد

| المفتاح | الافتراضي | المعنى |
|---------|-----------|--------|
| `Payroll:SalaryChange:RequireMonthStart` | `true` | D-53-1 |
| `Payroll:SalaryChange:AllowOutOfGradeBounds` | `false` | D-53-2 |
| `Payroll:SalaryChange:RequireLegalReference` | `true` | D-PAY-6 |
| `Payroll:SalaryChange:Activation:Enabled` | `true` | مهمة التفعيل |
| `Payroll:SalaryChange:Activation:IntervalMinutes` | `60` | فاصل الدورة |
| `Authorization:Policies:Payroll.MasterData:Roles` | `hr-admin`، `payroll-processor` | أدوار البيانات المرجعية |

القيم في `appsettings.json` و`Production` و`Staging` لخدمة الرواتب، ومرآتها في `docker-compose.acceptance-prodlike.yml`.

## 12. السلالم والدرجات بطلب (D-53-4)

`SalaryScales` و`PayGrades` لا تُكتب في مكانها. الإنشاء والتعديل والحذف **طلب** (`PayrollReferenceChangeRequest`،
جدول `PayrollReferenceChangeRequests` بأمان صفوف و`xmin`) يقدّمه حامل `Payroll.MasterData` **بسبب ومرجع قانوني
إلزاميين**، ويعتمده حامل `Payroll.Approve` **ليس الطالب**؛ والتغيير يُطبَّق داخل حفظ الاعتماد نفسه.

| الحقل | المعنى |
|-------|--------|
| `EntityType` | `SalaryScale` \| `PayGrade` |
| `EntityId` | السجل المعدَّل أو المحذوف؛ فارغ للإنشاء حتى يُطبَّق |
| `EntityLabel` | كود السجل واسمه وقت الطلب (للقوائم) |
| `Operation` | `Create` \| `Update` \| `Delete` |
| `ProposedJson`، `CurrentJson` | الحقول المسموحة فقط (`PayGradeChange`، `SalaryScaleChange`)، لا كيان مربوط |
| `Reason`، `LegalReference` | إلزاميان |
| `State` | `PendingApproval` (0) \| `Applied` (1) \| `Rejected` (2) \| `Cancelled` (3) |
| الطالب، صاحب القرار، الملغي، `AppliedAt` | كما في طلب تغيير الراتب |

- **الأوامر** (`Features/ReferenceChanges/`): `SubmitPayrollReferenceChangeCommand`، `DecidePayrollReferenceChangeCommand`،
  `CancelPayrollReferenceChangeCommand`؛ والإجابات بأنواع الرفض نفسها (400، 409، 403، 404).
- **التحقق عند الطلب وعند الاعتماد:** الحد الأدنى ≤ الأساسي ≤ الحد الأعلى، الجدول موجود، الكود فريد (تعارض الكود 409
  برسالة عربية، لا 500)، وطلب مفتوح واحد لكل سجل (`IX_PayrollReferenceChangeRequests_OneOpen`).
- **لا حذف لمستعمَل:** درجة يسمّيها ملف أجر (فعّال أو مغلق) أو طلب تغيير راتب مفتوح، أو جدول فيه درجات ← 409 برسالة،
  والسجل باقٍ. والترحيل حوّل العلاقتين إلى `Restrict` (كانتا `SetNull` على ملف الأجر و`Cascade` على درجات الجدول).
- **تعديل الحدود لا يغيّر ملف أجر.** الملف الخارج عن الحدود الجديدة يُعرض ولا يُعدَّل: تصفية «خارج حدود الدرجة» في شاشة
  ملفات الأجر، وعدّاد في تفاصيل الدرجة، وعدّاد في صفحة الطلب قبل اعتماده. **الخطة افترضت «تقرير استثناءات قائماً» وهو
  غير موجود في الرواتب؛ هذه التصفية هي العرض.** ويصحَّح الملف بطلب تغيير راتب تحكمه الحدود الجديدة.
- **HR.Core** يقرأ حدود الدرجة من الجدول نفسه (`PayGradeLookupInternalController`): لا يرى إلا القيم المعتمدة.
- **الشاشات:** «طلبات البيانات المرجعية» (`PayrollReferenceChangesController`): قائمة، وتفاصيل بجدول قبل/بعد بأسماء
  الحقول العربية (الـ JSON تحت «عرض تقني» فقط)، واعتماد ورفض (ملاحظة إلزامية) وإلغاء. وصفحة تفاصيل لكل جدول ودرجة
  تعرض الطلب المفتوح عليه.
- **`Payroll:MasterData:RequireApproval`** = `true`. القيمة `false` تُحترم في Development فقط (يُطبَّق الطلب عند تقديمه
  بالشيفرة نفسها، بلا صاحب قرار)؛ وفي غيرها تُتجاهل مع تحذير عند الإقلاع.
- `CompensationTypes` و`DeductionTypes` بسياسة `Payroll.MasterData` فقط، بلا طلب (الخصومات النظامية إلى PLAN-54).
- الكاتب الوحيد للسلالم والدرجات هو `PayrollReferenceChangeApplier` والبذرة (`PayrollReferenceWritersTests`).

## 13. حدود معروفة

- **شهر يُحسب بعد أن طُبّق تغيير شهر لاحق يقرأ النسخة الأحدث.** التشغيل يأخذ الملف الفعّال وحده
  (`PayrollRunJobHandlers.cs`) ويقسّط بتواريخه، والمحرّك لا يقسّط إلا حين تكون أيام الموظف بين الصفر وأيام الفترة
  (`PayrollRules.cs`). فتشرين الأول إذا حُسب في 2 تشرين الثاني، بعد تطبيق تغيير يسري من 1 تشرين الثاني، يقرأ النسخة
  الجديدة التي لا يوم لها في تشرين الأول، فيُدفع **بالراتب الجديد كاملاً**. PLAN-53 لا يغيّر محرك التشغيل
  (القاعدة 2 في البند 2 من الخطة). **عيب مُحال إلى PLAN-56** (اختيار النسخة النافذة في فترة التشغيل).
- لا أثر رجعي ولا تقسيط داخل الشهر (PAY-H، الموجة 2).
- المستهلكات ما زالت تكتب ملف الأجر مباشرة (§4)، ويوم الحدّ عند تفعيل العقد يُحسب مرتين حتى PLAN-55.

## 14. كيف تمتد الخطط التالية

- **PLAN-54** (المدخلات اليدوية): النمط نفسه — أمر طلب وأمر قرار، والمعتمد ليس الطالب — بسياسات `Payroll.Process`
  و`Payroll.Approve` و`Payroll.MasterData` القائمة. لا يضيف حالات إلى هذا الكيان.
- **PLAN-55**: ينقل مستهلكَي العقد وإنشاء الموظف إلى `ApplySystemChangeAsync` (ويحذف اسميهما من قائمة الكتّاب)،
  ويضيف كيان الحساب المصرفي بصانع ومدقق؛ الحساب ليس `ChangeKind` هنا.
- **PLAN-56**: آلة حالة التشغيل؛ يقرأ `FindBlockingRunAsync` حالة التشغيل كما هي، ويصلح الحدّ المعروف أعلاه.
- **PAY-G / PAY-H / WF-F** (الموجة 2): النقل والترفيع، الأثر الرجعي، تفويض `payroll-approver`.
- الإضافة المسموحة: حقول جديدة، مصادر جديدة في `PayProfileChangeSource`، أحداث جديدة. **الممنوع:** تغيير معنى حالة
  أو انتقال، أو كاتب ثانٍ لنسخ ملف الأجر.

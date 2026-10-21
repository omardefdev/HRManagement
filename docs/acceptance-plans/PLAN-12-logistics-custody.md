# PLAN-12 — عهدة اللوجستيات: نطاق الفرع + الفقد والتسوية (AC-32, AC-33)

**التغطية:** AC-32, AC-33. **الطبقة:** 4. **يعتمد على:** PLAN-00، PLAN-01.

## الهدف

إثبات أن إسناد عهدة في فرع لا يقبل التعديل من فرع شقيق (يُسمح للفرع المالك والمركز فقط)، وأن تسجيل فقد أصل وتسوية العهدة يطبّق مسار الاعتماد ويحفظ الأدلة والحالة السابقة واللاحقة.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: مسار العهدة في [AssetsController.cs](../../src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs) بحالات `PendingApproval/Active/Returned/Rejected` ([AssetAssignmentStatus.cs](../../src/Services/AdminLogistics/Admin.Logistics.Domain/Enums/AssetAssignmentStatus.cs))؛ الموافقة عبر السياسة/سير العمل؛ الأدلة (`EvidenceRef`/`HandoverFileId`)؛ مسار الفقد `AssetLossReport` + `AssetLostRules` + `AssetLossWorkflowConsumers`.
- فجوة **AC-32 (معلنة صراحةً في الأطروحة CAP-ORG-01/CAP-LOG-01):** `Admin.Logistics` **بلا أي حارس نطاق** — صفر استخدام لـ`ForbidIfEmployeeOutOfScopeAsync`/`WhereInOrgScope`/`OrgScopeResolver`. الإجراءات المكشوفة: `AssignToEmployee`, `ReturnAssignment`, `MarkLost`, `RecoverAsset`, `Details`, `Edit`. (اختبار BOLA المعماري يتخطى `[HttpPost]` فلا يكشفها.) و`Asset` بلا `OrgUnitId` مالك — يُشتق النطاق من وحدة الموظف المُسنَد إليه.
- فجوة **AC-33:** الحالة "قبل/بعد" ملاحظات لا لقطة منظّمة.

## تغييرات التنفيذ (مهام)

1. **AC-32:** إضافة حارس النطاق على إجراءات العهدة في `AssetsController` باستخدام `EmployeeOrgScopeGuard`/`OrgScopeResolver` (يُسمح للفرع المالك والمركز، ويُمنع الشقيق)؛ اختبار منع الفرع الشقيق للوجستيات (غير موجود حالياً — الاعتماد على `Fun005*` فقط).
2. **AC-33:** مسار فقد+تسوية عبر الموافقة مع أدلة وحالة قبل/بعد (لقطة مبنية أو ملاحظات منظّمة).

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-32 | الفرع المالك والمركز يعدّلان العهدة | محاولة الفرع الشقيق ⇒ 403/منع |
| AC-33 | فقد + تسوية ⇒ مسار اعتماد + حفظ الأدلة والحالة السابقة/اللاحقة | لا تسوية بلا اعتماد/أدلة |

## منهج الاختبار

مجموعة `Ac32..Ac33`؛ إعادة استخدام `Fun005IdentityAssetsItTests`, `AssetLostRulesTests`, وE2E `Fun005IdentityAssetsItE2ETests`، مع اختبار نطاق شقيق جديد للوجستيات.

## مخرجات الأدلة

`AC-32/`: محاولة الفرع الشقيق (403) + نجاح المالك/المركز + سجل الرفض. `AC-33/`: بلاغ الفقد + اعتماد + حالة الأصل/العهدة قبل/بعد + الأدلة.

## تعريف الإنجاز

حارس النطاق يمنع الشقيق ويسمح للمالك/المركز؛ الفقد يتبع الاعتماد ويحفظ الأدلة والحالة؛ AC-32/AC-33 = `Pass`. (إغلاق هذه الفجوة يرفع CAP-ORG-01/CAP-LOG-01 نحو "متحقق".)

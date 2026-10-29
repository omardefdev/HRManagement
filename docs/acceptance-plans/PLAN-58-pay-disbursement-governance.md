# PLAN-58 — حوكمة الصرف: مسار دفع واحد، أربع عيون حقيقية، وحجز على مستوى الموظف (PAY-F)

**Scope:** PAY — [دراسة الفجوات](../PAY_001_GAP_STUDY.md): G-PAY-08، G-PAY-09 (عدا جزء الإطار المنفّذ في PLAN-54)، G-PAY-15، وملاحظة المحقق عن تعارض الأدوار، والتسجيل اليدوي لتأكيد القناة من G-PAY-16 (تحتاجه PLAN-64)  
**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-56 (الحراس، `PostedBy`، إبطال الدفعة)، PLAN-55 (الحساب المثبّت على السطر). ويستهلك: PLAN-54 (الشخصية `a-payroll-approver-2`، وقواعد أولوية الصرف داخل الإطار)، PLAN-57 (إيصالات الحضور لقاعدة FR-01)، PLAN-40 (النشر الذري)  
**Related cards:** PAY-001، PAY-004، EXT-003، FRD-001، NAT-003، IAM-003، SYR-007

## Goal

- أن يخرج المال من مسار واحد فقط: ترحيل ← دفعة وأوامر ← قرار تمويل (إن لزم) ← معالجة بيد المُرحِّل وفحص احتيال وبوابة لكل موظف ← ملف مصرفي يُولَّد من الأوامر مرة واحدة بمجاميع ضبط.
- أن يكون كل «أربع عيون» فعلين لشخصين من جلستين، لا معرّفاً يُكتب في نموذج.
- أن يُطلب اعتمادان مختلفان فوق العتبة، وأن يراجع التشغيلَ دورٌ مستقل قبل الترحيل.
- ألا يمرّ مستخدم يحمل دورين متعارضين في رمزه إلى أي فعل مالي.
- ألا يؤخر موظف واحد محجوز أو بلا نظام خدمة رواتبَ الجهة كلها، وأن تعمل قواعد الاحتيال على بيانات حقيقية.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-PAY-2 — صرف قبل الترحيل | لا. قرار التمويل يُتخذ على دفعة `Pending` بعد الترحيل فقط، والتنفيذ (`process`) بيد المُرحِّل | يتغير سلوك AC-24 ويُعاد كتابته |
| D-PAY-3 — من يراجع قبل الترحيل | حامل الدور `auditor` (`Payroll:Audit:ReviewerRoles`، الافتراضي `auditor`)، وليس منشئ التشغيل ولا معتمده ولا مُرحِّله. و`Payroll:Audit:Required=true`. والإلغاء بقرار الجهة يكون بضبطه `false` مع سطر في سجل التشغيل | يصل `auditor` إلى شاشات مراجعة الرواتب للقراءة والإغلاق فقط |
| D-PAY-4 — نطاق الحجز وحاجز نظام الخدمة | الموظف (`Payroll:Blockers:Mode=HoldEmployee`): يُحسب سطره ويُحجز أمره، ويُصرف للباقين. والخيار `BlockRun` (السلوك الحالي) إعداد للجهة | لا يوقف موظف واحد الدفعة |
| D-PAY-5 — عتبة الاعتماد المزدوج | `Payroll:Approval:DualApprovalThreshold` = 50,000,000 ([SodMatrixDefaults.cs:9](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs))، وفوقها معتمدان مختلفان من حملة `Payroll.Approve`. سلسلة «مدير مالي ثم آمر صرف» بأدوار مسماة في الموجة 2 (WF) | يلزم معتمدا رواتب، وهو شرط PLAN-54 أصلاً |
| D-PAY-19 — إعادة إصدار الملف المصرفي | ممنوعة إلا بطلب بسبب يعتمده حامل `Payroll.Approve` غير الطالب. والملف الجديد يحمل رقم تسلسل جديداً ويُبطل السابق | لا ملفان صالحان للدفعة نفسها |
| D-PAY-20 — حامل دورين متعارضين في الرمز | يُرفض في كل فعل مالي (`Payroll:Sod:RejectConflictingRoleHolders=true`)، بأزواج التعارض المعرّفة في [SodMatrixDefaults.cs:165-199](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs) | مستخدم يجمع المعالج والمعتمد لا يعالج ولا يعتمد حتى يُصحَّح إسناده |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-58-pay-disbursement-governance` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay001\|FullyQualifiedName~Pay004\|FullyQualifiedName~Frd001\|FullyQualifiedName~Ext003\|FullyQualifiedName~SeparationOfDuties"` | أخضر أو قائمة في `reports/plan58-baseline.md` |
| 0.3 | جرد مسارات المال | كل `new PaymentBatch`، وكل استدعاء لـ `EnsureAndSubmitOrdersAsync`، وكل نقطة تُخرج ملفاً مصرفياً (`ExportTreasuryFile`، `ExportExternalIntegrationBundle`، `PayrollExportJobService`) | الجدول في التقرير. كل بند إما يُحذف أو يُوجَّه إلى المسار الواحد |
| 0.4 | جرد «المعتمد من الطلب» | كل معامل أو حقل نموذج يحمل معرّف «معتمد» أو «مفرج» من العميل في Payroll | الجدول في التقرير |
| 0.5 | جرد الشخصيات | الأدوار الفعلية في `testdata/acceptance/personas.json` وأي جمع متعارض | لا شخصية متعارضة عدا الشخصية الاختبارية المضافة هنا |

---

## In scope

### 1. مسار دفع واحد بعد الترحيل (G-PAY-08)

**المشكلة:**
- الصرف الجزئي يقبل تشغيلاً `Approved` ([ExecutePartialDisbursementCommand.cs:73-74](../../src/Services/Payroll/Payroll.Application/Features/PartialDisbursement/ExecutePartialDisbursementCommand.cs))، وينشئ دفعة ([PartialDisbursementService.cs:134-145](../../src/Services/Payroll/Payroll.Application/Services/PartialDisbursementService.cs))، ويرسل الأوامر فوراً ([ExecutePartialDisbursementCommand.cs:108-118](../../src/Services/Payroll/Payroll.Application/Features/PartialDisbursement/ExecutePartialDisbursementCommand.cs) ← [PaymentOrderCoordinator.cs:12-28](../../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderCoordinator.cs)).
- يتم ذلك بلا سجل مراجعة، ولا فصل المُرحِّل، ولا فحص احتيال، ولا بوابة. هذه كلها في `ProcessPaymentBatch` وحده ([PayrollRunJobHandlers.cs:355-419](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
- قرار التمويل يُتخذ على تشغيل `Approved` ([CreateFundingDecisionCommand.cs:28-29](../../src/Services/Payroll/Payroll.Application/Features/PartialDisbursement/CreateFundingDecisionCommand.cs)).
- اختبار PAY-001 المعماري يفحص المتحكمات و`PayrollRunHandlers.cs` فقط ([Pay001ApprovalBypassTests.cs:26-56](../../src/Tests/HRManagement.UnitTests/Pay001ApprovalBypassTests.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| الترحيل ينشئ الأوامر | `PostPayrollRunHandler` | الدفعة `Pending` + أمر `Draft` لكل سطر بالحساب المثبّت (PLAN-55)، في معاملة الترحيل نفسها. والسطر بلا حساب ← أمر `Held` بسبب `NO-ACCOUNT` |
| قرار التمويل | `CreateFundingDecisionCommand` | على دفعة `Pending` لتشغيل `Posted` فقط. يعيد توزيع مبالغ الأوامر بقواعد الأولوية من نسخة الإطار المثبتة (PLAN-54)، ويقيّد الباقي في `ArrearsLedgerEntry` كما اليوم. صاحب `Payroll.Approve` |
| `ExecutePartialDisbursementCommand` و`PartialDisbursementService.ExecuteAsync` | — | يُحذفان. ونقطة `partial-disburse` ([PayrollApiController.cs:198-230](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)) تُحذف، فالصرف الجزئي = قرار تمويل ثم `process` |
| `ProcessPaymentBatchHandler` | [PayrollRunJobHandlers.cs:355-419](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs) | المنفذ ≠ المنشئ ≠ كل معتمد ≠ مقرر التمويل. فحص الاحتيال، ثم البوابة لكل موظف (البند 6): أوامر المحجوزين `Held` والباقي `Submitted`. ثم الملف (البند 2) |
| `submit-held` | `POST api/payroll/payment-batches/{id}/submit-held` (`Payroll.Post`) | يرسل الأوامر التي رُفع حجزها بعد إرسال الدفعة، بملف تكميلي (البند 2). هكذا يُصرف للمحجوز بعد الإفراج دون دفعة جديدة |
| `PaymentBatchIssuer` | `Payroll.Application/Services/PaymentBatchIssuer.cs` | النقطة الوحيدة التي تنشئ `PaymentBatch` وأوامرها، بمصدر صريح: `PayrollRunPosted` هنا، ونوع «تسوية نهائية» تضيفه PLAN-64 بالواجهة نفسها. كل دفعة أياً كان مصدرها تمر بـ `process` والبوابة والملف |
| اختبار معماري | `Pay058SinglePaymentPathArchitectureTests` (يوسّع `Pay001ApprovalBypassTests`) | لا `new PaymentBatch` خارج `PaymentBatchIssuer`، ولا استدعاء له إلا من `PostPayrollRunHandler` (ومن معالج PLAN-64 بعد دمجها). ولا `EnsureAndSubmitOrdersAsync` خارج `ProcessPaymentBatchHandler` و`submit-held` |

**القواعد:**
1. لا أمر `Submitted` لتشغيل غير `Posted`، ولا أمر يُرسل دون فحص احتيال وبوابة في الطلب نفسه.
2. مجموع الأوامر غير المحجوزة + المحجوزة + المتأخرات = `TotalNetPay` للتشغيل. ويُفحص ذلك عند كل انتقال للدفعة.

### 2. ملف مصرفي من الأوامر، مرة واحدة، بمجاميع ضبط (G-PAY-08)

**المشكلة:** `ExportTreasuryFile` طلب GET ([PayrollReportsController.cs:245-270](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)) يصدّر صافي كل سطر كاملاً ([PayrollExternalExportService.cs:59-82](../../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)). يتجاهل الحجز والتمويل وحالة الدفعة، ويُعاد توليده بلا حد.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PaymentBatchFile` | `Payroll.Domain/Entities/PaymentBatchFile.cs` | `PaymentBatchId`، `Sequence`، `Kind` (`Initial`\|`HeldRelease`\|`Reissue`)، `ControlCount`، `ControlTotal`، `Sha256`، `Content` (bytea)، `GeneratedBy`، `GeneratedAt`، `SupersededById`، `ReissueReason`، `ReissueApprovedBy`. فريد على `(PaymentBatchId, Sequence)` |
| `PaymentBatchFileBuilder` | `Payroll.Infrastructure/Integration/PaymentBatchFileBuilder.cs` | يبني CSV الحالي (D-PAY-9) من الأوامر `Submitted` في هذا الإرسال فقط، بالحساب المثبّت، وسطر ختامي بالعدد والمجموع |
| التوليد | داخل `process` و`submit-held` | ملف واحد لكل إرسال، في المعاملة نفسها |
| التنزيل | `GET api/payroll/payment-batches/{id}/files/{fileId}` + زر في شاشة الدفعة | يقرأ المخزَّن ولا يولّد. `Payroll.Post` أو `Payroll.Approve`، ويُسجَّل في `SecurityAudit` بنوع `BulkExport` |
| إعادة الإصدار | طلب أربع عيون (البند 3) بنوع `BankFileReissue` | D-PAY-19 |
| حذف | `ExportTreasuryFile` وجزء الخزينة في `ExportExternalIntegrationBundle` ([PayrollReportsController.cs:304-305](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)) | لا مسار آخر يُخرج ملف دفع |

### 3. أربع عيون بطلبين من جلستين (G-PAY-09)

**المشكلة:**
- الإفراج عن الحجز يأخذ معرّف «المعتمد» من النموذج أو الجسم ([PaymentHoldsController.cs:65-69](../../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs)، [PaymentHoldsApiController.cs:25-32](../../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsApiController.cs))، والخدمة تفحص اختلاف الـ GUIDين فقط ([PaymentHoldService.cs:26-28](../../src/Services/Payroll/Payroll.Application/Services/PaymentHoldService.cs)).
- حلّ تنبيه الاحتيال بلا معتمد ثانٍ يجعل الحالّ معتمداً ([FraudAlertsController.cs:96-115](../../src/Services/Payroll/Payroll.Web/Controllers/FraudAlertsController.cs)).
- تعديل المتأخرات يأخذ `approverId` من الطلب ([ArrearsController.cs:95-102](../../src/Services/Payroll/Payroll.Web/Controllers/ArrearsController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollDualControlRequest` | `Payroll.Domain/Entities/PayrollDualControlRequest.cs` | `Kind` (`HoldRelease`\|`CriticalAlertResolve`\|`ArrearsAdjust`\|`BankFileReissue`)، `TargetId`، `PayloadJson` (السبب، والمبلغ للمتأخرات)، `RequestedBy`، `RequestedAt`، `Status` (`Pending`\|`Approved`\|`Rejected`\|`Expired`)، `DecidedBy`، `DecidedAt`، `DecisionNotes`. فريد: طلب معلّق واحد لكل `(Kind, TargetId)`. و`ConfigureOptimisticConcurrency` |
| `PayrollDualControlService` | `Payroll.Application/Services/PayrollDualControlService.cs` | `RequestAsync` و`DecideAsync`. الفاعل في الحالتين من الرمز فقط. المقرر ≠ الطالب، ويحمل السياسة المطلوبة للنوع، ولا يحمل دورين متعارضين (البند 5). وتنفيذ الأثر (الإفراج، الحل، القيد، إعادة الإصدار) داخل `DecideAsync` في المعاملة نفسها |
| مهلة | `Payroll:DualControl:ExpiryHours` (الافتراضي 72) | الطلب المنتهي `Expired` بلا أثر |
| حذف المدخلات | `ReleasePaymentHoldCommand.ApproverUserId`، حقل النموذج، معامل `approverId` في المتأخرات، `ApprovedBy` في حل التنبيه | لا معرّف فاعل من العميل |
| شاشة | `Views/DualControl/Inbox.cshtml` + `GET/POST api/payroll/dual-control` | «طلبات بانتظار قرارك» لحملة السياسة، مع استبعاد ما طلبه المستخدم نفسه |

**القواعد:**
1. حل تنبيه غير حرج يبقى فعلاً واحداً. والحرج يتطلب طلباً وقراراً.
2. الإفراج عن حجز `REGIME-BLOCKER` يتطلب، فوق القرار، أن يعيد عميل HR.Core «غير محجوب» للموظف لحظة القرار.

### 4. اعتماد مزدوج فوق العتبة، ومراجعة مستقلة قبل الترحيل (G-PAY-09)

**المشكلة:**
- العتبة معلنة ([SodMatrixDefaults.cs:29-37](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs)؛ [SeparationOfDutiesService.cs:29-46](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs))، لكن `PayrollRun.Approve` ينقل إلى `Approved` من أول اعتماد ([PayrollRun.cs:135-149](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)).
- سجل المراجعة يفتحه ويغلقه حملة `PayrollApprove` ([PayrollExtendedControllers.cs:243-303](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs))، و`auditor` لا يصل إلى الوحدة ([HrManagementAuthorizationExtensions.cs:41](../../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)، [GatewayPathAuthorizationMiddleware.cs:228-235](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollRun.Approve` | `PayrollRun.cs` | يأخذ `requiredApprovals` من `sod.GetRequiredApprovalCount("PayrollRun", TotalNetPay)` بعتبة الإعداد. يبقى `UnderReview` حتى يكتمل العدد بمعتمدين مختلفين، ويعرض «اعتماد 1 من 2». والرفض من أيهما يعيد الفتح (PLAN-56) |
| `ApprovePayrollRunHandler` | [PayrollRunHandlers.cs:105-147](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) | المعتمد الثاني ≠ الأول ≠ المنشئ. والمُرحِّل ≠ أي معتمد (`PayrollSodGuard` يفحص كل الاعتمادات لا آخرها) |
| `Payroll.AuditReview` | `HrManagementAuthorizationExtensions.cs` + `HrManagementPolicies` | الأدوار من `Payroll:Audit:ReviewerRoles` |
| مراجعة السجل | `PayrollAuditRecordsController` ([PayrollExtendedControllers.cs:208-303](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)) | الفتح والإغلاق بـ `Payroll.AuditReview`. والمغلق ليس منشئ التشغيل ولا معتمده ولا مُرحِّله، ويبقى ≠ الفاتح (`:290-295`) |
| الوصول | `HrModuleAuthorizationFilter` ([HrModuleAuthorizationFilter.cs:28-45](../../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs))، `GatewayPathAuthorizationMiddleware` | `PayrollAuditRecords` يحمل `[ModulePolicyOverride(Payroll.AuditReview)]` (سمة PLAN-50، والقائمة بالأسماء حذفها PLAN-51)، فلا يطلب دور الوحدة. والبوابة تسمح لـ `Payroll.AuditReview` بمسارات `/ui/payroll/PayrollAuditRecords/*` و`GET /api/payroll/runs/{id}` و`/api/payroll/runs/{id}/audit-records` فقط |
| API | `POST api/payroll/runs/{id}/audit-records`، `POST api/payroll/audit-records/{id}/close` | للقبول بلا SQL، بديلاً عن [acceptance-payroll-audit-fixture.ps1:28-46](../../scripts/acceptance-payroll-audit-fixture.ps1) |

### 5. حامل دورين متعارضين لا يمر إلى فعل مالي (G-PAY-09، ملاحظة المحقق)

**المشكلة:**
- أزواج التعارض (معالج/معتمد، معتمد/مُرحِّل، معالج/مُرحِّل) معرّفة ([SodMatrixDefaults.cs:165-199](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs)).
- **تدقيق لملاحظة المحقق:** `ValidateRoleAssignment` ([SeparationOfDutiesService.cs:109-134](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs)) له مستدعٍ واحد غير مباشر. فالمنح من شاشة Auth.Directory يمر بـ `RoleAssignmentGuard` ([AuthUsersController.cs:124, 134](../../src/Services/AuthDirectory/Auth.Directory.Web/Controllers/AuthUsersController.cs) ← [RoleAssignmentGuard.cs:44, 78](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/RoleAssignmentGuard.cs)).
- لكن الأدوار المسندة في Keycloak مباشرة (لوحة الإدارة، أو استيراد الـ realm عبر [prepare-keycloak-import.ps1](../../scripts/prepare-keycloak-import.ps1)) لا تمر بأي فحص. ولا خدمة رواتب تفحص أدوار الرمز.
- فمستخدم يحمل الأدوار الثلاثة يمر بكل سياسة. والفحص لكل تشغيل ([PayrollSodGuard.cs](../../src/Services/Payroll/Payroll.Application/Services/PayrollSodGuard.cs)) يمنعه من إنشاء التشغيل نفسه واعتماده وترحيله، لكنه لا يمنعه من الجمع بين قرار التمويل والتنفيذ، أو بين طرفي «أربع عيون» على تشغيلات مختلفة.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollRoleConflictRequirement` + معالجها | `Payroll.Web/Authorization/PayrollRoleConflictRequirement.cs` | يُضاف إلى `Payroll.Process`/`Approve`/`Post`/`Reverse`/`AuditReview` في Payroll. يرفض (403) إن حمل الرمز زوجاً متعارضاً من `SeparationOfDutiesService.RolesConflict`، ويكتب `SecurityAudit` بنوع `RoleConflict` |
| `admin` | — | يُعامل في هذا الفحص كحامل للأدوار الثلاثة ما دام يمر بسياسات الرواتب. استبعده PLAN-51 (D-IAM-4) من `Payroll.Post`/`Payroll.Reverse` في الإنتاج، فيبقى الفحص قائماً على `Payroll.Process`/`Payroll.Approve`. وتفكيك الدور كلياً ← الموجة 2 (IAM-G) |
| حارس الاستيراد | `prepare-keycloak-import.ps1` (أو `scripts/keycloak/render-realm.ps1` الذي يغلّفه منذ PLAN-39، حيث تُقرأ الشخصيات) | يرفض أي شخصية بزوج متعارض، إلا ما يحمل `"sodConflictFixture": true` |
| شخصية اختبار | `a-payroll-multirole` في `personas.json` و`AcceptancePersonaCatalog.cs` (`payroll` + `payroll-processor` + `payroll-approver` + `payroll-poster`، `sodConflictFixture: true`) | تُستعمل في القبول لإثبات الرفض. ويُحدَّث العدد (+1 على القيمة القائمة وقت التنفيذ) في [prepare-keycloak-import.ps1:137-138, 269](../../scripts/prepare-keycloak-import.ps1) أو المولّد |

### 6. الحجز وحاجز نظام الخدمة على مستوى الموظف، ومدخلات احتيال حقيقية (G-PAY-15)

**المشكلة:**
- حجز واحد يوقف الدفعة كلها ([PaymentDisbursementGate.cs:21-41](../../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentDisbursementGate.cs))، وكذلك تنبيه حرج واحد (`:43-55`). والحجز بلا تشغيل يوقف كل دفعة ([InboundEventHandlers.cs:89-94, 147-178](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- حاجز نظام الخدمة يوقف حساب التشغيل كله ويذكر العدد لا الأسماء ([PayrollRegimeGovernance.cs:28-33](../../src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs)؛ [PayrollRunParallelCalculator.cs:44-57](../../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)).
- مدخلات الاحتيال شكلية ([FraudScanInputBuilder.cs:22-29](../../src/Services/Payroll/Payroll.Application/Services/FraudScanInputBuilder.cs)). وFR-01/FR-08 تعلّمان كل سطر تصحيح.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PaymentDisbursementGate` | [PaymentDisbursementGate.cs:15-58](../../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentDisbursementGate.cs) | يعيد `HeldEmployees` (الموظف، والسبب، والمرجع) بدل `Block`. الحجز النشط لهذا التشغيل أو بلا تشغيل، والتنبيه الحرج المفتوح للموظف، يحجزان أمر الموظف وحده. والتنبيه الحرج بلا موظف يوقف الدفعة كما اليوم |
| `PaymentOrderStatus.Held` + `HoldReason` | `PaymentOrderStatus.cs`، `PaymentOrder.cs` | الأمر المحجوز خارج الإرسال والملف |
| حاجز النظام | `PayrollRegimeGovernance` + `IHrCoreServiceRegimeClient` | `Payroll:Blockers:Mode`. في `HoldEmployee` يُحسب الكل، ويُنشأ لكل محجوز `PaymentHold` بنوع `REGIME-BLOCKER` باسمه وسببه (من [ServiceRegimePayrollBlockService.cs:22-52](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimePayrollBlockService.cs)). وقائمة المحجوزين بالأسماء في شاشة الاعتماد، فاعتماد التشغيل يشمل العلم بها |
| `FraudScanInputBuilder` | [FraudScanInputBuilder.cs:22-29](../../src/Services/Payroll/Payroll.Application/Services/FraudScanInputBuilder.cs) | FR-01: `HasAttendance` من إيصالات PLAN-57 للفترة (`null` إن لم يوجد إيصال، فلا تنبيه؛ وغياب الإيصال تتولاه بوابة الحضور). FR-08: سطر التصحيح يحمل مصدره بـ `SourceRunId`، فـ `HasSourceRecord=true`. FR-04: الحساب المثبّت (PLAN-55). FR-05: `ExpectedNetPay` = صافي الموظف في آخر تشغيل اعتيادي مرحّل. FR-07: `HireDate` = أقدم `EffectiveFrom` لملفات الموظف |
| تقرير | `GET api/payroll/payment-batches/{id}/held` + بطاقة في شاشة الدفعة | المحجوزون بالأسماء والأسباب والطلبات المعلّقة |

**القواعد:**
1. الإفراج يمر بالبند 3، ثم `submit-held` يرسل الأمر بملف تكميلي.
2. حجز بلا تشغيل يبقى ساري الأثر على أوامر الموظف في كل دفعة، لا على الدفعات كلها.
3. حجز `NO-ACCOUNT` أو `ACCOUNT-RETIRED` (PLAN-55) لا يحتاج طلب إفراج: التحقق من حساب الموظف (PLAN-55) فعلُ شخصين أصلاً. عند تفعيل الحساب يُثبَّت على السطر والأمر غير المرسل (`SalaryLine.BankAccountId`، `PaymentOrder.BeneficiaryIban`)، ويُسجَّل ذلك في `EntityAuditLog`، ويُرفع الحجز، ثم `submit-held`.

### 7. تسجيل تأكيد القناة يدوياً بشخصين (جزء من G-PAY-16، تحتاجه PLAN-64)

**المشكلة:** أوامر `AckPaymentBatchCommand` و`SettlePaymentBatchCommand` و`RejectPaymentBatchCommand` موجودة ([PayrollRunJobHandlers.cs:422-515](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)) ولا يستدعيها إلا اختبارات التكامل. فلا تصل دفعة إلى «مسدَّدة»، ولا تجد PLAN-64 تأكيداً تبني عليه «مدفوع».

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| API | `POST api/payroll/payment-batches/{id}/ack`، `…/settlement-requests`، `…/rejection-requests` | `ack` فعل واحد بمرجع القناة (`Payroll.Post`). والتسوية والرفض طلبان بأربع عيون (البند 3، نوعان جديدان `BatchSettle` و`BatchReject`) بمرجع كشف المصرف والمبلغ المسدَّد |
| الأوامر | `SettlePaymentBatchHandler`، `RejectPaymentBatchHandler` | تُستدعى من `DecideAsync` فقط، وتحدّث الأوامر المرسلة في الدفعة معاً |

**القواعد:** المبلغ المسدَّد ≠ مجموع الأوامر المرسلة ← رفض، والتسوية الجزئية لكل أمر في الموجة 2. والاستيراد الآلي لإشعار القناة وكشف المصرف والمطابقة بالمرجع تبقى لـ **PAY-I**.

---

## Out of scope

- فصل واجبات الإطار القانوني (محرر ≠ معتمد ≠ مصادق ≠ ناشر) ← **PLAN-54** (منفّذ هناك، والدراسة تشير إليه في G-PAY-09).
- العكس واعتماده وإبطال الدفعة ← **PLAN-56**.
- استيراد إشعارات القناة وكشف المصرف آلياً، والمطابقة بالمرجع، والتسوية الجزئية لكل أمر، وسداد المتأخرات في الدورة التالية، واسترجاع المال المسدَّد ← الموجة 2 (**PAY-I**). والتسجيل اليدوي بشخصين في البند 7.
- FR-02 (السجل المدني)، وFR-03 (بصمة الرقم الوطني)، وFR-06 (حجم أفعال المعتمد)، ومهمة شهادة الحياة ([FraudAnomalyEngine.cs:97-127](../../src/Services/Payroll/Payroll.Application/Services/FraudAnomalyEngine.cs)) ← الموجة 2 (**PAY-J**).
- سلسلة اعتماد بأدوار مسماة (مدير مالي ثم آمر صرف) عبر محرك سير العمل ← الموجة 2 (WF).
- فصل `admin` الكامل عن الأفعال المالية، وتقرير المستخدمين المتعارضين في Keycloak، ومزامنة الأدوار ← الموجة 2 (**IAM-G**). ونفّذ **PLAN-51** (D-IAM-4) استبعاد `hr-admin` من `Payroll.Post`/`Payroll.Reverse` في الإنتاج.
- حساب نهاية الخدمة وصرفه (G-PAY-14) ← **PLAN-64**، ويصرف عبر مسار هذه الخطة.
- ذرية `Save→Publish` في المعالجة ← **PLAN-40**.

## Acceptance scenarios

على الملف الشبيه بالإنتاج، `AcPay58DisbursementGovernanceTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-PAY-58")`). ويُعاد كتابة `Ac24PartialDisbursementTests` و`Ac25FraudHoldTests` على السلوك الجديد:

| ID | Positive | Negative |
|----|----------|----------|
| AC-PAY-58a | تمويل 60%: ترحيل ← `payroll-approver` يقرر التمويل على الدفعة `Pending` ← `a-payroll-poster` ينفذ ← الملف يطابق الأوامر (`ControlTotal` = 60%)، والباقي متأخرات | قرار تمويل على تشغيل `Approved` ← 400. `partial-disburse` ← 404. المعالج ينفذ ← 403 |
| AC-PAY-58b | دفعة فيها موظف محجوز: يُرسل الباقون، والمحجوز `Held` خارج الملف. `payroll-approver` يطلب الإفراج، و`a-payroll-approver-2` يعتمده من جلسته، ثم `submit-held` ← ملف تكميلي بالموظف وحده | `ApproverUserId` في الجسم يُتجاهل، والطالب لا يقرر طلبه (400). توليد الملف مرة ثانية للإرسال نفسه لا ينتج ملفاً ثانياً |
| AC-PAY-58c | تشغيل فوق العتبة: اعتماد أول ← `UnderReview` «1 من 2»، واعتماد ثانٍ من `a-payroll-approver-2` ← `Approved` | المعتمد الأول يعتمد ثانية ← 400 |
| AC-PAY-58d | `auditor` يفتح سجل المراجعة ويُغلقه عبر البوابة، ثم يرحّل المُرحِّل | المعتمد يغلق السجل ← 403. `auditor` يرسل POST إلى `PayrollRuns/Approve` ← 403 |
| AC-PAY-58e | — | `a-payroll-multirole` يعالج أو يعتمد أو يرحّل ← 403 برسالة التعارض، وسطر `RoleConflict` في `SecurityAudit` |
| AC-PAY-58f | موظف بحساب غير محقق: أمره `Held` بسبب `NO-ACCOUNT` ويُصرف للباقين، والتحقق من حسابه يرفع الحجز، و`submit-held` يرسله وحده | — |
| AC-PAY-58g | موظفان بحساب مثبّت واحد ← تنبيه FR-04 لكل منهما وحجز أمريهما فقط | تصحيح لموظف حقيقي لا ينتج FR-01 ولا FR-08 |
| AC-PAY-58h | `a-payroll-poster` يسجّل `ack` ثم يطلب التسوية بمرجع الكشف، و`payroll-approver` يعتمد ← الدفعة وأوامرها `Settled` | الطالب يعتمد طلبه ← 400. مبلغ مسدَّد لا يطابق الأوامر ← 400 والدفعة كما هي |

اختبارات تكامل على PostgreSQL:
- معالجتان متزامنتان للدفعة نفسها ← واحدة تنجح، وملف واحد (فريد `(PaymentBatchId, Sequence)` و`xmin`).
- قراران متزامنان على طلب أربع عيون واحد ← واحد ينجح.
- الترحيل ينشئ الدفعة والأوامر في معاملة واحدة: فشل مصطنع بعد الأوامر لا يترك دفعة يتيمة.
- حاجز نظام الخدمة لموظف واحد (`FakeHrCoreServiceRegimeClient` من PLAN-34 يُضبط ليحجبه): التشغيل يُحسب، وأمره `Held` بسبب `REGIME-BLOCKER` باسمه، ويُصرف للباقين. والإفراج قبل أن يعيد العميل «غير محجوب» ← رفض. (لا يُختبر حياً لأن بذرة القبول تصنّف كل الموظفين بعد PLAN-54، ولا واجهة تفتح استثناء نظام.)

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay058|FullyQualifiedName~Pay001|FullyQualifiedName~Pay004|FullyQualifiedName~Frd001|FullyQualifiedName~SeparationOfDuties"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Pay058|FullyQualifiedName~PaymentOrder"
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web
pwsh scripts/prepare-keycloak-import.ps1   # الشخصية a-payroll-multirole وحارس التعارض
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcPay58|FullyQualifiedName~Ac24|FullyQualifiedName~Ac25"
```

## Definition of done

- لا مسار يُخرج مالاً أو ملفاً مصرفياً إلا الترحيل ثم `process`/`submit-held`، والاختبار المعماري يفرض ذلك.
- كل «أربع عيون» طلبان من مستخدمين، ولا معرّف فاعل من العميل في Payroll.
- الاعتماد المزدوج فوق العتبة، والمراجعة المستقلة بدور `auditor`، يعملان عبر البوابة.
- حامل الدورين المتعارضين يُرفض في كل فعل مالي.
- موظف محجوز أو بحاجز لا يوقف الدفعة، ويُصرف له بعد الإفراج بملف تكميلي.
- تأكيد القناة يُسجَّل بشخصين، فتصل الدفعة إلى `Settled`.
- AC-PAY-58a..h وAC-24 وAC-25 = Pass في `acceptance-evidence/`.
- أسطر «الحالة» لـ G-PAY-08 وG-PAY-09 وG-PAY-15 محدّثة، مع ما بقي للموجة 2.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-58-pay-disbursement-governance.md` |
| Domain | `PaymentBatchFile.cs`، `PayrollDualControlRequest.cs`؛ تعديل `PayrollRun.cs` (الاعتمادات المتعددة)، `PaymentOrder.cs`/`PaymentOrderStatus.cs` (`Held`) |
| Application | `PayrollDualControlService.cs`، `PaymentBatchIssuer.cs`؛ تعديل `PayrollRunHandlers.cs`، `PayrollRunJobHandlers.cs` (`ProcessPaymentBatchHandler`)، `CreateFundingDecisionCommand.cs`، `PaymentHoldService.cs`، `PayrollRegimeGovernance.cs`، `FraudScanInputBuilder.cs`، `PayrollSodGuard.cs`؛ حذف `ExecutePartialDisbursementCommand.cs` ومسار `PartialDisbursementService.ExecuteAsync` |
| Infrastructure | `PaymentBatchFileBuilder.cs`؛ تعديل `PaymentDisbursementGate.cs`، `PaymentOrderCoordinator.cs`؛ ترحيل `PAY058_DisbursementGovernance` |
| Web/Shared | `PayrollRoleConflictRequirement.cs`، سياسة `Payroll.AuditReview`، تعديل `HrModuleAuthorizationFilter.cs` و`GatewayPathAuthorizationMiddleware.cs`، شاشة `DualControl/Inbox`، نقاط API الجديدة؛ حذف `ExportTreasuryFile` و`partial-disburse` |
| Scripts/Data | حارس التعارض في `prepare-keycloak-import.ps1`، الشخصية `a-payroll-multirole`؛ حذف `scripts/acceptance-payroll-audit-fixture.ps1` بعد إعادة كتابة AC-25 |
| Tests | `Pay058*Tests.cs` (وحدة ومعمارية)، `Pay058DisbursementPostgreSqlTests.cs` (تكامل)، `AcPay58DisbursementGovernanceTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41)، وإعادة كتابة `Ac24PartialDisbursementTests.cs` و`Ac25FraudHoldTests.cs` (قبول) |
| Docs | `docs/PAY_001_APPROVAL_BYPASS.md:94` و`docs/EXT_003_PAYMENT_ORDER_BINDING.md:3` (مسار واحد، D-PAY-2)، `docs/FRD_001_FRAUD.md:42` (الأربع عيون، والقواعد العاملة فعلاً)، `docs/PAY_004_*` (الحجز للموظف)، أسطر «الحالة» في `docs/PAY_001_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| رفض حامل الأدوار المتعارضة يوقف مستخدمين حقيقيين في جهة صغيرة | رسالة 403 تسمي الزوج المتعارض. والإعداد `RejectConflictingRoleHolders` قابل للتعطيل بقرار موثق، ويُكتب سطر `SecurityAudit` عند كل مرور |
| حذف `partial-disburse` يكسر عميلاً خارجياً | لا مستهلك خارجي في المستودع (الجرد 0.3). وتُعاد 404 بعد الحذف، والوثائق تشرح البديل |
| اعتمادان لكل تشغيل فوق العتبة يبطئان الدورة | العتبة إعداد لكل جهة، وصندوق «بانتظار قرارك» يُظهر الاعتماد الثاني |
| تعارض عدّ الشخصيات مع خطط أخرى تضيف شخصيات | التحديث في آخر خطوة قبل الدمج، والاستيراد يرفض أي عدد غير مطابق فيكشف التعارض فوراً |

## Effort estimate

9.5–10 أيام عمل: المسار الواحد والتمويل (2)، الملف المصرفي (1.5)، الأربع عيون (1.5)، الاعتماد المزدوج والمراجعة المستقلة والوصول (1.5)، حارس التعارض (0.5)، الحجز للموظف والحاجز ومدخلات الاحتيال (1.5)، تسجيل تأكيد القناة (0.5)، إعادة كتابة AC-24/25 والقبول والوثائق (1).

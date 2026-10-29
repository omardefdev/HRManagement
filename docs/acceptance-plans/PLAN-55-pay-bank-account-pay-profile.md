# PLAN-55 — الحساب المصرفي للموظف وتكامل نسخ ملف الأجر (PAY-B)

**Scope:** PAY — [دراسة الفجوات](../PAY_001_GAP_STUDY.md): G-PAY-02  
**Status:** Proposed — 2026-10-30  
**Priority:** P0  
**Depends on:** PLAN-54 (بذرة القبول والسياسات)، PLAN-53 (نقطة تطبيق تغيير الراتب)  
**Related cards:** PAY-002، EXT-003، SYR-011، FRD-001 (FR-04)، PRV-001 (بيانات مالية شخصية)

## Goal

- أن يُسجَّل لكل موظف حساب مصرفي يتحقق منه شخص ثانٍ، فتحمل أوامر الدفع وملف الخزينة وجهة صحيحة.
- أن يُثبَّت الحساب في التشغيل لحظة الحساب، فلا يغيّر تبديلُه بعد الاعتماد وجهةَ المال.
- ألا تمحو أي نسخة جديدة من ملف الأجر الوحدةَ التنظيمية والنظام القانوني، أياً كان مصدرها: تفعيل عقد، أو تغيير راتب، أو نقل.

---

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-PAY-11 — صيغة معرّف الحساب (IBAN أم رقم حساب محلي لكل مصرف) | يُقبل IBAN بفحص ISO 13616 (mod 97) دائماً. ويُقبل الرقم المحلي فقط لمصرف معرّف في `Payroll:Bank:Banks` (`code`، `nameAr`، `accountRegex`). القائمة فارغة في الإنتاج حتى يُعتمد ملف المصارف (SYD-014) | لا يُرفض أي حساب IBAN صحيح. والحسابات المحلية تنتظر إعداد المصارف |
| D-PAY-14 — من يسجّل الحساب ومن يتحقق منه | يسجّل حامل سياسة المعالجة، ويتحقق حامل سياسة الاعتماد (≠ المسجّل) بمرجع تأكيد من المصرف (نص إلزامي). الخدمة الذاتية للموظف في الموجة 2 | الحساب لا يُستعمل قبل التحقق |
| D-PAY-15 — لحظة تثبيت الحساب في التشغيل | لحظة لقطة الحساب (`PayrollRunInputSnapshot`) | الحساب الذي يتغير بعد الحساب يسري في الدورة التالية |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-55-pay-bank-account-pay-profile` | — |
| 0.2 | البناء والاختبارات | `dotnet build HRManagement.slnx`؛ `dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay029\|FullyQualifiedName~ContractActivated\|FullyQualifiedName~PaymentOrder"` | أخضر أو قائمة في `reports/plan55-baseline.md` |
| 0.3 | جرد | كل قراءة لـ `BankAccountIban`/`BankAccountVerifiedAt`، وكل `new EmployeePayProfile` (يشمل نقطة التطبيق التي أدخلها PLAN-53) | الجدول في التقرير |

---

## In scope

### 1. كيان الحساب المصرفي بصانع ومدقق (G-PAY-02)

**المشكلة:**
- الحقلان على `EmployeePayProfile` ([EmployeePayProfile.cs:43-59](../../src/Services/Payroll/Payroll.Domain/Entities/EmployeePayProfile.cs)) لا يسندهما أي رمز. فملف الخزينة يرفض كل تشغيل ([PayrollExternalExportService.cs:105-110](../../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs))، وأوامر الدفع بلا IBAN ([PaymentOrderService.cs:22-29, 52](../../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderService.cs)).
- وقاعدة FR-04 لا ترى شيئاً ([FraudScanInputBuilder.cs:25](../../src/Services/Payroll/Payroll.Application/Services/FraudScanInputBuilder.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `EmployeeBankAccount` | `Payroll.Domain/Entities/EmployeeBankAccount.cs` | `EmployeeId`، `AccountKind` (`Iban`\|`Local`)، `AccountNumber` (مطبَّع، `[AuditRedact]`)، `BankCode`، `BankNameAr`، `HolderNameAr`، `Status` (`PendingVerification`\|`Active`\|`Rejected`\|`Retired`)، `VerifiedBy`، `VerifiedAt`، `VerificationReference`، `RejectionReason`، `RetiredAt`، `SupersedesAccountId`. يرث `AuditableEntity` (`CreatedBy` = المسجّل) |
| فهارس | `PayrollDbContext` | فريد مشروط: حساب `Active` واحد لكل `(TenantId, EmployeeId)`، و`PendingVerification` واحد لكل موظف. و`ConfigureOptimisticConcurrency` |
| `BankAccountNumberValidator` | `Payroll.Domain/Services/BankAccountNumberValidator.cs` | تطبيع (حذف المسافات، أحرف كبيرة)، وفحص mod 97 لـ IBAN، وفحص `accountRegex` للمحلي (D-PAY-11) |
| أوامر | `Payroll.Application/Features/BankAccounts/` | `RequestBankAccountCommand` (معالجة)، `VerifyBankAccountCommand` (اعتماد؛ المتحقق ≠ المسجّل؛ يحيل السابق `Retired`)، `RejectBankAccountCommand` (سبب إلزامي)، `RetireBankAccountCommand` (سبب؛ انظر القاعدة 5) |
| API | `Payroll.Web/Controllers/EmployeeBankAccountsApiController.cs` (`api/payroll/employees/{employeeId}/bank-accounts`) | `GET`، `POST`، `POST {id}/verify`، `POST {id}/reject`، `POST {id}/retire`. بسياسات صريحة ونطاق الوحدة (`OrgScopeResolver` على الملف الفعّال) |
| MVC | `EmployeeBankAccountsController` + `Views/EmployeeBankAccounts/Index.cshtml` (حسابات موظف)، `Pending.cshtml` (بانتظار التحقق)، `Create.cshtml` | الرقم يُعرض مقنّعاً (آخر 4 خانات) إلا لصاحب الاعتماد في شاشة التحقق |
| ترحيل | `PAY055_EmployeeBankAccounts` | ينشئ الجدول. ويرحّل أي `BankAccountIban` غير فارغ إلى `PendingVerification` (لا تحقق ضمني). وتُعلَّم حقول الملف القديمة `[Obsolete]` ولا تُقرأ |

**القواعد:**
1. لا حساب `Active` إلا بتحقق شخص غير المسجّل، مع مرجع تأكيد.
2. تغيير الحساب = طلب جديد ثم تحقق. الحساب النافذ يبقى نافذاً حتى يُتحقق من البديل.
3. IBAN غير صحيح أو مصرف غير معرّف ← 400 برسالة عربية، ولا صف.
4. كل تسجيل وتحقق ورفض وإحالة يُسجَّل في `EntityAuditLog` (الرقم مُحجوب بـ `[AuditRedact]`) وفي `SecurityAudit` بنوع `PayrollOverride`.
5. إحالة حساب مثبَّت في سطر تشغيل لم يُصرف بعد تنشئ `PaymentHold` للموظف في ذلك التشغيل بسبب `ACCOUNT-RETIRED`، فلا يُدفع إلى حساب سُحب.

### 2. تثبيت الحساب في التشغيل واستعماله في الأوامر والملف (G-PAY-02)

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `PayrollRunInputSnapshot.BankAccountId` | `Payroll.Domain/Entities/PayrollRunInputSnapshot.cs` | الحساب `Active` لحظة اللقطة ([PayrollRunJobHandlers.cs:119-133](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)) |
| `SalaryLine.BankAccountId` | `Payroll.Domain/Entities/SalaryLine.cs` | يُنسخ من اللقطة عند بناء الحزمة ([PayrollEmployeeCalculationUnit.cs:22-54](../../src/Services/Payroll/Payroll.Application/Services/PayrollEmployeeCalculationUnit.cs)) |
| `PaymentOrderService.EnsureOrdersForBatchAsync` | [PaymentOrderService.cs:11-63](../../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderService.cs) | `BeneficiaryIban` من حساب السطر لا من الملف الفعّال |
| `PayrollExternalExportService` | [PayrollExternalExportService.cs:44-110](../../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs) | الحساب من السطر، ورسالة الرفض تسرد الأسماء لا العدد. تغيير صغير مؤقت: PLAN-58 يحذف هذا التصدير ويبني الملف من الأوامر |
| تقرير الجاهزية | `GET api/payroll/runs/{id}/bank-readiness` + بطاقة في `Views/PayrollRuns/Details.cshtml` | عدد الأسطر بحساب مثبت، والأسطر بلا حساب بالأسماء |
| FR-04 | [FraudScanInputBuilder.cs:22-29](../../src/Services/Payroll/Payroll.Application/Services/FraudScanInputBuilder.cs) | IBAN من حساب السطر، فتعمل قاعدة الحساب المشترك |
| DSAR | [DsarInternalController.cs:27-28](../../src/Services/Payroll/Payroll.Web/Controllers/DsarInternalController.cs) | `bankAccountPresent`/`bankAccountVerified` من الكيان الجديد |

**القواعد:**
1. تبديل الحساب بعد اللقطة لا يغيّر سطر التشغيل ولا أوامره.
2. السطر بلا حساب لا يمنع الحساب ولا الاعتماد. يظهر في تقرير الجاهزية، ويتعامل معه مسار الصرف (PLAN-58).

### 3. نسخة ملف الأجر لا تمحو الوحدة والنظام (G-PAY-02)

**المشكلة:**
- `UpsertEmployeePayProfileHandler` ([EmployeePayProfileHandlers.cs:38-46](../../src/Services/Payroll/Payroll.Application/Features/EmployeePayProfiles/EmployeePayProfileHandlers.cs)؛ حذفه PLAN-53 وحلّ محله `PayProfileChangeApplier` الذي ينسخ كل الحقول) و`HandleContractActivatedHandler` ([InboundEventHandlers.cs:53-61, 72-79](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) ينشئان نسخة بلا `OrgUnitId` ولا `RegimeCode`.
- أول تفعيل عقد بعد `IEmployeeCreated` ([:640-648](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) لا يرى الملف غير الفعّال الذي يحمل الوحدة. وإن وصل `IEmployeeCreated` بعد التفعيل يُهمل كلياً لأن ملفاً موجوداً ([:637-638](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- فيختفي الموظف من قائمة ملفات الأجر لمستخدم الفرع ([EmployeePayProfilesController.cs:32-36](../../src/Services/Payroll/Payroll.Web/Controllers/EmployeePayProfilesController.cs)).
- وفحص نطاق الـ API يقرأ أول ملف بلا ترتيب ([PayrollApiController.cs:154-158](../../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|-------|
| `EmployeePayProfileVersioning.CreateNextVersion` | `Payroll.Domain/Services/EmployeePayProfileVersioning.cs` (أنشأه PLAN-53 للنسخ الكامل والإغلاق؛ يُوسَّع هنا ولا يُعاد) | نقطة الإنشاء الوحيدة لنسخة جديدة. تأخذ الملف الفعّال إن وجد، وإلا آخر ملف للموظف (فعّال أو غير فعّال) — هذا هو الجديد هنا. تنسخ `OrgUnitId` و`RegimeCode` ما لم يمرّر المصدر قيمة جديدة. وتضع تاريخ نهاية السابق وتاريخ بداية التالي، وتنشئ لقطة تاريخ |
| المستدعون | `PayProfileChangeApplier` (PLAN-53، يستعملها أصلاً)، و`HandleContractActivatedHandler` عبر `IPayProfileChangeApplier.ApplySystemChangeAsync` (`Source=ContractActivated`، كما يشترط عقد PLAN-53) | كلها عبر `CreateNextVersion`، فيُسجَّل كل تغيير آلي صفاً `Applied` في `PayProfileChangeRequest` |
| `IEmployeeCreated` | `HandleEmployeeCreatedHandler` | يحدّث `OrgUnitId`/`RegimeCode` للملف القائم إن وصل الحدث بعد تفعيل العقد، بدل أن يتجاهله |
| فحوص النطاق | `PayrollApiController.cs:154-158`، `ArrearsController.cs:129-133` | تقرأ الملف الفعّال وحده (`IsActive`) |
| ترحيل بيانات | `scripts/pay055-backfill-profile-orgunit.ps1` + نقطة إدارة `POST api/payroll/admin/backfill-profile-orgunit?dryRun=true` | ينسخ `OrgUnitId`/`RegimeCode` من آخر ملف يحملهما إلى الملف الفعّال الفارغ |

**القواعد:**
1. لا يُنشأ `EmployeePayProfile` جديد خارج `CreateNextVersion`، ويفرض ذلك تحديث `PayProfileWritersTests` الذي أدخله PLAN-53 (لا `new EmployeePayProfile` في `Payroll.Application` و`Payroll.Web` إلا داخل `CreateNextVersion` والبذور، ويُحذف `HandleContractActivatedHandler` من قائمة الكتّاب المسموحين). لا اختبار معماري ثانٍ.
2. النقل يحدّث `OrgUnitId` في مكانه كما اليوم ([InboundEventHandlers.cs:301-315](../../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). وتصحيح الأثر المالي المزدوج للنقل خارج هذه الخطة.

---

## Out of scope

- استبعاد الموظف بلا حساب من الدفعة والملف، ومسار الصرف الموحد ← **PLAN-58**.
- معاملة تغيير الراتب ← **PLAN-53** (تُستعمل نقطة تطبيقها فقط).
- تسجيل الموظف حسابه عبر الخدمة الذاتية، والتحقق الآلي مع المصرف ← الموجة 2 (**PAY-I**، **PAY-J**، D-PAY-9).
- تشفير رقم الحساب في قاعدة البيانات ← الموجة 2 (سيادة البيانات SOV).
- الأثر المالي المزدوج للنقل والانتداب ← الموجة 2 (**PAY-G**، G-MOV-04).

## Acceptance scenarios

على الملف الشبيه بالإنتاج، `AcPay55BankAccountTests` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-PAY-55")`):

| ID | Positive | Negative |
|----|----------|----------|
| AC-PAY-55a | `payroll-processor` يسجّل IBAN صالحاً لموظف ← `PendingVerification`. `payroll-approver` يتحقق بمرجع ← `Active` | المسجّل نفسه يتحقق ← 400. IBAN بخانة تحقق خاطئة ← 400 ولا صف. طلب ثانٍ معلّق للموظف نفسه ← 409 |
| AC-PAY-55b | تغيير راتب الموظف عبر معاملة PLAN-53 ← الملف الفعّال الجديد يحمل الوحدة والنظام نفسيهما، ويظهر لمدير الفرع في قائمة ملفات الأجر | — |
| AC-PAY-55c | حساب تشغيل ← سطر الموظف يحمل `BankAccountId`، وتقرير الجاهزية يعدّه | تحقق حساب بديل بعد الحساب ← سطر التشغيل يبقى على الحساب الأول، والدورة التالية تأخذ الجديد |
| AC-PAY-55d | — | إحالة الحساب المثبّت قبل الصرف ← حجز `ACCOUNT-RETIRED` للموظف في ذلك التشغيل |
| AC-PAY-55e | موظف بلا حساب ← يظهر باسمه في تقرير الجاهزية | `api/payroll/employees/{id}/bank-accounts` لموظف خارج فرع المستخدم ← 403 |

اختبارات تكامل على PostgreSQL:
- تحققان متزامنان من حسابين للموظف نفسه ← واحد ينجح والآخر تعارض (الفهرس المشروط و`xmin`).
- `IEmployeeCreated` ثم `IContractActivated` ← الملف الفعّال يحمل الوحدة والنظام.
- تشغيل مرحّل مبني في الاختبار ← ملف الخزينة يحمل IBAN الحساب المثبّت لا الحالي.

## Verification

```powershell
dotnet build HRManagement.slnx
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Pay055|FullyQualifiedName~Pay029|FullyQualifiedName~ContractActivated|FullyQualifiedName~Frd001"
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Pay055"
dotnet ef migrations has-pending-model-changes -p src/Services/Payroll/Payroll.Infrastructure -s src/Services/Payroll/Payroll.Web
# قبول على الملف الشبيه بالإنتاج (PLAN-37) بعد إعادة إنشاء payroll-web:
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcPay55"
pwsh scripts/pay055-validate.ps1
```

## Definition of done

- حساب مصرفي يُسجَّل ويُتحقق منه بشخصين، ويُثبَّت في التشغيل، ويصل إلى أوامر الدفع وملف الخزينة.
- لا نسخة ملف أجر بلا وحدة ونظام حين يكونان معروفين، والاختبار المعماري يمنع إنشاء نسخة خارج `CreateNextVersion`.
- قاعدة FR-04 تعمل على الحسابات الحقيقية.
- AC-PAY-55a..e = Pass في `acceptance-evidence/AC-PAY-55/`، واختبارات التكامل الثلاثة خضراء.
- سطر «الحالة» لـ G-PAY-02 محدّث في الدراسة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-55-pay-bank-account-pay-profile.md` |
| Domain | `EmployeeBankAccount.cs`، `BankAccountNumberValidator.cs`، `CreateNextVersion` في `EmployeePayProfileVersioning.cs` (PLAN-53)، حقلا `BankAccountId` على `PayrollRunInputSnapshot` و`SalaryLine` |
| Application | `Features/BankAccounts/*`، تعديل `PayrollRunJobHandlers`، `PayrollEmployeeCalculationUnit`، `FraudScanInputBuilder`، معالجات الأحداث |
| Infrastructure | تعديل `PaymentOrderService`، `PayrollExternalExportService`؛ ترحيل `PAY055_EmployeeBankAccounts` |
| Web | `EmployeeBankAccountsApiController.cs`، `EmployeeBankAccountsController.cs` وشاشاته، تقرير الجاهزية في `Details.cshtml`، إصلاح فحص النطاق |
| Scripts | `scripts/pay055-backfill-profile-orgunit.ps1`، `scripts/pay055-validate.ps1` |
| Unit | `Pay055BankAccountTests.cs`، `Pay055BankAccountNumberValidatorTests.cs`، `Pay055ProfileVersioningTests.cs`، تحديث `PayProfileWritersTests.cs` (PLAN-53) |
| Integration | `Pay055BankAccountPostgreSqlTests.cs` |
| Acceptance | `AcPay55BankAccountTests.cs` (مُدرج في `tests/acceptance/ci-scope.json`، `prodLike[]` بـ `required: true`، PLAN-41) |
| Docs | `docs/PAYROLL_RULES.md` (الحساب وتثبيته)، `docs/PRV_001_DPIA_PAYROLL.md` (رقم الحساب)، سطر «الحالة» في `docs/PAY_001_GAP_STUDY.md` |

## Risks

| الخطر | التخفيف |
|-------|---------|
| Syria خارج سجل IBAN الرسمي، فقد لا تحمل حسابات المصارف العامة IBAN | مسار الرقم المحلي لكل مصرف معرّف (D-PAY-11)، وقائمة المصارف إعداد لا شيفرة |
| انحراف أسماء PLAN-53 (`PayProfileChangeApplier`، `EmployeePayProfileVersioning`) عند التنفيذ | المرحلة 0.3 تجرد كل إنشاء للملف، وتوجّهه إلى `CreateNextVersion` |
| بيانات قديمة بملفات فعّالة بلا وحدة | سكربت الإصلاح بـ `dryRun` أولاً |

## Effort estimate

5–6 أيام عمل: الكيان والأوامر والشاشات (2.5)، التثبيت والأوامر والملف والاحتيال (1)، نسخ ملف الأجر والإصلاح (1)، الاختبارات والقبول والوثائق (1–1.5).

# دليل استخدام واختبار عملية التوظيف ومعاملة «موظف جديد»

دليل تشغيلي: من تسجيل طلب التوظيف حتى اكتمال التعيين والأرشفة — **بدون إدخال معرّف موظف (GUID) يدوياً**.

**المدخل الموحّد:** البوابة `http://localhost:5000`

**مراجع:** [HIRE_RBAC_MATRIX.md](./HIRE_RBAC_MATRIX.md) | [MESSAGE_EVENT_MATRIX.md](./MESSAGE_EVENT_MATRIX.md)

---

## ما الذي تغيّر؟

| قبل | الآن |
|-----|------|
| إنشاء موظف منفصل ثم نسخ GUID | التعيين من **نموذج واحد** ينشئ الموظف أو يربط موجوداً |
| حقل نصي `hiredEmployeeId` في قائمة الطلبات | زر **تعيين** → صفحة `Hire/{id}` |
| `HireWizard/Step1` لإدخال GUID | **توجيه تلقائي** إلى `Continue?employeeId=...` ثم Step2 |
| قائمة المعاملات تعرض GUID فقط | تعرض **اسم الموظف** من ملاحظات المعاملة |

---

## 0) قبل أن تبدأ

### تشغيل النظام

```bash
# من جذر المشروع
docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait
```

أو حسب [GATEWAY_RUNBOOK.md](./GATEWAY_RUNBOOK.md) و[KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md).

| المكوّن | العنوان |
|---------|---------|
| البوابة | `http://localhost:5000` |
| Keycloak | `http://localhost:8080` |
| فحص صحة الخدمات | `http://localhost:5000/Home/UpstreamHealth` |

### حسابات الاختبار

| المستخدم (مقترح) | الدور | ماذا يفعل |
|------------------|-------|-----------|
| `test-admin` / `admin-hire` | `hr-admin` | اختبار سريع — كل المراحل |
| `core-recruiter` | `hr-core` | طلبات التوظيف + التعيين + السيرة |
| `contracts-officer` | `hr-contracts` | عقد + مباشرة + معالج التعيين |
| `mgmt-approver` | `hr-management-approver` | اعتماد عليا (خطوة 3) |
| `payroll-officer` | `payroll` | التحقق من ملف الراتب |
| `idcard-officer` | `idcard` | إصدار البطاقة |

> الجلسة تُشارَك بين `/ui/hr-core` و`/ui/hr-contracts` عبر البوابة.

### متطلب: نوع عقد `DEFAULT`

قبل أول تعيين، تأكد من وجود نوع عقد برمز **`DEFAULT`**:

`http://localhost:5000/ui/hr-contracts/ContractTypes` → إنشاء نوع جديد برمز `DEFAULT`

---

## 1) عملية التوظيف — المرجع السريع (HCM-001)

**الدليل التشغيلي في البوابة:** `http://localhost:5000/Home/JourneyDetails/new-hire` — مرحلتان: **التوظيف (HCM-001)** ثم **التمكين (FUN-001)**.

### مسار الطلب (حالات — بعد HCM Plans 1–7)

```text
Draft → PendingApproval → Approved → Published
    → Submitted → … → OfferExtended → OfferAccepted → Hired
                              ↓
                          Rejected / Withdrawn
```

| الحالة | المعنى | ملاحظة |
|--------|--------|--------|
| `Published` | إعلان منشور (عام/داخلي) | استقبال عبر `/careers` أو HR أو ESS |
| `OfferAccepted` | قبول خطاب العرض | **زر التعيين** يظهر في تفاصيل الطلب |
| `Hired` | تم التعيين وإغلاق الشاغر | يُفتح **معالج التعيين** (FUN-001) |

> زر **تعيين** في `RecruitmentApplications/Details` يظهر **فقط** عندما تكون الحالة `OfferAccepted`.

### الروابط الأساسية للتوظيف

| الغرض | الرابط |
|-------|--------|
| دليل المسار (بوابة) | `http://localhost:5000/Home/JourneyDetails/new-hire` |
| طلبات الشاغر | `http://localhost:5000/ui/hr-core/RecruitmentRequisitions` |
| منصب ممول + مسودة | `http://localhost:5000/ui/hr-core/RecruitmentRequisitions/Create` |
| بوابة المواطن | `http://localhost:5000/careers` |
| طلبات HR | `http://localhost:5000/ui/hr-core/RecruitmentApplications` |
| إعلانات المواطنين | `http://localhost:5000/ui/hr-core/PublicJobPostings` |
| القناة الداخلية (HR) | `http://localhost:5000/ui/hr-core/TalentMarketplace` *(اختياري)* |
| ترشّح ESS | `http://localhost:5000/ui/hr-core/EssTalent` *(اختياري — ModuleEss)* |
| خطابات العرض | `http://localhost:5000/ui/hr-core/OfferLetters` |
| لجان الاختيار | `http://localhost:5000/ui/hr-core/SelectionCommittees` *(اختياري)* |
| متابعة المعالج (بعد التعيين) | `http://localhost:5000/ui/hr-contracts/HireWizard/Continue?employeeId={id}` |
| قائمة معاملات التعيين | `http://localhost:5000/ui/hr-contracts/HireWizard` |

---

## 2) خطوات التوظيف — تفصيل الشاشات (مرحلة HCM-001)

**الحساب:** `hr-core` أو `hr-admin`

### المرحلة أ — من الشاغر الممول حتى قبول العرض

1. **التحقق من المنصب الممول** — `RecruitmentRequisitions/Create`: اختر منصباً `Vacant` (EST-001).
2. **طلب الشاغر** — `RecruitmentRequisitions`: مسودة + **تقديم للاعتماد**.
3. **اعتماد الطلب (PLAN-21)** — من `RecruitmentRequisitions/Details`:
   - الخطوة 1: `acc-br1-mgr` (أو أي `hr-core` غير المنشئ) → **اعتماد**
   - الخطوة 2: `acc-a-management-approver` → **اعتماد**
   - المنشئ (`acc-central-a`) **لا يرى** أزرار الاعتماد على طلبه (SoD).
4. **نشر الإعلان** — بعد `Approved`: **نشر الإعلان العام** / **نشر الشاغر الداخلي** حسب القناة.
5. **التقديم** — مرشح خارجي عبر `/careers`؛ أو HR عبر `RecruitmentApplications`؛ أو `PromoteFromCitizen` من `PublicJobPostings`.
6. **القناة الداخلية (اختياري)** — `TalentMarketplace` (HR) و/أو `EssTalent` (ESS) ثم `PromoteFromInternal`.
7. **الاختيار** — COI، درجة، ترتيب في `RecruitmentApplications/Details`.
   - **لجنة الاختيار (PLAN-22، اختياري):** `SelectionCommittees` → أعضاء (≥3 للنصاب) → **تقديم للاعتماد** → Details → 3 خطوات اعتماد.
   - **استئناف (PLAN-22، اختياري):** رفض مرشح → FileAppeal → `acc-a-compliance` يعتمد من Details.
8. **خطاب العرض (PLAN-22)** — `OfferLetters/Details`:
   - **تقديم للاعتماد** → `acc-br1-mgr` → `acc-a-management-approver` → `Issued`
   - المنشئ لا يعتمد عرضه (SoD) — ثم **قبول العرض** → `OfferAccepted`

### صندوق العمل (PLAN-23 / WF-002)

**الصفحة:** `WorkInbox/Index` (القائمة الجانبية: **صندوق العمل**)

- يجمع اعتمادات **workflow** المعلّقة (طلب شاغر، عرض، لجنة، استئناف) + **legacy** `ApprovalRequests` حسب دورك وSoD.
- اضغط **فتح** للانتقال إلى صفحة Details المعتمدة (PLAN-21/22) — لا اعتماد inline من الصندوق في v1.
- عداد البوابة (`pending-approvals`) يعكس workflow + legacy للمستخدم الحالي.
- رابط **اعتمادات legacy** في أسفل الصندوق → `Approvals/Pending`.

| Persona | ماذا يرى |
|---------|----------|
| `acc-br1-mgr` | خطوة 1 لطلبات الشاغر والعروض |
| `acc-a-management-approver` | خطوة 2 فقط |
| `acc-central-a` | لا يرى طلباته/عروضه (SoD) |
| `acc-a-compliance` | استئنافات معيّنة له كمراجع مستقل |

### المرحلة ب — التعيين (نهاية HCM / بداية FUN-001)

**الصفحة:** `RecruitmentApplications/Details` عند `OfferAccepted`

1. اضغط **تعيين** (Hire) — ينفّذ `RecruitmentHireClosureService`: شغل المنصب، إغلاق الشاغر، `ICandidateAccepted`.
2. **تحقق:** الحالة `Hired` + اسم الموظف + زر **متابعة المعالج**.

**أخطاء شائعة:** إذا ضغطت **تعيين** قبل قبول العرض، ستُرفض العملية — أكمِل خطاب العرض أولاً.

### الخطوة التالية — نموذج التعيين / HireWizard

بعد `Hired`، يُوجَّه المتصفح إلى `HireWizard/Continue?employeeId=...` (أو **متابعة المعالج** من قائمة الطلبات).

#### تبويب «إنشاء موظف جديد» (الافتراضي)

| الحقل | مطلوب؟ | مصدر القيمة |
|-------|--------|-------------|
| الرقم الوطني | نعم | من الطلب أو إدخال يدوي |
| الاسم الأول / الأوسط / العائلة | الأول والعائلة مطلوبان | يُقسَّم تلقائياً من اسم المتقدم (أول كلمة = أول، آخر كلمة = عائلة، الوسط = الباقي) |
| تاريخ الميلاد | نعم | إدخال يدوي — **لا قيمة افتراضية صامتة** |
| الجنس | نعم | اختيار من القائمة |
| الوحدة التنظيمية | لا | اختياري |
| تاريخ المباشرة المتوقع | لا | افتراضي: اليوم |
| فئة التوظيف | لا | افتراضي: `DEFAULT` |

#### تبويب «ربط موظف موجود»

| الحقل | مطلوب؟ | ملاحظة |
|-------|--------|--------|
| موظف موجود | نعم | قائمة منسدلة بأسماء الموظفين والرقم الوطني |
| تاريخ المباشرة / فئة التوظيف | — | نفس الحقول المشتركة أعلاه |

**متى تستخدم «ربط موجود»؟** عندما يكون سجل الموظف موجوداً مسبقاً في النواة (مثلاً أُدخل يدوياً من `Employees/Create`).

---

### الخطوة 4 — تأكيد التعيين

اضغط **تأكيد التعيين ومتابعة المعالج**.

**ماذا يحدث خلف الكواليس:**

```text
1. إنشاء Employee + سجل وظيفي أولي (إن اخترت «جديد»)
2. تحديث الطلب → Hired + ربط HiredEmployeeId
3. نشر ICandidateAccepted
   → HR.Contracts: عقد Pending + معاملة Appointment + checklist
   → تعليم P1.1 (استلام المرشح) + P2.1 (الملف الشخصي)
4. نشر IEmployeeCreated
5. توجيه المتصفح إلى HireWizard/Continue?employeeId=...
   → إن وُجدت المعاملة: Step2 (قائمة التحقق)
   → إن لم تُنشأ بعد: رسالة انتظار + العودة لقائمة المعاملات
```

> **FUN-001:** كل بند checklist له مالك/SLA/دليل (`OwnerRole`/`SlaHours`/`EvidenceRef`). اكتمال الرحلة يتطلب ACK أطراف الـsaga بما فيها **Auth.Directory**. عند الإلغاء قبل الإكمال يُرسل تعويض خفيف (`Compensate=true`) — انظر [FUN_001_RECONCILIATION_CHECKLIST.md](./FUN_001_RECONCILIATION_CHECKLIST.md).

**تحقق فوري:**

| ماذا تتحقق | أين | النتيجة المتوقعة |
|------------|-----|------------------|
| رسالة نجاح | بعد الإرسال | «تم تعيين … بنجاح. جاري فتح معالج التعيين…» |
| حالة الطلب | قائمة التوظيف | `Hired` + اسم الموظف |
| الموظف الجديد | `Employees/Index` | يظهر بالاسم والرقم الوطني (إن أنشأت جديداً) |
| معاملة التعيين | `HireWizard` | معاملة `Appointment` بحالة `InProcess` واسم المرشح في الملاحظات |
| قائمة التحقق | `HireWizard/Step2` | بندا **استلام المرشح** و**الملف الشخصي** ✓ |

**إن لم يُفتح المعالج تلقائياً:** انتظر 5–15 ثانية ثم اضغط **متابعة المعالج** من قائمة الطلبات (للطلبات `Hired`).

---

## 3) بقية مسار التعيين (بعد التوظيف)

### خريطة كاملة

```text
[التوظيف] طلب → مختصر → تعيين
    → ICandidateAccepted + IEmployeeCreated
[العقود] عقد Pending + معاملة Appointment
    → إثبات مباشرة → تفعيل عقد → IContractActivated
[الرواتب] ملف أجر نشط
[البطاقات] إصدار بطاقة → ICardIssued
[المعالج] Step2 → Step3 (اعتماد عليا) → Step4 (إكمال)
    → IHireCaseCompleted → أرشفة → ديوان
```

### قائمة التحقق السبعة

| البند | الرمز | كيف يُكمّل |
|-------|-------|------------|
| استلام المرشح | P1.1 | تلقائي عند التعيين (`ICandidateAccepted`) |
| تصنيف وعقد | P1.2 | إثبات مباشرة في العقود |
| الملف الشخصي | P2.1 | تلقائي عند التعيين (إنشاء موظف + `ProcessCandidateAccepted`) |
| ربط الراتب | P6.1 | تفعيل العقد |
| اعتماد السيرة | P5.1 | خطوة 3 في المعالج أو `EmploymentRecords/Approve` |
| الهوية | P8.1 | إصدار بطاقة |
| أرشفة التعيين | P7.1 | إكمال المعاملة (خطوة 4) |

---

### المرحلة ب — العقد وإثبات المباشرة

**الحساب:** `hr-contracts`  
**الروابط:**

| الإجراء | الرابط |
|---------|--------|
| العقود | `http://localhost:5000/ui/hr-contracts/Contracts` |
| إثبات مباشرة | `http://localhost:5000/ui/hr-contracts/CommencementRecords/Create` |

1. ابحث عن عقد الموظف (حالة `Pending`)
2. أنشئ **إثبات مباشرة** لنفس الموظف والعقد
3. من تفاصيل العقد → **تفعيل العقد** (`Active`)

---

### المرحلة ج — الرواتب

**الحساب:** `payroll`  
`http://localhost:5000/ui/payroll/EmployeePayProfiles` — تأكد `IsActive = true` بعد تفعيل العقد.

---

### المرحلة د — السجل الوظيفي (اختياري)

**الحساب:** `hr-core`  
`http://localhost:5000/ui/hr-core/EmploymentRecords/Index?employeeId={id}`

يُنشأ تلقائياً عند التعيين بحالة «بانتظار اعتماد». الاعتماد الرسمي يتم من **خطوة 3** في المعالج.

---

### المرحلة هـ — بطاقة الهوية

**الحساب:** `idcard`

1. `http://localhost:5000/ui/idcard/CardRequests/Create` — طلب بطاقة
2. `http://localhost:5000/ui/idcard/IssuedCards/Create` — إصدار البطاقة

---

### المرحلة و — معالج التعيين

**المدخل:** `http://localhost:5000/ui/hr-contracts/HireWizard`  
**أو بعد التعيين:** `HireWizard/Continue?employeeId=...` (تلقائي)

| الخطوة | الرابط | الحساب | الإجراء |
|--------|--------|--------|---------|
| القائمة | `/HireWizard` | `hr-contracts` | مراجعة المعاملات المفتوحة |
| 2 — قائمة التحقق | `/HireWizard/Step2?transactionId=...` | `hr-contracts` | راجع البنود السبعة → التالي |
| 3 — اعتماد عليا | `/HireWizard/Step3?transactionId=...` | `hr-management-approver` أو `hr-admin` | اعتماد وبدء التنفيذ |
| 4 — إكمال | `/HireWizard/Step4?transactionId=...` | `hr-contracts` | إكمال المعاملة عند اكتمال كل البنود |

> **Step1** لم يعد مستخدماً — يُعاد توجيهه إلى القائمة. لا حاجة لإدخال GUID.

---

### المرحلة ز — التحقق النهائي

| ماذا | أين |
|------|-----|
| معاملة مؤرشفة | `ServiceTransactions?type=Appointment` |
| عنصر أرشيف | `records-archive/ArchiveItems` (مرجع `HIRE-{transactionId}`) |
| مراسلة ديوان | `hr-core/DiwanCorrespondences` |
| تتبع الأحداث | `Home/Operations?correlationId=...` |

---

## 4) سيناريوهات اختبار

### أ) المسار المعياري (موظف جديد من الطلب)

**الحساب:** `hr-admin` | **المدة:** ~30 دقيقة

1. تأكد من وجود `ContractType` برمز `DEFAULT`
2. `RecruitmentApplications` → طلب جديد (اسم + وظيفة + رقم وطني)
3. مراجعة → مختصر → **تعيين**
4. تبويب «إنشاء موظف جديد» → أكمل تاريخ الميلاد والجنس → **تأكيد**
5. يُفتح Step2 في المعالج (أو **متابعة المعالج** بعد انتظار قصير)
6. `CommencementRecords/Create` → تفعيل العقد
7. `IssuedCards/Create` — إصدار بطاقة
8. HireWizard Step3 (اعتماد) → Step4 (إكمال)
9. تحقق: معاملة `Archived` + أرشيف + ديوان

### ب) ربط موظف موجود

**متى:** الموظف مُسجَّل مسبقاً في `Employees`

1. أنشئ موظفاً من `Employees/Create` (أو استخدم موجوداً)
2. طلب توظيف → مراجعة → مختصر → **تعيين**
3. تبويب **ربط موظف موجود** → اختر من القائمة → **تأكيد**
4. تابع من **متابعة المعالج** أو التوجيه التلقائي

### ج) التحقق من نقاط التكامل (للمختبرين)

| # | الإجراء | النتيجة المتوقعة |
|---|---------|------------------|
| 1 | تعيين من `Shortlisted` | الطلب → `Hired` |
| 2 | تعيين بإنشاء جديد | موظف + سجل وظيفي في النواة |
| 3 | بعد التعيين | معاملة `Appointment` في العقود |
| 4 | Step2 | P1.1 و P2.1 مكتملان فوراً |
| 5 | إثبات مباشرة | P1.2 ✓ |
| 6 | تفعيل عقد | P6.1 ✓ + عقد `Active` |
| 7 | إصدار بطاقة | P8.1 ✓ |
| 8 | Step3 اعتماد | P5.1 ✓ |
| 9 | Step4 إكمال | P7.1 ✓ + `IHireCaseCompleted` |

---

## 5) استكشاف الأخطاء

| المشكلة | السبب | الحل |
|---------|-------|------|
| زر **تعيين** غير ظاهر | الطلب ليس `Shortlisted` | مراجعة → مختصر |
| «يمكن التعيين فقط للقائمة المختصرة» | حالة خاطئة | أعد تجهيز الطلب |
| «لا توجد معاملة تعيين جاهزة بعد» | تأخر RabbitMQ | انتظر 5–15 ثانية → **متابعة المعالج** |
| لا معاملة بعد انتظار طويل | الخدمات متوقفة أو لا `DEFAULT` | راجع `UpstreamHealth` ونوع العقد |
| لا عقد تلقائي | لا نوع `DEFAULT` | أنشئه في `ContractTypes` |
| فشل تفعيل العقد | لا إثبات مباشرة | `CommencementRecords/Create` |
| بند الراتب معلق | العقد غير مفعّل | Activate |
| بند الهوية معلق | لا بطاقة | `IssuedCards/Create` |
| زر إكمال معطّل | checklist ناقص | راجع Step2 |
| 403 خطوة 3 | بلا `hr-management-approver` | `hr-admin` أو مستخدم اعتماد |
| حقول الاسم فارغة في التعيين | اسم متقدم بكلمة واحدة | عدّل يدوياً في النموذج |

---

## 6) اختبار آلي (للمطورين)

```bash
# قواعد سير عمل التعيين
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~HireWorkflow|FullyQualifiedName~RecruitmentHire|FullyQualifiedName~HireWizard"

# تكامل التوظيف + معاملة التعيين
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~HireCase|FullyQualifiedName~RecruitmentHire"
```

---

## 7) ملخص الروابط

| الغرض | URL |
|-------|-----|
| البوابة | `http://localhost:5000` |
| مسار التوظيف (دليل البوابة) | `http://localhost:5000/Home/HireJourney` |
| **طلبات التوظيف** | `http://localhost:5000/ui/hr-core/RecruitmentApplications` |
| **نموذج التعيين** | `.../RecruitmentApplications/Hire/{applicationId}` |
| الموظفون | `http://localhost:5000/ui/hr-core/Employees` |
| معالج التعيين | `http://localhost:5000/ui/hr-contracts/HireWizard` |
| متابعة بعد التعيين | `http://localhost:5000/ui/hr-contracts/HireWizard/Continue?employeeId={id}` |
| العقود | `http://localhost:5000/ui/hr-contracts/Contracts` |
| إثبات المباشرة | `http://localhost:5000/ui/hr-contracts/CommencementRecords` |
| ملفات الأجر | `http://localhost:5000/ui/payroll/EmployeePayProfiles` |
| البطاقات | `http://localhost:5000/ui/idcard/IssuedCards` |
| الأرشيف | `http://localhost:5000/ui/records-archive/ArchiveItems` |
| الديوان | `http://localhost:5000/ui/hr-core/DiwanCorrespondences` |

---

## 8) خطوات اختيارية (لا تمنع الإكمال)

| الخدمة | الرابط | الدور |
|--------|--------|-------|
| الدليل والهوية | `/ui/auth-directory` | `auth-directory` |
| تقنية المعلومات | `/ui/it-infra` | `it-infra` |
| التدريب | `/ui/hr-training` | `hr-training` |
| الفحص الصحي | `/ui/hr-health` | `hr-health` |

---

*آخر تحديث: 2026-05-29 — دليل عملية التوظيف: طلب → مختصر → تعيين (نموذج) → متابعة تلقائية للمعالج.*

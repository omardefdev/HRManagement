# PLAN-30 — تجربة المرشح والقناة الموحدة والإشعارات (PLAN-HCM-H)

**Scope:** HCM-001/HCM-006 + COM-001 + GOV-001 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-16, G-17, G-18، وجزء من G-20 (الاحتفاظ بالبيانات)  
**Status:** Proposed  
**Priority:** P1 (التوحيد والدقة) / P2 (الخدمة الذاتية)  
**Depends on:** PLAN-26 (تاريخ الإغلاق)، PLAN-27 (حالات العرض والاحتياط)، PLAN-28 (تقديم الاستئناف)، PLAN-29 (PDF العرض) — كلها منفذة. PLAN-25 مؤجلة  
**Decisions:** D-4 (قناة الخدمة الذاتية)، D-8 (مزوّد البريد/SMS)  
**تعديلات قبل التنفيذ (2026-10-23):**
- **التنفيذ على ثلاث جلسات:** (1) توحيد القناة والإسقاط العام وسجل الأحداث والترحيل والأهلية؛ (2) المرفقات والإشعارات والاحتفاظ؛ (3) البوابة بـ OTP والقبول بالنيابة والخصوصية والوصولية والقبول والتوثيق.
- **الربط:** الربط موجود أصلاً في `CitizenApplication.PromotedRecruitmentApplicationId` و`InternalApplication.PromotedRecruitmentApplicationId`، فيُعاد استخدامه مع فهرس فريد بدل عمود جديد `SourceCitizenApplicationId`. يُضاف `IntakeChannel` على طلب التوظيف للتمييز بين القنوات.
- **حالة العرض:** `OfferExtended` تُضبط عند إنشاء **مسودة** العرض (`RecruitmentOfferService.CreateDraftAsync`)، لا عند إصداره. لذلك يعتمد الإسقاط العام وإشعار «عرض صادر» على `OfferLetter.Status = Issued`.
- **سجل الأحداث:** الحدث الداخلي `RecruitmentApplicationStatusChanged` يُحفظ كجدول أحداث يُكتب في المعاملة نفسها، ويغذي الخط الزمني والإشعارات (ولاحقاً مؤشرات PLAN-32).
- **الرفع:** `POST /api/files` في File.Storage يتطلب مستخدماً مصادقاً، والمرشح مجهول. لذلك يمر الرفع عبر HR.Core بهوية الخدمة، بسياسة S2S جديدة على نمط `payroll.grades.read`.
- **الإشعارات:** `INotificationService` عقد صندوق داخلي (`GetUnread`/`MarkRead`) لا يناسب مستلماً خارجياً بلا حساب. تُضاف واجهة مرسل للقناة بجانبه. لا يوجد مزوّد بريد/SMS بعد (D-8)، فيُستخدم محوّل FileDrop في المختبر، ويبقى الإنتاج معطّلاً دون إسقاط صامت للرسائل.
- **الراتب:** لا يظهر في أي رسالة، لا SMS ولا بريد، ويُعرض في البوابة بعد OTP.
- **الأهلية:** كتالوج المؤهلات في MDM (`DIP`/`BACH`/`MASTER`/`PHD`) بلا ترتيب، ولا حدود عمرية لأنظمة الخدمة في المستودع. الترتيب والحدود إعدادات، ولا تُخترع قيم قانونية.
- **جلسة البوابة:** البوابة (Gateway) لا تمرّر إلى الخدمات إلا كوكيز مكافحة التزوير، فيلزم سماح صريح لكوكي `careers_session` على مسار `/careers` فقط. ومحدّد المعدل الحالي نافذة واحدة مشتركة بين كل الزوار، فيُضاف حدّ لكل عنوان IP لنقاط OTP.
- **القبول بالنيابة:** `HrCoreApprove` ليست أضيق من `HrCoreWrite`، بل تضيف أدواراً. يبقى القبول بالنيابة على `HrCoreWrite`، مع سبب ومرفق إثبات إلزاميين وتدقيق.
- **الاحتفاظ:** `CanPurge(CitizenApplication)` يرفض كل طلب مرتبط، وبعد التوحيد تصبح كل الطلبات مرتبطة. لذلك يعالج الاحتفاظ الزوج (طلب القناة + طلب التوظيف) معاً، ويمتد الإخفاء إلى الحقول والمرفقات والإشعارات ورموز OTP. التشغيل تجريبي (dry-run) افتراضياً في الإنتاج حتى اعتماد مسؤول حماية البيانات.

## Goal

1. **مسار واحد للمرشح:** كل تقديم مرتبط بطلب شاغر يدخل `RecruitmentApplication` مباشرة، و`CitizenApplication` تبقى سجل القناة العامة وحالتها **مشتقة** من المسار الرئيسي.
2. **طلب كامل:** مرفقات عبر File.Storage، ومؤهلات وخبرة، وفحص أهلية آلي يُعلِّم ولا يرفض، وفترة تقديم واضحة.
3. **إشعارات** للمرشح في كل نقطة قرار، عبر البريد أو SMS بمحتوى مقلَّل للبيانات الشخصية.
4. **خدمة ذاتية آمنة على `/careers`:** متابعة دقيقة، ورد على العرض، وانسحاب، واستئناف، واستكمال مستندات.
5. **احتفاظ آلي** ببيانات المرشحين وفق GOV-001.

## الوضع الحالي

| البند | المرجع |
|-------|--------|
| حالات `CitizenApplication` منفصلة؛ HR يفرز في `PublicJobPostings/Details` ثم `PromoteFromCitizen` إلى `Shortlisted` | `RecruitmentApplicationsController.cs:489-545` |
| الترقية الداخلية لا تنقل COI | `:599` |
| `/careers/track` يعلّم «المقابلة» و«التعيين» منجزتين لمجرد الترقية | `PublicCareersController.cs:196, 204` |
| نموذج التقديم: اسم، رقم وطني، هاتف، بريد، ملاحظة فقط | `CitizenApplicationApplyViewModel.cs` |
| لا إشعار في أي خدمة توظيف؛ `INotificationService` له تنفيذ داخلي فقط | `ServiceDefaults/Notifications/InAppNotificationService.cs` |
| قبول/رفض العرض من HR نيابة عن المرشح | `OfferLettersController.AcceptOffer/DeclineOffer` |
| الاحتفاظ والإخفاء واجهة JSON يدوية بلا مهمة | `CandidateLifecycleController.cs` |

---

## In scope

### 1. توحيد القناة (G-16)

**القرار التصميمي:** `RecruitmentApplication` هو **سجل الحالة الوحيد**. `CitizenApplication` و`InternalApplication` سجلات قناة (بيانات التقديم والهوية الخارجية والرقم المرجعي).

| التغيير | التفاصيل |
|---------|----------|
| إنشاء آلي | `PublicCareersController.Apply` لإعلان مرتبط بطلب شاغر ينشئ في الـ transaction نفسها `RecruitmentApplication` بحالة `Submitted` و`SourceCitizenApplicationId` (Migration `HCM030_UnifiedIntake`)؛ والأمر نفسه في `EssTalentController.Apply` مع نقل COI |
| إيقاف الترقية اليدوية | `PromoteFromCitizen`/`PromoteFromInternal` للإعلانات المرتبطة بطلب شاغر تُعطَّل (تبقى للإعلانات القديمة `IsLegacyUnlinked`) |
| شاشة الفرز | أزرار تغيير الحالة في `PublicJobPostings/Details` و`TalentMarketplace/Details` تُستبدل برابط إلى `RecruitmentApplications?requisitionId=…`، فيصبح الفرز في مكان واحد |
| الحالة العامة | `CandidatePublicStatusProjection` (Domain) تربط الحالة الداخلية بالحالة العامة (الجدول أدناه)؛ و`CitizenApplication.Status` يصبح محسوباً عند القراءة، أو يُحدَّث عبر معالج حدث داخلي `RecruitmentApplicationStatusChanged` |
| backfill | `scripts/hcm030-backfill-unified-intake.ps1` (على نمط `hcm001-migrate-legacy-applications.ps1`): كل `CitizenApplication` مرتبطة بطلب شاغر وبلا ترقية ← إنشاء `RecruitmentApplication` بحالة مكافئة |

**جدول الإسقاط العام:**

| الحالة الداخلية | ما يراه المرشح |
|-----------------|----------------|
| Submitted / UnderReview | قيد المراجعة |
| Shortlisted / InterviewScheduled | مدعو للمقابلة (+ الموعد) |
| Assessed / Ranked | قيد التقييم النهائي |
| OfferExtended (`Issued`) | عرض بانتظار ردك حتى (تاريخ) |
| OfferAccepted / Hired | مقبول — بانتظار استكمال التعيين |
| ReserveListed | في قائمة الاحتياط حتى (تاريخ) |
| OfferDeclined | العرض منتهٍ/مرفوض |
| Rejected | غير مقبول — يمكنك الاستئناف حتى (تاريخ) |
| Withdrawn | منسحب |

### 2. اكتمال الطلب والأهلية (G-17)

- **حقول التقديم** (Migration `HCM030_ApplicationProfile` على `CitizenApplication`، تُنسخ إلى `RecruitmentApplication` كـ snapshot):
  - `EducationLevel` من قائمة MDM
  - `Specialization`
  - `GraduationYear`
  - `YearsOfExperience`
  - `BirthDate` (للأهلية العمرية حسب النظام)
- **المرفقات** عبر File.Storage (`/api/files` مع فحص ClamAV الموجود):
  - كيان `CandidateDocument` (ApplicationId, Type CV/Certificate/IdCopy/Other، FileId).
  - الحد 5 ملفات × 5MB، والأنواع pdf/jpg/png.
  - الرفع عبر `IFileReferenceOutbox`.
  - المرفق الإلزامي يُحدَّد لكل طلب شاغر (`RequiredDocumentTypes`).
- **الأهلية** `RecruitmentEligibilityRules`: شروط من طلب الشاغر (أدنى مؤهل، مجال عمري من نظام الخدمة، سنوات خبرة) تُقيَّم عند التقديم ← `EligibilityFlags` على الطلب (مثل «دون المؤهل المطلوب»). **لا رفض آلي**؛ تظهر لـ HR في الفرز، ويتطلب تجاوزها سبباً.
- **فترة التقديم:** تُعرض بوضوح، وتُرفض الطلبات بعدها (المنطق في PLAN-26).

### 3. الإشعارات (G-18)

- **القنوات (D-8):** `EmailNotificationChannel` (SMTP عبر MailKit أو مكتبة معتمدة في `LICENSE_POLICY.md`) و`SmsNotificationChannel` (محوّل HTTP لبوابة SMS)، يطبّقان `INotificationService` مع `NotificationChannel.Email/Sms` الموجودين، ومحوّل `FileDrop` للمختبر يكتب الرسائل في مجلد لفحصها في القبول.
- **الأحداث:** `RecruitmentNotificationHandler` يستمع لحدث داخلي `RecruitmentApplicationStatusChanged` (+ أحداث المقابلة والعرض):

| الحدث | المحتوى |
|-------|---------|
| استلام الطلب | الرقم المرجعي ورابط المتابعة |
| مقابلة مجدولة/معدلة/ملغاة | الموعد والمكان أو الرابط |
| عرض صادر | رابط البوابة وتاريخ الانتهاء — **دون** الراتب في SMS |
| تذكير قبل انتهاء العرض بـ 48 ساعة | من `OfferExpiryJob` في PLAN-27 |
| عدم القبول | نافذة الاستئناف |
| قائمة الاحتياط | — |
| قبول نهائي | الخطوات التالية |

- **الضوابط:**
  - قوالب عربية في `Resources`.
  - مفتاح idempotency (`ApplicationId + EventType + Version`).
  - تدقيق إرسال، بلا رقم وطني في أي رسالة.
  - موافقة المرشح على القناة عند التقديم.

### 4. الخدمة الذاتية على `/careers` (G-18)

- **الدخول (D-4):** الرقم المرجعي + الرقم الوطني ← رمز لمرة واحدة (OTP، 6 أرقام، صلاحيته 10 دقائق، 5 محاولات) إلى البريد أو الهاتف المسجل ← جلسة قصيرة (cookie مستقل `careers_session`، 30 دقيقة) مع rate limit الموجود.
- **صفحة «طلبي»:** الحالة العامة الدقيقة، والخط الزمني، والمقابلات، والمستندات (رفع ما ينقص قبل الإغلاق).
- **الإجراءات:**

| الإجراء | الاستدعاء |
|---------|-----------|
| عرض PDF العرض | PLAN-29 |
| قبول العرض | `RecruitmentOfferService.AcceptAsync` مع `acceptedBy=Candidate` وإقرار نصي |
| رفض العرض بسبب | `DeclineAsync(declinedBy: Candidate)` |
| الانسحاب من التقديم | `Withdrawn` |
| تقديم استئناف ضمن النافذة | `RecruitmentAppealService.FileAppealAsync` دون مراجع، والتعيين لاحقاً (PLAN-28) |

- **HR:** يبقى زر «قبول نيابة عن المرشح» مع سبب إلزامي ومرفق إثبات (توقيع ورقي)، بسياسة `HrCoreApprove`.
- **الوصولية:** توسيع `scripts/hcm006-a11y-scan.ps1` ليشمل الصفحات الجديدة.

### 5. الاحتفاظ الآلي (جزء G-20)

- `CandidateRetentionJob` (أسبوعي، قفل موزع): يطبّق جدول `GOV_001_RETENTION_SCHEDULE.md` على طلبات المرشحين غير المعيَّنين بعد إغلاق الطلب بـ N شهراً، باستخدام `CandidateApplicationLegalLifecycleService` (إخفاء الهوية)، ويحترم `LegalHold` والاستئنافات المفتوحة.
- **شاشة إدارية** `CandidateLifecycle/Index` (`HrCoreDelete`): المستحقون، وتشغيل تجريبي dry-run، وسجل التنفيذ. الـ endpoints الحالية تبقى للخدمات.

---

## Out of scope

- تطبيق جوال للمرشحين (MOB-001)
- التحقق من الهوية عبر السجل المدني الحي (EXT-002) — يُستخدم الـ mock الحالي إن لزم
- منتقى المراجع وقرارات الاستئناف ← PLAN-28

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-30a | تقديم مواطن ← `RecruitmentApplication` بحالة `Submitted` فوراً، ويظهر في قائمة HR للطلب | — |
| AC-HCM-30b | رفض HR للطلب ← `/careers/track` يُظهر «غير مقبول + نافذة الاستئناف» | ترقية الطلب لم تعد تُظهر «التعيين ✓» |
| AC-HCM-30c | تقديم مع CV وشهادة ← المستندات ظاهرة لـ HR | ملف exe أو > 5MB ← رفض؛ ملف مصاب (EICAR) ← حجر |
| AC-HCM-30d | مؤهل أدنى من المطلوب ← علم أهلية ظاهر، والطلب مقبول للفرز | — |
| AC-HCM-30e | جدولة مقابلة ← رسالة بريد/SMS في محوّل الملفات بلا رقم وطني | تكرار الحدث ← رسالة واحدة (idempotency) |
| AC-HCM-30f | المرشح يدخل بـ OTP ← يقبل العرض ← `OfferAccepted` | OTP خاطئ 5 مرات ← قفل مؤقت؛ رقم مرجعي لشخص آخر ← لا وصول |
| AC-HCM-30g | المرشح يرفض العرض بسبب ← `OfferDeclined` + اقتراح التالي (PLAN-27) | — |
| AC-HCM-30h | المرشح المرفوض يقدّم استئنافاً ضمن النافذة | بعد النافذة ← الزر غير متاح والطلب مرفوض |
| AC-HCM-30i | مهمة الاحتفاظ ← إخفاء هوية مرشحين مستحقين | مرشح عليه `LegalHold` ← لا يُمس |
| AC-HCM-30j | backfill: طلبات مواطنين قديمة ← طلبات توظيف بحالات مكافئة دون تكرار | — |

## Verification

```powershell
powershell -File scripts/hcm030-validate.ps1
powershell -File scripts/hcm006-a11y-scan.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm030|CandidatePublicStatusProjection|RecruitmentEligibility"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm030|PublicCareers"
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcHcm30|AcHcm06"
```

## Definition of done

- لا شاشتا فرز للطلب الواحد، ولا خط زمني مضلل للمرشح.
- كل نقطة قرار تُنتج إشعاراً مُدقَّقاً، وكل رسالة خالية من الرقم الوطني.
- المرشح يرد على العرض ويستأنف بنفسه، ويبقى القبول بالنيابة مقيداً وموثقاً.
- الاحتفاظ آلي ومحترم للحجز القانوني.
- AC-HCM-30a..j = Pass، وفحص الوصولية دون أخطاء حرجة.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-30-hcm-candidate-experience.md` |
| Domain | `CandidatePublicStatusProjection.cs`، `RecruitmentEligibilityRules.cs`، `CandidateDocument`، حدث `RecruitmentApplicationStatusChanged` |
| Infra | `EmailNotificationChannel`، `SmsNotificationChannel`، `FileDropNotificationChannel`، `RecruitmentNotificationHandler`، `CandidateOtpService`، `CandidateRetentionJob` |
| Web | `/careers/my` (صفحات الخدمة الذاتية)، `CandidateLifecycle/Index` |
| Migrations | `HCM030_UnifiedIntake`، `HCM030_ApplicationProfile`، `HCM030_CandidateDocuments`، `HCM030_CandidateOtp` |
| Scripts | `scripts/hcm030-validate.ps1`، `scripts/hcm030-backfill-unified-intake.ps1` |
| Tests | `Hcm030UnifiedIntakeTests`، `Hcm030NotificationTests`، `Hcm030CandidatePortalTests`، `Hcm030RetentionJobTests`، `AcHcm30CandidateExperienceTests` |
| Docs | `HIRE_ONBOARDING_GUIDE.md`، `COM_001_NOTIFICATIONS.md` (القنوات الفعلية)، `GOV_001_RETENTION_SCHEDULE.md`، `docs/prv-ropa/processing-activities.json` (المعالجة الجديدة: OTP ومرفقات) |

## Effort estimate

10–12 يوم عمل: التوحيد + backfill (3)، الملف والمرفقات والأهلية (2)، قنوات الإشعار والمعالج (2)، الخدمة الذاتية + OTP (2.5)، الاحتفاظ (0.5)، القبول والوصولية (1.5).

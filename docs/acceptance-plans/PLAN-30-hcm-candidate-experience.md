# PLAN-30 — تجربة المرشح والقناة الموحدة والإشعارات (PLAN-HCM-H)

**Scope:** HCM-001/HCM-006 + COM-001 + GOV-001 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-16, G-17, G-18، وجزء من G-20 (الاحتفاظ بالبيانات)  
**Status:** Implemented (acceptance evidence pending)  
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
powershell -ExecutionPolicy Bypass -File scripts/hcm030-validate.ps1
powershell -ExecutionPolicy Bypass -File scripts/hcm006-a11y-scan.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm030|FullyQualifiedName~CandidatePublicStatusProjection|FullyQualifiedName~RecruitmentEligibility|FullyQualifiedName~Fs030"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm030|FullyQualifiedName~Fs030|FullyQualifiedName~PublicCareers"
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

## Implementation notes / deviations

Execution guide: [PLAN-30-EXECUTION](execution/PLAN-30-EXECUTION.md). Three sessions on branch
`feature/plan-30-candidate-experience` (from `main` @ `55fbcdd`); details in `reports/plan30-session1-handoff.md`,
`reports/plan30-session2-handoff.md` and `reports/plan30-final-report.md`. Status stays **acceptance evidence pending**:
AC-HCM-30a/b/d/e/f/g/h/i/j passed live (2026-10-29), AC-HCM-30c could not run because the pinned MinIO image can no longer
be pulled, so File.Storage was not up (see follow-ups); the test now runs its `.exe`/> 5 MB refusals first and skips the
File.Storage steps with that reason instead of failing.

**Session 1 — unified intake, events, projection, backfill, eligibility (A–D)**

- **Link columns reused** (`CitizenApplication`/`InternalApplication.PromotedRecruitmentApplicationId`, recreated as filtered
  unique indexes; the migration raises if duplicates exist) instead of a new `SourceCitizenApplicationId`; `IntakeChannel`
  (`HrEntered`/`Public`/`Internal`/`Legacy`) added on the application and back-filled by the migration (new column only).
- **`OfferExtended` is set at draft**, so the public projection and the «عرض صادر» message key off `OfferLetter.Status = Issued`
  (a Draft/PendingApproval offer shows «قيد التقييم النهائي»).
- **Events:** the internal `RecruitmentApplicationStatusChanged` is a `RecruitmentApplicationEvent` table written in the same
  save (change tracker, no PII, append-only except `NotificationsPlannedAt`); `FromStatus`/`ToStatus` hold the triggering
  entity's enum. Each event row also gets a generic `EntityAuditLog` row. Suppression scope for bulk fixes; backfilled and
  historic rows have no events and are never notified.
- Internal intake is one transaction with **two saves** (EF refuses the two-way insert cycle); public intake is one save.
- Old promote buttons replaced by «ربط بمسار التوظيف» for requisition-linked rows that are not yet linked. Channel-row status
  columns are no longer written for linked rows (documented on the properties).
- **Eligibility** flags only (never rejects); evaluated at intake only (HR-entered, backfilled and linked-later rows show
  «لم تُقيَّم»); qualification ranks and regime age bounds are options (no legal age data, Production empty). Shortlisting a
  flagged application needs a reason (≥ 10) and writes `EligibilityOverride`.
- Backfill: one idempotent SQL transaction (`SHARE ROW EXCLUSIVE` on both channel tables); `RejectedAt` is never "now" (no
  appeal window reopens); backfilled internal rows carry the employee's hash and no plaintext national id. The C# mapping is
  pinned against the SQL on PostgreSQL. `/careers/track` legacy timeline lost the two steps that only meant "promoted".

**Session 2 — documents, notifications, retention (E–G)**

- **Upload** (S2S scope `file-storage.service-upload`, policy `Service.FileStorageServiceUpload`, client `svc-hr-core` only,
  modelled on `payroll.grades.read`): a separate `FilesServiceUploadController` (`api/files/service-upload` + `metadata` +
  Clean-only `content`) so `ModuleFileStorage` is not ANDed in; HR.Core calls File.Storage **directly**
  (`Integration:FileStorageBaseUrl`, caller proof signed) — gateway unchanged. HR downloads go through an HR.Core proxy
  (`RecruitmentApplications/CandidateDocument/{id}`, Clean only) because HR users lack `ModuleFileStorage`. No File.Storage
  migration. Keycloak: provisioned by `sync-keycloak-dev-realm.ps1` (no realm re-import; the realm JSON templates do not
  carry the scope).
- Upload limits: 5 files, 5 MiB each, **9 MiB per apply post** (`MaxTotalBytes`, below the 10 MB Kestrel limit); any upload
  failure fails the whole apply with «تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.» and saves nothing. Required
  documents apply to public applications only (`MissingRequiredDocuments` flag). Outbox rows get the posting tenant explicitly.
- **Notifications:** a channel-sender abstraction (`INotificationChannelSender`: SMTP/MailKit 4.18.1, generic HTTP SMS, lab
  FileDrop) **beside** the unchanged in-app `INotificationService` (contrary to COM-001 decision 4 / D-8 wording); templates in
  one C# class; rendered at planning time; preferred channel only, with consent; idempotency key `{eventId}:{channel}`.
  **No salary in any message**, no name or national id; SMS one 70-char segment without link or location. Internal and
  HR-entered candidates get no email/SMS. The 48 h reminder is a dispatcher pass. Provider `None` in Production/Staging
  (D-8 pending): rows stay `Pending`, nothing is dropped silently.
- **Retention** treats the **pair** (application + channel row): requisition closed/cancelled + `Recruitment:Retention:Months`
  (36), not hired, no legal hold on either row, no pending appeal; half-anonymized pairs are completed. `Apply=false`
  (**dry run by default**; `true` only in acceptance) until DPO approval. `CandidateRetentionRun` is one row per tenant per run
  (no nullable tenant). Internal pairs are held via the application's `LegalHold`. Cutoff `trigger <= now.AddMonths(-n)`.
  `TryAnonymize` also clears profile, contact, consent, rejection and override texts (affects the DSAR/lifecycle erasures).

**Session 3 — OTP portal, gateway, candidate actions, on-behalf acceptance, privacy, acceptance (H–M)**

- **OTP (D-4):** `CandidateOtpChallenge` stores only `HMACSHA256(key, challengeId:code)` and a keyed hash of the client
  address; key `Recruitment:Portal:OtpHmacKey` (env, ≥ 32 chars). Production refuses to start without it; elsewhere a random
  per-process key is used (deviation from "derive through Data Protection": Data Protection offers no deterministic
  derivation; pending codes die with the process). Always the neutral message; silent refusal when locked, no contact, or
  within the 60 s resend window; one active challenge; constant-time verify; 5 wrong codes → 30-minute lock audited
  `CandidatePortalLocked`. Sent immediately through the channel senders; the `CandidateNotification` row stores the body
  redacted with a terminal status. Extra column `CitizenApplication.PortalOtpRequestedAt` (resend window).
- **Partitioned OTP limiter:** ASP.NET Core applies one endpoint policy, so `careers-otp` is a chained global limiter = the
  existing shared `public-careers` window + a per-client 10-minute window (5 requests / 20 checks). HR.Core does not run
  `UseForwardedHeaders`; `CareersClientIpResolver` uses the right-most `X-Forwarded-For` entry only when the peer is a trusted
  proxy (`ForwardedProxyTrust`). Arabic 429 page for both policies (Apply/Track returned an empty 429 before). Acceptance
  raises the per-client limits to 60/120 (all tests share one address).
- **Session:** `careers_session` (Data Protection purpose `HR.Core.CareersSession.v1`, absolute 30 minutes in the payload),
  HttpOnly, SameSite=Strict, path = request path base (`/careers`), Secure always in Production/Staging and same-as-request
  elsewhere. No TempData on the portal (the gateway strips it); the code form carries the reference.
- **Gateway allow-list:** the transform forwards the exact cookie name `careers_session` only on route `careers-public`
  (`/careers/{**catch-all}`), next to the antiforgery cookies, and additionally **never attaches a staff access token** on that
  route (a signed-in HR browser's `tenant_id` claim would otherwise win over the session tenant). Other routes unchanged.
- **Portal actions** resolve the application/offer/appeal from the session only, and run existing services with the session
  tenant as ambient tenant (`TenantIdFlow`, restored afterwards) so a client `X-Tenant-ID` cannot redirect them.
  Accept needs the acknowledgement + typed full name (Arabic letter variants ignored); decline uses the same HCM-027 next-offer
  draft as HR (`DeclineAndDraftNextAsync`, moved out of the controller). **Withdraw only before `OfferAccepted`** (an accepted
  offer has no workflow exit; the portal shows «تم قبول العرض؛ للانسحاب يرجى التواصل مع جهة التوظيف.»); the projection's
  `CanWithdraw` was narrowed accordingly; Issued → Declined («انسحاب المرشح: …»), Draft/PendingApproval → Revoked with the
  workflow cancelled; HCM-027 auto-draft and the next-candidate suggestion now also accept a Withdrawn application whose offer
  was settled by the withdrawal. `/careers/track` stays as a read-only lookup showing the public status label only.
  Notification links point to `/careers/my` (`CandidatePortalUrl()`). `hcm028`/`hcm029-validate.ps1` marker checks now assert
  the implemented symbols.
- **On-behalf acceptance:** **`HrCoreWrite` kept** (`HrCoreApprove` adds `HrManagementApprover`/`PlatformAdmin`, i.e. wider);
  restricted by a mandatory reason (≥ 20) + evidence file + `AcceptOfferOnBehalf` audit. The evidence is relayed server-side
  through the candidate document service upload (pdf/jpg/png ≤ 5 MB, scanned) instead of the browser upload widget (needs
  `ModuleFileStorage`), registered as `hr-core:OfferLetter:{id:N}:AcceptanceEvidence`; Clean-only download through HR.Core.
  Extra: optional decline evidence (`DeclineEvidenceFileId`, migration `HCM030_OfferDeclineEvidence`); the native `confirm()`
  on the accept form became `data-hr-confirm`. The old `AcceptAsync(offerId)` overload records `HrOnBehalf`. AcHcm27 ×2, 28 ×1,
  29 ×2 now send a reason and a PDF.
- **Privacy:** retention also deletes the pair's OTP rows, releases on-behalf offer evidence, clears
  `PortalLockedUntil`/`PortalOtpRequestedAt` (inside `TryAnonymize`, so DSAR erasure too) and replaces the candidate free
  text of offers (`AcceptanceNote`, `DeclineReason`, `RevokeReason`) and appeals (`Reason`, `AdditionalInfo`) with «—»
  (statuses, dates, channel and the appeal decision kept; beyond the K1 minimum, from the session-3 review); pairs anonymized
  earlier with such residue are due again. DSAR exports profile, channel, flags, document metadata, masked message log, offers (no salary) and
  OTP counts only; digit runs ≥ 9 in file names are masked (the Compliance sanitizer would drop the array). New
  `PRV_001_DPIA_RECRUITMENT.md` and ROPA activities for OTP, documents and notifications (processor «يُحدَّد عند D-8»).
- **Accessibility:** `hcm006-a11y-scan.ps1` covers 10 views (public careers + `/careers/my` login/code/index + layout);
  every check blocks (also no inline styles/handlers, no `confirm()`, icons `aria-hidden`). The apply form keeps native
  browser validation (a server re-render loses the chosen files) plus a focusable error summary.
- **Acceptance:** fixtures in `AcceptanceCandidateExperiencePatch` (runs at every start, adds missing rows only) rather than
  inside `AcceptanceDataSeeder`; 30i uses the default 36 months with a requisition cancelled four years earlier; 30f/g create
  their own requisition; citizen client uses a manual cookie header; backfill gains `-DockerContainer`;
  `prepare-keycloak-import.ps1` uses `SHA256.Create()` (Windows PowerShell 5.1).
- **D-8 provider pending:** Production/Staging keep `Notifications:Candidate:Provider=None`; with no provider no OTP can be
  sent, so `/careers/my` sign-in only works once D-8 is decided.

**Deferred follow-ups**

- ~~`FileReferenceOutboxDispatcher` reads the outbox under the tenant filter and sends no `X-Tenant-ID`~~ — **fixed** after
  PLAN-30 (branch `fix/file-reference-tenant-filters`): rows of every tenant are read through
  `IgnoreTenantFilterForSystem(BackgroundQueueProcessing)` and sent with their own `TenantId` as `X-Tenant-ID`.
- ~~`FilePlatformService.CollectGarbageAsync` has no tenant bypass~~ — **fixed behind a guard**: the GC job collects and
  purges tenant-owned orphans only with `FileStorage__GarbageCollection__AllTenants=true` (default `false`), to be set only
  after the dispatcher fix is deployed and its Register backlog is drained (`docs/FILE_SERVICE.md`, Garbage collection).
  Both jobs still read nothing in Production until the "Production RLS" item below is solved.
- `RegisterReference` and Module grants: decided not to add a Module grant to API-uploaded files (`docs/FILE_SERVICE.md`,
  Access grants on RegisterReference).
- Production Data Protection key ring for hr-core: `docker-compose.production.yml` sets no `DataProtection__KeyRingPath` for
  `hr-core-web`, so `careers_session` cookies (and PII field encryption) do not survive a restart or reach another replica.
- `AuditSaveChangesInterceptor` copies old values of non-`[AuditRedact]` columns into `EntityAuditLog` on anonymization
  (names, contact, cover note, notes, profile except `BirthDate`, and the offer/appeal free text that retention now
  neutralizes — K1 fix: `AcceptanceNote`, `DeclineReason`, `RevokeReason`, appeal `Reason`/`AdditionalInfo` → «—»).
- Production RLS: cross-tenant jobs (notification dispatcher, retention job, every recruitment job, the file-reference
  outbox dispatcher) run under `hr_core_app` without `app.current_tenant` and read no rows on RLS-hardened tables; the same
  holds for File.Storage's GC and scan jobs under `file_storage_app`.
- Production compose: hr-core `ServiceIdentity__Scope` lacks `file-storage.service-upload`, and there is no
  `Integration__FileStorageBaseUrl` or `Recruitment__PublicPortalBaseUrl`; the production realm needs the scope linked to
  `svc-hr-core`. Until then every apply with files fails with the Arabic message.
- D-8: choose the SMTP/SMS provider (and processor contract); until then Production sends no candidate message and no OTP.
- Internal-candidate notifications (ESS) — out of scope; internal candidates follow their status in ESS only.
- `Hcm028AppealGovernanceMigrationTests.Legacy_appeal_notes…` formats a date with the current culture (fails on ar-SA/Hijri).
- AC-HCM-30c live run (MinIO image), and the File.Storage paths of the AC-HCM-27/28/29 on-behalf acceptance tests.
- Remaining `// PLAN-30:` comments ("notify candidate", "alert compliance", requisition-cancellation notice) are covered by
  the recorded events or left as follow-ups (list with file:line in the final report).

## Effort estimate

10–12 يوم عمل: التوحيد + backfill (3)، الملف والمرفقات والأهلية (2)، قنوات الإشعار والمعالج (2)، الخدمة الذاتية + OTP (2.5)، الاحتفاظ (0.5)، القبول والوصولية (1.5).

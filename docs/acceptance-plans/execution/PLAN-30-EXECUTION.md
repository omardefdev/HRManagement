# PLAN-30-EXEC — تجربة المرشح والقناة الموحدة والإشعارات (HCM-H)

تنفيذ [PLAN-30](../PLAN-30-hcm-candidate-experience.md) لإثبات **AC-HCM-30a..j** على ثلاث جلسات.
التسليمات: [`reports/plan30-session1-handoff.md`](../../../reports/plan30-session1-handoff.md) (التوحيد، الأحداث، الإسقاط العام،
الترحيل، الأهلية)، [`reports/plan30-session2-handoff.md`](../../../reports/plan30-session2-handoff.md) (المستندات، الإشعارات، الاحتفاظ)،
والتقرير الختامي [`reports/plan30-final-report.md`](../../../reports/plan30-final-report.md) (الجلسة 3: OTP، `/careers/my`، القبول بالنيابة،
الخصوصية والوصولية، القبول، التوثيق).

## المتطلبات

- **الصور:** تتغير `hr-core-web` و`file-storage-web` و`hr-gateway` (ترحيلات HR.Core الثمانية `HCM030_*` تُطبَّق عند الإقلاع؛ لا ترحيل
  في File.Storage؛ سماح كوكي `careers_session` في `HRManagement.ServiceDefaults`). قبل `acceptance-up` شغّل
  `powershell -ExecutionPolicy Bypass -File scripts/docker-generate-build-manifest.ps1`: `docker/build-manifest` (غير متتبَّع)
  لا يُعاد توليده في `acceptance-up.ps1`، وإن كان قديماً يفشل بناء الصور (MailKit غير مستعاد).
- **Keycloak — نطاق `file-storage.service-upload`:** لا إعادة استيراد للـ realm. `scripts/sync-keycloak-dev-realm.ps1` (يشغّله
  `acceptance-up.ps1` و`dev-profile-up.ps1`) ينشئ النطاق ويضيفه إلى `svc-hr-core`. على Keycloak قائم: شغّل السكربت **ثم أعد إنشاء
  `hr-core-web`** (`docker compose … up -d --force-recreate --no-deps hr-core-web`)، وإلا يفشل كل طلب رمز خدمة من HR.Core بـ
  `invalid_scope`. على Windows PowerShell 5.1 اضبط قبله
  `[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)` (تمرير JSON إلى docker يضيف BOM).
- **مفتاح OTP — `HR_CORE_OTP_HMAC_KEY`:** `Recruitment:Portal:OtpHmacKey` (32 حرفاً على الأقل، سري، env فقط). الإنتاج لا يقلع دونه
  (`CandidateOtpStartupValidator`؛ `docker-compose.production.yml` يشترط `HR_CORE_OTP_HMAC_KEY`؛ في k8s السر
  `/hrmanagement/production/hr-core/otp-hmac`). في Development/القبول يُستخدم مفتاح عشوائي لكل عملية مع تحذير في السجل: الرموز المعلّقة لا
  تنجو من إعادة تشغيل `hr-core-web` (عمرها 10 دقائق).
- **FileDrop (رسائل المرشح في المختبر):** القبول يربط `./artifacts/acceptance/candidate-notifications` على
  `/var/hrm/candidate-notifications` (`Notifications__Candidate__Provider: FileDrop`). `acceptance-up.ps1` ينشئ المجلد ويمنحه للمستخدم 1654.
  اسم الملف `sha256(idempotencyKey).json`؛ مفتاح رسالة الحالة `{eventId}:{Email|Sms}`، ومفتاح OTP `otp:{challengeId}:{channel}`. الملف
  يحوي النص الكامل (ورمز OTP غير محجوب — مختبر فقط)، وصف `CandidateNotifications` يحفظه محجوباً. Development: `artifacts/candidate-notifications`
  نسبةً إلى جذر المحتوى؛ docker التطويري `/tmp/candidate-notifications` داخل الحاوية. الإنتاج يرفض FileDrop.
- **File.Storage:** `Integration__FileStorageBaseUrl: http://file-storage-web:8080` (قبول/local). يعتمد على MinIO وClamAV. **عائق حالي:**
  `minio/minio:RELEASE.2025-04-22T22-12-26Z` لم يعد قابلاً للسحب (Docker Hub: «pull access denied»؛ quay.io: 401)، فـ AC-HCM-30c ومسارات
  إثبات القبول بالنيابة في AC-HCM-27/28/29 تفشل دون `file-storage-web` («Name or service not known»). يلزم قرار بصورة S3 بديلة معتمدة أو
  بيانات اعتماد للسجل.
- **Windows PowerShell 5.1:** `acceptance-up.ps1` يتوقف عند `docker image inspect … *> $null` مع `ErrorActionPreference=Stop`، و
  `acceptance-verify` يتطلب PowerShell 7. إن لم يتوفر `pwsh`: `compose up --no-deps` للخدمات، ثم مزامنة Keycloak (مع ترميز الإدخال أعلاه)،
  ثم سكربت أدوار PostgreSQL، ثم إعادة إنشاء `hr-core-web`. `scripts/prepare-keycloak-import.ps1` صار يعمل على 5.1
  (`SHA256.Create().ComputeHash`).
- **الإعادة:** `powershell -ExecutionPolicy Bypass -File scripts/acceptance-reset.ps1` (يحذف بيانات مشروع `hrm-acceptance` فقط).

### بيانات القبول (`AcceptanceCandidateExperiencePatch`)

يعمل عند كل إقلاع بعد حزمة البذر، بمعرّفات ثابتة (`AcceptanceSeedIds` 0512–0522)، ويضيف الناقص فقط (لا يُعاد ضبط ما غيّرته الاختبارات؛
أحداث الطلب مكبوتة للصفوف المبذورة):

| البيانات | الغرض |
|----------|--------|
| `REQ-2026-A-CX01` (منشور، حد أدنى `BACH` وسنتان خبرة، CV وشهادة مطلوبان، إعلان عام) | 30a–30h |
| `AC30-BF-0001` (Received) و`AC30-BF-0002` (UnderReview) — طلبا مواطن غير مرتبطين بتواريخ حديثة | 30j |
| `REQ-2022-A-RET1` (ملغى قبل أربع سنوات) + زوج مستحق `AC30-RET-0001` (مستند واحد) + توأم محجوز قانونياً `AC30-RET-0002` | 30i |

### الإعدادات

| الخدمة | المفتاح | الافتراضي | Development | القبول (compose) | Staging | Production | الجلسة |
|--------|---------|-----------|-------------|------------------|---------|------------|--------|
| HR.Core | `Recruitment:Eligibility:QualificationRanks` | `DIP=1, BACH=2, MASTER=3, PHD=4` | | | | | 1 |
| HR.Core | `Recruitment:Eligibility:AgeBoundsByRegime` | فارغ | | | | فارغ (لا قيم قانونية) | 1 |
| HR.Core | `Recruitment:CandidateDocuments:{MaxFiles,MaxBytes,MaxTotalBytes}` | 5 / 5,242,880 / 9,437,184 | | | | | 2 |
| HR.Core | `Integration:FileStorageBaseUrl` | فارغ (الرفع يفشل مغلقاً) | فارغ | `http://file-storage-web:8080` | — | غير مضبوط | 2 |
| HR.Core | `ServiceIdentity:Scope` | `file-storage.references file-storage.service-upload` | | + `payroll.grades.read` | | compose الإنتاج: `file-storage.references` فقط | 2 |
| HR.Core | `Notifications:Candidate:Provider` | `None` | `FileDrop` | `FileDrop` | **`None`** | **`None`** (حتى D-8) | 2 |
| HR.Core | `Notifications:Candidate:FileDrop:Path` | — | `artifacts/candidate-notifications` | `/var/hrm/candidate-notifications` | — | مرفوض | 2 |
| HR.Core | `Notifications:Candidate:MaxAttempts` | 5 | | | | | 2 |
| HR.Core | `Notifications:Candidate:Smtp:*` / `Sms:*` (السر `Smtp:Password`، `Sms:AuthToken` env فقط) | — | | | | عند D-8 | 2 |
| HR.Core | `Recruitment:PublicPortalBaseUrl` (رابط `/careers/my` في البريد) | فارغ (بلا رابط) | `http://localhost:5000` | `http://localhost:6200` | فارغ | فارغ | 2/3 |
| HR.Core | `Recruitment:Portal:PhoneCountryCode` | `963` | | | | | 2 |
| HR.Core | `Recruitment:Jobs:CandidateNotificationsEnabled` / `…IntervalSeconds` | `true` / 60 | | | | | 2 |
| HR.Core | `Recruitment:OfferReminderHours` | 48 | | | | | 2 |
| HR.Core | `Recruitment:Retention:Months` / `Apply` / `BatchSize` | 36 / **`false`** / 100 | | `Apply=true` | **`false`** | **`false`** (حتى اعتماد DPO) | 2 |
| HR.Core | `Recruitment:Jobs:CandidateRetentionEnabled` / `…IntervalMinutes` | `true` / 10080 | | | | | 2 |
| HR.Core | `Recruitment:Portal:OtpHmacKey` (env `Recruitment__Portal__OtpHmacKey`) | مفتاح عشوائي لكل عملية | | | | **مطلوب** (`HR_CORE_OTP_HMAC_KEY`) | 3 |
| HR.Core | `Recruitment:Portal:OtpResendSeconds` / `OtpValidityMinutes` | 60 / 10 | | | | | 3 |
| HR.Core | `Recruitment:Portal:OtpMaxAttempts` / `LockMinutes` / `SessionMinutes` | 5 / 30 / 30 | | | | | 3 |
| HR.Core | `Recruitment:Portal:OtpRequestsPerIpPer10Min` / `OtpVerifyPerIpPer10Min` | 5 / 20 | | **60 / 120** (كل الاختبارات من عنوان واحد عبر البوابة) | | | 3 |
| HR.Core | `Recruitment:PublicCareersRateLimitPerMinute` (النافذة المشتركة) | 30 | | | | | HCM-006 |
| HR.Core | `Recruitment:AutoDraftNextOffer` | `false` | | `false` | | | PLAN-27 |

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| A | التوحيد: كل تقديم مرتبط بشاغر يدخل `RecruitmentApplication` بحالة `Submitted` | `UnifiedIntakeService`, `HCM030_UnifiedIntake`, `PublicCareersController.Apply`, `EssTalentController.Apply` |
| B | سجل أحداث الطلب والإسقاط العام | `RecruitmentApplicationEvent`, `RecruitmentApplicationEventRecorder`, `CandidatePublicStatusProjection`, `CandidatePublicStatusService` |
| C | ترحيل الطلبات غير المرتبطة | `scripts/hcm030-backfill-unified-intake.ps1`, `scripts/sql/hcm030-backfill-unified-intake.sql`, `UnifiedIntakeStatusMapping` |
| D | الملف الشخصي ومعايير الشاغر وأعلام الأهلية | `RecruitmentEligibilityRules`, `HCM030_ApplicationProfile`, `_EligibilityCriteriaFields` |
| E | المستندات عبر رفع خدمة File.Storage | `FilesServiceUploadController`, `FileServiceUploadService`, `CandidateDocumentUploadClient`, `CandidateDocumentService`, `HCM030_CandidateDocuments` |
| F | قنوات الإشعار والقالب والمخطِّط/المرسِل والتذكير | `INotificationChannelSender`, `FileDrop/Smtp/HttpSms…Sender`, `CandidateNotificationTemplates`, `CandidateNotificationDispatcher`, `CandidateNotificationJob`, `HCM030_CandidateNotifications` |
| G | الاحتفاظ بالزوج وسجل التشغيل والشاشة | `CandidateRetentionService`, `CandidateRetentionJob`, `CandidateLifecycle/Index`, `HCM030_CandidateRetention` |
| H | OTP وجلسة `careers_session` وحدّ المعدل لكل عميل | `CandidateOtpChallenge`, `CandidateOtpService`, `CandidateOtpKeyRing`, `CareersClientIpResolver`, `PublicCareersRateLimitExtensions`, `CandidatePortalSession`, `HCM030_CandidatePortal` |
| H5 | سماح البوابة لكوكي `careers_session` على مسار careers فقط | `GatewayUpstreamCookieForwarding`, `GatewayAccessTokenTransform` |
| I | `/careers/my` وإجراءات المرشح | `CandidatePortalController`, `CandidatePortalService`, `Views/CandidatePortal/*`, `RecruitmentOfferService`, `RecruitmentSelectionService.WithdrawByCandidateAsync`, `HCM030_OfferAcceptance` |
| J | القبول بالنيابة بسبب وإثبات | `OfferOnBehalfService`, `OfferLettersController.AcceptOffer`, `OfferLetters/Details`, `HCM030_OfferDeclineEvidence` |
| K | الخصوصية والوصولية | `CandidateRetentionService` (OTP)، `DsarInternalController`، `docs/prv-ropa/processing-activities.json`، `PRV_001_DPIA_RECRUITMENT.md`، `hcm006-a11y-scan.ps1` |
| L/M | القبول والتوثيق | `AcHcm30CandidateExperienceTests`, `AcceptanceCandidateExperiencePatch`, `hcm030-validate.ps1` |

## أوامر التحقق

```powershell
powershell -ExecutionPolicy Bypass -File scripts/hcm030-validate.ps1          # -SkipModelCheck / -SkipTests للتسريع
powershell -ExecutionPolicy Bypass -File scripts/hcm006-a11y-scan.ps1
powershell -ExecutionPolicy Bypass -File scripts/prv001-ropa-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm030|FullyQualifiedName~CandidatePublicStatusProjection|FullyQualifiedName~RecruitmentEligibility|FullyQualifiedName~Fs030|FullyQualifiedName~GatewayUpstreamCookieForwarding"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm030|FullyQualifiedName~Fs030|FullyQualifiedName~PublicCareers"   # Testcontainers
$env:RUN_ACCEPTANCE = "true"; $env:GATEWAY_BASE_URL = "http://localhost:6200"; $env:KEYCLOAK_BASE_URL = "http://localhost:6388"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcHcm30|FullyQualifiedName~AcHcm06"
```

ملاحظة الجهاز: على لغة ar-SA (التقويم الهجري) تفشل بعض الاختبارات القديمة لأسباب بيئية (القائمة في التقرير الختامي §6)، واختبارات
`*_validate_script_passes` تحتاج `-ExecutionPolicy Bypass` (أو `PSExecutionPolicyPreference=Bypass`).

آخر تشغيل (2026-10-29، مكدّس جزئي بلا MinIO و`file-storage-web`): **7/8 Pass** — 30a، 30b، 30d، 30e، 30f، 30g، 30h، 30i، 30j ناجحة؛
**30c متخطّى بيئياً** (يُجرى رفض `.exe` و> 5 MB أولاً وينجح، ثم يظهر «تعذّر رفع المستندات حالياً…» لغياب File.Storage
فيتخطّى الاختبار بـ `Skip.If`؛ الفحص النظيف والتنزيل وحجر EICAR غير متحققة). تشغيل الإصلاح على المكدّس نفسه: 30a/b/h ناجح (مع التحقق
من موعد الاستئناف «حتى …»)، و30j متخطّى لأن صفوفه رُبطت في التشغيل الأول — يرفض الاختبار النجاح الشكلي ويطلب
`scripts/acceptance-reset.ps1` لإعادة تهيئة الصفوف. اختبارات التكامل (Testcontainers):
`Hcm030|Fs030|Hcm02|PublicCareers|EssTalent|Recruitment|Offer` ← 51 ناجح / 2 فشل بيئي معروف (`EstablishmentHire…HirePartyCompensate`،
`Hcm028…Legacy_appeal_notes` الهجري) / 0 متخطّى. الأدلة:
`acceptance-evidence/AC-HCM-30/` (`steps-<سيناريو>.md`، `db-checks-<سيناريو>.json`، `http/`، `before.json`/`after.json`، `audit.json`،
`timing.json`؛ `result.json` يحمل حالة آخر سيناريو كتب فيه).

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| 30a/b/h | مواطن مجهول عبر البوابة `/careers` + `central-a` | التقديم، الرفض، الاستئناف |
| 30c | مواطن + `central-a` | المستندات وحالة الفحص |
| 30d/e | مواطن + `central-a` | الأعلام، الفرز، المقابلة والرسالة |
| 30f/g | مواطنان + `central-a` (+ `br1-mgr` و`a-management-approver` لاعتماد الطلب والعرض) | OTP، القبول/الاعتذار، المرشح التالي |
| 30i | `a-directory-checker` (hr-admin) | «تنفيذ الإخفاء» على شاشة الاحتفاظ |
| 30j | — (السكربت على حاوية PostgreSQL) | الترحيل |

الشروط المسبقة الاختبارية (SQL، موثقة في الاختبار): الترتيب (نطاق AC-HCM-26)، قراءة المراجع والمعرّفات التي لا تعرضها الواجهة إلا عبر TempData،
إرجاع `RejectedAt` 15 يوماً (30h السلبي)، ومسح `NotificationsPlannedAt` لإعادة التخطيط (30e السلبي). عميل المواطن لا يستخدم `CookieContainer`
(الكوكيز الآمنة لا تُرسل عبر http) بل ترويسة كوكي يدوية (`UseCookies=false`) تحمل `careers_session` وكوكي مكافحة التزوير.

## Manual — التقديم والمستندات (AC-HCM-30a, c)

1. `http://localhost:6200/careers` → إعلان `REQ-2026-A-CX01` → **تقديم**. املأ البيانات والمؤهل («بكالوريوس» أو أدنى) وسنوات الخبرة،
   وأرفق السيرة الذاتية (`(مطلوب)`) والشهادة (PDF/JPG/PNG حتى 5 MB، والمجموع حتى 9 MB)، واختر قناة الإشعار ووافق على
   «أوافق على تلقي إشعارات حول طلبي عبر القناة المختارة» → إرسال ← صفحة الاستلام مع الرقم المرجعي.
2. ملف `.exe` أو أكبر من 5 MB ← رسالة حقل ولا يُحفظ شيء. File.Storage غير متاح ← «تعذّر رفع المستندات حالياً، يرجى المحاولة لاحقاً.».
3. `acc-central-a` → `RecruitmentApplications?RequisitionId=…` ← الطلب بحالة `Submitted` وقناة `IntakeChannel=Public` مباشرةً (لا شاشة فرز ثانية؛
   `PublicJobPostings/Details` يحيل «فرز الطلبات» إلى القائمة نفسها).
4. تفاصيل الطلب → بطاقة **«المستندات»**: «سليم» مع رابط تنزيل، «قيد الفحص»، أو «محجور — غير متاح» (EICAR) بلا رابط. التنزيل يمر عبر HR.Core
   (`RecruitmentApplications/CandidateDocument/{id}`) لا عبر `/api/files`.

## Manual — الأهلية والفرز والمقابلة والرسالة (AC-HCM-30d, e)

1. القائمة: شارة أهلية وفلتر «مع ملاحظات أهلية». التفاصيل: «دون المؤهل المطلوب» / «خبرة أقل من المطلوب» (أو «لم تُقيَّم» لطلبات HR والترحيل).
2. انقل الطلب إلى `UnderReview` ثم `Shortlisted` دون سبب ← مرفوض؛ بسبب (10 أحرف على الأقل) ← مقبول مع تدقيق `EligibilityOverride`.
3. جدول مقابلة ← خلال دقيقة (`CandidateNotificationJob`) يظهر ملف واحد في `artifacts/acceptance/candidate-notifications/` بقالب
   `INTERVIEW_SCHEDULED` دون الرقم الوطني أو الاسم أو الراتب؛ بطاقة **«الإشعارات»** في تفاصيل الطلب تُظهر المستلم مقنَّعاً والحالة `Sent`.
   إعادة تشغيل المرسِل أو إعادة التخطيط ← لا رسالة ثانية (`(TenantId, IdempotencyKey)` فريد).

## Manual — الدخول بـ OTP والعرض (AC-HCM-30f, g)

1. `http://localhost:6200/careers/my` ← «الدخول إلى طلباتي»: «الرقم المرجعي» + «الرقم الوطني» → **إرسال الرمز** ← دائماً
   «إذا كانت البيانات صحيحة فسيصلك رمز تحقق على وسيلة التواصل المسجلة.» (حتى لو كانت البيانات خاطئة أو الحساب مقفلاً أو أُعيد الطلب خلال 60 ثانية).
2. افتح ملف FileDrop الأحدث (القالب `OTP`، النص «رمز التحقق لبوابة التوظيف: …… — صالح 10 دقائق، لا تشاركه مع أحد.») ← «إدخال رمز التحقق» → «رمز التحقق (6 أرقام)» → **دخول** ← صفحة «طلبي».
3. 5 رموز خاطئة ← قفل 30 دقيقة (`PortalLockedUntil`، تدقيق `CandidatePortalLocked` بلا بيانات شخصية)؛ الرمز الصحيح بعدها مرفوض ولا جلسة.
   مرجع شخص آخر مع رقمك الوطني ← الرسالة المحايدة نفسها، لا رمز، و`/careers/my` يعيد إلى `/careers/my/login`.
4. «طلبي»: «ملخص الطلب» (الحالة العامة والموعد)، «مسار الطلب»، «المقابلات» (التاريخ والوقت والمكان أو «عن بُعد»، بلا أسماء اللجنة)،
   «المستندات» (رفع الناقص ما دام التقديم مفتوحاً)، و**«عرض العمل»** عند عرض صادر: المسمى والدرجة والراتب وتاريخ المباشرة والانتهاء
   (الراتب يظهر هنا فقط، بعد OTP) و**«تنزيل خطاب العرض»** (PDF ورمز التحقق).
5. **قبول العرض:** «أقرّ بأنني اطلعت على شروط العرض وأقبلها» + «الاسم الكامل» مطابقاً ← العرض `Accepted`، `AcceptedBy=Candidate`
   («المرشح عبر بوابة التوظيف» في `OfferLetters/Details`)، الطلب `OfferAccepted`. عرض منتهٍ ← «انتهت صلاحية العرض ولا يمكن قبوله.».
6. **الاعتذار عن العرض** («سبب الاعتذار») ← العرض `Declined`، `DeclinedBy=Candidate`، الطلب `OfferDeclined`؛ لدى HR في `OfferLetters/Details`
   اقتراح «المرشح التالي بالجدارة» (أو مسودة عرض تلقائية إن كان `Recruitment:AutoDraftNextOffer=true`).
7. **خروج** يحذف الكوكي. الجلسة مطلقة 30 دقيقة ثم يعود الدخول.

## Manual — القبول بالنيابة (HR، J)

`OfferLetters/Details` لعرض صادر → «قبول نيابةً عن المرشح»: «سبب القبول نيابةً عن المرشح» (20 حرفاً على الأقل) + «مستند إثبات القبول»
(PDF/JPG/PNG حتى 5 MB، يُفحص) → **تسجيل القبول نيابةً عن المرشح** → نافذة تأكيد داخل الصفحة ← «تم تسجيل قبول خطاب العرض نيابةً عن المرشح».
التفاصيل تُظهر «HR نيابة عن المرشح» والسبب ورابط الإثبات (سليم فقط). «تسجيل رفض المرشح» يقبل «مستند إثبات الرفض (اختياري)». الصلاحية تبقى `HrCoreWrite`.

## Manual — الانسحاب والاستئناف (AC-HCM-30h)

1. **الانسحاب من الطلب** ← لوحة «تأكيد الانسحاب» في الصفحة («سبب الانسحاب» + «أؤكد رغبتي في الانسحاب من هذا الطلب») → **تأكيد الانسحاب**
   ← «منسحب». عرض صادر يُغلق `Declined` (`DeclinedBy=Candidate`) بسبب «انسحاب المرشح: …»؛ مسودة/قيد الاعتماد تُلغى ويُلغى سير عملها. بعد قبول العرض: لا زر،
   ورسالة «تم قبول العرض؛ للانسحاب يرجى التواصل مع جهة التوظيف.».
2. HR يرفض طلباً آخر ← المرشح يرى «غير مقبول» و«يمكنك الاستئناف حتى …» (و`/careers/track` يعرض التسمية العامة فقط) ← «الاستئناف» →
   «سبب الاستئناف» (20 حرفاً على الأقل) → **تقديم الاستئناف** ← `FiledBy=Candidate`، `Pending`، ويظهر في `RecruitmentAppeals`.
   طلب معلومات من المراجع ← «المعلومات المطلوبة» → **إرسال المعلومات**.
3. بعد 14 يوماً من الرفض: لا زر، و«انتهت مدة الاستئناف في yyyy/MM/dd»؛ أي POST مباشر يُرفض ولا يُنشأ استئناف.

## Manual — الاحتفاظ (AC-HCM-30i)

1. `acc-a-directory-checker` (Admin) → «بوابة المواطن للتوظيف» → **«الاحتفاظ ببيانات المرشحين»** (`CandidateLifecycle/Index`): قائمة
   المستحقين (المرجع أو `#XXXXXXXX`، رقم الشاغر، تاريخ السبب، «إلغاء الشاغر»/«إغلاق الشاغر») وثلاث بطاقات عدّ.
2. **تشغيل تجريبي** ← سطر في سجل التشغيل بلا أي تغيير (`DryRun`).
3. **تنفيذ الإخفاء** (يظهر فقط عند `Recruitment:Retention:Apply=true`، بتأكيد داخل الصفحة) ← `AC30-RET-0001`: الاسم والرقم الوطني والتواصل
   والملف الشخصي والنصوص الحرة مُفرَّغة على الصفين، المستند محذوف مع صف `Release` في صندوق مراجع الملفات، الرسائل «—»، رموز OTP محذوفة؛
   `AC30-RET-0002` (حجز قانوني) دون تغيير و`SkippedLegalHold ≥ 1`.

## Manual — الترحيل (AC-HCM-30j)

```powershell
# تشغيل تجريبي (افتراضي): تقرير فقط، ROLLBACK
powershell -ExecutionPolicy Bypass -File scripts/hcm030-backfill-unified-intake.ps1 -DockerContainer hrm-acceptance-postgresql-1
# تطبيق
powershell -ExecutionPolicy Bypass -File scripts/hcm030-backfill-unified-intake.ps1 -Apply -DockerContainer hrm-acceptance-postgresql-1
# بديل باتصال مباشر (psql على المضيف): -Apply -ConnectionString "Host=localhost;Port=6433;Database=hr_core_db;Username=…"
```

النتيجة في `artifacts/hcm030-backfill/` (CSV): `AC30-BF-0001` ← `Submitted`، `AC30-BF-0002` ← `UnderReview`، والطلبات الداخلية ← قناة
`Internal`. تشغيل ثانٍ بـ `-Apply` يربط 0. لا أحداث ولا إشعارات للصفوف التاريخية. في الإنتاج: بعد الترحيلات، تجريبي أولاً ثم `-Apply`.

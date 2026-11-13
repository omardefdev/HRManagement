# دراسة الواجهة: القراءة بالعربية، وسياسة أمن المحتوى، وبيانات خادم العرض (UX-004) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-11-13  
**النطاق:** واجهات Razor في الخدمات الأربع عشرة والبوابة والمكتبة المشتركة `HRManagement.Web.Shared` (652 ملف عرض)، وسياسة أمن المحتوى (CSP) كما تُرسَل في Production، والبوابة العامة للتوظيف، وما تتركه فحوص الخادم على خادم العرض.  
**الدافع:** لقطتان من المالك على خادم العرض في 2026-11-13: صفحة قالب سياسة (`/ui/hr-core/PolicyTemplates/Details/…`)، والصفحة التي يفتحها `/ui/hr-core/` (البوابة العامة للتوظيف).  
**المنهج:** قراءة ثابتة للشيفرة والإعدادات والوثائق عند `main` @ `e6a93705` (بعد PLAN-51)، ومطابقتها مع اللقطتين. الأعداد مستخرجة بمسح نصي لملفات العرض، وكل فجوة مدعومة بمرجع ملف/سطر.  
**حدود الدراسة:** لم يُبنَ المشروع ولم يُشغَّل، ولم تُجلب رؤوس خادم العرض، لأن الشبكة من بيئة الدراسة لا تصله. البنود الموسومة **«يحتاج تحقق»** تُثبَت في المرحلة 0 من PLAN-67.  
**الوثائق المقارَنة:** [ADR-UX-001-xss-csp-sri.md](ADR-UX-001-xss-csp-sri.md) · [UX_001_VERIFICATION.md](UX_001_VERIFICATION.md) · [UX_003_CONFIRM_DIALOGS.md](UX_003_CONFIRM_DIALOGS.md) · [L10N_001_LOCALIZATION.md](L10N_001_LOCALIZATION.md) · [L10N_003_ARABIC_UI.md](L10N_003_ARABIC_UI.md) · G-POL-14 في [POL_001_GAP_STUDY.md](POL_001_GAP_STUDY.md) · G-WF-16 في [WF_001_GAP_STUDY.md](WF_001_GAP_STUDY.md)

---

## 1. الخلاصة

1. **ما يبدو في البوابة العامة «تصميماً سيئاً» هو تنسيق حجبه المتصفح (يحتاج تحقق).**
   - البوابة العامة تعرّف شريطها العلوي وبطاقاتها وأزرارها وتذييلها في كتلة `<style>` داخل `_PublicLayout`.
   - في اللقطة يعمل Bootstrap، وهو ملف خارجي، ولا يعمل شيء من تلك الكتلة: لا خلفية للبطاقات، ولا شريط ملوّن، وزر «التفاصيل والتقديم» رابط عادي، والتذييل ملتصق بالحافة.
   - **السبب الأرجح:** البوابة وHR.Core تضيف كل منهما سياسة CSP برقم nonce مختلف، والبوابة تضيف سياستها قبل تمرير الطلب، فيصل المتصفح سياستان. ويجب أن يرضيهما كل عنصر مضمّن، فيُحجب كل `<style>` و`<script>` مضمّن في صفحات الخدمات حين تُفتح عبر البوابة.
2. **وحتى بسياسة واحدة، ما يُكتب داخل الوسوم محجوب حتماً على خادم العرض.** خادم العرض يعمل Production، وCSP فيه `Enforce` في التطبيقات الخمسة عشر (الخدمات الأربع عشرة والبوابة)، بلا `unsafe-inline`:
   - **12 معالجاً مضمّناً في HR.Core (`onclick`/`onsubmit`):**
     - عشرة منها تأكيد `confirm()` قبل حذف مسودة شاغر أو إلغائه أو إغلاقه، أو تجميد منصب أو إلغاء تجميده، أو تعطيل دليل أو رمز مرجعي، أو إلغاء طلب تغيير، أو رفض خطوة مسار. فتُنفَّذ هذه بلا تأكيد.
     - واثنان ينسخان ملاحظات المراجع في صفحة الاعتماد، فتصل الموافقة والرفض بلا ملاحظات.
   - **46 خاصية `style="..."`** تُتجاهل: أشرطة التقدم، وعرض الأعمدة، وموضع الإشعارات العائمة، والنماذج المتجاورة.
   - **ولا تكشف الاختبارات شيئاً من ذلك:** الملف الشبيه بالإنتاج ومكدّس E2E يعملان بـ `ReportOnly`.
3. **القراءة بالعربية ناقصة في أطراف محددة،** لا في الواجهة كلها:
   - **27 تسمية إنجليزية،** منها 21 في ست صفحات للسياسات.
   - **نحو 38 موضعاً** يطبع حالة أو نوعاً كما هو في الشيفرة (`PendingApproval`، `Published`، `National`).
   - **نحو 125 موضعاً** يطبع معرّفاً (GUID) بدل اسم شخص أو سجل.
   - **الأوقات:** 97 وقتاً تُعرض بتوقيت UTC بلا تحويل ولا إشارة، أي متأخرة ثلاث ساعات عن دمشق. وخدمة التقويم (`ICalendarService`) موجودة منذ L10N-001 ولا يستعملها أي عرض. ويوجد خمسة تنسيقات للتاريخ.
4. **قواعد السياسات تُعرض وتُكتب وتُحاكى JSON خاماً.**
   - تخزينها JSON صحيح.
   - عرضها لا يصح: لكل حقل في مخطط الحقائق اسم عربي (`DisplayNameAr`)، فالعرض بجملة عربية ممكن الآن.
   - المحرر المنظّم يبقى لـ POL-G.
5. **البوابة العامة مربوطة بجذر HR.Core،** فيفتحها `/ui/hr-core/` بدل لوحة الموظفين.
6. **بقايا فحوص الخادم ظاهرة:** ثماني فئات فحص تترك سجلات موسومة «PLAN-xx server check»، ومنها طلبات شواغر معتمدة تظهر إعلاناتها في البوابة العامة.
7. **لا حارس:** لا اختبار يقرأ نص الواجهة، أو رؤوس CSP، أو يلتقط صورة صفحة.
8. **الحجم:** عشر فجوات: ثلاث P1، وست P2، وواحدة P3. تحملها خطة واحدة، PLAN-67، بنحو 10–12 يوم عمل.

---

## 2. خريطة الواجهة كما هي في الشيفرة

| # | الطبقة | التنفيذ الحالي | الحكم |
|---|--------|----------------|-------|
| 1 | التخطيط والثيم | `_HrSharedLayout` و`syrian-theme.css` و27 جزءاً مشتركاً (`_HrDataTable`، `_HrEmptyState`، `_HrConfirmModal`، `_EmployeeDisplayName`…) | ✅ |
| 2 | اللغة والاتجاه | عربي RTL في كل التخطيطات | ✅ |
| 3 | الحالات والأنواع | `HrEnumDisplay` في 302 موضع؛ ونحو 38 طباعة خام، وتعدادات السياسات بلا أسماء عربية | ⚠️ G-UX-03 |
| 4 | الأشخاص والسجلات | `display-names.js` يحوّل معرّفات الموظفين والوحدات إلى أسماء؛ لا شيء للمستخدمين | ⚠️ G-UX-04 |
| 5 | الوقت | `ICalendarService` موجود وغير مستعمل؛ 97 وقتاً بتوقيت UTC | ❌ G-UX-05 |
| 6 | JSON | ملخص مقروء لطلبات تغيير الملاك، والحمولة مطوية تحت «تشخيص» ✅؛ السياسات خام | ❌ G-UX-06 |
| 7 | CSP | nonce تلقائي لكل `<script>`/`<style>` ✅؛ سياستان عبر البوابة (يحتاج تحقق)؛ 12 معالجاً و46 `style` محجوبة | ❌ G-UX-01 |
| 8 | التأكيد | `data-hr-confirm` الموحّد (UX-003) ✅؛ عشرة `confirm()` مضمّنة | ❌ G-UX-01 |
| 9 | بيئة الاختبار | `ReportOnly` في القبول وE2E | ❌ G-UX-02 |
| 10 | الجداول الفارغة | 184 من 211 جدولاً برسالة فراغ؛ 27 بلا رسالة | ⚠️ G-UX-09 |
| 11 | البوابة العامة | على جذر HR.Core، وتنسيق تاريخ مختلف | ⚠️ G-UX-07 |
| 12 | بيانات خادم العرض | بقايا فحوص ظاهرة، حتى للعامة | ⚠️ G-UX-08 |
| 13 | الحراس | لا شيء | ❌ G-UX-10 |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **نظام تصميم مشترك فعلي:** تخطيط واحد ([_HrSharedLayout.cshtml](../src/Shared/HRManagement.Web.Shared/Views/Shared/_HrSharedLayout.cshtml))، وثيم ([syrian-theme.css](../src/Shared/HRManagement.Web.Shared/wwwroot/css/syrian-theme.css))، و27 جزءاً مشتركاً، تستعملها الخدمات كلها. صفحة القالب في اللقطة الأولى تحمل الهوية نفسها: الشريط الجانبي والبطاقات والألوان.
- **التعريب واسع:**
  - `HrEnumDisplay` و`HrStoredEnumDisplay` في 302 موضع ([HrDisplayHtmlExtensions.cs:9-15](../src/Shared/HRManagement.Web.Shared/Html/HrDisplayHtmlExtensions.cs)).
  - أغلب التعدادات تحمل `[Display(Name = "…")]` عربياً، مثل [EstablishmentChangeRequest.cs:7-19](../src/Services/HRCore/HR.Core.Domain/Entities/EstablishmentChangeRequest.cs).
  - 184 من 211 جدولاً تعرض رسالة فراغ.
- **أمن الواجهة (UX-001):** وسم nonce تلقائي لكل `<script>` و`<style>` ([CspNonceTagHelper.cs:7-27](../src/Shared/HRManagement.Web.Shared/TagHelpers/CspNonceTagHelper.cs))، ولا مصادر خارجية، وCSP بلا `unsafe-inline`.
- **ديالوغ تأكيد موحّد (UX-003):** `data-hr-confirm` في [shared-ui.js](../src/Shared/HRManagement.Web.Shared/wwwroot/js/shared-ui.js) (السطر 372)، بديلاً عن `confirm()`.
- **نمط صحيح لعرض JSON:** صفحة طلب تغيير الملاك تعرض ملخصاً مقروءاً، وتطوي الحمولة الخام تحت «حمولة JSON (تشخيص)» ([EstablishmentChanges/Details.cshtml:44-51](../src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/Details.cshtml)). هذا ما تحتاجه صفحات السياسات.
- **أسماء بدل المعرّفات للموظفين والوحدات:** `data-hr-employee-id` و`data-hr-org-unit-id` تُحوَّل إلى أسماء بطلب مجمّع ([display-names.js:150-160](../src/Shared/HRManagement.Web.Shared/wwwroot/js/display-names.js)).
- **التقويم:** `DamascusCalendarService` يحوّل UTC إلى `Asia/Damascus` ([DamascusCalendarService.cs](../src/Shared/HRManagement.ServiceDefaults/Localization/DamascusCalendarService.cs)، [L10N_001_LOCALIZATION.md:12, 88](L10N_001_LOCALIZATION.md)). ينقصه أن يُستعمل.

---

## 4. الفجوات بالتفصيل

### 4.1 فجوات P1

#### G-UX-01 — سياسة أمن المحتوى تحجب ما يُكتب داخل الوسوم على خادم العرض، وعبر البوابة ربما كل عنصر مضمّن

- **الدليل:**
  - **CSP مفروض في Production:**
    - `Security:CspMode = Enforce` في `appsettings.Production.json` للخدمات الأربع عشرة والبوابة، مثلاً [HR.Core.Web/appsettings.Production.json:259](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json) و[Gateway/appsettings.Production.json:37](../src/Gateway/HRManagement.Gateway/appsettings.Production.json). وخادم العرض يعمل Production (تقرير PLAN-51 §8).
    - السياسة: `script-src 'self' 'nonce-…'; style-src 'self' 'nonce-…'` ([HrSecurityHeadersExtensions.cs:49-83](../src/Shared/HRManagement.ServiceDefaults/HrSecurityHeadersExtensions.cs)).
    - nonce لا يغطي معالجات الأحداث ولا خاصية `style`، فهذه محجوبة حتماً.
  - **معالجات مضمّنة (12، كلها في HR.Core):**
    - `confirm()` في [RecruitmentRequisitions/Details.cshtml:84, 92, 106, 180](../src/Services/HRCore/HR.Core.Web/Views/RecruitmentRequisitions/Details.cshtml)، و[FundedPositions/Details.cshtml:80, 88](../src/Services/HRCore/HR.Core.Web/Views/FundedPositions/Details.cshtml)، و[EstablishmentChanges/Details.cshtml:86](../src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/Details.cshtml)، و[JobCatalog/Details.cshtml:22](../src/Services/HRCore/HR.Core.Web/Views/JobCatalog/Details.cshtml)، و[ReferenceCatalog/Details.cshtml:88](../src/Services/HRCore/HR.Core.Web/Views/ReferenceCatalog/Details.cshtml)، و[_WorkflowActionPanel.cshtml:57](../src/Services/HRCore/HR.Core.Web/Views/Shared/_WorkflowActionPanel.cshtml) (رفض خطوة المسار).
    - **ملاحظات المراجع:** في [Approvals/Review.cshtml:95-105](../src/Services/HRCore/HR.Core.Web/Views/Approvals/Review.cshtml) يملأ `onclick` الحقلين المخفيين `reviewComments` في نموذجي الموافقة والرفض. والإجراء يقبل الملاحظات فارغة ([ApprovalsController.cs:171](../src/Services/HRCore/HR.Core.Web/Controllers/ApprovalsController.cs)).
  - **46 خاصية `style="…"`** في عشرة مشاريع، منها:
    - `width:@(pct)%` لأشرطة التقدم؛
    - `position:fixed;top:1rem;z-index:10000` لحاوية الإشعارات؛
    - `display:inline` لثمانية نماذج متجاورة؛
    - `white-space: pre-line` لنص متعدد الأسطر.
  - **سياستان على الاستجابة نفسها (يحتاج تحقق):**
    - البوابة وكل خدمة تستدعي `UseHrProductionMiddleware`، وفيه `UseHrSecurityHeaders` ([ProductionHostExtensions.cs:85-103](../src/Shared/HRManagement.ServiceDefaults/ProductionHostExtensions.cs)).
    - في البوابة يجري هذا قبل `MapReverseProxy` ([Gateway Program.cs:190, 253](../src/Gateway/HRManagement.Gateway/Program.cs))، فتكتب البوابة رأس CSP بـ nonce خاص بها قبل أن تمرر الطلب.
    - YARP (2.3.0) يضيف رؤوس الخدمة إلى الرؤوس الموجودة، ولا يستبدلها، فيصل المتصفح سياستان.
    - العنصر المضمّن في صفحة الخدمة يحمل nonce الخدمة، فترفضه سياسة البوابة.
  - **العَرَض في اللقطة يطابق هذا:**
    - [_PublicLayout.cshtml:7-9](../src/Services/HRCore/HR.Core.Web/Views/Shared/_PublicLayout.cshtml) يحمّل Bootstrap والثيم من ملفات، ويعرّف `careers-navbar` و`careers-card` و`careers-btn-primary` و`careers-footer` وخلفية الصفحة في `<style>` مضمّن (الأسطر 10-129).
    - في اللقطة تعمل شبكة Bootstrap ولا يعمل شيء من الكتلة المضمّنة.
  - **عناصر أخرى قد تكون محجوبة عبر البوابة:**
    - 16 كتلة `<script>` مضمّنة، منها مبدّل وضع التعيين («موظف جديد» أو «ربط بموظف قائم») في [RecruitmentApplications/Hire.cshtml:157-162](../src/Services/HRCore/HR.Core.Web/Views/RecruitmentApplications/Hire.cshtml)، والرسوم البيانية في لوحتي الرواتب والعقود.
    - كتلة `<style>` تضبط لون الجهة في كل صفحة ([_OrganizationBranding.cshtml:9-12](../src/Shared/HRManagement.Web.Shared/Views/Shared/_OrganizationBranding.cshtml)).
  - **ما حذّر منه القرار المعماري:**
    - [ADR-UX-001:37](ADR-UX-001-xss-csp-sri.md): «Enforce صارم قد يكسر صفحات بـinline غير مُعلَّمة بـnonce حتى تُرحَّل».
    - والمسار الموثق هو: `ReportOnly`، ثم إصلاح المخالفات، ثم `Enforce`.
    - [UX_001_VERIFICATION.md:8](UX_001_VERIFICATION.md) يقيّم `Report-Only` وحده: «OK».
- **الأثر:**
  - إجراءات هدّامة تُنفَّذ بلا تأكيد.
  - ملاحظات الاعتماد تضيع بصمت.
  - صفحات عامة تبدو مكسورة.
  - إن ثبتت السياستان: مبدّل وضع التعيين لا يعمل، فقد يُنشأ موظف جديد مكرراً بدل ربط الموظف القائم. يحتاج تحقق: أي وضع يُرسَل افتراضياً.
- **الإصلاح المقترح:**
  - سياسة واحدة لكل استجابة: سياسة الخدمة التي ترسم الصفحة، وسياسة البوابة لصفحاتها وحدها.
  - نقل المعالجات إلى `data-hr-confirm` وشيفرة غير مضمّنة.
  - استبدال خصائص `style` بأصناف CSS.
  - لا تخفيف للسياسة: لا `unsafe-inline` ولا `unsafe-hashes`.
- **التحقق المطلوب:**
  - **موجب:**
    - على خادم العرض ومكدّس `Enforce` محلي، كل صفحة رئيسية تصل برأس CSP واحد وبلا مخالفة في المتصفح؛
    - ديالوغ التأكيد يظهر قبل حذف مسودة شاغر؛
    - ملاحظات المراجع تصل سجل الاعتماد.
  - **سالب:** معالج مضمّن مزروع في صفحة اختبار يُسقط فحص CSP.

#### G-UX-02 — لا بيئة اختبار تفرض CSP

- **الدليل:**
  - القيمة الافتراضية `ReportOnly` ([HrSecurityHeadersExtensions.cs:15](../src/Shared/HRManagement.ServiceDefaults/HrSecurityHeadersExtensions.cs)).
  - الملف الشبيه بالإنتاج يعمل `ASPNETCORE_ENVIRONMENT=Acceptance`، ولا يضبط `Security__CspMode` في `docker-compose.acceptance-prodlike.yml`، وكذلك `docker-compose.ci-e2e.yml`.
  - لا اختبار في المستودع يذكر `Content-Security-Policy`.
- **الأثر:** كل ما في G-UX-01 يمر في القبول وE2E، ثم ينكسر على خادم العرض وحده. وهذا يكرر درس PLAN-37: قيمة Production غير منعكسة في الملف الشبيه بالإنتاج.
- **الإصلاح المقترح:**
  - `Security__CspMode=Enforce` في الملف الشبيه بالإنتاج وفي مكدّس E2E.
  - فحص بالمتصفح يجمع أحداث `securitypolicyviolation` على قائمة صفحات رئيسية، ويفشل عند أول مخالفة.
  - فحص رؤوس يعدّ سياسات CSP في الاستجابة.
- **التحقق المطلوب:**
  - **موجب:** الفحص أخضر بعد إصلاح G-UX-01.
  - **سالب:** أعِد معالجاً مضمّناً أو سياسة البوابة على صفحة خدمة، فيفشل الفحص.

#### G-UX-05 — الأوقات تُعرض بتوقيت UTC بلا تحويل ولا إشارة

- **الدليل:**
  - 97 موضعاً تطبع وقتاً بساعاته (`HH:mm`) في 11 مشروعاً، ولا يحوّل أي منها إلى التوقيت المحلي.
  - `ICalendarService` لا يستعمله أي عرض أو متحكم.
  - صفحة القالب تطبع `…Z` صراحة (اللقطة الأولى)، ونموذج القواعد يطلب الإدخال بتوقيت UTC ([PolicyTemplates/EditRules.cshtml:106-107](../src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/EditRules.cshtml)).
  - التنسيقات خمسة: `yyyy/MM/dd` في 123 موضعاً، و`yyyy-MM-dd` في 112، و`yyyy/MM/dd HH:mm` في 61، و`yyyy-MM-dd HH:mm` في 42، و`dd-MM-yyyy` في البوابة العامة.
- **الأثر:**
  - الوقت المعروض متأخر ثلاث ساعات عن دمشق، بلا ما يدل على ذلك.
  - سجل اعتماد عند التاسعة صباحاً يظهر عند السادسة.
  - نفاذ سياسة يُدخل بتوقيت لا يعرفه المستخدم.
  - هذا يمس الثقة بسجلات التدقيق.
- **الإصلاح المقترح:**
  - مساعد عرض واحد فوق `ICalendarService`، والمنطقة من الإعداد (`Asia/Damascus` افتراضياً).
  - تنسيق واحد للتاريخ وتنسيق واحد للوقت.
  - الإدخال بالتوقيت المحلي، ويُحوَّل عند الحفظ.
  - التخزين يبقى UTC.
- **التحقق المطلوب:**
  - **موجب:** وقت معروض = القيمة المخزنة + فرق المنطقة، وتلميح «بتوقيت دمشق».
  - **سالب:** إدخال نفاذ عند 00:30 بتوقيت دمشق يُخزَّن 21:30 من اليوم السابق UTC، ويُعرض 00:30.

### 4.2 فجوات P2

#### G-UX-03 — مفردات إنجليزية، وقيم تعداد خام، ورموز داخلية أمام المستخدم

- **الدليل:**
  - **27 تسمية إنجليزية في 11 ملفاً:**
    - 21 منها في ست صفحات سياسات: `Domain` و`Subject` و`Scope` و`Rule` و`Priority` و`When` و`Then` في [Details.cshtml:38-40, 105](../src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/Details.cshtml)، و`Action` و`Mode` و`Workflow` و`Hash` في `Ledger.cshtml:7`، و`Diffs` في `ShadowReport.cshtml:10`، و`Facts (JSON)` في `Simulate.cshtml:15`، و`Shadow report` في `Index.cshtml:11`.
    - والباقي اختصارات: `SKU` في ثلاث صفحات لوجستيات، و`COI` في `SelectionCommittees/Index.cshtml:50`، و`FTE` في `WorkforcePlans/Details.cshtml:48`.
  - **نحو 38 طباعة خام لحالة أو نوع في 25 ملفاً،** منها:
    - شارة حالة طلب الشاغر `@requisition.Status`، فتظهر `PendingApproval` ([RecruitmentRequisitions/Details.cshtml:216](../src/Services/HRCore/HR.Core.Web/Views/RecruitmentRequisitions/Details.cshtml)، و`Index.cshtml:78`)؛
    - `FundedPositions/Index.cshtml:73`؛
    - `EstablishmentChanges/Index.cshtml:75-76` (النوع والحالة، مع أن للنوع أسماء عربية)؛
    - `WorkforcePlans`، و`ReferenceCatalog`، و`Payroll/Arrears/Details.cshtml:28`، و`EndOfService/Details.cshtml:25`، و`HR.Training/EssLearning/MyLearning.cshtml:18`؛
    - وفي السياسات: `@Model.Scope` و`@v.Status` و`@publication.Kind` (`Details.cshtml:40, 66, 87`)، و`@row.Mode` (`Index.cshtml:89, 93`).
  - **تعدادات السياسات بلا أسماء عربية:** `PolicyTemplateScope` و`PolicyVersionStatus` ([PolicyEntities.cs:7-18](../src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs))، ونوع النشر `Manual`/`SystemSeed`/`Rollback` (السطور 78-81).
  - **المجال يظهر رمزاً:** `Core.Recruitment` بلا اسم عربي (`PolicyDomainDescriptor` في [PolicyDomainCatalog.cs:37-59](../src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs)).
  - **رموز داخلية في نص المستخدم:** `D-POL-1` و`R-ETL` و`POL-049-SCOPE-RETIRE` و`(RequireApproval)` (اللقطة الأولى).
- **الأثر:** صفحات تبدو «نصف مترجمة»، والمصطلح الإنجليزي يربك موظف الموارد البشرية ويُضعف العرض أمام الجهة.
- **الإصلاح المقترح:**
  - أسماء عربية لكل تعداد يُعرض، وطباعته عبر `HrEnumDisplay`.
  - اسم عربي لكل مجال سياسة.
  - تسميات عربية في الصفحات، والاختصار بعد العربي بين قوسين.
  - الرموز الداخلية تُعرض ثانوية (تلميح أو خط صغير)، لا في جملة المستخدم.
- **التحقق المطلوب:**
  - **موجب:** صفحات السياسات بلا كلمة إنجليزية خارج قائمة الاختصارات.
  - **سالب:** حارس النص يفشل على تسمية إنجليزية أو طباعة خام مزروعة.

#### G-UX-04 — معرّفات (GUID) بدل الأسماء

- **الدليل:**
  - نحو 125 موضعاً في 14 مشروعاً، منها:
    - المحرّر والناشر في صفحة القالب ([Details.cshtml:88-89](../src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/Details.cshtml))؛
    - `ReleasedBy` و`ReleaseApprovedBy` و`PayrollRunId` في `Payroll/PaymentHolds/Details.cshtml:17-24`؛
    - `OrgUnitId` و`CreatedBy` في `HR.Movements/DistributionPlans/Details.cshtml:14, 17`؛
    - `FiledBy` و`FindingId` في Compliance؛
    - `IssuedBy` و`RequestedBy` في IDCard وHR.Health وIT.Infra.
  - **الآلية الموجودة** تحوّل الموظفين والوحدات فقط ([display-names.js:150-160](../src/Shared/HRManagement.Web.Shared/wwwroot/js/display-names.js)). ولا يوجد ما يحوّل معرّف المستخدم (`sub` في Keycloak) إلى اسم.
  - **الدليل لا يعرف مستخدمي خادم العرض:**
    - `AuthUser` يحمل `KeycloakUserId` ([AuthUser.cs:39](../src/Services/AuthDirectory/Auth.Directory.Domain/Entities/AuthUser.cs))؛
    - لكن بذرة العرض تنشئ `emp001`… بلا هذا الحقل ([DemoAuthSeeder.cs:82-86](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Data/DemoAuthSeeder.cs))؛
    - ومستخدمو `e2e-*` غير موجودين فيه أصلاً.
  - **البوابة تعرف كل من دخل:** سجل تدقيقها يحفظ `UserSub` و`UserName` والجهة لكل طلب ([GatewayAuditEntry.cs:10-11, 22](../src/Gateway/HRManagement.Gateway/Models/GatewayAuditEntry.cs)).
  - **المسارات تحفظ اسم الفاعل وقت الفعل** (`ActorName` في [WorkflowStepHistoryEntry.cs:14](../src/Shared/HRManagement.SharedKernel/Workflow/WorkflowStepHistoryEntry.cs))، فسجلها مقروء أصلاً.
- **الأثر:** لا يعرف القارئ من نشر السياسة أو من أفرج عن الدفعة. ومعرّف طويل في صفحة عرض يوحي بنظام غير مكتمل.
- **الإصلاح المقترح:**
  - **سجل أسماء صغير في البوابة:** تملؤه عند كل دخول من مطالبات الرمز (المعرّف، والاسم المعروض، والجهة).
  - **استعلام مجمّع عنه:** يصله الموظف التفاعلي وحده (تعريف PLAN-50)، لأسماء جهته. ويُستدعى بـ `data-hr-user-id` في `display-names.js`.
  - السجلات المرتبطة باسمها ورابطها (الآلية الموجودة، أو تضمين في الخادم).
  - المعرّفات التقنية تبقى بسبب معلن: معرّف الترابط في صفحة التدقيق، ومعرّف Keycloak في صفحة إدارة المستخدم، والرقم الوطني، والبصمات.
- **التحقق المطلوب:**
  - **موجب:** صفحة القالب تعرض اسمي المحرّر والناشر، و«النظام» للبذور.
  - **سالب:** موظف غير تفاعلي، أو خدمة، لا يصل استعلام الأسماء. ومعرّف مجهول يُعرض «مستخدم غير معروف».

#### G-UX-06 — قواعد السياسات تُعرض وتُكتب وتُحاكى JSON خاماً

- **الدليل:**
  - **العرض:** `<code>@r.WhenJson</code>` و`<code>@r.ThenJson</code>` في [Details.cshtml:112-113](../src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/Details.cshtml) و`EditRules.cshtml:65-66`.
  - **الكتابة:** حقلا نص JSON (`EditRules.cshtml:46-47, 88-89`)، وهذا G-POL-14 بعينه ([POL_001_GAP_STUDY.md:425-443](POL_001_GAP_STUDY.md)).
  - **المحاكاة:** حقائق JSON في `Simulate.cshtml:15-16`.
  - **المادة اللازمة للعرض المقروء موجودة:**
    - لكل حقل في مخطط الحقائق اسم عربي ونوع ([PolicyFactsSchemaModels.cs:3-9](../src/Shared/HRManagement.SharedKernel/Policy/PolicyFactsSchemaModels.cs)، والبذور [PolicyEngineDomainSeeds.cs:120-121](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs))؛
    - العوامل ثمانية ([PolicyRuleJson.cs:20-23](../src/Shared/HRManagement.SharedKernel/Policy/PolicyRuleJson.cs))؛
    - الآثار أحد عشر ([PolicyEffect.cs](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEffect.cs)).
- **الأثر:**
  - الجهة لا تقرأ قاعدتها: «إذا كانت البيئة = الإنتاج ← تتطلب موافقة» تظهر `{"all":[{"field":"environment",…}]}`.
  - المحاكاة تحتاج مطوّراً.
  - وهذا يناقض وعد «تغيير القاعدة دون نشر» الذي يُعرض أمام العميل.
- **الإصلاح المقترح:**
  - **عرض مقروء:** جملة عربية لكل قاعدة: الشرط بأسماء الحقول العربية، والعامل بكلمة عربية، والقيمة بحسب نوعها، والأثر بفعل عربي ومعاملاته.
  - **JSON خلف «عرض تقني»:** على نمط طلبات تغيير الملاك، لمؤلفي السياسات ومدققيها فقط.
  - **صفحة الكتابة:** تعرض الجملة بجانب حقلي JSON، معاينةً.
  - **المحاكاة:** نموذج يولَّد من مخطط الحقائق، وJSON خيار متقدم.
  - **يبقى لـ POL-G** (الموجة 3): المحرر المنظّم الكامل، وكشف التعارض، والمحاكاة على المسودة.
- **التحقق المطلوب:**
  - **موجب:** لكل قاعدة منشورة، الجملة تطابق JSON حقلاً حقلاً؛ والمحاكاة بالنموذج تعطي قرار المحاكاة بـ JSON نفسه.
  - **سالب:** مستخدم بلا دور سياسات لا يرى JSON؛ وحقل غير معروف في المحاكاة يُرفض كما اليوم.

#### G-UX-07 — البوابة العامة على جذر HR.Core

- **الدليل:**
  - `PublicCareersController` يحمل `[Route("")]` و`[HttpGet("")]` ([PublicCareersController.cs:23-24, 36-37](../src/Services/HRCore/HR.Core.Web/Controllers/PublicCareersController.cs)). والمسار المسمّى يسبق المسار الافتراضي `{controller=Dashboard}` ([HR.Core Program.cs:376-378](../src/Services/HRCore/HR.Core.Web/Program.cs)).
  - فيفتح `/ui/hr-core/` بوابة المواطن (اللقطة الثانية).
  - عنوانها العام عند البوابة `/careers` ([Gateway appsettings.json:380-384](../src/Gateway/HRManagement.Gateway/appsettings.json)، `GatewayJourneyCatalog.cs:53`).
  - تاريخ الإغلاق بتنسيق `dd-MM-yyyy`، خلافاً لبقية الواجهة (`PublicCareers/Index.cshtml:28`).
- **الأثر:** الموظف الذي يكتب عنوان الوحدة يرى بوابة المواطن. ولبوابة المواطن عنوانان.
- **الإصلاح المقترح:**
  - البوابة العامة تحت `careers` داخل HR.Core، والبوابة تمرر `/careers` إليه.
  - `/ui/hr-core/` يفتح لوحة القيادة، ويُحوَّل غير المسجّل لتسجيل الدخول.
  - تنسيق التاريخ الموحّد (G-UX-05).
- **التحقق المطلوب:**
  - **موجب:** `/careers` وكل روابطه تعمل كما اليوم، و`/ui/hr-core/` يفتح لوحة القيادة لموظف مسجّل.
  - **سالب:** `/ui/hr-core/` لا يعرض بوابة المواطن.

#### G-UX-08 — بقايا فحوص الخادم ظاهرة للمستخدم وللعامة

- **الدليل:**
  - ثماني فئات فحص تترك سجلات موسومة على خادم العرض عمداً:
    - [AcWf43VpsChecks.cs:9, 13](../src/Tests/HRManagement.AcceptanceTests/AcWf43VpsChecks.cs)، و`AcWf44VpsChecks.cs:10, 14`، و`AcIam50VpsChecks.cs:9, 15, 245`: طلبات شواغر معتمدة؛
    - `AcWf45VpsChecks.cs:9, 13`: أصول؛
    - `AcWf46VpsChecks.cs:8, 12`: طلبات بطاقات وإنذار؛
    - `AcPol47VpsChecks.cs:140-151`: إصدارات سياسة بدليل «PLAN-47 server check: rollback»؛
    - `AcPol49VpsChecks.cs:14-19`: تجميد؛
    - `AcOps38VpsDemoDefectChecks.cs:109`: إجازات.
  - طلبات الشواغر المعتمدة تُنشر في البوابة العامة: «PLAN-43 server check» وصفاً لوظيفة «محاسب» (اللقطة الثانية).
  - بعض الفحوص يعتمد على بقايا ما قبله: الفحص الثالث في PLAN-49 يقرأ ما كتبته فحوص PLAN-43..46.
- **الأثر:** نص اختبار إنجليزي في بوابة المواطن وفي صفحات الإدارة، أمام من يُعرض عليه النظام.
- **الإصلاح المقترح:**
  - الفحوص تكتب بيانات عربية واقعية، وتحفظ وسمها في حقل لا يُعرض للعامة.
  - طلبات الشواغر التي تُنشئها الفحوص لا تُنشر في البوابة العامة، ما لم يكن النشر هو ما يُفحص، فتُغلق بعده.
  - بقايا خادم العرض الحالية: تُغلق إعلاناتها العامة بإجراء التطبيق نفسه، بلا حذف. وتبقى سجلات الإدارة تاريخاً.
  - إعادة تهيئة خادم العرض ببيانات نظيفة قرار مستقل (D-67-6).
- **التحقق المطلوب:**
  - **موجب:** لا نص «server check» في البوابة العامة بعد النشر وبعد تشغيل فحوص الخادم.
  - **سالب:** فحوص PLAN-38..52 تبقى خضراء بالوسم الجديد.

#### G-UX-10 — لا حارس يمنع العودة، ولا صور دليل

- **الدليل:**
  - مشروع الاختبارات المعمارية (PLAN-51) يفحص السياسات والمسارات، لا نص الواجهة.
  - لا اختبار يذكر `Content-Security-Policy` (G-UX-02).
  - لا لقطات للصفحات في دليل القبول.
- **الأثر:** كل ما سبق قابل للعودة بصفحة جديدة واحدة.
- **الإصلاح المقترح:**
  - **حارس نص للعرض:** لا تسمية إنجليزية خارج قائمة الاختصارات، ولا طباعة خام لحالة أو معرّف أو JSON خارج الجزء التقني، ولا معالج مضمّن، ولا خاصية `style`.
  - **قوائمه** سقوف تتقلص إلى الصفر، عدا استثناءات تقنية معلنة في الاختبار نفسه.
  - **لقطات** لصفحات رئيسية على عرضي حاسوب وهاتف، في دليل القبول.
- **التحقق المطلوب:**
  - **موجب:** الحارس أخضر.
  - **سالب:** كل قاعدة تفشل على خلل مزروع.

### 4.3 فجوات P3

#### G-UX-09 — جداول بلا رسالة فراغ

- **الدليل:**
  - 27 من 211 صفحة فيها جدول بحلقة تكرار بلا رسالة فراغ: 18 في HR.Core، منها ثلاث للسياسات (اللقطة الأولى: جدول قواعد فارغ برؤوسه وحدها)، وأربع في الرواتب.
  - أعمدة صفحة القالب تتزاحم مع JSON طويل (اللقطة الأولى: `When` و`Priority` ملتصقان).
- **الأثر:** المستخدم لا يعرف هل الجدول فارغ أم لم يكتمل تحميله.
- **الإصلاح المقترح:** `_HrEmptyState` في كل جدول، وعرض مقروء للقواعد (G-UX-06) ينهي تزاحم الأعمدة.
- **التحقق المطلوب:** الحارس يفشل على جدول بلا رسالة فراغ.

### 4.4 تقاطعات مع الدراسات الأخرى

- **G-POL-13 وG-POL-14 ← POL-G:** المحرر المنظّم، وكشف التعارض، والمحاكاة على المسودة. خارطة الاستكمال تضعه في الموجة 3 (§5.4)، وPLAN-47 استبعده صراحة. **أقترح تقديمه** إلى ما بعد الموجة 1، لأنه ما يجعل «تغيير القاعدة دون مطوّر» حقيقياً أمام العميل. هذه الدراسة تغطي العرض المقروء والمحاكاة بنموذج فقط.
- **G-WF-16 ← WF-H:** واجهة إدارة تعريفات المسارات. اليوم واجهة API فقط، تُرسَل لها بيانات JSON ([WorkflowDefinitionsController.cs:21-25](../src/Services/HRCore/HR.Core.Web/Controllers/WorkflowDefinitionsController.cs)). خارطة الاستكمال تضعها في الموجة 3.
- **IAM (D-51-3، D-IAM-7):**
  - استعلام أسماء المستخدمين يتبع نمط `HrCore.Lookup`: الموظف التفاعلي وحده بتعريف PLAN-50.
  - ويقتصر على أسماء جهة السائل، كما يقتصر تدقيق البوابة على صفوف جهته.
  - ولا يضيف نطاقاً جديداً بين الخدمات.
- **OPS:** إعادة تهيئة خادم العرض ببيانات نظيفة تمس النسخ الاحتياطي والبذور (OPS-G). لذلك هي قرار مستقل.

---

## 5. قرارات مطلوبة من صاحب العمل

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-67-1 — مَن يملك سياسة CSP للاستجابة | الخدمة التي ترسم الصفحة. البوابة تضيف رؤوسها لصفحاتها، ولما وصلها بلا رأس | سياسة واحدة لكل صفحة، وnonce يطابق ما في الصفحة |
| D-67-2 — المنطقة الزمنية والتنسيق | العرض بتوقيت `Asia/Damascus` من الإعداد `Localization:TimeZone`، والتخزين UTC كما هو. التاريخ `yyyy/MM/dd` (الأكثر استعمالاً اليوم)، والوقت `yyyy/MM/dd HH:mm`، مع تلميح «بتوقيت دمشق». الإدخال بالتوقيت المحلي ويُحوَّل عند الحفظ | كل الأوقات تتقدم ثلاث ساعات عما يظهر اليوم |
| D-67-3 — مصدر أسماء المستخدمين | سجل في البوابة تملؤه عند كل دخول من مطالبات الرمز: المعرّف، والاسم المعروض، والجهة. ويُجاب لموظفي الجهة نفسها. «النظام» للفاعل الآلي، و«مستخدم غير معروف» مع المعرّف في تلميح للمدقق | الاسم يظهر لكل من دخل مرة بعد النشر. ومن لم يدخل بعده يبقى «غير معروف» |
| D-67-4 — مَن يرى JSON القواعد | مؤلفو السياسات ومدققوها والمسؤول، خلف «عرض تقني». ولا أحد غيرهم | غير المختص يرى الجملة العربية وحدها |
| D-67-5 — عنوان البوابة العامة | `/careers` وحده. و`/ui/hr-core/` يفتح لوحة القيادة | رابط قديم إلى `/ui/hr-core/` لم يعد يفتح البوابة العامة |
| D-67-6 — بقايا الفحوص على خادم العرض | تُغلق إعلاناتها العامة بإجراء التطبيق، بلا حذف. وإعادة تهيئة خادم العرض قرار لاحق مستقل | صفحات الإدارة تحتفظ بالتاريخ |
| D-67-7 — الاختصارات | العربي أولاً، والاختصار بين قوسين: «رمز الصنف (SKU)» | — |

---

## 6. خارطة التنفيذ المقترحة

| الرمز | الخطة | الموضوع | الفجوات | الأولوية | يعتمد على | أيام العمل |
|-------|-------|---------|---------|----------|-----------|-----------|
| UX-A | [PLAN-67](acceptance-plans/PLAN-67-ux-readable-arabic-interface.md) | واجهة عربية مقروءة، وسياسة CSP واحدة، وخادم عرض نظيف | G-UX-01..10 | P1 | PLAN-52 (تسلسل: خطة واحدة في المجلد الرئيسي) | 10–12 |
| POL-G | الموجة 3 (أقترح تقديمها) | المحرر المنظّم والمحاكاة على المسودة | G-POL-13، G-POL-14 | P2 | POL-C، POL-D، PLAN-67 (العرض المقروء) | 7–9 |
| WF-H | الموجة 3 | واجهة تعريفات المسارات والقرار الإداري | G-WF-16، G-WF-18 | P2 | WF-A، WF-C | 6–8 |

PLAN-67 خطة جانبية بعد PLAN-52، كما كانت PLAN-66. لذلك تبدأ أرقام خطط الموجة 2أ من PLAN-68، لا من PLAN-66 كما في [PROGRAM_COMPLETION_ROADMAP.md](PROGRAM_COMPLETION_ROADMAP.md) §7 (البند 6).

---

## 7. معيار «واجهة جاهزة للعرض» المقترح

- **CSP:** كل صفحة رئيسية تصل بسياسة CSP واحدة وبلا مخالفة في المتصفح، على خادم العرض وفي الملف الشبيه بالإنتاج المفروض.
- **لا شيء مضمّن داخل الوسوم:** لا معالج حدث، ولا خاصية `style`، في أي ملف عرض.
- **الحالات بالعربية:** لا تسمية إنجليزية خارج قائمة الاختصارات، ولا حالة أو نوع يُطبع كما هو في الشيفرة.
- **الأسماء:** لا معرّف شخص أو سجل يُطبع بدل اسمه، عدا الاستثناءات التقنية المعلنة.
- **الوقت:** كل وقت بتوقيت المنطقة المضبوطة، وبتنسيق واحد.
- **القواعد:** كل قاعدة سياسة تُقرأ جملة عربية، وJSON خلف «عرض تقني» للمختصين.
- **البوابة العامة:** على `/careers` وحده، بتنسيقها الكامل.
- **بيانات خادم العرض:** لا نص اختبار في البوابة العامة.
- **الحراس:** حارس النص أخضر بلا صفوف سقف، ولقطات الصفحات الرئيسية في دليل القبول.

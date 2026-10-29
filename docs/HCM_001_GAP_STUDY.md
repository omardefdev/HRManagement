# دراسة مسار التوظيف (HCM-001) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-21  
**آخر تحديث:** 2026-10-29 (PLAN-33: سطر «الحالة» لكل فجوة مع الخطة والدليل، وتحويل §7 إلى قائمة تحقق بالحالة الفعلية). الأقسام 1–3 تصف الوضع وقت الدراسة ولم تُعدَّل؛ الحالة الراهنة في أسطر «الحالة» وفي §7.  
**النطاق:** مسار التوظيف من طلب الشاغر حتى تسليم المرشح إلى معالج التعيين (FUN-001)، مع نقاط التماس مع العقود والرواتب والبوابة.  
**المنهج:** قراءة ثابتة للشيفرة في `src/Services/HRCore` و`src/Services/HRContracts` و`src/Shared` والاختبارات والوثائق والأدلة، ومقارنتها بما تدّعيه الوثائق (`HCM_001_RECRUITMENT.md`، PLAN-21/22/23، `HIRE_ONBOARDING_GUIDE.md`، `completion-backlog/09`).  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK على الجهاز وقت الدراسة). كل فجوة أدناه مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.

**الخطط المرتبطة:** [PLAN-25](acceptance-plans/PLAN-25-hcm-production-workflow-binding.md) · [PLAN-26](acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md) · [PLAN-27](acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md) · [PLAN-28](acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md) · [PLAN-29](acceptance-plans/PLAN-29-hcm-hire-contract-documents.md) · [PLAN-30](acceptance-plans/PLAN-30-hcm-candidate-experience.md) · [PLAN-31](acceptance-plans/PLAN-31-hcm-competitions-exams.md) · [PLAN-32](acceptance-plans/PLAN-32-hcm-recruitment-analytics.md) · [PLAN-33](acceptance-plans/PLAN-33-hcm-verification-docs.md)

---

## 1. الخلاصة

1. **المسار مبني بنسبة كبيرة.** توجد كيانات وخدمات وواجهات لكل مرحلة: طلب شاغر مربوط بمنصب ممول، اعتماد بمسار عمل، نشر عام/داخلي، تقديم عبر ثلاث قنوات (`/careers`، HR، ESS)، فرز وCOI وتقييم وترتيب، خطاب عرض باعتماد، لجنة واستئناف، إغلاق تعيين ذري مع نشر `ICandidateAccepted`، ثم معالج التعيين وsaga في العقود.
2. **عبارة «Done» في `HCM_001_RECRUITMENT.md` و`completion-backlog/09` لا تطابق الواقع التشغيلي.** وجدنا 5 عوائق من الدرجة P0، أي أن المسار لا يعمل كما ينبغي خارج بيئة المختبر:
   - **(أ)** في إعدادات Staging/Production لا يُنشأ مسار اعتماد لطلب الشاغر ولا لخطاب العرض، فيعلق كلاهما في `PendingApproval` بلا مخرج.
   - **(ب)** لا توجد أي واجهة أو خدمة لإنشاء **معايير التقييم**، وبدونها لا يمكن الوصول إلى `Assessed` ثم `Ranked` بالمسار السليم.
   - **(ج)** صلاحية دور كل خطوة اعتماد، ونصاب اللجنة، والمراجع المستقل، تُفحص **في الواجهة فقط** ولا يتحقق منها الخادم.
   - **(د)** دور `compliance` المكلّف بمراجعة الاستئناف لا يملك أصلاً صلاحية دخول وحدة HR.Core.
   - **(هـ)** كل شيفرة التوظيف (HCM 1–7 وPLAN-21/22/23) **غير مُودعة في git**، والـ CI الأخضر المذكور في الوثائق يخص commit الأساس فقط.
3. **ثغرات منطقية في سلامة القرار (P1):**
   - رفض العرض أو إلغاؤه لا يعيد حالة الطلب ولا ينتقل إلى المرشح التالي.
   - يمكن إصدار عرض لمرشح غير مرتَّب، فيُتجاوز مبدأ الجدارة.
   - طريق «استئناف قديم» يتجاوز المراجع المستقل.
   - يمكن فتح طلبَي شاغر على المنصب نفسه.
   - شروط العرض (الراتب والدرجة) لا تصل إلى العقد.
4. **فجوات قدرات (P2):** المسابقات متعددة الشواغر والاختبارات الكتابية، وإشعارات المرشح وخدمته الذاتية، والمرفقات والأهلية، والتقارير، والأتمتة الزمنية (انتهاء العرض، إغلاق الإعلان، الاحتفاظ بالبيانات).
5. **فجوة تحقق:** PLAN-22 يعرّف 5 سيناريوهات قبول (AC-HCM-22a..e)، لكن لا يوجد إلا اختبار قبول واحد، وهو ينجح دون فحص إذا لم يجد بيانات. ولا يوجد مجلد أدلة لـ AC-HCM-21/22 ولا لـ WF-23. كذلك `Hcm001RecruitmentTests` يفحص نماذج `SharedKernel.Recruitment` غير المستخدمة في التشغيل.

**التقدير الإجمالي للإكمال:** نحو 67–85 يوم عمل لمطوّر واحد (≈ 4–5 sprints لفريق من مطوّرَين)، منها 15–19 يوماً للمرحلة P0 (PLAN-25 وPLAN-26).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | طلب شاغر من منصب ممول | `RecruitmentRequisitionsController.Create` — منصب `Vacant`، مرجع قانوني SYR-001، قناة نشر | ✅ مع ثغرة تكرار الطلب (G-11) |
| 2 | اعتماد الطلب | `RECRUITMENT-REQUISITION-APPROVAL` (خطوتان) + SoD للمنشئ | ⚠️ يعمل في المختبر فقط (G-02)، وفحص الدور في الواجهة فقط (G-04) |
| 3 | النشر عام/داخلي | `PublishAsync` / `PublishInternalAsync` → `PublicJobPosting` / `InternalVacancy` | ⚠️ لا تاريخ إغلاق عند النشر (G-17) |
| 4 | التقديم | `/careers` (CitizenApplication)، HR (RecruitmentApplication)، ESS (InternalApplication) ثم Promote | ⚠️ قناتان بحالات منفصلة بلا مزامنة (G-16) |
| 5 | الفرز وCOI | `SetStatus` + `DeclareCoi` | ✅ |
| 6 | المقابلة | `ScheduleInterview` فقط | ❌ لا إكمال/إلغاء/نتيجة (G-08) |
| 7 | التقييم | `RecordAssessment` يتطلب `CriteriaVersionId` | ❌ لا واجهة لإنشاء المعايير (G-03) |
| 8 | الترتيب | `AssignRank` يدوي مع منع التكرار | ⚠️ يمكن تجاوزه بالعرض المباشر (G-07) |
| 9 | لجنة الاختيار | جلسة + أعضاء + مسار 3 خطوات | ⚠️ شكلية: غير مربوطة بالمرشحين ولا شرطاً للعرض (G-10) |
| 10 | الاستئناف | `FileAppeal` + مسار مراجع مستقل + طريق قديم `ReopenAppeal` | ❌ الدور المكلّف لا يصل، والطريق القديم يتجاوز الحوكمة (G-05، G-09) |
| 11 | خطاب العرض | مسودة ← اعتماد خطوتين ← `Issued` ← قبول/رفض | ⚠️ يعلق في الإنتاج (G-02)، والرفض لا يعالج الطلب (G-06) |
| 12 | التعيين | `RecruitmentHireClosureService` ذري: شغل المنصب، إغلاق الطلب، رفض البقية، `ICandidateAccepted` v3، `IEmployeeCreated` عبر Outbox | ✅ مع ثغرات التكرار وشروط العقد (G-12، G-13) |
| 13 | معالج التعيين (FUN-001) | معاملة Appointment + checklist 7 بنود + HireCase saga | ✅ (خارج نطاق هذه الدراسة إلا نقاط التماس) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- نموذج نطاق واضح في `HR.Core.Domain` مع قواعد انتقال صريحة: `SelectionWorkflowRules`، `OfferWorkflowRules`، `RequisitionWorkflowRules`، `RecruitmentAppealWorkflowRules`.
- ترحيلات EF لكل مرحلة (`HCM001`…`HCM005`، `HCM022_SelectionCommitteeApprovedAt`).
- إغلاق تعيين ذري داخل Transaction مع Outbox (`AddPostgresOutbox<HRCoreDbContext>`)، وidempotency عبر `IsConvertedToEmployee`، وتسجيل نسخة المنصب الممول (`FundedPositionVersion`).
- ربط قانوني SYR-001 من الطلب حتى العرض والتعيين (`RecruitmentLegalSourceValidator`، `LineageMatches`) وربط بنظام الخدمة SYR-003.
- خصوصية المرشح: `NationalIdHash`، إخفاء الهوية، حجز قانوني، شريحة DSAR في Compliance.
- بوابة `/careers` بـ rate limiting وhoneypot وموافقة خصوصية وتتبع برقم مرجعي.
- صندوق عمل موحّد (PLAN-23) يجمع اعتمادات الطلب والعرض واللجنة والاستئناف.
- معالج التعيين في العقود (HireWizard + saga + تعويض + checklist بمالك وSLA).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار · **P2** قدرة ناقصة للمنتج · **P3** تحسين.

### 4.1 عوائق P0

#### G-01 — شيفرة التوظيف غير مُودعة ولم تمر بالـ CI
- **الدليل:** `git log` يحوي 3 commits فقط، آخرها `docs(syd): record G0 closure…`. و`git status` تحت `src/Services/HRCore` و`docs/acceptance-plans` و`scripts` يُظهر **283 ملفاً غير متتبَّع و140 ملفاً معدّلاً**، منها كل كيانات التوظيف (`RecruitmentRequisition.cs`، `OfferLetter.cs`، `RecruitmentAppeal.cs`…) وترحيلات `HCM001`–`HCM005`.
- **الأثر:** لا ضمان أن الشيفرة تُبنى أو أن اختباراتها تنجح في CI، ولا إمكانية مراجعة أو تراجع. ادعاء «green CI» في الوثائق لا يغطي هذا العمل.
- **الخطة:** PLAN-25 المرحلة 0.
- **الحالة:** ⚠️ مغلقة جزئياً — شيفرة التوظيف كلها مُودعة الآن في commits محلية (لا ملفات غير متتبَّعة تحت `src/Services/HRCore`)، وآخرها commits فرع PLAN-33 (`7c9dcf2`، `5d4e4d0`، `b93ddba`+`29670a0`، `4588a23`، `95c70e1`، `d91b7b8`، `69ad2e3`). لكن لم يُدفع أي منها إلى المستودع البعيد، فلم يُشغَّل الـ CI البعيد على هذا العمل قط (`reports/plan33-progress.md`: «local only, never pushed»). بقية البند (CI أخضر على البعيد) ضمن PLAN-25 §0 — **Proposed، مؤجلة**.

#### G-02 — الاعتماد معلّق في إعدادات Staging/Production
- **الدليل:**
  - `RecruitmentRequisitionLifecycleService.cs:41-69` و`RecruitmentOfferService.cs:101-129`: عند `PolicyEnforcementMode.Off` تُضبط الحالة على `PendingApproval`، ولا يبدأ المسار إلا إذا كان `IsLabScale=true`.
  - `appsettings.Production.json:33` و`appsettings.Staging.json:9`: `IsLabScale=false`.
  - لا يوجد مفتاح `PolicyEngine:DefaultMode` ولا `PolicyEngine:Core.Recruitment` في أي `appsettings` أو `docker-compose*` أو `deploy/`، فالوضع الافتراضي `Off`.
  - `SelectionCommitteeLifecycleService.cs:44-62`: التقديم في هذا الوضع لا يفعل شيئاً ويعيد نجاحاً.
- **الأثر:** في الإنتاج يبقى كل طلب شاغر وكل خطاب عرض في `PendingApproval` إلى الأبد، إذ لا يوجد إجراء اعتماد مباشر بديل. الواجهة تعرض فقط تحذير «فعّل PolicyEngine…». المسار متوقف كلياً.
- **الخطة:** PLAN-25 (فصل بدء مسار الاعتماد عن وضع محرك السياسات).
- **الحالة:** ❌ مفتوحة — PLAN-25 (**Proposed، مؤجلة**). أثبت PLAN-33 السلوك الحالي دون إصلاحه: `src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs` (6/6) — مع `IsLabScale=false` والمحرك `Off` يبقى طلب الشاغر وخطاب العرض في `PendingApproval` بلا نسخة مسار مع تحذير في الواجهة، ويُرفض الاعتماد، ولا يصل العرض إلى `Issued`؛ أما جلسة اللجنة فتُقفَل دون مسار و**دون أي تحذير** (توقف صامت). ونسخة E2E الشبيهة بالإنتاج (`docker-compose.acceptance-prodlike.yml`، `Category=JourneyProdLike`) تتوقف في المرحلة الأولى: الطلب بعد التقديم `PendingApproval` بلا نسخة مسار (`acceptance-evidence/AC-HCM-33/prodlike/result.json`).

#### G-03 — لا توجد طريقة لإنشاء معايير التقييم
- **الدليل:**
  - `SelectionWorkflowRules.cs:57-62`: الانتقال إلى `Assessed` يتطلب `CriteriaVersionId`.
  - `RecruitmentSelectionService.RecordAssessmentAsync` يرفض أي معايير غير موجودة لطلب الشاغر.
  - الموضع الوحيد الذي يُنشئ `RecruitmentAssessmentCriteria` هو `AcceptanceDataSeeder.cs:637`، ولا متحكم ولا خدمة ولا واجهة.
- **الأثر:** في أي جهة حقيقية لا يمكن تقييم المرشح، وبالتالي لا يمكن ترتيبه بالمسار السليم. وهذا يدفع المستخدمين إلى طرق التفاف (G-07).
- **الخطة:** PLAN-26.
- **الحالة:** ✅ عولجت في [PLAN-26](acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md) (Implemented) — AC-HCM-26a..g وi ناجحة حيّاً (2026-10-21، `acceptance-evidence/AC-HCM-26/`).

#### G-04 — دور الخطوة والبوابات الإضافية تُفحص في الواجهة فقط
- **الدليل:**
  - `WorkflowStageGuard.ValidateApprove` و`WorkflowEngine.ApproveStep(:39)` لا يفحصان `RoleRequired`.
  - الفحص موجود فقط في `WorkflowPanelBuilder.cs:159-173` (لإظهار الأزرار أو إخفائها).
  - نصاب اللجنة يُفحص فقط في `SelectionCommitteeWorkflowUiService.cs:44`، والمراجع المستقل فقط في `RecruitmentAppealWorkflowUiService` (`AdditionalBlockReason`).
  - نقاط POST `ApproveWorkflow` محمية بسياسة `HrCoreBranchOperation` العامة: Admin وHrCore وBranchManager وHrManagementApprover.
- **الأثر:** أي مستخدم `hr-core` غير منشئ يستطيع بطلب POST مباشر اعتماد خطوة تتطلب `hr-management-approver`، أو اعتماد لجنة بلا نصاب، أو البت في استئناف ليس مراجعه المستقل. هذا يكسر فصل الواجبات الذي يدّعيه PLAN-21/22.
- **الخطة:** PLAN-25.
- **الحالة:** ❌ مفتوحة — PLAN-25 (**Proposed، مؤجلة**). `RoleRequired` للخطوة ما زال يُفحص في `WorkflowPanelBuilder` وصندوق العمل فقط، ولا يفحصه `WorkflowStageGuard` ولا `WorkflowEngine` (`src/Shared/HRManagement.ServiceDefaults/Workflow/`) عند POST. نُقل إلى الخادم في PLAN-28 فحص نصاب اللجنة وفصل الواجبات فيها والمراجع المستقل للاستئناف (داخل خدمات الكيان، لا في البوابات العامة).

#### G-05 — دور `compliance` لا يصل إلى مراجعة الاستئناف
- **الدليل:**
  - خطوة `RECRUITMENT-APPEAL-REVIEW` تتطلب الدور `compliance` (`WorkflowPackSeeder.cs:59-61`).
  - سياستا `ModuleHrCore` و`HrCoreReadOnly` لا تشملان `compliance` (`HrManagementAuthorizationExtensions.cs:13-22, 62-71`)، والفلتر العام `HrModuleAuthorizationFilter` يطبّق `ModuleHrCore` على كل متحكمات HR.Core.
  - `ApproveAppealWorkflow` يتطلب `HrCoreBranchOperation`.
  - الشخصية `a-compliance` في `testdata/acceptance/personas.json` لديها `['compliance']` فقط.
- **الأثر:** السيناريو AC-HCM-22d غير قابل للتحقيق كما صُمم، فالاستئنافات تبقى معلّقة أو تُحسم بطرق التفاف.
- **الخطة:** PLAN-25 (الصلاحيات) + PLAN-28 (منطق الاستئناف).
- **الحالة:** ⚠️ مغلقة جزئياً — عولج جزء الاستئناف في PLAN-28 (شريحة من PLAN-25): متحكما `RecruitmentAppeals` و`WorkInbox` مستثنيان من مرشح الوحدة (`HrModuleAuthorizationFilter`) ويحملان سياستَي `HrCoreAppealReview` و`HrCoreWorkInbox` اللتين تقبلان دور `compliance`، وقواعد البوابة (`GatewayPathAuthorizationMiddleware`) تسمح لمستخدم `compliance` بصفحات الاستئناف وصندوق العمل فقط. الدليل: AC-HCM-28a..i (2026-10-22، `acceptance-evidence/AC-HCM-28/run-summary.md`) وAC-HCM-22d بمراجع `compliance` مُسنَد (2026-10-29، `acceptance-evidence/AC-HCM-22/`). بقية PLAN-25 (البوابات العامة لدور الخطوة) مؤجلة.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-06 — رفض العرض أو إلغاؤه أو انتهاؤه لا يعالج الطلب ولا ينتقل للمرشح التالي
- **الدليل:**
  - `RecruitmentOfferService.DeclineAsync` (`:176-186`) و`RevokeAsync` (`:188-198`) يغيّران حالة العرض فقط. الطلب يبقى `OfferExtended` و`ActiveOfferLetterId` يبقى مشيراً إلى عرض ميت.
  - `OfferStatus.Expired` لا يضبطه أي سطر في الشيفرة، و`CanAccept` يكتفي برفض القبول بعد التاريخ.
- **الأثر:** طلبات عالقة، ولا «قائمة احتياط»، ولا تتبع لسبب الرفض. ولإكمال الشاغر يُضطر HR إلى تحريك مرشح آخر يدوياً دون قاعدة.
- **الخطة:** PLAN-27.
- **الحالة:** ✅ عولجت في PLAN-27 — الرفض/الإلغاء/الانتهاء تحسم الطلب (`OfferDeclined`/`Ranked`/`Rejected`)، مهمة انتهاء العروض، اقتراح المرشح التالي بالجدارة، وقائمة احتياط بمهلة. انظر [HCM_001_RECRUITMENT.md](HCM_001_RECRUITMENT.md#plan-27--offer-integrity-merit-principle-and-reserve-list-hcm-e). الدليل: AC-HCM-27a..h ناجحة حيّاً (2026-10-22، `acceptance-evidence/AC-HCM-27/run-summary.md`).

#### G-07 — يمكن تجاوز الترتيب والجدارة عند إصدار العرض
- **الدليل:**
  - `RecruitmentOfferService.CreateDraftAsync` (`:27-81`) لا يتحقق من حالة الطلب ولا من رتبته.
  - `Views/RecruitmentApplications/Details.cshtml:273` يُظهر نموذج إنشاء العرض لحالتي `Ranked` و**`Assessed`**.
  - مرشح `Assessed` يحصل على عرض، فتبقى حالته `Assessed`، ثم `AcceptAsync` ينقله مباشرة إلى `OfferAccepted` متخطياً الترتيب.
  - لا يوجد فحص أن المرشح صاحب العرض هو الأعلى رتبة المتاح.
  - الراتب والدرجة يُدخلان نصاً حراً دون تحقق من درجة المنصب الممول.
- **الأثر:** خرق مبدأ الجدارة والشفافية، وهو جوهر معايير قبول HCM-001.
- **الخطة:** PLAN-27.
- **الحالة:** ✅ عولجت في PLAN-27 — العرض للمرتبين فقط وبترتيب الجدارة، والاستثناء (تجاوز الجدارة/اختلاف الدرجة/راتب غير متحقق) بمبرر ومسار من ثلاثة معتمدين مختلفين؛ الدرجة من المنصب الممول والراتب ضمن حدود سلّم الرواتب في Payroll. الدليل: AC-HCM-27a..h (2026-10-22، `acceptance-evidence/AC-HCM-27/`).

#### G-08 — دورة المقابلة ناقصة
- **الدليل:**
  - `RecruitmentInterviewService` لا يملك إلا `ScheduleAsync`.
  - `InterviewSlotStatus.Completed` و`Cancelled` لا يضبطهما أي سطر.
  - الجدولة من `Assessed` أو `Ranked` تعيد الحالة إلى `InterviewScheduled` (`:32-38`)، فتمحو التقدم.
  - لا ربط للمقابلين، ولا درجات لكل معيار، ولا تعدد مقيّمين.
- **الخطة:** PLAN-26.
- **الحالة:** ✅ عولجت في [PLAN-26](acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md) — AC-HCM-26a..g وi ناجحة حيّاً (2026-10-21، `acceptance-evidence/AC-HCM-26/`). وتمر رحلة E2E (`RecruitmentToHireJourneyE2ETests`) بالمقابلة ومقيّمَين اثنين قبل الترتيب (`acceptance-evidence/AC-HCM-33/`).

#### G-09 — مساران متوازيان للاستئناف، أحدهما يتجاوز الحوكمة
- **الدليل:**
  - `RecruitmentSelectionService.ReopenAppealAsync` وزر «استئناف (مسار قديم)» في `Details.cshtml:257` ينقلان الطلب المرفوض إلى `UnderReview` مباشرة بصلاحية `HrCoreWrite`، دون مراجع مستقل ولا مسار عمل.
  - المراجع المستقل يُدخل كـ GUID نصي (`Details.cshtml:202`).
  - تعارض نوع المعرّف: `FileAppealAsync` يقارن المراجع بـ `EmployeeId` لأعضاء اللجنة (`RecruitmentAppealService.cs:47-49`)، بينما `CanDecideAppeal` يقارنه بـ `sub` المستخدم في Keycloak.
  - اعتماد المسار يعني دائماً «قبول الاستئناف»، ولا يوجد قرار صريح (قبول/رفض/تعديل رتبة).
  - يمكن الاستئناف بعد إتمام التعيين على الشاغر نفسه دون أثر عملي.
- **الخطة:** PLAN-28.
- **الحالة:** ✅ عولجت في PLAN-28 — طريق واحد: تقديم بلا مراجع، ثم تعيين مراجع مستقل من منتقي الموظفين (مستقل عن اللجنة والمقابلة والتقييم) يبدأ المسار، وقرار صريح (`Upheld`/`UpheldRankCorrection`/`Dismissed`/`UpheldNoRemedy`) لا يصدره إلا المراجع المعيّن بدور `compliance` ومطالبة `employee_id`، مع طلب معلومات واستيفاء. حُذف `ReopenAppeal` ونُقل تاريخه إلى استئنافات `LegacyReopen`، والمهلة تُحسب من `RejectedAt` بحسب نوع الرفض، ولا تعيين مع استئناف معلق. انظر [HCM_001_RECRUITMENT.md](HCM_001_RECRUITMENT.md#plan-28--selection-committee-and-appeal-governance-hcm-f). الدليل: AC-HCM-28a..i (2026-10-22، `acceptance-evidence/AC-HCM-28/run-summary.md`) وAC-HCM-22c/22d (2026-10-29، `acceptance-evidence/AC-HCM-22/`).

#### G-10 — لجنة الاختيار شكلية
- **الدليل:**
  - `SelectionCommitteeSession` مربوطة بطلب الشاغر فقط، لا بالمرشحين ولا بالدرجات.
  - `SelectionCommitteeMember.Vote` نص غير مستخدم.
  - `ApprovedAt` ليس شرطاً في `SelectionWorkflowRules` للترتيب أو العرض.
  - `AddMemberAsync` يقبل الإضافة بعد التقديم أو الاعتماد، ولا يمنع تكرار العضو.
  - لا SoD لمنشئ الجلسة في `ValidateSelfApprovalAsync`، واختيار العضو يتم بـ GUID.
- **الخطة:** PLAN-28.
- **الحالة:** ✅ عولجت في PLAN-28 — أصوات وتعارض مصالح لكل مرشح وتوصيات برتبة مقترحة، قفل بعد التقديم، نصاب وفصل واجبات على الخادم، محضر PDF، واعتماد اللجنة شرط للترتيب على الطلبات الخاضعة للجنة (`RequiresCommitteeApproval`، القرار D-5). الدليل: AC-HCM-28a..i (2026-10-22، `acceptance-evidence/AC-HCM-28/`). تنبيه: في غير المختبر مع المحرك `Off` تُقفَل الجلسة دون مسار ودون تحذير (انظر G-02).

#### G-11 — سلامة طلب الشاغر مقابل الملاك
- **الدليل:**
  - `RequisitionWorkflowRules.ActiveStatuses` (`:9`) لا تشمل `Published`، فبعد النشر يمكن إنشاء طلب ثانٍ على المنصب نفسه، ولا يوجد فهرس فريد مشروط في DB.
  - `CanCancel` (`:81`) يرفض إلغاء طلب `Approved/Published` رغم أن `CanTransition` يسمح به.
  - `CloseAsync` لا يحسم الطلبات المفتوحة على الشاغر.
  - `EstablishmentService.FreezePosition` لا يتعامل مع طلبات الشاغر المفتوحة على المنصب.
- **الخطة:** PLAN-26.
- **الحالة:** ✅ عولجت في [PLAN-26](acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md) — الدليل الحي AC-HCM-26a..g وi (2026-10-21، `acceptance-evidence/AC-HCM-26/`). استثناء: تتابع تجميد المنصب (AC-HCM-26h) لم يُشغَّل حيّاً قط، لعدم وجود معتمد `hr-core` مركزي ثانٍ في كتالوج الشخصيات؛ وهو مثبت على PostgreSQL في `src/Tests/HRManagement.IntegrationTests/Hcm026EstablishmentCascadeIntegrationTests.cs`. حالته في إعادة التشغيل الحي (P6) في `reports/plan33-final-report.md`.

#### G-12 — إنشاء موظف مكرر وأخطاء غير معالجة عند التعيين
- **الدليل:**
  - `HireAndCreateEmployeeAsync` لا يبحث عن موظف بالرقم الوطني نفسه قبل الإنشاء، والفهرس الفريد `(TenantId, NationalIdHash)` (`HRCoreDbContext.cs:128`) سيرمي `DbUpdateException`.
  - المتحكم يلتقط `InvalidOperationException` فقط (`RecruitmentApplicationsController.cs:473`)، فالنتيجة خطأ 500.
  - لا مقارنة بين الرقم الوطني المُدخل ورقم الطلب.
  - `EmploymentType` مثبت على `Permanent` (`RecruitmentHireClosureService.cs:309, 394`) أياً كانت فئة التوظيف المختارة (PERMANENT/TEMPORARY/CONTRACT/INTERNSHIP).
- **الخطة:** PLAN-29.
- **الحالة:** ✅ عولجت في [PLAN-29](acceptance-plans/PLAN-29-hcm-hire-contract-documents.md) (Implemented) — AC-HCM-29b (رقم وطني لموظف قائم ← رسالة تحقق لا 500) وAC-HCM-29c (رقم وطني مخالف بلا سبب ← رفض) وAC-HCM-29a (نوع العقد مشتق من النظام) ناجحة حيّاً (2026-10-22، `acceptance-evidence/AC-HCM-29/`).

#### G-13 — شروط العرض لا تصل إلى العقد
- **الدليل:**
  - `ICandidateAccepted` يحمل معرّفات فقط، بلا راتب ولا درجة ولا نوع عقد ولا نظام خدمة.
  - `ProcessCandidateAcceptedHandler` (`InboundEventCommands.cs:52-66`) ينشئ عقداً معلّقاً من نوع `DEFAULT` فقط، ويتجاهل الأمر بصمت إن لم يوجد هذا النوع.
  - نسب المعاملة إلى طلب التوظيف يُخزَّن داخل نص `Notes` ويُستخرج بتحليل نصي (`:97-121`).
  - الأنبوب موجود لكنه فارغ: `IContractActivated` يحمل `BaseSalary` و`PayGradeCode`، لكن العقود تنشرهما `null` دائماً (`HR.Contracts.Infrastructure/Messaging/MassTransitEventPublisher.cs:43-45`)، وكيان العقد لا يحوي حقلَي راتب أو درجة. لذلك يُبنى ملف الأجر في الرواتب دون شروط العرض المعتمدة.
  - فئات التوظيف في نموذج التعيين (PERMANENT/TEMPORARY/CONTRACT/INTERNSHIP في `RecruitmentHireViewModel.cs:18-24`) لا تطابق `EmploymentType` في النطاق (Permanent/Administrative/Temporary)، ولا الأصناف الثلاثة في `Requariments.md` (دائم – إداري – متعاقد مؤقت).
  - **يحتاج تحقق:** في التنقل الداخلي يُنشر `ICandidateAccepted` أيضاً، فقد تُنشأ معاملة `Appointment` لموظف قائم.
- **الخطة:** PLAN-29.
- **الحالة:** ✅ عولجت في [PLAN-29](acceptance-plans/PLAN-29-hcm-hire-contract-documents.md) — العقد المعلّق بنوع مشتق من نظام الخدمة وبدرجة العرض وراتبه، و`IContractActivated` يحمل الراتب والدرجة إلى Payroll، وتدخل مرئي حين لا يوجد نوع عقد مطابق، ونسب المعاملة إلى طلب التوظيف في حقول صريحة. وتأكد بند «يحتاج تحقق» (التنقل الداخلي كان يُنشئ `Appointment`) وعولج. الدليل: AC-HCM-29a، d، e، h، i (2026-10-22، `acceptance-evidence/AC-HCM-29/`)، ورحلة E2E تنتهي بمعاملة `Appointment` وعقد بدرجة العرض وراتبه (`acceptance-evidence/AC-HCM-33/`). ملاحظة متابعة من PLAN-33: HR.Contracts يطبّق `X-Correlation-ID` القادم في ترويسة الرسالة على سياق التنفيذ لكنه لا يحفظه ولا يسجّله في مسار النجاح.

#### G-14 — مسار `RECRUITMENT-HIRE` يتيم في وضع Enforce
- **الدليل:** القاعدة `R-REC-HIRE` (`PolicyEngineDomainSeeds.cs:86-91`) تطلق `RECRUITMENT-HIRE` عند `Hire`، لكن `RecruitmentApplicationsController.Hire` يتجاهل `WorkflowInstanceId` ويعيّن فوراً، ولا يوجد handler لـ `RecruitmentApplication` في `WorkflowInstanceActionService`.
- **الأثر:** عناصر معلّقة في صندوق العمل لا يغيّر اعتمادها شيئاً.
- **الخطة:** PLAN-25.
- **الحالة:** ❌ مفتوحة — PLAN-25 (**Proposed، مؤجلة**). `RECRUITMENT-HIRE` ما زال معرّفاً في `PolicyEngineDomainSeeds.cs` و`WorkflowPackSeeder.cs`، ولا معالج له في `WorkflowInstanceActionService`.

#### G-15 — ثغرات صلاحيات في متحكمات الإعلانات والشواغر الداخلية
- **الدليل:** `PublicJobPostingsController` و`TalentMarketplaceController` بلا `[Authorize(HrCoreWrite)]` على `Edit` و`Delete` و`ChangeApplicationStatus` و`DeclareCitizenCoi`، فيكفي دور الوحدة (مثل `hr-management-approver` أو `branch-manager`) لتعديلها. كما أن `ClosingDate` قابل للتعديل في أي وقت دون تدقيق.
- **الخطة:** PLAN-25.
- **الحالة:** ❌ مفتوحة — PLAN-25 (**Proposed، مؤجلة**). إجراءات `Edit` و`Delete` و`ChangeApplicationStatus` و`DeclareCitizenCoi` في `PublicJobPostingsController.cs`، و`Edit` و`Delete` و`ChangeApplicationStatus` في `TalentMarketplaceController.cs`، ما زالت بلا `[Authorize(HrCoreWrite)]` على مستوى المتحكم.

### 4.3 تجربة المرشح وتوحيد القنوات (P1/P2)

#### G-16 — قناتا تقديم بحالات منفصلة بلا مزامنة
- **الدليل:**
  - المواطن يمر بـ `CitizenApplicationStatus` (Received→UnderReview→Shortlisted→Accepted/Rejected) في `PublicJobPostings/Details`.
  - ثم `PromoteFromCitizen` ينشئ `RecruitmentApplication` بحالة `Shortlisted` مباشرة (`RecruitmentApplicationsController.cs:533`)، فيتكرر الفرز وCOI.
  - الترقية الداخلية لا تنقل إقرار COI (`:599`).
  - لا مزامنة عكسية، و`BuildTimeline` (`PublicCareersController.cs:196, 204`) يعلّم «المقابلة» و«التعيين» كمنجزتين لمجرد الترقية، ولا يُظهر للمرشح الرفض أو التعيين النهائي.
- **الخطة:** PLAN-30.
- **الحالة:** ✅ عولجت في PLAN-30 — كل تقديم مرتبط بطلب شاغر (عام أو داخلي) ينشئ `RecruitmentApplication` بحالة `Submitted` فوراً مع `IntakeChannel`، والربط عبر `PromotedRecruitmentApplicationId` (فهرس فريد)، وقائمة فرز واحدة لكل طلب شاغر، وحالة المرشح مشتقة من المسار الرئيسي عبر `CandidatePublicStatusProjection` وسجل `RecruitmentApplicationEvent`، وترحيل الطلبات السابقة بسكربت `hcm030-backfill-unified-intake.ps1`. انظر [HCM_001_RECRUITMENT.md](HCM_001_RECRUITMENT.md#plan-30--candidate-experience-unified-channel-and-notifications-hcm-h). الدليل: AC-HCM-30a وb وj ناجحة حيّاً (2026-10-29، `acceptance-evidence/AC-HCM-30/`، و`reports/plan30-final-report.md` §7).

#### G-17 — طلب المواطن بلا مرفقات ولا مؤهلات ولا أهلية، والإعلان بلا تاريخ إغلاق
- **الدليل:**
  - `CitizenApplicationApplyViewModel` يحوي فقط الاسم والرقم الوطني والهاتف والبريد والملاحظة، رغم أن `FR-PUB-02` في `THESIS_REQUIREMENTS_CATALOG_CURRENT.md:69` يطلب حفظ الملفات عبر File.Storage.
  - `MapToPublicJobPosting` يضبط `ClosingDate = null` (`RecruitmentRequisitionService.cs:65, 86`)، رغم أن `FR-HIRE-01` يطلب أن يحدد الإعلان الفترة.
- **الخطة:** PLAN-30.
- **الحالة:** ✅ عولجت (تاريخ الإغلاق في PLAN-26؛ الباقي في PLAN-30) — ملف المرشح (المؤهل والتخصص وسنة التخرج والخبرة وتاريخ الميلاد)، معايير الشاغر، أعلام أهلية لا ترفض (وتجاوزها بسبب مُدقَّق)، ومستندات (CV/شهادة/هوية) تُرفع إلى File.Storage عبر هوية خدمة HR.Core وتُفحص قبل الإتاحة، ومستندات مطلوبة لكل شاغر. الدليل: AC-HCM-30d ناجحة حيّاً (2026-10-29، `acceptance-evidence/AC-HCM-30/`). **غير مثبت حيّاً بعد:** رفع المستندات السليمة وفحصها وتنزيلها وحجر EICAR (AC-HCM-30c)، إذ تُخطّي في تشغيل 2026-10-29 لغياب File.Storage عن مكدّس القبول (`reports/plan30-final-report.md` §7). صارت File.Storage تعمل في القبول بالمزوّد `Local` (commit `4588a23`)، ونتيجة 30c في إعادة التشغيل الحي (P6) في `reports/plan33-final-report.md`. ورحلة E2E ترفع ملف PDF مع التقديم العام وتنجح (`acceptance-evidence/AC-HCM-33/`).

#### G-18 — لا إشعارات للمرشح ولا خدمة ذاتية للعرض ولا وثيقة عرض رسمية
- **الدليل:**
  - لا استخدام لأي خدمة إشعارات في `Services/Recruitment` ولا في `PublicCareersController`.
  - قبول العرض أو رفضه يتم من HR نيابة عن المرشح (`OfferLettersController.AcceptOffer`).
  - تقديم الاستئناف إجراء داخلي في HR.
  - لا توليد PDF لخطاب العرض رغم وجود `HRManagement.Documents.QuestPdf`.
  - التنفيذ الوحيد لـ `INotificationService` هو `InAppNotificationService`. لا يوجد مرسل بريد أو SMS في الشيفرة رغم أن COM-001 يعرّف القناتين، والمرشح الخارجي لا يملك حساباً داخل التطبيق أصلاً.
- **الخطة:** PLAN-30 (إشعارات وخدمة ذاتية) + PLAN-29 (الوثائق).
- **الحالة:** ✅ عولجت (الوثيقة في PLAN-29؛ الباقي في PLAN-30) — مرسلات قنوات بريد/SMS بجانب `INotificationService` (محوّل FileDrop في المختبر؛ لا مزوّد في الإنتاج حتى D-8)، رسالة لكل نقطة قرار بلا رقم وطني ولا راتب وبموافقة المرشح، وبوابة `/careers/my` برمز تحقق لمرة واحدة (القرار D-4): تنزيل خطاب العرض، القبول/الاعتذار، الانسحاب، الاستئناف ضمن المهلة، واستكمال المستندات؛ والقبول بالنيابة يبقى `HrCoreWrite` مع سبب ومستند إثبات إلزاميين وتدقيق. متبقٍ: إشعارات المرشحين الداخليين (خارج النطاق) واختيار المزوّد (D-8). الدليل: AC-HCM-30e–h ناجحة حيّاً (2026-10-29، `acceptance-evidence/AC-HCM-30/`)، وAC-HCM-29f لـ PDF العرض (2026-10-22، `acceptance-evidence/AC-HCM-29/`)، وقبول المرشح للعرض برمز التحقق على `/careers/my` ضمن رحلة E2E (`acceptance-evidence/AC-HCM-33/`).

### 4.4 قدرات ناقصة (P2)

#### G-19 — المسابقات متعددة الشواغر والاختبارات الكتابية
- **الدليل:**
  - `RequisitionWorkflowRules.ValidateVacancyCount` (`:25-26`) يفرض `VacancyCount == 1`، وطلب الشاغر مربوط بمنصب واحد.
  - لا كيان لاختبار كتابي أو جلسة امتحان أو إعلان نتائج.
  - `Requariments.md` يذكر صراحة «وضع آليات الانتقاء والاختبارات» و«نتائج الاختبارات» كمدخل لفرع الخدمة والعقود.
- **الخطة:** PLAN-31 (تحتاج قراراً من صاحب العمل، انظر §5).
- **الحالة:** ✅ عولجت (PLAN-31، بقرار D-2 بتاريخ 2026-10-29) — مسابقة متعددة الأسطر تجمع طلبات شاغر معتمدة (يبقى كل طلب لمنصب
  واحد)، طلب واحد لكل شخص بتفضيلات مرتبة، جلسات امتحان وأرقام امتحانية عشوائية وبطاقات PDF، تصحيح مزدوج مجهول بدور
  `hr-exam-marker` مع مطابِق، نتائج تُعتمد وتُنشر بأرقام الامتحان، اعتراض خلال مدة تبدأ من أول نشر بنسخ نتائج متتالية، توزيع حسب
  الجدارة ثم التفضيل، احتياط لكل سطر مع الاستدعاء، ومسودات عروض جماعية في الاعتماد القائم. المدخلات القانونية لكل مسابقة ولا قيم
  افتراضية في الإنتاج. دليل القبول AC-HCM-31a..g ناجح حيّاً (2026-10-29، `acceptance-evidence/AC-HCM-31/`).

#### G-20 — لا أتمتة زمنية
- **الدليل:** الخدمة الخلفية الوحيدة الخاصة بالنطاق والمسجلة في HR.Core هي `LeaveScheduleReconcilerJob` (`HR.Core.Web/Program.cs:143`). لا مهام لانتهاء العروض، ولا لإغلاق الإعلانات بتاريخها، ولا لتطبيق الاحتفاظ على بيانات المرشحين. `CandidateLifecycleController` واجهة JSON يدوية بلا شاشة.
- **الخطة:** PLAN-26/27/30.
- **الحالة:** ✅ عولجت — PLAN-26 (إغلاق التقديم)، PLAN-27 (انتهاء العروض والاحتياط)، PLAN-30 (جزء بيانات المرشحين): مهمة `CandidateRetentionJob` تُخفي هوية الزوج (طلب القناة + طلب التوظيف) بعد إغلاق/إلغاء الشاغر و`Recruitment:Retention:Months` (36) مع احترام الحجز القانوني والاستئناف المعلق، وتحذف المستندات ورموز OTP وتحيّد الرسائل؛ تشغيل تجريبي افتراضياً في الإنتاج حتى اعتماد مسؤول حماية البيانات، وشاشة «الاحتفاظ ببيانات المرشحين» بدل واجهة JSON وحدها. الدليل: `acceptance-evidence/AC-HCM-26/` (2026-10-21)، و`acceptance-evidence/AC-HCM-27/` (2026-10-22)، وAC-HCM-30i (2026-10-29، `acceptance-evidence/AC-HCM-30/`).

#### G-21 — لا تقارير ولا مؤشرات توظيف
- **الدليل:** لا مؤشرات funnel أو time-to-hire أو تقارير لكل طلب شاغر. ما يوجد في Dev.Analytics بيانات تجريبية فقط (`DemoAnalyticsSeeder.cs`).
- **الخطة:** PLAN-32.
- **الحالة:** ✅ عولجت — PLAN-32: اثنا عشر مؤشراً معرّفاً في STR-007 (`recruitment_*`) ومحسوبة بدقة من سجل أحداث الطلبات مع قرائن
  موثقة للصفوف القديمة والتغطية؛ شاشة «تقارير التوظيف» (قمع، طلبات الشاغر، الاعتمادات المتأخرة عن مهلة موثقة، العروض وأسباب
  رفضها بفئات، القنوات، الاستئنافات والاعتراضات، تجاوزات الجدارة، الشواغر المفتوحة، المسابقات) بنطاق الوحدة؛ تصدير Excel/PDF/CSV
  بلا رقم وطني وبالأسماء لمن يملك `HrCoreWrite` فقط؛ نشر مجاميع `IRecruitmentRequisitionClosed` إلى Dev.Analytics مع حجب الخلايا
  الصغيرة؛ AC-HCM-32a..d ناجحة على المكدّس (2026-10-29، `acceptance-evidence/AC-HCM-32/`، `reports/plan32-final-report.md`). مؤشرات العدالة مؤجلة (لا قرار GOV-001).

#### G-22 — قرار التعيين والفحص الطبي قبل التعيين خارج قائمة التحقق
- **الدليل:** `HireChecklistDefaults` يحوي 7 بنود، ليس بينها «إصدار قرار التعيين» ولا «الفحص الطبي». بينما `Requariments.md` يذكر «إصدار… أوامر التعيين» و«فحوصات… قبل التعيين».
- **الخطة:** PLAN-29 (بقرار تفعيل قابل للضبط).
- **الحالة:** ✅ عولجت في [PLAN-29](acceptance-plans/PLAN-29-hcm-hire-contract-documents.md) — أُضيف البندان `AppointmentDecisionIssued` و`PreEmploymentMedical` إلى قائمة التحقق للمعاملات الجديدة، ويُصدر قرار التعيين كـ `AdministrativeDecision` من المعالج. الدليل: AC-HCM-29g (إكمال المعاملة دون القرار مرفوض) ناجحة حيّاً (2026-10-22، `acceptance-evidence/AC-HCM-29/`). انحراف عن القرار D-6: بند الفحص الطبي لا يُضاف إلا بالمفتاح العام `HireChecklist:RequireMedicalFitness` (معطّل افتراضياً في `HR.Contracts.Web/appsettings.json`)، لا لكل نظام خدمة.

### 4.5 فجوات التحقق والتوثيق

#### G-23 — ادعاءات القبول غير مدعومة بأدلة
- **الدليل:**
  - `AcHcm22RecruitmentWorkflowsUiTests.cs` فيه اختبار واحد فقط (22b) من خمسة. يكتب `Skip` ثم `return` إن لم توجد بيانات، فينجح دون فحص.
  - `AcHcm21…` فيه اختبار واحد.
  - `acceptance-evidence/` لا يحوي `AC-HCM-21` ولا `AC-HCM-22` ولا `AC-WF-23`.
  - `Hcm001RecruitmentTests` اختبارات انعكاس ووجود ملفات على `SharedKernel.Recruitment.*`، وهي نماذج **غير مستخدمة** في التشغيل لكنها مذكورة كدليل إغلاق في `completion-backlog/09`.
  - E2E (`HireSeparationJourneyE2ETests`) يبدأ من HireWizard، لا من طلب الشاغر.
- **الخطة:** PLAN-33.
- **الحالة:** ✅ عولجت في [PLAN-33](acceptance-plans/PLAN-33-hcm-verification-docs.md) (هذه الخطة؛ التقرير `reports/plan33-final-report.md`):
  - **لا نجاح دون فحص:** `src/Tests/HRManagement.AcceptanceTests/AcceptanceGate.cs` — غياب الشرط المسبق يكتب `Blocked` في `result.json` ويُتخطّى الاختبار، أو يفشل تحت `ACCEPTANCE_REQUIRE_ALL=true` للسيناريوهات الموسومة `[RequiredAcceptance]` (AcHcm21، 22، 26–32، AcWf23). ويمنع اختبار المعمارية `src/Tests/HRManagement.UnitTests/Architecture/AcceptanceSkipPatternArchitectureTests.cs` نمط «Skip ثم return» وما شابهه.
  - **حالات حتمية:** `AcceptanceRecruitmentStatesPatch` (`HR.Core.Infrastructure/Seeding`) مع `testdata/acceptance/recruitment-states.json`.
  - **الأدلة:** على مكدّس القبول (ملف مختبري: `NationalScope:IsLabScale=true` والمحرك `Off`) ومع `ACCEPTANCE_REQUIRE_ALL=true`: AC-HCM-21a..d (4 اختبارات) وAC-HCM-22a..e (5) وAC-WF-23a..f (6) ناجحة في تشغيلين (2026-10-29)، والأدلة في `acceptance-evidence/AC-HCM-21/` و`AC-HCM-22/` و`AC-WF-23/` (`result.json` فيه خريطة `scenarios`).
  - **E2E من طلب الشاغر:** `src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs` (`Category=Journey`) ناجح على ملف القبول (`acceptance-evidence/AC-HCM-33/`)؛ ونسخته الشبيهة بالإنتاج (`Category=JourneyProdLike`، مستبعدة من CI حتى PLAN-25) تفشل في المرحلة الأولى بسبب G-02 (`acceptance-evidence/AC-HCM-33/prodlike/`). وكلتا النسختين مستبعدتان من مهمة E2E في `.github/workflows/ci.yml` (`Category!=JourneyProdLike&FullyQualifiedName!~RecruitmentToHireJourney`)، فلا تُشغَّلان إلا على مكدّس القبول.
  - **الدليل الوهمي أُزيل:** حُذفت نماذج `SharedKernel.Recruitment` (لم يكن لها مستهلك في التشغيل)، وصارت `Hcm001RecruitmentTests` اختبارات سلوك على قواعد `HR.Core.Domain`، ويفحص `scripts/hcm001-validate.ps1` الملفات الفعلية.

#### G-24 — انحراف التوثيق
- **الدليل:**
  - `HIRE_ONBOARDING_GUIDE.md` يناقض نفسه: §2 يقول إن زر التعيين يظهر عند `OfferAccepted` فقط، بينما §4 أ و§5 يقولان «مراجعة → مختصر → تعيين».
  - قسم Domain Model في `HCM_001_RECRUITMENT.md` يصف نماذج SharedKernel لا الكيانات الفعلية.
  - `HIRE_RBAC_MATRIX.md` لا يذكر `compliance` ولا أدوار اللجنة.
- **الخطة:** PLAN-33.
- **الحالة:** ✅ تُعالج في [PLAN-33](acceptance-plans/PLAN-33-hcm-verification-docs.md) المرحلة P7 (تصحيح التوثيق ليطابق الشيفرة، ولكل حالة دليل)، ومنها هذا التحديث للدراسة. الوثائق المصحَّحة ونتيجتها في `reports/plan33-final-report.md`.

---

## 5. قرارات مطلوبة من صاحب العمل قبل التنفيذ

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-1 | هل يبدأ مسار الاعتماد دائماً بصرف النظر عن وضع محرك السياسات؟ | **نعم:** المسار إلزامي، والسياسة تضيف رفضاً أو خطوات فقط | PLAN-25 |
| D-2 | هل التعيين في الجهة يتم بمسابقة/اختبار كتابي متعدد الشواغر؟ | إن كان النظام القانوني المعتمد يتطلب ذلك، فـ PLAN-31 ضمن v1، وإلا فيؤجَّل — **قرر المالك التنفيذ (2026-10-29)؛ نُفّذ في PLAN-31** مع إدخال المعاملات القانونية لكل مسابقة من المرجع المعتمد | PLAN-31 |
| D-3 | هل يجوز تجاوز ترتيب الجدارة في العرض؟ | لا، إلا بمبرر مكتوب ومسار اعتماد إضافي | PLAN-27 |
| D-4 | قناة الخدمة الذاتية للمرشح (قبول العرض/الاستئناف) | `/careers` برقم مرجعي + رقم وطني + رمز تحقق لمرة واحدة عبر البريد/SMS | PLAN-30 |
| D-5 | هل لجنة الاختيار إلزامية قبل الترتيب؟ | قابلة للضبط لكل جهة (`Recruitment:RequireCommitteeApproval`)، والافتراضي `true` في الإنتاج | PLAN-28 |
| D-6 | هل قرار التعيين والفحص الطبي بندان إلزاميان في المعالج؟ | قرار التعيين إلزامي، والفحص الطبي قابل للضبط حسب نظام الخدمة | PLAN-29 |
| D-7 | مدة صلاحية قائمة الاحتياط | مدة قابلة للضبط (مثلاً 6 أشهر) أو حتى إغلاق الطلب — **نُفّذ في PLAN-27:** `Recruitment:ReserveValidityMonths` (افتراضي 6) | PLAN-27/31 |
| D-8 | مزوّد البريد والرسائل النصية للمرشحين | SMTP مؤسسي + بوابة SMS حكومية خلف `INotificationService`، مع محوّل وهمي للمختبر | PLAN-30 |

---

## 6. خارطة التنفيذ

```text
الموجة 0 (2–3 أيام)   PLAN-25 §0: إيداع git + CI أخضر + خط أساس اختبارات
الموجة 1 (P0)         PLAN-25 (ربط الاعتماد + الصلاحيات)  ║  PLAN-26 (المعايير + المقابلات + سلامة الطلب)
الموجة 2 (P1)         PLAN-27 (العرض + الجدارة + الاحتياط) ║  PLAN-28 (اللجنة + الاستئناف) ║ PLAN-29 (التعيين + العقد + الوثائق)
الموجة 3              PLAN-30 (تجربة المرشح والقناة الموحدة)
الموجة 4 (P2، بقرار)  PLAN-31 (المسابقات والاختبارات) ║ PLAN-32 (التقارير)
مستمر                 PLAN-33 (القبول الحي + E2E + تصحيح التوثيق) — يُغلق مع كل خطة
```

| الخطة | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|---------|----------|-----------|-------------------|
| [PLAN-25](acceptance-plans/PLAN-25-hcm-production-workflow-binding.md) | G-01, G-02, G-04, G-05, G-14, G-15 | P0 | — | 7–9 |
| [PLAN-26](acceptance-plans/PLAN-26-hcm-assessment-interview-requisition.md) | G-03, G-08, G-11, جزء G-20 | P0/P1 | PLAN-25 §0 | 8–10 |
| [PLAN-27](acceptance-plans/PLAN-27-hcm-offer-merit-reserve.md) | G-06, G-07, جزء G-20 | P1 | PLAN-25، PLAN-26 | 5–7 |
| [PLAN-28](acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md) | G-09, G-10, (G-05) | P1 | PLAN-25، PLAN-26 | 8–10 |
| [PLAN-29](acceptance-plans/PLAN-29-hcm-hire-contract-documents.md) | G-12, G-13, G-22, جزء G-18 | P1 | PLAN-27 | 7–9 |
| [PLAN-30](acceptance-plans/PLAN-30-hcm-candidate-experience.md) | G-16, G-17, G-18, جزء G-20 | P1/P2 | PLAN-27، PLAN-28 | 10–12 |
| [PLAN-31](acceptance-plans/PLAN-31-hcm-competitions-exams.md) | G-19 | P2 (قرار D-2) | PLAN-26، PLAN-27 | 12–15 |
| [PLAN-32](acceptance-plans/PLAN-32-hcm-recruitment-analytics.md) | G-21 | P2 | PLAN-27 | 4–5 |
| [PLAN-33](acceptance-plans/PLAN-33-hcm-verification-docs.md) | G-23, G-24 | P0→مستمر | كل الخطط | 6–8 |
| **المجموع** | | | | **≈ 67–85** |

**ملاحظة على التقدير:** الأرقام تقريبية لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق لكل خطة. أكبر مصادر عدم اليقين: نتيجة البناء الأولى (G-01)، وقرار D-2.

---

## 7. معيار «اكتمال مسار التوظيف» المقترح

يُعدّ المسار مكتملاً لإصدار v1 عندما تتحقق البنود الأربعة الآتية. الحالة كما في 2026-10-29:

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-01…G-18 عدا ما يؤجَّل بقرار موثّق).
  **غير متحقق.** G-02 وG-04 وG-14 وG-15 مفتوحة وتنتظر PLAN-25 (Proposed، مؤجلة)، وG-01 مغلقة جزئياً (لا دفع ولا CI بعيد)، وG-05 مغلقة جزئياً (بقية الصلاحيات في PLAN-25). بقية فجوات P0/P1 مغلقة بخططها (انظر سطر «الحالة» لكل فجوة في §4).
- [ ] **2. ينجح سيناريو E2E واحد على الأقل بإعدادات Production** (`IsLabScale=false`): طلب شاغر ← اعتماد خطوتين ← نشر ← تقديم مواطن مع مرفق ← فرز ← مقابلة مكتملة ← تقييم بمعايير مُنشأة من الواجهة ← ترتيب ← عرض ← قبول المرشح ذاتياً ← تعيين ← عقد بشروط العرض ← معاملة Appointment.
  **غير متحقق.** الرحلة الشبيهة بالإنتاج موجودة (`src/Tests/HRManagement.E2E/RecruitmentToHireJourneyE2ETests.cs`، `Category=JourneyProdLike`، `docker-compose.acceptance-prodlike.yml`) وتتوقف عند أول اعتماد: الطلب بعد التقديم `PendingApproval` بلا نسخة مسار (G-02؛ `acceptance-evidence/AC-HCM-33/prodlike/`). الرحلة نفسها تنجح كاملة على ملف القبول المختبري (`Category=Journey`؛ `acceptance-evidence/AC-HCM-33/`)، من طلب الشاغر حتى معاملة `Appointment` وعقد بدرجة العرض وراتبه.
- [ ] **3. توجد أدلة `acceptance-evidence/AC-HCM-*` لكل سيناريو موجب وسالب في PLAN-21/22/25…30، ولا يوجد اختبار قبول ينجح دون فحص.**
  **متحقق جزئياً.** أعاد تشغيل PLAN-33 الحي (P6، بيئة قبول نظيفة، `ACCEPTANCE_REQUIRE_ALL=true`، 2026-10-29) سيناريوهات AC-HCM-21 و22 و26..32 وAC-WF-23: نجح 45 من 46، ومنها AC-HCM-30c واختبار `Ac27_f_g` اللذان تعذّرا سابقاً لغياب File.Storage (تعمل الآن بمزوّد Local)، ونجحت رحلة AC-HCM-33 من طلب الشاغر حتى معاملة التعيين. يبقى AC-HCM-26h **محجوباً (Blocked)**: لا توجد شخصية مركزية ثانية بدور `hr-core` لاعتماد خطوة تغيير الملاك (فصل المهام)، وتغطيه اختبارات التكامل `Hcm026EstablishmentCascadeIntegrationTests`. كل مجلد يحوي العناصر الثمانية، والنتيجة لكل سيناريو في `reports/plan33-final-report.md`. سيناريوهات PLAN-25 غير موجودة بعد (الخطة مؤجلة). ولا يمكن لاختبار قبول أن ينجح دون فحص: `AcceptanceGate` يكتب `Blocked` أو يُفشل الاختبار تحت `ACCEPTANCE_REQUIRE_ALL=true`، واختبار المعمارية `AcceptanceSkipPatternArchitectureTests` يمنع الأنماط الصامتة.
- [x] **4. تُصحَّح `HCM_001_RECRUITMENT.md` و`HIRE_ONBOARDING_GUIDE.md` و`HIRE_RBAC_MATRIX.md` و`completion-backlog/09` لتعكس الواقع.**
  **متحقق** في PLAN-33 المرحلة P7 (انظر G-24 و`reports/plan33-final-report.md`).

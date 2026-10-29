# مصفوفة صلاحيات معاملة «موظف جديد»

**مرجع:** DFD Level 2 + `usecase-reviewed-table-functional.xlsx` + [usecases.md](./usecases.md)  
**دليل التشغيل والاختبار:** [HIRE_ONBOARDING_GUIDE.md](./HIRE_ONBOARDING_GUIDE.md)  
**مصدر الحقيقة في الشيفرة:** `src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs` (السياسات)، `HrManagementRoles.cs` (الأدوار وأسماء السياسات)، `GatewayPathAuthorizationMiddleware.cs` (فحص البوابة حسب المسار والطريقة)، `HrModuleAuthorizationFilter.cs` (سياسة الوحدة على كل متحكم والمتحكمات المستثناة منها)، وسمات `[Authorize]` في المتحكمات. آخر مطابقة مع الشيفرة: PLAN-25.

| خطوة DFD | النشاط | دور Keycloak | سياسة ASP.NET |
|----------|--------|--------------|---------------|
| P0.x | طلب الشاغر (إنشاء، تعديل، تقديم، نشر، تمديد، إغلاق، إلغاء)، طلبات التوظيف (الفرز، المقابلات، الترتيب، التعيين)، إنشاء خطاب العرض وتقديمه | `hr-core` | `HrCore.Write`؛ القراءة `HrCore.ReadOnly`. في البوابة: GET ← `HrCore.ReadOnly`، وPOST ← `HrCore.Write` |
| P0.x | اعتماد طلب الشاغر / خطاب العرض: الخطوة 1 «مراجعة HR» / الخطوة 2 «اعتماد إداري» (عرض تجاوز الجدارة: خطوة 3 «اعتماد استثناء الجدارة») | دور الخطوة: `hr-core` / `hr-management-approver` / `hr-management-approver` | `HrCore.BranchOperation` + فحوص الخادم على الاعتماد **والرفض**: دور الخطوة (`WorkflowStageGuard`)، ثم بوابة الكيان (`RequisitionApprovalGate` / `OfferApprovalGate`): النطاق التنظيمي، المنشئ لا يعتمد ولا يرفض، ومعتمد مختلف لكل خطوة في عرض التجاوز (عند الاعتماد) — PLAN-25 |
| P0.x | بوابة المواطن `/careers` (الإعلانات، التقديم مع المرفق، تتبّع الطلب، نتائج المسابقات العامة) | *(عام)* | `[AllowAnonymous]` (`PublicCareersController`) — بدون تسجيل HR |
| P0.x | المرشح في «طلبي» `/careers/my`: قبول العرض أو الاعتذار عنه، سحب الطلب، الاستئناف واستيفاء معلوماته، رفع المستندات، بطاقة الامتحان والاعتراض على النتيجة | *(لا دور — المرشح ليس مستخدماً في Keycloak)* | `[AllowAnonymous]` + رمز لمرة واحدة (OTP) يُرسل إلى جهة الاتصال المسجّلة، ثم ملف تعريف الارتباط `careers_session` (مدة مطلقة `Recruitment:Portal:SessionMinutes`، الافتراضي 30 دقيقة)؛ يُقرأ الطلب من الجلسة لا من معرّف في المسار (`CandidatePortalController`) |
| P0.x | لجنة الاختيار — أمين السر (إنشاء الجلسة، الأعضاء وتعيين الرئيس، التعارض، تسجيل الأصوات، التوصيات، التقديم) | `hr-core` (موظف HR الذي يدير الجلسة؛ لا دور «أمين سر» مستقل) | `HrCore.Write` (PLAN-28) |
| P0.x | لجنة الاختيار — الرئيس/العضو | *(لا دور في Keycloak)* | «رئيس» و«عضو» صفتان على سجل العضو (`SelectionCommitteeMember.Role` = `Chair`/`Member`) لموظف يُختار بمنتقي الموظفين، لا دوران. لا يدخل العضو النظام بصفته هذه؛ يسجّل أمين السر صوته. النصاب 3 أعضاء غير منسحبين ورئيس واحد؛ لا يصوّت المنسحب ولا من سُجّل عليه تعارض مع المرشح؛ العضو لا يعتمد الخطوة 3 (`SelectionCommitteeRules`) |
| P0.x | اعتماد اللجنة: الخطوة 1 «تشكيل اللجنة» والخطوة 2 «نصاب اللجنة» / الخطوة 3 «اعتماد مدير HR» | دور الخطوة: `hr-core` / `hr-management-approver` | `HrCore.BranchOperation` + فحوص الخادم (`CommitteeApprovalGate`): دور الخطوة، النطاق التنظيمي، المنشئ، العضو لا يعتمد الخطوة 3 ولا يرفضها، والنصاب للاعتماد فقط (تُرفض اللجنة دون نصاب) — PLAN-25 |
| P0.x | تقييم المرشح (المقيّم) | `hr-core` — لا دور «مقيّم» ولا قائمة مقيّمين معيّنين؛ المستخدم الحالي هو المقيّم | `HrCore.Write` على `RecruitmentApplications/SubmitScores` + قواعد `RecruitmentScoringService`: إقرار عدم التعارض، الطلب في حالة «مقابلة مجدولة» أو «مُقيَّم»، لا يقيّم المتقدم الداخلي طلبه، يُرفض عضو لجنة الطلب المسجّل بتعارض، بنود المقابلة بعد إكمالها بنتيجة «حضر»؛ ينتقل الطلب إلى «مُقيَّم» عند بلوغ `Recruitment:MinAssessors` (الافتراضي 1) |
| P0.x | محضر اللجنة PDF | قراء HR.Core | `HrCore.ReadOnly` |
| P0.x | تقديم الاستئناف نيابة عن المرشح، تعيين المراجع، استيفاء المعلومات | `hr-core` | `HrCore.Write` (PLAN-28) |
| P0.x | قرار الاستئناف / طلب معلومات (المراجع المستقل المعيّن فقط) | `compliance` (+ مطالبة `employee_id`) | `HrCore.AppealReview` على البوابة والمتحكم + دور الخطوة `compliance` في الخادم (PLAN-25؛ `hr-admin` لا يتجاوزه) + فحص الخدمة والبوابة `AppealApprovalGate`: والاستئناف معلّق ومعيّن لمعرّف موظف المستخدم (`RecruitmentAppealWorkflowRules.IsAssignedReviewer`). `hr-core` يجتاز السياسة لكنه لا يبت. يُستبعد من التعيين المتقدم وأعضاء اللجنة ولجنة المقابلة والمقيّمون ومقدّم الاستئناف ومن عيّن المراجع |
| P0.x | المراجع المستقل في HR.Core (حدود الوصول) | `compliance` وحده | يصل إلى `/ui/hr-core/RecruitmentAppeals` و`/ui/hr-core/WorkInbox` فقط (المتحكمان مستثنيان من `Module.HrCore`)، ويرى الاستئنافات المعيّنة لمعرّف موظفه فقط؛ أي مسار آخر في HR.Core يُرفض لأن `compliance` ليس في `HrCore.ReadOnly` ولا `HrCore.Write` (عدا الأصول الثابتة وواجهة البحث المشتركة `/api/lookup` المتاحتين لأي مستخدم مسجّل) |
| P0.x | صندوق عمل HR.Core | قراء HR.Core (`hr-core`، `hr-core-supervisory`، `hr-core-executive`، `branch-manager`، `hr-management-approver`) + `compliance` (استئنافاته المعيّنة فقط) | `HrCore.WorkInbox`؛ يُدرج البند فقط إذا كان ضمن النطاق التنظيمي، ولم ينشئه المستخدم، ويحمل المستخدم دور الخطوة الحالية (AC-WF-23) |
| P0.x | المسابقات (PLAN-31): إنشاء، أسطر، إلحاق طلبات، إعلان، فرز، جلسات الامتحان، التوزيع على القاعات، البطاقات، الحضور، تعيين المصححين، احتساب النتائج وتقديمها ونشرها، الاعتراض نيابةً، مسودات العروض | `hr-core` | `HrCore.Write` |
| P0.x | إنهاء المسابقة (التوزيع) وإلغاؤها | `hr-core` | `HrCore.Approve` في المتحكم (`Finalize`، `Cancel`)، لكن البوابة تفرض `HrCore.Write` على طلبات POST، فلا يصل `hr-management-approver` إليهما رغم أنه في `HrCore.Approve` |
| P0.x | اعتماد المسابقة / اعتماد النتائج: الخطوة 1 «مراجعة HR» أو «لجنة المسابقة» / الخطوة 2 | دور الخطوة: `hr-core` / `hr-management-approver` | `HrCore.BranchOperation` + فحوص الخادم: المنشئ لا يعتمد المسابقة (ومُطلق المسار لا يعتمد الخطوة 2)؛ لا يعتمد النتائج من احتسبها أو قدّمها أو بتّ في تعادلها، ومعتمد مختلف لكل خطوة من خطوات النتائج (`CompetitionApprovalGate` / `ResultsApprovalGate`). دور الخطوة يُفحص في الخادم (PLAN-25)؛ المسابقات للمركزية فقط: مستخدم الفرع يُرفض (403) على كل شاشات المسابقات |
| P0.x | تصحيح الامتحانات (أرقام الامتحان فقط) | `hr-exam-marker` (+ مطالبة `employee_id`؛ يُربط حساب الدخول بالتعيين عند أول زيارة) | `HrCore.ExamMarking` (مسار البوابة `/ui/hr-core/ExamMarking`، والمتحكم مستثنى من `Module.HrCore`)؛ لا يصل المصحح إلى بيانات الطلبات (PLAN-31) |
| P0.x | قرار الاعتراض على النتيجة (المراجع المستقل؛ مصححو المسابقة مستبعدون) | `compliance` | `HrCore.AppealReview` + فحص المراجع المعيّن |
| P0.x | ترشّح ESS (شاغر داخلي، مسابقة، «طلباتي») | `hr-employee` (+ مطالبة `employee_id`) | `Module.Ess` (`hr-admin` أو `hr-employee`؛ لا يحمله `hr-core`). المسار `/ui/hr-core/EssTalent` يخضع في البوابة لقاعدة ESS، والمتحكم مستثنى من `Module.HrCore` |
| P0.x | تقارير التوظيف (PLAN-32): المؤشرات، القمع، الطلبات، الاعتمادات المتأخرة، التصدير Excel/PDF/CSV | قراء HR.Core | `HrCore.ReadOnly` + النطاق التنظيمي للمستخدم. أسماء المرشحين في قوائم التفصيل وتصدير CSV للطلبات لمن يجتاز `HrCore.Write` فقط (`hr-admin`، `hr-core`)، وتصدير Excel/PDF إجماليات بلا أسماء، ولا رقم وطني أبداً؛ تنزيل مهمة التصدير يعيد الفحص (`RecruitmentReportAccess`) |
| P1.1 | استلام مرشح / معاملة تعيين (معالج التعيين) | `hr-contracts`، `hr-core` (+ `hr-management-approver` للعرض) | `HrContracts.HireView` (العرض) / `HrContracts.HireExecute` (التنفيذ). المعالج مستثنى من `Module.HrContracts` |
| P1.1 | اعتماد معاملة التعيين (الخطوة 3 في المعالج) | `hr-management-approver` | `HrManagement.Approve` (`Step3Approve`) |
| P1.2 | تصنيف وعقد ومباشرة | `hr-contracts` | `HrContracts.Approve` (تفعيل عقد، `Step4Complete`) |
| P1.2 | إنشاء العقد المعلّق يدوياً لنوع عقد غير مطابق (PLAN-29) | `hr-contracts`, `hr-core` | `HrContracts.HireExecute` |
| P1.2 | ربط أنواع العقود بأنظمة الخدمة — قراءة / تعديل (PLAN-29) | `hr-contracts`، `hr-management-approver` / `hr-contracts` | `HrContracts.HireView` ضمن `Module.HrContracts` (لا يصل `hr-core` لأن المسار خارج المعالج) / `HrContracts.Approve` |
| D-6 | إصدار قرار التعيين (PLAN-29) | `hr-contracts`, `hr-core` | `HrContracts.HireExecute` |
| — | الفحص الطبي قبل التعيين — تسجيل الدليل (PLAN-29، اختياري) | `hr-contracts`, `hr-core` | `HrContracts.HireExecute` |
| P0.x | خطاب العرض الرسمي PDF (PLAN-29) | قراء HR.Core | `HrCore.ReadOnly` |
| P2.1 | ملف شخصي | `hr-core` | `HrCore.Write` |
| P6.1 | ربط راتب بعد تفعيل العقد | `payroll` (الوحدة) + `payroll-processor` | `Module.Payroll`, `Payroll.Process` |
| P5.1 | تدقيق واعتماد السيرة | `hr-core`, `hr-management-approver` | `HrCore.Approve`, `HrManagement.Approve` |
| P8.1 | إصدار هوية | `idcard` | `Module.IdCard` |
| P7.1 | أرشفة | `records-archive` | `Module.RecordsArchive` |
| ديوان | تسجيل مراسلة | `hr-core` | `HrCore.Write` (يدوي) + حدث `ITransactionRegisteredInDiwan` |

> `hr-admin` يجتاز كل السياسات أعلاه عدا `HrCore.ExamMarking` (المقصورة على `hr-exam-marker`).

## معتمدو مسارات العمل حسب الخطوة

أدوار الخطوات مبذورة في `src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs`:

| المسار | الخطوة 1 | الخطوة 2 | الخطوة 3 |
|--------|----------|----------|----------|
| `RECRUITMENT-REQUISITION-APPROVAL` | «مراجعة HR» — `hr-core` | «اعتماد إداري» — `hr-management-approver` | — |
| `RECRUITMENT-OFFER-APPROVAL` | «مراجعة HR» — `hr-core` | «اعتماد إداري» — `hr-management-approver` | — |
| `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` | «مراجعة HR» — `hr-core` | «اعتماد إداري» — `hr-management-approver` | «اعتماد استثناء الجدارة» — `hr-management-approver` |
| `RECRUITMENT-SELECTION-COMMITTEE` | «تشكيل اللجنة» — `hr-core` | «نصاب اللجنة» — `hr-core` | «اعتماد مدير HR» — `hr-management-approver` |
| `RECRUITMENT-COMPETITION-APPROVAL` | «مراجعة HR» — `hr-core` | «اعتماد إداري» — `hr-management-approver` | — |
| `RECRUITMENT-RESULTS-APPROVAL` | «لجنة المسابقة» — `hr-core` | «اعتماد مدير HR» — `hr-management-approver` | — |
| `RECRUITMENT-APPEAL-REVIEW` | «مراجع مستقل» — `compliance` | — | — |

- **فحص دور الخطوة في الخادم (G-04، مغلقة في PLAN-25):** كل اعتماد **ورفض** يمر بـ `WorkflowApprovalEvaluator` في `WorkflowInstanceActionService`، بالترتيب: سلامة المرحلة (قيد التنفيذ، لا تخطّي، الخطوة موجودة) ← دور الخطوة (`WorkflowStageGuard.ValidateStepRole`: يجب أن يكون `RoleRequired` ضمن **كل** أدوار المستخدم؛ الرسالة «هذه الخطوة تتطلب دور «…».») ← بوابة الكيان (`IWorkflowApprovalGate`) ← قاعدة مُطلق المسار (للاعتماد فقط). اللوحة في صفحات التفاصيل تسأل المقيِّم نفسه، فلا تعرض الواجهة إجراءً يرفضه الخادم ولا العكس (`Hcm025PanelServerParityTests`). صندوق العمل يطبّق قاعدة الدور نفسها.
  - يُفرض الدور على الأنواع في `Workflow:EnforceStepRoleEntityTypes`: طلب الشاغر، العرض، جلسة اللجنة، الاستئناف، المسابقة، نسخة النتائج، طلب تغيير الملاك. بقية النطاقات (العهدة، الفقدان، النقل…) خارج الفرض حالياً (متابعة).
  - `hr-admin` لا يتجاوز دور الخطوة: `Workflow:AdminBypass=false` في كل البيئات.
  - بوابات الكيان: `RequisitionApprovalGate` و`OfferApprovalGate` و`CommitteeApprovalGate` و`AppealApprovalGate` (المراجع المعيّن فقط، والنتيجة الصريحة قبل الاعتماد) و`CompetitionApprovalGate` و`ResultsApprovalGate` و`EstablishmentChangeApprovalGate`. نُقلت إليها قواعد `ValidateSelfApprovalAsync` المحذوفة وفحوص العضوية والنصاب.
  - الاعتماد وحده (الرفض مسموح): النصاب، المعتمد المختلف لكل خطوة (عرض التجاوز والنتائج)، النتيجة الصريحة للاستئناف، وقاعدة مُطلق المسار.
- **بدء مسار الاعتماد (G-02، مغلقة في PLAN-25):** لا تصل المعاملة إلى «بانتظار الاعتماد» (ولا تُقدَّم اللجنة) إلا مع نسخة مسار، أياً كان `IsLabScale` أو وضع محرك السياسات (`ApprovalStartCoordinator`). المعاملات العالقة من إصدارات سابقة: زر «إعادة بدء مسار الاعتماد» (`HrCore.Write`) أو `scripts/hcm025-repair-stuck-approvals.ps1` (`HrCore.Delete`).
- **إجراءات الكتابة الصريحة (G-15، مغلقة في PLAN-25):** الإعلانات العامة والشواغر الداخلية القديمة: `Create`/`Edit`/`ChangeApplicationStatus`/`DeclareCitizenCoi` ← `HrCore.Write`، و`Delete` ← `HrCore.Delete`. اختبار `HrCorePostActionsHaveExplicitPolicyTests` يمنع أي `[HttpPost]` جديد في HR.Core بلا سياسة صريحة (القائمة المجمّدة `tests/architecture/hrcore-post-policy-allowlist.txt` لا تضم أي متحكم توظيف).

## النطاق التنظيمي للفرع

- `branch-manager` (دون `hr-admin`) مقيّد بوحدته والوحدات التابعة لها عبر المطالبة `scope_org_unit_id`؛ وغيابها أو عدم صلاحيتها يعني الرفض (`HrOrgScopeVisibility`، `OrgScopeService`). الأدوار المركزية غير مقيّدة داخل الجهة.
- منذ PLAN-25 (D10) يُفرض النطاق على **شاشات التوظيف** لمستخدم الفرع (`RecruitmentOrgScopeFilter`): طلبات الشاغر، طلبات التوظيف، خطابات العرض، جلسات اللجنة — القوائم تعرض وحداته فقط، وتفاصيل سجل خارج نطاقه 404، وأي POST بمعرّف خارج نطاقه (في المسار أو في حقل النموذج) يُرفض 404 دون تغيير؛ السجل بلا وحدة (طلب توظيف قديم دون طلب شاغر) للمركزية فقط. المسابقات للمركزية فقط (403 لمستخدم الفرع). مطالبة نطاق غير صالحة ← 403.
- ويبقى النطاق مفروضاً في بوابات الاعتماد (`WorkflowGateOrgScope`) وعلى صندوق العمل وقائمة الاستئنافات (لغير المراجع المستقل) وتقارير التوظيف؛ طلب تغيير الملاك واعتماد المسابقة والنتائج للمركزية فقط.
- دور الخطوة 1 في كل مسارات التوظيف والملاك هو `hr-core` (المرجع: `WorkflowPackSeeder`)؛ `branch-manager` وحده لا يعتمد الخطوة 1، ويرفضه الخادم منذ PLAN-25. مستخدمو الفروع في بيانات القبول يحملون `hr-core` + `branch-manager` (`testdata/acceptance/personas.json`، سيناريو AC-WF-23a)؛ الاستثناء الوحيد `acc-negative-invalid-scope`.

## مسارات البوابة

| المسار | الوصف |
|--------|--------|
| `/Home/HireJourney` | دليل مسار `new-hire` |
| `/Home/JourneyDetails/new-hire` | مسار التوظيف الكامل (مرحلتان: HCM-001 + FUN-001) |
| `/careers` | بوابة المواطن — تقديم على إعلانات منشورة (عام) |
| `/careers/my` | «طلبي» للمرشح — دخول برمز لمرة واحدة ثم `careers_session` |
| `/ui/hr-core/RecruitmentRequisitions` | طلبات الشاغر ونشر الإعلان |
| `/ui/hr-core/RecruitmentApplications` | طلبات HR، تقييم، اختيار، تعيين (OfferAccepted) |
| `/ui/hr-core/PublicJobPostings` | PromoteFromCitizen |
| `/ui/hr-core/TalentMarketplace` | PromoteFromInternal (HR — اختياري) |
| `/ui/hr-core/EssTalent` | ترشّح ESS (`Module.Ess` ← `hr-employee`) |
| `/ui/hr-core/OfferLetters` | خطابات العرض |
| `/ui/hr-core/SelectionCommittees` | جلسات لجنة الاختيار والمحضر |
| `/ui/hr-core/RecruitmentAppeals` | استئنافات التوظيف (`HrCore.AppealReview` على البوابة والمتحكم لكل الطرق؛ إجراءات HR تبقى `HrCore.Write` في المتحكم) |
| `/ui/hr-core/WorkInbox` | صندوق العمل (`HrCore.WorkInbox` لطلبات GET) |
| `/ui/hr-core/Competitions` | المسابقات (PLAN-31) |
| `/ui/hr-core/ExamMarking` | شاشات التصحيح المجهّلة (`HrCore.ExamMarking`) |
| `/ui/hr-core/RecruitmentReports` | تقارير التوظيف (PLAN-32، `HrCore.ReadOnly`) |
| `/ui/hr-contracts/HireWizard` | معالج التعيين (قائمة تحقق + إكمال). PLAN-29: `CreatePendingContract`، `IssueAppointmentDecision`، `CompleteMedicalStep` ← `HrContracts.HireExecute` على البوابة؛ `Step3Approve` ← `HrManagement.Approve`؛ `Step4Complete` ← `HrContracts.Approve` |
| `/ui/hr-contracts/ContractTypeRegimeMaps` | ربط أنواع العقود بأنظمة الخدمة (التعديل ← `HrContracts.Approve` على البوابة) |
| `/ui/hr-core/OfferLetters/Document/{id}` | خطاب العرض الرسمي PDF (صادر/مقبول) |

## الأدلة

- التحقق الحي على مكدس القبول (الملف المخبري) مع `ACCEPTANCE_REQUIRE_ALL=true` (PLAN-33، 2026-10-29): `acceptance-evidence/AC-HCM-21`، `AC-HCM-22`، `AC-WF-23` (صندوق العمل حسب دور الخطوة والنطاق والمراجع المستقل).
- رحلة التوظيف الكاملة على ملف القبول: `acceptance-evidence/AC-HCM-33` (نجاح)؛ والنسخة الشبيهة بالإنتاج `acceptance-evidence/AC-HCM-33/prodlike` (PLAN-25).
- PLAN-25: `acceptance-evidence/AC-HCM-25` (25a–h: بدء المسار بإعدادات الإنتاج، رفض الدور في الخادم، النصاب، المراجع المستقل، سياسة كتابة الإعلانات، تقاعد RECRUITMENT-HIRE، سكربت الإصلاح)؛ اختبارات `Hcm025*` و`WorkflowStageGuardRoleTests` و`HrCorePostActionsHaveExplicitPolicyTests`.
- نتائج «Pass» مسجّلة في `result.json` من إعادة التشغيل الحي في PLAN-33 P6 (2026-10-29): `AC-HCM-28` (الاستئناف والمراجع المستقل)، و`AC-HCM-30` (بوابة المرشح)، و`AC-HCM-31` (المصححون)، و`AC-HCM-32` (التقارير والتصدير).
- اختبارات الوحدة: `src/Tests/HRManagement.UnitTests/GatewayPathAuthorizationMiddlewareTests.cs`، `Hcm032ExportTests.cs` (مطابقة قاعدة الأسماء للسياسة).

## أدوار تشغيلية مقترحة في Keycloak

| الدور | يُعادل اليوم |
|-------|-------------|
| `hr-service-ops` | `hr-contracts` + `HrContracts.HireExecute` |
| `hr-contracts-ops` | `hr-contracts` + `HrContracts.Approve` |
| `hr-career-auditor` | `hr-core` + `HrCore.Approve` |
| `hr-management-approver` | `hr-management-approver` + `HrManagement.Approve` |

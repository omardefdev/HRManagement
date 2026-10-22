# PLAN-28 — حوكمة لجنة الاختيار والاستئناف (PLAN-HCM-F)

**Scope:** HCM-001/HCM-006 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-09, G-10 (ومنطق G-05 بعد إصلاح الوصول في PLAN-25)  
**Status:** Implemented (acceptance evidence pending)  
**Priority:** P1  
**Depends on:** PLAN-26 (الدرجات المنظمة)، PLAN-27 (الجدارة والاحتياط). PLAN-25 مؤجلة لعدم توفر خادم  
**تعديلات قبل التنفيذ (2026-10-22):**
- **وصول دور `compliance`:** تُنفَّذ هنا الشريحة الضرورية فقط من PLAN-25 §3 (سياسة `HrCoreAppealReview` + `RecruitmentAppealsController` + صندوق العمل للاستئنافات). هي شيفرة فقط ولا تحتاج خادماً، ومن دونها لا يعمل الاستئناف.
- **فحوص الخادم:** النصاب والمراجع المستقل وفصل الواجبات للّجنة تُفحص في خدمات الكيان (لا في البوابات العامة لـ PLAN-25).
- **هوية المراجع:** يُختار المراجع من **منتقي الموظفين** الموجود (`IndependentReviewerEmployeeId`)، ويُتحقق عند البت من مطابقة مطالبة `employee_id` ودور `compliance`. لا حاجة لواجهة مستخدمين في Auth.Directory.
- **إلزامية اللجنة:** على مستوى طلب الشاغر (`RequiresCommitteeApproval`)، بقيمة ابتدائية من `Recruitment:CommitteeApprovalDefault`.
- **التفاصيل:** في `execution/PLAN-28-AGENT-PROMPT-SESSION-1.md` و`-SESSION-2.md`.  
**Decisions:** D-5 (إلزامية اللجنة)

## Implementation notes / deviations

Execution guide: [PLAN-28-EXECUTION](execution/PLAN-28-EXECUTION.md). Both sessions on branch
`feature/plan-28-committee-appeal-governance`.

**Session 1 — committee (A–E)**

- **Committee switch per requisition (D-5):** `RecruitmentRequisition.RequiresCommitteeApproval`, initial value from
  `Recruitment:CommitteeApprovalDefault` (false; true in Production/Staging), frozen after the first rank — instead of
  one global `Recruitment:RequireCommitteeApproval`.
- **Server-side checks in the entity services**, not in the generic PLAN-25 gates: quorum (≥ 3 non-recused, from step 2),
  the creator never approves (every step, matching the UI), members do not approve step 3.
- **Minutes generated on demand** (`SelectionCommittees/Minutes/{id}`) from the locked data; no `MinutesFileId` and no
  upload requirement at submit — archiving via File.Storage is PLAN-29.
- Duplicate members were already impossible (unique index since the committee tables were created); the service now
  returns a message. `Purpose` defaults to Ranking. Votes are recorded by the HR secretary on the member's behalf.
- `HCM028_CommitteeDecisions`, `HCM028_RankOverride`: new columns/tables only.

**Session 2 — appeal (F–K)**

- **Compliance access slice of PLAN-25** (code only): `HrCoreAppealReview`, `HrCoreWorkInbox`, module-filter exemption of
  `RecruitmentAppeals` *and* `WorkInbox` (the inbox needs it too). Gateway finding: the `ui-hr-core` YARP route has no
  route policy, but the shared `GatewayPathAuthorizationMiddleware` mapped every hr-core GET to `HrCoreReadOnly` and every
  POST to `HrCoreWrite`, so a compliance-only user got 403 (pages and static assets). Fixed in that middleware (appeal
  paths → `HrCoreAppealReview`, inbox GET → `HrCoreWorkInbox`, static assets → `HrCoreLookup`); no gateway config change.
- **Employee-based reviewer identity:** the reviewer is chosen from the existing employee picker
  (`IndependentReviewerEmployeeId`) after filing — not at filing — and matched against the `employee_id` claim plus the
  `compliance` role. No Auth.Directory user listing was needed. `IndependentReviewerId` stays as the legacy user id and still
  works for old rows. The filer's employee is stored (`FiledByEmployeeId`) for the independence rule.
- **Explicit outcomes** `Upheld`, `UpheldRankCorrection`, `Dismissed`, `UpheldNoRemedy` (+ `LegacyReopen` for history). A
  rejected review step means "request information" (`InfoRequest` → `AdditionalInfo`/`AdditionalInfoAt` on resubmit).
  A generic workflow approval without a stored outcome is refused (entity hook in `WorkflowInstanceActionService`).
- **Rank correction outranks the committee:** an upheld rank correction is applied through a dedicated path
  (`AppealRankCorrection` audit, rationale as `RankOverrideReason`), not through the committee gate of `AssignRankAsync`,
  so a candidate the committee rejected can be ranked when the reviewer upholds the appeal. Upheld (re-assessment) releases
  the old rank.
- **Rejection tracking** on every rejection path (`ApplicationRejection.Apply`), with `RankBeforeRejection`; a new
  rejection after an upheld appeal can be appealed again.
- **H5:** with policy Off the review workflow now starts outside lab scale too; when no `RECRUITMENT-APPEAL-REVIEW`
  definition resolves, the assigned reviewer decides directly under the same checks (`AppealDecidedWithoutWorkflow`).
  The seeded policy rule still matches the action name `FileAppeal`, now evaluated at assignment.
- **Legacy reopen removed;** its history is migrated by the insert-only data step of `HCM028_AppealGovernance` instead of a
  separate `hcm028-migrate-legacy-appeal-notes.ps1`.
- Commit plan: the rejection-tracking and appeal-path commits are one commit (they share the migration).
- Persona `a-compliance-2` (`ACCEPTANCE_A_COMPLIANCE_2_PASSWORD`). AC-HCM-22 has no committee/appeal scenario (only 22b),
  so nothing there needed updating.


## Goal

1. تصبح لجنة الاختيار **صاحبة قرار مُدقَّق**: تصوّت على المرشحين وتقترح الترتيب ومحضرها موقّع، واعتمادها شرط للترتيب (قابل للضبط).
2. يصبح للاستئناف **طريق واحد** عبر مراجع مستقل محدَّد الهوية بشكل صحيح، وبقرار صريح وأثر منضبط على المسار.

## الوضع الحالي

| البند | المرجع |
|-------|--------|
| الجلسة مرتبطة بالطلب فقط؛ `Vote` نص غير مستخدم | `SelectionCommitteeSession.cs`، `SelectionCommitteeMember.cs` |
| `ApprovedAt` ليس شرطاً لأي انتقال | `SelectionWorkflowRules.cs` |
| إضافة أعضاء بعد التقديم والاعتماد ممكنة، وبلا منع تكرار، وبـ GUID | `SelectionCommitteeService.AddMemberAsync:31-54` |
| طريق قديم `ReopenAppeal` يتجاوز المراجع | `RecruitmentSelectionService.ReopenAppealAsync:141-172`، `Details.cshtml:257` |
| المراجع GUID نصي، ويُقارن مرة بـ EmployeeId ومرة بـ UserId | `Details.cshtml:202`، `RecruitmentAppealService.cs:47-49`، `RecruitmentAppealWorkflowRules.CanDecideAppeal` |
| اعتماد المسار = قبول الاستئناف دائماً | `RecruitmentAppealWorkflowHandler.cs:22-30` |
| نافذة الاستئناف تُحسب من `ReviewedAt` الذي تكتبه عمليات أخرى | `RecruitmentAppealWorkflowRules.CanAppeal` |

---

## In scope

### 1. لجنة الاختيار كجهة قرار (G-10)

**Migration `HCM028_CommitteeDecisions` — حقول جديدة على `SelectionCommitteeSession`:**

| الحقل | الغرض |
|-------|--------|
| `Purpose` | enum Interview/Ranking/Final |
| `SubmittedAt` | يُضاف إن لم يُضَف في PLAN-25 |
| `LockedAt` | — |
| `MinutesFileId` | File.Storage |
| `ChairEmployeeId` | — |

**كيانات جديدة:**

| الكيان | الحقول |
|--------|--------|
| `SelectionCommitteeDecision` | SessionId, ApplicationId, `Recommendation` (Recommend/Reserve/Reject)، `ProposedRank`، `Rationale` — فريد (Session, Application) |
| `SelectionCommitteeVote` | SessionId, MemberId, ApplicationId, `Vote` (Agree/Disagree/Abstain)، `Comment` — فريد (Member, Application). يحل محل `SelectionCommitteeMember.Vote` النصي |
| `SelectionCommitteeMemberCoi` | MemberId, ApplicationId, `HasConflict`, `DeclaredAt` |

**القواعد** (`SelectionCommitteeRules` في Domain):

- رئيس واحد، والنصاب ≥ 3 غير منسحبين (فحص خادم عبر بوابة PLAN-25).
- تعارض العضو مع مرشح محدد يحجب صوته على هذا المرشح فقط.
- **قفل العضوية** بعد `SubmittedAt`: لا إضافة ولا حذف (رسالة: «تم تقديم الجلسة للاعتماد — العضوية مقفلة»)، وفهرس فريد (SessionId, EmployeeId).
- التوصية تتطلب أغلبية أصوات الأعضاء المؤهلين.
- التقديم للاعتماد يتطلب توصية لكل مرشح `Assessed` على الطلب، ومحضراً مرفوعاً.
- SoD: منشئ الجلسة وأعضاؤها لا يعتمدون الخطوة 3 (مدير HR).

**بوابة على المسار الرئيسي:** مع `Recruitment:RequireCommitteeApproval=true` (D-5؛ الافتراضي `true` في Production و`false` في Development):

- `AssignRankAsync` يتطلب جلسة `Ranking/Final` معتمدة (`ApprovedAt`) للطلب.
- الرتبة يجب أن تساوي `ProposedRank` ما لم يوجد `RankOverrideReason` مسجل في التدقيق.

**الواجهة:**

- `SelectionCommittees/Details`: تبويبات الأعضاء (منتقي موظفين عبر `/api/lookup/employees`)، المرشحين والأصوات (مصفوفة)، التوصيات، المحضر.
- وثيقة محضر PDF عبر QuestPdf (`SelectionCommitteeMinutesDocument`) تُرفق تلقائياً عند التقديم.
- رابط اللجنة من `RecruitmentRequisitions/Details` مع حالة الاعتماد.

### 2. الاستئناف بطريق واحد (G-09)

**إزالة الطريق القديم:**

- حذف بطاقة «استئناف (مسار قديم)» من `Details.cshtml:250-266` و`ReopenAppeal` من المتحكم.
- `ReopenAppealAsync` يبقى فقط لترحيل البيانات (internal)، أو يُحذف بعد تشغيل `scripts/hcm028-migrate-legacy-appeal-notes.ps1` الذي يحوّل `AppealNotes` التاريخية إلى سجلات `RecruitmentAppeal` بحالة `Decided` وقرار «مسار قديم».

**توحيد هوية المراجع (Migration `HCM028_AppealReviewerIdentity`):**

| الحقل | الاستخدام |
|-------|-----------|
| `IndependentReviewerUserId` (Keycloak `sub`) | للبت والبوابة |
| `IndependentReviewerEmployeeId` (يُحل عبر Auth.Directory) | لفحص الاستقلالية عن أعضاء اللجنة ولجنة المقابلة والمقيّمين |
| `AssignedAt`، `AssignedBy` | — |

يُرحَّل `IndependentReviewerId` الحالي إلى `IndependentReviewerUserId`.

**دورة الاستئناف:**

1. **التقديم:** من HR أو من المرشح عبر البوابة (PLAN-30)، بسبب ومرفقات اختيارية، **دون** إلزام تحديد المراجع لحظة التقديم.
2. **التعيين:** دور `compliance` أو HR يعيّن المراجع من منتقٍ يسرد مستخدمي `compliance` (`GET /api/directory/users?role=compliance` عبر Auth.Directory). يُرفض المراجع إن كان عضواً في اللجنة أو المقابلة أو مقيّماً للمرشح.
3. **المسار:** `RECRUITMENT-APPEAL-REVIEW` يبدأ بعد التعيين.
4. **القرار الصريح:** نموذج في `RecruitmentAppeals/Details` بخيارات `Upheld` (إعادة تقييم)، `Dismissed` (رفض)، `UpheldRankCorrection` (تصحيح رتبة مقترح)، ونص تعليل إلزامي.
   - الإرسال يسجّل `Outcome` + `Decision` ثم يستدعي `ApproveAsync`.
   - «رفض الخطوة» في المسار يعني **إعادة للاستيفاء** (طلب معلومات)، لا رفض الاستئناف.
   - Migration: `Outcome` enum + `RejectedAt` على `RecruitmentApplication`.
5. **الأثر:**
   - `Upheld` ← الطلب `UnderReview` بدرجات وتوصيات مُعلّمة «قبل الاستئناف».
   - `UpheldRankCorrection` ← تعديل الرتبة عبر خدمة الترتيب مع سبب.
   - `Dismissed` ← يبقى `Rejected`.

**قواعد إضافية:**

- النافذة `Recruitment:AppealWindowDays` (افتراضي 14) تُحسب من `RejectedAt` لا من `ReviewedAt`.
- `Recruitment:BlockHireOnPendingAppeal=true` يمنع `Hire` على الطلب ما دام فيه استئناف معلق (العرض مسموح، والتعيين موقوف).
- إن كان الطلب قد أُغلق بتعيين، تقتصر النتائج الممكنة على `Dismissed` أو «قبول بلا أثر على الشاغر» مع تسجيل (`UpheldNoRemedy`)، ويُرفع تنبيه إلى الامتثال.

---

## Out of scope

- الصلاحيات والوصول لدور compliance ← PLAN-25
- تقديم المرشح للاستئناف من البوابة ← PLAN-30
- الاعتراض على نتائج المسابقات الجماعية ← PLAN-31

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-28a | جلسة 3 أعضاء + رئيس ← أصوات وتوصيات ← محضر ← تقديم ← اعتماد 3 خطوات | إضافة عضو بعد التقديم ← رفض |
| AC-HCM-28b | — | عضو مكرر ← رفض؛ منشئ الجلسة يعتمد الخطوة 3 ← رفض |
| AC-HCM-28c | `RequireCommitteeApproval=true`: ترتيب بعد اعتماد اللجنة بالرتبة المقترحة | ترتيب قبل الاعتماد ← رفض؛ رتبة مخالفة بلا سبب ← رفض |
| AC-HCM-28d | عضو صاحب تعارض مع مرشح ← صوته محجوب لهذا المرشح فقط | — |
| AC-HCM-28e | رفض مرشح ← تقديم استئناف ← تعيين `acc-a-compliance` ← قرار `Upheld` ← `UnderReview` | تعيين عضو لجنة مراجعاً ← رفض |
| AC-HCM-28f | قرار `Dismissed` ← يبقى `Rejected` مع تعليل | — |
| AC-HCM-28g | — | استئناف بعد انقضاء 14 يوماً من `RejectedAt` ← رفض |
| AC-HCM-28h | — | `Hire` على طلب فيه استئناف معلق ← رفض |
| AC-HCM-28i | — | زر «مسار قديم» غير موجود، وPOST `ReopenAppeal` ← 404 |

## Verification

```powershell
powershell -File scripts/hcm028-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm028|RecruitmentAppealWorkflowRules|SelectionCommittee"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm028"
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcHcm28|AcHcm22"
```

## Definition of done

- لا طريق لإعادة فتح طلب مرفوض إلا عبر استئناف بمراجع مستقل.
- هوية المراجع موحدة (UserId للقرار، EmployeeId للاستقلالية) ومختارة من منتقٍ.
- اللجنة تنتج قراراً لكل مرشح ومحضراً، واعتمادها شرط قابل للضبط للترتيب.
- AC-HCM-28a..i = Pass، وAC-HCM-22c/22d محدّثان وناجحان.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-28-hcm-committee-appeal-governance.md` |
| Domain | `SelectionCommitteeRules.cs`، كيانات Decision/Vote/MemberCoi، `RecruitmentAppealOutcome` enum |
| Infra | تحديث `SelectionCommitteeService`، `SelectionCommitteeLifecycleService`، `RecruitmentAppealService`، `RecruitmentAppealWorkflowHandler`؛ `SelectionCommitteeMinutesDocument` |
| Auth.Directory | `GET /api/directory/users?role=` (سياسة خدمة) إن لم يكن موجوداً |
| Migrations | `HCM028_CommitteeDecisions`، `HCM028_AppealReviewerIdentity` |
| Scripts | `scripts/hcm028-validate.ps1`، `scripts/hcm028-migrate-legacy-appeal-notes.ps1` |
| Tests | `Hcm028CommitteeRulesTests`، `Hcm028AppealLifecycleTests`، `Hcm028CommitteeGateIntegrationTests`، `AcHcm28CommitteeAppealTests` |
| Docs | `HCM_001_RECRUITMENT.md`، `HIRE_RBAC_MATRIX.md` (رئيس/عضو لجنة، مراجع مستقل)، `SOD_MATRIX.md` |

## Effort estimate

8–10 أيام عمل: نموذج اللجنة والأصوات (3)، البوابة على الترتيب (1)، المحضر PDF (1)، الاستئناف الموحد والهوية (2.5)، القبول (1.5).

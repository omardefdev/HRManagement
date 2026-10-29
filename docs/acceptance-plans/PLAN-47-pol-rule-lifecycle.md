# PLAN-47 — دورة حياة قاعدة السياسة: أربع عيون حقيقية، وصيغة مُتحقَّق منها، وإصدار منشور ثابت، وتأريخ وتراجع (POL-C)

**Scope:** POL — [دراسة الفجوات](../POL_001_GAP_STUDY.md): G-POL-06، G-POL-07. ومن G-POL-15 جزآن: مسار النشر الواحد وحدثه، وإبطال الكاش في كل نسخ HR.Core.  
**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-42 (فصل نقطة الإنفاذ، و`IPolicyModeResolver`)، PLAN-40 (نمط النشر الذري للأحداث عبر outbox)  
**Related cards:** PROD-001 ([07](../completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md) §PROD-001)، SYR-001 (`legal-source-binding.md`: «No silent modifications»). وأيضاً PLAN-02 (AC-10 السالبة: «ولا يُعدَّل إصدار منشور»)، وPLAN-25 §11 (متابعة «لا `IsActive`»)، وAC-HCM-25g.

## Goal

أن تصبح السياسة المنشورة **قراراً موثَّقاً لا يتغير بصمت**:

- لا ينشر أحد ما ألّفه أو عدّله هو. لا علم «Lab» في الإنتاج، والناشر من دور اعتماد لا من دور التأليف.
- لا تُحفظ قاعدة بصيغة لا يفهمها المقيّم. لا شرط فارغ يطابق كل شيء، ولا أثر يُبتلع، ولا عامل مجهول يعيد `false` بصمت.
- الإصدار المنشور لا يُعدَّل، لا من الواجهة ولا من البذور ولا من SQL. وكل تغيير، ومنه تغيير البذور وسحب `R-REC-HIRE`، إصدار جديد بسجل نشر.
- المسودة تبدأ نسخة من الإصدار النافذ وتُعدَّل وتُحذف منها القواعد. ويمكن جدولة نفاذها، والتقييم «كما في تاريخ».
- التراجع إلى إصدار سابق إجراء بأربع عيون يُنتج إصداراً جديداً مرتبطاً بالسابق.
- النشر مسار واحد يرسل `IPolicyPublished` ذرّياً، وكل نسخ HR.Core تبطل كاشها.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-POL-3: من يؤلف ومن ينشر | التأليف: سياسة `PolicyAuthor` = `admin` أو `hr-core` أو الدور الجديد `policy-author`. النشر: سياسة `PolicyApprover` = `admin` أو `hr-management-approver` أو الدور الجديد `policy-approver`. والمستخدم نفسه لا ينشر ما حرّره أبداً. يمكن تغيير الأدوار بالمفتاحين `PolicyEngine:Roles:Author`/`Approver` دون شيفرة | §2 |
| D-POL-10: النفاذ بأثر رجعي | ممنوع: `EffectiveFrom ≥ now − 5 min`. ويفتحه مفتاح `PolicyEngine:Publish:AllowRetroactive=true` | §6 |
| D-POL-11: تغييرات البذور النظامية | تُنشر بلا أربع عيون، بنوع نشر `SystemSeed` وبمعرّف مجموعة التغيير وبصمتها، وتظهر في سجل الإصدارات. ومفتاح `PolicyEngine:Seeds:RequireApproval=true` يجعلها مسودات تنتظر معتمداً | §4 |
| D-POL-12: مسودة واحدة لكل قالب | نعم، فهرس فريد مشروط. لا مسودات متوازية | §5 |

## المرحلة 0 — خط الأساس (نصف يوم)

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-47-pol-rule-lifecycle` | — |
| 0.2 | البناء والترحيلات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` | أخضر |
| 0.3 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "PolicyEngine\|PolicyFactsValidator\|PolicyOrchestrator\|Hcm025RecruitmentHireRetired\|Hcm027\|Prod001\|S2s042"` + `IntegrationTests --filter "Hcm025ProductionApproval\|RecruitmentRequisitionPolicy"` | أخضر أو قائمة في `reports/plan47-baseline.md` |
| 0.4 | بصمة قبل التغيير | سكربت `scripts/pol047-policy-fingerprint.ps1` (psql على قاعدة القبول): SHA-256 لكل `(VersionId, RuleKey, WhenJson, ThenJson, Priority)` في الإصدارات المنشورة | ملف `reports/plan47-fingerprint-before.json` |

---

## In scope

### 1. مدقق القاعدة ورفض الصيغ الصامتة (G-POL-06)

**المشكلة:**
- `AddRule` يحفظ النص كما هو (`PolicyTemplatesController.cs:151-169`).
- الشرط بصيغة غير متوقعة يتحول إلى مجموعة فارغة (`PolicyEntityMapper.cs:43-57`)، والمجموعة الفارغة تطابق كل شيء (`PolicyConditionModels.cs:41`).
- `ThenJson` الخاطئ يُبتلع إلى `[]` (`PolicyEntityMapper.cs:75-77`).
- العامل المجهول يعيد `false` (`WorkflowCondition.cs:34`). والمقارنة الرقمية بثقافة الجهاز (`WorkflowCondition.cs:28-30`)، وثقافة جهاز المالك ar-SA.
- حارس `ValidateInvalidPublish` غير مستدعى (`ProdPolicyGuard.cs:116`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicyRuleValidator` | `src/Services/HRCore/HR.Core.Domain/Services/Policy/PolicyRuleValidator.cs` | يعيد `IReadOnlyList<PolicyRuleIssue(Code, MessageAr, JsonPath)>` لقاعدة واحدة، ولإصدار كامل (تكرار المفاتيح) |
| `PolicyRuleJson` (تحليل صارم) | `src/Shared/HRManagement.SharedKernel/Policy/PolicyRuleJson.cs` | `ParseWhenStrict`/`ParseThenStrict` عبر `JsonDocument` بلا تسامح: الخاصية المجهولة خطأ، والمصفوفة الفارغة خطأ |
| `IPolicyDomainCapabilities` | `src/Services/HRCore/HR.Core.Domain/Services/Policy/IPolicyDomainCapabilities.cs` | نقطة امتداد: الآثار وحقول `SetField` ورموز المسار المسموحة لكل مجال. التنفيذ الافتراضي يسمح بكل التعداد. PLAN-49 يملؤها من الكتالوج |
| `WorkflowCondition` | `src/Shared/HRManagement.SharedKernel/Workflow/WorkflowCondition.cs:24-35` | `decimal.TryParse(..., NumberStyles.Number, CultureInfo.InvariantCulture, ...)` وإضافة `LessThanOrEqual` |
| الخريطة وقت التشغيل | `PolicyEntityMapper.cs:43-79` | الصيغة غير الصالحة لا تصبح «تطابق الكل». تصبح `PolicyConditionGroup.Never` (لا تطابق)، وتُضاف القاعدة إلى `PolicyVersionSnapshot.InvalidRuleKeys`، ويكتب المنسّق خطأ مرة لكل تحميل لقطة |

**القواعد:**

1. **`When`:**
   - كائن بخاصية واحدة فقط من `all`/`any`/`not`، أو ورقة `{field, operator, value}`.
   - العمق ≤ 6، والأوراق ≤ 50.
   - الفارغ (`{}` أو `{"all":[]}`) مرفوض.
2. **العوامل:**
   - المسموح: `Equals`، `NotEquals`، `GreaterThan`، `LessThan`، `GreaterThanOrEqual`، `LessThanOrEqual`، `Contains`، `In`.
   - العوامل الرقمية تشترط حقلاً من نوع `int`/`decimal` في مخطط المجال، وقيمة تُحلَّل بالثقافة الثابتة (Invariant).
3. **الحقل:** يجب أن يكون في مخطط حقائق المجال (`PolicyFactsSchemas`). والحقل `bool` قيمته `true`/`false`.
4. **`Then`:**
   - مصفوفة غير فارغة، و`effect` اسم من التعداد (لا أرقام).
   - المعاملات الإلزامية:
     - `Deny` ← `reason` (1–500 حرف).
     - `TriggerWorkflow` ← `workflowCode`: تعريف منشور، ومربوط بالمجال في `PolicyWorkflowBindings`، ومسموح في `IPolicyDomainCapabilities`.
     - `SetField` ← `field` مسموح + `value`.
     - `SelectCodePolicy` ← `policyKey` موجود في `CodePolicyTemplates`.
   - المعامل المجهول خطأ.
5. **المفتاح والاسم والأولوية:**
   - `RuleKey` يطابق `^[A-Z][A-Z0-9:-]{2,99}$`، وفريد داخل الإصدار.
   - `Name` ≤ 200، و`Priority` بين 1 و1000.
6. **مواضع التحقق:** يجري عند `AddRule`/`UpdateRule` (واجهة وAPI)، ثم على الإصدار كله عند النشر. ويُستدعى `IPolicyPlatformClaimGuard.ValidateInvalidPublish(isValid, published: true)` في النشر. الرسائل عربية وتذكر المسار داخل JSON.

### 2. أربع عيون حقيقية وأدوار التأليف والنشر (G-POL-06)

**المشكلة:**
- **علم التجاوز بلا فحص بيئة:** `ForceSameActorForLab` حقل في API (`PoliciesController.cs:114`، `:160-164`) ومربع في الواجهة (`EditRules.cshtml:54-56`، `PolicyTemplatesController.cs:186-192`).
- **«المحرّر» منشئ المسودة فقط:** `makerId = version.CreatedBy`، و`PolicyRule` بلا كاتب (`PolicyEntities.cs:56-74`).
- **الأدوار تتداخل:** التأليف (`HrCoreWrite`) والنشر (`HrCoreApprove`) يشملان `hr-core` كلاهما (`HrManagementAuthorizationExtensions.cs:73-86`).
- **الحارس غير موصول:** `ValidateFourEyesPublish` لا يُستدعى (`ProdPolicyGuard.cs:99`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicyVersionEdit` | `src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs` + ترحيل `POL047_PolicyRuleLifecycle` | `(VersionId, UserId, Action, RuleKey?, OccurredAt)`. كل تعديل على المسودة يُسجَّل: إنشاء، إضافة، تعديل، حذف، تاريخ نفاذ، نسخ |
| `PolicyPublishService` | `src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyPublishService.cs` | المسار الوحيد للنشر (§8): تحقق §1 ← أربع عيون ← حارسا المصفوفة ← `PolicyVersionPublisher` (وهو ينشر الحدث قبل حفظه، منذ PLAN-40) |
| سياستا `PolicyAuthor`/`PolicyApprover` | `src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs`، و`HrManagementRoles.PolicyAuthor="policy-author"` و`PolicyApprover="policy-approver"` | D-POL-3 |
| حذف التجاوز | `PoliciesController.cs:160-164`، `PolicyTemplatesController.cs:174-192`، `EditRules.cshtml:54-56` | لا حقل ولا مربع. والاستثناء الوحيد `PolicyEngine:Publish:AllowSameActorInDevelopment=true` مع `IsDevelopment()` |
| قاعدة البوابة | `src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs:180-191` | `…/PolicyTemplates/PublishFromUi` و`…/api/policies/templates/{id}/publish` ← `PolicyApprover`، بدل `HrCoreWrite` الذي لا يملكه المعتمد |
| Keycloak | `docker/keycloak/realm-manifest.json` (PLAN-39: مصدر الأدوار، ويقرؤه `sync-keycloak-dev-realm.ps1` و`render-realm.ps1`) | إنشاء الدورين بلا إسناد افتراضي |

**القواعد:**

1. الناشر مستخدم مصادَق بمعرّف صالح، وليس ضمن محرري الإصدار (`PolicyVersionEdits`)، ويحقق سياسة `PolicyApprover`.
2. المؤلف يحقق `PolicyAuthor`. ومن يملك الدورين معاً لا ينشر مسودة عدّل فيها شيئاً.
3. يُستدعى `ValidateFourEyesPublish("policy-author", "policy-approver")`، ويُسجَّل الدوران الفعليان في `PolicyPublication`.

### 3. ثبات الإصدار المنشور (G-POL-07)

**المشكلة:**
- لا شيء في الشيفرة أو القاعدة يمنع تعديل صفوف إصدار منشور.
- البذور تفعل ذلك فعلاً (`PolicyEngineDomainSeeds.cs:275-289`، `:297-329`؛ `PolicyEngineSeeder.cs:30-54`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PublishedPolicyImmutabilityInterceptor` | `src/Services/HRCore/HR.Core.Infrastructure/Data/Interceptors/PublishedPolicyImmutabilityInterceptor.cs`، ويُسجَّل مع `AuditSaveChangesInterceptor` (`HR.Core.Web/Program.cs:113-125`) | يرفض: إضافة `PolicyRule` إلى إصدار غير مسودة أو تعديلها أو حذفها؛ تعديل إصدار `Published` في غير `EffectiveTo`/`UpdatedAt`/`UpdatedBy`؛ حذف إصدار غير مسودة. يرمي `PolicyImmutabilityException` برسالة عربية |
| Trigger في PostgreSQL | ترحيل `POL047_PolicyRuleLifecycle` (`migrationBuilder.Sql` محروس بـ`ActiveProvider == Npgsql`) | `policy_rules_guard()` على `INSERT/UPDATE/DELETE` في `PolicyRules`: يرفض إن لم تكن حالة الإصدار `Draft`. و`policy_versions_guard()` يرفض تغيير أي عمود في إصدار `Published` عدا `EffectiveTo`/`UpdatedAt`/`UpdatedBy`. هذا يغطي `ExecuteUpdate` وSQL الخام اللذين يتجاوزان المعترِض |
| تزامن النشر | `HRCoreDbContext.cs:890-897` | `ConfigureOptimisticConcurrency<PolicyTemplate>()` (xmin)، ويلمس `PolicyVersionPublisher` القالب. نشران متزامنان لقالب واحد ← الثاني يتلقى 409 «نُشر إصدار آخر للتو؛ أعد المحاولة» |

**القواعد:**

1. لا مسار في الشيفرة يكتب في إصدار منشور، ومنها البذور (§4) والاختبارات المساعدة.
2. لا تُعدَّل الصفوف التاريخية التي أعاد `DeactivateRuleIfPresentAsync` كتابتها قبل هذه الخطة. يسردها سكربت `scripts/pol047-history-report.ps1` (الأسماء بادئتها «[معطّلة]») في التقرير النهائي لملاحظة التدقيق.

### 4. تغييرات البذور عبر إصدارات جديدة (بديل `AddRuleIfMissingAsync` و`DeactivateRuleIfPresentAsync`) (G-POL-07)

**المشكلة:**
- `SeedIncrementalRulesAsync` يضيف القواعد إلى أحدث إصدار منشور (`PolicyEngineDomainSeeds.cs:172-269`، `:297-329`).
- `DeactivateRuleIfPresentAsync` يعيد كتابة `R-REC-HIRE` في كل الإصدارات (`:267`، `:275-289`).
- `SeedSelectCodePolicyRuleIfMissingAsync` مثلهما (`PolicyEngineSeeder.cs:30-54`).
- هذا التفاف مسجّل في [plan25-final-report.md §11](../../reports/plan25-final-report.md)، ويعتمد عليه AC-HCM-25g (`AcHcm25ProductionApprovalTests.cs:309-330`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicySeedChangeSet` | `src/Services/HRCore/HR.Core.Infrastructure/Data/PolicySeedChangeSets.cs` | سجل ثابت: `Id` (مثل `POL-2026-10-30-BASELINE`)، `Domain`، `Operations`. العمليات: `AddRule`، `ReplaceRule`، `RetireRule(key, reasonAr)`، `EnsureFactsField` |
| `PolicySeedChangeSetApplier` | `src/Services/HRCore/HR.Core.Infrastructure/Data/PolicySeedChangeSetApplier.cs`، يستدعيه `PolicyEngineSeeder.SeedAsync` (`:12-28`) بدل `SeedIncrementalRulesAsync` (`:27`) | لكل مجموعة لم تُطبَّق على القالب: نسخة من الإصدار النافذ + العمليات، ثم `PolicyRuleValidator`، ثم `PolicyVersionPublisher` بـ`PublicationKind=SystemSeed` و`EvidenceRef=Id`. وإن كانت كل العمليات بلا أثر على الإصدار النافذ تُسجَّل «NoOp» بلا إصدار |
| `PolicySeedApplication` | كيان + جدول في الترحيل نفسه | `(ChangeSetId, TemplateId, ResultVersionId?, Outcome, Digest, AppliedAt)`، فريد على `(ChangeSetId, TemplateId)` |
| مجموعات التغيير الأولى | `PolicySeedChangeSets.cs` | `POL-2026-10-30-BASELINE`: قواعد `SeedIncrementalRulesAsync` الحالية و`R-PRIV-CODE` وحقول المخطط كـ`AddRule`/`EnsureFactsField`. لا أثر لها على القواعد القائمة، وتنشئ v2 على قاعدة جديدة. و`POL-2026-10-30-RETIRE-R-REC-HIRE`: `RetireRule("R-REC-HIRE")` ← إصدار جديد بلا القاعدة، حيثما وُجدت في الإصدار النافذ |
| حذف الطرق الكاتبة | `PolicyEngineDomainSeeds.cs:172-329`، `PolicyEngineSeeder.cs:30-54` | تبقى `SeedAllDomainsAsync` (`:13-169`) لإنشاء v1 على قاعدة فارغة فقط |
| تحديث المستهلكين | `Hcm027OfferTermsTests.cs:237`، `Hcm027OverrideWorkflowTests.cs:129`، `Hcm025ProductionApprovalIntegrationTests.cs:236`، `Hcm025RecruitmentHireRetiredTests`، و`AcHcm25ProductionApprovalTests.cs:309-330` (25g) | تستدعي `PolicySeedChangeSetApplier`. و25g يتحقق أن `R-REC-HIRE` غائبة عن الإصدار النافذ لـ`Core.Recruitment`، لا أنها «معطّلة» |

**القواعد:**

1. لا تُعدَّل مجموعة تغيير مُطبَّقة أبداً، وأي تعديل يكون مجموعة جديدة بمعرّف جديد. تُخزَّن البصمة (SHA-256 للعمليات)، واختلاف البصمة لمعرّف مُطبَّق يُفشل الإقلاع.
2. مجموعات التغيير تمر بالمدقق نفسه، ومجموعة لا تجتاز التحقق تُفشل الإقلاع برسالة.
3. مع `PolicyEngine:Seeds:RequireApproval=true` تنشئ المجموعة مسودة (المحرر = `system`) ولا تنشرها.

### 5. المسودة: نسخ وتعديل وحذف (G-POL-07)

**المشكلة:**
- `CreateDraft` يبدأ إصداراً فارغاً (`PolicyTemplatesController.cs:100-132`).
- لا تعديل ولا حذف لقاعدة، ونشر مسودة فارغة يُسقط المجال إلى البديل الثابت (`PolicyOrchestrator.cs:92-95`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicyDraftService` | `src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyDraftService.cs` | `CreateDraftAsync(templateId, copyFromVersionId?)` ينسخ قواعد الإصدار النافذ افتراضياً. و`UpdateRuleAsync`، و`RemoveRuleAsync`، و`DiscardDraftAsync` (الحالة `Archived`). كلها تسجّل `PolicyVersionEdit` |
| فهرس المسودة الواحدة | الترحيل | فهرس فريد مشروط على `PolicyTemplateVersions(PolicyTemplateId)` حيث `Status='Draft'` |
| المتحكم والواجهة | `PolicyTemplatesController.cs:100-197`، `Views/PolicyTemplates/EditRules.cshtml`، `Details.cshtml` | تحرير القاعدة وحذفها في المكان، ورسائل المدقق، وقائمة المحررين. زر النشر يظهر للمعتمد غير المحرر فقط. وفي سجل الإصدارات: النوع والناشر والمراجع والدليل و«تراجع عن» |
| رفض الإصدار الفارغ | `PolicyPublishService` | لا يُنشر إصدار بلا قواعد إلا بإجراء صريح «سحب كل قواعد المجال» بسبب مكتوب |

### 6. تاريخ النفاذ والتقييم «كما في تاريخ» (G-POL-07)

**المشكلة:**
- لا إدخال لـ`EffectiveFrom`، والنشر يضبطه على الآن (`PolicyVersionPublisher.cs:32`).
- التقييم دائماً بالوقت الحالي (`PolicyOrchestrator.cs:91`).
- مفتاح الكاش باليوم ومدته 5 دقائق (`PolicyPublishedSnapshotCache.cs:15-16`، `:21`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `SetEffectiveFrom` | `PolicyDraftService` + الواجهة | تاريخ ووقت UTC يُعرض بتوقيت المستخدم. يُرفض ما هو قبل `now − 5 min` (D-POL-10) |
| الناشر | `PolicyVersionPublisher.cs:22-61` | منطق الإغلاق الحالي (`:42-46`) يبقي السابق نافذاً حتى `EffectiveFrom` المستقبلي. يُضاف `PublicationKind` (`Manual`/`SystemSeed`/`Rollback`) |
| `PolicyEnforceRequest.AsOfUtc` | `src/Shared/HRManagement.SharedKernel/Policy/PolicyDecisionModels.cs` | يحترمه `EvaluateAsync` و`EnforceAsync` داخل العملية، و`POST api/policies/evaluate`، والمحاكاة. والنقطة الداخلية (PLAN-42) تتجاهله. والدفتر يسجل `EvaluatedAsOf` حين يختلف عن الآن |
| انتهاء الكاش عند الحدود | `PolicyOrchestrator.cs:100-129` | مدة صلاحية الإدخال = الأقل من 5 دقائق ومن أقرب `EffectiveFrom` مستقبلي للقالب |

### 7. التراجع (G-POL-07)

**المشكلة:**
- `RolledBackFromVersionId` لا يضبطه أي سطر (`PolicyEntities.cs:102`).
- لا إجراء تراجع، وحارس `ValidateRollback` غير مستدعى (`ProdPolicyGuard.cs:143`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `CreateRollbackDraftAsync(templateId, targetVersionId, reasonAr)` | `PolicyDraftService` | مسودة نسخة من الهدف تحمل `RollbackOfVersionId` والسبب (إلزامي) |
| النشر | `PolicyPublishService` | النشر بأربع عيون كالمعتاد، و`PolicyPublication.RolledBackFromVersionId = target`، و`PublicationKind=Rollback`، و`ValidateRollback(true)` |
| الواجهة | `Details.cshtml` | زر «إنشاء مسودة تراجع إلى هذا الإصدار» للمؤلف |

**القواعد:**

1. التراجع لا يعيد نشر الإصدار القديم نفسه، بل ينشئ إصداراً جديداً برقم جديد.
2. المدقق يعيد فحص قواعد الهدف، كتعريف مسار سُحب منذ ذلك الحين، فيمنع النشر ويشرح السبب.

### 8. مسار نشر واحد وحدثه، وإبطال الكاش في كل نسخ HR.Core (G-POL-07، جزء G-POL-15)

**المشكلة:**
- `PoliciesController.Publish` ينشر `IPolicyPublished` بعد `SaveChanges` في bus outbox (`PoliciesController.cs:101-139`، `:117-127`).
- `PublishFromUi` لا ينشره أصلاً (`PolicyTemplatesController.cs:171-197`).
- الإبطال داخل العملية فقط (`PolicyVersionPublisher.cs:59`)، وHR.Core بنسختين في `production-ha`.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicyPublishService` | §2 | المتحكمان يستدعيانه فقط. الحدث يبقى حيث وضعه PLAN-40: داخل `PolicyVersionPublisher.PublishDraftAsync` قبل `SaveChanges` في المعاملة نفسها، فيغطي النشر اليدوي والتراجع ومجموعات البذور (`SystemSeed`) معاً. `PolicyPublishService` لا ينشره ثانية. وتُوسَّع الحمولة هناك: `Domain` و`VersionId` و`TenantId` و`VersionNumber` و`EffectiveFrom` |
| `PolicyPublishedCacheInvalidationConsumer` | `src/Services/HRCore/HR.Core.Infrastructure/Consumers/PolicyPublishedCacheInvalidationConsumer.cs`، ويُسجَّل في `HR.Core.Web/Program.cs` بنقطة مؤقتة لكل نسخة (`.Endpoint(e => { e.InstanceId = Environment.MachineName; e.Temporary = true; })`) | كل نسخة من HR.Core تبطل كاش المجال. الإبطال idempotent بطبيعته، فتُستثنى هذه النقطة من فلتر الإيصالات (`InboxConsumeFilter`) ومن outbox المستهلك (PLAN-40)؛ وإلا تتخطاه النسخة الثانية لأن مفتاح الإيصال `(MessageId, ConsumerType, TenantId)` مشترك في قاعدة HR.Core. ويثبت ذلك `Pol047PublishOutboxPostgresTests` بناقلين لنسختين |

---

## Out of scope

| البند | الخطة |
|-------|-------|
| فصل نقطة الإنفاذ، وحل الوضع الموحد، وعميل الإنفاذ المغلق عند الفشل | PLAN-42 |
| نمط النشر الذري للأحداث في كل الخدمات | PLAN-40 |
| القوالب الوطنية لكل الجهات، وسقف الجهة، ومنع نطاق `National` داخل الجهة | PLAN-48 |
| كتالوج المجالات وقدراتها (تنفيذ `IPolicyDomainCapabilities`)، وسحب القواعد الميتة واليتيمة، ومواءمة الحقائق | PLAN-49 |
| تعديل مخطط الحقائق من الواجهة، والمحرر المنظّم، وكشف التعارض، ومحاكاة الأثر الدفعية (G-POL-13/14) | الموجة 2 |
| `legal_source_id` على القاعدة (G-POL-12) | الموجة 2 |
| إعادة كتابة AC-09/AC-10 على خدمات المجال | PLAN-65. وهنا يُعدَّل AC-10 بالحد الأدنى: ينشر بشخصيتين بدل العلم المحذوف |

## Acceptance scenarios

ملف القبول: الملف الشبيه بالإنتاج. القالب المستعمل قالب عهدة الجهة A (`TenantOverride`)، الذي تبذره `AcceptanceDataSeeder` بإصدار v2 منشور. ولا تحتاج السيناريوهات إلى رؤية القوالب الوطنية (PLAN-48). الشخصيات: `central-a` (مؤلف، `hr-core`) و`a-management-approver` (معتمد). كل سيناريو في `AcPol47RuleLifecycleTests` بـ`[RequiredAcceptance]` و`AcceptanceGate` و`EvidenceRecorder("AC-POL-47…")`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-POL-47a | `central-a` ينشئ مسودة (القواعد منسوخة = قواعد v2)، ويضيف `ACC-P47-CAT` (`category Equals 'أصول ثابتة'` ← `RequireEvidence`). ثم ينشرها `a-management-approver`، فيصبح الإصدار الجديد نافذاً. `POST api/policies/evaluate` يطابق القاعدة الجديدة، والنشر مسجّل بـ maker=`central-a` وchecker=المعتمد | `central-a` ينشر ← 403. `a-management-approver` يضيف قاعدة ← 403. جسم النشر يحوي `forceSameActorForLab=true` ← يُتجاهل، وتبقى المسودة غير منشورة إن كان الناشر محرراً |
| AC-POL-47b | قاعدة `value LessThanOrEqual 10000.5` تُقبل، وتطابق `value=10000.5` ولا تطابق `10001` | `AddRule` بكل من: `{"when":{…}}`، والعامل `LessThanOrEqualz`، والحقل `employeeName`، و`TriggerWorkflow` برمز `NOPE`، و`Then=[]` ← 400 برسالة عربية تذكر المسار، ولا صف جديد (psql) |
| AC-POL-47c | مسودة بـ`EffectiveFrom = now + 10 min` تُنشر. `evaluate` بـ`asOf=now` يعيد معرّف الإصدار السابق، وبـ`asOf=now+11min` يعيد الجديد | `EffectiveFrom = now − 1 day` ← رفض «النفاذ بأثر رجعي غير مسموح» |
| AC-POL-47d | تراجع إلى v2 بسبب مكتوب ← إصدار جديد بصمة قواعده = بصمة v2، و`RolledBackFromVersionId = v2`، والنشر بشخصين | تراجع بلا سبب ← 400 |
| AC-POL-47e | إعادة تشغيل `hr-core-web` مرتين ← بصمة كل الإصدارات المنشورة لم تتغير عن `reports/plan47-fingerprint-before.json` باستثناء الإصدارات الجديدة. و`R-REC-HIRE` غائبة عن الإصدار النافذ لـ`Core.Recruitment` وموجودة في سجل أقدم | `AddRule` إلى إصدار منشور عبر API ← 409. و`UPDATE "PolicyRules" SET "WhenJson"='{}'` على قاعدة منشورة عبر psql ← خطأ trigger ولا تغيير |
| AC-POL-47f | `PolicySeedApplications` يحوي المجموعتين بنتيجتهما (`Published`/`NoOp`)، وعدد الإصدارات ثابت بعد إعادة التشغيل الثانية | تعديل بصمة مجموعة مُطبَّقة في صورة اختبار ← فشل الإقلاع برسالة (سكربت `scripts/pol047-changeset-tamper-check.ps1`) |
| AC-POL-47g | بعد النشر صف `IPolicyPublished` في جدول outbox لـ HR.Core (psql) بنوع الرسالة ومعرّف الإصدار | — |

## Verification

```powershell
git switch -c feature/plan-47-pol-rule-lifecycle
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations add POL047_PolicyRuleLifecycle -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web

# وحدة: المدقق تحت ar-SA وen-US، وأربع العيون، والمسودة، والتأريخ، والتراجع، ومجموعات التغيير، والمعترِض (SQLite)
dotnet test src/Tests/HRManagement.UnitTests --filter "Pol047|PolicyEngine|PolicyFactsValidator|PolicyOrchestrator|Prod001|Hcm025RecruitmentHireRetired|Hcm027"
# تكامل على PostgreSQL (Testcontainers): trigger الثبات، وتزامن النشر (xmin)، والحدث في outbox
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Pol047|Hcm025ProductionApproval|RecruitmentRequisitionPolicy"

# قبول على الملف الشبيه بالإنتاج
pwsh scripts/pol047-policy-fingerprint.ps1 -Out reports/plan47-fingerprint-before.json
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcPol47|AcHcm25|Ac10Enforce"
pwsh scripts/pol047-validate.ps1   # لا AddRuleIfMissing/DeactivateRuleIfPresent، ولا forceSameActorForLab، والترجمة العربية للرسائل
```

اختبارات الوحدة `Pol047*`:
- `Pol047RuleValidatorTests`: يغطي كل قاعدة في §1، ويُشغَّل تحت `CultureInfo("ar-SA")` و`en-US`.
- `Pol047FourEyesTests`.
- `Pol047DraftServiceTests`: النسخ والتعديل والحذف والمسودة الواحدة.
- `Pol047EffectiveDatingTests`: يشمل حدود الكاش.
- `Pol047RollbackTests`.
- `Pol047SeedChangeSetApplierTests`: عدم التكرار، وNoOp، والسحب، وتلاعب البصمة.
- `Pol047ImmutabilityInterceptorTests`.
- `Pol047MapperNeverMatchTests`: الصيغة التالفة لا تطابق.

اختبارات التكامل `Pol047*`:
- `Pol047PolicyImmutabilityPostgresTests`: الـtrigger يرفض UPDATE/DELETE/INSERT، وقبول `EffectiveTo`.
- `Pol047ConcurrentPublishPostgresTests`: نشران متزامنان ← واحد ينجح والآخر 409.
- `Pol047PublishOutboxPostgresTests`.

## Definition of done

- لا يُنشر إصدار حرّره الناشر، ولا علم تجاوز في الإنتاج، والنشر لدور الاعتماد فقط.
- لا تُحفظ قاعدة لا يجتازها المدقق. والصيغة التالفة المخزنة قبل الخطة لا تطابق شيئاً، ويُبلَّغ عنها.
- لا تتغير صفوف إصدار منشور، يثبت ذلك المعترِض والـtrigger والبصمة قبل/بعد. ولا يوجد في الشيفرة `AddRuleIfMissingAsync` ولا `DeactivateRuleIfPresentAsync`.
- المسودة تُنسخ وتُعدَّل وتُحذف منها القواعد. والنفاذ المستقبلي والتقييم `asOf` يعملان، والتراجع ينشئ إصداراً جديداً مرتبطاً.
- النشر مسار واحد يرسل الحدث ذرّياً، وكل نسخ HR.Core تبطل كاشها.
- AC-POL-47a..g = Pass في `acceptance-evidence/AC-POL-47/`، وAC-HCM-25g المعدّل ناجح، وانحدار المرحلة 0 أخضر.
- أسطر «الحالة» محدّثة لـ G-POL-06 وG-POL-07 في `POL_001_GAP_STUDY.md`.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-47-pol-rule-lifecycle.md` |
| Migration | `POL047_PolicyRuleLifecycle`: `PolicyVersionEdits`، `PolicySeedApplications`، `PublicationKind`، `EvaluatedAsOf`، فهرس المسودة الواحدة، xmin للقالب، trigger الثبات |
| Domain/Shared | `PolicyRuleValidator.cs`، `IPolicyDomainCapabilities.cs`، `PolicyRuleJson.cs`، تعديل `WorkflowCondition.cs` و`PolicyEntityMapper.cs` و`PolicyConditionModels.cs` |
| Infrastructure | `PolicyPublishService.cs`، `PolicyDraftService.cs`، `PublishedPolicyImmutabilityInterceptor.cs`، `PolicySeedChangeSets.cs`، `PolicySeedChangeSetApplier.cs`، `PolicyPublishedCacheInvalidationConsumer.cs` |
| Web | تعديل `PoliciesController.cs` و`PolicyTemplatesController.cs` و`EditRules.cshtml` و`Details.cshtml` |
| Scripts | `scripts/pol047-policy-fingerprint.ps1`، `scripts/pol047-history-report.ps1`، `scripts/pol047-changeset-tamper-check.ps1`، `scripts/pol047-validate.ps1`، تعديل `scripts/sync-keycloak-dev-realm.ps1` |
| Tests | الوحدة والتكامل أعلاه، و`AcPol47RuleLifecycleTests.cs`، وتعديل `AcHcm25ProductionApprovalTests.cs` (25g) و`Ac10EnforcePolicyTests.cs` واختبارات `Hcm025`/`Hcm027` المذكورة |
| Docs | تحديث `docs/PROD_001_FOUR_EYES_PUBLISH.md` و`PROD_001_ROLLBACK.md` و`PROD_001_EFFECTIVE_DATING.md` و`PROD_001_VERSIONING.md` و`ADR-PROD-001a-live-policy-engine.md` (قسم «دورة الحياة»)، وأسطر «الحالة» في `POL_001_GAP_STUDY.md` |
| Report | `reports/plan47-baseline.md`، `reports/plan47-final-report.md` (يشمل تقرير الصفوف التاريخية المعاد كتابتها) |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| الـtrigger يكسر ترحيلاً أو بذوراً قديمة تكتب في إصدار منشور | §4 يحذف كل مسار كتابة قبل إضافة الـtrigger. ويُشغَّل اختبار التكامل على قاعدة مُرحَّلة بالكامل مع البذور الوطنية |
| قواعد منشورة اليوم لا تجتاز المدقق الجديد (مفتاح أو عامل) | المدقق يعمل على الجديد فقط. والقديم يُفحص بتقرير `pol047-validate.ps1`، وما يفشل يُصلَح بمجموعة تغيير جديدة لا بتعديل في المكان |
| اختلاف `AcceptanceDataSeeder` (ينشئ v3 مسودة) مع فهرس المسودة الواحدة | الفهرس يسمح بمسودة واحدة، والبذور تنشئ واحدة. ويُضبط سيناريو 47a ليستعمل v3 القائمة أو يتجاهلها (`DiscardDraft`) قبل البدء |
| النقطة المؤقتة لكل نسخة تنشئ طوابير يتيمة في RabbitMQ | `Temporary=true` يحذفها عند الانفصال. وتُختبر على مكدّس القبول بإيقاف `hr-core-web` وإعادة تشغيله |
| ثقافة ar-SA على جهاز المالك تغيّر الفاصل العشري | التحليل بالثقافة الثابتة في المدقق والمقيّم، واختبار صريح تحت ar-SA |

## Effort estimate

8–10 أيام عمل: المرحلة 0 (0.5)، المدقق والمقيّم (1.5)، أربع العيون والأدوار (1)، الثبات (المعترِض والـtrigger والتزامن) (1)، مجموعات التغيير وتحديث المستهلكين (1.5)، المسودة والواجهة (1)، التأريخ (0.5)، التراجع (0.5)، مسار النشر والحدث والإبطال (0.5)، القبول والتوثيق (1.5).

# دراسة محرك السياسات وربط القواعد القانونية (POL-001) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** محرك السياسات في HR.Core (`PolicyOrchestrator`، القوالب والإصدارات والقواعد والشروط والآثار، دفتر الإنفاذ، `PolicyStaticFallback`، البذور `PolicyEngineSeeder`/`PolicyEngineDomainSeeds`، متحكما `Policies` و`PolicyTemplates`)؛ عميل الإنفاذ المشترك (`PolicyEnforceClient`، `PolicyDomainFactsBuilders`)؛ حل الوضع Off/Shadow/Enforce لكل مجال وخدمة؛ استعمال نتائج Shadow؛ دورة حياة القاعدة؛ ربط القيم القانونية بأنظمة الخدمة (SYR-001/SYR-003: `LegalRegime`، `service-regimes.json`، `legal-source-binding.md`) ووصولها إلى الإجازات والفصل والتأديب والرواتب (آلية الربط وتغطيتها فقط؛ قواعد حساب الرواتب لدراسة PAY).  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والإعدادات (`appsettings.*`، `docker-compose.production.yml`، `deploy/k8s`، Keycloak realm) والوثائق عند `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات ولا الحاويات (لا يوجد .NET SDK ولا Docker). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [ADR-PROD-001-policy-platform.md](ADR-PROD-001-policy-platform.md) · [ADR-PROD-001-production-policy-platform-binding.md](ADR-PROD-001-production-policy-platform-binding.md) · [ADR-PROD-001a-live-policy-engine.md](ADR-PROD-001a-live-policy-engine.md) · [ADR-PROD-001b-unified-workflow-binding.md](ADR-PROD-001b-unified-workflow-binding.md) · [PLAN-02](acceptance-plans/PLAN-02-policy-shadow-enforce.md) · [PROD-001 في 07](completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md) · [prod-policy/policy-platform-matrix.json](prod-policy/policy-platform-matrix.json) · [POLICY_ENGINE_UAT.md](POLICY_ENGINE_UAT.md) · [POLICY_DOMAIN_REGISTRY.md](POLICY_DOMAIN_REGISTRY.md) · [syr/legal-source-binding.md](syr/legal-source-binding.md) · [12-SYRIA](completion-backlog/12-SYRIA-BASELINE-AND-SOURCES.md) · [13-SYRIA](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md) · [14-SYRIA](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md) · [HCM_001_GAP_STUDY.md](HCM_001_GAP_STUDY.md) (G-14) · [plan25-final-report.md §11](../reports/plan25-final-report.md)

---

## 1. الخلاصة

1. **المحرك مبني لكنه لا يحكم أي قرار في الإنتاج.** النواة صغيرة ونظيفة: مقيّم بشروط AND/OR/NOT، تعداد آثار مغلق، إصدارات منشورة، دفتر إنفاذ، واجهة `/PolicyTemplates`. لكن:
   - **15 من 20 مجالاً** مسجّلاً لا توجد لها أي نقطة استدعاء في الشيفرة.
   - المجالات الخمسة المستدعاة: أربعة منها `Off` في Production/Staging، والخامس (`Core.Recruitment`) في `Shadow` الذي لا يغيّر النتيجة بطبيعته.
   - 5 أنواع آثار من 11 (`RequireApproval`، `RequireDualApproval`، `RequirePrivacyMask`، `BlockUntilChecklist`، `RequireOriginReceipt`) لا يستهلكها أي سطر.
   - القواعد الفعلية محلية: 31 ملف `*Rules.cs` وثوابت في الشيفرة وملف JSON لأنظمة الخدمة.
   
   مع ذلك تقول بطاقة PROD-001 «Done»، وتقول المصفوفة `live-policy-engine: Satisfied`.
2. **عائق P0 واحد (يحتاج تحقق).** قواعد أنظمة الخدمة للفصل والترفيع والنقل تُقرأ من ملف وثائق بالبحث في المجلدات الأب. ولا يشحن الملف إلا مشروع HR.Core. لذلك يُتوقع أن تعيد حاويتا HR.Contracts وHR.Movements قواعد فارغة، فيُرفض **كل** نشر فصل وكل ترفيع وكل طلب نقل في الإنتاج (G-POL-01).
3. **ثغرات سلامة وحوكمة (P1).** اتباع خطة الطرح الموثقة (Shadow ثم Enforce لكل مجال) يكسر النظام أو يمرّر كل شيء بصمت:
   - العميل البعيد يحوّل رفض HR.Core (HTTP 422) إلى سماح (G-POL-03).
   - حقائق الإجازات والبطاقات والسلف ترفضها مخططات المجال نفسها (G-POL-04).
   - السياسات الوطنية غير مرئية لأي جهة غير الجهة الافتراضية (G-POL-05).
   - فصل المهام في النشر يُتجاوز بمربع «Lab» في واجهة الإنتاج، والقواعد بلا تحقق من الصحة (G-POL-06).
   - الإصدارات المنشورة تُعدَّل في مكانها، ولا تأريخ نفاذ مستقبلي، ولا تراجع (G-POL-07).
   - نقطة `enforce` العامة تثق بالوضع والفاعل والكيان الآتي من المستدعي (G-POL-08).
   - السياسة في Enforce تستطيع اختيار مسار اعتماد أضعف (G-POL-09).
   - الدفتر لا يربط القرار بالمعاملة، وفروق Shadow ضجيج دائم (G-POL-10).
   - قاعدة `TriggerWorkflow` يتيمة حية (`R-ID-PROCESS`) وخمس قواعد ميتة (G-POL-11).
   - القيم القانونية للإجازات والسلف والفصل والتأديب خارج المحرك وبلا `legal_source_id`. مثال: فصل موظف القانون 50 تأديبياً أو طبياً أو قضائياً لا يُسجَّل بنوعه بل بـ«إنهاء» عام، وقاعدة «الحد الوطني 30 يوماً» سترفض إجازة الأمومة لو فُعّلت (G-POL-12).
4. **فجوات قدرات (P2):** لا محاكاة على المسودة ولا تحليل أثر لتغيير قانوني. التأليف JSON خام بلا محرر ولا حذف ولا تعطيل ولا دور قانوني. انتشار النشر وحل الوضع متفرقان.
5. **فجوة تحقق:**
   - AC-09 وAC-10 لم تُشغَّلا قط.
   - AC-11 «Pass» يثبت أن مدير فرع أنشأ مسار اعتماد لكيان وهمي عبر API خام، لا أن خدمة مجال تطبّق سياسة.
   - حراس PROD-001 (أربع عيون، تراجع، سقف الجهة، منع النشر غير الصالح) مختبرة لكنها **غير مستدعاة** من المحرك.
   - الوثائق متناقضة: «Satisfied» في المصفوفة، و«Blocked» في البطاقة وفي `PROD_001_VERIFICATION.md`.

**التقدير الإجمالي:** نحو 50–65 يوم عمل لمطوّر واحد، منها 4–5 أيام للمرحلة P0 (POL-A). يتقلص التقدير بنحو 6–10 أيام إذا قرر المالك قصر المحرك على المجالات المستهلكة فعلاً (D-POL-1).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | تأليف القاعدة | `CreateDraft` يُنشئ إصداراً فارغاً بلا نسخ القواعد السابقة. `AddRule` يحفظ `WhenJson`/`ThenJson` نصاً خاماً بلا تحقق. لا تعديل ولا حذف ولا تعطيل للقاعدة | ⚠️ G-POL-06، G-POL-14 |
| 2 | المحاكاة | واجهة `Simulate` تقيّم **اللقطة المنشورة** لا المسودة المختارة. API المحاكاة تقيّم الإصدار وحده بلا دمج الجهة | ⚠️ G-POL-13 |
| 3 | النشر بأربع عيون | المقارنة بين منشئ المسودة والناشر فقط. يتجاوزها `ForceSameActorForLab` في الإنتاج. أي `hr-core` يؤلف وينشر | ❌ G-POL-06 |
| 4 | الإصدار والتأريخ | الإغلاق التلقائي لـ`EffectiveTo` السابق ✅. `EffectiveFrom` يساوي وقت النشر دائماً. البذور تعدّل الإصدارات المنشورة. لا تراجع | ❌ G-POL-07 |
| 5 | انتشار النشر | إبطال الكاش داخل العملية فقط. `IPolicyPublished` يُنشر بعد `SaveChanges` في bus outbox | ⚠️ G-POL-15 |
| 6 | حل الوضع | 5 نسخ من منطق الحل. Production: `Core.Recruitment=Shadow` وكل ما عداه `Off` | ⚠️ G-POL-02، G-POL-15 |
| 7 | المقيّم | Deny يغلب، ومخطط الحقائق يُفحص. الشرط الفارغ يطابق كل شيء، والعامل المجهول `false` بصمت | ✅ مع ملاحظات (G-POL-06) |
| 8 | الوطني مقابل الجهة | القوالب الوطنية مفلترة بالمستأجر الافتراضي. الجهة تستطيع تغيير `SetField` و`TriggerWorkflow` الوطنيين | ❌ G-POL-05 |
| 9 | الاستدعاء من الخدمات | 5 من 20 مجالاً مستدعاة. العميل البعيد يفشل مفتوحاً. حقائق 3 مجالات مرفوضة بالمخطط | ❌ G-POL-02، G-POL-03، G-POL-04 |
| 10 | تطبيق الآثار | العهدة تستهلك كل آثارها. 5 آثار بلا مستهلك. `TriggerWorkflow` يتيم للبطاقات. اختيار مسار أضعف ممكن | ⚠️ G-POL-02، G-POL-09، G-POL-11 |
| 11 | الدفتر وShadow | صف لكل استدعاء في غير Off، بلا `EntityId` وبلا الحقائق. فروق Shadow مقارنة ببديل ثابت غير السلوك الفعلي | ⚠️ G-POL-10 |
| 12 | ربط القيم القانونية | ربط المصدر على مستوى الكيان للتوظيف والملاك ✅. الإجازات والسلف والفصل والتأديب: ثوابت أو JSON أو جداول محلية بلا مصدر. حزمة الأنظمة غير مشحونة لخدمتين | ❌ G-POL-01، G-POL-12 |
| 13 | التحقق | AC-09/10 لم تُشغَّلا. AC-11 يثبت API خاماً. حراس PROD-001 غير موصولة | ❌ G-POL-16 |

### 2.1 مصفوفة المجالات (الإنتاج/Staging كما في `f9eab1a`)

النتيجة عند عدم التطابق واحدة لكل المجالات:
- لا تطابق ← سماح ([PolicyEvaluator.cs:30-38](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)).
- لا إصدار منشور أو إصدار بلا قواعد ← `PolicyStaticFallback`، وهو سماح لكل المجالات عدا ثلاثة ([PolicyStaticFallback.cs:15-19](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyStaticFallback.cs)).
- `Off` في الخدمات البعيدة ← «shim» العهدة الثابت لكل المجالات ([PolicyEnforceClient.cs:33-41](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)).

| المجال | الخدمة | نقاط الاستدعاء | الوضع الفعلي | القواعد المبذورة ([PolicyEngineDomainSeeds.cs](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)) | قيم قانونية؟ | الأثر الفعلي |
|---|---|---|---|---|---|---|
| Assets.Custody | Admin.Logistics | `AssetsController.cs:411` (Assign)، `:815` (Create→رمز)، `AssetClassifier.cs:15` (Classify) | Off ← shim بعتبات 10,000/5,000 ثابتة في الشيفرة ([PolicyEnforceClient.cs:146](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)) | 6: R-CLASSIFY-HIGH-VALUE، R-CLASSIFY-CAPITAL، R-PRIV-EVIDENCE، R-HIGH-APPROVAL، R-PRIV-WF، R-PRIV-CODE ([PolicyEngineSeeder.cs:78-136](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs)) | عتبات إدارية، بلا مصدر | المجال الوحيد الذي تُستهلك كل آثاره؛ في الإنتاج تحكمه الشيفرة لا القواعد |
| Assets.Procurement | Admin.Logistics | لا يوجد | Off | 2: R-RCV-FULFIL، R-RCV-DENY (بلا فلتر `action`) `:63-72` | — | ميت |
| IDCard.Lifecycle | IDCard | `CardRequestsController.cs:132` (Process) | Off | 1: R-ID-PROCESS ← `TriggerWorkflow ID-CARD-ISSUANCE` `:15-21` | — | يتيم (G-POL-11). حقيقة `employeeId` مرفوضة (G-POL-04) |
| Contracts.Leave | HR.Contracts | `LeaveRequestHandlers.cs:64` (Create عبر HR فقط) | Off | 3: R-LEAVE-LONG (`RequireApproval` بلا مستهلك) `:25-27`، R-LEAVE-SELF (الحقيقة لا تُرسل) `:174-177`، R-LEAVE-MAX (Deny > 30 «حد وطني») `:179-182` | **عنصر نائب** بلا مصدر | لو فُعّل: رفض شامل بالمخطط أو سماح بالفشل المفتوح |
| Contracts.Advance | HR.Contracts | لا (`ForAdvanceRequest` غير مستعمل) | Off | 1: R-ADV-HIGH (`RequireDualApproval` > 50,000) `:31-37` | عنصر نائب؛ الحد الفعلي 100,000 ثابت في الشيفرة | ميت |
| Contracts.Hire | HR.Contracts | لا | Off | 1: R-HIRE-START ← `HIRE-WIZARD` `:39-45` | — | ميت (يتيم لو رُبط) |
| Contracts.Separation | HR.Contracts | لا | Off | 1: R-SEP-START ← `SEPARATION-WIZARD` `:47-53` | — | ميت. قواعد الفصل الفعلية من JSON (G-POL-01/12) |
| Contracts.Contract | HR.Contracts | لا | Off | 1: R-CON-RENEW (`RequireApproval`) `:55-61` | — | ميت |
| Movements.Transfer | HR.Movements | لا | Off | 1: R-TRF ← `TRANSFER-APPROVAL` `:74-80` | — | ميت. النقل يُعتمد محلياً |
| Movements.Workforce | HR.Movements | لا | Off | 0 `:82-84` | — | فارغ |
| **Core.Recruitment** | HR.Core (داخل العملية) | 7: `RecruitmentRequisitionLifecycleService.cs:64` (تقديم عبر المنسّق)، `:172` (Publish)، `:252` (PublishInternal)، `RecruitmentOfferService.cs:169`، `SelectionCommitteeLifecycleService.cs:67`، `RecruitmentAppealService.cs:423`، `RecruitmentApplicationsController.cs:567` (Hire) | **Shadow** | 10 فعّالة + R-REC-HIRE معطّلة في مكانها `:184-267` | لا (قواعد إجرائية) | لا أثر على النتيجة. صف دفتر بفروق ضجيج (G-POL-10). المسار يبدأ دائماً بالمنسّق |
| Core.Establishment | HR.Core | `EstablishmentChangeLifecycleService.cs:103` | Off (لا مفتاح) | 2: R-EST-SUBMIT، R-EST-DENY-BRANCH `:92-106` | — | تخطٍّ. قاعدة الرفض لا تتطابق أبداً (`hasOrgScope: false` ثابت، `:109`) |
| Core.Employment | HR.Core | لا | Off | 0 | — | فارغ |
| Health.Insurance | HR.Health | لا | Off | 1: R-INS-MASK (`RequirePrivacyMask` بلا مستهلك) `:112-118` | — | ميت |
| Compliance.Disciplinary | Compliance | لا | Off | 1: R-DISC ← `DISCIPLINARY-APPROVAL` `:120-126` | العقوبات `enum` من 4 قيم | ميت |
| Payroll.Period | Payroll | لا | Off | 1: R-PAY-CLOSE (`RequireDualApproval`) `:128-134` | — | ميت (فصل المهام محلي، AC-12) |
| IT.Tickets | IT.Infra | لا | Off | 1: R-TKT ← `IT-TICKET` `:136-142` | — | ميت |
| Archive.Retention | Records.Archive | لا | Off | 2: R-ARC، R-ARC-HOLD `:144-150`، `:208-211` | — | ميت. الحجز القانوني مطبّق محلياً |
| Training.Learning | HR.Training | لا | Off | 1: R-TRN (`BlockUntilChecklist`) `:152-158` | — | ميت |
| Analytics.ETL | Dev.Analytics | لا | Off | 1: R-ETL `:160-166` | — | ميت |

**المجموع:** 37 قاعدة فعّالة. 15 منها في مجالات بلا أي استدعاء. 12 قاعدة بلا شرط `action`، فتطابق كل إجراء في مجالها.

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **نواة تقييم سليمة وقابلة للاختبار:**
  - `PolicyEvaluator` في SharedKernel بشروط متداخلة، وDeny يغلب، وتعداد آثار مغلق بلا شيفرة تنفيذية.
  - اختبارات وحدة في [PolicyEngineTests.cs](../src/Tests/HRManagement.UnitTests/PolicyEngineTests.cs) (12 اختباراً).
- **النشر يغلق الإصدار السابق ويبطل الكاش:** [PolicyVersionPublisher.cs:32-59](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyVersionPublisher.cs)، مثبت في `PolicyOrchestratorPublishTests`. الانتقاء بنافذة النفاذ وأعلى رقم إصدار.
- **PLAN-25 فصل الاعتماد عن المحرك في التوظيف والملاك:**
  - `ApprovalStartCoordinator` يجعل السياسة ترفض أو تبدأ مساراً، ولا تُلغي الاعتماد أبداً. لذلك لا يغيّر Shadow النتيجة.
  - الحالة والنسخة تُحفظان في معاملة واحدة.
  - `R-REC-HIRE` حُيّدت و`RECRUITMENT-HIRE` سُحب.
  - الحارس المعماري [WorkflowDefinitionsHaveHandlersTests.cs](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs).
  - `Hcm025RecruitmentHireRetiredTests` يختبر قواعد التوظيف في وضع Enforce.
- **ربط المصدر القانوني على مستوى الكيان للتوظيف والملاك:** `RecruitmentLegalSourceValidator` و`EstablishmentLegalSourceValidator` يتحققان من سجل التشريع SYR-001.
- **نظام الخدمة لكل موظف محفوظ في قاعدة البيانات:**
  - `LegalRegime` يُبذر من JSON مع حل الأسماء المستعارة في سجل التشريع.
  - الرواتب تُحجب لمن لا نظام له أو له استثناء مفتوح ([ServiceRegimePayrollBlockService.cs](../src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimePayrollBlockService.cs)).
- **عهدة الأصول هي المجال الوحيد المربوط طرفاً لطرف:** دليل، ومسار اعتماد، وتصنيف `SetField`، واختيار قالب الترميز. وفيه حارس محلي لسجل الاستلام.
- **الحواجز المحلية موجودة حيث تكرر البذورُ القاعدة:** الحجز القانوني في الأرشيف (`ArchiveItemsController.cs:132`)، وفصل المهام في الرواتب. أي أن غياب المحرك لا يترك هذه الحالات بلا ضبط.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-POL-01 — حزمة قواعد أنظمة الخدمة لا تُشحن مع HR.Contracts وHR.Movements: كل فصل وترفيع ونقل مرفوض في الحاويات (يحتاج تحقق)
- **الدليل:**
  - تُحمَّل قواعد الفصل والترفيع والحركات والمادة 159 في SharedKernel من ملف واحد. يُبحث عنه في `AppContext.BaseDirectory/seed-packs/...` ثم في المجلدات الأب حتى `docs/syr/service-regimes.json` ([ServiceRegimeJsonPackPaths.cs:8-14](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeJsonPackPaths.cs)).
  - المشروع الوحيد الذي ينسخ الملف إلى مخرجاته هو HR.Core ([HR.Core.Infrastructure.csproj:16](../src/Services/HRCore/HR.Core.Infrastructure/HR.Core.Infrastructure.csproj)). HR.Contracts وHR.Movements لا يشيران إليه، ولا يتضمنه `ServiceDefaults` ولا `SharedKernel`.
  - صورة التشغيل تنسخ `/app/publish` فقط ([DotnetService.Dockerfile:51](../docker/DotnetService.Dockerfile)). لا يوجد `docs/` في الحاوية، ولا تركيب volume في `docker-compose.production.yml` ولا في `deploy/k8s`.
  - عند غياب الملف تعود القواميس فارغة ([ServiceRegimeSeparationRules.cs:94-95](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeSeparationRules.cs)):
    - الفصل ← «لا توجد قواعد فصل مهيأة لهذا النظام» (`:62-69`)، ويُستدعى عند نشر الفصل ([SeparationRecordHandlers.cs:166](../src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs)).
    - النقل ← «نوع الحركة غير معروف» لأن `TryGet("transfer")` يفشل ([ServiceRegimeMovementRules.cs:66-67](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeMovementRules.cs))، ويُستدعى عند إنشاء النقل واعتماده ([TransferRequestsController.cs:94](../src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs) و`:172` و`:222` و`:339`).
    - الترفيع ← «لا توجد قواعد ترفيع مهيأة» ([ServiceRegimePromotionRules.cs:69-70](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimePromotionRules.cs))، عبر `MovementsExtendedControllers.cs:911` و`:975`.
  - تنجح الاختبارات لأن `BaseDirectory` في بيئة الاختبار يقع داخل المستودع، فيجد البحث `docs/syr`. ولا يوجد دليل قبول حيّ لفصل أو نقل أو ترفيع على الحاويات (AC-13..29 لم تُشغَّل، [summary.md](../acceptance-evidence/summary.md)).
  - يزيد الأمرَ سوءاً أن `hr-contracts-web` و`hr-movements-web` و`idcard-web` في الإنتاج بلا `Integration__HrCoreBaseUrl` ([docker-compose.production.yml:279](../docker-compose.production.yml) و`:320` و`:386`؛ لا يوجد إلا `:371` للرواتب و`:442` للوجستيات). عندها يتصل `HrCoreActiveRegimeClient` بـ`http://localhost:5010` ([HrCoreActiveRegimeClient.cs:45](../src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreActiveRegimeClient.cs)) ويرمي استثناءً (→ OPS).
- **الأثر:** في أي جهة حقيقية لا يمكن إنهاء خدمة موظف، ولا ترفيعه، ولا نقله من الواجهة. الرسائل توحي بنقص إعداد قانوني، فيلجأ المستخدمون إلى طرق التفاف.
- **الإصلاح المقترح:**
  - تضمين الحزمة كمورد مضمَّن (`EmbeddedResource`) في SharedKernel مع بصمة SHA-256، أو قراءة القواعد من HR.Core عبر API مخزّن مؤقتاً (يتسق مع POL-F).
  - فحص جاهزية يُفشل الإقلاع إن كانت الحزمة فارغة.
  - إضافة `Integration__HrCoreBaseUrl` لكل خدمة تستهلك HR.Core في compose وk8s.
- **الخطة:** [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md)
- **التحقق المطلوب:**
  - `docker run <hr-contracts-image> ls /app/seed-packs` يثبت الحالة الحالية.
  - موجب: على مكدّس شبيه بالإنتاج ينجح نشر فصل `Resignation` لموظف قانون 50، وإنشاء نقل، وترفيع بمرجع قرار.
  - سالب: حذف الحزمة من الصورة يُفشل `/ready` برسالة صريحة، لا يرفض المعاملات بصمت.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-POL-02 — المحرك لا يحكم أي قرار في الإنتاج: مجالات بلا استدعاء وآثار بلا مستهلك
- **الدليل:**
  - **الاستدعاء:** نقاط `EnforceAsync` الوحيدة في الخدمات البعيدة هي `AssetsController.cs:411` و`:815` و`AssetClassifier.cs:15` و`LeaveRequestHandlers.cs:64` و`CardRequestsController.cs:132`. ثماني خدمات تسجّل العميل ولا تستدعيه: Training وHealth وCompliance وRecordsArchive وMovements وPayroll وIT وDevAnalytics (`Program.cs` لكل منها: `AddHrPolicyEnforceClient`).
  - **الوضع:** المفتاح الوحيد في Production/Staging هو `PolicyEngine:Core.Recruitment=Shadow` ([appsettings.Production.json:232-233](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)). لا مفتاح آخر في أي `appsettings` أو compose أو k8s، فكل مجال آخر `Off` ([PolicyEnforceClient.cs:29](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)، [ApprovalStartCoordinator.cs:73-79](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)).
  - **الآثار:** `RequireApproval` و`RequireDualApproval` و`RequirePrivacyMask` و`BlockUntilChecklist` و`RequireOriginReceipt` لها صفر مستهلك خارج نواة المحرك. 7 قواعد مبذورة تُنتجها: R-LEAVE-LONG، R-CON-RENEW، R-ETL، R-ADV-HIGH، R-PAY-CLOSE، R-INS-MASK، R-TRN.
  - **القواعد الفعلية خارج المحرك:** [policy-rules-inventory.csv](policy-rules-inventory.csv) يعدّ 31 ملف `*Rules.cs` محلياً. حدود السلف ثوابت ([LeaveAndAdvanceRules.cs:9-11](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)). تصنيف الأصول في Off يأتي من الـshim ([PolicyEnforceClient.cs:140-166](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)).
  - **الواجهة تضلّل:** `PolicyDomainRegistry` يعرض «Shadow» لـ15 مجالاً، منها 11 بلا أي استدعاء ([PolicyTemplatesController.cs:218-241](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). ويُسقط `Core.Establishment` من القائمة.
- **الأثر:**
  - مسؤول يغيّر قاعدة «سلفة > 50,000 تتطلب اعتماداً مزدوجاً» أو «إغلاق فترة الرواتب يتطلب اعتماداً مزدوجاً» ويرى القرار في المحاكاة، ولا يتغير شيء في التشغيل.
  - الادعاء بأن «القواعد ليست شروطاً ثابتة في الشيفرة» ([COMMITTEE_LIVE_DEMO_GUIDE_AR.md](COMMITTEE_LIVE_DEMO_GUIDE_AR.md) §10) غير صحيح في الإنتاج.
- **الإصلاح المقترح:**
  - قرار نطاق (D-POL-1).
  - إزالة المجالات والقواعد والآثار التي لا مستهلك لها من البذور والواجهة، أو ربطها فعلياً، مجالاً مجالاً، بنقطة استدعاء ومستهلك أثر.
  - عرض «الوضع الفعّال/نقاط الاستدعاء/آخر قرار» لكل مجال في `/PolicyTemplates` بدل عمود نصي ثابت.
  - رفض نشر قاعدة بأثر لا يستهلكه المجال (سجل قدرات لكل مجال).
- **الخطة:** [PLAN-49](acceptance-plans/PLAN-49-pol-engine-scope-orphans.md)
- **التحقق المطلوب:**
  - موجب: لكل مجال يبقى «فعّالاً» سيناريو يغيّر قاعدة فيتغير السلوك حيّاً.
  - سالب: نشر `RequireDualApproval` في مجال لا يستهلكه يُرفض برسالة.
  - اختبار معماري: كل مجال في السجل له نقطة استدعاء واحدة على الأقل.

#### G-POL-03 — عميل الإنفاذ البعيد يفشل مفتوحاً: الرفض يتحول إلى سماح
- **الدليل:**
  - HR.Core يعيد رفض وضع Enforce بـ`UnprocessableEntity` (422) ([PoliciesController.cs:34-35](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)). العميل يعامل أي رمز غير ناجح كعطل ويذهب إلى `Degraded` ([PolicyEnforceClient.cs:47-50](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs)).
  - `Degraded` يقيّم لقطة مخزنة إن وُجدت، وإلا يعيد `shim.Allowed` الذي يساوي `true` دائماً لغير العهدة (`:85-108`).
  - اللقطة لا تُخزَّن إلا بعد استجابة 200 (`:54-58`)، أو عبر `IPolicyPublished` الذي لا تستهلكه إلا IDCard (وانظر G-POL-15). لذلك بعد كل إعادة تشغيل يصبح كل رفض سماحاً حتى أول نجاح.
  - **مستدعيان من ثلاثة لا يصلان أصلاً إلى HR.Core:**
    - لا عنوان HR.Core في الإنتاج لـHR.Contracts وIDCard (`docker-compose.production.yml:279`، `:386`). عندها `AddHttpClient` بلا `BaseAddress` (`PolicyEnforceClient.cs:191`) ← استثناء ← `Degraded`.
    - هوية الخدمة `svc-hr-contracts` تطلب `file-storage.references` فقط ([HR.Contracts.Web/appsettings.json:47](../src/Services/HRContracts/HR.Contracts.Web/appsettings.json)).
    - العميلان `svc-hr-contracts` و`svc-idcard` بلا `hr-core.workflow` في realm ([realm-hrmanagement-dev.template.json:516-541](../docker/keycloak/realm-hrmanagement-dev.template.json))، بخلاف `svc-admin-logistics` (`:552-563`). وسياسة `HrCoreBranchOperation` تتطلب هذا النطاق للحسابات الخدمية ([HrManagementAuthorizationExtensions.cs:91-97](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). (يحتاج تحقق على realm الإنتاج.)
  - الخطأ في كتابة قيمة الوضع يعطي `Off` في HR.Core (`ApprovalStartCoordinator.cs:78`)، لكنه يعطي **`Enforce`** في الخدمات البعيدة. السبب أن `request.Mode` يبقى على قيمته الافتراضية `Enforce` ([PolicyDecisionModels.cs:36](../src/Shared/HRManagement.SharedKernel/Policy/PolicyDecisionModels.cs)، `PolicyEnforceClient.cs:30-31`).
  - اللقطة المخزنة مفتاحها المجال وحده، بلا مستأجر، وصلاحيتها 24 ساعة ([PolicySnapshotStore.cs:16](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicySnapshotStore.cs) و`:23`). لقطة جهة قد تُطبَّق على جهة أخرى.
- **الأثر:** تفعيل Enforce لأي مجال بعيد لا يمنع شيئاً، ولا يترك أثراً في الدفتر. «المنع» الموعود في PLAN-02 لا يحدث، ولا أحد يعلم.
- **الإصلاح المقترح:**
  - التمييز بين 422 (رفض نهائي يُعاد كما هو) وأعطال النقل.
  - سياسة فشل مُعلنة لكل مجال (D-POL-2)، والافتراضي في Enforce: إغلاق.
  - مفتاح لقطة بالمجال والمستأجر.
  - محلّل وضع واحد مع تحقق عند الإقلاع يرفض القيم المجهولة.
  - ربط النطاق والعنوان لكل مستدعٍ.
  - مقياس `policy_degraded_total`.
- **الخطة:** [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md)
- **التحقق المطلوب:**
  - موجب: Enforce في `Contracts.Leave` مع قاعدة رفض ← الطلب يُرفض برسالة القاعدة، وفي الدفتر صف `Enforce`.
  - سالب: إيقاف HR.Core ← رفض صريح «تعذر التحقق من السياسة» لا سماح.
  - سالب: قيمة وضع خاطئة ← فشل الإقلاع.

#### G-POL-04 — قواعد المجالات البعيدة غير قابلة للتفعيل كما هي: الحقائق مرفوضة والقواعد معطوبة والقنوات تتجاوز المحرك
- **الدليل:**
  - **مخطط الحقائق يرفض أي مفتاح غير مسجل** ([PolicyFactsSchemaModels.cs:29-30](../src/Shared/HRManagement.SharedKernel/Policy/PolicyFactsSchemaModels.cs))، والرفض قرار `Allowed=false` ([PolicyEvaluator.cs:10-22](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)). البناة يرسلون `employeeId` للبطاقة والإجازة والسلفة ([PolicyDomainFactsBuilders.cs:19](../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyDomainFactsBuilders.cs) و`:36` و`:54`). المخططات المبذورة لا تحويه: البطاقة `purpose, action`، والإجازة `durationDays, leaveType, action` (+`isSelfApproval`)، والسلفة `amount, action` ([PolicyEngineDomainSeeds.cs:21](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs) و`:29` و`:37`). النتيجة: كل طلب في Enforce يُرفض بـ«حقل غير مسموح في schema: employeeId».
  - **R-LEAVE-SELF** تعتمد `isSelfApproval` (`:174-177`)، ولا يرسلها أي بانٍ. والاعتماد نفسه لا يستدعي المحرك.
  - **R-EST-DENY-BRANCH** لا تتطابق أبداً، لأن المستدعي يمرر `hasOrgScope: false` ثابتاً ([EstablishmentChangeLifecycleService.cs:109](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs)).
  - **12 قاعدة بلا شرط `action`** تطابق كل إجراء في مجالها: R-PRIV-EVIDENCE، R-HIGH-APPROVAL، R-PRIV-WF، R-LEAVE-LONG، R-LEAVE-SELF، R-LEAVE-MAX، R-ADV-HIGH، R-RCV-FULFIL، R-RCV-DENY، R-INS-MASK، R-ARC، R-ETL. المقيّم يتجاهل معامل `action` ([PolicyEvaluator.cs:5-8](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)).
  - **الإجازة تمر بالمحرك من قناة واحدة:** إنشاء HR فقط. طلب ESS الوارد يُنشأ دون المحرك، ودون حتى `ValidateLeaveRequest` ([InboundEventCommands.cs:252-289](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)). والتعديل يغيّر المدة دون المحرك ([LeaveRequestHandlers.cs:101-126](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs)). والإجازة تُرسل `TenantId = null` (`:65`)، فلا يُحمَّل تجاوز الجهة.
  - لا يوجد اختبار يقارن مفاتيح البناة بالمخططات المبذورة. `ForLeaveRequest`/`ForIDCardRequest`/`ForAdvanceRequest` بلا أي اختبار.
- **الأثر:** إصلاح G-POL-03 وحده يحوّل الفشل المفتوح إلى **رفض شامل** لكل إجازة وبطاقة. ولو صُحّح المخطط، يمكن الالتفاف على قاعدة المدة بالتقديم من ESS أو بتعديل طلب قائم.
- **الإصلاح المقترح:**
  - مواءمة البناة والمخططات واختبار معماري يفرضها.
  - إرسال الحقائق التي تشترطها كل قاعدة (`isSelfApproval`، `hasOrgScope` الحقيقي).
  - تحذير عند النشر لقاعدة بلا شرط `action` في مجال متعدد الإجراءات.
  - استدعاء المحرك في كل قناة تُنشئ أو تعدّل الكيان نفسه.
- **الخطة:** [PLAN-49](acceptance-plans/PLAN-49-pol-engine-scope-orphans.md)
- **التحقق المطلوب:**
  - موجب: Shadow في `Contracts.Leave` لعشر طلبات (HR وESS وتعديل) ← عشرة صفوف دفتر بلا خطأ مخطط.
  - سالب: قاعدة Deny على المدة تمنع الطلب من القنوات الثلاث.

#### G-POL-05 — السياسات الوطنية غير مرئية لأي جهة غير الافتراضية، والجهة تستطيع تجاوز الوطني
- **الدليل:**
  - **الإخفاء:**
    - كيانات السياسة كلها «مملوكة للمستأجر»، فليست في قائمة الكيانات العامة ([TenantEntityClassification.cs:20-33](../src/Shared/HRManagement.ServiceDefaults/TenantEntityClassification.cs)). لذلك تُطبَّق عليها `e.TenantId == EffectiveTenantId` ([TenantQueryFilterExtensions.cs:64-69](../src/Shared/HRManagement.ServiceDefaults/TenantQueryFilterExtensions.cs)).
    - القوالب الوطنية والمخططات مبذورة بـ`TenantDefaults.DefaultTenantId` ([PolicyEngineDomainSeeds.cs:366](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs) و`:408`).
    - `LoadPublishedAsync` لا يستعمل تجاوز `PolicyResolution` ([PolicyOrchestrator.cs:138-144](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs))، بخلاف تعريفات المسار التي تستعمله ([ApprovalStartCoordinator.cs:181-182](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)).
    - النتيجة: لجهة الوزارة/المحافظة لا يوجد قالب وطني، فيُستعمل البديل الثابت (سماح).
  - **الاختبار يُخفي المشكلة:** القبول يبذر قالب `TenantOverride` خاصاً لكل جهة ([AcceptanceDataSeeder.cs:1057-1087](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs)). لذلك تنجح AC-10/11 للجهتين A وB.
  - **«الوطني» داخل الجهة:** `CreateDraft` يبحث عن القالب الوطني داخل مستأجر المستخدم فقط، وإن لم يجده أنشأ قالباً «National» جديداً في الجهة ([PolicyTemplatesController.cs:103-119](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). أي `hr-core` في جهة يؤلف «سياسة وطنية» لجهته.
  - **سقف الجهة غير مطبّق:**
    - الدمج يضم قواعد الجهة ويرتبها بالأولوية ([PolicyEvaluator.cs:91-111](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)).
    - أول `TriggerWorkflow` يفوز ([PolicyDecisionModels.cs:13](../src/Shared/HRManagement.SharedKernel/Policy/PolicyDecisionModels.cs))، فقاعدة جهة بأولوية أصغر تستبدل مسار الاعتماد الوطني.
    - آخر `SetField` يفوز (`:21-25`)، فقاعدة جهة بأولوية أكبر تعيد `isPrivileged=false` وتُلغي إلزام الدليل والاعتماد للأصل.
    - Deny وحده لا يُتجاوز.
  - **مخالفة الادعاء:** المصفوفة تدّعي `cannotBypassNationalMandatory: true` ([policy-platform-matrix.json:29](prod-policy/policy-platform-matrix.json)).
  - **حزم التهيئة:** `AgencyPolicyOverlaySeeder` لا تضيف إلا أول قاعدة لكل مجال، لأن الإضافة داخل `if (template is null)` ([AgencyProvisioningService.cs:234-236](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)).
- **الأثر:** في نشر وطني متعدد الجهات لا تطبّق أي جهة السياسة الوطنية. وما يُطبَّق هو ما تكتبه الجهة لنفسها بلا سقف.
- **الإصلاح المقترح:**
  - تحميل القوالب الوطنية عبر تجاوز مُسمّى للقراءة فقط.
  - منع إنشاء نطاق `National` خارج المستأجر الوطني.
  - دلالات سقف صريحة (D-POL-4): قواعد الجهة تضيف Deny أو آثاراً أشد فقط، ولا تغيّر `SetField`/`TriggerWorkflow`/`SelectCodePolicy` الوطنية.
  - إصلاح حلقة التهيئة.
- **الخطة:** [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md)
- **التحقق المطلوب:**
  - موجب: جهة B بلا تجاوز تتلقى قرار القاعدة الوطنية.
  - سالب: قاعدة جهة `SetField isPrivileged=false` أو `TriggerWorkflow` بأولوية 1 تُرفض عند النشر.
  - سالب: مستخدم جهة لا يستطيع إنشاء قالب `National`.

#### G-POL-06 — النشر بأربع عيون قابل للتجاوز، والقاعدة تُنشر بلا تحقق من صحتها
- **الدليل:**
  - **تجاوز فصل المهام:**
    - `ForceSameActorForLab` حقل في جسم API ([PoliciesController.cs:114](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)، `:163`) ومربع «Lab: نفس المحرّر» في واجهة الإنتاج ([EditRules.cshtml:55](../src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/EditRules.cshtml)). لا فحص للبيئة.
    - AC-10 نفسه ينشر بهذا التجاوز ([Ac10EnforcePolicyTests.cs:31](../src/Tests/HRManagement.AcceptanceTests/Ac10EnforcePolicyTests.cs)).
  - **«المحرّر» منشئ المسودة فقط** (`makerId = version.CreatedBy`، [PolicyTemplatesController.cs:186-188](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). `AddRule` لا يسجل كاتب القاعدة (`PolicyRule : BaseEntity` بلا `CreatedBy`، [PolicyEntities.cs:56](../src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs)). الناشر يستطيع إضافة قواعد إلى مسودة غيره ثم نشرها.
  - **الأدوار:** `HrCoreWrite` (تأليف) و`HrCoreApprove` (نشر) يشملان كلاهما `hr-core` ([HrManagementAuthorizationExtensions.cs:73-86](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). لا دور قانوني، رغم أن المصفوفة تسمّي `legal-control-officer` مراجعاً.
  - **لا تحقق من القاعدة:**
    - `AddRule` يحفظ النص كما هو ([PolicyTemplatesController.cs:154-169](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)).
    - شرط بصيغة غير متوقعة يتحول إلى مجموعة فارغة ([PolicyEntityMapper.cs:43-57](../src/Services/HRCore/HR.Core.Domain/Services/Policy/PolicyEntityMapper.cs))، والمجموعة الفارغة **تطابق كل شيء** ([PolicyConditionModels.cs:41](../src/Shared/HRManagement.SharedKernel/Policy/PolicyConditionModels.cs)).
    - JSON غير صالح يرمي استثناءً عند كل تقييم للمجال.
    - `ThenJson` الخاطئ يُبتلع إلى `[]` (`PolicyEntityMapper.cs:75-77`).
    - العامل المجهول (مثل `LessThanOrEqual`) يعيد `false` بصمت ([WorkflowCondition.cs:34](../src/Shared/HRManagement.SharedKernel/Workflow/WorkflowCondition.cs)).
    - الحقل غير الموجود في المخطط لا يُكتشف عند النشر.
  - **الحارس غير موصول:** `ValidateFourEyesPublish` و`ValidateInvalidPublish` موجودان ([ProdPolicyGuard.cs:99](../src/Shared/HRManagement.ServiceDefaults/ProdPolicy/ProdPolicyGuard.cs)، `:116`)، لكن لا يستدعيهما أي متحكم.
- **الأثر:** مستخدم `hr-core` واحد يستطيع نشر سياسة تحكم وطنياً. وخطأ إملائي واحد في `When` قد يحوّل `Deny` إلى رفض كل إجراءات المجال، أو `TriggerWorkflow` إلى إطلاق اعتماد لكل إجراء.
- **الإصلاح المقترح:**
  - إزالة التجاوز خارج Development.
  - تسجيل كل محرر للمسودة والتحقق من أن الناشر ليس منهم.
  - دوران منفصلان `policy-author`/`policy-approver` (D-POL-3).
  - مدقق نشر: JSON صالح، عوامل وآثار معروفة، حقول ضمن المخطط، `Then` غير فارغ، `When` غير فارغ إلا بتأكيد صريح.
  - ربط `ProdPolicyGuard` بالمسار الحقيقي أو حذفه.
- **الخطة:** [PLAN-47](acceptance-plans/PLAN-47-pol-rule-lifecycle.md)
- **التحقق المطلوب:**
  - سالب: نشر بالمستخدم نفسه ← 400 حتى مع العلم.
  - سالب: ناشر أضاف قاعدة إلى المسودة ← رفض.
  - سالب: `{"when":...}` أو عامل مجهول أو حقل خارج المخطط ← رفض بتفسير.
  - موجب: مؤلف ومعتمد مختلفان ينشران بنجاح.

#### G-POL-07 — الإصدارات المنشورة ليست ثابتة، والتأريخ شكلي، ولا يوجد تراجع
- **الدليل:**
  - **البذور تعدّل الإصدار المنشور في مكانه:**
    - `AddRuleIfMissingAsync` يضيف القاعدة إلى أحدث إصدار **منشور** ([PolicyEngineDomainSeeds.cs:297-329](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs))، وكذلك `SeedSelectCodePolicyRuleIfMissingAsync` ([PolicyEngineSeeder.cs:30-54](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs)).
    - `DeactivateRuleIfPresentAsync` يعيد كتابة `When`/`Then`/`Name` لكل نسخ `R-REC-HIRE` في **كل** الإصدارات، ومنها التاريخية (`PolicyEngineDomainSeeds.cs:275-289`).
    - هذا التفاف لغياب `IsActive` ([plan25-final-report.md §11](../reports/plan25-final-report.md)). ويخالف «No silent modifications» ([legal-source-binding.md:39](syr/legal-source-binding.md)) ونقطة AC-10 السالبة «ولا يُعدَّل إصدار منشور» ([PLAN-02:26](acceptance-plans/PLAN-02-policy-shadow-enforce.md)).
  - **تاريخ النفاذ:** لا يوجد إدخال لـ`EffectiveFrom` في أي متحكم. النشر يضبطه على «الآن» ([PolicyVersionPublisher.cs:32](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyVersionPublisher.cs)). والتقييم دائماً بـ`DateTime.UtcNow` ([PolicyOrchestrator.cs:91](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs))، ولا `AsOf` في `PolicyEnforceRequest`. لا يمكن جدولة قانون يسري الشهر القادم، ولا تقييم قرار رجعي بقاعدة زمنه.
  - **لا تراجع:** `RolledBackFromVersionId` لا يضبطه أي سطر ([PolicyEntities.cs:102](../src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs)). `Archived` لا تضبطه الشيفرة. `CreateDraft` يبدأ إصداراً **فارغاً** ([PolicyTemplatesController.cs:121-128](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). أي إصدار جديد أو تراجع يعني إعادة كتابة كل القواعد يدوياً بـJSON، ونشر مسودة فارغة يُسقط المجال إلى البديل الثابت (`PolicyOrchestrator.cs:92-95`).
  - **المخطط بالشيفرة:** لا واجهة لتعديل مخطط الحقائق، والحقول تُضاف بالبذور فقط (`EnsureFactsFieldAsync`، `:331-350`). إضافة حقيقة جديدة لقاعدة تتطلب نشر شيفرة، وهذا يخالف `changeWithoutDeployOrFork` ([policy-platform-matrix.json:33](prod-policy/policy-platform-matrix.json)).
- **الأثر:**
  - لا يمكن إثبات القاعدة النافذة وقت قرار سابق.
  - تغيير قانوني بتاريخ نفاذ محدد يتطلب نشراً في اللحظة نفسها.
  - التراجع عن قاعدة خاطئة يستغرق إعادة إدخال كاملة تحت ضغط.
- **الإصلاح المقترح:**
  - عمود `IsActive`/`RetiredAt` للقاعدة. تغييرات البذور عبر إصدار جديد ينشره `PolicyVersionPublisher` بسجل نشر.
  - منع تعديل صفوف إصدار منشور (حارس `SaveChanges` أو trigger).
  - `CreateDraft` ينسخ قواعد الإصدار النافذ.
  - `EffectiveFrom` مستقبلي على المسودة، و`AsOf` اختياري في الطلب.
  - إجراء «تراجع» يعيد نشر إصدار سابق كإصدار جديد مع `RolledBackFromVersionId`.
- **الخطة:** [PLAN-47](acceptance-plans/PLAN-47-pol-rule-lifecycle.md)
- **التحقق المطلوب:**
  - موجب: نشر v3 بتاريخ غد ← اليوم يقيَّم بـv2 وغداً بـv3.
  - موجب: تراجع إلى v2 يُنشئ v4 مطابقاً.
  - سالب: إعادة تشغيل الخدمة لا تغيّر `WhenJson` لأي قاعدة منشورة (بصمة قبل/بعد).

#### G-POL-08 — نقطة `enforce` العامة تثق بالمستدعي: الوضع والفاعل والكيان من جسم الطلب
- **الدليل:**
  - `POST api/policies/enforce` متاح لـ`HrCoreBranchOperation`: Admin وHrCore وBranchManager وHrManagementApprover ([PoliciesController.cs:17-29](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)، [GatewayPathAuthorizationMiddleware.cs:184-189](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - `Mode` يؤخذ من الجسم كما هو (`:34`). `ActorUserId ??=` يحتفظ بالقيمة المرسلة (`:31`)، فيُسجل في الدفتر وفي `InitiatedBy` فاعل مزيّف.
  - في Enforce مع `TriggerWorkflow` تُنشأ نسخة مسار لأي `SubjectType`/`EntityId` يرسله المستدعي ([PolicyOrchestrator.cs:46-52](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)، `:168-177`).
  - دليل حي: AC-11 «Pass» أنشأ فيه `b-branch-manager` مساراً لـ`AssetAssignment` بمعرّف عشوائي غير موجود ([Ac11PolicyWorkflowTests.cs:28-34](../src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs)، [AC-11/steps.md:9](../acceptance-evidence/AC-11/steps.md)).
  - **سلسلة محتملة (يحتاج تحقق):**
    - مستخدم ينشئ مسبقاً بـ`enforce` نسخة `RECRUITMENT-OFFER-APPROVAL` لعرض حقيقي بفاعل مزيّف.
    - عند التقديم الحقيقي يعيد المنسّق استعمال النسخة غير المبدوءة بالرمز نفسه ([ApprovalStartCoordinator.cs:172-191](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)).
    - فحص «المبادر لا يعتمد» يقارن بـ`InitiatedBy` ([WorkflowApprovalEvaluator.cs:51](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/Gates/WorkflowApprovalEvaluator.cs))، وهذا الحقل صار مزيّفاً.
- **الأثر:** واجهة كتابة عامة تسمح بإغراق صناديق العمل بنسخ وهمية، وتزييف سجل الفاعل في الدفتر، واحتمالاً تجاوز فصل المهام في الاعتماد.
- **الإصلاح المقترح:**
  - فصل `api/internal/policies/enforce` (حساب خدمة فقط، والوضع يُحل في الخادم من الإعداد) عن `evaluate` للمستخدمين.
  - تجاهل `ActorUserId`/`Mode` من الجسم.
  - قصر المجال و`SubjectType` على الخدمة المستدعية.
  - منع `TriggerWorkflow` من هذه النقطة لأي كيان لا تملكه الخدمة.
  - رفض إعادة استعمال نسخة بدأها غير المقدِّم.
- **الخطة:** [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md)
- **التحقق المطلوب:**
  - سالب: `branch-manager` يرسل `Mode=Enforce` مع `EntityId` عشوائي ← 403 أو قرار بلا نسخة.
  - سالب: `ActorUserId` مختلف عن الرمز ← يُسجَّل الفاعل الحقيقي.
  - موجب: حساب `svc-admin-logistics` يحصل على قرار ونسخة لتسليم أصل حقيقي.

#### G-POL-09 — في Enforce تستطيع السياسة اختيار مسار اعتماد أضعف
- **الدليل:**
  - المنسّق يقبل أي نسخة بدأتها السياسة دون مقارنة رمزها بالرمز المطلوب، مثل `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` للعرض الاستثنائي ([ApprovalStartCoordinator.cs:98-99](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)).
  - معالجات النتيجة تُختار بنوع الكيان لا بالتعريف ([WorkflowInstanceActionService.cs:189-202](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). وتتحقق فقط من `WorkflowInstanceId == instance.Id` ([OfferLetterWorkflowHandler.cs:24](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/OfferLetterWorkflowHandler.cs)).
  - `WorkflowStarter` يتجاهل `PolicyWorkflowBinding` إن لم يجد ربطاً، ويبدأ أي تعريف منشور بالرمز ([WorkflowStarter.cs:93-124](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)). الربط يثبّت إصدار الرموز المربوطة فقط (v1 لطلب الشاغر والعرضين)، ولا يقيّد **أي رمز** تختاره السياسة، رغم أن الأطروحة تقول إنه «يحدد تعريف المسار» ([THESIS_GRADUATION_CURRENT.md:356](THESIS_GRADUATION_CURRENT.md)).
  - مثال: قاعدة بأولوية 1 `Issue ← TriggerWorkflow RECRUITMENT-OFFER-APPROVAL` تجعل العرض الاستثنائي يمر بخطوتين بدل ثلاثة معتمدين. أما قاعدة `IT-TICKET` على `OfferLetter` فتُعلِّق العرض: دور الخطوة `it-support` مفروض لهذا النوع، وحامله لا يصل إلى HR.Core.
- **الأثر:** في Enforce يصبح محرر السياسة قادراً على خفض سلسلة الاعتماد التي فرضها PLAN-27/28 دون تغيير شيفرة. Production اليوم على Shadow، فالثغرة كامنة حتى التفعيل. *(صُحّح بعد التحقق المستقل: الربط يثبّت إصدار الرموز المربوطة ولا يقيّد اختيار الرمز؛ ومثال `IT-TICKET` يُعلِّق العرض ولا يعتمده)*
- **الإصلاح المقترح:**
  - المنسّق يرفض نسخة السياسة إن لم يكن رمزها ضمن قائمة الرموز المسموحة لنوع الكيان وللحالة (العادي/الاستثنائي)، أو يعامل `TriggerWorkflow` كـ«مسار إضافي» لا بديل.
  - المعالجات تتحقق من رمز التعريف.
  - الربط إلزامي: لا بدء لرمز غير مربوط بالمجال.
- **الخطة:** [PLAN-49](acceptance-plans/PLAN-49-pol-engine-scope-orphans.md)
- **التحقق المطلوب:**
  - سالب: Enforce مع قاعدة تطلق مساراً أضعف لعرض استثنائي ← رفض التقديم برسالة.
  - موجب: القاعدة المبذورة R-OFF-EXCEPTION تبدأ مسار الثلاثة.

#### G-POL-10 — الدفتر لا يجيب «أي قاعدة قررت في المعاملة X»، وفروق Shadow ضجيج دائم
- **الدليل:**
  - **حقول الدفتر:** المجال والإجراء والإصدار و`CorrelationId` و`FactsHash` والقرار والفاعل والوضع ([PolicyEntities.cs:116-146](../src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs)). لا `EntityId` ولا `SubjectType`. والحقائق مختزلة إلى 16 حرفاً من SHA-256 ([PolicyOrchestrator.cs:180-185](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)).
  - **لا ربط بالمعاملة:** المستدعون داخل HR.Core لا يضبطون `CorrelationId` (المنسّق [ApprovalStartCoordinator.cs:88-93](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs)، والنشر [RecruitmentRequisitionLifecycleService.cs:161-170](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs)). لا يمكن ربط الصف بالطلب، ولا إعادة تشغيل القرار، ولا تنفيذ «Verification» في [legal-source-binding.md:60-64](syr/legal-source-binding.md).
  - **Off لا يترك أثراً:** لا صف أصلاً، وهو وضع 19 مجالاً في الإنتاج.
  - **Shadow يقارن بالبديل الثابت، لا بالسلوك الفعلي** ([PolicyStaticFallback.cs:231-247](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyStaticFallback.cs)):
    - المقيّم يُدرج `Allow` في الآثار عند التطابق ([PolicyEvaluator.cs:76-78](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)). البديل الثابت يعيد `[TriggerWorkflow]` بلا `Allow` لـ`SubmitForApproval`، و`[]` للنشر (`PolicyStaticFallback.cs:96-136`).
    - النتيجة: كل تقديم ونشر واستئناف ولجنة في التوظيف يُسجَّل «فرقاً». وللمجالات غير الثلاثة يقارن البديل «سماح بلا آثار» بأي قرار.
  - **العرض محدود:** تقرير Shadow آخر 7 أيام و200 صف بلا تجميع ([PolicyTemplatesController.cs:202-207](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). المقياس الوحيد سطر `LogInformation` (`PolicyOrchestrator.cs:42`). لا يُعرض قرار Shadow لمستخدم المعاملة.
- **الأثر:** Shadow لا يقيس الأثر كما تدعي الأطروحة ([THESIS_GRADUATION_CURRENT.md:350](THESIS_GRADUATION_CURRENT.md)). ولا يوجد معيار موضوعي للانتقال إلى Enforce. والرقابة لا تستطيع تتبع قرار إلى قاعدة.
- **الإصلاح المقترح:**
  - إضافة `EntityId`/`SubjectType`/`TenantId` والحقائق بعد تنقيح البيانات الشخصية، مع احتفاظ مقرر (D-POL-9).
  - `CorrelationId` إلزامي من المستدعين.
  - مقارنة Shadow بالنتيجة الفعلية للمعاملة: سُمح/رُفض/أيّ مسار بدأ.
  - تقرير تجميعي لكل مجال وقاعدة، ومقياس للفروق.
  - صف دفتر مختصر حتى في Off للمجالات المسجلة.
- **التحقق المطلوب:**
  - موجب: من شاشة طلب شاغر يُعرض صف الدفتر وإصدار القاعدة والحقائق.
  - سالب: عشرون تقديماً متطابقاً في Shadow بلا فرق حقيقي ← صفر فروق.

#### G-POL-11 — قواعد `TriggerWorkflow` يتيمة أو ميتة بعد R-REC-HIRE
- **الدليل:**
  - **حيّة ويتيمة: `R-ID-PROCESS`** ← `ID-CARD-ISSUANCE` على `CardRequest` ([PolicyEngineDomainSeeds.cs:15-21](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)). IDCard يستدعيها في `ProcessRequest` ويتجاهل `WorkflowInstanceId`، ويصدر البطاقة فوراً ([CardRequestsController.cs:132-161](../src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs)). و`CardRequest` ليس في `HandledEntityTypes` ([WorkflowInstanceActionService.cs:59-70](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). في Enforce تظهر مهمة «إصدار أمني» لدور `security` لبطاقة صدرت أصلاً، واعتمادها لا يفعل شيئاً، وهو نمط G-14 في HCM-001 نفسه.
  - **ميتة لكنها منشورة:** `R-HIRE-START` (`HIRE-WIZARD`)، و`R-SEP-START` (`SEPARATION-WIZARD`)، و`R-TRF` (`TRANSFER-APPROVAL`)، و`R-DISC` (`DISCIPLINARY-APPROVAL`)، و`R-TKT` (`IT-TICKET`) (`:39-53`، `:74-80`، `:120-126`، `:136-142`). تعريفاتها منشورة ([WorkflowPackSeeder.cs:29-50](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)).
  - **الحارس المعماري يعفيها بتبريرات** مثل «IDCard processes the request locally» و«the compliance service owns the action» ([WorkflowDefinitionsHaveHandlersTests.cs:15-23](../src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs)). لكن لا يوجد مستهلك لنتيجة هذه المسارات في أي خدمة، ولا حدث يُنشر لها من HR.Core.
  - البذرة تربطها بالمجالات ([PolicyEngineSeeder.cs:172-185](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs)).
- **الأثر:** أي ربط مستقبلي لهذه المجالات بالمحرك في Enforce يكرر G-14: عناصر معلّقة في صناديق العمل لا يغيّر اعتمادها شيئاً.
- **الإصلاح المقترح:**
  - حذف `R-ID-PROCESS` (أو تطبيق مستهلك في IDCard ينتظر النتيجة).
  - سحب التعريفات الخمسة وقواعدها بآلية الإصدار الجديد (G-POL-07)، أو ربط كل منها بمعالج ونشر حدث.
  - تشديد الحارس: الإعفاء يتطلب مستهلكاً مُثبتاً (اسم consumer) لا جملة.
- **الخطة:** [PLAN-49](acceptance-plans/PLAN-49-pol-engine-scope-orphans.md) (القواعد)، و[PLAN-46](acceptance-plans/PLAN-46-wf-cross-service-approvals.md) (التعريفات اليتيمة وقائمة السماح)
- **التحقق المطلوب:**
  - موجب: Enforce في `IDCard.Lifecycle` ← لا نسخة مسار، أو البطاقة تنتظر الاعتماد.
  - سالب: الحارس يفشل عند إضافة تعريف منشور بلا معالج أو مستهلك.

#### G-POL-12 — القيم القانونية خارج المحرك وبلا ربط بالمصدر، وتغطية أنواع الفصل ناقصة
- **الدليل:**
  - **المعيار المطلوب:** [legal-source-binding.md:9-15](syr/legal-source-binding.md) ومعيار SYR-001 «100% من قواعد HR/payroll/leave/pension/tax لها `legal_source_id`…» ([13-SYRIA:10](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md)) والبطاقة «Done». لكن `PolicyRule` بلا `LegalSourceId` ولا مادة ولا مالك ([PolicyEntities.cs:56-74](../src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs)).
  - **أين تعيش القيم فعلاً (المحرك ليس مصدر الحقيقة في أي منها):**

    | المجال | مصدر القيمة | مؤرخ؟ | مصدر قانوني؟ |
    |---|---|---|---|
    | التوظيف والملاك | `LegalSourceId` على الكيان + مدخلات المسابقة | لكل كيان | ✅ |
    | الرواتب | `PayrollConfiguration` + سجل قرارات Payroll بحالة `ApprovedInterim` (→ PAY) | خاص بـPAY | جزئي |
    | الإجازات | جدول `LeavePolicy` محلي. `MaxDaysPerYear` ([LeavePolicy.cs:15](../src/Services/HRContracts/HR.Contracts.Domain/Entities/LeavePolicy.cs)) يُنسخ إلى `LeaveBalance.TotalEntitlement` عند تهيئة الأرصدة ([LeaveBalancesController.cs:152](../src/Services/HRContracts/HR.Contracts.Web/Controllers/LeaveBalancesController.cs))، ويُفرض عند الاعتماد بشرط `Used + DurationDays <= TotalEntitlement` ([LeaveRequestHandlers.cs:284-285](../src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs))، بلا مصدر قانوني. R-LEAVE-MAX «تتجاوز الحد الأقصى الوطني» = 30 يوم عمل لكل الأنواع ([PolicyEngineDomainSeeds.cs:179-182](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)) | لا | ❌ |
    | السلف | ثوابت 100,000 / 12 قسطاً / نسبة 3 ([LeaveAndAdvanceRules.cs:9-11](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs))، وقسمة 5,000 (`:214`)؛ وR-ADV-HIGH = 50,000 غير مستهلكة | لا | ❌ |
    | الفصل/الترفيع/النقل | `service-regimes.json` بتحميل `Lazy` مرة لكل عملية | لا | على مستوى النظام فقط |
    | التأديب | `ActionType` بأربع قيم: إنذار/إيقاف/فصل/أخرى ([ActionType.cs:8-14](../src/Services/Compliance/Compliance.Domain/Enums/ActionType.cs)) | لا | ❌ |

  - **قيمة نائبة ضارة لو فُعّلت:** `LeaveType.Maternity` موجود ([LeaveType.cs:14](../src/Services/HRContracts/HR.Contracts.Domain/Enums/LeaveType.cs)). قاعدة 30 يوماً بلا فلتر نوع ترفض كل إجازة أمومة أو مرضية أطول، أيّاً كان النص النافذ (القيمة القانونية الصحيحة قرار D-POL-5).
  - **تغطية الفصل لقانون 50:** المسموح `resignation, retirement, termination` فقط، و`notice_days: 30` لكل الأنظمة عدا القضائي ([service-regimes.json:99-104](syr/service-regimes.json)). بينما `SeparationType` يعرّف `Medical` و`Judicial` و`Disciplinary` ([SeparationType.cs:16-20](../src/Services/HRContracts/HR.Contracts.Domain/Enums/SeparationType.cs))، ولا يوجد نوع للوفاة. النتيجة: الأنواع `Disciplinary` و`Medical` و`Judicial` تُرفض لموظف قانون 50 بـ«غير مسموح لنظام الخدمة الحالي». يبقى `termination` مسموحاً لكل الأنظمة، فتُسجَّل هذه الحالات تحت «إنهاء» عام وتفقد تصنيفها القانوني.
  - **الأنظمة الخاصة:** `special_banking`/`special_judiciary` تُحوَّل إلى `null` في رمز نظام الرواتب ([ServiceRegimeToPayrollMapper.cs:9-15](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeToPayrollMapper.cs))، رغم أن SYR-003 «Done» بأربعة أنظمة ([13-SYRIA:44](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md)).
- **الأثر:**
  - لا يمكن لجهة رقابية تتبع إجازة أو سلفة أو فصل إلى مادة قانونية.
  - تغيير قانوني يتطلب نشر شيفرة، ويغيّر الماضي بصمت لأن الملف بلا إصدار.
  - حالات فصل قانونية حقيقية لا تُسجَّل بنوعها الصحيح. *(صُحّح بعد التحقق المستقل: `MaxDaysPerYear` مفروض عبر رصيد الإجازة عند الاعتماد، و`termination` متاح لكل الأنظمة؛ غياب `legal_source_id` مؤكد)*
- **الإصلاح المقترح:**
  - حقول `LegalSourceId`/`Article`/`Owner` على `PolicyRule` (أو على الإصدار)، يتحقق منها `LegislationRegisterProvider` عند النشر.
  - نقل قواعد أنظمة الخدمة إلى جداول مؤرخة في HR.Core تخدمها API (يتكامل مع G-POL-01).
  - حذف القواعد النائبة من الإنتاج حتى تصل القيم من المالك القانوني (D-POL-5..8).
  - رمز رواتب أو حجب صريح للأنظمة الخاصة.
- **التحقق المطلوب:**
  - موجب: عينة إجازة وفصل وترفيع تُتتبع من الواجهة إلى `legal_source_id` ومادة وإصدار.
  - سالب: نشر قاعدة بلا مصدر نافذ ← رفض.
  - موجب: فصل تأديبي لموظف قانون 50 بعد اعتماد D-POL-6 ينجح، ويُرفض نوع غير مسموح وفق الجدول المعتمد.

### 4.3 قدرات ناقصة (P2)

#### G-POL-13 — لا محاكاة على المسودة ولا تحليل أثر لتغيير قانوني
- **الدليل:**
  - واجهة `Simulate` تستقبل `versionId` لكنها تقيّم `orchestrator.EvaluateAsync` على **اللقطة المنشورة** للمجال ([PolicyTemplatesController.cs:68-74](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)). المسودة لا تُحاكى من الواجهة.
  - API المحاكاة تقيّم الإصدار وحده بلا دمج وطني/جهة، وتحفظ الحقائق الحقيقية دون فحص ([PoliciesController.cs:74-99](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs))، رغم `realDataForbidden` ([policy-platform-matrix.json:22](prod-policy/policy-platform-matrix.json)).
  - لا إعادة تشغيل دفعية على قرارات سابقة، لأن الحقائق غير مخزنة (G-POL-10). ولا «impact report» يطلبه [legal-source-binding.md:47](syr/legal-source-binding.md).
- **الأثر:** لا يمكن لمسؤول قانوني أن يعرف قبل النشر كم طلب إجازة أو أصل أو عرض سيتغير قراره.
- **الإصلاح المقترح:**
  - محاكاة المسودة مع الدمج.
  - تشغيل دفعي على حقائق مخزنة لآخر N يوم، مع تقرير «يتغير/لا يتغير» لكل قاعدة.
  - مولّد حقائق اصطناعية للمخطط.
- **التحقق المطلوب:**
  - موجب: مسودة تخفض الحد من 30 إلى 20 ← تقرير بعدد الطلبات المتأثرة من آخر 30 يوماً.
  - سالب: المحاكاة لا تكتب في الدفتر ولا تبدأ مساراً.

#### G-POL-14 — أدوات إدارة السياسة دون ما تحتاجه إدارة موارد بشرية حكومية
- **الدليل:**
  - التأليف حقلا نص JSON ([EditRules.cshtml](../src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/EditRules.cshtml)).
  - لا تعديل ولا حذف ولا تعطيل لقاعدة، ولا كشف تعارض بين قاعدتين (Deny وAllow على الشرط نفسه، أو `TriggerWorkflow` متنافسان).
  - لا خيار «رفض افتراضي» لمجال. عدم التطابق سماح دائماً، وقاعدة `Allow` لا معنى لها (R-REQ-PUBLISH، R-RCV-FULFIL).
  - التفسيرات «قاعدة X: تطابق» بلا نص مادة ([PolicyEvaluator.cs:49](../src/Shared/HRManagement.SharedKernel/Policy/PolicyEvaluator.cs)).
  - لا اختبارات دلالية لقواعد المجالات المبذورة غير التوظيف.
- **الأثر:** التشغيل يعتمد على مطوّر يكتب JSON صحيحاً، وهذا يناقض هدف «تغيير القانون دون نشر».
- **الإصلاح المقترح:**
  - محرر منظّم يعتمد مخطط الحقائق.
  - تعطيل وحذف في المسودة.
  - كشف تعارضات عند النشر.
  - `DefaultEffect` لكل مجال.
  - تفسير عربي يذكر المادة.
  - «حالات ذهبية» لكل قاعدة تُشغَّل عند النشر.
- **التحقق المطلوب:**
  - موجب: مستخدم غير مطوّر ينشئ قاعدة إجازة بالمحرر، فتُنشر بعد المراجعة وتمر حالاتها الذهبية.
  - سالب: قاعدتان متعارضتان ← تحذير يمنع النشر.

#### G-POL-15 — حل الوضع وانتشار النشر والكاش متفرقة وغير موثوقة (P2/P3)
- **الدليل:**
  - **حل الوضع مكرر في خمسة أماكن:** `ApprovalStartCoordinator.cs:73`، `RecruitmentRequisitionLifecycleService.cs:491`، `RecruitmentAppealService.cs:484`، `RecruitmentApplicationsController.cs:721`، `PolicyEnforceClient.cs:29`. ولا تحقق عند الإقلاع (انظر اختلاف القيمة الخاطئة في G-POL-03).
  - **حدث النشر يضيع على الأرجح:** يُنشر `IPolicyPublished` بعد `SaveChanges` الذي يجريه الناشر ([PoliciesController.cs:117-127](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)). وHR.Core يستعمل `UseBusOutbox` ([MassTransitExtensions.cs:115-120](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)، [Program.cs:262](../src/Services/HRCore/HR.Core.Web/Program.cs))، فالرسالة تُضاف إلى سياق لا يُحفظ بعدها ولا تُرسل (يحتاج تحقق). والنشر من الواجهة `PublishFromUi` لا ينشر الحدث أصلاً (`PolicyTemplatesController.cs:174-197`).
  - **الكاش لكل نسخة تشغيل:** إبطال الكاش داخل العملية فقط ([PolicyVersionPublisher.cs:59](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyVersionPublisher.cs)). HR.Core بنسختين في `production-ha` ([replicas.yaml:11-13](../deploy/k8s/overlays/production-ha/patches/replicas.yaml))، فالنسخة الأخرى تبقى على اللقطة القديمة حتى 5 دقائق ([PolicyPublishedSnapshotCache.cs:21](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyPublishedSnapshotCache.cs)).
  - **وحدة العمل:** `EnforceAsync` يحفظ سياق المستدعي كله ([PolicyOrchestrator.cs:70](../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)). عند نشر طلب شاغر يُحفظ تاريخ الإغلاق المعدّل (`RecruitmentRequisitionLifecycleService.cs:154`، `:476`) حتى لو رفضت السياسة النشر (`:172-174`) (P3).
- **الأثر:** قرارات غير متسقة بين النسخ لدقائق بعد النشر. والخدمات البعيدة لا تعلم بالنشر. وكتابات جزئية عند الرفض.
- **الإصلاح المقترح:**
  - `IPolicyModeResolver` واحد مع تحقق إقلاع وعرض في الواجهة.
  - نشر الحدث قبل `SaveChanges` داخل المعاملة نفسها.
  - إبطال الكاش عبر الحدث في كل النسخ، أو مفتاح كاش برقم الإصدار.
  - الدفتر في سياق منفصل.
- **الخطة:** [PLAN-47](acceptance-plans/PLAN-47-pol-rule-lifecycle.md) (جزئياً: مسار نشر واحد وحمولته، وإبطال الكاش في كل نسخ HR.Core)، و[PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md) (جزئياً: نشر `IPolicyPublished` قبل الحفظ)، و[PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md) (جزئياً: `IPolicyModeResolver` واحد مع تحقق إقلاع). والدفتر في سياق منفصل «الموجة 2»
- **التحقق المطلوب:**
  - موجب: نشر على نسختين ← القرار الجديد من النسختين خلال ثوانٍ، ورسالة `PolicyPublished` في جدول outbox.
  - سالب: رفض النشر لا يغيّر `ApplicationClosingDate`.

### 4.4 فجوات التحقق والتوثيق

#### G-POL-16 — ادعاءات PROD-001 وPLAN-02 بلا دليل، والوثائق متناقضة
- **الدليل:**
  - **حالة متناقضة:**
    - البطاقة «Done (production policy-platform binding)» ([07:397](completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md))، ومعاييرها «تغيير قانون دون deploy؛ الماضي يستخدم نسخته؛ invalid policy لا تنشر؛ override لا يتجاوز national» (`:400`)، وكلها غير متحققة في الشيفرة (G-POL-05/06/07).
    - البطاقة نفسها تقول `live-policy-engine = Blocked` (`:411`)، وكذلك [PROD_001_VERIFICATION.md:10](PROD_001_VERIFICATION.md)، بينما المصفوفة تقول `Satisfied` ([policy-platform-matrix.json:53-54](prod-policy/policy-platform-matrix.json)).
    - `PROD_001_ROLLBACK.md` و`PROD_001_FOUR_EYES_PUBLISH.md` من ثلاثة أسطر.
  - **حراس مختبرة غير مستعملة:** `IPolicyPlatformClaimGuard` (`ValidateFourEyesPublish`، `ValidateRollback`، `ValidateOverrideCeiling`…) لا يستدعيه أي متحكم. يختبره `Prod001ProductionBindingTests.cs:213-222` فقط ([Prod001ProductionBindingTests.cs](../src/Tests/HRManagement.UnitTests/Prod001ProductionBindingTests.cs)). هذا نمط «اختبار نموذج غير مستعمل».
  - **القبول:**
    - AC-09 وAC-10 «not run» ([summary.md:44-45](../acceptance-evidence/summary.md)).
    - كلاهما `[SkippableFact]` بلا `AcceptanceGate`/`[RequiredAcceptance]` ([Ac09ShadowPolicyTests.cs:13](../src/Tests/HRManagement.AcceptanceTests/Ac09ShadowPolicyTests.cs)، [Ac10EnforcePolicyTests.cs:13](../src/Tests/HRManagement.AcceptanceTests/Ac10EnforcePolicyTests.cs)).
    - كلاهما يرسل `Mode` في الجسم إلى API خام بدل اختبار خدمة مجال. وAC-10 يعتمد على تجاوز أربع العيون.
    - AC-11 «Pass» على كيان وهمي (G-POL-08).
  - **وثائق تخالف الشيفرة:**
    - [FUN_005_JOURNEY_MATRIX.md:26](FUN_005_JOURNEY_MATRIX.md) «المعايير في محرك السياسات (`R-CLASSIFY-*`)»، والإنتاج يستعمل shim ثابتاً.
    - [THESIS_GRADUATION_CURRENT.md:352](THESIS_GRADUATION_CURRENT.md) «ولا تعدل النسخة المنشورة».
    - [POLICY_DOMAIN_REGISTRY.md](POLICY_DOMAIN_REGISTRY.md) يقول «Static» لمعظم المجالات، والواجهة تقول «Shadow».
    - [POLICY_ENGINE_UAT.md](POLICY_ENGINE_UAT.md) يعد بـ`LivePolicyEngineClaim=true` في Staging.
- **الأثر:** قرار الإطلاق أو الاعتماد يُبنى على «Done» لا يصمد. ولا يوجد تشغيل واحد يثبت أن خدمة مجال تمنع شيئاً بقاعدة منشورة.
- **الإصلاح المقترح:**
  - إعادة كتابة AC-09/10 عبر نقاط استدعاء المجالات على الملف الشبيه بالإنتاج بـ`AcceptanceGate`.
  - سيناريوهات `AC-POL-*` لكل خطة.
  - تصحيح البطاقة والمصفوفة وADR وFUN_005 والأطروحة وسجل المجالات.
  - ربط حراس ProdPolicy بالمسار الحقيقي أو حذفها.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)
- **التحقق المطلوب:**
  - مجلدات `acceptance-evidence/AC-09`، `AC-10`، `AC-POL-*` بالعناصر الثمانية ونتيجة لكل سيناريو.
  - سالب: لا اختبار قبول للمحرك ينجح دون شرطه المسبق.

### 4.5 تقاطعات مع الدراسات الأخرى

- → **OPS:** `hr-contracts-web`/`hr-movements-web`/`idcard-web` بلا `Integration__HrCoreBaseUrl` في الإنتاج، ولا في `hr-app-config` على k8s ([docker-compose.production.yml:279](../docker-compose.production.yml)، `:320`، `:386`). هذا يكسر عميل نظام الخدمة النشط وعميل السياسات.
- → **OPS:** لا اختبار دخان للصورة يتحقق من حزم البيانات المشحونة (`seed-packs`) (G-POL-01).
- → **IAM:** `svc-hr-contracts` و`svc-idcard` بلا نطاق `hr-core.workflow`/`hr-core.lookup` في realm ([realm-hrmanagement-dev.template.json:516-541](../docker/keycloak/realm-hrmanagement-dev.template.json)).
- → **IAM:** `branch-manager` يصل إلى `POST api/policies/enforce` ([GatewayPathAuthorizationMiddleware.cs:184-189](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
- → **IAM:** مستخدم جهة ينشئ قالباً بنطاق `National` في جهته ([PolicyTemplatesController.cs:103-119](../src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs)).
- → **WF:** `WorkflowStarter` يتجاوز الربط ويبدأ أي تعريف منشور بالرمز، والربط يثبّت `WorkflowDefinitionId` لحظة البذر ([WorkflowStarter.cs:93-124](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)).
- → **WF:** نسخة تكتمل عند البدء (`Completed`) لا تستدعي معالج النتيجة (`WorkflowStarter.cs:79-80`).
- → **WF:** المعالجات لا تتحقق من رمز التعريف ([OfferLetterWorkflowHandler.cs:24](../src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/OfferLetterWorkflowHandler.cs)).
- → **QA:** AC-09/10 بـ`[SkippableFact]` خارج `AcceptanceGate`.
- → **QA:** `Prod001ProductionBindingTests` تختبر حراساً غير مستعملة.
- → **QA:** لا اختبار «مفاتيح البناة ⊆ المخطط».
- → **QA:** فجوة DI لـ`IHrCoreActiveRegimeClient` في مضيف اختبارات التكامل ([plan25-final-report.md §11](../reports/plan25-final-report.md)).
- → **PAY:** الأنظمة الخاصة بلا رمز رواتب ([ServiceRegimeToPayrollMapper.cs:9-15](../src/Shared/HRManagement.SharedKernel/Legal/ServiceRegimeToPayrollMapper.cs)).
- → **PAY:** سجل القرارات القانونية للرواتب بحالة `ApprovedInterim` آلية منفصلة عن المحرك ([LegalDecisionRegisterSeed.cs:14-30](../src/Services/Payroll/Payroll.Domain/Services/LegalDecisionRegisterSeed.cs)).
- → **دراسة الإجازات/العقود:** طلب ESS الوارد يتجاوز `ValidateLeaveRequest` (التداخل وأيام العمل) لا المحرك فقط ([InboundEventCommands.cs:252-289](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-POL-1 | نطاق المحرك: مصدر حقيقة لكل القواعد المجالية (ترحيل 31 ملف `*Rules.cs`) أم بوابة محدودة؟ | **بوابة محدودة الآن:** التوظيف والملاك والعهدة، ثم الإجازات والسلف بعد ربطها. حذف المجالات والقواعد الميتة من البذور والواجهة، وعدم ادعاء «محرك حي» لما لا يُستدعى | POL-E، G-POL-02/11 |
| D-POL-2 | سلوك Enforce عند تعذّر الوصول إلى HR.Core | **إغلاق** للإجراءات المقيدة برسالة واضحة، ولقطة موقّعة للقراءة فقط بمدة قصيرة لكل مستأجر | POL-B |
| D-POL-3 | من يؤلف السياسة ومن يعتمد نشرها؟ | دوران مستقلان: `policy-author` (موارد بشرية مركزية) و`policy-approver` (قانوني/`legal-control-officer`). لا تجاوز في الإنتاج | POL-C |
| D-POL-4 | سقف تجاوز الجهة | الجهة تضيف قيوداً أشد فقط (Deny، دليل، اعتماد إضافي). لا تغيّر `SetField`/`TriggerWorkflow`/`SelectCodePolicy` الوطنية | POL-D |
| D-POL-5 | القيم القانونية للإجازات (حد كل نوع، الأمومة، المرضية، الإشعار) وللسلف (السقف، الأقساط، النسبة) | تُستلم من المالك القانوني وفق القانون 50/2004 وتعليماته (SYD-001). حتى ذلك: لا قاعدة Deny بقيمة نائبة في الإنتاج | POL-F |
| D-POL-6 | أنواع إنهاء الخدمة المسموحة لكل نظام ومدد الإنذار (ومنها التأديبي والطبي والقضائي والوفاة لقانون 50) | جدول موقّع من التنمية الإدارية (SYD-001/002) يُحمَّل كإصدار مؤرخ | POL-A، POL-F |
| D-POL-7 | سلّم العقوبات التأديبية وأثرها على الراتب والترفيع | من النظام النافذ، بدل `enum` من أربع قيم، مع `legal_source_id` لكل عقوبة | POL-F |
| D-POL-8 | الأنظمة الخاصة (`special_banking`، `special_judiciary`) في الرواتب | رمز نظام رواتب لكل نظام خاص، أو حجب صريح للراتب حتى يُعرَّف | POL-F |
| D-POL-9 | الاحتفاظ بحقائق الدفتر (تتضمن بيانات شخصية) | حقائق منقّحة (بلا رقم وطني أو اسم) لمدة الاحتفاظ في SYD-007، تكفي لإعادة التشغيل والتتبع | POL-D، POL-G |

---

## 6. خارطة التنفيذ المقترحة

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة الموجة 1 |
|-------|---------|---------|----------|-----------|-------------------|--------------|
| POL-A | حزمة أنظمة الخدمة في التشغيل | G-POL-01 (وجزء G-POL-12) | P0 | — | 4–5 | [PLAN-37](acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) (G-POL-01) |
| POL-B | إنفاذ عبر الخدمات مغلق عند الفشل وحدود ثقة واضحة | G-POL-03، G-POL-04، G-POL-08، G-POL-09، جزء G-POL-15 | P1 | D-POL-2 | 7–9 | [PLAN-42](acceptance-plans/PLAN-42-s2s-trust-boundary.md) (G-POL-03، G-POL-08، وحل الوضع من G-POL-15)؛ G-POL-04 وG-POL-09 نُقلتا إلى PLAN-49 |
| POL-C | دورة حياة القاعدة: أربع عيون وتحقق وثبات وتأريخ وتراجع | G-POL-06، G-POL-07، جزء G-POL-15 | P1 | D-POL-3 | 8–10 | [PLAN-47](acceptance-plans/PLAN-47-pol-rule-lifecycle.md) (ومعها مسار النشر وإبطال الكاش من G-POL-15) |
| POL-D | النطاق الوطني/الجهة ودفتر قابل للتتبع وShadow ذو معنى | G-POL-05، G-POL-10 | P1 | D-POL-4، D-POL-9، POL-C | 6–8 | [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md) (G-POL-05)؛ G-POL-10 «الموجة 2» |
| POL-E | قرار النطاق وتنظيف المجالات واليتامى | G-POL-02، G-POL-11، جزء G-POL-04 | P1 | D-POL-1، POL-B | 5–8 | [PLAN-49](acceptance-plans/PLAN-49-pol-engine-scope-orphans.md) (G-POL-02، G-POL-04 كاملة، G-POL-09، G-POL-11) |
| POL-F | الربط القانوني للقيم وأنظمة الخدمة المؤرخة | G-POL-12 | P1 | D-POL-5..8، POL-A، POL-C | 8–10 | «الموجة 2» (أنواع الفصل لكل نظام في PLAN-61) |
| POL-G | المحاكاة وتحليل الأثر وأدوات التأليف | G-POL-13، G-POL-14 | P2 | POL-C، POL-D | 7–9 | «الموجة 2» |
| POL-H | التحقق والتوثيق | G-POL-16 | P0→مستمر | كل الخطط | 4–6 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) |
| **المجموع** | | | | | **≈ 49–65** | |

**ربط الموجة 1 (2026-10-30):**
- بقيت الرموز POL-A..H للتتبع، وتقابلها خطط الموجة 1 المرقّمة في العمود الأخير.
- PLAN-37 وPLAN-48 وPLAN-65 يكتبها مؤلفو COD وQA.
- ما وُسم «الموجة 2» يبقى بالرمز الحرفي: POL-F (G-POL-12)، وPOL-G (G-POL-13/14)، وG-POL-10 من POL-D، وبقية G-POL-15 (إبطال لقطات الخدمات البعيدة لم يعد مطلوباً بعد حذفها في PLAN-42).
- الفرق عن التقدير الأصلي: نُقلت G-POL-04 كاملة وG-POL-09 إلى PLAN-49، لأن كتالوج المجالات فيها هو الذي يحدد الحقائق والرموز المسموحة. وانضمت إلى PLAN-42 فجوات IAM/COD/SEP عن الثقة بين الخدمات.

**POL-A — حزمة أنظمة الخدمة في التشغيل (P0، 4–5 أيام).**
- تضمين `service-regimes.json` كمورد مضمَّن في SharedKernel مع بصمة، وإزالة البحث في المجلدات الأب في غير Development.
- فحص جاهزية `/ready` يفشل إن كانت قواعد الفصل أو الحركات أو الترفيع فارغة، ويسجّل بصمة الحزمة.
- `Integration__HrCoreBaseUrl` لـHR.Contracts وHR.Movements وIDCard في compose وk8s (بالتنسيق مع OPS).
- اختبار دخان للصورة: تشغيل الحاوية واستدعاء نقطة فحص تعيد عدد القواعد.
- السيناريوهات: AC-POL-Aa (نشر فصل `Resignation` لقانون 50 على الملف الشبيه بالإنتاج)، AC-POL-Ab (إنشاء نقل واعتماده)، AC-POL-Ac (ترفيع بمرجع قرار)، AC-POL-Ad سالب (صورة بلا الحزمة ← `/ready` 503).

**POL-B — إنفاذ عبر الخدمات مغلق عند الفشل (P1، 7–9 أيام).**
- العميل يعيد رفض 422 كما هو. سياسة فشل لكل مجال (D-POL-2). لقطة بمفتاح المجال والمستأجر.
- `IPolicyModeResolver` واحد مع تحقق إقلاع.
- فصل نقطة الخدمة الداخلية عن نقطة المستخدم: الوضع والفاعل من الخادم، وقصر المجال و`SubjectType` على الخدمة، ومنع `TriggerWorkflow` لكيان غير مملوك.
- مواءمة البناة والمخططات مع اختبار معماري.
- المنسّق يقبل نسخة السياسة فقط إن كان رمزها مسموحاً لنوع الكيان. المعالجات تتحقق من رمز التعريف.
- نطاقات Keycloak للمستدعين (مع IAM).
- السيناريوهات: AC-POL-Ba (Enforce إجازة مع قاعدة رفض ← رفض ودفتر)، AC-POL-Bb سالب (HR.Core متوقف ← رفض صريح)، AC-POL-Bc سالب (`branch-manager` يرسل `Mode=Enforce` بكيان عشوائي ← لا نسخة)، AC-POL-Bd سالب (قاعدة تطلق مساراً أضعف لعرض استثنائي ← رفض)، AC-POL-Be (قيمة وضع خاطئة ← فشل إقلاع).

**POL-C — دورة حياة القاعدة (P1، 8–10 أيام).**
- `IsActive`/`RetiredAt`، ومنع تعديل الإصدار المنشور، وتحويل تغييرات البذور إلى إصدارات جديدة عبر الناشر (بديل `DeactivateRuleIfPresentAsync`).
- `CreateDraft` ينسخ القواعد. تعديل وحذف داخل المسودة.
- مدقق نشر (JSON، العوامل، الآثار، الحقول، `When` الفارغ).
- تسجيل كل محرر، وفصل المؤلف عن الناشر، وإزالة `ForceSameActorForLab` خارج Development، والدوران الجديدان (D-POL-3).
- `EffectiveFrom` مستقبلي و`AsOf`، وإجراء تراجع مع `RolledBackFromVersionId`.
- نشر `IPolicyPublished` داخل المعاملة.
- السيناريوهات: AC-POL-Ca سالب (نشر بالمستخدم نفسه مع العلم ← 400)، AC-POL-Cb سالب (قاعدة بعامل مجهول ← رفض)، AC-POL-Cc (v3 بتاريخ غد ← اليوم v2 وغداً v3)، AC-POL-Cd (تراجع ← v4 مطابق لـv2)، AC-POL-Ce سالب (إعادة تشغيل لا تغيّر بصمة أي إصدار منشور).

**POL-D — النطاق الوطني/الجهة والدفتر (P1، 6–8 أيام).**
- تحميل القالب الوطني لكل الجهات (تجاوز `PolicyResolution` للقراءة). منع نطاق `National` خارج المستأجر الوطني. دلالات السقف (D-POL-4). إصلاح حلقة التهيئة.
- الدفتر: `EntityId`/`SubjectType`/`TenantId`/حقائق منقّحة و`CorrelationId` إلزامي.
- مقارنة Shadow بالنتيجة الفعلية، وتقرير تجميعي ومقياس.
- إبطال الكاش في كل النسخ.
- السيناريوهات: AC-POL-Da (جهة B بلا تجاوز تتلقى القرار الوطني)، AC-POL-Db سالب (تجاوز `SetField` وطني ← رفض النشر)، AC-POL-Dc (من شاشة الطلب يظهر صف الدفتر والإصدار والحقائق)، AC-POL-Dd سالب (20 تقديماً متطابقاً ← صفر فروق Shadow).

**POL-E — قرار النطاق وتنظيف المجالات (P1، 5–8 أيام حسب D-POL-1).**
- تنفيذ D-POL-1: سحب المجالات والقواعد الميتة بإصدارات جديدة، أو ربطها بنقطة استدعاء ومستهلك.
- حذف `R-ID-PROCESS` أو جعل IDCard ينتظر النتيجة.
- سحب أو ربط التعريفات الخمسة اليتيمة، وتشديد حارس `WorkflowDefinitionsHaveHandlersTests`.
- استدعاء المحرك في قنوات الإجازة الثلاث (HR، ESS، التعديل) إن بقي المجال.
- سجل قدرات الآثار لكل مجال، يرفض نشر أثر بلا مستهلك.
- السيناريوهات: AC-POL-Ea (سجل المجالات في الواجهة = نقاط الاستدعاء الفعلية)، AC-POL-Eb سالب (نشر `RequireDualApproval` لمجال لا يستهلكه ← رفض)، AC-POL-Ec (Enforce بطاقات ← لا نسخة يتيمة)، AC-POL-Ed سالب (قاعدة مدة الإجازة تمنع طلب ESS).

**POL-F — الربط القانوني للقيم (P1، 8–10 أيام، يعتمد على قرارات المالك).**
- `LegalSourceId`/`Article`/`Owner` على القاعدة (أو الإصدار) والتحقق من سجل التشريع عند النشر.
- قواعد أنظمة الخدمة (الفصل، الترفيع، الحركات، المادة 159) في جداول مؤرخة في HR.Core تخدمها API، بدل JSON ثابت.
- إزالة القيم النائبة (R-LEAVE-MAX، R-ADV-HIGH) حتى D-POL-5.
- أنواع الفصل وفق D-POL-6. سلم العقوبات وفق D-POL-7. رمز رواتب الأنظمة الخاصة وفق D-POL-8 (مع PAY).
- السيناريوهات: AC-POL-Fa (عينة إجازة وفصل وترفيع تُتتبع إلى `legal_source_id` ومادة وإصدار)، AC-POL-Fb سالب (نشر قاعدة بلا مصدر نافذ ← رفض)، AC-POL-Fc (فصل تأديبي لقانون 50 بعد D-POL-6 ينجح، ونوع غير مسموح يُرفض)، AC-POL-Fd (تعديل قانوني بتاريخ مستقبلي لا يغيّر قرارات الماضي).

**POL-G — المحاكاة وتحليل الأثر وأدوات التأليف (P2، 7–9 أيام).**
- محاكاة المسودة مع الدمج، وتشغيل دفعي على الحقائق المخزنة، وتقرير أثر لكل قاعدة.
- محرر منظّم يعتمد المخطط، وكشف تعارضات، و`DefaultEffect` لكل مجال، وتفسير عربي بالمادة، وحالات ذهبية تُشغَّل عند النشر.
- السيناريوهات: AC-POL-Ga (مسودة تخفض حداً ← تقرير بعدد المعاملات المتأثرة)، AC-POL-Gb سالب (المحاكاة لا تكتب دفتراً ولا تبدأ مساراً)، AC-POL-Gc سالب (قاعدتان متعارضتان ← منع النشر).

**POL-H — التحقق والتوثيق (4–6 أيام، مستمر).**
- إعادة كتابة AC-09/10 عبر خدمات المجال على الملف الشبيه بالإنتاج بـ`AcceptanceGate`/`[RequiredAcceptance]`.
- تحويل AC-11 إلى كيان حقيقي عبر `AssetsController`.
- تشغيل كل `AC-POL-*` بأدلة كاملة.
- تصحيح بطاقة PROD-001 والمصفوفة وADR-PROD-001a/b و`PROD_001_*` و`POLICY_DOMAIN_REGISTRY.md` و`FUN_005_JOURNEY_MATRIX.md` والأطروحة ودليل العرض، وتصحيح SYR-001/SYR-003 إلى «جزئي» حتى POL-F.
- ربط حراس ProdPolicy بالمسار الحقيقي أو حذفها مع اختباراتها.
- السيناريوهات: AC-POL-Ha (كل مجلد أدلة بعناصره الثمانية)، AC-POL-Hb سالب (غياب الشرط المسبق ← `Blocked` لا `Pass`).

**ملاحظة على التقدير:** الأرقام تقريبية لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: D-POL-1 (ترحيل القواعد المحلية إلى المحرك قد يضيف 15–25 يوماً لو اختير)، وتوقيت استلام القيم القانونية (D-POL-5..8).

---

## 7. معيار «اكتمال محرك السياسات وربط القواعد القانونية» المقترح

- [ ] **1. لا ادعاء بلا استدعاء.** كل مجال يظهر «فعّالاً» في `/PolicyTemplates` له نقطة استدعاء واحدة على الأقل، ووضع فعّال معروض محلول من الإعداد، وسيناريو حي يغيّر فيه نشرُ قاعدة السلوكَ. ولا تُنشر قاعدة بأثر لا يستهلكه مجالها.
- [ ] **2. Enforce يمنع فعلاً ويُغلق عند الفشل.** على الملف الشبيه بالإنتاج يرفض مجال بعيد (الإجازة أو العهدة) الطلب عند رفض HR.Core وعند تعذّر الوصول إليه، والدفتر يربط القرار بالكيان والإصدار والحقائق المنقّحة.
- [ ] **3. الإصدار ثابت ومؤرخ وقابل للتراجع بأربع عيون حقيقية.** لا يتغير صف في إصدار منشور بعد النشر (بصمة قبل/بعد إعادة التشغيل). النشر المستقبلي والتراجع يعملان. النشر بالمستخدم نفسه أو بقاعدة غير صالحة مرفوض في الإنتاج.
- [ ] **4. القيم القانونية متتبعة ومشحونة.** صور HR.Contracts وHR.Movements تحمل حزمة أنظمة الخدمة وتتحقق منها عند الإقلاع. عينة إجازة وفصل وترفيع وعقوبة تُتتبع إلى `legal_source_id` ومادة وإصدار نافذ يوم القرار.
- [ ] **5. الأدلة مطابقة للوثائق.** AC-09 وAC-10 (معاد كتابتهما) وكل `AC-POL-*` ناجحة بأدلة كاملة تحت `ACCEPTANCE_REQUIRE_ALL=true`. بطاقة PROD-001 والمصفوفة وSYR-001/003 تعكس الحالة الفعلية بلا تناقض.

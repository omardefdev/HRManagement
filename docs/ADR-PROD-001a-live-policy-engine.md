# ADR-PROD-001a — Live policy engine

## Status

Accepted — implements PROD-001a live policy engine with declarative rules, versioning, and enforcement ledger.

## Decision

- Central store in **HRCore** (`PolicyTemplate`, `PolicyTemplateVersion`, `PolicyRule`, `PolicyFactsSchema`, `PolicyEnforcementLedger`)
- Evaluation kernel in **SharedKernel** (`PolicyEvaluator`, condition groups AND/OR/NOT)
- Runtime API: `POST /api/policies/enforce`, `POST /api/policies/evaluate`
- Admin UI: `/PolicyTemplates`
- No arbitrary code in rules — closed effect enum only

## دورة الحياة (PLAN-47)

- **التأليف والنشر:** المسودة لسياسة `Policy.Author` (`admin` أو `hr-core` أو `policy-author`)، والنشر لسياسة `Policy.Approver` (`admin` أو `hr-management-approver` أو `policy-approver`)، والأدوار من الإعداد `PolicyEngine:Roles:*`. كل تعديل على المسودة يُسجَّل في `PolicyVersionEdits`، ولا ينشر أحد مسودة حرّرها. لا علم «Lab».
- **المسودة:** واحدة لكل قالب، تبدأ نسخة من الإصدار النافذ، وتُضاف قواعدها وتُعدَّل وتُحذف، وتُهمَل إلى `Archived`. لا يُنشر إصدار فارغ إلا بإجراء «سحب كل قواعد المجال» بسبب مكتوب.
- **التحقق:** `PolicyRuleValidator` على كل قاعدة جديدة أو معدّلة، وعلى الإصدار كله عند النشر، وعلى مجموعات تغيير البذور. القاعدة المخزنة التالفة لا تطابق شيئاً (`PolicyConditionGroup.Never`) وتُذكر في `InvalidRuleKeys`.
- **الثبات:** الإصدار المنشور لا يتغير إلا في `EffectiveTo`: معترِض EF، وtrigger في PostgreSQL يعمل تحت أمن الصفوف.
- **التأريخ:** `EffectiveFrom` مستقبلي على المسودة، ولا أثر رجعي (D-POL-10)، والتقييم «كما في تاريخ» داخل HR.Core بـ`AsOfUtc`، والنقطة الداخلية (PLAN-42) تتجاهله.
- **التراجع:** مسودة تراجع بسبب مكتوب، تُنشر بأربع عيون إصداراً جديداً `Rollback` مرتبطاً بالهدف.
- **مسار نشر واحد:** `PolicyPublishService` للواجهة وAPI والتراجع ومجموعات البذور (`SystemSeed`، D-POL-11). الحدث `IPolicyPublished` يُحفظ مع النشر في المعاملة نفسها (PLAN-40)، وكل نسخة من HR.Core تبطل كاشها عليه بطابور مؤقت خاص بها.
- **البذور:** `PolicySeedChangeSets` تُطبَّق مرة لكل قالب وتُسجَّل ببصمتها في `PolicySeedApplications`. تغيّر بصمة مجموعة مطبّقة يُفشل الإقلاع.
- **نقطة الامتداد:** `IPolicyDomainCapabilities` (يسمح بالكل افتراضياً؛ يملؤها PLAN-49 من كتالوج المجالات).

## Consequences

- Migration `PROD001ab_ComprehensivePolicyEngine` on `hr_core_db`
- Services integrate via `IPolicyEnforceClient` (ServiceDefaults)
- Shadow → Enforce rollout per domain via `PolicyEngine:{Domain}` config

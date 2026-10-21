# PLAN-04 — الرموز المتوازية + الهيكل التنظيمي المؤرخ (AC-13, AC-14)

**التغطية:** AC-13, AC-14. **الطبقة:** 2. **يعتمد على:** PLAN-00.

## الهدف

إثبات توليد مئات الرموز بالتوازي دون تكرار مع احترام (جهة/نوع/إصدار/عداد)، وإثبات الاستعلام عن الهيكل التنظيمي في تاريخ سابق (الرئيس والوحدة والوظيفة الممولة كما كانت).

## الوضع الحالي (منفّذ / فجوة)

- منفّذ (AC-13): [CodeGeneratorService.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs) يحجز التسلسل داخل معاملة `Serializable` مع فهرس فريد؛ معاينة/توليد في `CodeGenerationController`؛ عميل [CodeGenerationClient.cs](../../src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs).
- فجوة (AC-13): لا اختبار تزامن حقيقي (مئات متوازية) على PostgreSQL؛ لا بديل موحّد لـ`ICodeGenerationClient` في التكامل.
- فجوة (AC-14 — أكبر فجوة حوكمة): [OrgUnitVersion.cs](../../src/Services/HRCore/HR.Core.Domain/Entities/OrgUnitVersion.cs) مخطط فقط؛ `OrgUnitsController.Create/Edit` يعدّل `OrgUnit` مباشرة **ولا يكتب نسخة**؛ لا استعلام as-of؛ [FundedPosition.cs](../../src/Services/HRCore/HR.Core.Domain/Entities/FundedPosition.cs) ليست زمنية.

## تغييرات التنفيذ (مهام)

1. **AC-13:** اختبار تكامل يشغّل مئات `Task.WhenAll` على `/api/codes/generate` مقابل PostgreSQL ويثبت عدم التكرار والعداد الذري؛ بديل `FakeCodeGenerationClient` مشترك.
2. **AC-14:** كتابة `OrgUnitVersion` عند Create/Rename/Move/Merge/Split/Close في [OrgUnitsController.cs](../../src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs)؛ إضافة endpoint استعلام "كما في تاريخ" يعيد الرئيس والوحدة والوظيفة الممولة؛ إضافة نفاذ زمني لـ`FundedPosition` (أو لقطة).

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-13 | مئات الرموز بالتوازي لنوع واحد ⇒ رموز فريدة، عداد صحيح | لا رمز مكرر ولا كسر لنطاق (جهة/نوع/إصدار) |
| AC-14 | استعلام تاريخ سابق ⇒ الحالة كما كانت في ذلك التاريخ | تعديل لاحق لا يغيّر نتيجة استعلام التاريخ السابق |

## منهج الاختبار

مجموعة `Ac13..Ac14`؛ إعادة استخدام [CodeGenerationTests](../../src/Tests/HRManagement.UnitTests/) و[Org001DatedStructureTests](../../src/Tests/HRManagement.UnitTests/) و[docs/ORG_001_DATED_STRUCTURE.md](../ORG_001_DATED_STRUCTURE.md).

## مخرجات الأدلة

`acceptance-evidence/AC-13/`: استجابات التوليد المتوازي + `db-checks.json` لتفرد `CodeIssuance`. `acceptance-evidence/AC-14/`: استجابة استعلام as-of قبل/بعد تعديل لاحق.

## تعريف الإنجاز

لا رموز مكررة تحت الحمل؛ استعلام as-of يعمل بمسار كتابة حقيقي؛ AC-13/AC-14 = `Pass` (أو AC-14 يبقى محجوزاً إن تعذّر إنجاز المسار الزمني، مع توثيق واضح).

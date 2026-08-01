# استثناءات تغطية Coverlet (QA-002-D) — Allowlist موقّع

أي استثناء خارج هذا الملف على منطق أعمال حساس **مرفوض** في المراجعة.  
مرجع الإعداد: [`coverlet.runsettings`](../coverlet.runsettings).

## مسموح (توليد / ضوضاء)

| نمط | السبب |
|---|---|
| `**/Migrations/**/*.cs` | كود EF مولَّد؛ المنطق في Domain/Handlers |
| `**/*.Designer.cs` | مصمّم مولَّد |
| `**/obj/**`, `**/bin/**` | مخرجات بناء |
| سمات `Obsolete`, `GeneratedCode`, `CompilerGenerated` | إطار العمل |
| تجميعات الاختبارات وMoq/xunit/coverlet/Testcontainers/MassTransit | ليست منتجاً |

## بوابة المخاطر (ليست استثناء Coverlet)

التقرير يجمع نطاقاً واسعاً؛ **العتبة 60%** تُطبَّق على `gateAssemblies` في [`coverage-floors.json`](../coverage-floors.json).  
Assemblies خارج البوابة تبقى مرئية في التقرير ولها floors عند الظهور.

## `[ExcludeFromCodeCoverage]`

| القاعدة | التفاصيل |
|---|---|
| ممنوع على محركات الرواتب / انتقالات الحالة / SOD / idempotency | إلا بصف هنا + موافقة مالك المجال |
| مسموح لـ DTOs فارغة / wrappers توليد | مع تعليق يشير لهذا الملف |

### Allowlist سمات (حالي)

لا صفوف أعمال حساسة مستثناة بالسمة حتى تاريخ QA-002. أضف صفاً قبل الدمج:

| النوع / العضو | السبب | المالك | التاريخ |
|---|---|---|---|
| — | — | — | — |

## Ratchet

تخفيض عتبة أو floor أو توسيع استثناء Migrations لمنطق يدوي = تحديث [ADR-QA-002](./ADR-QA-002-risk-based-coverage.md).

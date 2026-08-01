# وظائف التصدير الخلفية (PERF-004)

## الدورة

1. HTTP: إن تجاوز العدد `MaxExportRows` (10000) أو `async=true` → إنشاء صف `ExportJobs` + نشر `IExportJobRequested` → **202 Accepted** مع `jobId`.
2. Consumer (MassTransit) في الخدمة المالكة يعالج المهمة (claim/lease).
3. الكتابة **مقطّعة** (`ChunkSize`، افتراضي 500) إلى ملف مؤقت ثم `IExportArtifactStore` (محلي تحت `export-artifacts/`).
4. المستخدم يستعلم `GET /export-jobs/{id}` ثم `GET /export-jobs/{id}/download`.
5. التحميل يعيد التحقق من `AuthSnapshot` (نفس المستخدم + tenant) ثم يبث الملف. صلاحية النتيجة: `ResultRetentionHours` (افتراضي 24 ساعة).

التصديرات الصغيرة تبقى متزامنة كما في PERF-002.

## الخدمات

| ServiceKey | أنواع |
|---|---|
| `payroll` | `payroll.run.csv`, `payroll.eos.csv`, `payroll.register.csv`, `payroll.bulk.payslips` |
| `hr-core` | `hrcore.employees.csv` |
| `hr-contracts` | leave/advance CSV |
| `compliance` | complaints CSV |

قوالب: `TemplateCode` + `TemplateVersion` (`1`) على الصف عند الإنشاء.

## الإلغاء

`POST /export-jobs/{id}/cancel` — يضع الحالة `Cancelled`؛ الحلقة تتحقق بين الدفعات.

## الذاكرة

لا يُبنى التقرير الكامل كـ`byte[]` واحد في مسار الخلفية؛ الدفعة تُكتب سطراً بسطر عبر `StreamWriter`. ZIP القسائم المتزامن ما زال محدوداً بـMaxExportRows أو يُحوَّل لوظيفة خلفية (فهرس CSV).

## الإعداد

```json
"ExportJobs": {
  "ChunkSize": 500,
  "ResultRetentionHours": 24,
  "DownloadPresignMinutes": 15,
  "LeaseSeconds": 120
}
```

## علاقة PERF-002

المتزامن فوق السقف كان 400؛ PERF-004 يستبدله بـ202 + تتبع. رسالة PERF-002 ما زالت صالحة للمسارات غير الموصولة بعد.

## المقياس الأفقي (OPS-001)

HPA قد يزيد عدد pods التي تشغّل consumers. **claim/lease** على صف `ExportJobs` يمنع معالجة مزدوجة لنفس المهمة — لا تُطبَّق HPA على Jobs لمرة واحدة (migrate). انظر [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md).

## الاختبارات

`Perf004ExportJobTests` — إكمال CSV، رفض تحميل مستخدم آخر، إلغاء، عزل tenant في Auth.

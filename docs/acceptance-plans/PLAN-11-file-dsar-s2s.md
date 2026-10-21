# PLAN-11 — DSAR + فحص الملفات + منح الوصول + ثقة الخدمات (AC-27..AC-30)

**التغطية:** AC-27, AC-28, AC-29, AC-30. **الطبقة:** 4. **يعتمد على:** PLAN-00.

## الهدف

إثبات جمع بيانات DSAR المسموح بها مع توثيق المستخرج والطالب، وحجر الملف الضار والسماح بالسليم بعد الفحص، ومنع الرابط المنتهي/من مستخدم آخر مع تسجيل الحدث، ورفض الاستدعاء بهوية خدمة أو Audience غير صحيح دون تنفيذ العملية.

## الوضع الحالي (منفّذ / فجوة)

- AC-27: [DsarRequestsController.cs](../../src/Services/Compliance/Compliance.Web/Controllers/DsarRequestsController.cs) + `DsarRequest`؛ `IDsarService`/`DsarService` (SharedKernel) لكن الجامع **وهمي** (in-memory)، ونموذجا DSAR مزدوجان. اختبارات `Prv002DsarTests`, `Gov001DsarWorkflowTests`.
- AC-28: فحص fail-closed عبر `FileScanProcessorJob` + `ClamAvMalwareScanner`/`EicarAwareScanner`؛ حالات `PendingScan/Clean/Infected/Quarantined/ScanError`. اختبارات `File002ScanningTests`, `Qa003MinioFileScanTests`. فجوة: ClamAV+MinIO حيّان غير مثبتين؛ CDR مؤسسي مؤجل.
- AC-29: [FileAccessGrant.cs](../../src/Shared/HRManagement.SharedKernel/Files/FileAccessGrant.cs) + [IFileAccessPolicy](../../src/Shared/HRManagement.ServiceDefaults/Files/IFileAccessPolicy.cs) + سقف رابط ≤300ث + `FileAccessAuditLog`. فجوة: لا `ExpiresAt` للمنحة (فقط TTL للرابط)؛ انحراف إعداد `PresignedUrlExpiryMinutes:60` مقابل السقف 300؛ لا اختبار رابط منتهٍ/من مستخدم آخر حياً.
- AC-30: [docs/SERVICE_TRUST_INVENTORY.md](../SERVICE_TRUST_INVENTORY.md)؛ اختبارات `Iam001NegativeTokenTests`, `ServiceToServiceNegativeMatrixTests`, `Qa003KeycloakTokenMatrixTests`. فجوة: لا إثبات حي أن المعالج **لا يُنفَّذ** عند جمهور/هوية خاطئة.

## تغييرات التنفيذ (مهام)

1. **AC-27:** استبدال الجامع الوهمي بجمع فعلي للبيانات المسموح بها عبر الخدمات مع توثيق المستخرج والطالب؛ توحيد نموذجي DSAR.
2. **AC-28:** رفع ملف سليم وآخر ضار (EICAR) عبر API مع ClamAV+MinIO حيّين؛ إثبات الحجر والترقية بعد النظافة والرفض للضار.
3. **AC-29:** إضافة انتهاء فعلي للمنحة (`ExpiresAt`) وتصحيح انحراف الإعداد؛ اختبار رابط منتهٍ/من مستخدم آخر ⇒ منع + سجل.
4. **AC-30:** استدعاء حي بجمهور/هوية خدمة خاطئة ⇒ رفض + إثبات عدم تنفيذ العملية (لا أثر جانبي).

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-27 | DSAR ⇒ جمع البيانات المسموح بها + توثيق المستخرج والطالب | لا يجمع بيانات خارج النطاق المسموح |
| AC-28 | ملف سليم يُرقَّى بعد الفحص | ملف ضار يبقى محجوراً ويُرفض تنزيله |
| AC-29 | رابط صالح لمستخدم مخوّل يعمل | رابط منتهٍ أو من مستخدم آخر ⇒ منع + سجل |
| AC-30 | استدعاء بهوية/جمهور صحيح ينفّذ | جمهور/هوية خاطئة ⇒ رفض ولا تُنفَّذ العملية |

## منهج الاختبار

مجموعة `Ac27..Ac30`؛ إعادة استخدام `File002ScanningTests`, `Sec003FileAccessIntegrationTests`, `Iam001NegativeTokenTests`, `ServiceToServiceNegativeMatrixTests` على الحزمة الحية (MinIO+ClamAV+Keycloak).

## مخرجات الأدلة

`AC-27/`: حزمة الاستخراج + سجل الطالب. `AC-28/`: حالة الفحص قبل/بعد للملفين. `AC-29/`: محاولة رابط منتهٍ/غريب + سجل الوصول. `AC-30/`: استجابة الرفض + `after.json` يثبت عدم التنفيذ.

## تعريف الإنجاز

DSAR يجمع المسموح ويوثّق؛ الفحص fail-closed حي؛ الروابط المنتهية/الغريبة ممنوعة ومسجّلة؛ الهوية/الجمهور الخاطئ يُرفض بلا تنفيذ؛ AC-27..30 = `Pass`. (CDR/ClamAV مؤسسي مصدّق يبقى Deferred — CAP-FILE-01.)

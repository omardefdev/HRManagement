# ADR-FUN-004 — إغلاق الصحة والتدريب والالتزام

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | FUN-004 (البطاقة 53) |
| التاريخ | 2026-07-29 |
| الاعتماديات | TEN-003 Done، FILE-002 Done؛ GOV-001 (`ret-medical`) مستهلك |

## السياق

وحدات HR.Health / HR.Training / Compliance موجودة كـCRUD وأحداث، لكن بلا least-access طبي، بلا موافقة، بلا دورة تأمين، بلا تحقق شهادة، بلا حماية مبلغ عنه، وحذف صلب يخالف الاحتفاظ بالتاريخ. UAT مؤجّل.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/fun-htc/journey-matrix.json` — Health / Training / Compliance |
| Orchestration | workflow rules + أحداث قائمة — **لا** Automatonymous saga جديد |
| Least-access | سياسة `HrHealthClinical` (دور `hr-health-clinical`) للحقول السريرية؛ HR العام Forbid/ملخص غير سريري |
| Consent | حقول على `MedicalRecord`؛ رفض توصية/قرار بلا موافقة سارية (أو EmergencyOverride + audit) |
| Insurance | حالات `Draft|Active|Suspended|Expired|Revoked` + soft-cancel |
| Training | `TrainingChecklist` + API تحقق `CertificateNo` |
| Compliance | `IsAnonymous` + إخفاء المبلغ عنه؛ `InvestigationChecklist`؛ `CanIssue` على التأديب |
| FILE-002 | مرفقات أدلة/شهادات عبر `IFileReferenceClient` |
| Retention | soft-cancel؛ بوابة purge تحترم `ret-medical` (لا job وطني كامل) |
| UAT | ReadyForSign-off (`FUN_004_UAT_PACK.md`) |
| خارج النطاق | FUN-005/006؛ wizards من الصفر؛ saga صحة/تدريب؛ منصة إشعارات وطنية |

## العواقب

- السجل الطبي السريري لا يُعرض لأدوار HR العامة.
- الشكوى تحمي هوية المبلغ عنه عن غير المحققين.
- التأديب لا يُصدر خارج workflow.
- الشهادة قابلة للتحقق برقم دون بيانات طبية.

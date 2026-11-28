# FUN-002 — مصفوفة رحلة الفصل والتسوية

المصدر الآلي: [`fun-separation/journey-matrix.json`](./fun-separation/journey-matrix.json) · ADR: [`ADR-FUN-002-separation-journey-closure.md`](./ADR-FUN-002-separation-journey-closure.md)

**PLAN-60:** لكل بند نمط إكمال. **يدوي (Manual):** يكمله HR بدليل (مرجع ≥ 5 أحرف أو ملف) قبل التنفيذ، أو يُستثنى بطلبين من شخصين.
**آلي (System):** تكمله الخدمة نفسها — القرار المرقّم عند إصداره، وإنهاء العقد عند التنفيذ. **إقرار الطرف (PartyAck):** يكمله إقرار
الطرف بعد التنفيذ، ولا يُكمَل يدوياً ولا يُستثنى من HR.Contracts. الاختبار `Fun002JourneyMatrixTests` يطابق هذا الجدول مع
`SeparationChecklistDefaults` في الاتجاهين.

| خطوة | SeparationStep | النمط | الطرف | إلزامي | مالك | SLA (س) | دليل | خدمة |
|---|---|---|---|---|---|---:|---|---|
| SeparationDecision | DecisionIssued | آلي | — | نعم | hr-contracts | 24 | رقم القرار `DEC-SEP-YYYY-#####` | HR.Contracts |
| EmployeeNotified | EmployeeNotified | يدوي | — | نعم* | hr-contracts | 48 | مرجع أو ملف | HR.Contracts |
| ContractTerminated | ContractTerminated | آلي (عند التنفيذ) | — | نعم | hr-contracts | 48 | عقد منتهٍ بتاريخ النفاذ | HR.Contracts |
| PayrollSettled | PayrollSettled | إقرار الطرف | Payroll | نعم | payroll | 72 | EOS Settlement Id | Payroll |
| CardRevoked | CardRevoked | إقرار الطرف | IDCard | نعم | idcard | 48 | party ACK | IDCard |
| AccountDisabled | AccountDisabled | إقرار الطرف | Auth.Directory | نعم | auth-directory | 24 | AuthUser معطّل | Auth.Directory |
| AssetsReturned | AssetsReturned | إقرار الطرف | Admin.Logistics | نعم | admin-logistics | 72 | party ACK | Admin.Logistics |
| ITAccessRevoked | ItAccessRevoked | إقرار الطرف | IT.Infra | نعم | it-infra | 48 | party ACK | IT.Infra |
| HrCoreUpdated | HrCoreUpdated | إقرار الطرف | HR.Core | نعم | hr-core | 24 | party ACK | HR.Core |
| Archived | Archived | إقرار الطرف | Records.Archive | نعم | records-archive | 24 | party ACK | Records.Archive |
| SeparationCompleted | — | — | — | نعم | hr-contracts | 1 | ISeparationCompleted | SeparationSaga |

\* `Separation:Checklist:RequireEmployeeNotification=false` يجعل التبليغ غير إلزامي في السجلات الجديدة.

**أطراف الـsaga الإلزامية:** Payroll, HR.Core, IDCard, Auth.Directory, Admin.Logistics, IT.Infra, Records.Archive — وهي أطراف بنود
«إقرار الطرف» السبعة.

**قاعدة:** لا تنفيذ قبل اعتماد السجل في محرك سير العمل، وقرار مرقّم، وتبليغ الموظف (أو استثنائه باعتماد ثانٍ)، وبلوغ تاريخ النفاذ بتوقيت
دمشق. لا يُعلَن `Completed` قبل إقرار كل الأطراف الإلزامية. الإلغاء من «مسودة» و«بانتظار الاعتماد» و«معتمد» فقط، بسبب إلزامي؛ إلغاء ما
بعد التنفيذ مسار التعويض في PLAN-63.

# FUN-002 — مصفوفة رحلة الفصل والتسوية

المصدر الآلي: [`fun-separation/journey-matrix.json`](./fun-separation/journey-matrix.json) · ADR: [`ADR-FUN-002-separation-journey-closure.md`](./ADR-FUN-002-separation-journey-closure.md)

| خطوة | SeparationStep | إلزامي | مالك | SLA (س) | دليل | خدمة |
|---|---|---|---|---:|---|---|
| SeparationDecision | — | نعم | hr-contracts | 24 | SeparationRecord | HR.Contracts |
| ContractTerminated | ContractTerminated | نعم | hr-contracts | 48 | عقد منتهٍ | HR.Contracts |
| PayrollSettled | PayrollSettled | نعم | payroll | 72 | EOS Settlement Id | Payroll |
| CardRevoked | CardRevoked | نعم | idcard | 48 | party ACK | IDCard |
| AccountDisabled | AccountDisabled | نعم | auth-directory | 24 | AuthUser معطّل | Auth.Directory |
| AssetsReturned | AssetsReturned | نعم | admin-logistics | 72 | party ACK | Admin.Logistics |
| ITAccessRevoked | — | نعم | it-infra | 48 | party ACK | IT.Infra |
| Archived | Archived | نعم | records-archive | 24 | party ACK | Records.Archive |
| SeparationCompleted | — | نعم | hr-contracts | 1 | ISeparationCompleted | SeparationSaga |

**أطراف الـsaga الإلزامية:** Payroll, HR.Core, IDCard, Auth.Directory, Admin.Logistics, IT.Infra, Records.Archive.

**قاعدة:** لا إعلان اكتمال منصة قبل ACK كل الأطراف. Cancel قبل النفاذ = soft-cancel + Compensate خفيف.

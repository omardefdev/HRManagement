# مصفوفة رحلة التعيين (FUN-001)

مرجع الآلة: [`fun-hire/journey-matrix.json`](./fun-hire/journey-matrix.json) — ADR: [ADR-FUN-001-hire-journey-closure.md](./ADR-FUN-001-hire-journey-closure.md).

| الخطوة | HireStep | إلزامي | المالك | SLA (س) | الدليل |
|---|---|---|---|---:|---|
| CandidateAccepted | CandidateReceived | نعم | hr-recruiter | 24 | ICandidateAccepted |
| EmployeeEnsure | PersonalFileOpened | نعم | hr-core | 8 | EmployeeId |
| ContractAppointment | ContractClassified | نعم | hr-contracts | 48 | ContractId |
| PayProfile | PayrollLinked | نعم | payroll | 24 | party ACK |
| DirectoryAccount | — (saga) | نعم | auth-directory | 8 | AuthUser |
| ITProvision | — (saga) | نعم | it-infra | 72 | party ACK |
| IdCard | IdentityIssued | نعم | idcard | 72 | ICardIssued |
| CareerRecord | CareerRecordApproved | لا | hr-core | 120 | notes |
| ArchiveDiwan | HireArchived | نعم | records-archive | 24 | party / IHireCaseCompleted |
| HireCaseCompleted | — | نعم | hr-contracts | 1 | IHireCaseCompleted |

**قاعدة:** لا إعلان اكتمال منصة قبل ACK كل الأطراف الإلزامية في الـsaga بما فيها Auth.Directory.

# قائمة مصالحة FUN-004 (صحة / تدريب / التزام)

| # | فحص | أين |
|---|---|---|
| 1 | MedicalRecord: Consent ساري؟ IsCancelled؟ | HR.Health |
| 2 | قراءة سريرية: دور `hr-health-clinical` + SensitiveDataAudit | Auth / audit |
| 3 | InsuranceCard.Status دورة حياة | HR.Health |
| 4 | TrainingChecklist خطوات + CertificateNo | HR.Training |
| 5 | Verify API لشهادة | `/api/certificates/verify` |
| 6 | Complaint IsAnonymous؛ AssignedTo ≠ ReporterProtectedId | Compliance |
| 7 | InvestigationChecklist Evidence FILE-002 | Compliance |
| 8 | DisciplinaryAction CanIssue + EvidenceFileId | Compliance |
| 9 | لا hard delete لسجلات طبية/تأمين/شكاوى | Controllers |

## حدود

لا saga Automatonymous؛ لا reverse رواتب داخل تأديب؛ احتفاظ `ret-medical` بوابة purge فقط.

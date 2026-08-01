# FUN-004 — مصفوفة رحلات الصحة والتدريب والالتزام

المصدر: [`fun-htc/journey-matrix.json`](fun-htc/journey-matrix.json) — انظر [ADR-FUN-004](ADR-FUN-004-health-training-compliance-closure.md).

## Health

| خطوة | إلزامي | مالك | SLA (س) | واجهة / حدث |
|---|---|---|---|---|
| ConsentRecorded | نعم | hr-health-clinical | 24 | `MedicalRecords` consent fields |
| MedicalRecord | نعم | hr-health-clinical | 48 | `MedicalRecordsController` |
| Recommendation | لا | hr-health-clinical | 72 | `IMedicalRecommendationIssued` |
| CommitteeDecision | لا | hr-health-clinical | 120 | `IMedicalDecisionIssued` |
| InsuranceActive | نعم | hr-health | 72 | `InsuranceCards` Activate |
| InsuranceEnded | لا | hr-health | 24 | Expire / Revoke |

## Training

| خطوة | إلزامي | مالك | SLA | واجهة / حدث |
|---|---|---|---|---|
| Enrolled | نعم | hr-training | 24 | `TrainingEnrollments` / ESS Enroll |
| Completed | نعم | hr-training | 720 | `ITrainingCompleted` |
| CertificateIssued | نعم | hr-training | 48 | `LearningCertificate` / `CertificateFileId` |
| CertificateVerified | لا | hr-training | 24 | `CertificatesController` Verify |

## Compliance

| خطوة | إلزامي | مالك | SLA | واجهة / حدث |
|---|---|---|---|---|
| ComplaintFiled | نعم | compliance | 24 | `ComplaintsController` (+ anonymous) |
| Assigned | نعم | compliance | 48 | Assign (SoD ≠ reporter) |
| EvidenceCollected | نعم | compliance | 168 | FILE-002 evidence |
| Resolved | نعم | compliance | 72 | `IComplaintResolved` |
| DisciplineIssued | لا | compliance | 72 | `CanIssue` + `IDisciplinaryActionTaken` |
| Closed | نعم | compliance | 24 | Close |

لا saga Automatonymous جديد — orchestration عبر قواعد + أحداث قائمة.

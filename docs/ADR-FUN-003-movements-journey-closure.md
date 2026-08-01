# ADR-FUN-003 — إغلاق النقل والانتداب والإعارة

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | FUN-003 (البطاقة 52) |
| التاريخ | 2026-07-29 |
| الاعتماديات | INT-003 Done، IAM-003 Done |

## السياق

سagas النقل والانتداب موجودة (INT-003) لكن بلا Cancel/Compensate، والصلاحية على Details/Execute OR وليست AND، والـchecklist بلا حوكمة، والإعارة حدث مباشر بلا بوابة، وUAT مؤجّل.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/fun-movements/journey-matrix.json` |
| MovementChecklist | OwnerRole / SlaHours / EvidenceRef / CompletedBy لـ Transfer\|Secondment\|Loan |
| صلاحية | مصدر **و** هدف (AND) على كل مسار مطفر |
| SoD | Transfer + Secondment + Loan Approve (actor من token؛ لا self-approve) |
| Cancel | soft-cancel؛ `ICancelTransferSaga` / `ICancelSecondmentSaga` → Compensate |
| Loan | event-hardened (لا Automatonymous جديد)؛ checklist + SoD + `ILoanAssignmentEnded` عند Close |
| تاريخ نفاذ | Transfer.EffectiveDate؛ لا saga قبل التاريخ ≤ اليوم |
| UAT | ReadyForSign-off (`FUN_003_UAT_PACK.md`) |
| خارج النطاق | FUN-004/005/006؛ wizards من الصفر؛ Loan saga كامل |

## العواقب

- لا Execute/Activate قبل checklist إلزامي بدليل.
- إلغاء قبل النفاذ يعوّض أطرافاً مكتملة (حدود runbook).
- تقارير الجهة تعتمد على أحداث صحيحة (HostOrg/EndDate من الكيان).

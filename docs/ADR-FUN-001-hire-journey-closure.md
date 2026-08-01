# ADR-FUN-001 — إغلاق رحلة التعيين من طرف إلى طرف

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | FUN-001 (البطاقة 50) |
| التاريخ | 2026-07-28 |
| الاعتماديات | INT-003 Done، QA-004 Done |

## السياق

مسار التعيين مبني (Wizard + checklist + HireCaseStateMachine) لكن Directory خارج بوابة الإكمال، والـchecklist بلا مالك/SLA/دليل، والتعويض ضعيف، وUAT موقّع مؤجّل من QA-004.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/fun-hire/journey-matrix.json` |
| Auth.Directory | طرف إلزامي في `Sagas:Hire:MandatoryParties` |
| HireChecklist | OwnerRole / SlaHours / EvidenceRef / CompletedBy |
| Cancel | `ICancelHireSaga` → Compensate=true للأطراف المكتملة |
| UAT | حزمة ReadyForSign-off (`FUN_001_UAT_PACK.md`) |
| خارج النطاق | FUN-002، ONB-001، HCM-001، FUN-006 |

## العواقب

- لا `IHireCaseCompleted` قبل ACK Directory.
- إكمال خطوة يدوي يتطلب EvidenceRef للخطوات الإلزامية الحساسة.
- Cancel من ProcessInterventions يعوّض أطرافاً مكتملة جزئياً (حدود موثّقة في runbook).

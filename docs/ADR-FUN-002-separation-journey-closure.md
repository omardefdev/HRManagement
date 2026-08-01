# ADR-FUN-002 — إغلاق رحلة الفصل والتسوية

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | FUN-002 (البطاقة 51) |
| التاريخ | 2026-07-29 |
| الاعتماديات | INT-003 Done، PAY-003 Done |

## السياق

مسار الفصل مبني (Wizard + checklist DOM-001 + SeparationStateMachine) لكن Checklist بلا Owner/SLA، وCancel يحذف السجل، وتعويض الأطراف سطحي، وسيناريوهات التاريخ المستقبلي / legal hold / إعادة التوظيف بلا حزمة إغلاق، وUAT موقّع مؤجّل من QA-004.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/fun-separation/journey-matrix.json` |
| SeparationChecklist | OwnerRole / SlaHours / CompletedByName (+ EvidenceRef القائم) |
| Cancel | soft-cancel (IsCancelled) — **لا حذف**؛ مع `ICancelSeparationSaga` إن نُشرت الـsaga |
| Compensate | `SeparationPartyAckConsumerBase.CompensateAsync`؛ حدود خفيفة (مسودة EOS فقط — ليس PAY-003 Reverse كامل) |
| تاريخ مستقبلي | إنشاء مسموح؛ Publish مرفوض حتى `EffectiveDate <= اليوم` |
| Legal hold | رفض Publish إن الموظف تحت LegalHold |
| UAT | ReadyForSign-off (`FUN_002_UAT_PACK.md`) |
| خارج النطاق | FUN-003، FUN-005 عمق أصول/بطاقة، FUN-006، بناء wizard من الصفر |

## العواقب

- لا `ISeparationCompleted` قبل ACK كل الأطراف الإلزامية.
- الإلغاء قبل النفاذ يعوّض أطرافاً مكتملة جزئياً (runbook).
- سجل الفصل يبقى للتدقيق بعد الإلغاء.
- تعيين لاحق (FUN-001) لا يحذف تاريخ الفصل.

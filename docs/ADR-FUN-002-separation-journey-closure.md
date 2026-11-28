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

## ملحق PLAN-60 — القرار والاعتماد (2026-11-28)

- **الحالة على الخادم:** `Draft` ← `PendingApproval` ← `Approved` ← `Executing` ← `Completed`، مع `Rejected` و`AwaitingIntervention` و`Cancelled`
  (`SeparationWorkflowRules.Transition` هو الموضع الوحيد الذي تتغير فيه الحالة؛ `SeparationEventPublished` مشتق: صحيح من «قيد التنفيذ» فما بعد).
- **الاعتماد في HR.Core وحده:** `SEPARATION-APPROVAL` (HR ثم صاحب الصلاحية) و`SEPARATION-APPROVAL-SENSITIVE` للتأديبي والقضائي (HR ثم الامتثال
  ثم صاحب الصلاحية)، باعتماد أشخاص مختلفين، ومنشئ السجل ومقدّمه مستبعدان من كل خطوة. لا زر «اعتماد» في HR.Contracts. النتيجة تصل بحدث
  `IWorkflowOutcomeDecided` عبر Outbox، وتطبّقها `ApplyOutcomeAsync` نفسها للمستهلك والمصالحة والسحب.
- **القرار الإداري:** يصدر لسجل معتمد فقط، مرة واحدة، برقم `DEC-SEP-YYYY-#####` من خدمة الترميز، ومُصدره غير المنشئ، ويكمل بند «إصدار القرار».
  لا يُعدَّل ولا يُحذف بعد ربطه بسجل فصل. وثيقة PDF للقرار من الموجة الثانية (G-SEP-16).
- **التنفيذ:** فقط لسجل معتمد له قرار، وبعد تبليغ الموظف (أو استثنائه باعتماد شخص ثانٍ)، وبعد بلوغ تاريخ النفاذ بتوقيت دمشق، ودون حجز قانوني،
  وبنظام خدمة صالح. ينتهي العقد بتاريخ النفاذ، وتُحفظ الحالة ورسالة `IStartSeparationSaga` في وحدة Outbox واحدة. لا مسار آخر يبدأ الـsaga.


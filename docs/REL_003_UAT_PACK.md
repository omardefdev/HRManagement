# REL-003 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | قراءة ADR + catalog journeys | أربع رحلات موجودة | |
| U2 | كل خدمة: owner + runbook + escalation + tier + sloRef | Rel003* يمر | |
| U3 | Severity Sev1–Sev4 + MTTA/MTTR | جدول منشور | |
| U4 | Incident → command → resolve | عملية موثّقة + OPS IC لـSev1 موقع | |
| U5 | Change Normal مع rollback DEP-001 | checklist كاملة | |
| U6 | تشغيل rel003-incident-game-day | PASS + ledger | |
| U7 | Access review CSV headers | قابل للملء | |
| U8 | Postmortem actions Open/Done | قالب + CSV | |
| U9 | Readiness blockedOn TRN/SUP/SRE | لا إغلاق وهمي | |
| U10 | OBS escalation يشير لـREL-003 | روابط محدّثة | |

**الحالة:** ReadyForSign-off — التوقيع التشغيلي الداخلي؛ المكتب الوطني لاحقاً SUP-002.

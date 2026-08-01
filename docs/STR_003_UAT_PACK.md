# STR-003 — حزمة UAT (ReadyForSign-off)

توقيعات: Product Management + Program.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + journeys | أربع رحلات | |
| U2 | 14 slug مطابقة Gateway/Pilot | موجودة في الكتالوج | |
| U3 | Ready ⇒ ملفات uat/runbook/slo | validate PASS | |
| U4 | opsCatalogId ∈ REL-003 | انضمام صحيح | |
| U5 | Agency contract Included/Excluded | walkthrough | |
| U6 | Feature flag gate | Pilot Ready فقط | |
| U7 | REL_001 يشير لـSTR-003 | رابط | |
| U8 | لا «متاحة» لـNotReady | EXCEPTION | |
| U9 | Str003* unit | نجاح | |
| U10 | منصة postgres/rabbitmq غير UI جهة | serviceTier=platform | |

**الحالة:** ReadyForSign-off — كتالوج وحدات منتج؛ ONB/تراخيص لاحقاً.

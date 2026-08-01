# STR-005 — حزمة UAT (ReadyForSign-off)

توقيعات: Program Sponsor + Architecture.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + soft blockers | SYD-017 Blocked؛ PGM-001 NotStarted | |
| U2 | decisionRights لكل نوع حرج | owner ≠ reviewer | |
| U3 | خمس مجالس + نصاب | Architecture/Security/Data/Change/Program | |
| U4 | decision-log + risk-register | روابط صحيحة | |
| U5 | محاكاة قانون | LawChange + استثناء + risk | |
| U6 | محاكاة حادث | Security/Change + expiry | |
| U7 | محاكاة تأخير | رفض eng-only؛ قبول Program+Sponsor | |
| U8 | Go-Live eng-only | FAIL | |
| U9 | Go-Live multi-seat | PASS | |
| U10 | str005-governance-validate | PASS | |
| U11 | Str005* unit | نجاح | |
| U12 | لا ادعاء أسماء SYD-017 / جدول PGM-001 | EXCEPTION / READINESS | |

**الحالة:** ReadyForSign-off — governance gate pack؛ أسماء الموقعين = SYD-017 لاحقاً.

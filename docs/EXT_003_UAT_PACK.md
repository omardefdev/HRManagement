# EXT-003 — حزمة UAT (ReadyForSign-off)

توقيعات: Payroll + Finance Integration.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | PAY-003/IAM-003/INT-003 Satisfied؛ EXT-004 NotStarted؛ live Blocked | |
| U2 | unapproved run order | FAIL | |
| U3 | duplicate submit | FAIL | |
| U4 | totals mismatch / recon match | FAIL / PASS | |
| U5 | partial reject correction | PASS | |
| U6 | manual unaudited file | FAIL | |
| U7 | out-of-order / delay ack / parallel | PASS | |
| U8 | cutoff breach | FAIL | |
| U9 | payment-order audit | PASS | |
| U10 | ext003-treasury-payment-validate | PASS | |
| U11 | Ext003* unit | نجاح | |

**الحالة:** ReadyForSign-off — treasury/payment channel gate pack.

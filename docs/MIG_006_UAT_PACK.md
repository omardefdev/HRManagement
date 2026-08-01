# MIG-006 — حزمة UAT (ReadyForSign-off)

توقيعات: Cutover Command + Internal Audit.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | MIG-005/OPS-003/REL-001 Satisfied؛ soft Blocked | |
| U2 | timed rehearsals ≥3 | PASS | |
| U3 | delta capture / loss | PASS / FAIL | |
| U4 | surprise vs paper rollback | PASS / FAIL | |
| U5 | Go/No-Go | PASS مع معايير | |
| U6 | legacy read-only | PASS | |
| U7 | recon T+0/T+1/T+30 | PASS | |
| U8 | REL-002 join note | lab Satisfied؛ live Blocked | |
| U9 | mig006-cutover-validate | PASS | |
| U10 | Mig006* unit | نجاح | |

**الحالة:** ReadyForSign-off — cutover/rehearsal gate pack.

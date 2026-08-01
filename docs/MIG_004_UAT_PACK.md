# MIG-004 — حزمة UAT (ReadyForSign-off)

توقيعات: Data Quality + Privacy.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | MIG-002/DATA-002/GOV-001/GOV-003-merge Satisfied؛ soft Blocked/NotStarted | |
| U2 | strategies + exceptions | 4 strategies؛ auto ممنوع على exceptions | |
| U3 | threshold sign-off | PASS؛ أدوار متمايزة | |
| U4 | high-risk auto-merge | FAIL | |
| U5 | merge/unmerge roundtrip | PASS؛ reversible | |
| U6 | cross-tenant review | FAIL | |
| U7 | labelled ≥20 + P/R lab | PASS مقابل targets | |
| U8 | adversarial twins/variants | PASS؛ لا AutoMerge | |
| U9 | mig004-match-validate | PASS | |
| U10 | Mig004* unit | نجاح | |

**الحالة:** ReadyForSign-off — identity match/merge gate pack.

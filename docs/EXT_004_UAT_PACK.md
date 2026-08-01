# EXT-004 — حزمة UAT (ReadyForSign-off)

توقيعات: PKI + Legal + Security.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | STR-002/OBS-003 Satisfied؛ SOV-003 Satisfied (binding)؛ hsm-prod Blocked | |
| U2 | missing signer role | FAIL | |
| U3 | revoked cert / expired without LTV | FAIL | |
| U4 | expired with LTV | PASS | |
| U5 | clock skew / key exfil / deprecated alg | FAIL | |
| U6 | TSA outage degrade / archive restore | PASS | |
| U7 | form signature as legal | FAIL | |
| U8 | signature audit | PASS | |
| U9 | ext004-pki-signature-validate | PASS | |
| U10 | Ext004* unit | نجاح | |

**الحالة:** ReadyForSign-off — PKI/signature contract gate pack.

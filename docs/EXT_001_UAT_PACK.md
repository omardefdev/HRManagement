# EXT-001 — حزمة UAT (ReadyForSign-off)

توقيعات: National IAM + Security Architecture.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | IAM-003/004/STR-002 Satisfied؛ IAM-001 + live soft Blocked | |
| U2 | assurance sign-off | PASS؛ أدوار متمايزة | |
| U3 | national + agency IdP interop lab | PASS؛ live=false | |
| U4 | name-only link | FAIL | |
| U5 | revocation SLA pass/breach | PASS / FAIL | |
| U6 | credential loss + IdP outage | PASS | |
| U7 | fraud cases | PASS؛ block+review | |
| U8 | ext001-federation-validate | PASS | |
| U9 | Ext001* unit | نجاح | |

**الحالة:** ReadyForSign-off — federation/assurance gate pack.

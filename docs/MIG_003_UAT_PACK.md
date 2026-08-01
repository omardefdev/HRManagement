# MIG-003 — حزمة UAT (ReadyForSign-off)

توقيعات: Migration Engineering + Security + QA.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | MIG-002/TEN-002/OPS-005 Satisfied؛ soft Blocked | |
| U2 | stages + staging zones | encrypted + tenantScoped | |
| U3 | same-batch reload | PASS؛ duplicates=0 | |
| U4 | mid-fail resume | PASS؛ no re-extract | |
| U5 | corrupt schema | FAIL؛ quarantine؛ no silent commit | |
| U6 | lineage sample | ≥5 records؛ الحقول المطلوبة | |
| U7 | log redaction | PASS؛ لا PII | |
| U8 | cutover budget | PASS؛ liveProof=false | |
| U9 | mig003-factory-validate | PASS | |
| U10 | Mig003* unit | نجاح | |

**الحالة:** ReadyForSign-off — hybrid factory gate pack.

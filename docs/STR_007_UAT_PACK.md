# STR-007 — حزمة UAT (ReadyForSign-off)

توقيعات: Product + Audit.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | STR-001/003/GOV-003 Satisfied؛ FIN-001 NotStarted؛ PROD-003 Blocked | |
| U2 | ≥9 KPIs كاملة الحقول | owner ≠ auditor | |
| U3 | baselines prePilotFreeze | لكل baselineRequired | |
| U4 | independent measurement | PASS | |
| U5 | sample reconcile | PASS / FAIL كما في fixtures | |
| U6 | quarterly review | بلا screen-count | |
| U7 | screen-count benefit | FAIL | |
| U8 | cross-tenant leak | FAIL | |
| U9 | str007-value-kpi-validate | PASS | |
| U10 | Str007* unit | نجاح | |

**الحالة:** ReadyForSign-off — value/baseline gate pack؛ التكلفة/ESS الحية = بطاقات لاحقة.

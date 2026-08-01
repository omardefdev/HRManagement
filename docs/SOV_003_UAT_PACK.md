# SOV-003 UAT pack

**Status:** ReadyForSign-off

| Id | Check | Expect |
|---|---|---|
| U1 | ADR + blockers | OPS/STR/SOV-002/EXT-004 Satisfied; SEC-004/live-* Blocked; SOV-004 Satisfied (ZT binding) |
| U2 | Hierarchy | hierarchy-inventory-pass |
| U3 | Vendor alone | vendor-alone-decrypt-fail |
| U4 | Dual control / rotation | dual-control-pass / rotation-drill-pass |
| U5 | LiveKeyCustodyClaim=false | live-key-custody-claim-fail |
| U6 | Cross-card sync | SOV-002 + EXT-004 SOV-003 Satisfied; hsm-prod Blocked |

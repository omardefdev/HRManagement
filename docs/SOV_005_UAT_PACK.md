# SOV-005 UAT Pack

| Id | Case | Expect |
|---|---|---|
| U1 | ADR + blockers | STR/TEN/INT/SOV-004 Satisfied; live-* Blocked; SOV-006 Satisfied (pen-red); NAT NotStarted |
| U2 | STRIDE / boundaries | fail missing-stride; pass stride/trust-boundary |
| U3 | High coverage | fail high-without-*; pass triad |
| U4 | Change / misuse / residual | fail unlinked/unsigned/no-trigger; pass linked/signed/trigger |
| U5 | LiveThreatModelClaim | fail when true |

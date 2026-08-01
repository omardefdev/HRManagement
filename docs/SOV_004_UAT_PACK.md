# SOV-004 UAT Pack

| Id | Case | Expect |
|---|---|---|
| U1 | ADR + blockers | IAM/SOV-003 Satisfied; live-* Blocked; SOV-005 Satisfied (TM binding); NAT NotStarted |
| U2 | Standing / JIT | fail standing/shared-root/jit-without-*; pass no-standing/jit-approved |
| U3 | Workstation / MFA / session | fail ordinary device; pass workstation/mfa/session |
| U4 | Break-glass / recert | fail no-alert/dormant; pass alert-review/quarterly |
| U5 | LivePamClaim | fail when true |

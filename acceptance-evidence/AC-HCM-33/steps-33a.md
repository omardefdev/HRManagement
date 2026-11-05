AC-HCM-33a — acceptance without data shows Skipped, never Passed.

Observed 2026-10-29 (PLAN-33 P1): `dotnet test src/Tests/HRManagement.AcceptanceTests` without RUN_ACCEPTANCE → 23 passed (static, stack-free tests only), 90 skipped, 0 failed: no live scenario passes without the stack. Guarded by AcceptanceSkipPatternArchitectureTests (no WriteResult("Skip"), no result+return, no raw Skip.If, every Ac* method asserts) and AcceptanceGateTests (Blocked written, then Skip).

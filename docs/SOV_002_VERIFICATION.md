# SOV-002 Verification

**الحالة:** Done (production data-sovereignty binding) — live egress/JIT/deletion Blocked؛ SYR-008/NAT-001 NotStarted؛ SOV-003 = Satisfied (key-sovereignty binding؛ HSM حي لاحقاً)؛ LiveAtoClaim unchanged.

```powershell
powershell -File scripts/sov002-data-sovereignty-validate.ps1
powershell -File scripts/sov002-data-sovereignty-runtime-validate.ps1
```

Expected: PASS. Staging/Production: `SovData:EnforceOnStartup=true`, `LiveSovereigntyClaim=false`.

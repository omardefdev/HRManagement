# SOV-001 Verification

**الحالة:** Done (production control-baseline binding) — live ATO Blocked؛ SYR-001/NAT-001 NotStarted؛ SOV-002 = Satisfied (data-sovereignty binding؛ egress حي لاحقاً)؛ STR-002 Blocked-Legal unchanged.

```powershell
powershell -File scripts/sov001-control-baseline-validate.ps1
powershell -File scripts/sov001-control-baseline-runtime-validate.ps1
```

Expected: PASS. Staging/Production: `SovBaseline:EnforceOnStartup=true`, `LiveAtoClaim=false`.

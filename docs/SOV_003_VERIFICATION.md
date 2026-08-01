# SOV-003 Verification

**الحالة:** Done (production key-sovereignty binding) — live HSM/ceremony/time Blocked؛ SEC-004 Blocked صادق؛ SOV-004=Satisfied (zero-trust binding؛ PAM حي لاحقاً)؛ SIG-001/NAT-001 NotStarted؛ hsm-prod Blocked.

```powershell
powershell -File scripts/sov003-key-sovereignty-validate.ps1
powershell -File scripts/sov003-key-sovereignty-runtime-validate.ps1
```

Expected: PASS. Staging/Production: `SovKeys:EnforceOnStartup=true`, `LiveKeyCustodyClaim=false`.

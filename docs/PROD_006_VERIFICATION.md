# PROD-006 Verification

**الحالة:** Done (production semantic-layer binding) — live warehouse/BI Blocked؛ ANA-001/REP-001 NotStarted؛ PROD-007 Satisfied (extensibility-platform binding).

```powershell
powershell -File scripts/prod006-semantic-layer-validate.ps1
powershell -File scripts/prod006-semantic-layer-runtime-validate.ps1
```

Expected: PASS. Staging/Production: `ProdSemantic:EnforceOnStartup=true`, `LiveSemanticLayerClaim=false`.

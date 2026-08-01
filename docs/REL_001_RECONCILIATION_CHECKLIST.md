# قائمة مصالحة REL-001 (Staging / Synthetic)

| # | فحص | أين |
|---|---|---|
| 1 | parity-matrix comparisons كلها match\|approved | `docs/rel-staging/parity-matrix.json` |
| 2 | `rel001-parity-diff.ps1` exit 0 | scripts |
| 3 | staging ingress TLS + host | `staging-ha/ingress.yaml` |
| 4 | staging data tls-stub + backup-retention | `staging-data-ha/` |
| 5 | synthetic manifest forbidProductionDump | `testdata/rel001/` |
| 6 | seed لا يقبل ProdDumpPath | `rel001-seed-synthetic.ps1` |
| 7 | Keycloak rel001 + roles matrix | `realm-rel001-staging.json` |
| 8 | reset DryRun يعمل | `rel001-reset-staging.ps1 -DryRun` |
| 9 | Feature flags موثّقة | `REL_001_FEATURE_FLAGS.md` |
| 10 | RACI + rehearsal checklist | ownership + rehearsal docs |

## حدود

لا REL-002 Go/No-Go؛ لا parallel payroll؛ Compose = lab drift معتمد.

# REL-001 — مصفوفة رحلات Staging / Synthetic / Rehearsal

المصدر: [`rel-staging/parity-matrix.json`](rel-staging/parity-matrix.json) — انظر [ADR-REL-001](ADR-REL-001-staging-synthetic-parity.md).

## TopologyParity

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| IngressTls | نعم | platform | 24 | `staging-ha/ingress.yaml` |
| DnsStub | نعم | platform | 48 | `hr.staging.example.com` / TF |
| ReplicasScale | لا | platform | 72 | approved lower replicas |
| ParityDiffGate | نعم | platform | 8 | `scripts/rel001-parity-diff.ps1` |

## SyntheticReset

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| PilotPack | نعم | qa | 24 | `testdata/rel001/` |
| NoProdPii | نعم | security | 8 | policy + obs-pii-scan |
| KeycloakRoles | نعم | iam | 24 | `realm-rel001-staging.json` |
| ResetScript | نعم | platform | 48 | `rel001-reset-staging.ps1` |

## ReleaseRehearsal

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| FeatureFlags | نعم | platform | 24 | `REL_001_FEATURE_FLAGS.md` |
| EnvOwnership | نعم | program | 48 | `REL_001_ENV_OWNERSHIP.md` |
| RehearsalChecklist | نعم | release | 24 | `REL_001_REHEARSAL_CHECKLIST.md` |
| VerifyE2eObs | نعم | qa | 48 | verify-stack + E2E + OBS |

اعتماديات: OPS-005، QA-004. Compose = lab drift معتمد.

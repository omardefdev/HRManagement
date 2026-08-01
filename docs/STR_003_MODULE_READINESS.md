# STR-003 — Module readiness

Rule: `readinessStatus=Ready` only when UAT pack + runbook + SLO refs exist on disk. Source of truth: [`str-modules/module-catalog.json`](str-modules/module-catalog.json).

| moduleId | readiness | uatPackRef | runbookRef | sloRef |
|---|---|---|---|---|
| hr-core | Ready | FUN_001_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| hr-contracts | Ready | FUN_002_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| hr-health | Ready | FUN_004_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| hr-movements | Ready | FUN_003_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| hr-training | Ready | FUN_004_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| payroll | Ready | FUN_002_UAT_PACK | OBS_ALERT_RUNBOOKS | OBS_SLO_CATALOG |
| idcard | Ready | FUN_005_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| records-archive | Ready | FUN_006_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| admin-logistics | Ready | FUN_005_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| it-infra | Ready | FUN_005_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| dev-analytics | Ready | FUN_006_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| compliance | Ready | FUN_004_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| auth-directory | Ready | FUN_005_UAT_PACK | KEYCLOAK_AND_SECRETS | OBS_SLO_CATALOG |
| file-storage | Ready | FUN_005_UAT_PACK | PRODUCTION_RUNBOOK | OBS_SLO_CATALOG |
| postgres | Ready | REL_001_UAT_PACK | BACKUP_POSTGRES | OBS_SLO_CATALOG |
| rabbitmq | Ready | REL_001_UAT_PACK | RABBITMQ_OPERATIONS | OBS_SLO_CATALOG |

## Gate

A module may be labeled **available** to an agency only if Ready (and in `agencyVisibleTiers` for UI modules). NotReady/Sunset → Excluded in agency contract unless exception.

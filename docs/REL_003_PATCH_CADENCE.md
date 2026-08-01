# REL-003 — Platform patch cadence

Separate from NuGet/SCA ([DEPENDENCY_UPDATE_SLA.md](DEPENDENCY_UPDATE_SLA.md)). National OS/DB/cert SLAs remain **OPS-007** (out of depth / Blocked nationally).

## Cadence (baseline)

| Layer | Target cadence | Owner | Evidence |
|---|---|---|---|
| Base container images | Monthly + critical CVE | platform | image digests / renovate PRs |
| Runtime (.NET host images) | Follow LTS channel monthly | platform | deploy digest |
| Postgres minor (lab/staging) | Monthly window | platform | BACKUP_POSTGRES + change ticket |
| RabbitMQ minor | Monthly window | platform | RABBITMQ_OPERATIONS |
| Keycloak | Monthly or security advisory | platform | KEYCLOAK_AND_SECRETS |
| Critical CVE (CVSS high+) | 7 days or waiver | security + platform | change + waiver |

## Rules

1. Every patch window is a Normal or Emergency change ([REL_003_CHANGE_PROCESS.md](REL_003_CHANGE_PROCESS.md)).
2. Production requires rollback plan (DEP-001).
3. Do not mark OPS-007 Done from this document.

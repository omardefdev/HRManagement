# STR-002 — Legal control trace samples

Minimum **three** law → config → test → report walks for verification.

<a id="trace-1"></a>

## Trace 1 — Tax / payroll legal decision

| Step | Artifact |
|---|---|
| Law / source | Law 50/2004 + platform interim brackets (file 12; not MoF gazette) |
| Control | `CTRL-TAX-BRACKETS-INTERIM` / `CTRL-PAY-LD-REGISTER` |
| Config | `TaxBrackets` via GOV-002 `LD-TAX-BRACKETS` in `docs/legal-decisions/register.json` |
| Test | `Gov002LegalGoldenPackTests` / `Gov002LegalGovernanceTests` |
| Report | `docs/GOV_002_VERIFICATION.md` + LegalAttest before Publish |
| Gap | SYD-004 Blocked-Legal until MoF table |

<a id="trace-2"></a>

## Trace 2 — Privacy Law 12/2024 → GOV-001

| Step | Artifact |
|---|---|
| Law / source | Law 12/2024 + executive instructions (file 12) |
| Control | `CTRL-PRV-LAW12-DPIA` |
| Config / process | DPIA / ROPA / DSAR / classification inventory (GOV-001) |
| Test | `Gov001*` unit/pack tests |
| Report | `docs/GOV_001_VERIFICATION.md` |
| Gap | SYD-008 license/DPO registration Blocked-Legal |

<a id="trace-3"></a>

## Trace 3 — Cross-tenant isolation (sovereignty / TEN)

| Step | Artifact |
|---|---|
| Law / policy | National isolation requirement (STR-001 envelope `national.cross_tenant_success=0`) |
| Control | `CTRL-SOV-CROSS-TENANT` |
| Config | Tenant context / EF filters / RLS (TEN-002/003) |
| Test | TEN/SEC suite references; walkthrough id in this pack |
| Report | STR-001 envelope matrix + TEN verification docs |
| Gap | Hosting sovereignty SYD-009 remains Blocked-Legal (separate control) |

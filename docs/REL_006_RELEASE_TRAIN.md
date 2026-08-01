# REL-006 — Release Train, Freeze Windows, and Hotfix

## Objective

Establish a predictable release cadence with enforced freeze windows during payroll close, legal deadlines, and national holidays. Emergency hotfixes follow a fast-track path with dual review and mandatory post-implementation review within 24 hours.

## Release cadence

| Type | Frequency | Approval | Notes |
|---|---|---|---|
| Scheduled release | Bi-weekly (Tuesday) | Change board + QA sign-off | Standard path |
| Patch release | As needed | Two reviewers + security | Bug fixes only |
| Emergency hotfix | As needed | Two reviewers + IC approval | PIR within 24h |
| Security patch | As needed | Security lead + one reviewer | Coordinated with PSIRT-001 |

## Freeze windows

All freeze windows are defined in [`docs/rel/freeze-windows.json`](./rel/freeze-windows.json).

| Window | Period | Exceptions |
|---|---|---|
| Payroll close | 25th – 5th monthly | Emergency hotfix only |
| End of fiscal year | Last 2 weeks of fiscal year | Emergency hotfix only |
| National holidays | Per national calendar | Emergency hotfix only |
| Pilot stabilization | First 30 days post-wave launch | Emergency hotfix only |

## Risk classification

| Level | Criteria | Required |
|---|---|---|
| Standard | No schema migration, no API change, no payroll logic | 1 reviewer + CI pass |
| Medium | Schema migration (backward-compatible) or API change | 2 reviewers + staging deploy |
| High | Breaking change, payroll logic, security control | Change board + staging soak |

## Emergency hotfix path

1. Incident Commander approves bypass of freeze window.
2. Two independent code reviews (neither is the author).
3. CI + security scan must pass.
4. Deploy with rollback plan ready.
5. Audit log records bypass reason, approver, and IC.
6. Post-Implementation Review (PIR) within 24 hours.

## Metrics

- **Failed change rate**: target < 5%.
- **Lead time for changes**: target < 2 days for standard, < 4h for emergency.
- **Change records completeness**: 100%.

## Validation

```powershell
scripts/rel006-validate.ps1
```

## Related cards

- REL-004 (API/event/schema compatibility)
- REL-005 (progressive delivery / canary)
- REL-003 (release management process)

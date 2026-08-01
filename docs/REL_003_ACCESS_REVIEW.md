# REL-003 — Privileged access review

Quarterly review of break-glass and high-privilege roles (Keycloak / ops). Evidence fill — not full IdP automation.

| Field | Value |
|---|---|
| Period | |
| Reviewer | |
| Approver (security) | |
| Scope | realm roles, cluster-admin break-glass, DB superuser list |

## Checklist

1. Export current privileged principals (attach evidence path).
2. Fill [`rel-ops/access-review-template.csv`](rel-ops/access-review-template.csv).
3. Revoke or justify each row; owner sign-off.
4. Re-test break-glass still works for designated accounts only.
5. File under evidence store; link from reconciliation.

## Cadence

| Environment | Frequency |
|---|---|
| Pilot / staging-ha | Quarterly |
| Production (when live) | Quarterly minimum; after Sev1 involving access |

SIEM privileged events (OBS-003) support detection; they do not replace this review.

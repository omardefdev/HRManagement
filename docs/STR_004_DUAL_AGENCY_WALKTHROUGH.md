# STR-004 — Dual-agency same-release walkthrough

Prove two conflicting agency configs from **one** `releaseDigest` without product forks.

| Field | Value |
|---|---|
| Release digest | `sha256:str004-demo-digest-20260730` |
| Agency A | `testdata/str004/agencies/agency-a.json` |
| Agency B | `testdata/str004/agencies/agency-b.json` |

## Conflict summary

| Dimension | Agency A | Agency B |
|---|---|---|
| Branding color | #0B3D5C | #2E7D32 |
| Optional on | payroll, compliance, dev-analytics | hr-training, idcard, admin-logistics |
| Optional off | hr-training, idcard | compliance, dev-analytics |

Mandatory core modules (STR-003) remain on for both.

## Walkthrough steps

1. Confirm both fixtures share identical `releaseDigest`.
2. Confirm entitlements differ only via config lists (not separate builds).
3. Confirm branding differs via config only.
4. Confirm no agency-named branch required in `src/`.
5. Record sign-off in UAT pack.

## Out of depth

Full signed provision publish = ONB-001; versioned policy simulation = PROD-001.

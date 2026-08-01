# REL-004 — Release notes template

**Release:** {{RELEASE_TAG}}  
**Train:** {{RELEASE_TRAIN}}  
**Platform version:** {{PLATFORM_VERSION}}  
**Date:** {{DATE}}

Copy this template for every platform release. Attach completed [compatibility matrix](rel-compat/compatibility-matrix-template.md).

---

## Summary

One paragraph: theme of the release, risk level, N/N-1 support statement.

---

## Upgrade notes

- **Minimum supported client version:** {{MIN_CLIENT_VERSION}}
- **N-1 supported until:** {{N_MINUS_1_EOL_DATE}}
- **Mixed-version cluster:** Required during rolling deploy (REL-005)
- **Database migrations:** {{MIGRATION_SUMMARY}} — rollback: see rollback section

---

## API changes

### Additions (expand — minor)

| Service | Endpoint | Description |
|---------|----------|-------------|
| | | |

### Migrations (dual-read / dual-write)

| Service | Surface | Old | New | Removal train |
|---------|---------|-----|-----|---------------|
| | | | | |

### Breaking changes (major — requires deprecation window closed)

| Service | Surface | Change | Deprecation ID |
|---------|---------|--------|----------------|
| | | | |

---

## Event / message changes

| Contract | SchemaVersion | Change | Consumer action |
|----------|---------------|--------|-----------------|
| | | | |

Event inventory: [event-schema-inventory.json](rel-compat/event-schema-inventory.json)

---

## Deprecations

| ID | Surface | Status | Earliest removal |
|----|---------|--------|------------------|
| | announced / warn / removed | | |

Full register: [deprecation-register.json](rel-compat/deprecation-register.json)

---

## Feature flags

| Flag | Default | Description |
|------|---------|-------------|
| | off | |

---

## External / partner impact (EXT-005)

| Integration | Purpose | Action required |
|-------------|---------|-----------------|
| | | |

---

## Rollback

- **Safe rollback window:** {{ROLLBACK_WINDOW}} (see [REL_004_ROLLBACK_POLICY.md](REL_004_ROLLBACK_POLICY.md))
- **Blocked rollback conditions:** {{BLOCKED_CONDITIONS}}
- **Compensating actions if rollback after migration:** {{COMPENSATION}}

---

## Verification checklist

- [ ] OpenAPI diff PASS or major bump documented
- [ ] `Rel004CompatibilityTests` green
- [ ] `rel004-compat-validate.ps1` PASS
- [ ] Compatibility matrix signed
- [ ] Deprecation register updated
- [ ] N-1 consumer regression (lab) PASS

---

## Contacts

| Role | Contact |
|------|---------|
| Release Authority | |
| API Governance | |
| On-call | [REL_003_ONCALL_ROSTER.md](REL_003_ONCALL_ROSTER.md) |

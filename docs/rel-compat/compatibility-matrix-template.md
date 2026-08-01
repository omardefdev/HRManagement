# Compatibility Matrix — Release {{RELEASE_TAG}}

**Card:** REL-004  
**Release train:** {{RELEASE_TRAIN}} (e.g. 2026.Q3.2)  
**Platform version:** {{PLATFORM_VERSION}} (semver)  
**Author:** {{AUTHOR}}  
**Reviewers:** Release Authority, API Governance, Data Governance  
**Date:** {{DATE}}

---

## 1. Summary

| Metric | Value |
|--------|-------|
| API breaking changes | {{COUNT_API_BREAKING}} |
| Event breaking changes | {{COUNT_EVENT_BREAKING}} |
| Deprecations announced | {{COUNT_DEPRECATIONS}} |
| Deprecations removed | {{COUNT_SUNSETS}} |
| N-1 cluster test | {{PASS_FAIL_PENDING}} |
| OpenAPI diff (vs baseline) | {{OPENAPI_DIFF_RESULT}} |

---

## 2. API changes

| Service | Change class | Endpoint / contract | Semver bump | N-1 compatible? | Deprecation ref | Notes |
|---------|--------------|---------------------|-------------|-----------------|-----------------|-------|
| hr-core | expand | `GET /api/hr-core/employees/{id}` | minor | Yes | — | Added optional `orgUnitPath` |
| payroll | migrate | `POST /api/payroll/runs` | minor | Yes | DEP-2026-001 | Dual-read `periodCode` + `periodId` |
| | contract | | major | No | | |

**Change class legend:** `expand` | `migrate` | `contract` (see [REL_004_COMPATIBILITY_POLICY.md](../REL_004_COMPATIBILITY_POLICY.md))

---

## 3. Event / message changes

| Contract | SchemaVersion (before→after) | Change class | Consumers affected | N-1 skip OK? | Notes |
|----------|------------------------------|--------------|----------------------|--------------|-------|
| ITrainingCompleted | 3→3 | expand | HR.Core, Payroll | Yes | Optional `CertificateUrl` |
| | | contract | | No | |

---

## 4. External / EXT-005 consumers

| Consumer ID | Integration | minSupported | maxTested | Certified? | Regression result |
|-------------|-------------|--------------|-----------|------------|-------------------|
| ext-treasury-disbursement | treasury-payroll-disbursement | 2026.06.1 | 2026.07.1 | lab-certified | PASS |
| ext-civil-registry-proof | civil-registry-proof-query | 2026.06.1 | 2026.07.1 | lab-certified | PASS |

Full inventory: [api-client-inventory.json](api-client-inventory.json)

---

## 5. Database / migration rollback

| Migration ID | Expand-only? | Rollback within 15m? | Waiver |
|--------------|--------------|----------------------|--------|
| {{MIG_ID}} | Yes/No | Yes/No/Conditional | {{WAIVER_REF}} |

See [REL_004_ROLLBACK_POLICY.md](../REL_004_ROLLBACK_POLICY.md).

---

## 6. Feature flags

| Flag | Default (N) | Default (N-1 pods) | Removal train |
|------|-------------|----------------------|---------------|
| {{FLAG_NAME}} | off | off | {{TRAIN}} |

---

## 7. Sign-off

| Role | Name | Date | Decision |
|------|------|------|----------|
| Release Authority | | | Approve / Hold |
| API Governance | | | Approve / Hold |
| Data Governance | | | Approve / Hold |

**Hold criteria:** Any `contract` row without major bump, sunset before R+2, or failed N-1/OpenAPI diff.

# MIG-002 — Legacy ID policy

| Rule | Value |
|---|---|
| storeAsReferenceOnly | true |
| neverUseAsAuthOrTenantKey | true |
| platformKey | Guid |
| referenceFields | EmpNo, NAT_ID, BadgeId, LegacyDocId, LOGIN_HINT |

## Implications

- Auth session / JWT subject / TenantId come from TEN-001 platform identity — **not** EmpNo.
- Legacy identifiers persist as searchable references for reconciliation (MIG-005) and match (MIG-004).
- Fixture `legacy-id-as-auth-key-fail.json` must FAIL validate.

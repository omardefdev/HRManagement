# MIG-002 — Independent agency-extract / SYR-003 / MDM-001 / MIG-003 review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-002 |
| Expires | When agency-extract-access, SYR-003, MDM-001, and MIG-003 are Accepted, or 2027-01-31 — whichever first |
| Owner | Data Architecture + Migration Office |

## Waiver statement

Live agency value coverage (agency-extract-access), full legal/business regime packs (SYR-003), job catalog SoT (MDM-001), and the migration transform worker (MIG-003) are **deferred**. Engineering adopts the code-map matrix as a startup/CI binding with lab universes and signed mapping metadata. This waiver does **not** authorize:

- Claiming a live code-map engine while `LiveCodeMapEngineClaim` must stay false
- Mass Other / catch-all dumps (`forbidOther` must remain true)
- Map dispositions without `targetValue`
- Using EmpNo / NAT_ID as auth, session, or tenant keys
- Signer role equal to reviewer role, or unsigned mappings (missing hash/version)

## Conditions

1. Matrix blockers remain: MIG-001/TEN-001/GOV-003=`Satisfied`, agency-extract-access=`Blocked`, SYR-003=`Blocked`, MDM-001=`Blocked`.
2. `MigCanonical:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveCodeMapEngineClaim=false`.
4. Negative fixtures (`mass-other-fail`, `unmapped-no-quarantine-fail`, `legacy-id-as-auth-key-fail`) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Data Architecture | Lab code-map binding acceptable for interim |
| Legal / HR Finance | Signed mapping metadata accepted pending live regimes |
| Migration Office | Transform worker deferred to MIG-003 |
| Security | Legacy-id reference-only policy accepted as gate |

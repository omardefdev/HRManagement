# EXT-004 — Independent CA / HSM / TSA / SYD-011 review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | EXT-004 |
| Expires | When national-pki-live, hsm-prod, tsa-live, and EXT-004a–f are Accepted, or 2027-01-31 — whichever first |
| Owner | PKI + Legal + Security |

## Waiver statement

Live national CA, production HSM, live TSA, SIG-001 UI, EXT-003e treasury signature bind, SYD-011 alignment, and PKI workers (EXT-004a–f) are **deferred**. Engineering adopts the pki-signature matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live PKI while `LivePkiClaim` must stay false
- Marking `livePkiForbidden=false` in this binding
- Treating form signatures as legal evidence, revoked/expired-without-LTV as valid, key exfil, or deprecated algorithms as valid
- Flipping treasury matrix `EXT-004` blocker to Satisfied before EXT-003e

## Conditions

1. Matrix blockers remain: STR-002/OBS-003=`Satisfied`; SOV-003=`Satisfied` (key-sovereignty binding; live HSM = SOV-003a); national-pki-live / hsm-prod / tsa-live / SYD-011=`Blocked`.
2. `ExtPki:EnforceOnStartup` remains true in Staging/Production.
3. Default `LivePkiClaim=false`.
4. Negative fixtures (signer-role / revoked / expired / clock / key-exfil / deprecated-alg / form-as-legal) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| PKI + Security | Lab PKI binding acceptable for interim |
| Legal Evidence | Signer intent + LTV + form-not-legal accepted as gate |
| Integration Office | Live CA/HSM/TSA deferred to EXT-004a–f |
| Finance Integration | EXT-003e treasury signature bind still deferred |

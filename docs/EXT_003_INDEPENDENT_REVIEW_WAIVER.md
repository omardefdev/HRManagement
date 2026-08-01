# EXT-003 — Independent treasury / bank / postal / PKI review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | EXT-003 |
| Expires | When treasury-live, bank-sandbox-cert, postal-channel-live, and EXT-003a–e are Accepted, or 2027-01-31 — whichever first |
| Owner | Payroll + Finance Integration |

## Waiver statement

Live MOF/treasury connector, bank/postal adapters, ProcessPaymentBatch + ack consumer, SYR-011/PAY-004, live PKI (EXT-004), and treasury workers (EXT-003a–e) are **deferred**. Engineering adopts the treasury-payment matrix as a startup/CI binding with lab harness fixtures. This waiver does **not** authorize:

- Claiming live treasury while `LiveTreasuryClaim` must stay false
- Marking `liveTreasuryForbidden=false` in this binding
- Unapproved-run payment orders, duplicate business effects, totals mismatch as success, unaudited manual files, or cutoff breach without contingency
- Live PKI while EXT-004 remains NotStarted

## Conditions

1. Matrix blockers remain: PAY-003/IAM-003/INT-003=`Satisfied`; EXT-004=`NotStarted`; treasury-live / bank-sandbox-cert / postal-channel-live=`Blocked`.
2. `ExtTreasury:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveTreasuryClaim=false`.
4. Negative fixtures (unapproved / duplicate / totals / manual / cutoff) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Payroll + Finance Integration | Lab treasury-payment binding acceptable for interim |
| Finance Control | Approved-run + recon + manual audit accepted as gate |
| Integration Office | Live MOF/bank/postal deferred to EXT-003a–e |
| Security | Live PKI deferred to EXT-003e + EXT-004 |

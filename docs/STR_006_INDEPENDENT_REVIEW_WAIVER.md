# STR-006 — Independent SRE-001 / FIN-001 / SOV-007 review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | STR-006 |
| Expires | When SRE-001 and FIN-001 are Accepted, or 2027-01-31 — whichever first |
| Owner | Sponsor + Finance + Platform |

## Waiver statement

National 24×7 SRE desk (SRE-001) and independent finance TCO workbook (FIN-001) remain **deferred**. SOV-007 production vendor-exit binding is Done (live exit drill = VEN-001). Engineering adopts the ownership matrix as a startup/CI binding with role placeholders and budget/vendor stubs. This waiver does **not** authorize:

- Claiming national 24×7 coverage while SRE-001 is Blocked
- Shipping critical components with busFactor < 2 or no backups
- Funding only development (dev-only TCO)
- Vendor contracts missing RTO, IncidentDisclosure, or Exit

## Conditions

1. Matrix blockers remain: STR-005=`Satisfied`, SOV-007=`Satisfied` (vendor-exit binding; live drill = VEN-001), FIN-001=`NotStarted`, SRE-001=`Blocked`.
2. `ProgramOwnership:EnforceOnStartup` remains true in Staging/Production.
3. Default `National24x7Claim=false`.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Sponsor | Role-only ownership matrix acceptable for interim |
| Finance | Budget stub + RoleApprovedStub TCO accepted pending FIN-001 |
| Platform | Roster template acceptable pending SRE-001 |
| Procurement | Vendor-exit binding Done (SOV-007); live exit drill = VEN-001 |

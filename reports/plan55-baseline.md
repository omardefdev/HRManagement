# PLAN-55 — baseline (P0)

Taken on `ad3b4e69` (`origin/main` `12b83970` + PLAN-54's merge note + this plan's prompt), 2026-11-24, on the owner's Linux notebook
(`/home/omar/HRManagement`). The full search output is `artifacts/plan55/inventory.txt` (not committed); this file is its summary.

## 0.2 Build and tests

| Step | Result |
|------|--------|
| `dotnet build HRManagement.slnx -warnaserror` | 0 errors, 4 min 12 s |
| Unit tests filtered on `Pay029`, `ContractActivated`, `PaymentOrder`, `Frd001`, `Iam053`, `Pay054` | 436 passed, 0 failed, 37 s |

## 0.3 Inventory

### The profile's bank fields (`BankAccountIban`, `BankName`, `BankAccountVerifiedAt`)

| Where | What it does |
|-------|--------------|
| `EmployeePayProfile.cs:47,51,60` | the three properties |
| `EmployeePayProfileVersioning.cs:79-81` | PLAN-53's copy-forward: the only code that writes them |
| `PaymentOrderService.cs:22-29,52` | reads the active profile's verified IBAN into `BeneficiaryIban` |
| `PayrollExternalExportService.cs:50-57,81-97,141-145` | reads the same for the treasury file; refuses with a count |
| `FraudScanInputBuilder.cs:25` | FR-04's IBAN |
| `DsarInternalController.cs:27-28` | `bankAccountPresent`, `bankAccountVerified` |
| `PayProfileWritersTests.cs:33-34` | the scanner's list of profile fields |
| Tests that set or assert them | `Iam053SalaryChangeHarness`, `Iam053SalaryChangeStateMachineTests`, `Iam053ApplierCopyForwardTests`, `Iam053SystemChangeAndCalendarTests`, `Iam053PayProfileChangePostgreSqlTests.Transaction`, `Frd001PaymentBlockIntegrationTests`, `Pay054V2RunPostgreSqlTests`, `AcIam53SalaryChangeTests` (by SQL) |
| Seeds, scripts, views | none: no seed sets them, no view shows them, no script names them |
| Docs | the gap study, its verification, `COD_ORG_GAP_STUDY.md`, PLAN-53's report and prompt, this plan |

No command, API, consumer or screen writes them: the demo and the acceptance stacks hold none (the demo: 0 of 425 profiles).

### Every creation of a pay profile (`new EmployeePayProfile`)

| Where | Kind |
|-------|------|
| `EmployeePayProfileVersioning.NextVersion` (`:72`) | PLAN-53's copy-forward (the applier's) |
| `HandleContractActivatedHandler` (`InboundEventHandlers.cs:54`, `:73`) | a first profile, and a version over the active one: **neither carries the unit or the regime** |
| `HandleEmployeeCreatedHandler` (`:643`) | the inactive, unpriced profile, with the unit and the regime, `EffectiveFrom = DateTime.UtcNow` |
| `DemoPayrollSeeder.cs:185` | the seed (the acceptance seeder calls it) |
| 27 places in the test projects | test data |

### Every other write of a profile

| Where | What |
|-------|------|
| `HandleContractActivatedHandler` `:70-71` | closes the active profile **on** the start date (the boundary day in two versions) |
| `HandleSeparationCompletedHandler` `:213-214` | closes the profile at the end of service (PLAN-64) |
| `HandleTransferExecutedHandler` `:307-316` | the unit, the regime and the salary difference, in place (PAY-G) |
| `HandleDisciplinaryActionHandler` `:615-616` | a dismissal closes the profile (PLAN-64) |
| `EmployeePayProfileVersioning.Close` | the applier's closing |

### Reads of «the employee's profile» for a scope or a unit

| Where | Which profile |
|-------|---------------|
| `PayrollOrgScopeKeys` (the org-scope filter's key of an employee) | the active one |
| `PayrollEmployeeScope` (`ForbidIfEmployeeOutOfScopeAsync`, `EmployeeIdsInScope`) | the active one |
| `ArrearsController.GetActiveEmployeeOrgUnitIdAsync` (`:129-133`) | the active one — **already right** |
| `PayrollApiController` PUT alias (`:144` on) | goes through PLAN-53's submit, whose handler reads the active one — **already right** |
| `SubmitPayProfileChangeHandler` (`PayProfileChangeCommands.cs:61,113`) | the active one |
| `PayProfileChangeApplier.ApplySystemChangeAsync` (`:169`) | the active one |

Every read takes the active profile. Nothing to fix; the two the plan names are recorded as already right.

### Payment orders, holds, the treasury file, and what «disbursed» means

- **`PaymentOrder` creators:** `PaymentOrderService.EnsureOrdersForBatchAsync` (one per salary line with a positive amount, `Draft`), and the
  acceptance seeder (its own IBANs on orders). **Readers:** `PaymentOrderCoordinator`, `BankReconciliationCoordinator`,
  `BankReconciliationService`, `ExecutePartialDisbursementCommand`.
- **Order statuses:** `Draft` → `Submitted` → `Acknowledged` → `Settled` | `PartiallySettled`; `Rejected` from `Submitted` or `Acknowledged`.
  **«Disbursed» for a line** = it has a payment order that left `Draft` and was not rejected (`Submitted`, `Acknowledged`, `Settled`,
  `PartiallySettled`): the instruction has gone to the channel. A line with no order, a `Draft` order or a `Rejected` order is not yet
  disbursed. This is the rule P1's retirement hold uses.
- **`PaymentHold` creators:** `FraudAnomalyEngine` (`Fraud`), `HandleContractActivatedHandler.RaiseReviewHoldAsync` (`Manual`, PAY-029, the
  rule code at the start of `Reason`, run-independent), the acceptance seeder. **Readers:** `PaymentDisbursementGate` (held employees),
  `RunPayrollFraudScanCommand`, `ReleasePaymentHoldCommand`, the holds' screens.
- **The treasury file:** `PayrollExternalExportService.ExportRunAsync` (a `Posted` run only), called by `PayrollReportsController` (three
  actions); the gateways are `FilePayrollDisbursementGateway` and `TreasuryLiveConnector`.

### The tests that pin PAY-029 and FR-04

`Pay029PayProfileTests` (7 tests; they build the handler from a repository and the options only), `Frd001FraudEngineTests`,
`Frd001PaymentBlockIntegrationTests`, `AcHcm29HireContractTests`, `Ac25FraudHoldTests`.

## The demo server's picture (read-only, 2026-11-24T05:16:31Z)

| Fact | Value |
|------|-------|
| Running tag | `014db7e3a01a` (PLAN-54's, on `origin/main`) |
| Containers / free disk | 22 / 24 GB of 145 GB |
| Last payroll migration | `20261123074128_PAY054_LegalFrameworkPacks` |
| Pay profiles | 425, all active, one agency (`…0001`) |
| Profiles with a bank field | 0 |
| Active profiles with an empty unit | 0 |
| Active profiles with an empty regime | 425 |
| …of which an earlier profile of the same employee carries a regime | 0 (each employee has one profile) |
| Payment orders | 0 |
| Payment holds | 0 |
| Runs by status | `Calculated` 1, `Posted` 4, `Approved` 1 |
| Checksum of the active profiles (id, unit, regime, salary, grade) | `f409f25a00f0f1ecafb418e9bf6c93e4` |
| Checksum without unit and regime (id, salary, grade) | `7c1359efefdbdd1a637f62393f9411c5` |

**What the repair will meet on the demo:** 425 active profiles with an empty regime and no earlier profile to copy from. The repair copies
only from the employee's own history, so its dry run is expected to list **0 changes and 425 profiles it cannot fill** (the seed never
wrote a regime). The raw output is `artifacts/plan55/vps/picture-before.txt`.

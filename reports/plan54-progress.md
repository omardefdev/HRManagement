Started (UTC): 2026-11-23T07:26:43Z

# PLAN-54 progress (the agent's checkpoint)

Session: one, in the main folder `/home/omar/HRManagement` (Linux notebook), branch `feature/plan-54-pay-legal-framework-reference-data`.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 baseline | done | 2026-11-23T07:36:53Z |
| P1 data and fingerprint | done | 2026-11-23T08:50:07Z |
| P2 create, import, governing version | done | 2026-11-23T09:39:43Z |
| P3 engine reads the version | next | |
| P4 governance | | |
| P5 manual inputs | | |
| P6 acceptance and review | | |
| P7 main | | |
| P8 demo server | | |
| P9 docs, gates, report, merge | | |

## P0 — timing, keep-awake, preconditions, baseline

- Started (UTC): 2026-11-23T07:26:43Z (the file's first line).
- **Keep-awake:** `systemd-inhibit --what=sleep:idle --who=plan54`, PID 3171465 (`artifacts/plan54/keepawake.pid`). Stop it by that PID at the end.
- **Preconditions:** 1 pass (main folder, on `docs/plan-53-merge-note`, no other session); 2 pass (`origin/main` = `53690e0e`, the merge of
  `release/plan-53` is there); 3 pass (tree clean); 4 pass (`ssh hrm-vps whoami` = `hrm`, ED25519 host key
  `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`, port 22 open); 5 pass (`gh`: `omardefdev` active, scopes `repo`, `workflow`).
- **P-1:** branch from `origin/main` (upstream unset, so nothing can push to `main` by accident); PLAN-53's merge note cherry-picked
  (`907c1cee`, same diff as `38933f23`); this prompt committed (`6ae73571`). **Deviation:** the prompt file was not on disk (the owner
  pasted it into the session); it was written to `docs/acceptance-plans/execution/PLAN-54-AGENT-PROMPT.md` from the session's own
  transcript, byte for byte. `docs/plan-53-merge-note` deleted after checking its content is on this branch.
- **The machine today:** `/` 50 GB free of 233 GB; Docker holds 8 images of other projects and the owner's dev containers (untouched).
  **PLAN-53's local base images and its helper (`artifacts/plan53/sdk-local/`) are gone**, and so are every acceptance image and the
  third-party images of the stack. **The link is fast today:** `mcr.microsoft.com/dotnet/sdk:10.0.400` pulled in 2 min 44 s
  (07:28:49Z to 07:31:33Z), `aspnet:10.0` and `runtime-deps:10.0` in 69 s more (`artifacts/plan54/pull-base.log`). So the images can be
  built from the registry's base images, as CI does; one service at a time stays (the disk).
- **Durations:** baseline build 4 min 54 s; unit filter 15 s (167/167); payroll integration filter 23 s (28/28); the demo's picture 6 s.
- **Baseline:** `reports/plan54-baseline.md`; inventories `artifacts/plan54/inventory.*.txt`; the demo's picture
  `artifacts/plan54/vps/p0-picture.txt`.
- **Server at P0:** tag `5aa61b78e3a6` (PLAN-53's last, the rollback target), 22 containers (20 healthy of 20 with a check), 30 GB free.

## P1 — the data and the fingerprint

- Finished (UTC): 2026-11-23T08:50:07Z.
- **Entities:** `StatutoryDeductionRule`, `PayrollConfigurationFieldSource`, `PayrollConfigurationEditor` (the editors' record of P-8);
  `ILegallySourcedValue` on brackets, holidays, statutory deductions, field sources and priority rules; `HashSchemaVersion` and the
  five child collections on `PayrollConfiguration`; `IManualPayInput` and its five fields on the three inputs (and `Reason` on an
  allowance); `LegalAttestation.InterimExceptionIdsJson`; `PayrollRunJob.Log`.
- **Fingerprint v2** (`PayrollConfigurationHashService`): v1's method is unchanged text; v2 adds the effective date, the four tables,
  the field sources and every reference, status and exception; rows sorted by their keys, money with two decimals.
- **`CloneAsDraft`** carries every table and reference, takes the date it is given, and for a v1 source starts from what a v1 run
  reads (`PayrollLegacyReferenceData`), unclassified.
- **Migration `20261123074128_PAY054_LegalFrameworkPacks`:** no byte order mark; every existing row by column default
  (`HashSchemaVersion` 1, `Approved`/`System`, `Interim`, `[]`), no data statement; row security on the three new tables;
  `has-pending-model-changes`: none.
- **Tests:** unit filter `Pay054|Pay002|Gov002|Payroll` 194/194 (11 s); `Pay054MigrationPostgreSqlTests` 3/3 on PostgreSQL as a
  NOSUPERUSER NOBYPASSRLS owner under forced row security (14 s); build `-warnaserror` 0/0 (35 s incremental); format clean.
- **Proof that v1 did not move:** `Pay054HashV2Tests` recomputes the three seeded versions' fingerprints and gets the demo server's
  stored values (`9d028823…`, `6a3fa3da…`, `a5de27a1…`).
- **Files:** `Payroll.Domain/Entities/{StatutoryDeductionRule,PayrollConfigurationFieldSource,PayrollConfigurationEditor,ILegallySourcedValue,IManualPayInput}.cs`,
  `Enums/{PayrollLegalValueStatus,ManualInputApproval}.cs`, edits to nine entities, the hash service, the versioning service,
  `PayrollDbContext`, the migration, and the tests `Pay054HashV2Tests`, `Pay054CloneCarriesEverythingTests`, `Pay054MigrationPostgreSqlTests`.

## P2 — create, import, and the version that governs a period

- Finished (UTC): 2026-11-23T09:39:43Z.
- **The pack:** `docs/syr/payroll-framework-packs/schema.v1.json` (the documented contract) and `law50-interim.json` (generated from
  the seeds' values, then checked against them by a test). The importer validates in code
  (`PayrollFrameworkPackValidator`); a test keeps the schema's required fields, enumerations and row properties in line with it.
- **Application** (`Payroll.Application/Features/PayrollConfigurations/`): `PayrollFrameworkPack` (model and reader: 512 KB, depth 16,
  unknown properties refused), `PayrollFrameworkPackValidator`, `PayrollFrameworkPackMapper`, and the commands import, create,
  set-effective-from and export (`PayrollFrameworkCommands.cs`). The import is one transaction; the actor is recorded as an editor.
- **Domain:** `ResolveGoverning` (Published and Superseded, an active code first, the latest date, the highest number, then the code
  and the id), `TryPublish` (Approved only, the version's own date, never on or before the published one's date, D-PAY-13),
  `TryActivate` (Published only), `PayrollFrameworkTemplate`, `PayrollFrameworkMessages`.
- **Repository:** `GetActiveConfigurationAsync` and `GetConfigurationByRegimeCodeAsync` are the resolver for today (Damascus,
  `IPayrollCalendar`); `GetConfigurationByIdAsync` loads every table; `GetFrameworkForUpdateAsync`.
- **Web:** `PayrollFrameworksApiController` (`api/payroll/frameworks`: antiforgery, list, get, create, import, export,
  effective-from; every POST and PUT with `[ValidateAntiForgeryToken]`), `PayrollConfigurationsController` (create, import, export,
  edit on a view model, activate; `[OrgScopeNotApplicable]`), the views `Create`, `Import`, `Details` (the four tables and the field
  sources with reference, status and exception), `Edit` (with the date), the dashboard's «لا يوجد إطار منشور», and the calculation's
  refusal naming the import page.
- **Operations:** `scripts/pay054-import-legal-pack.ps1` (ASCII, parses under pwsh 7); `scripts/deploy-organization.ps1` prints it.
- **Lists that shrank:** `tests/architecture/org-scope-allowlist/payroll.txt` lost `PayrollConfigurations` (it declares its stance);
  `tests/architecture/entity-binding-allowlist/payroll.txt` lost `PayrollConfigurations.Edit` (it binds a view model now).
- **Tests:** full unit suite 6713/6713 (9 min 19 s); architecture 371/371; payroll integration (not Testcontainers) 29/29;
  `Pay054FrameworkHttpTests` 2/2 on PostgreSQL through Payroll.Web; build `-warnaserror` 0/0; format clean.
  New: `Pay054FrameworkPackImporterTests` (50 planted defects, each refused with the field named, no echo, no row; 8 unreadable
  files; decisions; replace; export round trip; create; the date; schema in line; pack equals seed), `Pay054VersioningTests` (21),
  `Pay054FrameworkWritersTests`, `Pay054FrameworkEditFormTests`, and the framework rows of `PayrollWriteActionsTests`.
- **Found while testing:**
  - a re-import by the same person in the same instant marked his existing editor row as new (a duplicate key); the importer now
    marks only the rows it added;
  - **the edit form could not bind a fraction** under the service's Arabic culture (`0.5` for the end-of-service rate: PLAN-53's
    known decimal defect) and bound the entity; it is a view model now with the invariant decimal binder, so the form works and its
    `IAM-F` row left the list;
  - the shared in-process test host starts in Development and so seeds the Demo frameworks; it takes optional settings now, and these
    tests run with `Seed:Profile=None`.

## Decisions (with reasons)

Recorded as they are taken; see each phase.

- **D1 (P-7, the name clash):** `PayrollRegimeAndRulesTests.cs` needed a qualified name, so the engine's rule class
  `Engine.Rules.StatutoryDeductionRule` is renamed `StatutoryDeductionEngineRule`; the entity keeps the plan's name.
- **D2 (P-7, `LegalSource`):** `DisbursementPriorityRule.LegalSource` («المصدر القانوني») means the same as a legal reference: it is
  reused (the interface's `LegalReference` reads it); only `LegalStatus` and `InterimExceptionId` are added to that entity.
- **D3 (`HashSchemaVersion` in code):** the C# default is 1 and only the creation paths (create, clone, import) set 2, so the Demo
  and Acceptance seeds and the several hundred test fixtures stay v1 and compute as before. A test pins which product files may
  construct a `PayrollConfiguration` (P2).
- **D4 (column defaults live in the migration only):** the model declares no default for the new enum columns. With a model default,
  EF would treat the CLR default (`Signed`, `Approved`) as «not set» and write the database default instead (`Interim`), a silent
  wrong value. EF writes every value explicitly; the defaults fill the existing rows and plain-SQL inserts.
- **D5 (a legal reference that starts with `LD-` cites a decision):** the plan gives each value three fields (`legalReference`,
  `legalStatus`, `interimExceptionId`) and asks that every cited decision exist; so a reference is either a decision id or a free
  citation.
- **D6 (the effective date is inside fingerprint v2):** an approved version cannot change its date without changing its fingerprint.
- **D7 (`LegalDecisionRecords` unique index):** now `(TenantId, DecisionId)`; it was unique across agencies (baseline fact 1).
- **D8 (`PayrollRunJob.Log`):** «سجل المهمة» had no field; the job gains a short text log (the legacy path, the interim indicator, an
  expired exception). A refusal before a job exists is recorded on a job row of status `Failed`, which is not an active job.

- **D9 (which version governs a period, P-6):** as the prompt says, with two additions: the code's name and the id settle a tie
  (so the answer never depends on row order), and `GetConfigurationByRegimeCodeAsync` — a third resolver the plan does not list —
  is the same resolver for today, filtered by regime or code.
- **D10 (the last bracket has no ceiling):** «ascending with no gap» includes the gap above the last limit: the last bracket's
  upper limit must be the open-ended amount `9999999999999999.99` (the seeds' own value), or income above it would be untaxed.
- **D11 (a code keeps one regime):** an import or a create under a code that another regime already uses is refused.
- **D12 (the interim pack's insurance rules):** today's export finds the employee's insurance share by name («تأمين»): `DED-SOC`
  and `DED-HEALTH`. So the pack marks both `insuranceBody: GOSI` and gives `DED-SOC` the employer's 14%; a v2 run of the pack
  exports what a v1 run exports. Which body each deduction really belongs to (pension to GOIP) is D-PAY-1 / PAY-E.
- **D13 (errors never echo the file):** a pack error carries a JSON path cut at the first name the contract does not know, and an
  Arabic message that says what is required. The API's import reads its body itself, not through the model binder, for the same
  reason.
- **D14 (the exception number on the page):** the interface guard reads a printed `…Id` as a technical id. A value's interim
  exception number is a legal citation the reader looks up (like a decree number), so the details page prints it through the view
  model's `ExceptionCitation`; no row was added to the guard's lists.
- **D15 (the API's surface):** the plan lists four endpoints; `GET` list and `GET {id}` are added as reads, and the cycle's acts get
  API endpoints in P4 (the acceptance class and the server check run the cycle as three users).

## Server changes and their undo commands

None yet.

## Open issues

None.

## Exact next step

P3: the engine's v2 paths (statutory deductions from the version with base kind and ceiling, holidays, priority rules, the export's
insurance shares), the v1 legacy path with its job log line, no `DefaultBrackets` on a v2 path, `IsStatutory` read-only on the
deduction types screens; tests: the identity proof (seeded v1 against imported v2), a v2 run reads only its rows.

Started (UTC): 2026-11-23T07:26:43Z

# PLAN-54 progress (the agent's checkpoint)

Session: one, in the main folder `/home/omar/HRManagement` (Linux notebook), branch `feature/plan-54-pay-legal-framework-reference-data`.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 baseline | done | 2026-11-23T07:36:53Z |
| P1 data and fingerprint | done | 2026-11-23T08:50:07Z |
| P2 create, import, governing version | next | |
| P3 engine reads the version | | |
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

## Server changes and their undo commands

None yet.

## Open issues

None.

## Exact next step

P2: the schema and the pack (`docs/syr/payroll-framework-packs/`), the pack reader/validator/importer and the create command
(`Payroll.Application/Features/PayrollConfigurations/`), the identity proof, the API with its antiforgery endpoint, the screens,
`TryPublish`/resolver/`GetActiveConfigurationAsync`/`Activate`, first-run diagnostics, the operator's script.

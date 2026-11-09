# COD-001 — Dynamic Code Generation Engine

Central **SoR in HRCore** for business identifier policies. Separate from the policy engine (allow/deny).

## Architecture

| Layer | Location |
|-------|----------|
| Models | `HRManagement.SharedKernel/CodeGeneration/` |
| Engine + DB | `HR.Core.Infrastructure/Services/CodeGeneration/` |
| API | `POST /api/codes/generate`, `POST /api/codes/preview` |
| Remote client | `ICodeGenerationClient` in ServiceDefaults (HTTP → HRCore) |
| In-process (HRCore) | `ICodeGenerator` directly |

## Entity kinds

PLAN-48: the full list (`CodeEntityKind`), with the national template the seed publishes (`CodeGenerationSeeder.NationalPolicies`):

| Kind | Number | Counter |
|------|--------|---------|
| `OrgUnit` | `OU-…` (parent's code, then the sequence) | per parent |
| `FundedPosition` | `{OrgUnitCode}-{GradeCode}-###` | per (unit, grade), **no budget year** (D-TEN-6) |
| `CostCenter` | `{OrgUnitCode}` | **derived**: no counter, issued once per unit (D-TEN-7) |
| `AssetTag` | `AH-YYYY-######`; key `ASSET-PRIVILEGED`: `VIP-YYYY-######` | yearly; the keyed template has its own counter |
| `ItAssetTag` | `IT-YYYY-######` | yearly |
| `ArchiveReference` | `ARC-{EntityPrefix}-YYYY-#####` | yearly per prefix |
| `LegalCase` | `LC-YYYY-#####` | yearly |
| `ServiceTransaction` | `SRV-{TypeCode}-YYYY-#####` | yearly per type code |
| `IdCardSerial` | `ID-YYYY-#######` | yearly |
| `CitizenReference` | `CIT-YYYY-######` | yearly, **one national sequence** (issued in the national tenant: references are unique across tenants) |
| `InventorySku` | `SKU-YYYY-######` | yearly |
| `RecruitmentRequisition` | `REQ-{BudgetYear}-#####` | per budget year (D-TEN-5) |
| `OfferLetter` | `OFF-YYYY-#####` | yearly |
| `EstablishmentChangeRequest` | `EST-YYYY-…` | yearly |
| `JobCatalogEntry` | `JC-…` | per scope |
| `AdministrativeDecision` | `DEC-{TypeCode}-YYYY-#####` | yearly per type code |
| `RecruitmentCompetition` | `CMP-YYYY-…` | yearly |
| `ForensicCase` | **no national policy**: refused (422) until one is published | — |

**Not generated:** `NationalId`, MDM catalog codes, payroll regime codes.

## Resolution order, and no silent fallback (PLAN-48)

`CodeGeneratorService` resolves the policy of a request in this order, through the national read (`OwnOrNational`):

1. the tenant's overlay (`Scope = TenantOverride`) of the kind and policy key;
2. the national template (national tenant, `Scope = National`) of the kind and policy key;
3. none.

A request without a key gets the kind's default template (no key) and never a keyed one; an unknown key is a missing policy. A
template with no version in force is passed over. An invalid published version is refused (`CodePolicyInvalidException`, 422).

With no policy, `CodeGeneration:MissingPolicy` decides (D-TEN-9): `Refuse` everywhere except `Development` and `Testing`, where
`Fallback` builds `{kind prefix}-YYYY-#####` from `FallbackPrefix` (never an empty text). Configuration can only make it
stricter; no environment means `Refuse`. The check comes before any counter moves. A fact the number shows is required
(`CodeFactMissingException`, 422), never an empty segment. A number already issued in the tenant is `CodeCollisionException`
(409). HR.Core's `NationalScopeErrorFilter` answers all of them with an Arabic message.

## The policy validator (PLAN-48, G-COD-03)

`CodePolicyValidator` checks every seeded, published and overlay version: the segments parse; exactly one `Sequence` (none for a
derived kind); every scope field of the sequence is shown in the number; a yearly reset shows the year; a per-scope reset has
scope fields; the number starts with a literal prefix; one default overlay per kind and one national default per kind. The
scope key of a counter is built in one place, `CodeScopeKey` (tenant, kind, policy key, scope values, year), for the generator,
the bootstrap and the reseeder alike.

Existing databases were corrected once at start-up (`CodePolicyUpgrades`, recorded as `ten048-code-policy-upgrade-v1` in
`SeedPackApplications`): `FundedPosition` v1 → v2 (no budget year in the counter) and `RecruitmentRequisition` v1 → v2
(`REQ-{BudgetYear}`, per budget year). Then every tenant's counters are reseeded from the numbers already issued
(`CodeCounterReseeder`): a counter is raised to the highest issued number of its scope and never lowered (`GREATEST`); a number
the formats cannot parse is reported, never guessed.

## Policy format (segments JSON)

```json
[
  { "kind": "Field", "value": "OrgUnitCode" },
  { "kind": "Literal", "value": "-" },
  { "kind": "Sequence", "padWidth": 3, "scopeFields": ["OrgUnitCode"] }
]
```

Kinds: `Literal`, `Field`, `Date`, `Sequence`, `ParentCode`, `LookupPrefix`.

## Adding a new consumer

1. Call `AddHrCodeGeneration(configuration)` in `Program.cs`. It needs `Integration:HrCoreBaseUrl`: outside Development
   a missing URL stops the service at start-up with a message naming the key, and `localhost` is refused in a container
   (PLAN-37, `HrCoreEndpoint`).
2. **Update `deploy/wiring/service-wiring.json` in the same commit** (PLAN-37 contract): the registration in the
   service's `registrations`, the call in its `calls` (endpoint `hr-core.codes.generate`, scope `hr-core.workflow`) and
   the scope in its `scopes`. `ProductionWiringMatrixTests` fails until compose, k8s and the Keycloak scripts carry it;
   see [SERVICE_WIRING_MATRIX.md](SERVICE_WIRING_MATRIX.md).
3. Interactive screens use `CodeGenerationGateway.GenerateOrErrorAsync(client, CodeGenerationRequests.XXX())` and show
   the error. A message consumer that must not continue without the number uses `GenerateRequiredAsync`: it throws
   `CodeGenerationUnavailableException` (a transient fault, not in the bus ignore list), so the message is retried and
   then dead-lettered instead of being acknowledged without its record (G-COD-06; the Records.Archive consumers count
   failures in `archive_reference_failures_total`).
4. **Do not** allow manual entry of generated codes in UI after issuance.
5. Seed a national policy in `CodeGenerationSeeder` if needed.

## Admin UI

`/CodePolicies` — list policies, preview dry-run, view issuances log.

## CI

```powershell
./scripts/code-generation-validate.ps1
```

## Configuration

| Key | Values |
|-----|--------|
| `CodeGeneration:DefaultMode` | `Required` (prod), `Off` (tests only) |
| `CodeGeneration:MissingPolicy` | `Refuse` (default; the only value outside Development and Testing), `Fallback` (Development and Testing only) — PLAN-48 |
| `Integration:HrCoreBaseUrl` | HRCore base URL for remote services; required outside Development (PLAN-37). `Services:HRCore:BaseUrl` / `HrCore:BaseUrl` still work with a start-up warning (deprecated, removed in wave 2) |

## Relation to PROD-001 / MDM

- **Policy engine** decides *permission*; `SelectCodePolicy` effect chooses which code policy key to apply (see below).
- **MDM-001** codes remain manually curated national reference data.

## SelectCodePolicy (policy → code bridge)

When a policy rule emits:

```json
{ "effect": "SelectCodePolicy", "params": { "policyKey": "ASSET-PRIVILEGED" } }
```

Consumers read `decision.GetCodePolicyKey()` and pass it to `CodeGenerationRequest.PolicyKey`.

**Pilot:** Admin Logistics `AssetsController` on asset create — privileged assets get `VIP-YYYY-######` via `ASSET-PRIVILEGED` template.

## IT asset tags

`UserDevice.AssetTag` uses `CodeEntityKind.ItAssetTag` (`IT-YYYY-######`). Registered in `IT.Infra.Web/Program.cs`.

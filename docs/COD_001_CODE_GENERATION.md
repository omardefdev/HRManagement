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

`OrgUnit`, `FundedPosition`, `AssetTag`, `ItAssetTag`, `ArchiveReference`, `LegalCase`, `ServiceTransaction`, `IdCardSerial`, `CitizenReference`, `CostCenter`

**Not generated:** `NationalId`, MDM catalog codes, payroll regime codes.

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

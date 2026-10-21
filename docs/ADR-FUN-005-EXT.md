# ADR-FUN-005-EXT — Asset custody extensions (Phase 5)

## Status

Accepted — implements post-FUN-005 extensions outside the original UI closure scope.

## Context

FUN-005 UI closure ([ADR-FUN-005](ADR-FUN-005-identity-assets-it-closure.md)) delivered employee picker, text evidence, MarkLost, Index/Details actions, and single-holder rules. Phase 5 adds optional platform integrations without replacing the direct assign path required by UAT U6–U7.

## Decision

### Track 1 — PDF handover receipt + FILE-002 evidence

- `AssetAssignment.HandoverFileId` / `ReturnFileId` registered via FILE-002 (`admin-logistics` holder keys).
- `AssetHandoverRules.HasProof` — text ≥ 2 chars **or** file; rejects placeholder `"موقّع"`.
- QuestPDF `HandoverReceiptPdf` action; employee name from S2S lookup when configured.

### Track 2 — Optional workflow approval

- `AssetAssignmentStatus`: `PendingApproval` → `Active` / `Rejected`.
- Triggered only when `AssetApprovalRules.RequiresApproval` (`IsPrivileged` or `Value > 10_000`).
- HRCore workflow definition `ASSET-ASSIGN-APPROVAL`; completion via `IAssetAssignmentApproved` / `IAssetAssignmentRejected` MassTransit events.
- **Direct assign remains default** for non-privileged, low-value assets.

### Track 3 — Agency-level procurement lineage

- `Asset.OriginReceiptRecordId` FK for lineage display; optional `OriginSupplyOrderLineId` for per-line asset wizard.
- **Mandatory policy (FUN-005-EXT completion):** new assets require receipt origin **or** `IsLegacyEntry` + `LegacyEntryReason` (≥ 5 chars). Grandfathered assets without origin remain valid.
- `SupplyOrderLine` structured lines; `ReceiptInventoryService` increments `InventoryItem.Quantity` on receipt (single-item or per-line via `CreateFromOrder`).
- **Full chain:** `Fulfill` → `ReceiptRecords/CreateFromOrder` → `CreateFromReceipt` (redirects to Edit) — **not** national inventory.
- UI: path picker on `Assets/Create`, legacy form, Index origin column + «بدون مصدر» audit filter, receipt asset counter wizard.

### Track 4 — IEmployeeLookup S2S

- `svc-admin-logistics` with scopes `hr-core.lookup`, `hr-core.workflow`, `file-storage.references`.
- Server-side names in Index/Details/PDF; `display-names.js` retained as fallback.

## Consequences

- Migrations required on `admin_db`.
- Keycloak client `svc-admin-logistics` must exist in dev/prod realms.
- Workflow path depends on HRCore + RabbitMQ; misconfiguration falls back to logged warning (workflow id null) but assignment stays pending.

## Docker (اختبار محلي)

```powershell
# من جذر المستودع — يبني ويشغّل المكدس الكامل
.\scripts\fun005ext-docker-up.ps1

# بعد تعديل قالب Keycloak (أول مرة أو بعد إضافة svc-admin-logistics):
.\scripts\fun005ext-docker-up.ps1 -RecreateKeycloak
```

**Compose files:** `docker-compose.yml` + `docker-compose.services.yml` + `docker-compose.local.yml`

**متغيرات Admin.Logistics في Docker:**

| متغير | الغرض |
|--------|--------|
| `Integration__HrCoreBaseUrl` | lookup + workflow S2S |
| `HrGateway__InternalBaseUrl` | FILE-002 references |
| `ServiceIdentity__ClientId` | `svc-admin-logistics` |
| `ServiceIdentity__Scope` | lookup + workflow + file refs |

## UAT mapping

| ID | Scenario |
|----|----------|
| E1 | Privileged assign + clean PDF upload → `HandoverFileId` set |
| E2 | PDF shows employee name (not GUID) |
| E3 | Privileged/high-value → PendingApproval → approve → InUse |
| E4 | Create asset from receipt → `OriginReceiptRecordId` visible; supply chain via Fulfill → CreateFromOrder |
| E4b | Legacy asset without receipt; Index filter «بدون مصدر» for grandfathered audit |
| E5 | Index shows holder name without JS |
| E6 | FILE-002 rejects PendingScan files |

## Out of scope

- EXT-004 legal PKI signatures
- National procurement / SKU federation
- Auto-upload generated PDF to FILE-002

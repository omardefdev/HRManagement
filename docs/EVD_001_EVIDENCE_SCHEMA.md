# EVD-001 — Evidence Schema

## Entity: `EvidenceRecord`

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| TenantId | Guid | Agency boundary |
| CorrelationId | string(128) | Trace / business correlation |
| CausationId | string(128)? | Parent event/evidence |
| Actor | string(256) | User/service identity |
| Subject | string(256)? | Employee / resource subject |
| Resource | string(512) | Entity type + id |
| Purpose | string(256) | Processing / decision purpose |
| AssuranceLevel | string(64)? | e.g. AAL2 |
| Policy / Rule / Reason | strings | Decision context |
| Result | string(64) | Allowed / Denied / Completed / … |
| BeforeHash / AfterHash | string(128)? | State digests |
| EvidenceLinksJson | text? | Related ids |
| ReleaseVersion / ConfigVersion | string(64)? | Deployment binding |
| OccurredAtUtc | DateTimeOffset | Event time |
| TimeSource | string | `system-clock` (default) |
| TimeZoneId | string | Default `UTC` |
| ChainKey / Sequence | string / long | Chain partition |
| PrevHash / EventHash | string(128) | SHA-256 seal |

## Reconstruction recipe (hire example)

1. Find all rows with `CorrelationId = hire-<txn>` ordered by `Sequence`.
2. Verify each `EvidenceHashChain.Verify(row)` and `PrevHash` linkage.
3. Walk `EvidenceLinksJson` to contracts, files, role grants, and legacy `EntityAuditLog` ids.
4. Confirm `ReleaseVersion` matches the deployment that executed the journey.

## Opt-in for a service

```csharp
modelBuilder.ConfigureEvidenceRecords();
// DI:
services.AddScoped<IEvidenceCollector, EvidenceCollector>();
services.AddSingleton<EvidenceImmutabilityInterceptor>();
// register interceptor on UseNpgsql / AddDbContext
```

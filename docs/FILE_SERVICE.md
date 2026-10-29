# File Storage Platform — Storage Platform Architecture

## Upload flow (browser-direct)

1. Browser POST `multipart/form-data` to `/api/files` (via Gateway)
2. Response: `{ fileId, originalName, size, mimeType }`
3. Form submits `FileId` (hidden field) to domain service
4. Domain service saves `FileId` and calls `POST /api/files/{fileId}/references`

## API

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/files` | Upload file |
| GET | `/api/files/{fileId}` | Metadata |
| GET | `/api/files/{fileId}/download` | Redirect to presigned URL |
| GET | `/api/files/{fileId}/presigned-url` | Signed URL (not stored) |
| POST | `/api/files/{fileId}/references` | Register reference `{ holderKey }` |
| DELETE | `/api/files/{fileId}/references` | Release reference |
| DELETE | `/api/files/{fileId}` | Delete when refCount=0 |
| POST | `/api/files/batch/metadata` | Batch metadata |
| GET | `/api/files/metrics` | Monitoring metrics |
| POST | `/api/files/service-upload` | S2S upload (`file`, `constraintProfile=CandidateDocument`; scope `file-storage.service-upload`, PLAN-30) |
| POST | `/api/files/service-upload/metadata` | S2S metadata of the caller's own service uploads `{ fileIds }` |
| GET | `/api/files/service-upload/{fileId}/content` | S2S content of the caller's own service upload, `Clean` only (else 404) |

## Reference holderKey format

`{service}:{EntityType}:{entityId:N}:{fieldName}`

Example: `hr-core:Employee:3fa85f64-5717-4562-b3fc-2c963f66afa6:PhotoFileId`

## Garbage collection

- Pending uploads with no references after `OrphanUploadTtlHours` (24 h) → marked `PendingPurge`
- Active files with refCount=0 after `ReleaseGraceHours` (24 h) → marked `PendingPurge`
- Purge queue re-checks live references and retention, deletes the object, then sets `Deleted`

`FileGarbageCollectorJob` (hourly, advisory lock) runs without a request tenant. It reaches tenant-owned files only when
`FileStorage:GarbageCollection:AllTenants=true` (env `FileStorage__GarbageCollection__AllTenants`, **default `false`**):
then `CollectGarbageAsync(allTenants: true)` and `ProcessPurgeQueueAsync(allTenants: true)` read `Files`,
`FileReferences` and `FilePurgeWorkItems` through `IgnoreTenantFilterForSystem(BackgroundQueueProcessing)` and keep each
purge item in its file's tenant. With `false` the job touches no tenant-owned file (the behaviour before the fix) and
logs a warning at start. Calls from a request (`POST api/files/admin/process-purge`, `CollectGarbageAsync()`) stay inside the
request tenant.

**Enable order (the guard exists because of it).** A `Pending` file is an orphan only if its Register never arrives.
Before the outbox fix, HR.Core's Register rows were never delivered, so candidate documents and offer evidence are
`Pending` and older than 24 h although they are in use. Collecting them first would delete them.

1. Deploy HR.Core with the per-tenant file-reference outbox dispatcher and File.Storage with `AllTenants` unset.
2. Wait until HR.Core's backlog is delivered (`hr_core_db`):
   `SELECT "Status", "Operation", COUNT(*) FROM "FileReferenceOutbox" GROUP BY 1, 2;` — no `Pending` Register rows left
   except new ones. Look at every `Dead` Register row: its file is still `Pending` and would be collected 24 h later. Fix
   the cause, then re-queue: `UPDATE "FileReferenceOutbox" SET "Status"='Pending', "Attempt"=0, "NextAttemptAt"=now()
   WHERE "Status"='Dead' AND "Operation"='Register';`
3. Only then set `FileStorage__GarbageCollection__AllTenants=true` on file-storage and restart it.

The same applies whenever a holder's Register delivery is down for longer than the TTL: a Register row goes `Dead` after
`FileStorage__ReferenceOutbox__MaxAttempts` (10, about 35 min of backoff) and is not retried by itself.

**Production RLS caveat.** Production app roles are `NOBYPASSRLS` (`docker/init-service-roles.sh`) and neither job sets
`app.current_tenant`, so on RLS-hardened tables both the HR.Core dispatcher and the GC job read no rows in Production —
the open item "Production RLS for cross-tenant jobs" (PLAN-30 deferred follow-ups) that also blocks the scan queue and the
recruitment jobs. The fixes work where the connection bypasses RLS (Development/acceptance owner or superuser).

## Domain services

Domain services **never** upload files. They store `FileId` only and use `IFileReferenceClient` for reference management.

Exception (PLAN-30, FS-030): HR.Core relays anonymous candidates' documents through `/api/files/service-upload` with its
service identity, because the candidate cannot call File.Storage. See `docs/SERVICE_TRUST_INVENTORY.md`.

## Service upload (FS-030, PLAN-30)

| Item | Value |
|------|-------|
| Controller | `File.Storage.Web/Controllers/FilesServiceUploadController.cs` (logic `File.Storage.Infrastructure/Services/FileServiceUploadService.cs`) — separate from `FilesApiController`, so its class-level `ModuleFileStorage` policy does not apply |
| Policy | `ServicePolicies.FileStorageServiceUpload` (`Service.FileStorageServiceUpload`): service account + scope `file-storage.service-upload` (audience `api-file-storage`) + allowed client `svc-hr-core` only; no existing policy changed |
| Headers | `Authorization: Bearer <svc-hr-core client_credentials token>`, `X-Tenant-ID` (required, 400 otherwise), caller proof (`GatewayCallerProof.Sign`) outside Development |
| Profile | `constraintProfile=CandidateDocument`: 5 MiB, `.pdf .jpg .jpeg .png`, matching content type and magic bytes; `[RequestSizeLimit]` 5,308,416 bytes (5 MiB + 64 KiB multipart headroom) |
| Path | `FilePlatformService.UploadAsync` (quarantine bucket + scan queue; status starts `PendingScan`) |
| Grant | HolderType `Service`, HolderId = caller client id, Module `hr-core`, Visibility `Module`, GranteeId = caller `sub`; metadata/content serve only the caller's own service uploads in the request tenant (24 h grant expiry ignored), content only when `Clean`, otherwise 404 |
| Migration | none (no entity change) |

HR.Core calls it **directly** at `Integration:FileStorageBaseUrl` (not through the gateway) with `CandidateDocumentUploadClient`
(named client `CandidateDocumentUpload`, 30 s; empty base URL → fails closed). Uses:

- candidate documents on the public apply form and on `/careers/my` (holder `hr-core:CandidateDocument:{id:N}:FileId`);
- HR on-behalf offer evidence (`OfferOnBehalfService`, holders `hr-core:OfferLetter:{id:N}:AcceptanceEvidence` /
  `…:DeclineEvidence`), relayed server-side because HR users lack `ModuleFileStorage`.

HR users download through **HR.Core proxies**, never `/api/files`: `RecruitmentApplications/CandidateDocument/{id}` and
`OfferLetters/Evidence` (tenant check + metadata check + content via the service identity; `Clean` only,
buffered ≤ 5 MB). References are registered/released through the HR.Core file-reference outbox with an explicit tenant.

Fixed (after PLAN-30): `FileReferenceOutboxDispatcherJob.ProcessBatchAsync` now reads every tenant's due rows through
`IgnoreTenantFilterForSystem(BackgroundQueueProcessing)` and sends each row's `TenantId` as `X-Tenant-ID`
(`ITenantFileReferenceDelivery`; a row without `TenantId` goes `Dead`), so candidate documents, offer evidence and retention
releases reach File.Storage and become `Active` / are released in their own tenant. The GC job can collect tenant-owned
orphans once `FileStorage__GarbageCollection__AllTenants=true` is set **after** that delivery is live (see Garbage
collection: enable order). Both still read nothing in Production under `NOBYPASSRLS` until the cross-tenant-job RLS item is
solved. Still open: production compose does not yet give hr-core the `file-storage.service-upload` scope or
`Integration__FileStorageBaseUrl`.

## Access grants on RegisterReference

`POST /api/files/{id}/references` calls `FileAccessGrantService.CreateGrantAsync(…, Visibility = Module)`, which returns
the file's existing non-revoked grant when there is one. So:

- a browser upload (`POST /api/files`) keeps its `Uploader`/`Private` grant (grantee = uploader) after Register;
- a service upload keeps its `Service`/`Module` grant (module `hr-core`, holder = client id);
- only a file with no live grant (legacy rows, or a file kept by legal hold / `RetainUntil` after its last release revoked
  its grants, then registered again) gets a Module grant derived from the holder key.

Decision: **RegisterReference does not add a Module grant to API-uploaded files.** Access is decided by the newest grant
(`FindGrantByFileAsync`), so a second Module grant would silently widen a user's private upload to every principal whose
roles match the holder's module, chosen by whoever registers the reference rather than by the owner. Holders that need
their users to read a file serve it through their own endpoint (as HR.Core does for candidate documents and offer
evidence) or create the upload with the right visibility. The per-tenant outbox fix does not change this: the grant is
created in the request tenant, which is now the row's tenant.

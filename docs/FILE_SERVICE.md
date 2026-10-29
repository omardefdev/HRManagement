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

- Pending uploads with no references after 24h → deleted
- Active files with refCount=0 after grace period → deleted

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

Known issues (pre-existing): `FileReferenceOutboxDispatcher` reads the outbox under the tenant filter in a background scope
and sends no `X-Tenant-ID`, so tenant-stamped Register/Release rows probably never reach File.Storage; `CollectGarbageAsync`
has no tenant bypass, so tenant-owned orphan `Pending` files are not collected. Production compose does not yet give hr-core
the `file-storage.service-upload` scope or `Integration__FileStorageBaseUrl`.

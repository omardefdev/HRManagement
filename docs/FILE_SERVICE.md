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

## Reference holderKey format

`{service}:{EntityType}:{entityId:N}:{fieldName}`

Example: `hr-core:Employee:3fa85f64-5717-4562-b3fc-2c963f66afa6:PhotoFileId`

## Garbage collection

- Pending uploads with no references after 24h → deleted
- Active files with refCount=0 after grace period → deleted

## Domain services

Domain services **never** upload files. They store `FileId` only and use `IFileReferenceClient` for reference management.

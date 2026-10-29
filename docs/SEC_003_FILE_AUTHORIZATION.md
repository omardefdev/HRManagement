# SEC-003 — Object-Level File Authorization

## Objective

Eliminate BOLA/IDOR vulnerabilities in the file platform. Every file operation (download, delete, reference) must be authorized against the `FileAccessGrant` model — no file is accessible just because the user is authenticated.

## Design

### FileAccessGrant (SharedKernel)

| Property | Purpose |
|---|---|
| `TenantId` | Tenant isolation boundary |
| `Module` | Owning bounded context (e.g. HR.Core, Payroll) |
| `HolderType` / `HolderId` | Domain entity that owns the file |
| `FileReference` | Storage-level object key |
| `Visibility` | Private / Module / Tenant / Public |
| `GranteeId` | Optional explicit identity grant |
| `PresignedUrlMaxLifetimeSeconds` | ≤ 300 seconds |
| `IsRevoked` / `RevokedAtUtc` | Revocation tracking |

### IFileAccessPolicy / FileAccessPolicy (ServiceDefaults)

Evaluation order:
1. **Revoked** → deny.
2. **Cross-tenant** → deny (no information leakage — same 403 as "not found").
3. **Admin / PlatformAdmin** → allow.
4. **Public visibility** → allow.
5. **Tenant visibility** → allow any authenticated user in same tenant.
6. **Module visibility** → allow if user has matching module role.
7. **Explicit grantee** → allow if `sub` matches.
8. **Holder match** → allow if `employee_id` matches `HolderId`.
9. Otherwise → deny.

Delete is restricted to Admin, PlatformAdmin, or FileStorage service role.

### Presigned URLs

- Maximum lifetime: 300 seconds (5 minutes).
- URLs are bound to the grant; revoked grants invalidate URLs.
- The client cannot specify the holder — the server derives it from the authenticated context.

### Service upload endpoint (FS-030, PLAN-30)

`api/files/service-upload` (`FilesServiceUploadController`) is authorized by the service policy
`Service.FileStorageServiceUpload` only: a service account token with scope `file-storage.service-upload` from client
`svc-hr-core` (401 without/invalid token; 403 for another client, a missing scope or an interactive user). The scope grants
nothing on `FilesApiController`. Object-level rules:

- the tenant comes from the required `X-Tenant-ID` header (service tokens carry no tenant claim);
- the upload creates one `FileAccessGrant` (HolderType `Service`, HolderId = calling client id, Module `hr-core`,
  Visibility `Module`, GranteeId = caller `sub`);
- `metadata` and `content` answer only for files that the **same client** service-uploaded in the **same tenant** (grant not
  revoked, file not deleted); anything else is silently omitted (metadata) or 404 (content) — no existence leak;
- content is served only when `ScanStatus = Clean`; checksum mismatch → 409.

End users never reach these files directly. HR.Core exposes them through its own actions, which apply the HR policy and the
tenant filter first: `RecruitmentApplications/CandidateDocument/{id}` (candidate documents) and `OfferLetters/Evidence`
(signed on-behalf acceptance/decline evidence, PLAN-30 J), both Clean-only. Candidates on `/careers/my` can upload missing
documents (relayed by HR.Core) but never download through the portal.

## Acceptance criteria mapping

| Criterion | Implementation |
|---|---|
| Cross-tenant/unit user cannot read metadata or distinguish 403 from existence | `FileAccessPolicy.Evaluate` returns uniform deny |
| Delete restricted to owning service or admin | `EvaluateDelete` checks Admin/PlatformAdmin/FileStorage |
| Presigned URL ≤ 5 min, invalid after revocation | `PresignedUrlMaxLifetimeSeconds` capped at 300; `IsRevoked` check |
| Matrix tests: owner/non-owner/cross-tenant/admin/service | `Sec003FileAuthorizationTests` |

## Validation

```powershell
scripts/sec003-validate.ps1
```

## Related cards

- IAM-001 (JWT / authentication)
- IAM-002 (org scope)
- TEN-001 / TEN-002 (tenant isolation)
- FILE-001 / FILE-002 (file lifecycle / scanning)

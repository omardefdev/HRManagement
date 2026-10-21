# خطة التنفيذ — PLAN-11 (DSAR + فحص الملفات + منح الوصول + S2S)

تنفيذ [PLAN-11](../PLAN-11-file-dsar-s2s.md) — AC-27..AC-30. يعتمد على [PLAN-00](PLAN-00-EXECUTION.md).

## المخرجات

| AC | مكوّن | تحقق |
|----|--------|------|
| AC-27 | `DsarDataCollectorService` + `POST .../fulfill` + `GET .../package` | حزمة masked + `packageContentHash` + audit |
| AC-28 | `acceptance-file-scan.ps1` + `FileScanLiveE2ETests` | Clean ⇒ 200؛ EICAR ⇒ deny |
| AC-29 | `FileAccessGrant.ExpiresAtUtc` + policy | `grant-expired` / `wrong-grantee` + audit |
| AC-30 | `S2sFileReferenceRejectionTests` + script | 403 + reference count ثابت |

## W11.1 — AC-27

- `Compliance.Infrastructure/Privacy/DsarDataCollectorService.cs` — fan-out HR.Core / Payroll / File.Storage
- `DsarInternalController` على HR.Core / Payroll / File.Storage
- `DsarRequestsController` — `/fulfill`, `/package`, collector على status Fulfilled
- `AddComplianceDsarCollector` في `Compliance.Web/Program.cs`
- `DsarDataCollectorTests`, `Ac27DsarExportTests`

## W11.2 — AC-28

- `scripts/acceptance-file-scan.ps1`
- `FileScanLiveE2ETests` (+ ClamAV skip when `CLAMAV_HOST` unset)
- `Ac28FileScanTests`

## W11.3 — AC-29

- `ExpiresAtUtc` + migration `20260824190000_SEC003_GrantExpiresAt`
- `FileAccessPolicy` — expiry + `wrong-grantee`
- `PresignedUrlExpiryMinutes: 5` في appsettings
- `Sec003FileAccessIntegrationTests` + `Ac29FileAccessExpiryTests`

## W11.4 — AC-30

- `S2sFileReferenceRejectionTests` (WebApplicationFactory + test service headers)
- `scripts/acceptance-s2s-negative.ps1`
- `Ac30S2sFileReferenceRejectionTests`

## تعريف الإنجاز

- DSAR fulfill ينتج JSON masked بلا national id خام.
- فحص Clean/EICAR مع أدلة `acceptance-evidence/AC-28/`.
- منحة منتهية / مستخدم خاطئ ⇒ deny + audit.
- token S2S خاطئ ⇒ لا صف مرجع جديد.
- `acceptance-evidence/AC-27/` .. `AC-30/` = Pass (أو fallback موثّق لاختبارات التكامل).

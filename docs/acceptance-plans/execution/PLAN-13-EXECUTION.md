# PLAN-13 execution — ops quality (AC-34..AC-37)

## Delivered

### W13.1 — AC-34 backup/restore drill
- `scripts/acceptance-backup-restore.ps1`
- `scripts/verify-restore-relationships.sql` + `.sh`
- Optional `docker-compose.dr-drill.yml` (Postgres :5434)
- Updated `docs/OPS_BACKUP_VERIFICATION.md`

### W13.2 — AC-35 scoped k6 perf
- `perf/k6/lookup-list-scoped.js` (branch persona token)
- `scripts/nfr-k6-summarize.ps1` (p50/p95/p99 + error rate, `slo_claimed: false`)
- `scripts/acceptance-perf-run.ps1`
- `nfr-k6-run.ps1` accepts `lookup-list-scoped` scenario

### W13.3 — AC-36 Arabic PDF
- `docs/acceptance-plans/checklists/AC-36-arabic-pdf-visual.md`
- `scripts/acceptance-pdf-arabic.ps1`
- Extended `ArabicPdfGenerationTests` + `Ac36ArabicPdfEvidenceTests`

### W13.4 — AC-37 audit journey rebuild
- `GatewayAuditEntry` tenant/scope (migration `PLAN01_GatewayAuditScopeFields` — pre-existing)
- `JourneyRebuildService` + `GET /api/platform/audit/journey/{correlationId}`
- `scripts/obs-audit-journey-rebuild.sh` + `scripts/acceptance-audit-journey.ps1`

### W13.5 — Tests + evidence contract
- `Ac34BackupRestoreDrillTests`, `Ac35ScopedLookupPerfTests`, `Ac36ArabicPdfEvidenceTests`, `Ac37JourneyRebuildTests`
- Live acceptance: `Ac34..Ac37` SkippableFact tests

## Verify

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Ac3[4-7]|ArabicPdf"

# Live (stack required):
pwsh scripts/acceptance-backup-restore.ps1
pwsh scripts/acceptance-perf-run.ps1 -Profile smoke
pwsh scripts/acceptance-pdf-arabic.ps1
pwsh scripts/acceptance-audit-journey.ps1

$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~Ac3[4-7]"
```

## DoD
- AC-34: restore drill archived + relationship checks + `measured_restore_seconds`
- AC-35: p50/p95/p99 measured; CAP-PERF-01 Deferred in result.md
- AC-36: 3 PDF samples + visual checklist
- AC-37: journey rebuild with tenant/scope; reference journey gaps empty when audit present

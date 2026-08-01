# FUN-006 — تحقق الإغلاق

| تحقق | دليل |
|---|---|
| ADR + matrix | `ADR-FUN-006-*.md`, `fun-archive/journey-matrix.json`, `FUN_006_JOURNEY_MATRIX.md` |
| hold + approved disposal + custody/hash | ArchiveRetentionRules, ArchiveItemsController, CustodyEvent |
| org dims + rebuild/freshness | AnalyticsEvent.OrgUnitId, ReadModels.Rebuild, AnalyticsProjectionRules |
| branch ≠ national + ExportJob | WorkforcePlanningController, Analytics/Archive ExportJob |
| Tests | `Fun006*` |
| UAT | `FUN_006_UAT_PACK.md` ReadyForSign-off |
| Backlog | FUN-006 Done في `04-P3` |

**الحالة:** Done.

# FUN-005 — تحقق الإغلاق

| تحقق | دليل |
|---|---|
| ADR + matrix | `ADR-FUN-005-*.md`, `fun-identity/journey-matrix.json`, `FUN_005_JOURNEY_MATRIX.md` |
| delivery proof + party revoke + lost/renew | IssuedCardsController, SeparationPartyAckConsumer, CardIssuanceWorkflow |
| single open holder + lost | AssetAssignmentRules, filtered unique index, MarkLost |
| provision fail/retry + device detach | ProvisioningStatus, RetryProvision, SeparationCompletedConsumer |
| Tests | `Fun005*` |
| UAT | `FUN_005_UAT_PACK.md` ReadyForSign-off |
| Backlog | FUN-005 Done في `04-P3` |

**الحالة:** Done.

## Auth.Directory Retry (ضيق)

إعادة محاولة حساب الدليل للمستخدم تتم عبر `AuthUsersController.SyncFromKeycloak` القائم — لا إعادة بناء IAM-004. تذاكر IT تستخدم `RetryProvision` لمسار `IDirectoryEntrySyncService`.

# GOV-003 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + SoR matrix | [ADR-GOV-003](./ADR-GOV-003-master-data-governance.md), [SOR](./GOV_003_SOR_MATRIX.md), `field-ownership.json` | Ready |
| عقود بيانات | [master-employee.v1](./data-contracts/master-employee.v1.md), [master-orgunit.v1](./data-contracts/master-orgunit.v1.md) + `Gov003OwnershipContractTests` | OK |
| Merge/Unmerge + duplicates | `MasterDataDuplicateCase`, `api/master-data/duplicates`, Unmerge API؛ `Gov003MergeUnmergeTests` | OK |
| جودة | [GOV_003_DATA_QUALITY](./GOV_003_DATA_QUALITY.md) + `gov003-data-quality-report.py` | OK |
| Lineage | [GOV_003_LINEAGE](./GOV_003_LINEAGE.md) + `lineage.json` | OK |

## خارج النطاق

MDM-001/002، MIG-004، ANA-001 BI، FUN-006 إغلاق أرشيف؛ Position catalog.

## إغلاق

**GOV-003 = Done**؛ **TEN-003 = Done** شكلياً مع أدلة threat-model/scripts. تحديث TRACEABILITY.

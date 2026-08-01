# MIG-002 — Code maps (human view)

Every legacy value has disposition **Map**, **Transform**, or **Quarantine**. Catch-all `Other` is forbidden as a mass dump.

| mapId | source | field | value | target | disposition |
|---|---|---|---|---|---|
| CM-STATUS-A | legacy-hr-batch | STATUS_CODE | A | Active | Map |
| CM-STATUS-T | legacy-hr-batch | STATUS_CODE | T | Terminated | Map |
| CM-STATUS-S | legacy-hr-batch | STATUS_CODE | S | Suspended | Map |
| CM-STATUS-XX | legacy-hr-batch | STATUS_CODE | XX | — | Quarantine |
| CM-ORG-HQ / BR1 | legacy-org-files | ORG_CODE | HQ/BR1 | same | Map |
| CM-ORG-LEGACY-Z | legacy-org-files | ORG_CODE | Z-OLD | — | Quarantine |
| CM-JOB-CLERK | legacy-hr-batch | JOB_TITLE | كاتب | free text | Transform |
| CM-JOB-BLANK | legacy-hr-batch | JOB_TITLE | (empty) | — | Quarantine |
| CM-REGIME-50 / CS26 | legacy-payroll | REGIME | … | LAW_50_2004 / CIVIL_SERVICE_2026 | Map |
| CM-GENDER-M/F | legacy-hr-batch | GENDER | M/F | Male/Female | Map |
| CM-NAME-TRIM | legacy-hr-batch | FULL_NAME | * | * | Transform |
| CM-DOC-IDCARD | legacy-docs-share | DOC_TYPE | ID | NationalIdCopy | Map |

Signing: `mappingVersion` + `contentHash` in matrix; see signoff fixture.

# MIG-001 — Target field source of truth

Joins GOV-003 platform writers to legacy extract fields. Mapping transforms = MIG-002.

| targetField | legacy source | sourceField | gov003 |
|---|---|---|---|
| Employee.NationalId | legacy-hr-batch | NAT_ID | field-ownership |
| Employee.FullName | legacy-hr-batch | FULL_NAME | field-ownership |
| Employee.CurrentStatus | legacy-hr-batch | STATUS_CODE | field-ownership |
| Employee.PrimaryOrgUnitId | legacy-org-files | ORG_CODE | field-ownership |
| OrgUnit.Code | legacy-org-files | ORG_CODE | field-ownership |
| EmploymentRecord.PositionTitle | legacy-hr-batch | JOB_TITLE | field-ownership |
| EmployeePayProfile.BaseSalary | legacy-payroll | BASE_SALARY | field-ownership |
| AuthUser.Username | legacy-hr-batch | LOGIN_HINT | field-ownership |

Missing SoT for a `requiredTargetFields` entry fails validate (`target-field-no-sot` fixture).

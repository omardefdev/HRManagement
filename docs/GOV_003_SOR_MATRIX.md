# مصفوفة SoR / ملكية الحقول (GOV-003)

مرجع الآلة: [`master-data/field-ownership.json`](./master-data/field-ownership.json) — ADR: [ADR-GOV-003-master-data-governance.md](./ADR-GOV-003-master-data-governance.md).

| الحقل | الكاتب (SoR) | قرّاء | سياسة تعارض |
|---|---|---|---|
| Employee.NationalId | HR.Core | Payroll, Auth, Contracts, Analytics | Unique per tenant؛ ممنوع إعادة الكتابة من المستهلك |
| Employee.FullName | HR.Core | Lookup / تقارير | Payroll عبر lookup فقط |
| Employee.CurrentStatus | HR.Core | Auth, Payroll, Contracts | أحداث الحالة |
| Employee.PrimaryOrgUnitId | HR.Core | Snapshots | Snapshot قد يتأخر |
| OrgUnit.Code/NameAr/Path | HR.Core | Snapshots / نطاق | SoR واحد |
| EmploymentRecord.PositionTitle | HR.Core (نص مؤقت) | تقارير | كتالوج = MDM-001 لاحقاً |
| EmployeePayProfile.BaseSalary | Payroll | محرك الرواتب | لا راتب SoR في HR.Core |
| AuthUser.Username/Email | Auth.Directory | IAM | رابط EmployeeId فقط |

**Steward:** مسؤول بيانات HR يستخدم طابور التكرار وتقرير الجودة (GOV-003-C/D).

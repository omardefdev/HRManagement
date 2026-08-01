# جرد تصنيف البيانات (GOV-001)

مصدر الآلة: [`data-classification/inventory.json`](./data-classification/inventory.json).  
مرجع: [ADR-GOV-001-data-classification-privacy.md](./ADR-GOV-001-data-classification-privacy.md).

| المعرّف | التصنيف | المالك | الغرض | أساس | سياسة احتفاظ |
|---|---|---|---|---|---|
| hrcore.employee.nationalId | PII | HR.Core | هوية موظف | علاقة عمل | ret-employee-lifecycle |
| hrcore.employee.birthDate | PII | HR.Core | أهلية | علاقة عمل | ret-employee-lifecycle |
| hrcore.employee.names | PII | HR.Core | تشغيل HR | علاقة عمل | ret-employee-lifecycle |
| health.medicalRecord.clinical | Medical | HR.Health | لياقة طبية | ضرورة طبية | ret-medical (3650d) |
| compliance.complaint.body | Complaint | Compliance | تظلمات | تنظيمي | ret-complaint (2555d) |
| payroll.payProfile.baseSalary | Payroll | Payroll | أجور | عقد/راتب | ret-payroll (3650d) |
| auth.user.identifiers | PII | Auth.Directory | وصول | أمن | ret-directory |
| files.metadata.holder | OtherSensitive | File.Storage | مرفقات | غرض الوحدة | ret-files-purge |

## AC → Story

| AC | Story |
|---|---|
| حقل حساس بمالك/غرض/مدة | A (+ جدول C) |
| retention قابل للتدقيق | C |
| لا prod خام في test | D |
| DPIA معتمد | A |
| DSAR workflow | E |

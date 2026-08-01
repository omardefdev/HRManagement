# التكامل الخارجي — المستوى الثاني (جاهزية)

**الإصدار:** 12 حزيران 2026  
**النطاق:** موصّلات مجرّدة + تنفيذات ملفات/Stub + OpenAPI موحّد + تصدير قياسي — **بدون** ربط حي مع الجهات الحكومية (المستوى 3).

---

## مستويات التكامل

| المستوى | الوصف | الحالة |
|---------|--------|--------|
| **1** | تكامل داخلي (MassTransit/RabbitMQ، YARP، Keycloak) | ✅ موجود |
| **2** | جاهزية التكامل الخارجي (هذه الوثيقة) | ✅ مُنفَّذ |
| **3** | موصّلات حية (خزانة، تأمينات، داخلية، بصمة…) | ⏳ عند العميل المرجعي |

**المبدأ:** منطق الأعمال يعتمد على **منافذ** (interfaces) في `HRManagement.Integration.Abstractions`. التنفيذ الحي لكل جهة = استبدال محوّل واحد عند توفر المواصفات التعاقدية.

---

## الإعداد

```json
"ExternalIntegrations": {
  "Mode": "File",
  "OutputDirectory": "integration-outbox",
  "StubNationalIdAcceptedPrefix": "01"
}
```

| `Mode` | السلوك |
|--------|--------|
| `File` | كتابة/قراءة ملفات CSV/JSON في `integration-outbox` (الافتراضي) |
| `Stub` | محاكاة فورية (هوية، بصمة، ضريبة) دون ملفات |
| `Live` | محجوز للمستوى 3 — يُفعَّل عند تسليم محوّلات العميل |

**الخدمات المسجّلة:** `Payroll.Web` (المالية، التأمينات، الضريبة)، `HR.Core.Web` (الهوية، البصمة).

**DI:** `AddHrPayrollExternalIntegrations` / `AddHrCoreExternalIntegrations` في `HRManagement.Integration.FileAdapters`.

---

## مصفوفة الجهات والموصّلات

| الجهة / النظام | الواجهة | التنفيذ الحالي | صيغة الملف | مجلد الإخراج |
|----------------|---------|----------------|------------|--------------|
| الخزانة / وزارة المالية | `IPayrollDisbursementGateway` | `FilePayrollDisbursementGateway` | CSV | `treasury/` |
| مؤسسة التأمينات الاجتماعية | `ISocialInsuranceReporter` | `FileSocialInsuranceReporter` | CSV | `social-insurance/` |
| وزارة الداخلية (الهوية) | `INationalIdentityVerifier` | `FileNationalIdentityVerifier` / `StubNationalIdentityVerifier` | JSON (طلب) | `identity-requests/` |
| الضرائب | `ITaxWithholdingReporter` | `FileTaxWithholdingReporter` / `StubTaxWithholdingReporter` | CSV | `tax/` |
| البصمة / الحضور | `IBiometricAttendanceProvider` | `FileBiometricAttendanceProvider` / `StubBiometricAttendanceProvider` | CSV وارد | `biometric-inbox/` |

---

## حقول الملفات القياسية

### ملف الرواتب للمالية (`payroll-disbursement-YYYYMM-*.csv`)

`PayrollRunId`, `PeriodStart`, `PeriodEnd`, `Currency`, `EmployeeId`, `NationalId`, `EmployeeName`, `GrossPay`, `TotalDeductions`, `NetPay`, `BankAccount`

### ملف التأمينات (`social-insurance-YYYYMM-*.csv`)

`PayrollRunId`, `PeriodStart`, `PeriodEnd`, `EmployeeId`, `NationalId`, `EmployeeName`, `InsurableWageBase`, `EmployeeContribution`, `EmployerContribution`

### ملف الحضور الوارد (`attendance-*.csv`)

`EmployeeId`, `NationalId`, `WorkDate`, `CheckIn`, `CheckOut`

---

## واجهة API (OpenAPI)

جميع **13** خدمة Web تسجّل Swagger عبر:

- `builder.Services.AddHrOpenApi("اسم الخدمة API");`
- `app.UseHrOpenApi();` (بيئة التطوير فقط)

الامتداد في `HRManagement.ServiceDefaults/HrOpenApiExtensions.cs`.

---

## التصدير من واجهة الرواتب

من **تقارير الرواتب** (`PayrollReportsController`):

- `ExportTreasuryFile` — ملف المالية
- `ExportSocialInsuranceFile` — ملف التأمينات
- `ExportExternalIntegrationBundle` — الاثنان معاً

يُبنى المحتوى عبر `PayrollExternalExportService` الذي يستدعي الموصّلات المسجّلة في DI.

---

## الانتقال للإنتاج (المستوى 3)

1. تنفيذ فئة `Live*` لكل واجهة في مشروع منفصل (مثلاً `HRManagement.Integration.Live`).
2. تعيين `ExternalIntegrations:Mode` = `Live`.
3. استبدال تسجيل DI في `ExternalIntegrationServiceCollectionExtensions` للمحوّل الحي — **دون تعديل** controllers أو منطق الرواتب.

---

## المراجع في الشيفرة

- `src/Shared/HRManagement.Integration.Abstractions/`
- `src/Shared/HRManagement.Integration.FileAdapters/`
- `src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs`
- `docs/BUSINESS_PLAN_REPORT.md` — صياغة المنتج والتكامل

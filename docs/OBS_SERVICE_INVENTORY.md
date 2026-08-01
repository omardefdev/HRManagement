# OBS-001 — جرد الخدمات والـWorkers

| Host | مسار | OTel (بعد OBS-001) | Jobs / consumers داخل العملية |
|---|---|---|---|
| Gateway | `src/Gateway/HRManagement.Gateway` | AspNetCore+Http+Runtime+Prometheus+OTLP | GatewayAuditQueue, GatewayAuditRetentionService |
| HR.Core.Web | `src/Services/HRCore/HR.Core.Web` | + MT + EF + meters | MassTransit consumers, FileReferenceOutbox, Export, InboxRetention |
| HR.Contracts.Web | `.../HRContracts/HR.Contracts.Web` | نفس النمط | consumers, outbox, export |
| HR.Health.Web | `.../HRHealth/HR.Health.Web` | نفس النمط | consumers |
| HR.Movements.Web | `.../HRMovements/HR.Movements.Web` | نفس النمط | consumers, sagas |
| HR.Training.Web | `.../HRTraining/HR.Training.Web` | نفس النمط | consumers |
| Payroll.Web | `.../Payroll/Payroll.Web` | + `hrm.payroll.run` | consumers, payroll run metrics |
| IDCard.Web | `.../IDCard/IDCard.Web` | نفس النمط | consumers |
| Records.Archive.Web | `.../RecordsArchive/...` | نفس النمط | consumers |
| Admin.Logistics.Web | `.../AdminLogistics/...` | نفس النمط | consumers |
| IT.Infra.Web | `.../ITInfrastructure/...` | نفس النمط | consumers / backup jobs |
| Dev.Analytics.Web | `.../DevAnalytics/...` | نفس النمط | consumers |
| Compliance.Web | `.../Compliance/...` | نفس النمط | consumers, export |
| Auth.Directory.Web | `.../AuthDirectory/...` | نفس النمط | — |
| File.Storage.Web | `.../FileStorage/...` | + job Activities | FileGarbageCollectorJob, FileScanProcessorJob |

## Prometheus scrape jobs (هدف الإغلاق)

`hr-gateway`, `hr-core`, `hr-contracts`, `hr-health`, `hr-movements`, `hr-training`, `payroll`, `idcard`, `records-archive`, `admin-logistics`, `it-infra`, `dev-analytics`, `compliance`, `auth-directory`, `file-storage`, `rabbitmq`, `postgres`.

## ActivitySources / Meters المنصة

| الاسم | الغرض |
|---|---|
| `HRManagement.Jobs` | spans لدورات hosted jobs |
| `HRManagement.Inbox` | عدادات inbox |
| `HRManagement.Saga` | عدادات saga |
| `HRManagement.Outbox` | عمق/عمر outbox |
| `hrm.payroll.run` | تشغيل الرواتب |
| `HRManagement.ExportJobs` | وظائف التصدير |

قارن بعد التشغيل: `scripts/obs-inventory-compare.sh`.

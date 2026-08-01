# جرد مخطط قواعد البيانات

هذا الجرد مولد آلياً من ملفات `DbContext` و`ModelSnapshot` و`appsettings.json` في المشروع. لا يعني وجود جدول في snapshot أنه مغطى باختبار تكامل حي؛ بل يثبت أنه جزء من النموذج البرمجي المعلن.

| الخدمة | DbContext | قاعدة البيانات | DbSet | جداول الأعمال | جداول البنية | المصدر |
|---|---|---|---:|---:|---|---|
| البوابة والتدقيق (Gateway Audit) | `GatewayAuditDbContext` | `gateway_audit_db` | 1 | 1 | - | `src/Gateway/HRManagement.Gateway/Data/GatewayAuditDbContext.cs` |
| اللوجستيات الإدارية (Admin.Logistics) | `AdminLogisticsDbContext` | `admin_db` | 9 | 7 | AuditLogs, IntegrationMessageReceipts | `src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs` |
| الدليل والمصادقة (Auth.Directory) | `AuthDirectoryDbContext` | `auth_directory_db` | 9 | 8 | AuditLogs | `src/Services/AuthDirectory/Auth.Directory.Infrastructure/Data/AuthDirectoryDbContext.cs` |
| الامتثال والشكاوى (Compliance) | `ComplianceDbContext` | `compliance_db` | 8 | 7 | AuditLogs | `src/Services/Compliance/Compliance.Infrastructure/Data/ComplianceDbContext.cs` |
| التحليلات (Dev.Analytics) | `AnalyticsDbContext` | `analytics_db` | 8 | 7 | AuditLogs | `src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Data/AnalyticsDbContext.cs` |
| تخزين الملفات (File.Storage) | `FileStorageDbContext` | `file_storage_db` | 4 | 3 | AuditLogs | `src/Services/FileStorage/File.Storage.Infrastructure/Data/FileStorageDbContext.cs` |
| العقود والخدمة (HR.Contracts) | `ContractsDbContext` | `hr_contracts_db` | 18 | 17 | AuditLogs, InboxState, OutboxMessage, OutboxState | `src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs` |
| النواة والموظفون (HR.Core) | `HRCoreDbContext` | `hr_core_db` | 32 | 30 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs` |
| الصحة المهنية (HR.Health) | `HealthDbContext` | `hr_health_db` | 13 | 11 | AuditLogs, IntegrationMessageReceipts | `src/Services/HRHealth/HR.Health.Infrastructure/Data/HealthDbContext.cs` |
| الحركات والتنقلات (HR.Movements) | `MovementsDbContext` | `hr_movements_db` | 10 | 9 | AuditLogs | `src/Services/HRMovements/HR.Movements.Infrastructure/Data/MovementsDbContext.cs` |
| التدريب والتعلم (HR.Training) | `TrainingDbContext` | `hr_training_db` | 14 | 13 | AuditLogs | `src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs` |
| الهويات والبطاقات (IDCard) | `IDCardDbContext` | `idcard_db` | 8 | 7 | AuditLogs | `src/Services/IDCard/IDCard.Infrastructure/Data/IDCardDbContext.cs` |
| البنية التقنية (IT.Infra) | `ITInfraDbContext` | `it_infra_db` | 9 | 7 | AuditLogs, IntegrationMessageReceipts | `src/Services/ITInfrastructure/IT.Infra.Infrastructure/Data/ITInfraDbContext.cs` |
| الرواتب والتعويضات (Payroll) | `PayrollDbContext` | `payroll_db` | 24 | 22 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs` |
| الأرشيف والسجلات (Records.Archive) | `ArchiveDbContext` | `archive_db` | 8 | 7 | AuditLogs | `src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs` |

## الكيانات المعلنة لكل سياق

### البوابة والتدقيق (Gateway Audit)

`GatewayAuditEntry`

### اللوجستيات الإدارية (Admin.Logistics)

`Asset`, `AssetAssignment`, `SupplyOrder`, `InventoryItem`, `MaintenanceRecord`, `Vendor`, `ReceiptRecord`, `IntegrationMessageReceipt`, `EntityAuditLog`

### الدليل والمصادقة (Auth.Directory)

`AuthUser`, `Role`, `AuthToken`, `DirectoryEntry`, `DirectorySyncRecord`, `PermissionPolicy`, `UserRole`, `AuthAuditLog`, `EntityAuditLog`

### الامتثال والشكاوى (Compliance)

`Complaint`, `AuditFinding`, `LegalCase`, `ComplianceRecommendation`, `DisciplinaryAction`, `LegalReferral`, `ComplaintFollowUp`, `EntityAuditLog`

### التحليلات (Dev.Analytics)

`Report`, `Dashboard`, `ETLJob`, `ReadModel`, `KPI`, `AnalyticsEvent`, `DataRefreshSchedule`, `EntityAuditLog`

### تخزين الملفات (File.Storage)

`FileRecord`, `FileReferenceRecord`, `FileAccessAuditLog`, `EntityAuditLog`

### العقود والخدمة (HR.Contracts)

`Contract`, `ContractType`, `ContractSignature`, `ProbationRecord`, `SeparationRecord`, `LeaveRequest`, `AdvanceRequest`, `AdministrativeDecision`, `CommencementRecord`, `ServiceTransaction`, `ServiceTransactionStateHistory`, `LeaveBalance`, `LeavePolicy`, `AdvanceInstallmentPlan`, `SeparationChecklist`, `HireChecklist`, `OrgUnitSnapshot`, `EntityAuditLog`

### النواة والموظفون (HR.Core)

`Employee`, `EmployeeProfile`, `EmploymentRecord`, `EmployeeDocument`, `Qualification`, `ContactInfo`, `EmploymentStatusHistory`, `OrgUnit`, `PerformanceEvaluation`, `ApprovedCV`, `IntegrationMessageReceipt`, `WorkforcePlan`, `HrStatisticReport`, `DailyAttendanceRecord`, `FieldInspectionVisit`, `DiwanCorrespondence`, `RecruitmentApplication`, `ApprovalRequest`, `ApprovalStep`, `Shift`, `ShiftRoster`, `ShiftAssignment`, `CareerPath`, `CareerPathStep`, `KeyPosition`, `SuccessionPlan`, `SuccessorCandidate`, `InternalVacancy`, `InternalApplication`, `PublicJobPosting`, `CitizenApplication`, `EntityAuditLog`

### الصحة المهنية (HR.Health)

`MedicalRecord`, `MedicalCommitteeReport`, `PeriodicMedicalCheck`, `InsuranceCard`, `HealthStatusEntry`, `MedicalRecommendation`, `HealthRetirementRecord`, `InsuranceContract`, `WorkplaceAccident`, `SafetyInspection`, `FirstAidTrainingRecord`, `IntegrationMessageReceipt`, `EntityAuditLog`

### الحركات والتنقلات (HR.Movements)

`TransferRequest`, `MovementNotice`, `DistributionPlan`, `MovementExecutionRecord`, `SecondmentRequest`, `LoanAssignmentRequest`, `TransferApproval`, `TransferFinancialImpact`, `OrgUnitSnapshot`, `EntityAuditLog`

### التدريب والتعلم (HR.Training)

`TrainingCourse`, `TrainingEnrollment`, `QualificationCode`, `TrainingResult`, `MissionFile`, `TrainingNomination`, `TrainingProvider`, `TrainingPlan`, `ELearningCourse`, `CourseModule`, `Lesson`, `LearningEnrollment`, `LearningCertificate`, `EntityAuditLog`

### الهويات والبطاقات (IDCard)

`CardDesign`, `IssuedCard`, `CardRequest`, `CardDeliveryRecord`, `OfficialSealRecord`, `AccessPermission`, `CardPermissionSet`, `EntityAuditLog`

### البنية التقنية (IT.Infra)

`ServerDevice`, `UserDevice`, `BackupJob`, `SecurityIncident`, `SystemPermissionRecord`, `ITSupportTicket`, `EnvironmentConfiguration`, `IntegrationMessageReceipt`, `EntityAuditLog`

### الرواتب والتعويضات (Payroll)

`PayrollRun`, `SalaryLine`, `CompensationType`, `AllowanceRecord`, `DeductionRecord`, `PayrollAdjustment`, `PayrollAuditRecord`, `PaymentBatch`, `SalaryScale`, `PayGrade`, `DeductionType`, `EmployeePayProfile`, `SalaryLineComponent`, `EndOfServiceSettlement`, `PayrollConfiguration`, `TaxBracket`, `OfficialHoliday`, `EmployeePayProfileHistory`, `PayrollRunApproval`, `PayslipDelivery`, `AbsenceDeduction`, `OrgUnitSnapshot`, `EntityAuditLog`, `IntegrationMessageReceipt`

### الأرشيف والسجلات (Records.Archive)

`ArchiveItem`, `PhysicalFileRecord`, `DigitalFileRecord`, `LoanRecord`, `ArchiveIndexEntry`, `DisposalRecord`, `DocumentClassification`, `EntityAuditLog`


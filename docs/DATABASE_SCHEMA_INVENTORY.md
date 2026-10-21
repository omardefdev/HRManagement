# جرد مخطط قواعد البيانات

هذا الجرد مولد آلياً من ملفات `DbContext` و`ModelSnapshot` و`appsettings.json` في المشروع. لا يعني وجود جدول في snapshot أنه مغطى باختبار تكامل حي؛ بل يثبت أنه جزء من النموذج البرمجي المعلن.

| الخدمة | DbContext | قاعدة البيانات | DbSet | جداول الأعمال | جداول البنية | المصدر |
|---|---|---|---:|---:|---|---|
| البوابة والتدقيق (Gateway Audit) | `GatewayAuditDbContext` | `gateway_audit_db` | 7 | 7 | - | `src/Gateway/HRManagement.Gateway/Data/GatewayAuditDbContext.cs` |
| اللوجستيات الإدارية (Admin.Logistics) | `AdminLogisticsDbContext` | `admin_db` | 13 | 11 | AuditLogs, IntegrationMessageReceipts | `src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs` |
| الدليل والمصادقة (Auth.Directory) | `AuthDirectoryDbContext` | `auth_directory_db` | 10 | 8 | AuditLogs, IntegrationMessageReceipts | `src/Services/AuthDirectory/Auth.Directory.Infrastructure/Data/AuthDirectoryDbContext.cs` |
| الامتثال والشكاوى (Compliance) | `ComplianceDbContext` | `compliance_db` | 14 | 12 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/Compliance/Compliance.Infrastructure/Data/ComplianceDbContext.cs` |
| التحليلات (Dev.Analytics) | `AnalyticsDbContext` | `analytics_db` | 10 | 8 | AuditLogs, IntegrationMessageReceipts | `src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Data/AnalyticsDbContext.cs` |
| تخزين الملفات (File.Storage) | `FileStorageDbContext` | `file_storage_db` | 12 | 10 | AuditLogs, IntegrationMessageReceipts | `src/Services/FileStorage/File.Storage.Infrastructure/Data/FileStorageDbContext.cs` |
| العقود والخدمة (HR.Contracts) | `ContractsDbContext` | `hr_contracts_db` | 25 | 23 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs` |
| النواة والموظفون (HR.Core) | `HRCoreDbContext` | `hr_core_db` | 90 | 88 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs` |
| الصحة المهنية (HR.Health) | `HealthDbContext` | `hr_health_db` | 15 | 13 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRHealth/HR.Health.Infrastructure/Data/HealthDbContext.cs` |
| الحركات والتنقلات (HR.Movements) | `MovementsDbContext` | `hr_movements_db` | 18 | 16 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRMovements/HR.Movements.Infrastructure/Data/MovementsDbContext.cs` |
| التدريب والتعلم (HR.Training) | `TrainingDbContext` | `hr_training_db` | 17 | 16 | AuditLogs, InboxState, OutboxMessage, OutboxState | `src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs` |
| الهويات والبطاقات (IDCard) | `IDCardDbContext` | `idcard_db` | 10 | 8 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/IDCard/IDCard.Infrastructure/Data/IDCardDbContext.cs` |
| البنية التقنية (IT.Infra) | `ITInfraDbContext` | `it_infra_db` | 12 | 10 | AuditLogs, IntegrationMessageReceipts | `src/Services/ITInfrastructure/IT.Infra.Infrastructure/Data/ITInfraDbContext.cs` |
| الرواتب والتعويضات (Payroll) | `PayrollDbContext` | `payroll_db` | 40 | 38 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs` |
| الأرشيف والسجلات (Records.Archive) | `ArchiveDbContext` | `archive_db` | 12 | 10 | AuditLogs, IntegrationMessageReceipts | `src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs` |

## الكيانات المعلنة لكل سياق

### البوابة والتدقيق (Gateway Audit)

`GatewayAuditEntry`, `SecurityAuditEvent`, `AuditLegalHold`, `EvidenceRecord`, `EvidenceArtifact`, `EvidencePackExport`, `GatewayAuditChainState`

### اللوجستيات الإدارية (Admin.Logistics)

`Asset`, `AssetAssignment`, `AssetLossReport`, `SupplyOrder`, `SupplyOrderLine`, `InventoryItem`, `MaintenanceRecord`, `Vendor`, `ReceiptRecord`, `IntegrationMessageReceipt`, `EmployeeOrgUnitSnapshot`, `OrgUnitSnapshot`, `EntityAuditLog`

### الدليل والمصادقة (Auth.Directory)

`AuthUser`, `Role`, `AuthToken`, `DirectoryEntry`, `DirectorySyncRecord`, `PermissionPolicy`, `UserRole`, `AuthAuditLog`, `IntegrationMessageReceipt`, `EntityAuditLog`

### الامتثال والشكاوى (Compliance)

`Complaint`, `AuditFinding`, `LegalCase`, `ComplianceRecommendation`, `DisciplinaryAction`, `LegalReferral`, `ComplaintFollowUp`, `IntegrationMessageReceipt`, `EntityAuditLog`, `ExportJob`, `DsarRequest`, `InvestigationChecklist`, `UnionEngagement`, `CollectiveGrievance`

### التحليلات (Dev.Analytics)

`Report`, `Dashboard`, `ETLJob`, `ReadModel`, `KPI`, `AnalyticsEvent`, `DataRefreshSchedule`, `ExportJob`, `IntegrationMessageReceipt`, `EntityAuditLog`

### تخزين الملفات (File.Storage)

`FileRecord`, `FileReferenceRecord`, `FileAccessGrant`, `FileAccessAuditLog`, `FilePurgeWorkItem`, `FileScanWorkItem`, `FileScanResult`, `FileReconcileException`, `FileReferenceOutboxMessage`, `IntegrationMessageReceipt`, `EntityAuditLog`, `IdempotencyRecord`

### العقود والخدمة (HR.Contracts)

`Contract`, `ContractType`, `ContractSignature`, `ProbationRecord`, `SeparationRecord`, `LeaveRequest`, `AdvanceRequest`, `AdministrativeDecision`, `CommencementRecord`, `ServiceTransaction`, `ServiceTransactionStateHistory`, `LeaveBalance`, `LeavePolicy`, `AdvanceInstallmentPlan`, `SeparationChecklist`, `HireChecklist`, `OrgUnitSnapshot`, `EmployeeOrgUnitSnapshot`, `IdempotencyRecord`, `IntegrationMessageReceipt`, `ProcessIntervention`, `SeparationSagaState`, `HireCaseSagaState`, `EntityAuditLog`, `ExportJob`

### النواة والموظفون (HR.Core)

`Employee`, `EmployeeCorrection`, `EmployeeProfile`, `EmploymentRecord`, `EmployeeDocument`, `Qualification`, `ContactInfo`, `EmploymentStatusHistory`, `OrgUnit`, `Tenant`, `TenantAssignmentRule`, `TenantQuarantineEntry`, `SodRule`, `ConflictingRolePair`, `ApprovalDelegation`, `PerformanceEvaluation`, `ApprovedCV`, `IntegrationMessageReceipt`, `WorkforcePlan`, `WorkforcePlanLine`, `FinanceBudgetImportBatch`, `FinanceBudgetLine`, `JobCatalogImportBatch`, `JobCatalogImportLine`, `HrStatisticReport`, `DailyAttendanceRecord`, `FieldInspectionVisit`, `DiwanCorrespondence`, `RecruitmentApplication`, `ApprovalRequest`, `ApprovalStep`, `Shift`, `ShiftRoster`, `ShiftAssignment`, `CareerPath`, `CareerPathStep`, `KeyPosition`, `SuccessionPlan`, `SuccessorCandidate`, `InternalVacancy`, `InternalApplication`, `PublicJobPosting`, `CitizenApplication`, `EntityAuditLog`, `FileReferenceOutboxMessage`, `ExportJob`, `MasterDataDuplicateCase`, `WorkflowDefinition`, `WorkflowInstance`, `WorkflowDelegation`, `SignatureRecord`, `ReferenceCatalogEntry`, `LegislationSource`, `LegislationKeyArticle`, `LegislationInstrumentLink`, `LegislationSourceAlias`, `LegalRegime`, `BenefitPlan`, `ServiceRegimeException`, `EmploymentRegimeTransition`, `OrgUnitVersion`, `JobCatalogEntry`, `FundedPosition`, `EstablishmentChangeRequest`, `EstablishmentChangeLog`, `JobCatalogVersion`, `FundedPositionVersion`, `RecruitmentRequisition`, `OfferLetter`, `SelectionCommitteeSession`, `SelectionCommitteeMember`, `RecruitmentAppeal`, `RecruitmentInterviewSlot`, `RecruitmentInterviewPanelMember`, `RecruitmentAssessmentCriteria`, `RecruitmentAssessmentScore`, `PolicyTemplate`, `PolicyTemplateVersion`, `PolicyRule`, `PolicyFactsSchemaEntity`, `PolicyPublication`, `PolicySimulationRun`, `PolicyEnforcementLedger`, `PolicyWorkflowBinding`, `CodePolicyTemplate`, `CodePolicyVersion`, `CodePolicyRule`, `CodeSequenceCounter`, `CodeIssuance`, `SeedPackApplication`

### الصحة المهنية (HR.Health)

`MedicalRecord`, `MedicalCommitteeReport`, `PeriodicMedicalCheck`, `InsuranceCard`, `HealthStatusEntry`, `MedicalRecommendation`, `HealthRetirementRecord`, `InsuranceContract`, `WorkplaceAccident`, `SafetyInspection`, `FirstAidTrainingRecord`, `IntegrationMessageReceipt`, `OrgUnitSnapshot`, `EmployeeOrgUnitSnapshot`, `EntityAuditLog`

### الحركات والتنقلات (HR.Movements)

`TransferRequest`, `MovementNotice`, `DistributionPlan`, `MovementExecutionRecord`, `SecondmentRequest`, `LoanAssignmentRequest`, `PromotionRequest`, `DelegationRequest`, `TransferApproval`, `TransferFinancialImpact`, `MovementChecklist`, `OrgUnitSnapshot`, `IdempotencyRecord`, `IntegrationMessageReceipt`, `ProcessIntervention`, `TransferSagaState`, `SecondmentSagaState`, `EntityAuditLog`

### التدريب والتعلم (HR.Training)

`TrainingCourse`, `TrainingEnrollment`, `QualificationCode`, `TrainingResult`, `MissionFile`, `TrainingNomination`, `TrainingProvider`, `TrainingPlan`, `ELearningCourse`, `CourseModule`, `Lesson`, `LearningEnrollment`, `LearningCertificate`, `TrainingChecklist`, `OrgUnitSnapshot`, `EmployeeOrgUnitSnapshot`, `EntityAuditLog`

### الهويات والبطاقات (IDCard)

`CardDesign`, `IssuedCard`, `CardRequest`, `CardDeliveryRecord`, `OfficialSealRecord`, `AccessPermission`, `CardPermissionSet`, `CardChecklist`, `IntegrationMessageReceipt`, `EntityAuditLog`

### البنية التقنية (IT.Infra)

`ServerDevice`, `UserDevice`, `BackupJob`, `SecurityIncident`, `SystemPermissionRecord`, `ITSupportTicket`, `EnvironmentConfiguration`, `LegacySystemRecord`, `ShadowOperationCheck`, `MediaDestructionCertificate`, `IntegrationMessageReceipt`, `EntityAuditLog`

### الرواتب والتعويضات (Payroll)

`PayrollRun`, `SalaryLine`, `CompensationType`, `AllowanceRecord`, `DeductionRecord`, `PayrollAdjustment`, `PayrollAuditRecord`, `PaymentBatch`, `PaymentOrder`, `BankStatementLine`, `FundingDecision`, `DisbursementPriorityRule`, `ArrearsLedgerEntry`, `PayrollRunJob`, `PayrollRunPartition`, `PayrollRunInputSnapshot`, `SalaryScale`, `PayGrade`, `DeductionType`, `EmployeePayProfile`, `SalaryLineComponent`, `EndOfServiceSettlement`, `PayrollConfiguration`, `TaxBracket`, `OfficialHoliday`, `LegalDecisionRecord`, `LegalAttestation`, `PayrollConfigChangeNotice`, `EmployeePayProfileHistory`, `PayrollRunApproval`, `PayslipDelivery`, `AbsenceDeduction`, `FraudAlert`, `PaymentHold`, `ProofOfLifeRecord`, `IdempotencyRecord`, `ExportJob`, `OrgUnitSnapshot`, `EntityAuditLog`, `IntegrationMessageReceipt`

### الأرشيف والسجلات (Records.Archive)

`ArchiveItem`, `PhysicalFileRecord`, `DigitalFileRecord`, `LoanRecord`, `ArchiveIndexEntry`, `DisposalRecord`, `DocumentClassification`, `ArchiveChecklist`, `CustodyEvent`, `ExportJob`, `IntegrationMessageReceipt`, `EntityAuditLog`


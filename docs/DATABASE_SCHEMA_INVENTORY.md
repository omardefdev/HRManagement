# جرد مخطط قواعد البيانات

هذا الجرد مولد آلياً من ملفات `DbContext` و`ModelSnapshot` و`appsettings.json` في المشروع. لا يعني وجود جدول في snapshot أنه مغطى باختبار تكامل حي؛ بل يثبت أنه جزء من النموذج البرمجي المعلن.

| الخدمة | DbContext | قاعدة البيانات | DbSet | جداول الأعمال | جداول البنية | المصدر |
|---|---|---|---:|---:|---|---|
| البوابة والتدقيق (Gateway Audit) | `GatewayAuditDbContext` | `gateway_audit_db` | 7 | 7 | - | `src/Gateway/HRManagement.Gateway/Data/GatewayAuditDbContext.cs` |
| اللوجستيات الإدارية (Admin.Logistics) | `AdminLogisticsDbContext` | `admin_db` | 13 | 11 | AuditLogs, IntegrationMessageReceipts | `src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs` |
| الدليل والمصادقة (Auth.Directory) | `AuthDirectoryDbContext` | `auth_directory_db` | 10 | 8 | AuditLogs, IntegrationMessageReceipts | `src/Services/AuthDirectory/Auth.Directory.Infrastructure/Data/AuthDirectoryDbContext.cs` |
| الامتثال والشكاوى (Compliance) | `ComplianceDbContext` | `compliance_db` | 14 | 12 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/Compliance/Compliance.Infrastructure/Data/ComplianceDbContext.cs` |
| التحليلات (Dev.Analytics) | `AnalyticsDbContext` | `analytics_db` | 11 | 9 | AuditLogs, IntegrationMessageReceipts | `src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Data/AnalyticsDbContext.cs` |
| تخزين الملفات (File.Storage) | `FileStorageDbContext` | `file_storage_db` | 12 | 10 | AuditLogs, IntegrationMessageReceipts | `src/Services/FileStorage/File.Storage.Infrastructure/Data/FileStorageDbContext.cs` |
| العقود والخدمة (HR.Contracts) | `ContractsDbContext` | `hr_contracts_db` | 26 | 24 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs` |
| النواة والموظفون (HR.Core) | `HRCoreDbContext` | `hr_core_db` | 109 | 107 | AuditLogs, InboxState, IntegrationMessageReceipts, OutboxMessage, OutboxState | `src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs` |
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

`Report`, `Dashboard`, `ETLJob`, `ReadModel`, `KPI`, `AnalyticsEvent`, `RecruitmentRequisitionFact`, `DataRefreshSchedule`, `ExportJob`, `IntegrationMessageReceipt`, `EntityAuditLog`

### تخزين الملفات (File.Storage)

`FileRecord`, `FileReferenceRecord`, `FileAccessGrant`, `FileAccessAuditLog`, `FilePurgeWorkItem`, `FileScanWorkItem`, `FileScanResult`, `FileReconcileException`, `FileReferenceOutboxMessage`, `IntegrationMessageReceipt`, `EntityAuditLog`, `IdempotencyRecord`

### العقود والخدمة (HR.Contracts)

`Contract`, `ContractType`, `ContractTypeRegimeMap`, `ContractSignature`, `ProbationRecord`, `SeparationRecord`, `LeaveRequest`, `AdvanceRequest`, `AdministrativeDecision`, `CommencementRecord`, `ServiceTransaction`, `ServiceTransactionStateHistory`, `LeaveBalance`, `LeavePolicy`, `AdvanceInstallmentPlan`, `SeparationChecklist`, `HireChecklist`, `OrgUnitSnapshot`, `EmployeeOrgUnitSnapshot`, `IdempotencyRecord`, `IntegrationMessageReceipt`, `ProcessIntervention`, `SeparationSagaState`, `HireCaseSagaState`, `EntityAuditLog`, `ExportJob`

### النواة والموظفون (HR.Core)

`Employee`, `EmployeeCorrection`, `EmployeeProfile`, `EmploymentRecord`, `EmployeeDocument`, `Qualification`, `ContactInfo`, `EmploymentStatusHistory`, `OrgUnit`, `Tenant`, `TenantAssignmentRule`, `TenantQuarantineEntry`, `SodRule`, `ConflictingRolePair`, `ApprovalDelegation`, `PerformanceEvaluation`, `ApprovedCV`, `IntegrationMessageReceipt`, `WorkforcePlan`, `WorkforcePlanLine`, `FinanceBudgetImportBatch`, `FinanceBudgetLine`, `JobCatalogImportBatch`, `JobCatalogImportLine`, `HrStatisticReport`, `DailyAttendanceRecord`, `FieldInspectionVisit`, `DiwanCorrespondence`, `RecruitmentApplication`, `RecruitmentApplicationEvent`, `CandidateDocument`, `CandidateNotification`, `CandidateRetentionRun`, `CandidateOtpChallenge`, `ApprovalRequest`, `ApprovalStep`, `Shift`, `ShiftRoster`, `ShiftAssignment`, `CareerPath`, `CareerPathStep`, `KeyPosition`, `SuccessionPlan`, `SuccessorCandidate`, `InternalVacancy`, `InternalApplication`, `PublicJobPosting`, `CitizenApplication`, `EntityAuditLog`, `FileReferenceOutboxMessage`, `ExportJob`, `MasterDataDuplicateCase`, `WorkflowDefinition`, `WorkflowInstance`, `WorkflowDelegation`, `SignatureRecord`, `ReferenceCatalogEntry`, `LegislationSource`, `LegislationKeyArticle`, `LegislationInstrumentLink`, `LegislationSourceAlias`, `LegalRegime`, `BenefitPlan`, `ServiceRegimeException`, `EmploymentRegimeTransition`, `OrgUnitVersion`, `JobCatalogEntry`, `FundedPosition`, `EstablishmentChangeRequest`, `EstablishmentChangeLog`, `JobCatalogVersion`, `FundedPositionVersion`, `RecruitmentRequisition`, `RecruitmentCompetition`, `CompetitionLine`, `CompetitionPreference`, `ExamSession`, `ExamCandidate`, `ExamMarkerAssignment`, `ExamScore`, `CompetitionResultVersion`, `CompetitionResult`, `CompetitionAllocation`, `CompetitionReserveEntry`, `OfferLetter`, `SelectionCommitteeSession`, `SelectionCommitteeMember`, `SelectionCommitteeMemberCoi`, `SelectionCommitteeVote`, `SelectionCommitteeDecision`, `RecruitmentAppeal`, `RecruitmentInterviewSlot`, `RecruitmentInterviewPanelMember`, `RecruitmentAssessmentCriteria`, `RecruitmentAssessmentScore`, `PolicyTemplate`, `PolicyTemplateVersion`, `PolicyRule`, `PolicyFactsSchemaEntity`, `PolicyPublication`, `PolicySimulationRun`, `PolicyEnforcementLedger`, `PolicyWorkflowBinding`, `CodePolicyTemplate`, `CodePolicyVersion`, `CodePolicyRule`, `CodeSequenceCounter`, `CodeIssuance`, `SeedPackApplication`

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


## إضافات HCM-030 / PLAN-30 (تحرير يدوي)

حُدِّث هذا الجرد يدوياً لـ PLAN-30 لأن Python غير متاح على جهاز التنفيذ (المولّد `scripts/extract_database_inventory.py` لم يُشغَّل)؛
يُعاد توليده عند توفره. كل الترحيلات في HR.Core وإضافية؛ لا ترحيل في File.Storage.

| الترحيل | الجدول | الأعمدة / الفهارس |
|---------|--------|-------------------|
| `HCM030_UnifiedIntake` | `RecruitmentApplications` | + `IntakeChannel` varchar(20) not null default `'HrEntered'` (تعبئة Postgres للقيمة) |
| | `CitizenApplications`, `InternalApplications` | `IX_*_PromotedRecruitmentApplicationId` فريد ومُرشَّح (`IS NOT NULL`) |
| | `RecruitmentApplicationEvents` (جديد) | `Id`, `TenantId`, `RecruitmentApplicationId` (FK cascade), `EventType` varchar(40), `FromStatus`/`ToStatus` int?, `RelatedEntityId` uuid?, `OccurredAt`, `NotificationsPlannedAt`?, `CreatedAt`/`UpdatedAt`؛ فهرس `(TenantId, RecruitmentApplicationId, OccurredAt)` و`IX_RecruitmentApplicationEvents_NotificationsPending` |
| `HCM030_ApplicationProfile` | `CitizenApplications` | + `EducationLevelCode` varchar(20), `Specialization` varchar(200), `GraduationYear` int, `YearsOfExperience` numeric(4,1), `BirthDate` date |
| | `RecruitmentApplications` | + الأعمدة الخمسة نفسها + `EligibilityFlags` varchar(200), `EligibilityEvaluatedAt`, `EligibilityOverrideReason` varchar(1000) |
| | `RecruitmentRequisitions` | + `MinQualificationCode` varchar(20), `MinYearsOfExperience` numeric(4,1), `MinAge` int, `MaxAge` int |
| `HCM030_CandidateDocuments` | `CandidateDocuments` (جديد) | `Id`, `TenantId`, `RecruitmentApplicationId` (FK cascade), `CitizenApplicationId`? (FK set null), `DocumentType` varchar(20), `FileId`, `OriginalFileName` varchar(256), `ContentType` varchar(100), `SizeBytes` bigint, `UploadedAt`, `UploadedBy` varchar(64), `CreatedAt`/`UpdatedAt`؛ فريد `(TenantId, FileId)` |
| | `RecruitmentRequisitions` | + `RequiredDocumentTypes` varchar(100) |
| `HCM030_CandidateNotifications` | `CandidateNotifications` (جديد) | `Id`, `TenantId`, `RecruitmentApplicationId` (FK cascade), `EventId`? (FK set null), `EventType` varchar(40), `Channel` varchar(20), `TemplateCode` varchar(40), `RecipientMasked` varchar(160), `Subject` varchar(200)?, `Body` varchar(2000), `IdempotencyKey` varchar(200), `Status` varchar(20), `Attempts`, `NextAttemptAt`?, `LastError` varchar(500)?, `SentAt`?, `ExpiresAt`?, `ProviderMessageId` varchar(200)?, `CreatedAt`/`UpdatedAt`؛ فريد `(TenantId, IdempotencyKey)`، `IX_CandidateNotifications_PendingDue` |
| | `CitizenApplications` | + `NotificationChannel` varchar(20), `NotificationConsentAt` |
| `HCM030_CandidateRetention` | `CandidateRetentionRuns` (جديد) | `Id`, `TenantId`, `StartedAt`, `FinishedAt`?, `DryRun`, `DueCount`, `AnonymizedCount`, `SkippedLegalHold`, `SkippedAppeal`, `TriggeredBy` varchar(64), `Error` varchar(1000)?, `CreatedAt`/`UpdatedAt`؛ فهرس `(TenantId, StartedAt)` |
| `HCM030_CandidatePortal` | `CandidateOtpChallenges` (جديد) | `Id`, `TenantId`, `CitizenApplicationId` (FK cascade), `CodeHmac` varchar(64), `Channel` varchar(20), `ExpiresAt`, `Attempts`, `ConsumedAt`?, `RequestIp` varchar(64)? (مُجزّأ بمفتاح)، `CreatedAt`/`UpdatedAt`؛ فهرس `(TenantId, CitizenApplicationId, CreatedAt)` |
| | `CitizenApplications` | + `PortalLockedUntil`, `PortalOtpRequestedAt` |
| `HCM030_OfferAcceptance` | `OfferLetters` | + `AcceptedBy` varchar(20), `AcceptanceNote` varchar(1000), `AcceptanceEvidenceFileId` uuid |
| `HCM030_OfferDeclineEvidence` | `OfferLetters` | + `DeclineEvidenceFileId` uuid |


## إضافات HCM-031 / PLAN-31 (تحرير يدوي)

حُدِّث هذا الجرد يدوياً لـ PLAN-31 لأن Python غير متاح على جهاز التنفيذ (المولّد `scripts/extract_database_inventory.py` لم يُشغَّل)؛
يُعاد توليده عند توفره. الترحيلات الثلاثة في HR.Core وإضافية فقط (لا إعادة كتابة لبيانات قائمة).

| الترحيل | الجدول | الأعمدة / الفهارس |
|---------|--------|-------------------|
| `HCM031_Competitions` | `RecruitmentCompetitions` (جديد) | الرقم، العنوان، `LegalSourceId`، القناة، الحالة (نص)، تواريخ الإعلان والإغلاق، المدخلات القانونية لكل مسابقة (`ObjectionWindowDays` 1–60، `ReserveValidityMonths` 0–36، `MaxLinesPerCandidate` ≥ 1، `PassMark`، `ReconcileThreshold`، `ReconcileRule`، `TieBreakOrder`)، الاعتماد، `CurrentResultVersionId`، `ResultsFirstPublishedAt`، `FinalizedAt`؛ فريد `(TenantId, Number)` وقيود تحقق للمدخلات |
| | `CompetitionLines` (جديد) | `CompetitionId`، `LineNo`، المسمى، `GradeCode`، الوظيفة والوحدة اختيارياً، `CriteriaVersionId`، `RequiredCount`، `LinePassMark`؛ فريد `(CompetitionId, LineNo)` |
| | `CompetitionPreferences` (جديد) | `RecruitmentApplicationId`، `CompetitionLineId`، `Order` (≥ 1)، `EligibilityFlags`؛ فريد `(RecruitmentApplicationId, Order)` |
| | `RecruitmentRequisitions` | + `CompetitionLineId` uuid? (FK) |
| | `RecruitmentApplications` | + `CompetitionId`، `CompetitionLineId` uuid?؛ فريد مُرشَّح `(TenantId, CompetitionId, NationalIdHash)` (طلب واحد لكل شخص في المسابقة) |
| `HCM031_Exams` | `ExamSessions` (جديد) | النوع، العنوان، الموعد، المدة، المكان، السعة، العلامة العظمى، الحالة، `MarkingClosedAt` |
| | `ExamCandidates` (جديد) | الجلسة، المسابقة، نوع الامتحان، الطلب، `ExamNumber` (8 أرقام)، المقعد، الحضور، `FinalMark`، `AdmissionCardSha256`/`AdmissionCardIssuedAt`؛ فريد `(CompetitionId, ExamType, ExamNumber)` و`(CompetitionId, ExamType, RecruitmentApplicationId)` و`(ExamSessionId, SeatNumber)` |
| | `ExamMarkerAssignments` (جديد) | الجلسة، `MarkerEmployeeId` (FK موظف)، `MarkerUserId` (يُربط عند أول دخول)، الدور، `HoldsHrReadAccess`؛ فريد `(ExamSessionId, Role)` و`(ExamSessionId, MarkerEmployeeId)` |
| | `ExamScores` (جديد) | الورقة، `MarkerUserId`، الجولة (نص)، العلامة، `SubmittedAt`، `Note`، `RecruitmentAppealId`؛ فريد مُرشَّح `(ExamCandidateId, Round)` عدا `ObjectionCorrection` |
| `HCM031_Results` | `CompetitionResultVersions` (جديد) | `VersionNo`، الحالة، الاحتساب، النشر، السبب، سير الاعتماد، `Sha256`، `DiffJson`؛ فريد `(CompetitionId, VersionNo)` و`IX_CompetitionResultVersions_OnePublished` (نسخة منشورة واحدة) |
| | `CompetitionResults` (جديد) | النسخة، الطلب، `ExamNumber`، علامة الامتحان والمقابلة والنهائية، `OverallRank`، النتيجة، التعادل وقراره؛ فريد `(ResultVersionId, ExamNumber)` و`(ResultVersionId, RecruitmentApplicationId)` |
| | `CompetitionAllocations` (جديد) | المسابقة، الطلب، السطر والطلب الشاغر الموزَّع، ترتيب التفضيل المحقق، `FromReserve` |
| | `CompetitionReserveEntries` (جديد) | السطر، الطلب، `LineRank`، `ReserveUntil`، الحالة، `RecalledRequisitionId` |
| | `RecruitmentAppeals` | + `Kind` varchar not null default `'Rejection'` (للصفوف القائمة)، `CompetitionResultVersionId`، `ObjectionExamCandidateId`، `ObjectionCorrectedScore` |
## إضافات HCM-032 / PLAN-32 (تحرير يدوي)

حُدِّث هذا الجرد يدوياً لـ PLAN-32 لأن Python غير متاح على جهاز التنفيذ (المولّد `scripts/extract_database_inventory.py` لم يُشغَّل)؛
يُعاد توليده عند توفره. الترحيلات الثلاثة إضافية فقط (لا إعادة كتابة لبيانات قائمة).

| الترحيل | الجدول | الأعمدة / الفهارس |
|---------|--------|-------------------|
| `HCM032_OfferDeclineCategory` (HR.Core) | `OfferLetters` | + `DeclineReasonCategory` varchar(20) nullable (`Salary`/`Location`/`OtherOffer`/`Personal`/`JobContent`/`Other`؛ الصفوف القديمة null = «غير مصنف») |
| `HCM032_AnalyticsIndexes` (HR.Core) | `RecruitmentApplications` | فهرس `(TenantId, AppliedAt)` |
| | `RecruitmentApplicationEvents` | فهرس `(TenantId, EventType, ToStatus, OccurredAt)` |
| `ANA032_RecruitmentRequisitionFacts` (Dev.Analytics) | `RecruitmentRequisitionFacts` (جديد) | `Id`، `TenantId`، `RequisitionId`، `OrgUnitId`، `Outcome` varchar(16)، `ClosedAt`، `Applicants`، `HiredCount`، `TimeToFillDays`?، أعداد القنوات الأربع، أعداد نتائج العروض الأربع، `SchemaVersion`، `CreatedAt`/`UpdatedAt`؛ فريد `(TenantId, RequisitionId)` وفهرس `(TenantId, ClosedAt)`؛ RLS مُشدَّد (TEN-002C) |

## مراجعة PLAN-33 (2026-10-29، تحرير يدوي)

لم يُشغَّل المولّد `scripts/extract_database_inventory.py` لأن Python غير متاح على جهاز التنفيذ. أُعيد تنفيذ فحوصه بقراءة الشيفرة مباشرة:
كل ملف `src/**/*DbContext.cs` (عدا مجلدات `Tests` و`Migrations` والسياقات المجردة) وتصريحات `DbSet<X> Name =>` فيه بترتيب ورودها،
وأسماء `b.ToTable("...")` في ملف `*ModelSnapshot.cs` المقابل لكل سياق، وقيمة `Database=` في `appsettings.json` لكل خدمة، ثم قورنت بهذا الجرد وبملف `DATABASE_SCHEMA_INVENTORY.json`.

النتيجة: 15 سياقاً و326 DbSet، وتطابقت الخدمات كلها في عدد DbSet وجداول snapshot وأسماء قواعد البيانات، عدا ما يلي وقد صُحِّح:

| السياق | الانحراف | التصحيح |
|--------|----------|---------|
| `AnalyticsDbContext` (Dev.Analytics) | `DbSet<RecruitmentRequisitionFact> RecruitmentRequisitionFacts` والجدول `RecruitmentRequisitionFacts` في snapshot (ترحيل `ANA032_RecruitmentRequisitionFacts`، PLAN-32) غائبان عن الجرد | أُضيفا إلى JSON وإلى صف الملخص وقائمة الكيانات؛ DbSet من 10 إلى 11، وجداول الأعمال من 8 إلى 9 |
| `HRCoreDbContext` (HR.Core) | الأعداد صحيحة (109 DbSet، 112 جدولاً منها 107 جداول أعمال)، لكن كيانات المسابقات الأحد عشر (`RecruitmentCompetition` … `CompetitionReserveEntry`) المضافة يدوياً في PLAN-31 وُضعت في غير موضعها في الشيفرة | أُعيد ترتيبها في JSON وفي قائمة الكيانات لتلي `RecruitmentRequisition` كما في `HRCoreDbContext.cs`، دون تغيير في الأعداد |
| `ContractsDbContext` (HR.Contracts) | لا انحراف (26 DbSet، 29 جدولاً منها 24 جدول أعمال) | لا تغيير |

يبقى الجرد قابلاً لإعادة التوليد بالمولّد عند توفر Python، والملاحق اليدوية أعلاه (PLAN-30 وPLAN-31 وPLAN-32) ليست من مخرجاته فيلزم الإبقاء عليها عند إعادة التوليد.

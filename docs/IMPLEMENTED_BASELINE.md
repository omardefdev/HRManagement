# خط الأساس المنفّذ (HRManagement)

**الغرض:** قائمة مرجعية لما أُنجز في الشيفرة والوثائق التشغيلية، لتفادي ازدواجية مع [REMAINING_IMPLEMENTATION.md](./REMAINING_IMPLEMENTATION.md) الذي يصف **ما يظل مطلوباً للمنتج الكامل**.

**آخر مراجعة:** 2026-05-29

## البنية التحتية المشتركة

| البند | الموقع / الملفات |
|--------|-------------------|
| فحوص الصحة (PostgreSQL + RabbitMQ اختياري) | `HRManagement.ServiceDefaults` — `HealthCheckExtensions`, `RabbitMqHealthCheck` |
| تعطيل فحص RabbitMQ | `HealthChecks:IncludeRabbitMq` في الإعدادات |
| سياسة الترحيل | `Database:RunMigrationsAtStartup` + [DATABASE_MIGRATION_POLICY.md](./DATABASE_MIGRATION_POLICY.md) |
| Correlation ID (HTTP + Serilog) | `CorrelationIdMiddleware`, `CorrelationIdFlow`, `UseHrCorrelationId` |
| MassTransit — تمرير Correlation إلى الرسائل | `MassTransitExtensions.ConfigurePublish` يضبط رأس `X-Correlation-ID` عند النشر |
| MassTransit — إعادة محاولة الرسائل | `MassTransitExtensions` — `UseMessageRetry` عبر `MassTransit:RetryCount` و`RetryIntervalSeconds` (افتراضي 3×5 ثوانٍ) على **جميع** الخدمات |
| MassTransit — outbox | HR.Contracts، HR.Core، Payroll — `AddPostgresOutbox` |
| MassTransit — fault logging | `MassTransitFaultLogger` (`IConsumeObserver`) |
| Keycloak — أدوار وحدات + سياسات | `HrManagementPolicies`، `GatewayPathAuthorizationMiddleware`، فلاتر MVC لكل خدمة |
| إنتاج — بروكسي وCORS | `AddHrProductionNetworking` / `UseHrProductionMiddleware`، `appsettings.Production.json` للبوابة |
| Keycloak — أسرار من البيئة | `Keycloak__ClientSecret` + [KEYCLOAK_AND_SECRETS.md](./KEYCLOAK_AND_SECRETS.md) |
| تدقيق بيانات حساسة | `SensitiveDataAudit` (Compliance شكوى، HR.Health توصية) |

## الخدمات (كل تطبيق ويب خدمة)

- `AddHrServiceHealthChecks<TDbContext>` + `MapHealthChecks("/health")` على الـ 13 خدمة.
- `UseHrCorrelationId()` بعد `UseRouting`.

## البوابة (Gateway)

- YARP: مسارات `/health/upstreams/{service}` → `/health` على كل cluster (انظر `appsettings.json`).
- لوحة رئيسية مع جلب حالة الخدمات (`HomeController.UpstreamHealth`).
- صفحة مسار الفصل: `Views/Home/SeparationJourney.cshtml`.
- مسار التوظيف الجديد: `Home/HireJourney` → `JourneyDetails` (`new-hire`) — **مرحلتان**: HCM-001 (12 خطوة توظيف + `/careers`) ثم FUN-001 (معالج التعيين والخدمات).
- تضمين جزئي من RCL: `HRManagement.Web.Shared` (`_HrSharedFooter`).
- إعدادات dev للجلسة: `HrAuth` cookie في `appsettings.Development.json` — انظر [`UI_CHECKLIST_RESULTS.md`](./UI_CHECKLIST_RESULTS.md).

## MassTransit — مستهلكون مسجّلون (تحديث 2026-05)

| الحدث | المستهلك | الخدمة |
|--------|-----------|--------|
| `ITrainingCompleted` | `TrainingCompletedConsumer` | HR.Core |
| `IPayrollProcessed` | `PayrollProcessedConsumer` | Dev.Analytics |
| `ITransferExecuted` | `TransferExecutedConsumer` | HR.Core، Admin.Logistics، Payroll، Auth.Directory |
| `ISecondmentExecuted` | `SecondmentExecutedConsumer` | Payroll، Auth.Directory |
| `IMissionApproved` | `MissionApprovedConsumer` | Payroll |
| `IDisciplinaryActionTaken` | `DisciplinaryActionTakenConsumer` | Payroll |
| `IEmployeeCreated` | `EmployeeCreatedConsumer` | HR.Contracts (قائمة تحقق P2.1 فقط عند مسار التعيين)، IT.Infra، Dev.Analytics |
| `IHireCaseCompleted` | `HireCaseCompletedConsumer` | Records.Archive |
| `IHireCaseCompleted` | `HireCaseCompletedDiwanConsumer` | HR.Core → `ITransactionRegisteredInDiwan` |
| `ICardIssued` | `CardIssuedHireChecklistConsumer` | HR.Contracts |
| `ICardIssued` | `CardIssuedAnalyticsConsumer` | Dev.Analytics |
| `IComplaintResolved` | `ComplaintResolvedConsumer` | Dev.Analytics |
| `IComplaintResolved` | `ComplaintResolvedArchiveConsumer` | Records.Archive |
| `ISeparationCompleted` | `SeparationCompletedConsumer` | IT.Infra، Dev.Analytics، HR.Core (بالإضافة إلى Payroll / Archive / IDCard / Admin) |

مصفوفة كاملة: [MESSAGE_EVENT_MATRIX.md](./MESSAGE_EVENT_MATRIX.md)، تدقيق: [EVENT_MESH_AUDIT.md](./EVENT_MESH_AUDIT.md). تشغيل RabbitMQ: [RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md).

## Idempotency (أنماط متعددة)

| النمط | أمثلة |
|--------|--------|
| `IntegrationMessageReceipts` (MessageId + ConsumerType) | HR.Core، HR.Health، Admin.Logistics، IT.Infra — انظر [IDEMPOTENCY_STRATEGY.md](./IDEMPOTENCY_STRATEGY.md) |
| `AdjustmentReason` / `Reason` markers | Payroll — إجازة، سلف، انتداب، مهمة، تأديب، فصل، عقد |
| `ReferenceNumber` | Records.Archive — فصل، إجازة، سلف |
| وصف التذكرة / payload JSON | IT.Infra — `[EmployeeCreated:{guid}]`؛ Dev.Analytics — `referenceKey` في payload |
| `AuthAuditLog.Details` marker | Auth.Directory — `SecondmentExecutedConsumer` |

`SchemaVersion` افتراضي `1` على واجهات أحداث [MessageContracts](../src/Shared/HRManagement.MessageContracts/Events).

## Payroll — قواعد فترة جزئية

- `PayrollPeriodRules` — أيام العمل، العطل الرسمية، التناسب، التحقق من الفترة (وحدات: `PayrollPeriodRulesTests`).
- **ليس** محرك رواتب قانوني كامل — انظر [REMAINING_IMPLEMENTATION.md](./REMAINING_IMPLEMENTATION.md).

## واجهات مستخدم (تعميق جزئي)

- Compliance: دورة شكوى (إنشاء، تفاصيل، تعيين، تحقيق، قرار، استئناف، إغلاق، متابعات).
- Records.Archive: إنشاء عنصر، تفاصيل، إعارة، إرجاع، فترة احتفاظ، **تسجيل إتلاف** (`RecordDisposal`).
- Admin.Logistics: تفاصيل أصل، تسليم، استرداد، **مخزون** (`InventoryItemsController`).
- IDCard: **طباعة وتسليم** على البطاقات المصدرة.
- HR.Training: **دورات** (`TrainingCoursesController`).
- HR.Health: **فحوص دورية** (`PeriodicMedicalChecksController`).
- IT.Infra: تذاكر، **نسخ احتياطي** (`BackupJobsController` — metadata + رابط [BACKUP_POSTGRES.md](./BACKUP_POSTGRES.md))، **حوادث أمنية** (`SecurityIncidentsController`).
- Dev.Analytics: لوحات + **API** `GET /api/analytics/events`.

## Auth.Directory

- `EmployeeCreatedConsumer`: إنشاء `AuthUser`؛ **Keycloak Admin API** عند `Keycloak:DirectorySync:Enabled=true` (`KeycloakUserProvisioningService`).
- `EmployeeStatusChangedConsumer`: تعطيل تقريبي حسب نص الحالة.
- `SecondmentExecutedConsumer`: تحديث `DirectoryEntry` + `AuthAuditLog` مع marker idempotency.
- `AuthUsersController`: مزامنة يدوية عبر `IKeycloakUserProvisioningService` (ليست placeholder).
- إعدادات التطوير: `DirectorySync:Enabled=true` في `appsettings.Development.json`.

## سير العمل — HR.Core / HR.Contracts (v1 workflow completion)

| البند | الموقع |
|--------|--------|
| قواعد تفعيل العقد + `ActivateContractCommand` | `HR.Contracts.Domain/Services/ContractWorkflowRules.cs`، `Features/Contracts/ActivateContractCommands.cs` |
| معاملة إجازة/سلفة مرتبطة + إكمال عند الاعتماد | `LeaveRequestHandlers`، `AdvanceRequestHandlers`، `ServiceTransactionWorkflowHelper` |
| فصل موحّد + قائمة تحقق افتراضية | `SeparationChecklistDefaults`، `PublishSeparationHandler`، `SeparationWizardController` → MediatR |
| تعيين موحّد + قائمة تحقق DFD | `HireChecklistDefaults`، `HireWorkflowRules`، `PublishHireCaseHandler`، `HireWizardController`، `ProcessCandidateAcceptedHandler` |
| صلاحيات التعيين | [HIRE_RBAC_MATRIX.md](./HIRE_RBAC_MATRIX.md) |
| دليل استخدام واختبار التعيين | [HIRE_ONBOARDING_GUIDE.md](./HIRE_ONBOARDING_GUIDE.md) |
| حالة الموظف عند اعتماد الإجازة | `LeaveApprovedConsumer` |
| تعيين → `ICandidateAccepted` | `RecruitmentIntegrationService`، `RecruitmentApplicationsController` |
| تسجيل ديوان → `ITransactionRegisteredInDiwan` | `DiwanIntegrationService`، `DiwanCorrespondencesController` |
| موافقات (استخراج خفيف) | `ApprovalWorkflowService`، `ApprovalsController` |
| انتقالات حالة الموظف | `EmployeeLifecycleService`، `EmployeesController` |

## الاختبارات والتشغيل

- [CI_AND_TESTING.md](./CI_AND_TESTING.md) — `RUN_TESTCONTAINERS=true` لمشروع التكامل.
- **وحدات:** ~30 ملفاً — عقود، Correlation، مستهلكون، `LeaveAndAdvanceRulesTests`، `ContractWorkflowRulesTests`، `HireWorkflowRulesTests`، `RecruitmentWorkflowRulesTests`، `ServiceTransactionWorkflowHelperTests`، `EmployeeLifecycleServiceTests`، `PayrollPeriodRulesTests`.
- **تكامل تعيين:** `HireCaseIntegrationTests` (معاملة + checklist + `IHireCaseCompleted`).
- **تكامل MassTransit:** `MassTransitConsumerTests`، `HRCoreConsumerTests`، `AdditionalConsumerIntegrationTests` (15 اختباراً؛ Testcontainers اختياري).
- **بوابة /ui:** إعدادات dev في [`UI_CHECKLIST_RESULTS.md`](./UI_CHECKLIST_RESULTS.md)؛ اختبار جلسة Keycloak يدوي.

## أخرى

- Swagger في Development على **Payroll**، **HR.Core**، **HR.Contracts**؛ قائمة `/ui`: [UI_CHECKLIST.md](./UI_CHECKLIST.md).
- HR.Core: تصدير CSV (`ReportsController`: موظفون، سجل حالات).
- RCL: `_HrAlerts` (TempData) بجانب `_HrSharedFooter`.
- نسخ PostgreSQL: [BACKUP_POSTGRES.md](./BACKUP_POSTGRES.md).
- [RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md)، [SECURITY_AND_COMPLIANCE.md](./SECURITY_AND_COMPLIANCE.md).

## الوحدات الوظيفية المستكمَلة (2026-06 — عمق أطروحة/عرض)

أُسندت كل وحدة ناقصة إلى **خدمة قائمة** دون إنشاء خدمة جديدة، متبعةً نمط الكيانات (`AuditableEntity`) + `DbContext` + متحكم CRUD + Views عربية RTL + ترحيل EF + بيانات Seed.

| الوحدة | الخدمة | الكيانات | المتحكمات | قاعدة الأعمال (قابلة للاختبار) | الترحيل |
|--------|--------|----------|-----------|-------------------------------|---------|
| المناوبات والورديات | HR.Core | `Shift`, `ShiftRoster`, `ShiftAssignment` | `ShiftsController`, `RostersController` | `ShiftAllowanceRules` (بدل ليلي/مسائي + عمل إضافي) | `AddShiftScheduling` |
| الصحة والسلامة المهنية | HR.Health | `WorkplaceAccident`, `SafetyInspection`, `FirstAidTrainingRecord` | `WorkplaceSafetyController` | ربط أيام الإجازة المرضية بحادث العمل | `AddOccupationalSafety` |
| التعاقب والمسارات | HR.Core | `CareerPath`+`CareerPathStep`, `KeyPosition`, `SuccessionPlan`+`SuccessorCandidate` | `CareerPathsController`, `KeyPositionsController`, `SuccessionPlansController` | `SuccessionReadinessRules` (مربوطة بآخر تقييم) | `AddSuccessionAndCareerPaths` |
| سوق المواهب الداخلي | HR.Core | `InternalVacancy`, `InternalApplication` | `TalentMarketplaceController` (إدارة) + `EssTalentController` (ترشّح ذاتي ESS) | منع الترشّح المكرر + سير حالة الترشّح | `AddInternalTalentMarketplace` |
| التعلّم الإلكتروني (LMS) | HR.Training | `ELearningCourse`, `CourseModule`, `Lesson`, `LearningEnrollment`, `LearningCertificate` | `ELearningCoursesController` (إدارة) + `EssLearningController` (التحاق ذاتي + تقدّم + شهادة) | `LearningCompletionRules` (إكمال 100% → شهادة تلقائية) | `AddELearning` |
| بوابة المواطن للتوظيف | HR.Core + Gateway | `PublicJobPosting`, `CitizenApplication` | `PublicCareersController` ‏`[AllowAnonymous]` + `PublicJobPostingsController` (إدارة) | رقم تتبّع مرجعي فريد + تتبّع بالرقم الوطني | `AddCitizenCareersPortal` |

**نقاط شاملة:**
- بوابة عامة مجهولة: مسار YARP `careers-public` (`/careers/**`) → `hr-core-cluster`، وفلتر الوحدات `HrModuleAuthorizationFilter` صار يحترم `[AllowAnonymous]`. تخطيط عام `_PublicLayout.cshtml`.
- ESS: متحكمات `EssTalent`/`EssLearning` تتجاوز فلتر الوحدة (بادئة `Ess`) وتستخدم سياسة `Module.Ess`.
- بيانات Seed: ورديات نهارية/مسائية/ليلية، دورتا تعلّم إلكتروني، إعلانا توظيف عامان (`HRCoreDataSeeder`، `ELearningSeeder`).
- اختبارات وحدة: `MissingModulesRulesTests` (13 اختباراً) تغطي قواعد البدلات والجاهزية وإكمال الدورة وترقيم الشهادة.
- التنقّل: مدخلات قائمة جانبية عربية في `_Layout.cshtml` لكل خدمة معدّلة.

---

عند إضافة ميزة كبيرة، حدّث هذا الملف ثم اضبط [REMAINING_IMPLEMENTATION.md](./REMAINING_IMPLEMENTATION.md) لإزالة أو تخفيف البنود المغطاة.

# الفصل الثاني: الدراسة النظرية

## 2.1 المفاهيم الأساسية

يُعدّ فهم المفاهيم النظرية التي يستند إليها مشروع **HRManagement** شرطاً أساسياً لقراءة الفصل التطبيقي؛ إذ تُترجم هذه المفاهيم إلى قرارات معمارية ملموسة: تقسيم النظام إلى أربع عشرة خدمة، عزل قواعد البيانات، وتبادل الأحداث عبر **RabbitMQ**. يستعرض هذا القسم المفاهيم المركزية وعلاقتها المباشرة بمتطلبات شعبة التنظيم والموارد البشرية في المؤسسة المدنية محل الدراسة.

### 2.1.1 نظام إدارة الموارد البشرية (HRMS)

**نظام إدارة الموارد البشرية (Human Resources Management System — HRMS)** هو منظومة معلومات تدعم دورة حياة القوى البشرية: التخطيط، الاستقطاب، التوظيف، إدارة الأداء، التعويضات، التدريب، التنقلات، والامتثال [13]. في البيئات المدنية الكبيرة، لا يقتصر HRMS على «سجل موظف» بل يشمل فروعاً متخصصة — الشؤون الشخصية، الخدمة والعقود، الصحية، الرواتب، الأرشيف — كما ينعكس في الهيكل التنظيمي للشعبة.

في مشروع **HRManagement**، يُوسَّع مفهوم HRMS ليشمل **ثلاثة عشر مجالاً إدارياً** تُمثَّل بخدمات مستقلة: **HR.Core** للملفات المرجعية، **HR.Contracts** للعقود والإجازات، **Payroll** للرواتب، **HR.Health** للشؤون الصحية، **Compliance** للشكاوى والتأديب، وغيرها. يغطي النظام مساراً متكاملاً من **الاستقطاب** (`PublicJobPosting`, `CitizenApplication`) إلى **الأرشفة** (`ArchiveItem`)، مع بوابة موظف (**ESS**) وبوابة توظيف عامة (`/careers`).

### 2.1.2 Bounded Context (السياق المحدود)

في **Domain-Driven Design**، يُعرَّف **Bounded Context** بأنه حدود لغوية ونموذجية تضمن أن كل مصطلح له معنى واحد داخل السياق [1]. مثلاً: كيان **Employee** في **HR.Core** يحمل الاسم الكامل والرقم الوطني والهيكل التنظيمي، بينما **EmployeeId** في **Payroll** مرجع مالي فقط (`EmployeePayProfile`) دون تكرار بيانات حساسة.

يُطبِّق المشروع هذا المبدأ عبر **Database-per-Service**: كل DbContext — `HRCoreDbContext`, `ContractsDbContext`, `PayrollDbContext` — يملك جداوله دون **Foreign Keys** عابرة للقواعد. التكامل يتم بمراجع **Guid** (`EmployeeId`, `OrgUnitId`) وأحداث تكامل (`IEmployeeCreated`, `IContractActivated`) [4].

### 2.1.3 Eventual Consistency (الاتساق النهائي)

في الأنظمة الموزعة، لا تُضمَن المعاملة الذرية (**ACID transaction**) عبر جميع الخدمات في لحظة واحدة؛ بل يُقبَل **تأخير قصير** حتى تُستهلك الأحداث وتُحدَّث الخدمات الم downstream [4]. عند تعيين موظف، ينشر **HR.Core** حدث `IEmployeeCreated`؛ وتستهلكه **Payroll** لإنشاء `EmployeePayProfile` و**Auth.Directory** لإنشاء مستخدم **Keycloak** — قد يمرّ بضع ثوانٍ قبل اكتمال الربط.

يُخفِّف **HRManagement** مخاطر إعادة التسليم عبر: **Outbox Pattern** (كتابة الحدث مع المعاملة في نفس DB)، **Idempotency** عبر جدول `IntegrationMessageReceipt` بمفتاح `(MessageId, ConsumerType)` فريد، و**Correlation ID** (`X-Correlation-ID`) للتتبع [13].

### 2.1.4 RBAC (Role-Based Access Control)

**التحكم بالوصول القائم على الأدوار (RBAC)** يفصل المصادقة (**Authentication**) عن التفويض (**Authorization**) [15]. في المشروع: **Keycloak** يُصدِر أدواراً (`hr-core`, `payroll`, `hr-employee`...) تُربَط بسياسات ASP.NET Core (`Module.HrCore`, `Payroll.Process`...) عبر `HrManagementAuthorizationExtensions`.

تُطبَّق الصلاحيات على خمس طبقات: مصادقة عامة في **Gateway**، `GatewayPathAuthorizationMiddleware`، `HrModuleAuthorizationFilter` لكل خدمة، `[Authorize(Policy)]` على الإجراءات، و**Cadre/Org scoping** على مستوى البيانات — لضمان أن مدير فرع يرى موظفي وحدته فقط.

---

## 2.2 معمارية الخدمات المصغّرة (Microservices)

### 2.2.1 التعريف والمبررات

**معمارية الخدمات المصغّرة (Microservices Architecture)** تقسّم التطبيق إلى خدمات صغيرة مستقلة قابلة للنشر والتوسع بمعزل عن بعضها، تتواصل عبر HTTP أو **Message Broker** [2][3]. اختير هذا النمط لأن متطلبات شعبة HR — أكثر من 2000 سطر في `Requariments.md` — تضم مجالاتاً وظيفية متباعة الحساسية: بيانات صحية (**HR.Health**) منفصلة عن شكاوى (**Compliance**) ورواتب (**Payroll**).

**[شكل (2-1): مخطط معمارية Microservices للنظام]**

يُوضِّح الشكل (2-1) — **Microservices Architecture Diagram** — الطبقات التالية من أعلى لأسفل:

1. **Client Layer:** متصفح المستخدم (موظف، مسؤول HR، مواطن على `/careers`) يتصل بنقطة دخول واحدة.
2. **API Gateway (YARP — Port 5000):** يستقبل الطلبات، يُجري **OIDC Authentication** مع **Keycloak**، يطبّق **RBAC**، ويُوجِّه `/ui/{module}/**` إلى Cluster الخدمة المناسبة.
3. **Application Services Layer:** أربع عشرة خدمة ASP.NET Core MVC — من **HR.Core (5010)** إلى **File.Storage (5140)** — كل منها Domain/Infrastructure/Web.
4. **Integration Layer:** **RabbitMQ 3.13** مع **MassTransit** لنشر واستهلاك 39 حدث تكامل.
5. **Data Layer:** خمس عشرة قاعدة **PostgreSQL 16** (14 للخدمات + `gateway_audit_db`).
6. **Identity Layer:** **Keycloak 24** realm `hr-management` للمصادقة المركزية.

الأسهم الصلبة تمثّل طلبات HTTP عبر YARP؛ الأسهم المتقطعة تمثّل أحداث AMQP. لا يوجد سهم FK بين قواعد البيانات — فقط مراجع منطقية وأحداث.

**[جدول (2-1): مقارنة Microservices vs Monolith]**

| المعيار | Monolith | Microservices (HRManagement) |
|---------|----------|------------------------------|
| النشر | وحدة واحدة | 14 خدمة + Gateway مستقلة |
| قاعدة البيانات | DB واحدة | 15 DB (Database-per-Service) |
| التوسع | عمودي | أفقي per service (مثلاً Payroll) |
| العزل | ضعيف | قوي (Health ≠ Payroll ≠ Compliance) |
| التعقيد التشغيلي | منخفض initially | مرتفع (15 DB + RabbitMQ + Keycloak) |
| الملاءمة | مشاريع صغيرة | 13+ مجال HR مؤسسي |

### 2.2.2 Database-per-Service

كل خدمة تملك **DbContext** و**Migrations** مستقلة على قاعدة PostgreSQL منفصلة (منفذ **5433** في Docker Compose). مثال: `hr_core_db` لـ **HRCoreDbContext**، `payroll_db` لـ **PayrollDbContext**. هذا يمنع **Coupling** على مستوى المخطط ويُمكّن فرقاً مختلفة من تطوير **HR.Training** و**Payroll** بالتوازي [4].

---

## 2.3 Domain-Driven Design (DDD)

### 2.3.1 الطبقات في كل خدمة

**[شكل (2-2): طبقات DDD في كل خدمة]**

يُبيِّن الشكل (2-2) — **DDD Layered Architecture** — الهيكل المتكرر في كل خدمة:

```
{Service}.Domain/         ← Entities, Enums, Domain Services, Business Rules
{Service}.Infrastructure/ ← DbContext, Consumers, Repositories, Migrations
{Service}.Web/            ← Controllers, Razor Views, Program.cs
```

- **Domain Layer:** يحتوي قواعد العمل الخالصة — مثل `HireWorkflowRules` التي تتحقق من اكتمال خطوات قائمة التحقق (P1.1–P7.1)، و`PayrollPeriodRules` لحساب أيام العمل والعطل.
- **Infrastructure Layer:** تنفيذ **EF Core**، مستهلكي **MassTransit**، و**Outbox**. مثال: `EmployeeCreatedConsumer` في **Payroll.Infrastructure**.
- **Web Layer:** واجهات **MVC** العربية RTL مع **AdminLTE 3** و**HRManagement.Web.Shared** RCL.

### 2.3.2 Shared Kernel

مشروع **HRManagement.SharedKernel** يوفّر **BaseEntity** (`Id`, `TenantId`, `CreatedAt`, `UpdatedAt`)، **AuditableEntity** (`CreatedBy`, `UpdatedBy`)، **EntityAuditLog**، وEnums مشتركة (`EmploymentStatus`, `Gender`). يُعدّ هذا **Shared Kernel** [1] — جزءاً مشتركاً محدوداً دون تسرب منطق Domain بين الخدمات.

### 2.3.3 Domain Services في المشروع

| Domain Service | الخدمة | الوظيفة |
|----------------|--------|---------|
| `EmployeeLifecycleService` | HR.Core | إدارة دورة حياة الموظف والحالات |
| `ApprovalWorkflowService` | HR.Core | سير موافقات generic (`ApprovalRequest/Step`) |
| `ContractWorkflowRules`, `HireWorkflowRules` | HR.Contracts | قواعد التعيين والعقود |
| `PayrollPeriodRules` | Payroll | أيام الفترة، العطل، التناسب |
| `SuccessionReadinessRules`, `ShiftAllowanceRules` | HR.Core | التعاقب والمناوبات |
| `LearningCompletionRules` | HR.Training | إتمام التعلم الإلكتروني |

---

## 2.4 Event-Driven Architecture

### 2.4.1 نموذج التكامل بالأحداث

**[شكل (2-3): نموذج Event-Driven Integration]**

يُظهر الشكل (2-3) — **Event-Driven Integration Pattern** — تدفقاً نموذجياً:

1. **Publisher Service** (مثلاً HR.Core) ينفّذ معاملة DB ويكتب **OutboxMessage**.
2. **MassTransit** ينشر الحدث إلى **RabbitMQ Exchange**.
3. **Multiple Consumers** (HR.Contracts, Payroll, IDCard...) تستقبل بالتوازي.
4. كل Consumer يتحقق من **Idempotency** قبل المعالجة.

### 2.4.2 عقود الأحداث

تُعرَّف 39 واجهة حدث في `HRManagement.MessageContracts/Events/` — مثل `IEmployeeCreated`, `ILeaveApproved`, `ISeparationCompleted`. كل حدث يحمل `SchemaVersion` لدعم التطور المستقبلي [7].

**Outbox Pattern:** خدمات **HR.Core**, **HR.Contracts**, **Payroll** تستخدم `AddPostgresOutbox` — جداول `OutboxMessage` و`OutboxState` — لضمان أن نشر الحدث لا يحدث إلا بعد commit المعاملة [4][7].

**Idempotency:** جدول `IntegrationMessageReceipt` — UK: `(MessageId, ConsumerType)` — في **HR.Core**, **Payroll**, **HR.Health**, **Admin.Logistics**, **IT.Infra**.

**Correlation:** رأس HTTP `X-Correlation-ID` يُمرَّر عبر Gateway إلى Serilog و`/Home/Operations` للتتبع التشغيلي.

---

## 2.5 .NET 10 و ASP.NET Core MVC

### 2.5.1 منصة .NET 10

يُبنى مشروع **HRManagement** على **.NET 10 SDK** [5] لاستفادة المشروع من تحسينات **Kestrel**، تكامل **OpenID Connect**، وتوافق **YARP**. الحل `HRManagement.slnx` يضم أكثر من 45 مشروعاً (14 خدمة × 3 طبقات + Shared + Tests + Gateway).

### 2.5.2 ASP.NET Core MVC

اختير **ASP.NET Core MVC** [5] لأن واجهات HR إدارية تتطلب نماذج معقدة، جداول، ومعالجات متعددة الخطوات (**HireWizard**, **SeparationWizard**) — وهذا يناسب **Server-side Rendering** بـ **Razor Views** أكثر من SPA لـ v1. كل خدمة Web تستدعي `AddServiceDefaults()` — health checks، Serilog، MassTransit — ثم `MapControllerRoute` convention. **AntiForgeryToken** إلزامي على POST.

### 2.5.3 AdminLTE و Web.Shared

**AdminLTE 3** مع دعم **RTL** وخط **ITF Qomra Arabic** يوفّر `_Layout.cshtml` موحّداً. **HRManagement.Web.Shared** RCL يوفّر `_HrSharedFooter`, `_HrAlerts` (TempData), وألوان الهوية (#6b1f2a, #428177). Sidebar يعرض روابط Controllers حسب Module policy.

### 2.5.4 MediatR و CQRS

**MediatR** يطبّق نمط **Mediator** [14]: `ActivateContractCommand`, `PublishHireCaseCommand`, `ApproveLeaveRequestCommand`, `CalculatePayrollRunCommand`, `PostPayrollRunCommand` — يُبقي Controllers رفيعة و Domain logic قابلة للاختبار.

---

## 2.6 Entity Framework Core و PostgreSQL

### 2.6.1 EF Core Code-First

**Entity Framework Core 10** [6] ORM يُستخدم **Code-First** مع Migrations مستقلة لكل DbContext — مثلاً `20260301120000_InitialHRCore` في `HR.Core.Infrastructure/Migrations/`. لا Views ولا Stored Procedures — كل المنطق في C# Domain Services [13]. `OnModelCreating` يُعرِّف FK behaviors: **Cascade** للملفات الفرعية (Documents, Qualifications)، **Restrict** للعقود، **SetNull** للOrgUnit عند حذف.

**UtcDateTimeNormalization** في `SaveChangesAsync` يضمن `CreatedAt/UpdatedAt` UTC — critical للتكامل cross-timezone.

### 2.6.2 PostgreSQL 16

**PostgreSQL 16** [8] على Docker port **5433** يوفّر ACID، JSON support، partial indexes، وNpgsql EF provider — مناسب للاستضافة الذاتية المؤسسية. Connection strings في `appsettings.Development.json` per service: `Host=localhost;Port=5433;Database=hr_core_db;...`

**Database-per-Service:** 15 connection string منفصلة — لا shared schema.

### 2.6.3 Repository Pattern و Unit of Work

```csharp
services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
services.AddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
```

**Repository** [14] يُجرّد `GetByIdAsync`, `ListAsync`, `AddAsync`. **Unit of Work** يضمن commit موحّد لكل HTTP request. Consumers MassTransit تستخدم DbContext مباشرة أو MediatR حسب التعقيد.

### 2.6.4 Migrations و CI

`dotnet ef migrations add` per service. CI pipeline (`CI_AND_TESTING.md`) يشغّل `dotnet test` مع **Testcontainers** PostgreSQL — يتحقق من schema compatibility دون production DB.

---

## 2.7 RabbitMQ و MassTransit

### 2.7.1 RabbitMQ

**RabbitMQ 3.13** [10] Message Broker على منفذ **5672** (Management UI **15672**). يدعم AMQP 0-9-1، exchanges per message type، **Dead Letter Queues** (`*_error` — راجع `RABBITMQ_OPERATIONS.md`)، وpersistent messages. Credentials من env vars في Docker Compose.

### 2.7.2 MassTransit

**MassTransit 8.x** [7] طبقة تجريد: `AddMassTransit` في Infrastructure، `UseMessageRetry` (3×5s)، `MassTransitFaultLogger`، Consumer registration per service، و**EventSchemaVersion** للتوافق. 39 event interfaces في `HRManagement.MessageContracts`.

### 2.7.3 Retry و Idempotency

عند فشل consumer → message to `*_error` queue. **IdempotentConsumerFilter** + `IntegrationMessageReceipt` يمنع duplicate side effects — critical لـ Payroll financial adjustments.

---

## 2.8 YARP API Gateway

**YARP (Yet Another Reverse Proxy)** [11] في **HRManagement.Gateway** (Port **5000**) — Microsoft native reverse proxy لـ .NET.

### 2.8.1 التوجيه (Routing)

`appsettings.json` Clusters وRoutes:

| Route Pattern | Upstream | Port |
|---------------|----------|------|
| `/ui/hr-core/{**catch-all}` | HR.Core | 5010 |
| `/ui/hr-contracts/{**catch-all}` | HR.Contracts | 5020 |
| `/ui/payroll/{**catch-all}` | Payroll | 5060 |
| `/api/files/{**catch-all}` | File.Storage | 5140 |

Path transform يُزيل prefix `/ui/{module}` قبل proxy.

### 2.8.2 Authorization Middleware

**GatewayPathAuthorizationMiddleware** يُطبّق قبل YARP:

- GET → policy `Module.{Name}` ReadOnly
- POST/PUT/DELETE → Write policy
- `/careers` → AllowAnonymous

**GatewayAccessTokenTransform** يُمرّر **Bearer JWT** من Cookie session لل upstream عند `HrAuth:BehindGateway=true`.

### 2.8.3 Audit

كل request يُسجَّل في `gateway_audit_entries` — CorrelationId, UserSub, Path, Decision, StatusCode — للمراجعة الأمنية.

---

## 2.9 Keycloak و OpenID Connect

### 2.9.1 Keycloak IAM

**Keycloak 24** [9] خادم **Identity and Access Management** — Docker port **8080**, realm **`hr-management`**, client **`hr-gateway`**. يدير Users, Roles, Sessions خارج تطبيق HR — فصل concerns أمني [15].

### 2.9.2 OIDC Flow

**OpenID Connect Authorization Code Flow:**

1. User → `/Account/Login` → redirect Keycloak
2. Keycloak authenticate → authorization code
3. Gateway exchange code → tokens
4. Cookie session + access token stored
5. YARP forwards Bearer to modules

Modules: `AddJwtBearer` when behind gateway — validates issuer `http://localhost:8080/realms/hr-management`.

### 2.9.3 Directory Sync

**Auth.Directory** `KeycloakUserProvisioningService`:

- `IEmployeeCreated` → create Keycloak user + `AuthUser` row
- `IEmployeeStatusChanged` → enable/disable
- `ISeparationCompleted` → deactivate account

`AuthUser.KeycloakUserId` UK link — لا passwords in `auth_directory_db`.

---

## 2.10 Docker و Docker Compose

### 2.10.1 docker-compose.yml

**Docker Compose** [12] infra stack:

| Service | Ports | Volume |
|---------|-------|--------|
| postgresql | 5433:5432 | pgdata |
| rabbitmq | 5672, 15672 | rabbitmq_data |
| keycloak | 8080:8080 | — |

Init script ينشئ 15 database. Dev: services via `dotnet run`; staging: Dockerfiles per service.

### 2.10.2 Health Checks

`AddServiceDefaults()` registers `/health` و`/alive` — Gateway Dashboard ي poll كل cluster. **RELEASE_READINESS_STAGING.md** يحدد readiness criteria.

### 2.10.3 Production Overlay

`docker-compose.production.yml` — env vars, HTTPS, resource limits. **PRODUCTION_RUNBOOK.md** — backup PostgreSQL, RabbitMQ queue purge, Keycloak realm export.

---

## 2.11 أنماط التصميم (Design Patterns)

| Pattern | التطبيق في HRManagement |
|---------|-------------------------|
| Repository | `EfRepository<T>` |
| Unit of Work | `EfUnitOfWork<TContext>` |
| Mediator | MediatR handlers |
| Outbox | MassTransit Postgres Outbox |
| Gateway | YARP + middleware |
| Dependency Injection | ASP.NET Core DI |
| Shared Kernel | SharedKernel project |
| Integration Events | MessageContracts interfaces |

**SOLID:** Single Responsibility (service per domain)، Open/Closed (policies)، Liskov (`IRepository`)، Interface Segregation (`IEventPublisher`, `IFileReferenceClient`)، Dependency Inversion (DI) [14].

**[جدول (2-2): التقنيات المستخدمة ومبرراتها]**

| التقنية | المبرر |
|---------|--------|
| .NET 10 | أحدث ecosystem، MVC، أداء |
| PostgreSQL 16 | ACID، مفتوح المصدر، self-hosted |
| RabbitMQ 3.13 | AMQP ناضج، دعم MassTransit |
| Keycloak 24 | OIDC، RBAC، self-hosted |
| YARP | Reverse proxy أصلي .NET |
| Docker | dev/staging قابل للتكرار |
| Serilog | Structured logging + correlation |
| xUnit + Testcontainers | اختبار آلي |

---

# الفصل الثالث: التطبيق العملي

## 3.1 بيئة العمل وأدوات التطوير

### 3.1.1 متطلبات البيئة

| المكوّن | الإصدار | الغرض |
|---------|---------|-------|
| .NET SDK | 10 | بناء وتشغيل الحل |
| Docker Desktop | — | PostgreSQL, RabbitMQ, Keycloak |
| IDE | VS / VS Code / Rider | تطوير وتصحيح |
| Git + GitHub Actions | — | CI/CD |

### 3.1.2 هيكل الحل (Solution Structure)

```
HRManagement.slnx
src/Gateway/HRManagement.Gateway
src/Shared/{SharedKernel, MessageContracts, ServiceDefaults, Web.Shared}
src/Services/{HRCore, HRContracts, HRHealth, HRMovements, HRTraining,
              Payroll, IDCard, RecordsArchive, AdminLogistics,
              ITInfrastructure, DevAnalytics, Compliance,
              AuthDirectory, FileStorage}/
  {Service}.Domain/
  {Service}.Infrastructure/
  {Service}.Web/
src/Tests/{UnitTests, IntegrationTests}
```

### 3.1.3 أوامر التشغيل

```bash
docker-compose up -d
dotnet build HRManagement.slnx
cd src/Gateway/HRManagement.Gateway && dotnet run --urls="http://localhost:5000"
```

---

## 3.2 تحليل النظام

### 3.2.0 الهيكل التنظيمي وعلاقته بالنظام

**[شكل (1-1): الهيكل التنظيمي لشعبة الموارد البشرية]**

يُمثِّل الشكل (1-1) — **Organizational Structure Tree Diagram** — الهيكل التنظيمي للشعبة كما ورد في `Requariments.md`:

- **الجذر:** شعبة التنظيم والموارد البشرية — «تنظيم إمداد المؤسسة بالقوى البشرية».
- **الفروع الرئيسية (→ Bounded Context):**
  - إدارة الكادر / الشؤون الشخصية → **HR.Core**, **HR.Health**
  - فرع الخدمة والعقود → **HR.Contracts**
  - فرع التنقلات → **HR.Movements**
  - فرع التدريب → **HR.Training**
  - فرع الرواتب → **Payroll**
  - إدارة الهويات → **IDCard**
  - إدارة السجلات → **Records.Archive**
  - الإدارة واللوجستيات → **Admin.Logistics**
  - تقنية المعلومات → **IT.Infra**, **Auth.Directory**
  - التحليلات → **Dev.Analytics**
  - الامتثال → **Compliance**
- **خطوط пунктية:** تدفق المعاملات (تعيين، فصل) عبر أكثر من فرع — justify Event-Driven.

هذا الإسقاط التنظيمي→تقني هو أساس تقسيم 14 Microservice.

### 3.2.1 الخدمات والمنافذ

**[جدول (3-1): الخدمات والمنافذ وقواعد البيانات]**

| # | الخدمة | Port | Database | DbContext |
|---|--------|------|----------|-----------|
| 1 | HR.Core | 5010 | hr_core_db | HRCoreDbContext |
| 2 | HR.Contracts | 5020 | hr_contracts_db | ContractsDbContext |
| 3 | HR.Health | 5030 | hr_health_db | HealthDbContext |
| 4 | HR.Movements | 5040 | hr_movements_db | MovementsDbContext |
| 5 | HR.Training | 5050 | hr_training_db | TrainingDbContext |
| 6 | Payroll | 5060 | payroll_db | PayrollDbContext |
| 7 | IDCard | 5070 | idcard_db | IDCardDbContext |
| 8 | Records.Archive | 5080 | archive_db | ArchiveDbContext |
| 9 | Admin.Logistics | 5090 | admin_db | AdminLogisticsDbContext |
| 10 | IT.Infra | 5100 | it_infra_db | ITInfraDbContext |
| 11 | Dev.Analytics | 5110 | analytics_db | AnalyticsDbContext |
| 12 | Compliance | 5120 | compliance_db | ComplianceDbContext |
| 13 | Auth.Directory | 5130 | auth_directory_db | AuthDirectoryDbContext |
| 14 | File.Storage | 5140 | file_storage_db | FileStorageDbContext |
| — | Gateway | 5000 | gateway_audit_db | GatewayAuditDbContext |

### 3.2.2 مخطط Use Case

**[شكل (3-1): مخطط Use Case Diagram]**

يُمثِّل الشكل (3-1) — **Use Case Diagram** — التفاعل بين **Actors** و**Use Cases**:

**Actors:**
- `hr-admin`: صلاحيات شاملة
- `hr-core`, `hr-contracts`, `payroll`: أدوار فرعية
- `hr-employee`: ESS
- `citizen` (anonymous): بوابة التوظيف

**Use Cases رئيسية:** Manage Employee، Hire Wizard، Separation Wizard، Approve Leave، Process Payroll، Execute Transfer، Resolve Complaint، Issue ID Card، Archive Document، ESS Submit Leave.

العلاقات `<<include>>` تربط Hire Wizard بـ Manage Employee وIssue ID Card؛ Separation Wizard بـ Archive Document.

### 3.2.3 مخطط المكوّنات

**[شكل (3-2): مخطط Component Diagram]**

**Component Diagram** يُظهر:
- **Gateway Component** → YARP, OIDC, Audit
- **14 Service Components** → MVC + Domain + Infrastructure
- **Shared Components** → MessageContracts, SharedKernel, ServiceDefaults
- **Infrastructure Components** → PostgreSQL×15, RabbitMQ, Keycloak, File.Storage

### 3.2.4 مخطط النشر

**[شكل (3-3): مخطط Deployment Diagram (Docker Compose)]**

**Deployment Diagram** على Host واحد (dev/staging):
- Container `postgresql` — 15 database
- Container `rabbitmq`
- Container `keycloak`
- 15 Container للخدمات + Gateway (أو `dotnet run` محلياً)

---

## 3.3 تحليل قاعدة البيانات

### 3.3.1 المبدأ المعماري

**Database-per-Service** — لا FK cross-database. التكامل عبر `EmployeeId`, `OrgUnitId` Guid references وأحداث MassTransit [4][13].

**[شكل (3-4): ER Diagram — HR.Core]**

**Entity-Relationship Diagram** لـ **HR.Core** يُظهر **Employee** كـ hub: علاقات Cascade للملفات الفرعية، OrgUnit شجري (ParentId self-FK)، ApprovalRequest/Step، Shifts، Recruitment، Succession.

**[شكل (3-5): ER Diagram — HR.Contracts]**

يركز على **Contract** ← **ContractType**، **ServiceTransaction** ← **HireChecklist/SeparationChecklist**، Leave/Advance entities.

**[شكل (3-6): ER Diagram — Payroll]**

**PayrollRun** ← **SalaryLine** ← **SalaryLineComponent**، **EmployeePayProfile** ← **PayGrade** ← **SalaryScale**.

**[شكل (3-7): ER Diagram — العلاقات المنطقية بين الخدمات]**

**Logical Cross-Service ER** — **EmployeeId** hub بدون FK físico: HR.Core (master) → Contracts, Payroll, Health, IDCard (references only)؛ الأسهم المنقطة = events.

### 3.3.2 جداول HRCoreDbContext (32 جدول)

**Employee** — PK: Id. FKs: PrimaryOrgUnitId→OrgUnit. UK: NationalId — السجل المرجعي الرئيسي للموظف يضم الاسم والرقم الوطني والحالة الوظيفية والكادر.

**OrgUnit** — PK: Id. FKs: ParentId→OrgUnit, HeadEmployeeId→Employee. UK: Code — وحدة تنظيمية في الهيكل الشجري للمؤسسة.

**EmployeeProfile** — PK: Id. FKs: EmployeeId→Employee (Cascade) — تفاصيل شخصية إضافية: الجنسية، محل الولادة، العنوان.

**EmploymentRecord** — PK: Id. FKs: EmployeeId→Employee, OrgUnitId→OrgUnit — سجل وظيفي تاريخي للمنصب والفترة.

**EmployeeDocument** — PK: Id. FKs: EmployeeId→Employee — وثائق مرفقة (هوية، شهادات) مع رابط تخزين.

**Qualification** — PK: Id. FKs: EmployeeId→Employee — مؤهلات علمية ومهنية للموظف.

**ContactInfo** — PK: Id. FKs: EmployeeId→Employee — وسائل اتصال (هاتف، بريد) مع تمييز أساسي.

**EmploymentStatusHistory** — PK: Id. FKs: EmployeeId→Employee, EmploymentRecordId→EmploymentRecord — سجل تغيّرات الحالة (Active, OnLeave, Separated).

**PerformanceEvaluation** — PK: Id. FKs: EmployeeId→Employee — تقييم أداء سنوي بالدرجة والحالة.

**ApprovedCV** — PK: Id. FKs: EmployeeId→Employee — سيرة ذاتية معتمدة مرتبطة بالملف.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — سجل idempotency لاستهلاك أحداث التكامل.

**WorkforcePlan** — PK: Id. FKs: OrgUnitId→OrgUnit — خطة قوى عاملة سنوية للوحدة التنظيمية.

**HrStatisticReport** — PK: Id. FKs: OrgUnitId→OrgUnit — تقرير إحصائي HR لفترة محددة.

**DailyAttendanceRecord** — PK: Id. FKs: EmployeeId→Employee. UK: (EmployeeId, AttendanceDate) — سجل حضور يومي.

**FieldInspectionVisit** — PK: Id. — زيارة تفتيش ميداني للالتزام بالدوام.

**DiwanCorrespondence** — PK: Id. FKs: RelatedEmployeeId→Employee — مكاتبة ديوان مرتبطة بموظف أو معاملة.

**RecruitmentApplication** — PK: Id. FKs: HiredEmployeeId→Employee — طلب توظيف داخلي يُربط بالموظف بعد التعيين.

**ApprovalRequest** — PK: Id. Index: (EntityType, EntityId), Status — طلب موافقة generic متعدد الخطوات.

**ApprovalStep** — PK: Id. FKs: ApprovalRequestId→ApprovalRequest. UK: (ApprovalRequestId, Level) — خطوة موافقة بمستوى وتوقيع.

**Shift** — PK: Id. UK: Code — تعريف وردية (صباحي، مسائي).

**ShiftRoster** — PK: Id. FKs: OrgUnitId→OrgUnit. Index: (OrgUnitId, PeriodStart) — جدول مناوبات لفترة.

**ShiftAssignment** — PK: Id. FKs: ShiftRosterId, ShiftId, EmployeeId. Index: (EmployeeId, WorkDate) — إسناد موظف لوردية بتاريخ.

**CareerPath** — PK: Id. — مسار مهني تنظيمي.

**CareerPathStep** — PK: Id. FKs: CareerPathId→CareerPath — مرحلة في المسار الوظيفي.

**KeyPosition** — PK: Id. FKs: OrgUnitId→OrgUnit — منصب حرج يتطلب تعاقباً.

**SuccessionPlan** — PK: Id. FKs: KeyPositionId→KeyPosition — خطة تعاقب للمنصب.

**SuccessorCandidate** — PK: Id. FKs: SuccessionPlanId, EmployeeId — مرشح خليفة محتمل.

**InternalVacancy** — PK: Id. FKs: OrgUnitId→OrgUnit — شاغر داخلي للتنقل.

**InternalApplication** — PK: Id. FKs: InternalVacancyId, EmployeeId. UK: (InternalVacancyId, EmployeeId) — طلب موظف للشاغر.

**PublicJobPosting** — PK: Id. — إعلان وظيفة للبوابة العامة `/careers`.

**CitizenApplication** — PK: Id. FKs: PublicJobPostingId. UK: ReferenceNumber — طلب مواطن للوظيفة العامة.

**EntityAuditLog** — PK: Id. — سجل تدقيق تغييرات الكيانات في HR.Core.

### 3.3.3 جداول ContractsDbContext (20 جدول)

**ContractType** — PK: Id. UK: Code — نوع عقد (دائم، مؤقت) بالأسماء العربية والإنجليزية.

**Contract** — PK: Id. FKs: ContractTypeId→ContractType. Ref: EmployeeId — عقد موظف بحالة (Draft, Active, Terminated).

**ContractSignature** — PK: Id. FKs: ContractId→Contract — توقيعات العقد بأنواع مختلفة.

**ProbationRecord** — PK: Id. FKs: ContractId→Contract. Ref: EmployeeId — فترة اختبار ونتيجتها.

**SeparationRecord** — PK: Id. FKs: ContractId→Contract. Ref: EmployeeId — سجل فصل (استقالة، تقاعد، فصل).

**LeaveRequest** — PK: Id. Ref: EmployeeId — طلب إجازة بنوع ومدة وحالة موافقة.

**AdvanceRequest** — PK: Id. Ref: EmployeeId — طلب سلفة مالية.

**AdministrativeDecision** — PK: Id. — قرار إداري مرتبط بالخدمة.

**CommencementRecord** — PK: Id. FKs: ContractId→Contract — سجل مباشرة العمل بعد التعيين.

**ServiceTransaction** — PK: Id. — معاملة خدمة موحّدة (تعيين، إجازة، فصل) بمرجع وحالة.

**ServiceTransactionStateHistory** — PK: Id. FKs: TransactionId→ServiceTransaction — تاريخ انتقالات حالة المعاملة.

**LeaveBalance** — PK: Id. Ref: EmployeeId. UK: (EmployeeId, LeaveType, Year) — رصيد إجازات سنوي.

**LeavePolicy** — PK: Id. UK: LeaveType — سياسة إجازات (أيام مستحقة).

**AdvanceInstallmentPlan** — PK: Id. FKs: AdvanceRequestId→AdvanceRequest — خطة أقساط السلفة.

**SeparationChecklist** — PK: Id. FKs: SeparationRecordId. UK: (SeparationRecordId, Step) — خطوة قائمة تحقق الفصل.

**HireChecklist** — PK: Id. FKs: ServiceTransactionId. UK: (ServiceTransactionId, Step) — خطوة قائمة تحقق التعيين (P1.1–P7.1).

**OrgUnitSnapshot** — PK: Id. — لقطة denormalized للهيكل التنظيمي وقت المعاملة.

**EntityAuditLog** — PK: Id. — تدقيق تغييرات HR.Contracts.

**OutboxMessage** — PK: SequenceNumber. — رسالة Outbox MassTransit pending للنشر.

**OutboxState** — PK: OutboxId. — حالة Outbox MassTransit.

### 3.3.4 جداول HealthDbContext (14 جدول)

**MedicalRecord** — PK: Id. Ref: EmployeeId — ملف طبي أساسي للموظف.

**MedicalCommitteeReport** — PK: Id. FKs: MedicalRecordId→MedicalRecord — تقرير لجنة طبية.

**PeriodicMedicalCheck** — PK: Id. Ref: EmployeeId — فحص طبي دوري.

**InsuranceCard** — PK: Id. FKs: InsuranceContractId→InsuranceContract. Ref: EmployeeId — بطاقة تأمين صحي.

**HealthStatusEntry** — PK: Id. Ref: EmployeeId — إدخال حالة صحية.

**MedicalRecommendation** — PK: Id. FKs: ReportId→MedicalCommitteeReport — توصية طبية (تُنشر `IMedicalRecommendationIssued`).

**HealthRetirementRecord** — PK: Id. Ref: EmployeeId — سجل تقاعد صحي.

**InsuranceContract** — PK: Id. — عقد تأمين جماعي.

**WorkplaceAccident** — PK: Id. Ref: EmployeeId. Index: AccidentDate — حادث عمل.

**SafetyInspection** — PK: Id. Index: InspectionDate — تفتيش سلامة مكان العمل.

**FirstAidTrainingRecord** — PK: Id. Ref: EmployeeId — تدريب إسعافات أولية.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency لـ `IEmployeeCreated`.

**EntityAuditLog** — PK: Id. — تدقيق HR.Health.

### 3.3.5 جداول MovementsDbContext (10 جداول)

**TransferRequest** — PK: Id. Ref: EmployeeId, FromOrgUnitId, ToOrgUnitId — طلب نقل بين وحدات.

**MovementNotice** — PK: Id. — إشعار حركة تنظيمية.

**DistributionPlan** — PK: Id. FKs: TransferRequestId→TransferRequest — خطة توزيع مرتبطة بنقل.

**MovementExecutionRecord** — PK: Id. FKs: TransferRequestId/SecondmentRequestId/LoanAssignmentRequestId — سجل تنفيذ حركة.

**SecondmentRequest** — PK: Id. Ref: EmployeeId — طلب انتداب مؤقت.

**LoanAssignmentRequest** — PK: Id. Ref: EmployeeId — طلب إعارة لجهة أخرى.

**TransferApproval** — PK: Id. FKs: TransferRequestId→TransferRequest — موافقة على النقل.

**TransferFinancialImpact** — PK: Id. FKs: TransferRequestId→TransferRequest — أثر مالي للنقل.

**OrgUnitSnapshot** — PK: Id. — لقطة هيكل تنظيمي.

**EntityAuditLog** — PK: Id. — تدقيق HR.Movements.

### 3.3.6 جداول TrainingDbContext (14 جدول)

**TrainingCourse** — PK: Id. FKs: ProviderId→TrainingProvider. UK: Code — دورة تدريبية.

**TrainingEnrollment** — PK: Id. FKs: CourseId→TrainingCourse. Ref: EmployeeId — تسجيل موظف بدورة.

**QualificationCode** — PK: Id. UK: Code — رمز مؤهل معتمد مرتبط بمتطلبات الترقية.

**TrainingResult** — PK: Id. FKs: EnrollmentId→TrainingEnrollment (1:1) — نتيجة الدورة.

**MissionFile** — PK: Id. Ref: EmployeeId — ملف مهمة/انتداب تدريبي.

**TrainingNomination** — PK: Id. FKs: CourseId. Ref: EmployeeId — ترشيح للتدريب.

**TrainingProvider** — PK: Id. — جهة تقديم التدريب.

**TrainingPlan** — PK: Id. Index: (Year, OrgUnitId) — خطة تدريب سنوية.

**ELearningCourse** — PK: Id. UK: Code — دورة تعلم إلكتروني.

**CourseModule** — PK: Id. FKs: ELearningCourseId→ELearningCourse — وحدة في الدورة.

**Lesson** — PK: Id. FKs: CourseModuleId→CourseModule — درس داخل الوحدة.

**LearningEnrollment** — PK: Id. FKs: ELearningCourseId. Ref: EmployeeId. UK: (ELearningCourseId, EmployeeId) — تسجيل e-learning.

**LearningCertificate** — PK: Id. FKs: LearningEnrollmentId. UK: CertificateNo — شهادة إتمام.

**EntityAuditLog** — PK: Id. — تدقيق HR.Training.

### 3.3.7 جداول PayrollDbContext (25 جدول)

**PayrollRun** — PK: Id. — دورة رواتب (Draft→Post) لفترة.

**SalaryLine** — PK: Id. FKs: PayrollRunId→PayrollRun. Ref: EmployeeId — سطر راتب موظف.

**CompensationType** — PK: Id. UK: Code — نوع تعويض (أساسي، بدل).

**AllowanceRecord** — PK: Id. FKs: AllowanceTypeId→CompensationType. Ref: EmployeeId — بدل مستحق.

**DeductionRecord** — PK: Id. FKs: AppliedInRunId→PayrollRun. Ref: EmployeeId — خصم مطبّق.

**PayrollAdjustment** — PK: Id. FKs: PayrollRunId. Ref: EmployeeId — تعديل inbound من حدث.

**PayrollAuditRecord** — PK: Id. FKs: PayrollRunId→PayrollRun — سجل تدقيق دورة.

**PaymentBatch** — PK: Id. FKs: PayrollRunId→PayrollRun — دفعة دفع بنكية.

**SalaryScale** — PK: Id. UK: Code — سلم رواتب.

**PayGrade** — PK: Id. FKs: SalaryScaleId→SalaryScale. UK: (SalaryScaleId, GradeCode) — درجة في السلم.

**DeductionType** — PK: Id. UK: Code — نوع خصم.

**EmployeePayProfile** — PK: Id. FKs: PayGradeId→PayGrade. Ref: EmployeeId. Index: (EmployeeId, IsActive) — ملف أجور نشط.

**SalaryLineComponent** — PK: Id. FKs: SalaryLineId→SalaryLine — مكوّن (بدل/خصم) في السطر.

**EndOfServiceSettlement** — PK: Id. Ref: EmployeeId. Index: EmployeeId — تسوية نهاية خدمة.

**PayrollConfiguration** — PK: Id. UK: Code — إعدادات نظام رواتب (v1 operational).

**TaxBracket** — PK: Id. FKs: PayrollConfigurationId→PayrollConfiguration — شريحة ضريبة.

**OfficialHoliday** — PK: Id. FKs: PayrollConfigurationId — عطلة رسمية.

**EmployeePayProfileHistory** — PK: Id. Ref: EmployeeId. Index: EmployeeId — تاريخ تغيّرات ملف الأجور.

**PayrollRunApproval** — PK: Id. FKs: PayrollRunId→PayrollRun — موافقة على دورة.

**PayslipDelivery** — PK: Id. FKs: SalaryLineId→SalaryLine — تسليم كشف راتب.

**AbsenceDeduction** — PK: Id. Ref: EmployeeId — خصم غياب.

**OrgUnitSnapshot** — PK: Id. — لقطة هيكل للرواتب.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency.

**OutboxMessage** — PK: SequenceNumber. — Outbox MassTransit.

**OutboxState** — PK: OutboxId. — حالة Outbox.

**EntityAuditLog** — PK: Id. — تدقيق Payroll.

*(ملاحظة: PayrollDbContext يضم 26 كياناً مسجّلاً؛ جدول EntityAuditLog مُدرج ضمن العدد الإجمالي ~180 جدولاً في المنظومة.)*

### 3.3.8 جداول IDCardDbContext (8 جداول)

**CardDesign** — PK: Id. — قالب تصميم بطاقة الموظف.

**IssuedCard** — PK: Id. FKs: CardDesignId→CardDesign, DeliveryRecordId→CardDeliveryRecord. Ref: EmployeeId — بطاقة صادرة.

**CardRequest** — PK: Id. Ref: EmployeeId — طلب إصدار/تجديد بطاقة.

**CardDeliveryRecord** — PK: Id. — سجل تسليم البطاقة.

**OfficialSealRecord** — PK: Id. — سجل ختم رسمي.

**AccessPermission** — PK: Id. FKs: IssuedCardId→IssuedCard — صلاحية دخول مرتبطة بالبطاقة.

**CardPermissionSet** — PK: Id. — مجموعة صلاحيات بطاقة.

**EntityAuditLog** — PK: Id. — تدقيق IDCard.

### 3.3.9 جداول ArchiveDbContext (8 جداول)

**DocumentClassification** — PK: Id. UK: Code — تصنيف وثائق أرشيفية.

**ArchiveItem** — PK: Id. FKs: ClassificationId→DocumentClassification. UK: ReferenceNumber — عنصر أرشيف رئيسي.

**PhysicalFileRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — ملف فيزيائي (رف، صندوق).

**DigitalFileRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — ملف رقمي (مرجع File.Storage).

**LoanRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — استعارة وثيقة.

**ArchiveIndexEntry** — PK: Id. FKs: ArchiveItemId→ArchiveItem — فهرس بحث.

**DisposalRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — إتلاف/نقل نهائي.

**EntityAuditLog** — PK: Id. — تدقيق Records.Archive.

### 3.3.10 جداول AdminLogisticsDbContext (9 جداول)

**Asset** — PK: Id. UK: TagNumber — أصل/logistics (جهاز، مركبة).

**AssetAssignment** — PK: Id. FKs: AssetId→Asset. Ref: EmployeeId — إسناد أصل لموظف.

**SupplyOrder** — PK: Id. — طلب توريد.

**InventoryItem** — PK: Id. UK: Sku — صنف مخزون.

**MaintenanceRecord** — PK: Id. FKs: AssetId→Asset — صيانة أصل.

**Vendor** — PK: Id. — مورّد.

**ReceiptRecord** — PK: Id. FKs: SupplyOrderId→SupplyOrder — استلام توريد.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency.

**EntityAuditLog** — PK: Id. — تدقيق Admin.Logistics.

### 3.3.11 جداول ITInfraDbContext (9 جداول)

**ServerDevice** — PK: Id. UK: Hostname — خادم بنية تحتية.

**UserDevice** — PK: Id. Ref: EmployeeId — جهاز مستخدم.

**BackupJob** — PK: Id. — مهمة نسخ احتياطي.

**SecurityIncident** — PK: Id. — حادث أمني.

**SystemPermissionRecord** — PK: Id. Ref: EmployeeId — صلاحية نظام IT.

**ITSupportTicket** — PK: Id. Ref: EmployeeId — تذكرة دعم.

**EnvironmentConfiguration** — PK: Id. — إعداد بيئة.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency.

**EntityAuditLog** — PK: Id. — تدقيق IT.Infra.

### 3.3.12 جداول AnalyticsDbContext (8 جداول)

**Report** — PK: Id. — تقرير تحليلي.

**Dashboard** — PK: Id. — لوحة KPI.

**ETLJob** — PK: Id. — مهمة ETL.

**ReadModel** — PK: Id. — نموذج قراءة denormalized.

**KPI** — PK: Id. UK: Code — مؤشر أداء.

**AnalyticsEvent** — PK: Id. — حدث تحليلي مستهلك من التكامل.

**DataRefreshSchedule** — PK: Id. FKs: TargetReadModelId→ReadModel — جدولة تحديث.

**EntityAuditLog** — PK: Id. — تدقيق Dev.Analytics.

### 3.3.13 جداول ComplianceDbContext (8 جداول)

**Complaint** — PK: Id. Ref: EmployeeId — شكوى موظف/مواطن.

**ComplaintFollowUp** — PK: Id. FKs: ComplaintId→Complaint — متابعة شكوى.

**AuditFinding** — PK: Id. — نتيجة تدقيق.

**ComplianceRecommendation** — PK: Id. FKs: FindingId, ComplaintId — توصية امتثال.

**LegalCase** — PK: Id. — قضية قانونية.

**LegalReferral** — PK: Id. FKs: ComplaintId→Complaint — إحالة قانونية.

**DisciplinaryAction** — PK: Id. FKs: ComplaintId. Ref: EmployeeId — إجراء تأديبي (ينشر `IDisciplinaryActionTaken`).

**EntityAuditLog** — PK: Id. — تدقيق Compliance.

### 3.3.14 جداول AuthDirectoryDbContext (9 جداول)

**AuthUser** — PK: Id. UK: Username, Email. Ref: EmployeeId, KeycloakUserId — مستخدم مرتبط بـ Keycloak.

**Role** — PK: Id. — دور نظام.

**UserRole** — PK: Id. FKs: AuthUserId, RoleId. UK: (AuthUserId, RoleId) — ربط مستخدم–دور.

**AuthToken** — PK: Id. FKs: UserId→AuthUser — رمز/session.

**DirectoryEntry** — PK: Id. Ref: EmployeeId — إدخال دليل.

**DirectorySyncRecord** — PK: Id. — سجل مزامنة Keycloak.

**PermissionPolicy** — PK: Id. — سياسة صلاحية.

**AuthAuditLog** — PK: Id. — تدقيق مصادقة.

**EntityAuditLog** — PK: Id. — تدقيق Auth.Directory.

### 3.3.15 جداول FileStorageDbContext (4 جداول)

**Files** — PK: Id. UK: (Bucket, ObjectKey). Index: Status, UploadedAt — ملف مخزّن binary/metadata.

**FileReferences** — PK: Id. FKs: FileId→Files. UK: (FileId, HolderKey) — مرجع من خدمة holder.

**FileAccessAuditLog** — PK: Id. FKs: FileId. Index: OccurredAt — تدقيق وصول ملف.

**EntityAuditLog** — PK: Id. — تدقيق File.Storage.

### 3.3.16 جداول GatewayAuditDbContext (1 جدول)

**gateway_audit_entries** — PK: Id. Index: OccurredAtUtc, CorrelationId, StatusCode — سجل طلبات Gateway (Method, Path, Decision, UserSub).

---

## 3.4 تحليل المستخدمين والصلاحيات

**[شكل (3-14): مخطط RBAC — الأدوار والسياسات]**

**RBAC Diagram** يُظهر:
- **Keycloak Roles** (19 دوراً) → **ASP.NET Policies** (20 policy)
- **Gateway** يفحص Module policy
- **Services** تطبّق fine-grained (`Payroll.Process`, `HrContracts.HireExecute`)

**[جدول (3-5): أدوار Keycloak وصلاحياتها]**

| الدور | Module Policy | Fine-grained |
|-------|---------------|--------------|
| hr-admin | All | Delete everywhere |
| hr-core | Module.HrCore | Write, Approve |
| hr-contracts | Module.HrContracts | Approve, HireExecute |
| hr-management-approver | — | HrManagement.Approve |
| payroll | Module.Payroll | Process, Approve, Post |
| hr-employee | Module.Ess | ESS only |
| branch-manager | Module.HrCore | ReadOnly + org scope |

**[جدول (3-6): سياسات Authorization]**

20 policy في `HrManagementAuthorizationExtensions`: `Module.*` (14)، `HrCore.*` (4)، `HrContracts.*` (2)، `HrManagement.Approve`, `Payroll.*` (3), `Gateway.Dashboard`.

**Enforcement layers:**
1. Gateway: `[Authorize]` عام
2. `GatewayPathAuthorizationMiddleware`
3. `HrModuleAuthorizationFilter`
4. `[Authorize(Policy)]` على Actions
5. `HrCadreVisibility` / `HrOrgScopeVisibility`

---

## 3.5 تحليل العمليات و Workflow

### 3.5.1 التعيين (Hire Journey)

**[شكل (3-8): Activity Diagram — مسار التعيين (Hire)]**

**Activity Diagram** لمسار **Hire**:
1. `RecruitmentApplication` (Submitted→Hire)
2. نشر `ICandidateAccepted` + `IEmployeeCreated`
3. **HR.Contracts:** Appointment + `HireChecklist` (7 steps)
4. **HireWizard:** Step2 checklist → Step3 mgmt approve → Step4 complete
5. `CommencementRecord` → Activate → `IContractActivated`
6. **Payroll:** `EmployeePayProfile`
7. **IDCard:** `ICardIssued`
8. `IHireCaseCompleted` → **Archive** + **Diwan**

Checklist: P1.1 CandidateReceived, P1.2 ContractActivated, P2.1 ProfileCreated, P6.1 PayrollLinked, P5.1 CareerApproved, P8.1 IdCardIssued, P7.1 ArchiveCompleted.

### 3.5.2 الفصل (Separation)

**[شكل (3-9): Activity Diagram — مسار الفصل (Separation)]**

**SeparationWizard** Step1–4 → `ISeparationCompleted` → 7 consumers (Payroll, Archive, IDCard, Admin, Auth, IT, Analytics).

### 3.5.3 دورة الرواتب

**[شكل (3-10): Activity Diagram — دورة الرواتب]**

Draft → Calculate → Review → Approve → Post → `IPayrollProcessed` → Dev.Analytics.

### 3.5.4 مخططات Sequence و Flowchart

**[شكل (3-11): Sequence Diagram — IEmployeeCreated]**

**Sequence Diagram:** HR.Core publishes → parallel consumers (Contracts, Health, IDCard, Auth, Payroll, IT, Analytics) — idempotent.

**[شكل (3-12): Sequence Diagram — تسجيل الدخول عبر Gateway]**

Browser → Gateway → Keycloak OIDC → Cookie → YARP → Module JwtBearer.

**[شكل (3-13): Flowchart — موافقة الإجازة]**

Create LeaveRequest → `ApproveLeaveRequestCommand` → `ILeaveApproved` → Payroll adjustment + HR.Core OnLeave + Archive.

---

## 3.6 شرح الخدمات (Modules)

**[شكل (3-15): Class Diagram — BaseEntity و Shared Kernel]**

**Class Diagram** يُظهر:
- `BaseEntity` (Id, TenantId, CreatedAt, UpdatedAt)
- `AuditableEntity` (+ CreatedBy, UpdatedBy)
- `EntityAuditLog`, `IntegrationMessageReceipt`
- Domain entities ترث BaseEntity/AuditableEntity

### 3.6.1 HR.Core (Port 5010)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | Master data للموظفين والهيكل التنظيمي |
| **المسؤوليات** | CRUD موظفين، OrgUnit tree، حضور، استقطاب، موافقات، ديوان، ESS، careers عامة، shifts، succession، talent marketplace |
| **المدخلات** | نماذج MVC، استيراد CSV، أحداث inbound |
| **المخرجات** | Views، Lookup API، أحداث تكامل |
| **التبعيات** | PostgreSQL hr_core_db، RabbitMQ، File.Storage (مراجع) |
| **ينشر** | `IEmployeeCreated`, `IEmployeeStatusChanged`, `ICandidateAccepted`, `ITransactionRegisteredInDiwan`, `IOrgUnitChanged` |
| **يستهلك** | `ILeaveApproved`, `IAdvanceApproved`, `ITransferExecuted`, `ITrainingCompleted`, `IHireCaseCompleted`, `ISeparationCompleted`, `IMedicalRecommendationIssued` |
| **Controllers** | EmployeesController, OrgUnitsController, EmployeeProfilesController, EmploymentRecordsController, DocumentsController, QualificationsController, ContactsController, EvaluationsController, ApprovedCVsController, StatusHistoryController, ApprovalsController, RecruitmentApplicationsController, PublicJobPostingsController, PublicCareersController, CitizenApplications (via PublicCareers), EssPortalController, EssTalentController, TalentMarketplaceController, ShiftsController, RostersController, CareerPathsController, KeyPositionsController, SuccessionPlansController, WorkforcePlansController, HrStatisticReportsController, DailyAttendanceRecordsController, FieldInspectionVisitsController, DiwanCorrespondencesController, DashboardController, ReportsController, DataImportController, LookupApiController, StatsApiController, HomeController |
| **Database** | hr_core_db / HRCoreDbContext (32 جدول) |

### 3.6.2 HR.Contracts (Port 5020)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | العقود والخدمة الوظيفية (إجازات، سلف، فصل، تعيين) |
| **المسؤوليات** | Contracts، Leave/Advance، Separation، HireWizard، ServiceTransactions، Commencement |
| **المدخلات** | نماذج HR، أحداث Employee/Candidate/Medical/Legal |
| **المخرجات** | Wizards، قرارات موافقة، أحداث downstream |
| **التبعيات** | hr_contracts_db، Outbox، HR.Core references |
| **ينشر** | `IContractActivated`, `ILeaveApproved`, `IAdvanceApproved`, `ISeparationCompleted`, `IHireCaseCompleted`, `ISeparationStepCompleted`, `ICommencementRecorded`, `IConfirmationApproved`, `IJobUpdateApplied`, `ITransactionArchived` |
| **يستهلك** | `IEmployeeCreated`, `ICandidateAccepted`, `ICardIssued`, `ILegalJudgmentIssued`, `IMedicalDecisionIssued`, `ITransactionRegisteredInDiwan`, inbound portal events |
| **Controllers** | ContractsController, ContractTypesController, ContractSignaturesController, LeaveRequestsController, LeaveBalancesController, LeavePoliciesController, AdvanceRequestsController, SeparationRecordsController, SeparationWizardController, HireWizardController, CommencementRecordsController, ProbationRecordsController, ServiceTransactionsController, AdministrativeDecisionsController, DashboardController, ExportController, HomeController |
| **Database** | hr_contracts_db / ContractsDbContext (20 جدول) |

### 3.6.3 HR.Health (Port 5030)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | الشؤون الصحية والسلامة المهنية |
| **المسؤوليات** | Medical records، insurance، accidents، recommendations |
| **المدخلات** | `IEmployeeCreated`، نماذج MVC |
| **المخرجات** | `IMedicalRecommendationIssued`, `IMedicalDecisionIssued` |
| **التبعيات** | hr_health_db، IntegrationMessageReceipt |
| **ينشر** | `IMedicalRecommendationIssued`, `IMedicalDecisionIssued` |
| **يستهلك** | `IEmployeeCreated` |
| **Controllers** | MedicalRecordsController, MedicalCommitteeReportsController, MedicalRecommendationsController, PeriodicMedicalChecksController, InsuranceCardsController, InsuranceContractsController, HealthRetirementRecordsController, HealthStatusEntriesController, WorkplaceSafetyController, HomeController |
| **Database** | hr_health_db / HealthDbContext (14 جدول) |

### 3.6.4 HR.Movements (Port 5040)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | التنقلات والانتدابات والإعارات |
| **المسؤوليات** | Transfer، Secondment، Loan assignment، distribution plans |
| **المدخلات** | طلبات نقل، `IOrgUnitChanged` |
| **المخرجات** | `ITransferExecuted`, `ISecondmentExecuted`, `ILoanAssignmentExecuted` |
| **التبعيات** | hr_movements_db |
| **ينشر** | `ITransferExecuted`, `ISecondmentExecuted`, `ILoanAssignmentExecuted` |
| **يستهلك** | `IOrgUnitChanged` |
| **Controllers** | TransferRequestsController, MovementNoticesController, DistributionPlansController, SecondmentRequestsController, LoanAssignmentRequestsController, MovementExecutionsController, TransferFinancialImpactsController, DashboardController, HomeController |
| **Database** | hr_movements_db / MovementsDbContext (11 جدول) |

### 3.6.5 HR.Training (Port 5050)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | التدريب والتعلم الإلكتروني والمهام |
| **المسؤوليات** | Courses، enrollments، e-learning، missions |
| **المدخلات** | Plans، nominations، ESS learning |
| **المخرجات** | `ITrainingCompleted`, `IMissionApproved` |
| **التبعيات** | hr_training_db |
| **ينشر** | `ITrainingCompleted`, `IMissionApproved` |
| **يستهلك** | — (v1 primarily publisher) |
| **Controllers** | TrainingCoursesController, TrainingEnrollmentsController, TrainingPlansController, QualificationCodesController, TrainingProvidersController, TrainingNominationsController, TrainingResultsController, MissionFilesController, ELearningCoursesController, EssLearningController, HomeController |
| **Database** | hr_training_db / TrainingDbContext (14 جدول) |

### 3.6.6 Payroll (Port 5060)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | الرواتب والتعويضات (دورة تشغيلية v1) |
| **المسؤوليات** | PayrollRun lifecycle، pay profiles، adjustments، EOS |
| **المدخلات** | 9+ event types inbound |
| **المخرجات** | `IPayrollProcessed`، payslips |
| **التبعيات** | payroll_db، Outbox، IntegrationMessageReceipt |
| **ينشر** | `IPayrollProcessed` |
| **يستهلك** | `IContractActivated`, `ILeaveApproved`, `IAdvanceApproved`, `ITransferExecuted`, `ISecondmentExecuted`, `ITrainingCompleted`, `IMissionApproved`, `IDisciplinaryActionTaken`, `ISeparationCompleted`, `IEmployeeCreated`, `IAttendancePeriodClosed`, `IOrgUnitChanged` |
| **Controllers** | PayrollRunsController, EmployeePayProfilesController, SalaryScalesController, PayGradesController, CompensationTypesController, DeductionTypesController, PayrollConfigurationsController, EndOfServiceController, PayrollReportsController, AllowanceRecordsController, DeductionRecordsController, PayrollAdjustmentsController, PayrollAuditRecordsController, PaymentBatchesController, PayrollApiController, EssController, DashboardController, HomeController |
| **Database** | payroll_db / PayrollDbContext (25+ جدول) |

### 3.6.7 IDCard (Port 5070)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | إصدار بطاقات الهوية والصلاحيات |
| **المسؤوليات** | Designs، requests، issued cards، delivery |
| **المدخلات** | `IEmployeeCreated`، طلبات يدوية |
| **المخرجات** | `ICardIssued` |
| **التبعيات** | idcard_db، File.Storage للأختام |
| **ينشر** | `ICardIssued` |
| **يستهلك** | `IEmployeeCreated`, `ISeparationCompleted` |
| **Controllers** | CardDesignsController, CardRequestsController, IssuedCardsController, AccessPermissionsController, OfficialSealRecordsController, CardPermissionSetsController, HomeController |
| **Database** | idcard_db / IDCardDbContext (8 جداول) |

### 3.6.8 Records.Archive (Port 5080)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | الأرشفة المؤسسية للوثائق |
| **المسؤوليات** | Archive items، physical/digital files، loans |
| **المدخلات** | Hire/Separation/Leave/Complaint events |
| **المخرجات** | Archive references |
| **التبعيات** | archive_db، File.Storage |
| **ينشر** | — |
| **يستهلك** | `IHireCaseCompleted`, `ISeparationCompleted`, `ILeaveApproved`, `IAdvanceApproved`, `IComplaintResolved`, `IMedicalRecommendationIssued`, `IMedicalDecisionIssued` |
| **Controllers** | ArchiveItemsController, DocumentClassificationsController, PhysicalFileRecordsController, DigitalFileRecordsController, LoanRecordsController, ArchiveIndexEntriesController, HomeController |
| **Database** | archive_db / ArchiveDbContext (8 جداول) |

### 3.6.9 Admin.Logistics (Port 5090)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | اللوجستيات والأصول والمخزون |
| **المسؤوليات** | Assets، inventory، supply orders |
| **المدخلات** | `ISeparationCompleted`, `ITransferExecuted` |
| **المخرجات** | Asset updates |
| **التبعيات** | admin_db، IntegrationMessageReceipt |
| **ينشر** | — |
| **يستهلك** | `ISeparationCompleted`, `ITransferExecuted` |
| **Controllers** | AssetsController, AssetAssignmentsController, InventoryItemsController, SupplyOrdersController, VendorsController, MaintenanceRecordsController, ReceiptRecordsController, HomeController |
| **Database** | admin_db / AdminLogisticsDbContext (9 جداول) |

### 3.6.10 IT.Infra (Port 5100)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | البنية التحتية IT والدعم |
| **المسؤوليات** | Servers، devices، backups، incidents، tickets |
| **المدخلات** | `IEmployeeCreated`, `ISeparationCompleted` |
| **المخرجات** | Provisioning/deprovisioning markers |
| **التبعيات** | it_infra_db |
| **ينشر** | — |
| **يستهلك** | `IEmployeeCreated`, `ISeparationCompleted` |
| **Controllers** | ServerDevicesController, UserDevicesController, BackupJobsController, SecurityIncidentsController, SystemPermissionRecordsController, ITSupportTicketsController, EnvironmentConfigurationsController, HomeController |
| **Database** | it_infra_db / ITInfraDbContext (9 جداول) |

### 3.6.11 Dev.Analytics (Port 5110)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | التحليلات وKPIs وETL |
| **المسؤوليات** | Dashboards، reports، event ingestion |
| **المدخلات** | Integration events |
| **المخرجات** | Analytics API، workforce planning |
| **التبعيات** | analytics_db |
| **ينشر** | — |
| **يستهلك** | `IEmployeeCreated`, `ISeparationCompleted`, `IPayrollProcessed`, `IComplaintResolved`, `ICardIssued` |
| **Controllers** | DashboardsController, ReportsController, KpisController, EtlJobsController, ReadModelsController, DataRefreshSchedulesController, AnalyticsEventsApiController, WorkforcePlanningController, HomeController |
| **Database** | analytics_db / AnalyticsDbContext (8 جداول) |

### 3.6.12 Compliance (Port 5120)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | الامتثال والشكاوى والتأديب |
| **المسؤوليات** | Complaints، legal cases، disciplinary actions |
| **المدخلات** | Medical recommendations، manual entry |
| **المخرجات** | `ILegalJudgmentIssued`, `IDisciplinaryActionTaken`, `IComplaintResolved` |
| **التبعيات** | compliance_db |
| **ينشر** | `ILegalJudgmentIssued`, `IDisciplinaryActionTaken`, `IComplaintResolved` |
| **يستهلك** | `IMedicalRecommendationIssued` |
| **Controllers** | ComplaintsController, LegalCasesController, DisciplinaryActionsController, AuditFindingsController, LegalReferralsController, ComplianceRecommendationsController, DashboardController, ExportController, HomeController |
| **Database** | compliance_db / ComplianceDbContext (8 جداول) |

### 3.6.13 Auth.Directory (Port 5130)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | دليل المستخدمين ومزامنة Keycloak |
| **المسؤوليات** | AuthUser، roles، directory sync |
| **المدخلات** | Employee lifecycle events |
| **المخرجات** | Keycloak provisioning |
| **التبعيات** | auth_directory_db، Keycloak API |
| **ينشر** | — |
| **يستهلك** | `IEmployeeCreated`, `IEmployeeStatusChanged`, `ISeparationCompleted`, `ITransferExecuted`, `ISecondmentExecuted` |
| **Controllers** | AuthUsersController, RolesController, DirectoryEntriesController, DirectorySyncRecordsController, PermissionPoliciesController, DirectoryInternalSyncController, StatsApiController, HomeController |
| **Database** | auth_directory_db / AuthDirectoryDbContext (9 جداول) |

### 3.6.14 File.Storage (Port 5140)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | تخزين ملفات مركزي |
| **المسؤوليات** | Upload/download، references، access audit |
| **المدخلات** | HTTP multipart، holder references |
| **المخرجات** | File IDs، signed URLs |
| **التبعيات** | file_storage_db، filesystem/blob |
| **ينشر** | — |
| **يستهلك** | — |
| **Controllers** | FilesApiController |
| **Database** | file_storage_db / FileStorageDbContext (4 جداول) |

### 3.6.15 Gateway (Port 5000)

| البند | التفاصيل |
|-------|----------|
| **الهدف** | نقطة دخول موحّدة ومصادقة |
| **المسؤوليات** | YARP reverse proxy، OIDC، RBAC middleware، audit |
| **المدخلات** | HTTP من المتصفح |
| **المخرجات** | Proxied responses، dashboard |
| **التبعيات** | Keycloak، 14 service clusters، gateway_audit_db |
| **ينشر** | — |
| **يستهلك** | — |
| **Controllers** | HomeController (Dashboard, Operations), AccountController (Login/Logout OIDC) |
| **Database** | gateway_audit_db / GatewayAuditDbContext (1 جدول) |

---

## 3.7 شرح الواجهات البرمجية (APIs)

**[جدول (3-8): REST APIs الصريحة]**

| API | Method | Auth | الوصف |
|-----|--------|------|-------|
| `/api/files` | POST/GET/DELETE | Authenticated | File.Storage CRUD |
| `/api/hr-core/api/lookup/employees` | GET | Anonymous | بحث موظفين للتكامل |
| `/api/payroll/api/payroll/runs/{id}/calculate` | POST | Payroll.Process | حساب دورة رواتب |
| `/api/dev-analytics/api/analytics/events` | GET | Module.DevAnalytics | أحداث تحليلية |
| `/careers/apply` | POST | Anonymous | تقديم طلب توظيف عام |
| `/api/auth-directory/internal/sync` | POST | Internal | مزامنة دليل |
| `/api/hr-core/stats` | GET | Module.HrCore | إحصائيات HR.Core |

**Swagger:** متاح في Development لـ HR.Core، HR.Contracts، Payroll.

**نمط التوجيه عبر Gateway:** `/ui/{module-slug}/{controller}/{action}` — YARP يُزيل البادئة ويُمرّر JWT.

---

## 3.8 شرح واجهات المستخدم

### 3.8.1 Gateway Dashboard

**[شكل (3-16): واجهة لوحة البوابة الرئيسية]**

**Gateway Dashboard** (`/` — Port 5000) — **UI Mockup Description:**

- **Header:** شعار المؤسسة، اسم المستخدم من Keycloak، زر Logout.
- **Module Grid:** 14 بطاقة للخدمات (HR.Core، Contracts، Payroll...) — كل بطاقة رابط `/ui/{module}/`.
- **Health Panel:** حالة كل خدمة (green/red) من health checks.
- **KPI Strip:** عدد موظفين نشطين، معاملات معلّقة، آخر PayrollRun.
- **Operations Link:** `/Home/Operations` — Correlation ID tracing.
- **RTL Layout:** AdminLTE sidebar على اليمين، خط Qomra Arabic.

### 3.8.2 HireWizard

**[شكل (3-17): واجهة معالج التعيين HireWizard]**

**HireWizard** (`/ui/hr-contracts/HireWizard`) — **4 Steps UI:**

1. **Step 1 — Select Transaction:** اختيار `ServiceTransaction` نوع Appointment.
2. **Step 2 — Checklist:** 7 checkboxes (P1.1–P7.1) مع حالة auto من events.
3. **Step 3 — Management Approval:** موافقة `HrManagement.Approve`.
4. **Step 4 — Complete:** `PublishHireCase` → `IHireCaseCompleted`.

Progress bar أفقي، `_HrAlerts` للنجاح/الخطأ، breadcrumb عربي.

### 3.8.3 ESS Portal

**ESS** (`/ui/hr-core/EssPortal`, `/ui/hr-contracts/...`, `/ui/payroll/Ess`):

- تقديم **LeaveRequest** و**AdvanceRequest**
- عرض **Payslip** (قراءة)
- **Attendance** self-view
- **ELearning** (`EssLearningController`)
- Policy: `Module.Ess` — موظف يرى بياناته فقط

### 3.8.4 Public Careers

**`/careers`** (anonymous):

- قائمة `PublicJobPosting`
- نموذج `CitizenApplication` مع `ReferenceNumber`
- بدون مصادقة — CSRF محمي

### 3.8.5 أنماط UI المشتركة

- **AdminLTE 3 RTL** — sidebar، navbar، boxes
- **`_Layout.cshtml`** per service مع module color
- **`_HrSharedFooter`**, **`_HrAlerts`** (TempData Success/Error)
- **CRUD Pattern:** Index → Create → Edit → Details (~100+ شاشة)
- **~114 Controller** عبر المنظومة
- **SeparationWizard** — 4 steps analog to Hire
- **Org Scope:** branch-manager يرى Index مفلتراً

### 3.8.6 الهوية البصرية

| العنصر | القيمة |
|--------|--------|
| Primary | #6b1f2a |
| Secondary | #428177 |
| Font | ITF Qomra Arabic |
| Direction | RTL |


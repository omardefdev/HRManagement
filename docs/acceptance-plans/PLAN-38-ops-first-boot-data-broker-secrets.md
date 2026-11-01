# PLAN-38 — الإقلاع الأول للبيانات والوسيط والأسرار (OPS-B)

**Scope:** OPS — [دراسة الفجوات](../PRG_002_OPERATIONS_GAP_STUDY.md): G-OPS-02، G-OPS-13، والجزء الخاص بالوسيط وقاعدة البيانات من G-OPS-10 (اسم عقدة RabbitMQ وvolume له، مجمعات الاتصال)  
**Status:** Implemented — 2026-11-01 (التقرير: `reports/plan38-final-report.md`)  
**Priority:** P0  
**Depends on:** PLAN-36 (ملف الخادم الواحد، وحارس الإعداد، وحزمة النشر)، PLAN-37 (الملف الشبيه بالإنتاج الذي يُبنى فوقه ملف القبول بأقل صلاحية، §6)  
**Related cards:** DB-001، SEC-004، IAM-004، OPS-002 (جزء الوسيط)، INT-003 (مهل الـ sagas)، PLAN-09 (AC-20)

## Goal

أن يُبنى أي خادم جديد من volumes فارغة **بلا خطوة يدوية**، ويعيد كل نشر لاحق مطابقة الحالة دون أثر جانبي:

- حاوية تهيئة لمرة واحدة (`hrm-bootstrap`) تنشئ قواعد البيانات والأدوار بكلمات مرور مستقلة، وتطبّق الترحيلات بدور `*_migrator`، وتنشئ مستخدمي RabbitMQ بأقل صلاحية، ثم تخرج بنجاح أو تُفشل النشر.
- RabbitMQ يحمل إضافة الرسائل المؤجلة التي تحتاجها مهل الـ sagas، ولا يفقد رسائله عند إعادة إنشاء حاويته.
- أسرار `.env` تُولَّد آلياً وتطابق كل `${VAR:?}` في ملفات الإنتاج. ولا تبقى قيمة وهمية في أي `appsettings.Production.json`.
- فشل البذور الإلزامية يوقف الإقلاع في Production بدل تحذير صامت.

بعد هذه الخطة يعمل الوسيط على الخادم الواحد بمستخدمي `mq_*` افتراضياً. أما قاعدة البيانات فتبقى في «وضع العرض» إلى أن تُغلق الموجة 2 (OPS-D) أثر RLS، والوضع الأقل صلاحية جاهز ومثبت خلف مفتاح (D-38-1).

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-38-1 وضع قاعدة البيانات على الخادم الواحد | `HRM_DB_POSTURE=demo` (التطبيقات بالحساب الإداري) حتى إغلاق OPS-D. الوضع `least-privilege` (أدوار `*_app`) متاح ومثبت في قبول هذه الخطة، ولا يصبح افتراضياً هنا | §1، §6 |
| D-38-2 وضع الوسيط | `HRM_MQ_POSTURE=least-privilege` افتراضياً بعد هذه الخطة؛ و`admin` للرجوع فقط | §2 |
| D-38-3 كلمات مرور الأدوار | كلمة مستقلة لكل دور `*_app` و`*_migrator` و`hrm_backup`، يولّدها `init-secrets` | §1، §3 |
| D-38-4 مجدول الرسائل المؤجلة | إضافة `rabbitmq_delayed_message_exchange` v3.13.0 في صورة RabbitMQ مبنية من المستودع | §2 |
| D-38-5 حفظ الأسرار على الخادم | `/opt/hrm/.env` بصلاحية 600 لمالك root، دون مدير أسرار | §3 |
| D-38-6 فشل البذور الإلزامية | يوقف الإقلاع في Production. مفتاح طوارئ `Seed:AllowMandatoryFailure=true` يسجّل `Critical` | §4 |
| D-38-7 MinIO | تهيئة مستخدم File.Storage تعمل فقط مع ملف تعريف `minio` (المزوّد الافتراضي `Local` من PLAN-36). وهذه الخطة مالكة التزام PLAN-34 المؤرخ «استبعاد ObjectStorage من CI» في `docs/qa/dated-obligations.json`: إن حدّد المالك `MINIO_IMAGE`/`HRM_MINIO_IMAGE` تعود الفئة إلى خطوة Testcontainers ويُغلق الالتزام، وإلا يُجدَّد بقرار في `decision-log.json` | §1 |

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-38-ops-first-boot-data-broker-secrets` | — |
| 0.2 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء |
| 0.3 | التحقق من مخرجات PLAN-36 | وجود `docker-compose.vps.yml` و`ProductionConfigurationGuard` و`scripts/vps/deploy.ps1` | موجودة؛ وإلا توقف وأبلغ |
| 0.4 | جرد طوبولوجيا RabbitMQ | على مكدّس القبول (مستخدم إداري): `rabbitmqctl list_exchanges name type` و`list_queues name` بعد رحلة AC-HCM-33 | `reports/plan38-rabbit-topology.json`: كل اسم تبادل وطابور مصنّف حسب الخدمة المالكة، ومنها طوابير `*_bus_*` المؤقتة وتبادلات `MassTransit:Fault--*` |
| 0.5 | جرد البذور | لكل خدمة: أي بذرة تعمل في `SeedProfile.None`، وأين يُبتلع فشلها | جدول في `reports/plan38-baseline.md` |
| 0.6 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "Sec004\|Ten002\|DB001"` | أخضر أو إخفاقات موثقة |

## In scope

### 1. حاوية التهيئة: قواعد البيانات والأدوار والترحيلات (G-OPS-02)

**المشكلة:**
- الترحيلات معطلة عند الإقلاع في الإنتاج ([docker-compose.production.yml:9](../../docker-compose.production.yml)). ومهمة `migrate` تعمل على runner في GitHub لا يصل إلى PostgreSQL المغلق ([deploy.yml:241-294](../../.github/workflows/deploy.yml)، [docker-compose.production.yml:59-61](../../docker-compose.production.yml)).
- المهمة بلا `ConnectionStrings__*Migrator`، فتسقط إلى اتصال التطبيق ([run-migrations.sh:106-107](../../scripts/run-migrations.sh)).
- صورة `hr-migrate` التي يطلبها k8s غير موجودة ([migrate-job.yaml:34](../../deploy/k8s/base/migrate-job.yaml)).
- الأدوار تُنشأ عند أول تهيئة فقط، بكلمة مرور واحدة لأن الحاوية لا تستلم غير `POSTGRES_APP_PASSWORD` ([docker-compose.yml:10-16](../../docker-compose.yml)، [init-service-roles.sh:6-15,104-149](../../docker/init-service-roles.sh)).
- `init-databases.sql` غير قابل للإعادة (`CREATE DATABASE` بلا شرط، [init-databases.sql:1-20](../../docker/init-databases.sql)).
- `hrm_backup` لا يُنشأ ([create-hrm-backup-role.sql](../../scripts/sql/create-hrm-backup-role.sql)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| صورة `hr-migrate` | `docker/Migrator.Dockerfile` (جديد) | مرحلة SDK تبني الحزم الـ15 بـ `scripts/build-ef-bundles.sh --self-contained -r linux-x64` (خيار جديد في السكربت). مرحلة تشغيل `runtime-deps:10.0` + `postgresql16-client` + `curl` + `deploy/bootstrap/*` + `scripts/run-migrations.sh` + `scripts/sql/*`. الاسم يطابق k8s ([migrate-job.yaml:34](../../deploy/k8s/base/migrate-job.yaml)) |
| سكربت التهيئة | `deploy/bootstrap/bootstrap.sh` (جديد) | بالترتيب، وكل خطوة قابلة للإعادة: (1) انتظار PostgreSQL؛ (2) `ensure-databases.sql` (`SELECT 'CREATE DATABASE …' WHERE NOT EXISTS … \gexec`)؛ (3) `ensure-roles.sql` (§1.1)؛ (4) لكل خدمة: الحزمة باتصال `<svc>_migrator` ثم `ten002c-harden-tenant-rls.sql` كما في [run-migrations.sh:82-96,135](../../scripts/run-migrations.sh)؛ (5) منح `hrm_backup`؛ (6) مستخدمو RabbitMQ (§2)؛ (7) مستخدم MinIO إن فُعّل ملف التعريف؛ (8) كتابة `/var/hrm/bootstrap/result-<utc>.json` |
| خدمة compose | `docker-compose.production.yml` (`hrm-bootstrap`) | `image: ${HRM_IMAGE_REPO}/hr-migrate:${IMAGE_TAG}`، و`restart: "no"`، وشبكة `data`، و`depends_on` على `postgresql` و`rabbitmq` بحالة `healthy`. وكل تطبيق يضيف `hrm-bootstrap: condition: service_completed_successfully` |
| بيئة الأدوار | `postgresql` و`hrm-bootstrap` | تمرير `POSTGRES_<SVC>_APP_PASSWORD` و`POSTGRES_<SVC>_MIGRATOR_PASSWORD` (15 خدمة) و`POSTGRES_BACKUP_PASSWORD` إلى الحاويتين. سلاسل اتصال التطبيقات في [docker-compose.production.yml:253](../../docker-compose.production.yml) وأخواتها تستخدم المتغير الخاص دون سقوط إلى قيمة مشتركة |
| ترحيلات بالتهيئة | `x-app-production-env` | `Database__RunMigrationsAtStartup: "false"` يبقى، و**في الوضعين** تطبق التهيئة الترحيلات. `docker-compose.vps-demo.yml` (PLAN-36) يكف عن ضبطه `true`، ويقتصر على سلاسل الاتصال الإدارية |
| خط النشر | `.github/workflows/deploy.yml` | حذف مهمة `migrate` التي تعمل على runner GitHub. النشر عبر `remote-deploy.sh` يشغّل التهيئة على المضيف. `scripts/run-migrations.sh` يبقى لمهمة k8s |

#### 1.1 `ensure-roles.sql`

- نسخة قابلة للإعادة من [init-service-roles.sh:20-78](../../docker/init-service-roles.sh) تُستدعى بمتغيرات psql:
  - إنشاء الدور إن غاب، ثم `ALTER ROLE … PASSWORD :'pwd'` في كل تشغيل. تدوير كلمة مرور في `.env` يكفي إذن؛
  - المنح، و`REVOKE CREATE` من `*_app`، و`NOBYPASSRLS`، و`REASSIGN OWNED BY <app> TO <migrator>`؛
  - `REVOKE CONNECT … FROM PUBLIC` وإلغاء الاتصال العابر ([init-service-roles.sh:80-102](../../docker/init-service-roles.sh)).
- `hrm_backup`:
  - `LOGIN BYPASSRLS NOSUPERUSER`، و`CONNECT` على القواعد الـ16؛
  - `SELECT` على كل الجداول؛
  - `ALTER DEFAULT PRIVILEGES FOR ROLE <svc>_migrator … GRANT SELECT ON TABLES TO hrm_backup`.
  - فحص TEN-002C يرفض `BYPASSRLS` على `%_app` فقط ([ten002c-harden-tenant-rls.sql:56-71](../../scripts/sql/ten002c-harden-tenant-rls.sql))، فلا يتعارض.
- `init-service-roles.sh` يصبح غلافاً رفيعاً يستدعي `ensure-roles.sql`، فيبقى مسار التطوير يعمل.

**القواعد:**
1. فشل أي خطوة يعطي رمز خروج غير صفري، فلا تُقلع التطبيقات (`service_completed_successfully`).
2. التشغيل الثاني على قاعدة مكتملة لا يغيّر أي صف بيانات ولا جدول. الناتج: «0 migrations applied» لكل قاعدة.
3. لا تُطبع كلمة مرور في السجلات. `psql` يستلمها عبر `PGPASSWORD` لكل أمر.
4. التطبيقات لا تملك `CREATE` على المخطط في وضع `least-privilege`. يثبت ذلك AC-OPS-38c.

### 2. الوسيط: مستخدمون بأقل صلاحية، ومهل مؤجلة، وعقدة ثابتة (G-OPS-02، G-OPS-10)

**المشكلة:**
- التطبيقات تتصل بـ `mq_*` ([docker-compose.production.yml:254-255](../../docker-compose.production.yml)) ولا شيء ينشئهم.
- صلاحية `configure` لا تطابق تبادلات `HRManagement.MessageContracts.Events:*` ([init-rabbitmq-service-users.sh:30](../../scripts/init-rabbitmq-service-users.sh)).
- `UseDelayedMessageScheduler` ([MassTransitExtensions.cs:79](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)) يحتاج إضافة غير موجودة في `rabbitmq:3.13-management-alpine`، وأربع sagas تجدول مهلاً ([HireCaseStateMachine.cs:91](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/HireCaseStateMachine.cs)).
- لا `hostname` ثابت ولا volume ([docker-compose.yml:26-40](../../docker-compose.yml)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| صورة الوسيط | `docker/rabbitmq/Dockerfile` (جديد) | `FROM rabbitmq:3.13-management-alpine`، وتنزيل `rabbitmq_delayed_message_exchange-3.13.0.ez` مع checksum، ثم `rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange`. تُبنى وتُدفع مع الصور (`build-push-images.ps1`) باسم `hr-rabbitmq` |
| عقدة ثابتة | `docker-compose.yml` (`rabbitmq`) | `hostname: rabbitmq`، و`RABBITMQ_NODENAME: rabbit@rabbitmq`، و`volumes: rabbitmq_data:/var/lib/rabbitmq` |
| مصفوفة الصلاحيات | `deploy/bootstrap/rabbitmq-users.json` (جديد، بلا أسرار) | لكل خدمة: المستخدم، ومتغير كلمة المرور، وتعبير `configure` مشتق من جرد 0.4. التعبير يشمل بادئة نقاط النهاية (`hr-core-web`…، وفق `ResolveEndpointPrefix`، [MassTransitExtensions.cs:98-105](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs))، و`HRManagement\.`، و`MassTransit`، وطوابير الناقل المؤقتة `.*_bus_.*`. و`write`/`read` = `.*` |
| إنشاء المستخدمين | `bootstrap.sh` الخطوة 6 | عبر واجهة الإدارة الداخلية (`http://rabbitmq:15672/api`) بالمستخدم الإداري: `PUT /api/users/{u}` ثم `PUT /api/permissions/%2F/{u}`. قابلة للإعادة وتدوّر كلمة المرور. `init-rabbitmq-service-users.sh` و`definitions-service-users.json` يُحذفان أو يصبحان غلافاً للمصفوفة نفسها |
| الوضع | `docker-compose.vps-demo.yml` | حذف تجاوز `RabbitMQ__Username/Password` الإداري. الخدمات تستخدم `mq_*` افتراضياً، و`HRM_MQ_POSTURE=admin` يعيد التجاوز في ملف مستقل `docker-compose.posture-mq-admin.yml` |

**القواعد:**
1. لا تطبيق يتصل بالمستخدم الإداري في `least-privilege`. يُفحص بـ `rabbitmqctl list_connections user`.
2. مستخدم خدمة لا ينشئ طابوراً لخدمة أخرى. AC-OPS-38c يثبت ذلك.
3. التعبيرات مشتقة من جرد فعلي ومختبرة على Testcontainers RabbitMQ بالصورة الجديدة، لا مكتوبة من الذاكرة.
4. `docker compose up -d --force-recreate rabbitmq` لا يفقد رسالة دائمة ولا طابور quorum.

### 3. توليد الأسرار وإزالة القيم الوهمية (G-OPS-13)

**المشكلة:**
- `GatewayCallerProof:Secret = "USE_ENV_OR_SECRET_MANAGER"` في 13 ملف وحدة وفي البوابة ([gateway appsettings.Production.json:35-38](../../src/Gateway/HRManagement.Gateway/appsettings.Production.json)، [HR.Core appsettings.Production.json:251-254](../../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)). وفي البوابة أيضاً `Keycloak:ClientSecret` و`ConnectionStrings:GatewayAuditDb` وهميتان.
- `.env.example` تنقصه 6 متغيرات إلزامية (`SVC_HR_HEALTH_CLIENT_SECRET`، `SVC_HR_MOVEMENTS_CLIENT_SECRET`، `SVC_DEV_ANALYTICS_CLIENT_SECRET`، `SVC_COMPLIANCE_CLIENT_SECRET`، `GRAFANA_ADMIN_USER`، `GRAFANA_ADMIN_PASSWORD`)، وتنقصه `POSTGRES_BACKUP_*`.
- حاوية النسخ تتصل بـ `hrm_backup` بكلمة مرور الحساب الإداري ([docker-compose.production.yml:143-147](../../docker-compose.production.yml)، [backup-postgres.sh:23-24](../../scripts/backup-postgres.sh)).
- `Sec004SecretRotationTests` نصية ولم تلتقط شيئاً من ذلك.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `init-secrets` | `scripts/vps/init-secrets.ps1` و`deploy/vps/init-secrets.sh` (جديدان) | يقرأ كل `${VAR:?` في سلسلة الإنتاج والخادم الواحد، ومصفوفة الأدوار والمستخدمين. يكمّل `.env` بقيم عشوائية (`RandomNumberGenerator`، 32 بايت base64url) لما ينقص فقط، ولا يكتب فوق قيمة موجودة. يطبع أسماء ما ولّده لا قيمه، ويضبط صلاحية 600. الأسماء غير الأسرار (`APP_DOMAIN`…) تُطلب ولا تُولَّد |
| تكافؤ الأمثلة | `.env.example` و`deploy/vps/.env.vps.example` | إضافة الستة الناقصة، و`POSTGRES_<SVC>_APP/MIGRATOR_PASSWORD`، و`RABBITMQ_<SVC>_PASS`، و`POSTGRES_BACKUP_PASSWORD`، و`GATEWAY_CALLER_PROOF_SECRET` |
| إزالة القيم الوهمية | 14 ملف `appsettings.Production.json` | حذف مفتاح `GatewayCallerProof:Secret` (يبقى `Enforce`)، وقيم `USE_ENV_*`/`USE_SECRET_MANAGER` في البوابة. القيمة تأتي من البيئة وحدها |
| حارس الإعداد | `ProductionConfigurationGuardOptions.CriticalKeys` (PLAN-36) | يُمدَّد إلى: سلسلة اتصال الخدمة الخاصة، و`RabbitMQ:Username/Password`، و`ServiceIdentity:ClientSecret` حين يُضبط `ClientId`، و`Recruitment:Portal:OtpHmacKey` لـ HR.Core. وكذلك `RabbitMQ:Username` مساوياً للمستخدم الإداري حين `HRM_MQ_POSTURE=least-privilege` |
| النسخ | `docker-compose.production.yml` (`postgres-backup`) | `BACKUP_PGUSER: hrm_backup` و`BACKUP_PGPASSWORD: ${POSTGRES_BACKUP_PASSWORD:?…}` بلا سقوط إلى الحساب الإداري. جدولة النسخ والتحقق منها ← الموجة 2 |

**القواعد:**
1. لا سر في المستودع ولا في حزمة النشر ولا في سجلات CI. `init-secrets` يرفض الكتابة داخل شجرة git إلا إلى `.env` المستثنى في `.gitignore`.
2. كل `${VAR:?}` في ملفات الإنتاج موجود في الأمثلة وفي مصفوفة `init-secrets`. يثبت ذلك اختبار الوحدة `Ops038EnvParityTests`.
3. أسرار عملاء S2S (`SVC_*`) تُولَّد هنا فقط. ربطها بالخدمات في compose ← PLAN-37، وبعملاء Keycloak ← PLAN-39.

### 4. البذور الإلزامية تفشل الإقلاع (G-OPS-02)

**المشكلة:**
- فشل `NationalSeedOrchestrator.ApplyMandatoryAsync` يُسجَّل تحذيراً ويستمر الإقلاع في الإنتاج ([HR.Core Program.cs:294,316-318](../../src/Services/HRCore/HR.Core.Web/Program.cs)).
- النمط نفسه في خدمات أخرى، منها HR.Contracts ([Program.cs:114-117](../../src/Services/HRContracts/HR.Contracts.Web/Program.cs)) وHR.Training ([Program.cs:64-67](../../src/Services/HRTraining/HR.Training.Web/Program.cs)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `SeedRunner` | `src/Shared/HRManagement.ServiceDefaults/Seeding/SeedRunner.cs` (جديد) | `RunMandatoryAsync(name, func)`: في Production يرمي ويوقف الإقلاع إلا مع `Seed:AllowMandatoryFailure=true` (تسجيل `Critical` ومقياس). و`RunOptionalAsync(name, func)`: تحذير كما اليوم |
| تطبيقه | `Program.cs` في HR.Core وكل خدمة تبذر في `SeedProfile.None` (من جرد 0.5) | البذور الوطنية والمراجع إلزامية. بذور Demo وAcceptance اختيارية |
| جرد البذور | `docs/OPS_SEED_INVENTORY.md` (جديد) | لكل خدمة: البذور التي تعمل في كل ملف تعريف، وتصنيفها. ويسرد ما **لا** يعمل في `None` مع أن الإنتاج يحتاجه: بذور HR.Contracts المرجعية لا تعمل إلا خارج `None` ([Program.cs:94-107](../../src/Services/HRContracts/HR.Contracts.Web/Program.cs))، وإطار Payroll القانوني لا يُبذر إلا في Demo ([Payroll Program.cs:134-136](../../src/Services/Payroll/Payroll.Web/Program.cs)). الأول يُصلح هنا لأنه صغير: `ContractsDataSeeder` (نوع العقد الافتراضي، مرجع بلا بيانات تجريبية) يخرج من شرط `None` ويُشغَّل بـ `RunMandatoryAsync` في كل الملفات عدا Acceptance، و`DemoContractsSeeder` يبقى لـ Demo. والثاني ← PLAN-54 |

**القواعد:**
1. في Acceptance يبقى السلوك الحالي (رمي)، وفي Development تحذير.
2. رسالة الفشل تسمّي البذرة والخدمة، ولا تطبع بيانات.

### 5. مجمعات الاتصال (G-OPS-10)

**المشكلة:** لا `Maximum Pool Size` في أي سلسلة اتصال، و`max_connections` كان 100 ([docker-compose.production.yml:253](../../docker-compose.production.yml)). PLAN-36 رفعه إلى 200.

**التصميم:**
- `Maximum Pool Size=${HRM_PG_POOL_MAX:-10}` في سلاسل الاتصال الـ15، و20 لـ HR.Core.
- `KC_DB_POOL_MAX_SIZE: 20` لـ Keycloak.
- المجموع 180 من 200.
- `postgres-exporter` (عند تفعيل المراقبة الكاملة) يعرض `pg_stat_activity_count`.

**القواعد:** المجموع المضبوط أقل من 90% من `max_connections`، ويفحصه `lint-compose.ps1`.

### 6. ملف قبول بأقل صلاحية

| Artifact | المسار | الغرض |
|----------|--------|--------|
| ملف القبول | `docker-compose.acceptance-leastpriv.yml` (جديد) | فوق الملف الشبيه بالإنتاج (PLAN-37): سلاسل اتصال `*_app`، ومستخدمو `mq_*`، وخدمة `hrm-bootstrap`، و`Seed__Profile=None`. يعمل باسم مشروع مستقل `hrm-leastpriv` على volumes جديدة، فلا يمس قاعدة القبول الرئيسية |
| سبب الفصل | — | بذور القبول تكتب صفوفاً لثلاث جهات، وتحت RLS المفروض قد يرفضها `WITH CHECK`. هذا من OPS-D (الموجة 2)، فلا يُنقل الملف الشبيه بالإنتاج كله إلى أدوار التطبيق في هذه الخطة |

## Out of scope

- **أثر RLS المفروض** على مهام الخلفية (فحص الملفات، `FileReferenceOutboxDispatcher`) وبوابة `/careers` والقراءات الوطنية من جلسة جهة أخرى، وجعل `least-privilege` افتراضياً على الخادم ← الموجة 2 (OPS-D)، مع PLAN-48 لرؤية الصفوف الوطنية على مستوى EF.
- جدولة النسخ واستعادتها والتنبيه عليها والنسخ خارج الموقع ← الموجة 2 (OPS-E). هنا يُنشأ `hrm_backup` ويُصحح اتصاله فقط.
- مستخدم Keycloak لقاعدته، والـ realm، وأسرار العملاء داخل Keycloak ← PLAN-39.
- ربط `SVC_*` و`Integration__*` و`ServiceIdentity__Scope` بالخدمات في compose وk8s ← PLAN-37.
- تصميم إثبات البوابة (مفتاح لكل خدمة، منع الإعادة) ← PLAN-42.
- إطار الرواتب القانوني في الإنتاج ← PLAN-54.
- سياسة إعادة المحاولة، وأداة إعادة تشغيل `_error` ← الموجة 2.
- مخطط k8s: يُكتفى بأن صورة `hr-migrate` صارت موجودة بالاسم الذي يطلبه.

## Acceptance scenarios

على `docker-compose.acceptance-leastpriv.yml` (مشروع `hrm-leastpriv`)، وكل سيناريو `[RequiredAcceptance]` مع `EvidenceRecorder("AC-OPS-38")`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-OPS-38a | volumes فارغة ← `hrm-bootstrap` يخرج 0؛ القواعد الـ16 موجودة؛ `__EFMigrationsHistory` يطابق آخر ترحيل في كل حزمة؛ كل تطبيق `healthy`؛ `pg_stat_activity.usename` لكل قاعدة هو `<svc>_app`؛ `rabbitmqctl list_connections user` يظهر `mq_*` فقط | لا اتصال تطبيق بالحساب الإداري في القاعدة أو الوسيط |
| AC-OPS-38b | تشغيل `hrm-bootstrap` ثانية ← «0 migrations applied»، ولا تغيير في عدد صفوف `pg_class`؛ تدوير `POSTGRES_PAYROLL_APP_PASSWORD` ثم النشر ← Payroll يتصل | كلمة المرور القديمة تُرفض (`28P01`) |
| AC-OPS-38c | `mq_payroll` ينشر `IPayrollProcessed` ويُستهلك | `hr_core_app` لا ينشئ جدولاً (`42501`) ولا يتصل بـ `payroll_db`؛ و`mq_payroll` لا ينشئ طابوراً `hr-core-web-x` (`ACCESS_REFUSED`). AC-20 الحي = Pass |
| AC-OPS-38d | نشر `IEmployeeCreated` بترويسة جهة (أداة AC-16) ← إيصال Inbox وأثر في Auth.Directory وHR.Training بمستخدمي `mq_*` | رسالة مكررة بالمعرّف نفسه ← لا أثر ثانٍ |
| AC-OPS-38e | `rabbitmq-plugins list` يظهر الإضافة مفعّلة؛ رسالة مجدولة بمهلة 10 ثوانٍ تصل (اختبار تكامل)؛ بدء saga تعيين لا يسجّل `x-delayed-message` خطأً | — |
| AC-OPS-38f | رسالة دائمة في طابور بلا مستهلك ← `--force-recreate rabbitmq` ← تُستهلك بعد عودة المستهلك | — |
| AC-OPS-38g | `init-secrets` على `.env` فارغ ← `docker compose config` ينجح، ولا يُطبع سر | `GatewayCallerProof__Secret` غائب ← الحارس يوقف الإقلاع؛ وسلسلة اتصال فيها `USE_ENV_` ← الحارس يوقف الإقلاع |
| AC-OPS-38h | البذور الإلزامية تكتمل والخدمة `healthy` | سحب `INSERT` على `WorkflowDefinitions` من `hr_core_app` ثم إعادة الإقلاع ← HR.Core يخرج برسالة تسمّي البذرة، لا تحذير صامت |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Ops038|FullyQualifiedName~SeedRunner|FullyQualifiedName~ProductionConfigurationGuard|FullyQualifiedName~Sec004"
# تكامل حقيقي: PostgreSQL وRabbitMQ بالصورة الجديدة
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Category=Testcontainers&FullyQualifiedName~Ops038"
pwsh scripts/vps/lint-compose.ps1
docker build -f docker/Migrator.Dockerfile -t hr-migrate:local .
docker build -f docker/rabbitmq/Dockerfile -t hr-rabbitmq:local docker/rabbitmq
pwsh scripts/vps/init-secrets.ps1 -EnvFile .env.leastpriv
docker compose -p hrm-leastpriv --env-file .env.leastpriv -f docker-compose.yml -f docker-compose.services.yml `
  -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml `
  -f docker-compose.acceptance-prodlike.yml -f docker-compose.acceptance-leastpriv.yml --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcOps38|FullyQualifiedName~Ac20DatabaseIsolation"
# انحدار الخادم الواحد (PLAN-36)
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcOps36"
```

## Definition of done

- خادم جديد يُبنى من volumes فارغة بلا خطوة يدوية، وإعادة النشر قابلة للتكرار وتدوّر كلمات المرور.
- مهمة `migrate` على runner GitHub محذوفة، وصورة `hr-migrate` موجودة.
- مستخدمو `mq_*` افتراضيون على الخادم الواحد، والإضافة المؤجلة مفعلة، والعقدة ثابتة.
- لا قيمة وهمية في `appsettings.Production.json`، و`.env.example` يطابق `:?`.
- البذور الإلزامية تفشل الإقلاع في Production، ونوع العقد الافتراضي يُبذر في `None`.
- التزام PLAN-34 «استبعاد ObjectStorage من CI» مغلق أو مجدَّد بقرار (D-38-7).
- AC-OPS-38a..h وAC-20 = Pass في `acceptance-evidence/AC-OPS-38/` و`AC-20/`، وانحدار AC-OPS-36 أخضر.
- تحديث سطر «الحالة» في G-OPS-02 وG-OPS-13 وجزء G-OPS-10 في الدراسة، و`PRODUCTION_RUNBOOK.md` §3 وما بعدها (الترحيلات)، و`RABBITMQ_OPERATIONS.md`، و`SEC_004_SECRET_ROTATION.md` (التدوير عبر `.env` ثم النشر).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md` |
| Images | `docker/Migrator.Dockerfile`، `docker/rabbitmq/Dockerfile` |
| Bootstrap | `deploy/bootstrap/bootstrap.sh`، `ensure-databases.sql`، `ensure-roles.sql`، `rabbitmq-users.json` |
| Compose | `docker-compose.production.yml` (`hrm-bootstrap`، كلمات مرور، مجمعات، نسخ)، `docker-compose.yml` (`rabbitmq`)، `docker-compose.vps-demo.yml` (معدل)، `docker-compose.posture-mq-admin.yml`، `docker-compose.acceptance-leastpriv.yml` |
| Scripts | `scripts/vps/init-secrets.ps1`، `deploy/vps/init-secrets.sh`، `scripts/build-ef-bundles.sh` (`--self-contained`)، `docker/init-service-roles.sh` (غلاف)، `scripts/vps/build-push-images.ps1` (صورتان إضافيتان) |
| Code | `SeedRunner.cs`، `Program.cs` للخدمات المعنية، تمديد `ProductionConfigurationGuardOptions`، 14 ملف `appsettings.Production.json` |
| CI | `.github/workflows/deploy.yml` (حذف `migrate`) |
| Unit | `Ops038EnvParityTests.cs`، `SeedRunnerTests.cs`، `Ops038RabbitPermissionMatrixTests.cs` (كل بادئة نقطة نهاية لها مستخدم وتعبير) |
| Integration | `Ops038BootstrapIdempotencyTests.cs` (Testcontainers PostgreSQL: تشغيلان، وتدوير كلمة مرور، و`hr_core_app` بلا DDL)، `Ops038RabbitLeastPrivilegeTests.cs` (Testcontainers RabbitMQ بالصورة الجديدة: ناقلان بمستخدمين مختلفين، ومهلة مؤجلة) |
| Acceptance | `AcOps38FirstBootTests.cs` |
| Docs | `docs/OPS_SEED_INVENTORY.md`، `docs/OPS_VPS_SINGLE_HOST.md` (قسم الإقلاع الأول والتدوير)، وأسطر «الحالة» في الدراسة |

## Risks

| الخطر | التخفيف |
|-------|---------|
| الحزم المستقلة (`--self-contained`) تكبّر الصورة (15 × ~70 MB) | مقبول لخادم واحد. البديل حزم معتمدة على الإطار فوق `dotnet/runtime:10.0`، وكلاهما مختبر في `Ops038BootstrapIdempotencyTests` |
| تشغيل الحزمة يستدعي باني المضيف في `Program.cs` فيُشغّل حراساً (يحتاج تحقق) | `IDesignTimeDbContextFactory` لكل سياق إن ظهر ذلك في 0.2؛ والقبول يكشفه مبكراً |
| تعبيرات `configure` تفوّت اسماً يولده MassTransit لاحقاً (مثل تبادل التأجيل) | الاشتقاق من جرد حي في 0.4 وبعد AC-OPS-38e، واختبار التكامل بناقل كامل لكل خدمة |
| ملف القبول الشبيه بالإنتاج (PLAN-37) قد ينتقل إلى أدوار `*_app` قبل OPS-D فيصطدم بـ RLS | هذه الخطة تفصل ملف `acceptance-leastpriv` في مشروع مستقل، وتوثّق الاعتماد على OPS-D في `reports/plan38-final-report.md` |
| `REASSIGN OWNED` على قواعد قائمة أنشأها الحساب الإداري (وضع العرض) | `ensure-roles.sql` لا ينقل ملكية جداول يملكها الحساب الإداري. نقل خادم عرض قائم إلى `least-privilege` إجراء موثق منفصل (`OPS_VPS_SINGLE_HOST.md`) بعد OPS-D |
| تنزيل إضافة RabbitMQ من GitHub أثناء البناء | البناء خارج الخادم مع checksum مثبت في Dockerfile |

## Effort estimate

9–10 أيام عمل:
- المرحلة 0 والجرد: 1
- صورة `hr-migrate` وسكربت التهيئة والأدوار: 3
- الوسيط (الصورة، والمستخدمون، والعقدة): 1.5
- الأسرار وإزالة القيم الوهمية والحارس: 1.5
- البذور: 0.5
- المجمعات: 0.5
- الاختبارات والقبول والوثائق: 1.5

## ملاحظات التنفيذ والانحرافات

نُفّذت الخطة في جلسة واحدة (المراحل P0–P8 من `docs/acceptance-plans/execution/PLAN-38-AGENT-PROMPT.md`)؛ التفاصيل والأزمنة في `reports/plan38-final-report.md`.

- **D-38-5:** بقي ملف الأسرار في `/opt/hrm/shared/.env` (600، مالكه `hrm`) كما وضعته PLAN-36، لا `/opt/hrm/.env` لمالك root؛ `deploy.ps1` و`remote-deploy.sh` يقرآنه من هناك.
- **§1 (P-5):** `hrm-bootstrap` يرحّل دائماً بدور `<svc>_migrator` ثم يطبّق TEN-002C، في وضعي قاعدة البيانات كليهما؛ الوضع يغيّر اتصال التطبيقات فقط. والحزم لا تأخذ كلمة مرور في سطر الأوامر (Npgsql يقرأ `PGPASSWORD`)، ولكل سياق `IDesignTimeDbContextFactory` كي لا تشغّل الحزمة `Program` الخدمة.
- **§1 دور النسخ:** `hrm_backup` يأخذ `pg_read_all_data` مع `BYPASSRLS` بدل `SELECT` لكل قاعدة مع صلاحيات افتراضية لكل migrator: يغطي الجداول المقبلة و`keycloak_db` (جداول يملكها المدير).
- **§2 المصفوفة:** نمط ناقل MassTransit `[^-]+_bus_[^-]+` بدل `.*_bus_.*` (المعرّف لا يحوي `-` أبداً، فلا يطابق طابور خدمة أخرى)، وصلاحية `HR\.Contracts\.Infrastructure\.Sagas:.*` لـ HR.Contracts وحده. ولا مستخدم للبوابة (لا تتصل بالوسيط).
- **§3 القيم الوهمية:** أُزيلت أيضاً، فوق القائمة، قيم `YOUR_*` في `AllowedHosts` وCORS و`keycloak.example.com`، وصار `FileStorage:SecretKey` فارغاً في 5 ملفات `appsettings.json` أساسية (عيب PLAN-36 رقم 3).
- **§4 البذور:** `SeedRunner` يعامل بيئة `Acceptance` كـ Production للبذور الإلزامية (الخطة: «السلوك الحالي في Acceptance»)، وإلا اكتفى مكدّس `hrm-leastpriv` بالتحذير. والإطار القانوني للرواتب يبقى بذرة عرض → PLAN-54.
- **§5 المجمّعات:** `max_connections` = 250 لا 200: أرقام الخطة (180 + احتياط 5) تبلغ 92.5 % من 200، فوق حد 90 % الذي تفرضه قاعدة `lint-compose.ps1`.
- **§7 عيبا PLAN-37:** لاعتماد الإجازة سببان لا سبب واحد: `xmin` بعد المطالبة (أُصلح بإعادة القراءة، ومعه إلغاء الإجازة بالنمط نفسه)، وصف `ServiceTransactionStateHistory` الجديد الذي يأخذ مفتاحه من الكيان فيعدّه EF موجوداً (أُصلح في المعالجات بـ `TrackNewHistoryAsync`). الإصلاح على مستوى النموذج (`ValueGeneratedNever` مع ترحيل) خارج حدود المعالج، فسُجّل لخطة لاحقة مع مواضع `MoveTo` الأخرى (معالج التعيين، و`ServiceTransactionsController`، و`InboundEventCommands`، و`SeparationRecordHandlers`، و`SagaSupportConsumers`) بوصفها **محتملة غير مثبتة**. وبطاقة الهوية: نموذج `IssuedCardFormModel` لـ Create و**Edit** معاً (كان Edit يربط الكيان نفسه).
- **القبول:** AC-OPS-36 لم يُعَد محلياً بل على الخادم (P7)؛ محلياً غطّت السلسلةَ `lint-compose.ps1` و`Ops036ComposeLintTests`. والبحث عن قيم سرية في السجلات يقتصر على القيم بطول 16 فأكثر (كلمة `postgres` ليست سراً).
- **الخادم:** فشل أول بناء لـ `hr-migrate` على الخادم (SDK 10.0.400: مولّد Razor مع خادم المترجم المشترك يفسد البناء التالي في سلسلة الـ15). الإصلاح: بناء كل مشروع بـ `--disable-build-servers` ثم الحزمة بـ `--no-build` (15/15 على الخادم). وفحص `containers-healthy` في `smoke.sh` صار يقبل `hrm-bootstrap` المنتهي بـ 0.
- **مؤجل:** مستخدم MinIO (D-38-7) واستثناء فئة ObjectStorage في CI (`PG-OPS38-OBJECTSTORAGE-CI`، مراجعة 2027-01-31)؛ ووضع `least-privilege` لقاعدة البيانات على الخادم بعد OPS-D.
- **المراجعة المستقلة (P8):** 0 عالية، 2 متوسطة، 4 منخفضة، وأُصلحت خمس باختبارات: مطالبة اعتماد الإجازة تطابق أيضاً القيم التي تحقق منها المعالج (تعديل بعد القراءة يُرفض ولا يُخصم شيء)؛ و`read` لمستخدمي الوسيط = تعبير `configure` (لا يستهلك مستخدم طوابير خدمة أخرى ولا يفرغها، و`write` يبقى `.*` كما قررت الخطة)؛ وكلمة مرور الوسيط تصل `jq` عبر stdin؛ ومسار k8s `scripts/run-migrations.sh` يمرر كلمات المرور عبر `PGPASSWORD` لا في سطر الأوامر؛ و`remote-deploy.sh` يطبّق قاعدة 90 % على أحجام المجمّعات من `.env`. والسادسة (غياب مفتاح الوضع في الحارس، وتوقيت الحارس بعد الترحيلات) سُجّلت لخطة لاحقة لأنها تغيّر أهداف نشر أخرى (k8s).
- **إعادة القبول بعد المراجعة:** كشفت إعادة AC-OPS-38 من volumes فارغة عيبين في المكدّس نفسه فأُصلحا: volumes القبول المشتركة قد تُنشأ بمالك root (يأخذ volume جديد مالكه من أول حاوية تركّبه) فتفشل File.Storage، فأُضيفت حاوية `acceptance-volume-init` قبل `hrm-bootstrap` كما في ملف الإنتاج؛ و38f صار يشترط عودة المستخدمين الذين يملكون مستهلكين فقط (ناقل File.Storage للنشر وحده يتصل عند أول نشر). ثم AC-OPS-38a..h = 8/8.

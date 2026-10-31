# PLAN-36 — إقلاع ملف الإنتاج على خادم واحد لعرض واختبار (OPS-A)

**Scope:** OPS — [دراسة الفجوات](../PRG_002_OPERATIONS_GAP_STUDY.md): G-OPS-01، G-OPS-10 (TLS والتعرض والذاكرة ومحدِّد المعدل والترويسات الممررة)، G-OPS-15  
**Status:** Done — 2026-10-31 (AC-OPS-36a..k Pass محلياً وعلى الخادم الحقيقي؛ انظر «ملاحظات التنفيذ والانحرافات» و[التقرير](../../reports/plan36-final-report.md))  
**Priority:** P0  
**Depends on:** PLAN-34 (خط أساس قابل للدفع)  
**Related cards:** DEP-001، SEC-001، IAM-001 (جانب الحافة)، OPS-001، REL-001 (completion-backlog 01/03/04)

## Goal

أن ينشر المالك النظام كاملاً، **بإعدادات `ASPNETCORE_ENVIRONMENT=Production`**، على خادم Hostinger KVM 4 واحد (4 vCPU، 16 GB، 200 GB NVMe، Ubuntu) كبيئة عرض واختبار ببيانات اصطناعية:

- تُبنى الصور على جهاز المالك (Windows/Docker Desktop) وتُدفع إلى سجل، ويسحبها الخادم. لا شيفرة مصدرية ولا أدوات بناء على الخادم.
- تصبح الخدمات الـ15 والبنية التحتية `healthy` بأمر نشر واحد موثّق.
- لا يُنشر للإنترنت إلا المنفذ 443 عبر Caddy بشهادة TLS حقيقية. البوابة وKeycloak خلفه بترويسات ممررة موثوقة.
- يُسجَّل الدخول من المضيف العام وتظهر الوحدات الـ14.
- تتسع الذاكرة على 16 GB مع swap. والمراقبة الخفيفة Seq وحده.
- قاعة عرض خلف NAT واحد لا تُخنق بمحدِّد المعدل.
- لا تُقلع أي خدمة بقيمة إعداد وهمية (`YOUR_*`، `USE_ENV_*`).

هذه الخطة **لا** تجعل البيئة صالحة لبيانات حقيقية. وضع قاعدة البيانات والوسيط فيها هو «وضع العرض» الذي أثبته مكدّس القبول (D-36-1)، ويرفعه PLAN-38 والموجة 2 (OPS-D).

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|------------------------------|-------|
| D-OPS-1 السجل ومصدر الصور | GHCR خاص: `ghcr.io/<owner>/hrmanagement/<service>:<sha12>`، تُبنى بـ `docker buildx --platform linux/amd64` على جهاز المالك. السجل يُضبط بـ `HRM_IMAGE_REPO`، فيصلح Docker Hub أو سجل آخر دون تعديل شيفرة | §6 |
| D-OPS-2 أسماء المضيفين وTLS | نطاقان فرعيان `APP_DOMAIN` و`SSO_DOMAIN` من نطاق المالك. إن لم يوجد نطاق: `hr.<ip-بشرطات>.sslip.io` و`sso.<ip-بشرطات>.sslip.io`. شهادات Let's Encrypt عبر Caddy بتحدي TLS-ALPN على 443 وحده | §2 |
| وصول لوحة Keycloak الإدارية | محجوبة من الخارج. يسمح بها `ADMIN_ALLOWED_CIDRS` (افتراضه فارغ = لا أحد)، والبديل نفق SSH | §2 |
| D-OPS-6 المراقبة | Seq وحده، مربوط بـ `127.0.0.1` ويُفتح بنفق SSH. OpenTelemetry معطل (`HRM_OTEL_ENABLED=false`). الحزمة الكاملة تبقى ملفاً اختيارياً | §3 |
| D-36-1 وضع قاعدة البيانات والوسيط | «وضع العرض» (`HRM_DEPLOYMENT_MODE=demo`): الخدمات تتصل بحساب PostgreSQL الإداري مع `Database__RunMigrationsAtStartup=true`، وبمستخدم RabbitMQ الإداري، كما في مكدّس القبول. تظهر لافتة «بيئة عرض — بيانات اصطناعية فقط». الأدوار الأقل صلاحية ← PLAN-38؛ RLS ← الموجة 2 (OPS-D) | §1، §5 |
| D-36-2 حدود المعدل | مجهول: 300 طلب/دقيقة لكل IP. مصادَق: 600 طلب/دقيقة لكل مستخدم. الأصول الثابتة معفاة | §4 |
| D-36-3 تخزين الملفات على الخادم | المزوّد `Local` على volume مسمّى. MinIO خلف ملف تعريف `minio` اختياري، بصورة يحددها المالك في `MINIO_IMAGE`، لأن الصورة المثبتة لم تعد قابلة للسحب | §1 |
| D-36-4 ملف البذور | `Seed__Profile=Demo` على خادم العرض | §6 |
| D-36-5 الذاكرة الاحتياطية | swap بحجم 8 GiB و`vm.swappiness=10` | §6 |
| D-OPS-9 الترخيص | معطل: `Deployment__RequireLicenseKey=false` | §7 |

كل افتراض خلف متغير في `deploy/vps/.env.vps.example`، فلا يتوقف التنفيذ على المالك.

## المرحلة 0 — خط الأساس

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع | `git switch -c feature/plan-36-ops-production-boot-single-host` | — |
| 0.2 | البناء | `dotnet build HRManagement.slnx -warnaserror` | 0 أخطاء (بعد PLAN-34) |
| 0.3 | لقطة التركيب الحالي | `docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml -f docker-compose.production.yml config` ثم الأمر نفسه مع `docker-compose.observability.yml` | الخطأ الأول بلا ملف المراقبة (`service "seq" has neither an image nor a build context`) مسجّل في `reports/plan36-baseline.md` |
| 0.4 | جرد القيم الموروثة | من ناتج `config --format json`: كل قيمة في خدمات الإنتاج تحوي `localhost` أو `Development` أو `YOUR_` أو `USE_ENV` | جدول في `reports/plan36-baseline.md` |
| 0.5 | قياس الذاكرة الحالي | مجموع `deploy.resources.limits.memory` | 12.25 GiB دون المراقبة (يطابق G-OPS-10) |

## In scope

### 1. تركيب الإنتاج يُقلع وحده (G-OPS-01: العوائق 1، 2، 5، 6، 7، 8)

**المشكلة:**
- **التطبيقات خلف ملف تعريف لا يفعّله النشر.** كل التطبيقات خلف `profiles: ["full-stack"]` ([docker-compose.yml:100,147,183](../../docker-compose.yml))، وأمر النشر لا يفعّله ([deploy.yml:347-364](../../.github/workflows/deploy.yml)).
- **الملف الإنتاجي مقرون بالمراقبة.** يعرّف خدمات المراقبة بلا `image` ([docker-compose.production.yml:571-630](../../docker-compose.production.yml)).
- **File.Storage بلا ملف إنتاج.** لا يوجد `appsettings.Production.json` له، فيرد 403 على البوابة ([GatewayCallerProof.cs:52-56](../../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProof.cs)).
- **البوابة ترث قيماً تطويرية.** `Keycloak__PublicAuthority=http://localhost:8088/...` و`Gateway__EnabledModules` = وحدتان ([docker-compose.yml:199,208-209](../../docker-compose.yml)).
- **HR.Core يطلب metadata عبر HTTPS.** `RequireHttpsMetadata: true` ([HR.Core appsettings.Production.json:21](../../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)، والافتراض في [KeycloakExtensions.cs:74](../../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs)) مع سلطة داخلية HTTP ([docker-compose.production.yml:22](../../docker-compose.production.yml)).
- **صورة MinIO لم تعد قابلة للسحب** ([docker-compose.yml:43](../../docker-compose.yml)، [plan30-final-report.md:297](../../reports/plan30-final-report.md)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| فصل تجاوزات المراقبة | `docker-compose.observability.production.yml` (جديد) | نقل كتلة `seq`…`postgres-exporter` من [docker-compose.production.yml:569-630](../../docker-compose.production.yml) إليه. يُستخدم مع `docker-compose.observability.yml` فقط، فيصبح الملف الإنتاجي صالحاً وحده |
| إعدادات البوابة الصريحة | `docker-compose.production.yml` (`hr-gateway`) | تُضبط صراحةً: `Keycloak__PublicAuthority=https://${SSO_DOMAIN}/realms/hrmanagement`، و`Gateway__EnabledModules__0..13` بالوحدات الـ14 (كما في [docker-compose.full-stack-gateway.yml:22-35](../../docker-compose.full-stack-gateway.yml))، و`HrAuth__BehindGateway: "false"`، و`AllowedHosts: ${APP_DOMAIN};localhost` |
| مرساة بيئة التطبيقات | `x-app-production-env` و`x-app-messaging-env` في الملف نفسه | `Keycloak__RequireHttpsMetadata: "false"` (السلطة داخلية على شبكة `data`)، و`Keycloak__AdditionalValidIssuers: https://${SSO_DOMAIN}/realms/hrmanagement`، و`AllowedHosts: "*"` للوحدات (مستمعات داخلية تحميها إثبات البوابة)، و`GatewayCallerProof__Secret: ${GATEWAY_CALLER_PROOF_SECRET:?…}` للبوابة والوحدات الـ14 |
| ملف إنتاج File.Storage | `src/Services/FileStorage/File.Storage.Web/appsettings.Production.json` (جديد) | `GatewayCallerProof:Enforce=true` (السر من البيئة)، و`Database:RunMigrationsAtStartup=false`، و`FileStorage:ClamAvRequired=true`، و`Serilog`. لا قيم وهمية |
| مزوّد الملفات | `docker-compose.production.yml` (`file-storage-web`، `hr-core-web`) | `FileStorage__Provider: ${HRM_FILE_PROVIDER:-Local}` و`FileStorage__LocalRootPath: /var/hrm/files` على volume `file_storage_data`. إزالة `depends_on: minio` من HR.Core وFile.Storage. MinIO يذهب إلى `profiles: ["minio"]` و`image: ${MINIO_IMAGE:?…}` |
| تفعيل ملف التعريف في النشر | `.github/workflows/deploy.yml` | إضافة `--profile full-stack` في النشر والتراجع؛ والبديل `COMPOSE_PROFILES=full-stack` في `.env` |

**القواعد:**
1. `docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.images.yml -f docker-compose.production.yml config` ينجح دون ملف المراقبة.
2. الناتج المعالَج لا يحوي في أي خدمة إنتاج: `localhost:8088`، ولا `ASPNETCORE_ENVIRONMENT: Development`، ولا `YOUR_`، ولا `USE_ENV`، ولا `dev-svc-secret-local-only`. يفحص ذلك `scripts/vps/lint-compose.ps1` (§8).
3. لا يُزال أي تشديد قائم (`cap_drop`، الشبكات `internal`). الإصلاح بالتجاوز الصريح فقط.
4. لا تمس هذه الخطة عناوين `Integration__*` و`ServiceIdentity__Scope` لكل خدمة. هي ملك PLAN-37، ويقتصر الفحص عليها هنا على الإبلاغ (§8).

### 2. الحافة: Caddy وTLS وكشف 443 وحده (G-OPS-01 العائق المصاحب، G-OPS-10)

**المشكلة:**
- لا حافة TLS. والبوابة وKeycloak منشوران على `0.0.0.0` بـ HTTP صريح ([docker-compose.production.yml:106-108,205-206](../../docker-compose.production.yml)).
- البوابة تفرض `CookieSecureAlways=true` ([gateway appsettings.Production.json:18](../../src/Gateway/HRManagement.Gateway/appsettings.Production.json))، وKeycloak `KC_HOSTNAME_STRICT_HTTPS=true` ([docker-compose.production.yml:120](../../docker-compose.production.yml)). فلا يعمل الدخول دون TLS.
- `KC_PROXY: edge` خيار مهجور ([:117](../../docker-compose.production.yml)).
- Docker يتجاوز `ufw` للمنافذ المنشورة. فالجدار الناري وحده لا يكفي، والمطلوب ألا يُنشر شيء غير Caddy.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| خدمة `edge` | `docker-compose.vps.yml` (جديد) | `caddy:2.8.4` (إصدار مثبت)، و`ports: ["443:443", "443:443/udp"]` فقط، وعنوان ثابت `172.28.0.10` على شبكة `edge` بشبكة فرعية ثابتة `172.28.0.0/24`، وvolumes `caddy_data` و`caddy_config`، وذاكرة 128 MiB |
| `Caddyfile` | `deploy/vps/Caddyfile` | موقعان: `{$APP_DOMAIN}` ← `reverse_proxy hr-gateway:8080`، و`{$SSO_DOMAIN}` ← `reverse_proxy keycloak:8080`. يتضمن: `auto_https disable_redirects`، و`request_body max_size 20MB`، ومطابِق `@ops path /health* /metrics* /alive*` يرد 404 لغير `ADMIN_ALLOWED_CIDRS`، ومطابِق `@kcadmin path /admin* /realms/master*` يرد 403 لغيرها، وسجل وصول JSON |
| إغلاق المنافذ | `docker-compose.vps.yml` | `ports: !override []` لـ `hr-gateway` و`keycloak`. وSeq وحده على `127.0.0.1:5380:80` |
| Keycloak خلف الحافة | `docker-compose.production.yml` (`keycloak`) | حذف `KC_PROXY`، وإضافة `KC_PROXY_HEADERS: xforwarded`، و`KC_HOSTNAME: ${SSO_DOMAIN:?…}`، و`KC_HOSTNAME_STRICT_BACKCHANNEL: "false"` (عناوين الخلفية داخلية لاتصال الخدمات بـ `http://keycloak:8080`)، و`command: ["start", "--import-realm"]` |
| الثقة بالوكيل | `hr-gateway` في `docker-compose.vps.yml` | `ForwardedHeaders__KnownProxies: 172.28.0.10` و`ForwardedHeaders__ForwardLimit: "1"`. عند ضبط `KnownProxies` تُلغى الشبكات الخاصة الافتراضية ([ForwardedProxyTrust.cs:48-51](../../src/Shared/HRManagement.ServiceDefaults/ForwardedProxyTrust.cs))، فلا يثق إلا بـ Caddy |

**القواعد:**
1. على الخادم لا يستمع للإنترنت إلا 443 (Caddy) و22 (SSH). يُثبت ذلك `ss -tlnp` و`docker compose ps --format json` في الاختبار الذاتي.
2. `X-Forwarded-For` الذي يرسله العميل لا يُعتمد. Caddy يلحق عنوان المتصل، والبوابة تأخذ آخر قفزة من الوكيل الموثوق وحده.
3. `/health/*` و`/metrics` و`/admin` غير متاحة من الإنترنت. الاختبار الذاتي يصلها عبر `docker compose exec` أو من `ADMIN_ALLOWED_CIDRS`.
4. لا شهادات ولا مفاتيح في المستودع. Caddy يديرها في `caddy_data`.

### 3. ميزانية الذاكرة والمراقبة الخفيفة (G-OPS-10)

**المشكلة:**
- سقوف الذاكرة 12.25 GiB دون المراقبة، و14–15 GiB معها ([docker-compose.production.yml:34-42,63-131](../../docker-compose.production.yml)). وHR.Core محصور في 512 MiB ([:245-247](../../docker-compose.production.yml)).
- ClamAV محصور في 2 GiB ([:77-86](../../docker-compose.production.yml)) مع إعادة تحميل التواقيع المتزامنة.
- OpenTelemetry يُفعَّل بمجرد وجود `OTEL_EXPORTER_OTLP_ENDPOINT` ([OpenTelemetryExtensions.cs:22-24](../../src/Shared/HRManagement.ServiceDefaults/OpenTelemetryExtensions.cs)، [docker-compose.production.yml:11-12](../../docker-compose.production.yml)).

**التصميم:** سقوف ملف `docker-compose.vps.yml`

| الحاوية | السقف | ملاحظة |
|---------|-------|--------|
| `hr-core-web` | 1024 MiB | أكبر خدمة |
| `payroll-web` | 768 MiB | |
| `hr-contracts-web` | 640 MiB | |
| `hr-gateway`، `file-storage-web` | 512 MiB لكل منهما | |
| الخدمات التسع الأخرى | 384 MiB لكل منها | 3.4 GiB معاً |
| `postgresql` | 2048 MiB | `command: postgres -c shared_buffers=512MB -c effective_cache_size=4GB -c max_connections=200 -c work_mem=8MB` |
| `keycloak` | 1024 MiB | |
| `clamav` | 2048 MiB | `deploy/vps/clamav/clamd.conf` مركّب يضبط `ConcurrentDatabaseReload no` |
| `rabbitmq` | 512 MiB | |
| `seq` | 768 MiB | `datalust/seq:2024.3` (المثبت أصلاً) |
| `edge` (Caddy) | 128 MiB | |
| **المجموع** | **≈ 13.1 GiB** | من 16 GB، مع swap 8 GiB احتياطاً لا تشغيلاً |

**بيئة التطبيقات:**
- `DOTNET_gcServer: "0"` (Workstation GC، يقلل الذاكرة المقيمة لـ15 عملية على 4 أنوية)؛
- `OpenTelemetry__Enabled: ${HRM_OTEL_ENABLED:-false}`؛
- `OTEL_EXPORTER_OTLP_ENDPOINT: ${HRM_OTEL_ENDPOINT:-}`؛
- `Serilog__SeqUrl: http://seq:5341`.

**وقت الإقلاع:** `healthcheck.start_period: 240s` للتطبيقات في ملف الخادم الواحد، لأن الترحيلات عند الإقلاع متسلسلة بقفل استشاري واحد ([DatabaseMigrationExtensions.cs:13-16](../../src/Shared/HRManagement.ServiceDefaults/DatabaseMigrationExtensions.cs)).

**القواعد:**
1. لا حاوية بلا `deploy.resources.limits.memory` في ملف الخادم الواحد، ويفحص ذلك `lint-compose.ps1`.
2. بعد تشغيل الاختبار الذاتي: لا حاوية `OOMKilled=true`، ومجموع الاستخدام أقل من 75% من ذاكرة المضيف. الأرقام الفعلية تُسجَّل في الدليل.
3. حزمة المراقبة الكاملة تبقى متاحة: `-f docker-compose.observability.yml -f docker-compose.observability.production.yml`.

### 4. محدِّد المعدل لقاعة عرض خلف NAT (G-OPS-10)

**المشكلة:**
- الحد العام 120 طلباً/دقيقة مقسوماً على `RemoteIpAddress` ([GatewayRateLimiting.cs](../../src/Gateway/HRManagement.Gateway/Services/GatewayRateLimiting.cs)، [gateway Program.cs:100,154](../../src/Gateway/HRManagement.Gateway/Program.cs)). بلا طابور منذ إصلاح العيب 15 لـ PLAN-35: الطلب الزائد يُرد بـ 429 فوراً.
- المحدِّد يعمل قبل المصادقة ([:189](../../src/Gateway/HRManagement.Gateway/Program.cs) مقابل [:219](../../src/Gateway/HRManagement.Gateway/Program.cs))، ويشمل أصول الوحدات عبر YARP: نحو 13 أصلاً في `_HrSharedLayout`، و[_HrSharedLayout.cshtml:17-25,166-199](../../src/Shared/HRManagement.Web.Shared/Views/Shared/_HrSharedLayout.cshtml).
- مكدّس القبول رفعه إلى 2000 ليعمل ([docker-compose.acceptance.yml:215](../../docker-compose.acceptance.yml)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `GatewayRateLimitPartitioner` | `src/Gateway/HRManagement.Gateway/Services/GatewayRateLimitPartitioner.cs` (جديد) | مفتاح التقسيم: `user:{sub}` للمصادَق، و`ip:{RemoteIpAddress}` لغيره، و`NoLimiter` للأصول الثابتة (امتدادات `.css .js .woff .woff2 .ttf .png .svg .ico .map` تحت `/ui/*/` أو الجذر) |
| خيارات | `RateLimiting:PermitLimitPerMinute` (المجهول، افتراضه 120 كما اليوم)، و`RateLimiting:AuthenticatedPermitLimitPerMinute` (افتراضه 600)، و`RateLimiting:ExemptStaticAssets` (افتراضه `true`) | قيم الخادم الواحد في `docker-compose.vps.yml`: 300 و600 |
| ترتيب الخط | `src/Gateway/HRManagement.Gateway/Program.cs` | نقل `app.UseRateLimiter()` بعد `app.UseAuthentication()`. أما `Retry-After` وتسجيل 429 بنوع التقسيم دون الهوية فموجودان منذ إصلاح العيب 15 في `GatewayRateLimiting` (النوع اليوم `ip`)؛ يمرّر المقسِّم الجديد `user` أو `ip` |

**القواعد:**
1. المحدِّد يبقى فعّالاً للمجهول، فطوفان طلبات من IP واحد يُرد بـ 429.
2. مستخدمون مصادَقون من IP واحد لا يتقاسمون حصة واحدة.
3. `Math.Clamp(…, 30, 2000)` يبقى للحدّين.
4. الحد لا يُضبط بـ `0` أو «بلا حد» في أي ملف إنتاج.

### 5. حارس الإعداد الإنتاجي (G-OPS-01 العائق 3)

**المشكلة:** `AllowedHosts` وهمي في 14 ملف إنتاج ([gateway appsettings.Production.json:34](../../src/Gateway/HRManagement.Gateway/appsettings.Production.json)، [HR.Core appsettings.Production.json:250](../../src/Services/HRCore/HR.Core.Web/appsettings.Production.json))، ولا شيء يرفض القيم الوهمية عند الإقلاع.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `ProductionConfigurationGuard` | `src/Shared/HRManagement.ServiceDefaults/ProductionConfigurationGuard.cs` (جديد) | `IHostedService` يعمل في Production (أو عند `Deployment:EnforceConfigGuard=true`) ويفشل الإقلاع برسالة عربية وإنجليزية تسمّي المفتاح، دون طباعة قيمة سرية |
| قائمة المفاتيح الحرجة | `ProductionConfigurationGuardOptions.CriticalKeys` | يبدأ بـ: `AllowedHosts`، و`GatewayCallerProof:Secret`، و`Keycloak:Authority`، و`Keycloak:PublicAuthority` (للبوابة)، و`Keycloak:ClientSecret` (للبوابة)، و`Organization:Name`. **PLAN-38 يمددها** إلى سلاسل الاتصال وبيانات الوسيط |
| القواعد المفحوصة | ↑ | قيمة تطابق `YOUR_`، `USE_ENV_OR_`، `USE_SECRET_MANAGER`، `CHANGE_ME`؛ و`GatewayCallerProof:Secret` أقصر من 32 حرفاً؛ و`Keycloak:PublicAuthority` يحوي `localhost` في Production؛ و`Gateway:EnabledModules` فيه عنصر فارغ؛ و`Organization:Name` يحوي تشويه ترميز (`Ã`) |
| التسجيل | `AddHrProductionNetworking` ([ProductionHostExtensions.cs:20-70](../../src/Shared/HRManagement.ServiceDefaults/ProductionHostExtensions.cs)) | يُسجَّل الحارس للبوابة والخدمات الـ14 |
| تحذير غير حاجب | ↑ | أي قيمة وهمية في مفتاح غير حرج تُسجَّل `Warning` باسم المفتاح، مثل `FileStorage:SecretKey` حين يكون المزوّد `Local` |

**القواعد:**
1. الحارس لا يقرأ ملفات ولا يتصل بشبكة. يفحص `IConfiguration` المعالَج فقط، فتجاوزات البيئة تُحترم.
2. لا يعمل الحارس في Development وAcceptance إلا بالمفتاح الصريح، فلا يكسر القبول الحالي.

### 6. الصور عبر السجل وحزمة النشر والخادم (G-OPS-01 العوائق 1، 9؛ D-OPS-1)

**المشكلة:**
- `docker-compose.images.yml` يثبت `ghcr.io/${GITHUB_REPOSITORY_OWNER}` ([docker-compose.images.yml:15-17](../../docker-compose.images.yml)).
- النشر يتطلب الشجرة المصدرية على الخادم لأن compose يركّب `./scripts` و`./docker` ([docker-compose.yml:15-16](../../docker-compose.yml)، [docker-compose.production.yml:152-155](../../docker-compose.production.yml)).
- `deploy-organization.ps1` يستدعي ملفاً غير موجود ويبني من المصدر ([deploy-organization.ps1:6,16](../../scripts/deploy-organization.ps1)).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| مرجع السجل | `docker-compose.images.yml` | `image: ${HRM_IMAGE_REPO:?…}/<service>:${IMAGE_TAG:?…}`. و`deploy.yml` يصدّر `HRM_IMAGE_REPO=ghcr.io/${{ github.repository_owner }}/hrmanagement` |
| البناء والدفع | `scripts/vps/build-push-images.ps1` (جديد) | يقرأ [docker/service-build-matrix.json](../../docker/service-build-matrix.json)، ويشغّل `scripts/docker-generate-build-manifest.ps1`، ثم `docker buildx build --platform linux/amd64 --push` لكل خدمة بوسم `git rev-parse --short=12 HEAD`. يرفض الشجرة المتسخة إلا بـ `-AllowDirty`، ويكتب `artifacts/vps/images-<tag>.json` (الوسم والـ digest) |
| حزمة النشر | `scripts/vps/make-deploy-bundle.ps1` (جديد) | أرشيف `artifacts/vps/hrm-deploy-<tag>.tar.gz` فيه: ملفات compose اللازمة، و`deploy/vps/*`، و`docker/init-databases.sql`، و`docker/init-service-roles.sh`، و`scripts/backup-postgres.sh` و`scripts/lib/`، و`.env.vps.example`. **لا يحوي `.env` ولا أسراراً** |
| النشر | `scripts/vps/deploy.ps1` (جديد) | من Windows: `scp` الحزمة، ثم عبر `ssh`: فك الحزمة في `/opt/hrm/releases/<tag>`، وربط `current`، و`docker compose pull`، و`up -d --remove-orphans`، والانتظار حتى `healthy`، ثم `smoke.sh`. يحفظ الوسم السابق للتراجع (`-Rollback`) |
| النشر على الخادم | `deploy/vps/remote-deploy.sh` (جديد) | السلسلة نفسها، ويستدعيها `deploy.yml` أيضاً بدل الأوامر المضمنة، فيبقى للنشر مسار واحد |
| تهيئة المضيف | `deploy/vps/host-setup.sh` (جديد، يُشغَّل مرة كـ root) | Docker Engine وCompose من مستودع Docker الرسمي؛ وswap بحجم 8 GiB و`vm.swappiness=10`؛ و`/etc/docker/daemon.json` بتدوير السجلات (`max-size=10m`، `max-file=3`)؛ و`ufw default deny incoming` مع 22 و443؛ وunattended-upgrades؛ و`/opt/hrm` بصلاحية 750 |
| مثال البيئة | `deploy/vps/.env.vps.example` | كل المتغيرات المطلوبة مع شرح عربي: `APP_DOMAIN`، `SSO_DOMAIN`، `ACME_EMAIL`، `ADMIN_ALLOWED_CIDRS`، `HRM_IMAGE_REPO`، `IMAGE_TAG`، `GATEWAY_CALLER_PROOF_SECRET`، `HRM_DEPLOYMENT_MODE=demo`، `COMPOSE_PROFILES=full-stack`، `HRM_ORGANIZATION_NAME`، `DEMO_USERS_PASSWORD`، والأسرار الحالية. توليدها آلياً ← PLAN-38 |
| `deploy-organization.ps1` | `scripts/deploy-organization.ps1` | يصبح غلافاً يضبط `HRM_ORGANIZATION_NAME` ثم يستدعي `scripts/vps/deploy.ps1`. ويحذف `HR_LEGAL_REGIME`/`HR_LICENSE_*` غير المقروءة، مع رسالة توجّه إلى شاشة الأطر القانونية |
| `realm` العرض | `scripts/prepare-keycloak-import.ps1` | ملف تعريف جديد `VpsDemo`: يضيف `https://${APP_DOMAIN}/signin-oidc` و`/signout-callback-oidc` وأصل الويب إلى `hr-gateway-client`، ويأخذ كلمة مرور المستخدمين التجريبيين من `DEMO_USERS_PASSWORD` (إلزامي، ≥ 16 حرفاً). يكتب الناتج إلى `artifacts/vps/import/` خارج git، وتنقله حزمة النشر إلى volume الاستيراد. الـ realm الإنتاجي الكامل ← PLAN-39 |
| وضع العرض | `docker-compose.vps-demo.yml` (جديد) | سلاسل الاتصال الـ15 بـ `Username=${POSTGRES_USER}`، و`Database__RunMigrationsAtStartup=true`، و`RabbitMQ__Username/Password` بالحساب الإداري، و`Seed__Profile=Demo`، و`Deployment__Banner=demo`. يُستبعد آلياً عند `HRM_DEPLOYMENT_MODE` ≠ `demo` |
| حلقات Data Protection | `docker-compose.production.yml` | `DataProtection__KeyRingPath=/var/hrm/dpkeys` وvolume مسمّى `dp_<service>` لكل خدمة، **ربطاً فقط** كي لا يكسر إعادة النشر بيانات العرض. الحارس والتشفير والنسخ والتحقق ← PLAN-39 |

**القواعد:**
1. الخادم لا يحتاج .NET SDK ولا git ولا الشيفرة. يحتاج Docker والحزمة و`.env` (صلاحية 600).
2. الوسم الثابت `<sha12>` فقط. `latest` مرفوض في `build-push-images.ps1` وفي `lint-compose.ps1`.
3. `.env` لا يُنسخ من جهاز المالك آلياً. `deploy.ps1` يتحقق من وجوده على الخادم ويرفض النشر إن نقصه متغير من `.env.vps.example`.
4. وضع العرض يرفض الإقلاع إن ضُبط `Seed__Profile=None` مع `HRM_DEPLOYMENT_MODE=demo` (تناقض)، وتظهر لافتته في رأس البوابة وHR.Core.

### 7. الترخيص والهوية المؤسسية (G-OPS-15)

**المشكلة:**
- الترخيص يُطبَّق قبل `/health` ([gateway Program.cs:196,252](../../src/Gateway/HRManagement.Gateway/Program.cs))، ويحلل التاريخ بثقافة الخادم ([LicenseValidationMiddleware.cs:17-19](../../src/Shared/HRManagement.ServiceDefaults/LicenseValidationMiddleware.cs)).
- اسم الجهة الافتراضي مشوّه الترميز ([gateway appsettings.json:104-108](../../src/Gateway/HRManagement.Gateway/appsettings.json)).

**التصميم:**
- `LicenseValidationMiddleware` يعفي `/health`، و`/alive`، و`/ready`، و`/metrics`، ويحلل `LicenseExpiresUtc` بـ `CultureInfo.InvariantCulture` و`DateTimeStyles.AdjustToUniversal | AssumeUniversal`. وتعليق «employee cap» يُحذف أو يُنفَّذ: يُحذف، وسقف الموظفين ← الموجة 2.
- إصلاح ترميز `Organization:Name` و`ShortName` في `appsettings.json` للبوابة إلى «شعبة التنظيم والموارد البشرية» و«الموارد البشرية».
- `Organization__Name: ${HRM_ORGANIZATION_NAME:-…}` للبوابة وHR.Core في `docker-compose.production.yml`.
- `Deployment__RequireLicenseKey: ${HRM_REQUIRE_LICENSE:-false}`.

### 8. الاختبار الذاتي وفحص التركيب

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `lint-compose.ps1` | `scripts/vps/lint-compose.ps1` (جديد) | يشغّل `docker compose … config --format json` لسلسلتي «الإنتاج» و«الخادم الواحد»، ويفشل على قواعد §1 وقواعد §3 و`latest`، وعلى أي `ports` غير Caddy وSeq الحلقي |
| `smoke.ps1` / `smoke.sh` | `scripts/vps/smoke.ps1` (جديد) و`deploy/vps/smoke.sh` (جديد) | يفحص: صحة كل الحاويات، وصلاحية شهادة TLS للمضيفين، وتحويل `/` إلى `https://${SSO_DOMAIN}`، ورد 404/403 لـ `/health` و`/admin` من الخارج، ورد 200 لـ `/ui/<slug>/` بعد الدخول، وإغلاق المنافذ 5000 و8080 و5432 و5672 و15672 و5341. ويحسب الأيام الباقية حتى `nextDue` في `legislative-watch-stamp.json` ويحذّر تحت 30 يوماً. ويخرج JSON لـ `EvidenceRecorder` |
| فحوص PLAN-37 المؤجلة | `smoke.ps1 -RequireIntegrationWiring` | قائمة فحوص لا تُشترط هنا وتُبلَّغ `Deferred(PLAN-37)`: `Integration__PayrollBaseUrl`/`FileStorageBaseUrl` وأنطقة HR.Core، و`Integration__HrCoreBaseUrl` لـ Contracts وIDCard وArchive وIT، وتوليد رمز عبر S2S، ورفع مستند مرشح إلى File.Storage، وأسرار `SVC_*` لكل خدمة. يقلب PLAN-37 المفتاح إلى إلزامي |

## Out of scope

- عناوين `Integration__*` و`ServiceIdentity__Scope` وأسرار S2S لكل خدمة في compose وk8s، وتغليف `service-regimes.json` ← PLAN-37. الاختبار الذاتي هنا يبلّغ عنها فقط (§8).
- الترحيل بدور `*_migrator`، وأدوار PG بكلمات مرور مستقلة، ومستخدمو `mq_*` وصلاحياتهم، وإضافة المهل المؤجلة في RabbitMQ، و`hostname` الوسيط وvolume له، ومجمعات الاتصال، وتوليد الأسرار، وإزالة القيم الوهمية من ملفات الإعداد ← PLAN-38.
- الـ realm الإنتاجي الكامل (العملاء الـ15، والنطاقات، وأسرار لكل عميل، و`sslRequired=external`)، وحارس حلقات Data Protection وتشفيرها ← PLAN-39.
- تصميم إثبات البوابة غير القابل للتزوير (مفتاح لكل خدمة، منع الإعادة) ← PLAN-42. هنا يُحقن سر مشترك حقيقي فقط.
- **RLS المفروض** بأدوار التطبيق، وأثره على مهام الخلفية وبوابة `/careers` والمراجع الوطنية من جلسة جهة أخرى ← الموجة 2 (OPS-D)، مع PLAN-48 لرؤية الصفوف الوطنية على مستوى EF. لذلك يبقى الخادم في «وضع العرض» (D-36-1).
- النسخ الاحتياطي المجدول والاستعادة ← الموجة 2 (OPS-E). ملف تعريف `backup` لا يُفعَّل على خادم العرض.
- k8s وTerraform وDR ← خارج مسار الخادم الواحد.
- قياس الأداء بأحمال k6 ← الموجة 2 (OPS-I).

## Acceptance scenarios

تُشغَّل على ملف «الخادم الواحد محلياً» (`docker-compose.vps-local.yml`): Caddy بـ `tls internal`، ومضيفان `hr.hrm.test` و`sso.hrm.test` يُحلّان إلى `127.0.0.1` داخل الاختبار (`SocketsHttpHandler.ConnectCallback`)، وسجل محلي `registry:2` على `localhost:5001` لإثبات مسار السحب. ويعيد المالك تشغيلها على الخادم الحقيقي بـ `VPS_TARGET_IP` و`VPS_APP_DOMAIN`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-OPS-36a | `deploy.ps1 -Tag <sha12>` يسحب الصور من السجل ويُقلع 15 تطبيقاً وPostgreSQL وRabbitMQ وKeycloak وClamAV وSeq وCaddy `healthy` خلال 10 دقائق، دون ملف المراقبة الكاملة | `docker compose … config` بلا `HRM_IMAGE_REPO` أو بوسم `latest` ← فشل صريح قبل أي سحب |
| AC-OPS-36b | دخول بمستخدم تجريبي عبر `https://hr…` يعيد التوجيه إلى `https://sso…` ثم يعود؛ والصفحة الرئيسية تعرض 14 وحدة؛ و`/ui/<slug>/` لكل وحدة 200 | لا تحويل إلى `localhost` في أي `Location`؛ والطلب عبر HTTP صريح لا يعطي جلسة |
| AC-OPS-36c | 443 مفتوح بشهادة صالحة لاسمَي المضيف | 5000 و8080 و5432 و5672 و15672 و5341 مغلقة من الخارج؛ و`/admin` و`/realms/master` من IP غير مسموح ← 403؛ و`/metrics` و`/health/ready` ← 404 |
| AC-OPS-36d | سجل تدقيق البوابة يحمل IP المتصل كما رآه Caddy | `X-Forwarded-For: 1.2.3.4` مزوّر من العميل لا يظهر عنواناً للمتصل |
| AC-OPS-36e | مستخدمان مصادَقان من IP واحد يطلبان 400 أصل و50 صفحة في دقيقة ← لا 429 | 400 طلب مجهول من IP واحد في دقيقة ← 429 بعد 300 مع `Retry-After` |
| AC-OPS-36f | تركيب صحيح يُقلع | `AllowedHosts=YOUR_MODULE_HOST` لـ `hr-core-web` ← خروج الحاوية برسالة تسمّي المفتاح؛ و`GATEWAY_CALLER_PROOF_SECRET` بطول 10 ← خروج البوابة |
| AC-OPS-36g | رفع ملف عبر البوابة إلى `/api/files` ← 201؛ وطلب `/ui/hr-core/` عبر البوابة ← 200 | طلب من حاوية على شبكة `application` إلى `hr-core-web:8080/` دون إثبات ← 403 |
| AC-OPS-36h | بعد الاختبار الذاتي وجولة 10 دقائق: لا `OOMKilled`، والاستخدام أقل من سقف كل حاوية، وتُسجَّل الأرقام | — |
| AC-OPS-36i | Seq يستقبل سجلات كل الخدمات الـ15 (`/api/events` عبر المنفذ الحلقي) | لا حاوية `otel-collector` ولا `prometheus`، ولا خطأ اتصال OTLP في السجلات |
| AC-OPS-36j | إعادة النشر بوسم جديد ثم `--force-recreate` للبوابة: الجلسة القائمة صالحة، وبيانات العرض تُقرأ | `deploy.ps1 -Rollback` يعيد الوسم السابق، والاختبار الذاتي أخضر |
| AC-OPS-36k | رأس البوابة يعرض `HRM_ORGANIZATION_NAME` بالعربية ولافتة «بيئة عرض» | ترخيص منتهٍ ← الواجهة 402 و`/health/live` 200 |

## Verification

```powershell
dotnet build HRManagement.slnx -warnaserror
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Ops036|FullyQualifiedName~GatewayRateLimit|FullyQualifiedName~ProductionConfigurationGuard|FullyQualifiedName~LicenseValidation"
pwsh scripts/vps/lint-compose.ps1   # سلسلتا الإنتاج والخادم الواحد
# بناء ودفع إلى سجل محلي ثم إقلاع ملف الخادم الواحد محلياً
docker run -d -p 5001:5000 --name hrm-registry registry:2
pwsh scripts/vps/build-push-images.ps1 -Registry localhost:5001/hrmanagement
pwsh scripts/vps/deploy.ps1 -Local -EnvFile deploy/vps/.env.vps-local -Tag (git rev-parse --short=12 HEAD)
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
$env:VPS_APP_DOMAIN = "hr.hrm.test"; $env:VPS_SSO_DOMAIN = "sso.hrm.test"; $env:VPS_TARGET_IP = "127.0.0.1"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcOps36"
# على الخادم الحقيقي (المالك): الأمر نفسه بـ VPS_TARGET_IP وVPS_APP_DOMAIN الفعليين، والدليل في acceptance-evidence/AC-OPS-36/vps/
```

## Definition of done

- سلسلة الإنتاج صالحة دون ملف المراقبة، و`lint-compose.ps1` أخضر للسلسلتين.
- لا قيمة وهمية تُقلع بها خدمة في Production، والحارس مثبت باختبار وحدة ونقيض حي.
- الخادم الواحد يُقلع من السجل بأمر واحد، ولا ينشر إلا 443.
- الدخول يعمل عبر TLS والمضيف العام. والمحدِّد يقسم المصادَقين حسب المستخدم ويُعفي الأصول.
- AC-OPS-36a..k = Pass محلياً مع `acceptance-evidence/AC-OPS-36/`، ونسخة الخادم الحقيقي إن شغّلها المالك في `acceptance-evidence/AC-OPS-36/vps/`.
- تحديث سطر «الحالة» في G-OPS-01 وG-OPS-10 (الجزء المغطى) وG-OPS-15 في الدراسة. وتحديث [PRODUCTION_RUNBOOK.md:15](../PRODUCTION_RUNBOOK.md) و[PILOT_DEPLOYMENT.md](../PILOT_DEPLOYMENT.md).

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-36-ops-production-boot-single-host.md` |
| Compose | `docker-compose.production.yml` (معدل)، `docker-compose.observability.production.yml`، `docker-compose.vps.yml`، `docker-compose.vps-demo.yml`، `docker-compose.vps-local.yml`، `docker-compose.images.yml` (معدل) |
| Edge/Host | `deploy/vps/Caddyfile`، `deploy/vps/clamav/clamd.conf`، `deploy/vps/host-setup.sh`، `deploy/vps/remote-deploy.sh`، `deploy/vps/smoke.sh`، `deploy/vps/.env.vps.example`، `deploy/vps/.env.vps-local` (قيم مختبر فقط) |
| Scripts | `scripts/vps/build-push-images.ps1`، `make-deploy-bundle.ps1`، `deploy.ps1`، `smoke.ps1`، `lint-compose.ps1`؛ `scripts/deploy-organization.ps1` (غلاف)؛ `scripts/prepare-keycloak-import.ps1` (`VpsDemo`) |
| Code | `ProductionConfigurationGuard.cs` (+Options)، `GatewayRateLimitPartitioner.cs`، `Program.cs` للبوابة، `LicenseValidationMiddleware.cs`، `File.Storage.Web/appsettings.Production.json`، إصلاح ترميز `appsettings.json` للبوابة |
| CI | `.github/workflows/deploy.yml` (`--profile full-stack` و`remote-deploy.sh`) |
| Unit | `Ops036ProductionConfigurationGuardTests.cs`، `GatewayRateLimitPartitionerTests.cs`، `LicenseValidationMiddlewareTests.cs`، `Ops036ComposeLintTests.cs` (نصي: ملف الإنتاج بلا خدمات مراقبة بلا `image`، وتجاوزات البوابة موجودة) |
| Acceptance | `src/Tests/HRManagement.AcceptanceTests/AcOps36VpsBootTests.cs` (`[RequiredAcceptance]`، `EvidenceRecorder("AC-OPS-36")`، `AcceptanceGate`) |
| Docs | `docs/OPS_VPS_SINGLE_HOST.md` (جديد: تهيئة المضيف، DNS، النشر، التراجع، الأنفاق)؛ تحديث `PRODUCTION_RUNBOOK.md` و`PILOT_DEPLOYMENT.md` و`KEYCLOAK_AND_SECRETS.md` (قسم الحافة)؛ أسطر «الحالة» في الدراسة |

## Risks

| الخطر | التخفيف |
|-------|---------|
| Let's Encrypt يرفض أسماء `sslip.io` أو يحدّها (يحتاج تحقق) | نطاق المالك هو الافتراض الموصى به؛ و`tls internal` للاختبار المحلي |
| Keycloak 24 لا يقبل `KC_PROXY_HEADERS` مع خيارات hostname v1 كما هو متوقع (يحتاج تحقق) | اختبار AC-OPS-36b يسقط أولاً؛ والبديل الإبقاء على `KC_PROXY=edge` المهجور مع تحذير موثق |
| ClamAV لا يقرأ `clamd.conf` المركّب (يحتاج تحقق) | AC-OPS-36h يرصد OOM؛ والبديل رفع السقف إلى 2.5 GiB وخفض حاويات خفيفة إلى 320 MiB |
| الترحيلات المتسلسلة عند الإقلاع تتجاوز مهلة الصحة على 4 أنوية | `start_period: 240s`، وقياس الزمن في AC-OPS-36a |
| `legislative-watch-stamp.json` يوقف الإقلاع في Production بعد `nextDue=2027-01-31` ([LegalControlGuard.cs:122-131](../../src/Shared/HRManagement.ServiceDefaults/LegalControl/LegalControlGuard.cs)) | الاختبار الذاتي يحذّر قبل 30 يوماً؛ والتجديد إجراء حوكمة موثق في `OPS_VPS_SINGLE_HOST.md` لا يُعطَّل |
| وضع العرض (حساب إداري للقاعدة والوسيط) يُستخدم ببيانات حقيقية خطأً | لافتة دائمة، و`HRM_DEPLOYMENT_MODE=demo` مكتوب في الدليل والتقرير، والحارس يرفض `Seed__Profile=None` في وضع العرض |
| تغيير ترتيب المحدِّد يفتح طريقاً لطلبات مجهولة قبل المصادقة | الحد المجهول يبقى، واختبار AC-OPS-36e السالب |

## Effort estimate

9–10 أيام عمل:
- المرحلة 0: 0.5
- التركيب وتجاوزات البوابة وFile.Storage: 1.5
- الحافة وKeycloak والترويسات: 1.5
- الذاكرة وSeq والتخزين المحلي: 1
- المحدِّد: 1
- الحارس والترخيص والهوية: 1
- السجل والحزمة والنشر والمضيف: 1.5
- القبول والأدلة والوثائق: 1.5

## ملاحظات التنفيذ والانحرافات (2026-10-31)

| الموضوع | الخطة | التنفيذ والسبب |
|---------|-------|----------------|
| المزوّد والخادم | Hostinger KVM 4 (4 vCPU، 16 GB) | **Contabo**: 8 vCPU (AMD EPYC)، **23.46 GiB**، 152 GB حرة، **Ubuntu 26.04.1** (لا 24.04)؛ جدول الذاكرة طُبّق كما هو |
| مجموع سقوف الذاكرة | ≈ 13.1 GiB («تسع خدمات» بـ 384 MiB) | **13.5 GiB**: الخدمات الصغيرة عشر لا تسع. الاستخدام الفعلي 21.6 % من الخادم (5.2 GiB) بعد جولة 10 دقائق |
| السجل | GHCR خاص | فشل الدفع إلى GHCR على الشبكة ثلاث مرات (مهلة ثم DNS) واتصال المالك يرفع بنحو 0.16 MB/s؛ استُخدم **السجل الحلقي على الخادم** (P-10، `127.0.0.1:5050`)، و**بنى الخادم الصور** من أرشيف المصدر (~30 MB) بقرار المالك: انحراف عن §6 القاعدة 1، ويُحذف المصدر وذاكرة البناء وصورة SDK بعد كل بناء (`deploy/vps/build-on-host.sh`). النشر يبقى سحباً من سجل |
| الأسماء | نطاق المالك | لا نطاق: `hr.161-97-185-71.sslip.io` و`sso.161-97-185-71.sslip.io` |
| الشهادات | Let's Encrypt عبر TLS-ALPN | staging أولاً ثم **production** (YE1/YE2، حتى 2027-01-29)؛ المنفذ 80 مغلق وتحدي HTTP معطل |
| المستخدم وSSH | — | المستخدم `hrm` بلا كلمة مرور، SSH بالمفتاح فقط، `PermitRootLogin no` عبر `00-hrm-hardening.conf`، و`ufw` على 22 و443 |
| مفاتيح MinIO وصورتها | `:?` | `${VAR:-}`: compose يقيّم الخدمات غير المفعّلة أيضاً، فـ `:?` يفرض أسراراً غير مستخدمة. الحماية: لا قيمة افتراضية، و`lint-compose.ps1`، والحارس يرفض File.Storage على Minio بمفاتيح فارغة |
| volumes التطبيقات | — | حاوية `volume-init` لمرة واحدة تمنح ملكية volumes للمستخدم 1654 (عيب PLAN-35 رقم 5) |
| HR.Core وvolume الملفات | ربط `file_storage_data` | لا ربط: HR.Core لا يسجّل خدمة التخزين؛ File.Storage وحده يملك الملفات |
| Keycloak | `KC_PROXY_HEADERS` | يعمل في 24.0؛ وأُضيف `KC_HOSTNAME_PORT=-1` (الملف الأساسي يضبط 8088 فحمل التحويل `:8088`) والمدير الأولي إلزامي |
| حارس الإعداد | يعمل بالمفتاح خارج Production | لا يمكن تعطيله في Production (`EnforceConfigGuard=false` يُتجاهل هناك) |
| فحص المنافذ من الخارج | `Test-NetConnection` | طريق المالك (VPN/وكيل) يجيب كل منفذ؛ أُضيف منفذ تحكم (23456)، والحكم عند الاعتراض لفحص الخادم (`ss`، `ufw`، tcpdump) |
| AC-OPS-36e على الخادم | 400 أصل في دقيقة | طلبات الأصول بـ HEAD (المسار نفسه في المحدِّد دون أجسام عبر رابط بطيء)، والدفعة المجهولة بتوازٍ 8 كي تقع في نافذة واحدة |

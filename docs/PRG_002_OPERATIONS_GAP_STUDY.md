# دراسة الجاهزية التشغيلية للبيئة الاصطناعية ولتجربة جهة واحدة (OPS) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** النشر (Compose الإنتاجي، `deploy.yml`، الصور والسجل، الإعداد لكل بيئة، Keycloak الإنتاجي، الأسرار، البوابة كسطح وحيد، الترخيص والهوية المؤسسية، خادم VPS واحد 4 vCPU/16 GB)؛ النسخ والاستعادة وDR؛ موثوقية الرسائل (Outbox/Inbox/DLQ، AC-15..18)؛ المصالحة وعزل قواعد البيانات (AC-19/20)؛ الجانب التشغيلي لـ DSAR وفحص الملفات ومنح الوصول وثقة الخدمات (AC-27..30)؛ المراقبة والتنبيه وأدلة التشغيل؛ استيراد البيانات وتهيئة الجهة (AC-07/08)؛ نموذج الجهة الواحدة (TEN-001..003)؛ الأداء والسعة (AC-35)؛ ومطابقة كل ذلك مع البوابتين G1 وG2.  
**المنهج:** قراءة ثابتة للشيفرة وملفات Compose وappsettings والسكربتات والاختبارات والأدلة في `main` @ `f9eab1a`، ومقارنتها بما تدّعيه البطاقات والوثائق. قُورن مكدّس القبول (الذي شُغّل فعلاً) بمكدّس الإنتاج (الذي لم يُشغَّل) مفتاحاً بمفتاح.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل أي حاوية (لا .NET SDK ولا Docker على الجهاز). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات من سلوك موثَّق للمكتبات (ASP.NET Core، MassTransit، PostgreSQL، RabbitMQ) تحتاج تشغيلاً حياً لتأكيدها.  
**الوثائق المقارَنة:** [00-MASTER-PLAN](completion-backlog/00-MASTER-PLAN.md) · [01-P0](completion-backlog/01-P0-RELEASE-BLOCKERS.md) · [02-P1](completion-backlog/02-P1-PLATFORM-INTEGRITY.md) · [03-P2](completion-backlog/03-P2-SCALE-AND-RELIABILITY.md) · [PLAN-05](acceptance-plans/PLAN-05-messaging-reliability.md) · [PLAN-08](acceptance-plans/PLAN-08-onboarding-quarantine.md) · [PLAN-09](acceptance-plans/PLAN-09-reconciliation-db-isolation.md) · [PLAN-11](acceptance-plans/PLAN-11-file-dsar-s2s.md) · [PLAN-13](acceptance-plans/PLAN-13-ops-quality.md) · [PRODUCTION_RUNBOOK](PRODUCTION_RUNBOOK.md) · [PILOT_DEPLOYMENT](PILOT_DEPLOYMENT.md) · [KEYCLOAK_AND_SECRETS](KEYCLOAK_AND_SECRETS.md) · [EVENT_PUBLISHER_INVENTORY](EVENT_PUBLISHER_INVENTORY.md) · [OPS_BACKUP_VERIFICATION](OPS_BACKUP_VERIFICATION.md) · [OPS_DR_BIA_RTO_RPO](OPS_DR_BIA_RTO_RPO.md) · [NFR_CAPACITY_MODEL](NFR_CAPACITY_MODEL.md) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md)

---

## 1. الخلاصة

1. **مكدّس الإنتاج لم يُقلع قط.** كل الأدلة الحية جاءت من مكدّس القبول (`ASPNETCORE_ENVIRONMENT=Acceptance`، مستخدم قاعدة بيانات superuser، مستخدم RabbitMQ إداري، مفاتيح Data Protection على volume). حتى الرحلة «الشبيهة بالإنتاج» لا تحمّل `appsettings.Production.json` ([docker-compose.acceptance-prodlike.yml:10-12](../docker-compose.acceptance-prodlike.yml)). البطاقات DEP-001 وSEC-001 وSEC-004 وIAM-001 وINT-001 وOPS-003/004 وNFR-001 موسومة Done، لكن الشيفرة تناقضها.
2. **سبعة عوائق P0:**
   - **(أ)** أمر النشر في `deploy.yml` لا يشغّل أي خدمة تطبيق، والملف الإنتاجي لا يعمل دون حزمة المراقبة، و14 ملف `appsettings.Production.json` تحمل `AllowedHosts` وهمياً، والبوابة ترفض متصفحها نفسه بإثبات المستدعي، وصورة MinIO المثبتة لم تعد قابلة للسحب (G-OPS-01).
   - **(ب)** لا مسار إقلاع أول: الترحيلات معطلة عند الإقلاع ولا يمكن تشغيلها من خط النشر، ومستخدمو RabbitMQ وMinIO ودور النسخ لا تُنشأ آلياً (G-OPS-02).
   - **(ج)** Keycloak الإنتاجي يدوي: لا استيراد realm، ولا قالب إنتاجي، والبوابة ترث `PublicAuthority=http://localhost:8088` (G-OPS-03).
   - **(د)** مفاتيح Data Protection لا تُحفظ إلا للبوابة، والرقم الوطني مشفّر بها. أول إعادة إنشاء لحاوية HR.Core تجعل كل سجلات الموظفين غير قابلة للقراءة (G-OPS-04).
   - **(هـ)** الوضع الإنتاجي لقاعدة البيانات (أدوار `*_app` + RLS مفروض) لم يُختبر قط. 123 تجاوزاً لمرشح الجهة على مستوى EF لا تتجاوز RLS (G-OPS-05).
   - **(و)** النسخ الاحتياطي يفشل بصمت، ودليل AC-34 «Pass» عقد سكربت من أغسطس (G-OPS-06).
   - **(ز)** استيراد الموظفين لا يُدخل موظفاً واحداً، واستيراد الهيكل مسطّح بلا آباء (G-OPS-07).
3. **ثغرات سلامة (P1):**
   - أحداث تُفقد بصمت: ترفيع وندب ونتائج اعتماد الأصول والمزامنة الأولية للوحدات. و21 ناشراً بنمط «Save → Publish → Save» غير الذري (G-OPS-08).
   - مصالحة لا تكشف الفقد بين الخدمات (G-OPS-09).
   - حافة وسعة غير ملائمة لخادم واحد (G-OPS-10).
   - مراقبة لا تغطي 9 من 15 خدمة ولا ترسل لإنسان (G-OPS-11).
   - نموذج الجهة الواحدة غير محسوم (G-OPS-12).
   - أسرار وهمية مُلتزمة في المستودع (G-OPS-13).
4. **فجوات قدرة (P2/P3):** الأداء غير مقاس على أي عتاد (G-OPS-14)، والترخيص شكلي والهوية المؤسسية مشوّهة الترميز (G-OPS-15).
5. **فجوة تحقق:** من 14 سيناريو قبول في نطاق هذه الدراسة (AC-07/08، 15..20، 27..30، 34، 35) لم يُشغَّل 11 قط، ولم يتجاوز الثلاثة الباقية NoResult قديماً (AC-16 وAC-30، أغسطس) أو عقد سكربت (AC-34). لا يوجد سيناريو واحد ناجح حياً. اختبارات SEC-001 «فحص المنافذ» تعابير نمطية على نص YAML (G-OPS-16).

### 1.1 الوضع مقابل البوابتين

| البوابة | الشرط | الحالة | الدليل |
|---|---|---|---|
| G1 | إغلاق P0 | ❌ | بطاقات P0 كلها «Done» في [01-P0](completion-backlog/01-P0-RELEASE-BLOCKERS.md)، لكن G-OPS-01/03/04/08/13 تنقض DEP-001 وIAM-001 وSEC-004 وINT-001 |
| G1 | CI صادق | ❌ (→ QA) | الـ CI البعيد لم يُشغَّل على عمل PLAN-25..33 ([plan25-final-report.md:4-6](../reports/plan25-final-report.md))؛ AC-34 «Pass» دون تشغيل؛ مكدّس القبول superuser ([.env.ci:2](../.env.ci)) |
| G1 | auth وdeployment من الصفر | ❌ | G-OPS-01..05 |
| G1 | خط أساس زمني معتمد | ❌ | `PGM-001` غير مغلق ولا تاريخ إطلاق ([00-MASTER-PLAN.md:141](completion-backlog/00-MASTER-PLAN.md))؛ وخط الأساس الأدائي قالب فقط (G-OPS-14) |
| G2 | tenant وعزل الكائنات | ⚠️ جزئي | AC-01..05 Pass لكن على اتصال superuser يتجاوز RLS (G-OPS-05)؛ BOLA → IAM |
| G2 | maker/checker | ❌ (→ IAM/WF) | AC-12 Blocked؛ AC-11 Pass وحده |
| G2 | رواتب قابلة للإعادة | ❌ (→ PAY/QA) | AC-21..25 لم تُشغَّل |
| G2 | restore ناجح | ❌ | G-OPS-06؛ مفاتيح التشفير خارج النسخ (G-OPS-04) |
| G2 (ضمني) | تحميل بيانات الجهة | ❌ | G-OPS-07؛ الخطوة 2 في [PILOT_DEPLOYMENT.md:12](PILOT_DEPLOYMENT.md) |

**التقدير الإجمالي للإكمال:** نحو 58–74 يوم عمل لمطوّر واحد. منها 25–31 يوماً للوصول إلى G1 على خادم واحد (OPS-A..D)، و21–27 يوماً إضافياً لـ G2 (OPS-E..G)، والباقي للمراقبة والأداء والتحقق (OPS-H، OPS-I).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | بناء الصور ونشرها | `deploy.yml`: وسم SHA، Trivy، SBOM، توقيع cosign وتحقق قبل النشر | ✅ مع ملاحظة: يُعاد البناء عند الوسم ولا يُرقّى digest المختبَر |
| 2 | تركيب Compose الإنتاجي | `docker-compose.yml` + services + observability + images + production | ❌ لا يشغّل التطبيقات ولا يُقلع (G-OPS-01) |
| 3 | الإقلاع الأول للبيانات | ترحيلات معطلة عند الإقلاع؛ `run-migrations.sh` من GitHub runner؛ أدوار PG في init-script | ❌ (G-OPS-02) |
| 4 | الهوية وTLS | `command: start` بلا استيراد؛ قالب dev فقط؛ لا TLS في المكدّس | ❌ (G-OPS-03، G-OPS-10) |
| 5 | المفاتيح والأسرار | `:?required` لمعظم الأسرار؛ DP key ring للبوابة فقط؛ سر إثبات المستدعي وهمي | ❌ (G-OPS-04، G-OPS-13) |
| 6 | الوضع الإنتاجي للقاعدة | أدوار `*_app` بلا BYPASSRLS + FORCE RLS | ❌ لم يُشغَّل (G-OPS-05) |
| 7 | الرسائل | Outbox على 8 خدمات، Inbox على 13 DbContext، retry، `_error` | ⚠️ أحداث مفقودة ونشر غير ذري (G-OPS-08) |
| 8 | المصالحة والعزل | مصالحة قراءة Analytics ذاتية؛ REVOKE CONNECT بين القواعد | ⚠️ (G-OPS-09) |
| 9 | الملفات وDSAR وS2S | ClamAV fail-closed، منح بمهلة، جامع DSAR عبر S2S | ⚠️ الشيفرة سليمة؛ التشغيل غير مثبت (G-OPS-05، G-OPS-10، AC-27..30) |
| 10 | المراقبة والتنبيه | Prometheus/Alertmanager/Seq/Tempo/Grafana + 14 قاعدة | ⚠️ تغطية ناقصة ولا قناة بشرية (G-OPS-11) |
| 11 | النسخ والاستعادة وDR | `postgres-backup` يومي، سكربت تمرين القبول | ❌ (G-OPS-06) |
| 12 | الاستيراد والتهيئة | `DataImportController` CSV؛ `AgencyProvisioningService` | ❌ الاستيراد معطوب (G-OPS-07)؛ التهيئة غير مشغلة (G-OPS-12) |
| 13 | نموذج الجهة الواحدة | `TenantDefaults.DefaultTenantId` للبذور؛ GUID جديد لكل جهة مُهيّأة | ⚠️ غير محسوم (G-OPS-12، D-OPS-3) |
| 14 | الأداء والسعة | k6 + ملفات تعريف؛ `summary.json` قالب | ❌ غير مقاس (G-OPS-14) |
| 15 | الوثائق والأدلة | بطاقات Done؛ تحقق OPS كله `[ ]` | ❌ (G-OPS-16) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **تقسيم شبكي في Compose الإنتاجي:** شبكات `edge/application/data/observability` والثلاث الأخيرة `internal: true`، و`ports: !override []` لكل خدمة داخلية ([docker-compose.production.yml:632-643](../docker-compose.production.yml)).
- **إقفال الأسرار الأساسية:** صيغة `${VAR:?required}` لأسرار PG وRabbitMQ وMinIO وعملاء Keycloak و`SVC_*` وGrafana و`HR_CORE_OTP_HMAC_KEY`.
- **تقوية الحاويات:** `no-new-privileges` و`cap_drop: ALL` للتطبيقات، والصورة تعمل بمستخدم غير root ([DotnetService.Dockerfile:52](../docker/DotnetService.Dockerfile)).
- **أدوار قواعد بيانات لكل خدمة:** فصل `*_app` عن `*_migrator`، و`NOBYPASSRLS`، و`REVOKE CONNECT ... FROM PUBLIC`، وإلغاء الاتصال العابر ([init-service-roles.sh:20-78,96-102](../docker/init-service-roles.sh)).
- **سلسلة توريد:** توقيع cosign والتحقق منه قبل النشر والتراجع، وTrivy، وSBOM، وprovenance ([deploy.yml:194-232,326-331](../.github/workflows/deploy.yml)).
- **MassTransit مضبوط جيداً في أساسه:**
  - bus outbox على 8 خدمات؛
  - فلتر Inbox ضمن معاملة واحدة مع أثر المستهلك ([IdempotentConsumerFilter.cs:63-101](../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs))، ومسجَّل في 13 من 14 DbContext؛
  - publisher confirms، وquorum queues، ونقل `X-Correlation-ID` و`X-Tenant-ID`.
- **ثقة الوكلاء آمنة افتراضياً:** `ForwardedProxyTrust` لا يثق بالجميع، ويقصر الثقة على النطاقات الخاصة ([ForwardedProxyTrust.cs:24-51](../src/Shared/HRManagement.ServiceDefaults/ForwardedProxyTrust.cs)).
- **فحص الملفات fail-closed:**
  - `FileStorage__ClamAvRequired=true` ([docker-compose.production.yml:553-557](../docker-compose.production.yml))؛
  - طابور فحص بانتخاب قائد ([FileScanProcessorJob.cs:31-43](../src/Services/FileStorage/File.Storage.Infrastructure/Jobs/FileScanProcessorJob.cs))؛
  - منحة الوصول صار لها `ExpiresAtUtc` ([FileAccessGrant.cs:68](../src/Shared/HRManagement.SharedKernel/Files/FileAccessGrant.cs)).
- **جامع DSAR حقيقي** عبر S2S من HR.Core وPayroll وFile.Storage ([HttpDsarSubjectSources.cs:154-166](../src/Services/Compliance/Compliance.Infrastructure/Privacy/HttpDsarSubjectSources.cs))، ودليل HCM-06-DSAR ناجح (2026-10-29).
- **اختبارات قبول حقيقية جاهزة للتشغيل:** `Ac15`..`Ac20` و`Ac27`..`Ac30` و`Ac34`/`Ac35` تستدعي سكربتات توقف حاويات وتعدّ الإيصالات وأعماق الطوابير. مثال: AC-34 يأخذ dump لـ16 قاعدة ويستعيد 5 ويفحص المراجع اليتيمة ([Ac34BackupRestoreLiveTests.cs:22-38](../src/Tests/HRManagement.AcceptanceTests/Ac34BackupRestoreLiveTests.cs)).
- **فجوات PLAN-08 عولجت في الشيفرة:** إنشاء الوحدات ونشر `IAgencyOnboarded` وحارس الحجر `TenantQuarantineGuard`. لكنها لم تُشغَّل (AC-07/08).

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-OPS-01 — مكدّس الإنتاج وخط النشر لم يُقلعا قط
- **الدليل:** تسعة عوائق مستقلة، كل منها يكفي وحده لمنع تشغيل النظام:
  1. **لا تُشغَّل أي خدمة تطبيق.**
     - كل خدمات التطبيق خلف `profiles: ["full-stack"]` ([docker-compose.yml:100,147,183](../docker-compose.yml)، و13 مرة في `docker-compose.services.yml`).
     - أمر النشر يفعّل `--profile backup` فقط ([deploy.yml:347-364](../.github/workflows/deploy.yml))، وكذلك التراجع ([deploy.yml:421-434](../.github/workflows/deploy.yml)).
     - لا `profiles: !reset` في `docker-compose.images.yml` ولا في `docker-compose.production.yml`.
  2. **الملف الإنتاجي يتطلب حزمة المراقبة.**
     - يعرّف `seq` و`tempo` و`otel-collector` و`prometheus` و`grafana` والمصدّرات بتجاوزات بلا `image` ([docker-compose.production.yml:571-630](../docker-compose.production.yml)).
     - لذلك يفشل التحقق من التركيب دون `docker-compose.observability.yml`، ومنه أمر «Production (single-host)» في [PRODUCTION_RUNBOOK.md:15](PRODUCTION_RUNBOOK.md).
  3. **`AllowedHosts` وهمي.**
     - القيمة `"YOUR_MODULE_HOST"` في 13 ملف إنتاج للوحدات و`"YOUR_GATEWAY_HOST"` للبوابة ([gateway appsettings.Production.json:34](../src/Gateway/HRManagement.Gateway/appsettings.Production.json)، [HR.Core appsettings.Production.json:250](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)).
     - لا متغير بيئة يتجاوزها في أي compose، ولا ذكر لها في `.env.example` أو الوثائق.
     - مرشح المضيف الافتراضي في ASP.NET Core يرد 400 على أي `Host` آخر: `hr-core-web:8080` من YARP، و`localhost:8080` من healthcheck.
  4. **البوابة ترفض متصفحها.**
     - `GatewayCallerProofMiddleware` يُفعَّل عندما `HrAuth:BehindGateway` (افتراضه `true`) و`Enforce` ([GatewayCallerProofMiddleware.cs:16-17](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs)).
     - البوابة تستدعيه على نفسها ([ProductionHostExtensions.cs:81](../src/Shared/HRManagement.ServiceDefaults/ProductionHostExtensions.cs)، [gateway Program.cs:195](../src/Gateway/HRManagement.Gateway/Program.cs)).
     - مكدّس القبول يضبط `HrAuth__BehindGateway: "false"` للبوابة صراحةً ([docker-compose.acceptance.yml:212](../docker-compose.acceptance.yml))، والإنتاج لا يضبطه.
  5. **File.Storage يرفض البوابة.**
     - لا يوجد له `appsettings.Production.json` أصلاً، فلا سر لديه، و`Validate` يعيد `!enforce=false` ([GatewayCallerProof.cs:52-56](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProof.cs)).
     - النتيجة 403 لكل `/api/files` عبر البوابة.
  6. **البوابة ترث قيماً تطويرية.**
     - من [docker-compose.yml:199,208-209](../docker-compose.yml): `Keycloak__PublicAuthority=http://localhost:8088/...` و`Gateway__EnabledModules` = `hr-core` و`hr-contracts` فقط.
     - `docker-compose.production.yml` لا يتجاوز أياً منهما. فيُعاد توجيه المتصفح إلى `localhost:8088` ([KeycloakExtensions.cs:173-187](../src/Shared/HRManagement.ServiceDefaults/KeycloakExtensions.cs))، وتعرض البوابة وحدتين من 14 ([GatewayModuleCatalog.cs:52-56](../src/Gateway/HRManagement.Gateway/Services/GatewayModuleCatalog.cs)).
  7. **HR.Core يرفض كل توكن.**
     - إعداده الإنتاجي `RequireHttpsMetadata: true` ([HR.Core appsettings.Production.json:21](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)) مع `Keycloak__Authority=http://keycloak:8080/...` ([docker-compose.production.yml:22](../docker-compose.production.yml)).
     - الخدمات الأخرى تتجاوز القيمة إلى `false` عبر `docker-compose.services.yml`، أما HR.Core المعرّف في `docker-compose.yml` فلا. JwtBearer يرفض metadata غير HTTPS (يحتاج تحقق).
  8. **صورة MinIO غير قابلة للسحب.** `minio/minio:RELEASE.2025-04-22T22-12-26Z` لم يعد قابلاً للسحب ([plan30-final-report.md:297](../reports/plan30-final-report.md)، [docker-compose.acceptance.yml:29](../docker-compose.acceptance.yml)). مع ذلك ما زال مثبتاً للإنتاج ([docker-compose.yml:43](../docker-compose.yml)) ولـ k8s ([statefulset.yaml:74](../deploy/k8s/data/base/minio/statefulset.yaml)). الأمر نفسه محتمل لـ `minio/mc` ([docker-compose.production.yml:173](../docker-compose.production.yml)، يحتاج تحقق).
  9. **سكربت الخطوة 1 من خطة التجربة معطوب.**
     - [deploy-organization.ps1:6,16](../scripts/deploy-organization.ps1) يستدعي `docker/docker-compose.yml` وهو غير موجود، ويبني من المصدر (`--build`).
     - متغيرات `HR_ORG_NAME` و`HR_LEGAL_REGIME` و`HR_LICENSE_*` التي يضبطها لا يقرؤها أي ملف.
- **وعوائق مصاحبة:**
  - لا حافة TLS في أي compose، بينما `HrAuth:CookieSecureAlways=true` ([gateway appsettings.Production.json:18](../src/Gateway/HRManagement.Gateway/appsettings.Production.json)) و`KC_HOSTNAME_STRICT_HTTPS=true` ([docker-compose.production.yml:120](../docker-compose.production.yml)). فلا يعمل الدخول عبر HTTP.
  - بوابة الصحة في `deploy.yml` تعدّ أي 4xx من الوحدات نجاحاً ([deploy.yml:388](../.github/workflows/deploy.yml)).
  - التراجع يعلن «completed» دون فحص ([deploy.yml:435](../.github/workflows/deploy.yml)).
- **الأثر:** لا يمكن اليوم تشغيل النظام بإعدادات Production على أي خادم. ادعاءات DEP-001 («rollback مجرب»، «لا يحتاج الخادم source tree») وIAM-001 («login من عنوان الإنتاج دون localhost») غير صحيحة. والخادم يحتاج فعلاً `./scripts` و`./docker` المركّبة في الحاويات ([docker-compose.yml:15-16](../docker-compose.yml)، [docker-compose.production.yml:153-155](../docker-compose.production.yml)).
- **الإصلاح المقترح:**
  - ملف `docker-compose.vps.yml` مكتفٍ ذاتياً: يزيل profiles، ويجعل المراقبة اختيارية، ويضبط صراحةً كل قيمة ورثها الإنتاج من الأساس التطويري (`PublicAuthority` و`EnabledModules` و`BehindGateway` للبوابة و`RequireHttpsMetadata` و`AllowedHosts`)، ويضيف حافة TLS (Caddy أو nginx)؛
  - إضافة `appsettings.Production.json` لـ File.Storage؛
  - حارس بدء يرفض أي قيمة `YOUR_*` أو `USE_ENV_*` في Production؛
  - صورة MinIO بديلة قابلة للسحب أو سجل داخلي (D-OPS-1)؛
  - مهمة CI `prod-boot` تُقلع الملف الإنتاجي على runner وتختبر الدخول والصحة.
- **الخطة:** [PLAN-36](acceptance-plans/PLAN-36-ops-production-boot-single-host.md)
- **التحقق المطلوب:**
  - (موجب) على خادم نظيف 4 vCPU/16 GB: أمر واحد موثّق، فتصبح 15 خدمة `healthy`، ويُسجَّل دخول عبر `https://<host>` مع ظهور 14 وحدة.
  - (سالب) ترك `AllowedHosts=YOUR_*` أو سر إثبات المستدعي فارغاً يمنع الإقلاع برسالة صريحة. وطلب مباشر إلى `hr-core-web:8080` من شبكة `application` دون إثبات يعطي 403.

#### G-OPS-02 — لا مسار إقلاع أول قابل للتكرار للبيانات والوسيط
- **الدليل:**
  - **الترحيلات:**
    - معطلة عند الإقلاع في الإنتاج ([docker-compose.production.yml:9](../docker-compose.production.yml)، [DatabaseMigrationExtensions.cs:35-40](../src/Shared/HRManagement.ServiceDefaults/DatabaseMigrationExtensions.cs)).
    - مهمة `migrate` تعمل على `ubuntu-latest` في GitHub وتتصل بقواعد الإنتاج عبر أسرار ([deploy.yml:241-294](../.github/workflows/deploy.yml)). لكن PostgreSQL الإنتاجي بلا منفذ منشور وعلى شبكة `internal` ([docker-compose.production.yml:59-61](../docker-compose.production.yml)).
    - المهمة لا تمرر `ConnectionStrings__*Migrator`، فتسقط إلى اتصال التطبيق ([run-migrations.sh:106-107](../scripts/run-migrations.sh)). ودور `*_app` لا يملك `CREATE` على المخطط ([init-service-roles.sh:53](../docker/init-service-roles.sh)).
    - لا حاوية ترحيل في compose (يوجد `migrate-job.yaml` لـ k8s فقط).
  - **كلمات مرور الأدوار:**
    - حاوية postgres لا تستلم إلا `POSTGRES_APP_PASSWORD` ([docker-compose.yml:13](../docker-compose.yml)). لذلك ينشئ init-script كل الأدوار الثلاثين (15 `*_app` و15 `*_migrator`) بكلمة مرور واحدة ([init-service-roles.sh:6-15,104-149](../docker/init-service-roles.sh)).
    - سلاسل الاتصال تستخدم `POSTGRES_<SVC>_APP_PASSWORD` إن ضُبط ([docker-compose.production.yml:253](../docker-compose.production.yml)). ضبط كلمة مرور فريدة، كما توصي `.env.example`، يكسر الاتصال.
    - السكربت يعمل عند أول تهيئة فقط.
  - **RabbitMQ:**
    - التطبيقات تتصل بـ `mq_hr_core` وأشباهه ([docker-compose.production.yml:254](../docker-compose.production.yml)). لا شيء في compose ينشئهم: لا تحميل لـ `definitions-service-users.json` ولا تشغيل لـ [init-rabbitmq-service-users.sh](../scripts/init-rabbitmq-service-users.sh).
    - صلاحية `configure` محصورة في `^(HR\.Core|hr-core|MassTransit).*` ([init-rabbitmq-service-users.sh:30](../scripts/init-rabbitmq-service-users.sh))، بينما تبادلات الرسائل تُسمّى `HRManagement.MessageContracts.Events:*`، ولا محوّل أسماء مخصص. فالإعلان عن التبادل يُرفض ACCESS_REFUSED (يحتاج تحقق).
    - مكدّس القبول يستخدم المستخدم الإداري (`RABBITMQ_DEFAULT_USER=hrm_ci`، [.env.ci:5](../.env.ci))، فلم يُختبر هذا قط.
  - **MinIO ودور النسخ:** مستخدم `MINIO_FILESTORAGE_ACCESS_KEY` يتطلب [init-minio-filestorage-user.sh](../scripts/init-minio-filestorage-user.sh) يدوياً. ودور `hrm_backup` يتطلب [create-hrm-backup-role.sql](../scripts/sql/create-hrm-backup-role.sql) يدوياً.
  - **البذور الإلزامية:** فشلها في الإنتاج يُسجَّل تحذيراً ويستمر الإقلاع ([HR.Core Program.cs:294,316-318](../src/Services/HRCore/HR.Core.Web/Program.cs)). فقد تعمل الخدمة بلا تعريفات مسارات اعتماد دون أي إنذار.
- **الأثر:** على خادم جديد إما لا تُطبَّق الترحيلات فتفشل كل الخدمات، أو يرتجل المشغّل خطوات يدوية غير موثقة ولا مكررة. وفشل `HealthChecks__IncludeRabbitMq` يُبقي كل الخدمات غير جاهزة.
- **الإصلاح المقترح:**
  - صورة `hrm-migrator` (efbundles + psql) تعمل كخدمة `depends_on` لمرة واحدة داخل شبكة `data` باتصال `*_migrator`، ثم تطبّق `ten002c-harden-tenant-rls.sql`؛
  - تمرير كلمات المرور لكل خدمة إلى حاوية postgres، أو إلغاء الوعد بها؛
  - `definitions.json` لـ RabbitMQ يُحمَّل عند الإقلاع، بصلاحيات تشمل `HRManagement\.MessageContracts.*`؛
  - حاوية init لـ MinIO؛
  - جعل فشل البذور الإلزامية يُفشل الإقلاع في Production.
- **الخطة:** [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md)
- **التحقق المطلوب:**
  - (موجب) `docker volume rm` لكل البيانات ثم `up`: تكتمل الترحيلات وRLS، ويتصل كل تطبيق بدوره ومستخدمه، ويُنشر `IEmployeeCreated` ويُستهلك.
  - (سالب) حذف تعريف مسار إلزامي من البذور يمنع إقلاع HR.Core في Production.

#### G-OPS-03 — Keycloak الإنتاجي غير قابل لإعادة الإنتاج
- **الدليل:**
  - الإنتاج يستبدل `start-dev --import-realm` ([docker-compose.yml:78](../docker-compose.yml)) بـ `command: start` ([docker-compose.production.yml:105](../docker-compose.production.yml)). فلا يُستورد أي realm.
  - القوالب الموجودة تطويرية فقط:
    - `realm-hrmanagement-dev.template.json`: `"sslRequired": "none"` ([:5](../docker/keycloak/realm-hrmanagement-dev.template.json))، وredirect URIs على `localhost` فقط ([:403-412](../docker/keycloak/realm-hrmanagement-dev.template.json))، وسر واحد `__SVC_SECRET__` لكل عملاء الخدمات ([:501](../docker/keycloak/realm-hrmanagement-dev.template.json)).
  - القالبان يخلوان من عملاء `svc-hr-health` و`svc-hr-movements` و`svc-dev-analytics` و`svc-compliance`، ومن نطاقات `dsar.subject.read` و`payroll.grades.read` و`file-storage.service-upload`. مع ذلك يطلب الإنتاج أسرار العملاء الأربعة بـ `:?` ([docker-compose.production.yml:312,332,485,505](../docker-compose.production.yml)).
  - هذه العناصر لا تُنشأ إلا بسكربت التطوير ([sync-keycloak-dev-realm.ps1:15,510-535](../scripts/sync-keycloak-dev-realm.ps1)) وبسر مختبري ثابت `dev-svc-secret-local-only`.
  - [KEYCLOAK_AND_SECRETS.md](KEYCLOAK_AND_SECRETS.md) §«ابدأ من هنا» يصف إنشاء realm يدوياً بخطوات على `localhost:5000`.
  - الإعدادات الإنتاجية:
    - `KC_HOSTNAME` افتراضه `localhost` ([docker-compose.production.yml:118](../docker-compose.production.yml))؛
    - `KC_PROXY: edge` خيار مهجور في Keycloak 24؛
    - منفذ 8080 منشور على كل الواجهات بلا قيد على `/admin` ([docker-compose.production.yml:106-108](../docker-compose.production.yml))؛
    - Keycloak يتصل بقاعدته بحساب superuser (`KC_DB_USERNAME: ${POSTGRES_USER}`، [:115](../docker-compose.production.yml)).
  - mapper `tenant_id` وسمات `employeeId` و`scope_org_unit_id` تُضبط يدوياً. وبدونها لا يرى المستخدم أي صف (G-OPS-12).
- **الأثر:** إعداد الهوية — وهو أساس كل صلاحية وفصل مهام — يدوي وغير موثق للإنتاج. ولا يمكن إعادة بناء البيئة بعد كارثة. ولوحة إدارة Keycloak مكشوفة على الإنترنت في تركيب VPS.
- **الإصلاح المقترح:**
  - قالب `realm-hrmanagement.production.json` مولَّد من سكربت التطوير نفسه، بـ `sslRequired: external` وredirect URIs من متغيرات، ويضم كل العملاء والنطاقات وmappers الجمهور و`tenant_id`؛
  - توليد سر لكل عميل من `.env`؛
  - استيراد عند الإقلاع الأول (`--import-realm` مع `IGNORE_EXISTING`)؛
  - حصر `/admin` بقائمة IP؛
  - مستخدم PG مستقل لـ Keycloak؛
  - تحديث `KC_PROXY` إلى `KC_PROXY_HEADERS=xforwarded`.
- **الخطة:** [PLAN-39](acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md)
- **التحقق المطلوب:**
  - (موجب) خادم نظيف يصبح realm فيه جاهزاً دون تدخل، ويحصل `svc-compliance` على توكن بجمهور `api-hr-core`، ويحمل توكن المستخدم `tenant_id`.
  - (سالب) توكن `svc-payroll` لا يُقبل على `api-file-storage`. وطلب `/admin` من IP غير مسموح يُرفض.

#### G-OPS-04 — مفاتيح Data Protection غير مستمرة: الرقم الوطني يصبح غير قابل للقراءة
- **الدليل:**
  - `EmployeePiiProtectionInterceptor` يشفّر `Employee.NationalId` قبل الحفظ ([EmployeePiiProtectionInterceptor.cs:41-42](../src/Services/HRCore/HR.Core.Infrastructure/Data/EmployeePiiProtectionInterceptor.cs)).
  - التشفير يتم عبر `IDataProtectionProvider` ([PiiFieldEncryptionService.cs:17-21](../src/Shared/HRManagement.ServiceDefaults/Privacy/PiiFieldEncryptionService.cs)). وفك التشفير يرمي `CryptographicException` عند غياب المفتاح ([:38-45](../src/Shared/HRManagement.ServiceDefaults/Privacy/PiiFieldEncryptionService.cs)).
  - `AddHrDataProtection` لا يحفظ الحلقة إلا إذا ضُبط `DataProtection:KeyRingPath` ([DataProtectionExtensions.cs:28-40](../src/Shared/HRManagement.ServiceDefaults/DataProtectionExtensions.cs)).
  - في الإنتاج يُضبط المسار للبوابة وحدها ([docker-compose.production.yml:223](../docker-compose.production.yml)). لا لـ HR.Core ولا لأي من الخدمات الـ13 الأخرى، ولا في أي `appsettings`.
  - ولا في k8s: لا ذكر لـ `DataProtection` في `deploy/k8s`. ومع نسختين أو أكثر من HR.Core لكل pod حلقة مختلفة.
  - مكدّس القبول يضبطه لكل الخدمات على volume ([docker-compose.acceptance.yml:5,10](../docker-compose.acceptance.yml)). لهذا لم يظهر العطل في أي دليل.
  - الحلقة غير مشفرة (`ProtectKeysWith*` غائب) وليست ضمن أي نسخة احتياطية (G-OPS-06).
- **الأثر:**
  - أول `docker compose up -d` بصورة جديدة يعيد إنشاء الحاوية، فتُولَّد حلقة جديدة. عندها يفشل تحميل أي `Employee` مشفّر الرقم الوطني، فتسقط شاشات الموظفين والرواتب المرتبطة.
  - استعادة نسخة على خادم آخر تعطي بيانات شخصية غير قابلة للقراءة.
  - في k8s يفشل الفك بين pod وآخر.
  - ويتأثر أيضاً antiforgery وTempData وجلسة بوابة المرشح `/careers/my` ([CandidatePortalSession.cs:31](../src/Services/HRCore/HR.Core.Web/Services/CandidatePortalSession.cs)).
- **الإصلاح المقترح:**
  - volume مسمّى لحلقة كل خدمة، أو حلقة مشتركة في PostgreSQL (`PersistKeysToDbContext`)؛
  - تشفير الحلقة بشهادة أو مفتاح خارج القاعدة؛
  - ضم الحلقة إلى مجموعة النسخ؛
  - حارس بدء في Production يرفض غياب `KeyRingPath` عن أي خدمة تشفّر PII.
- **الخطة:** [PLAN-39](acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md)؛ وربط وحدات تخزين المفاتيح في [PLAN-36](acceptance-plans/PLAN-36-ops-production-boot-single-host.md)
- **التحقق المطلوب:**
  - (موجب) إنشاء موظف، ثم `docker compose up -d --force-recreate hr-core-web`، فيُعرض الرقم الوطني. واستعادة النسخة على خادم ثانٍ تعرض الرقم نفسه.
  - (سالب) حذف volume الحلقة يمنع الإقلاع برسالة صريحة، ولا يولّد حلقة جديدة بصمت.

#### G-OPS-05 — الوضع الإنتاجي لقاعدة البيانات (أدوار التطبيق + RLS مفروض) لم يُشغَّل قط
- **الدليل:**
  - **الإنتاج:** التطبيقات تتصل بأدوار `*_app` بلا `BYPASSRLS` ([init-service-roles.sh:53-54](../docker/init-service-roles.sh))، و`run-migrations.sh` يطبّق `FORCE ROW LEVEL SECURITY` على كل جدول فيه `TenantId` ([run-migrations.sh:82-96,135](../scripts/run-migrations.sh)).
  - **سياسة RLS:** `"TenantId"::text = NULLIF(current_setting('app.current_tenant', true), '')`، فلا يرى أي صف من لم يضبط الجهة ([TenantRlsMigrationHelper.cs:24-28](../src/Shared/HRManagement.ServiceDefaults/TenantRlsMigrationHelper.cs)).
  - **ضبط الجهة على الاتصال:** المعترض لا يضبط `app.current_tenant` إذا لم توجد جهة ([NpgsqlTenantSessionInterceptor.cs:32-33](../src/Shared/HRManagement.ServiceDefaults/NpgsqlTenantSessionInterceptor.cs)).
  - **القبول وكل الأدلة الحية:** الخدمات تتصل بـ `Username=${POSTGRES_USER}` ([docker-compose.yml:115](../docker-compose.yml)) = `hrm_ci` superuser ([.env.ci:2](../.env.ci)). والـ superuser يتجاوز RLS. حتى سكربتات القبول تعمل بـ `-U hrm_ci` ([acceptance-backup-restore.ps1:58](../scripts/acceptance-backup-restore.ps1)).
  - **`IgnoreTenantFilterForSystem` يتجاوز مرشح EF فقط** (`query.IgnoreQueryFilters()`، [TenantFilterBypass.cs:66-74](../src/Shared/HRManagement.ServiceDefaults/TenantFilterBypass.cs)). يُستخدم في 123 موضعاً: 114 في HR.Core، منها 37 لبوابة `/careers` المجهولة و21 لطوابير الخلفية، و4 في File.Storage، و3 في Auth.Directory، و2 في Contracts.
  - **مثال حاسم:** مهمة الفحص تقرأ `FileScanWorkItems` و`Files` بهذا التجاوز بلا جهة ([FilePlatformService.cs:611-624](../src/Services/FileStorage/File.Storage.Infrastructure/Services/FilePlatformService.cs)، [FileScanProcessorJob.cs:29-37](../src/Services/FileStorage/File.Storage.Infrastructure/Jobs/FileScanProcessorJob.cs)). والجدولان يرثان `TenantId` من `BaseEntity`. تحت RLS ترى المهمة صفر صفوف، فيبقى كل ملف `PendingScan` ويُمنع تنزيله (fail-closed) (يحتاج تحقق).
  - **إيصالات Inbox** تحمل `TenantId` ويشملها RLS مع `WITH CHECK`. الرسالة بلا ترويسة جهة لا تُسجَّل، فيفشل المستهلك إلى `_error` (يحتاج تحقق).
  - **الاختبارات:** `CrossTenantRlsIntegrationTests` تختبر السياسات بـ SQL مباشر يضبط `app.current_tenant` بنفسه. ولا اختبار يشغّل التطبيق أو مهامه الخلفية بدور `*_app`.
- **الأثر:** في الإنتاج يُتوقع أن تتعطل بصمت:
  - فحص الملفات، فلا يُتاح أي مستند؛
  - بوابة `/careers` العامة، فلا تُعرض إعلانات؛
  - مهام الخلفية بلا جهة؛
  - البذور الإلزامية خارج `DefaultTenantId`.

  ادعاء TEN-002 «اختبار SQL مباشر بحساب التطبيق» ([02-P1:17-31](completion-backlog/02-P1-PLATFORM-INTEGRITY.md)) لا يغطي التشغيل.
- **الإصلاح المقترح:**
  - تشغيل مكدّس القبول بأدوار `*_app` وRLS مفروض؛
  - مسار نظامي على مستوى القاعدة، مثل `app.system_bypass` تضبطه أغلفة `IgnoreTenantFilterForSystem` مع سياسة `OR` مقيدة بدور، أو دور `*_worker` بـ BYPASSRLS لعمّال الخلفية فقط؛
  - التكرار على الجهات في مهام الخلفية مع `TenantIdFlow`؛
  - توثيق القرار في D-OPS-4. والدلالة الأمنية للتجاوز → IAM.
- **الخطة:** [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md) (جزئياً: أدوار `*_app` و`*_migrator` وملف قبول مستقل بأقل صلاحية)، و[PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md) (جزئياً: قراءة الصفوف الوطنية تحت RLS المفروض). والمسار النظامي والمهام الخلفية وجعل الوضع افتراضياً «الموجة 2» (OPS-D)
- **التحقق المطلوب:**
  - (موجب) على المكدّس بأدوار الإنتاج: AC-01..05، ورحلة HCM-33، ورفع مستند ثم فحصه ثم تنزيله، كلها تنجح.
  - (سالب) استعلام بدور `hr_core_app` دون `app.current_tenant` يعيد صفر صفوف. وطلب جهة A لا يرى صفوف B حتى مع تعطيل مرشح EF.

#### G-OPS-06 — النسخ الاحتياطي يفشل بصمت والاستعادة غير مثبتة
- **الدليل:**
  - **دور النسخ غير موجود.** حاوية `postgres-backup` تستخدم `BACKUP_PGUSER=${POSTGRES_BACKUP_USER:-hrm_backup}` بكلمة مرور `POSTGRES_PASSWORD` ([docker-compose.production.yml:143-147](../docker-compose.production.yml)). لا شيء ينشئ `hrm_backup`، و`POSTGRES_BACKUP_*` غائبة عن `.env.example`.
  - **الدور، لو أُنشئ، لا يقرأ الجداول المحمية.** [create-hrm-backup-role.sql:8-21](../scripts/sql/create-hrm-backup-role.sql) لا يمنحه `BYPASSRLS`. و`pg_dump` بالافتراض `row_security=off` يفشل على الجداول `FORCE RLS` (يحتاج تحقق).
  - **الفشل لا يُلاحظ.** حلقة `while true` لا تفحص رمز الخروج ([docker-compose.production.yml:166-169](../docker-compose.production.yml))، والحاوية تبقى «تعمل». ونسخ MinIO يبتلع الفشل بـ `|| true` ([:198](../docker-compose.production.yml)). ولا تنبيه على عمر آخر نسخة (G-OPS-11).
  - **النسخ على الخادم نفسه.** النسخ في volume محلي (`postgres_backups`)، ولا مزامنة خارجية مجدولة في compose. `backup-offsite-sync.sh` يدوي.
  - **مجموعة النسخ ناقصة.** لا تضم حلقة Data Protection (G-OPS-04) ولا تعريفات RabbitMQ ولا تصدير realm.
  - **دليل AC-34 قديم وشكلي.** «Pass (Script contract verified)» بتاريخ 2026-08-24 على `9cb3705` ([AC-34/result.json](../acceptance-evidence/AC-34/result.json))، و`db-checks.json` يقول «Run ... against live PG for full drill». الاختبار الحالي يتطلب `drill-result.json` ([Ac34BackupRestoreLiveTests.cs:28-38](../src/Tests/HRManagement.AcceptanceTests/Ac34BackupRestoreLiveTests.cs)) ولم يُشغَّل، لكن الملخص يعرضه Pass ([summary.md](../acceptance-evidence/summary.md)).
  - **تمرين القبول نفسه محدود.** يستعيد داخل حاوية PostgreSQL نفسها كـ superuser ([acceptance-backup-restore.ps1:152-167](../scripts/acceptance-backup-restore.ps1))، ولا يستعيد الملفات ولا realm ولا يتحقق من قراءة PII.
  - **كل بنود التحقق غير مؤشّرة:** [OPS_BACKUP_VERIFICATION.md:8-34](OPS_BACKUP_VERIFICATION.md) كلها `[ ]`، وسجل تمارين DR فارغ ([OPS_DR_VERIFICATION.md:43-45](OPS_DR_VERIFICATION.md))، بينما OPS-003/004 «Done» ([03-P2:105,122](completion-backlog/03-P2-SCALE-AND-RELIABILITY.md)).
  - **أهداف RTO/RPO مبنية على CNPG/WAL في k8s** ([OPS_DR_BIA_RTO_RPO.md:32](OPS_DR_BIA_RTO_RPO.md)). على خادم Compose واحد RPO الفعلي 24 ساعة في أحسن حال، وصفر نسخ في أسوئها.
- **الأثر:** شرط G2 «restore ناجح» غير متحقق. أي عطل قرص على VPS يعني فقد كل البيانات. وحتى النسخة الناجحة تُستعاد ببيانات شخصية مشفرة غير مقروءة.
- **الإصلاح المقترح:**
  - dump بدور `*_migrator`، المالك المستثنى من RLS عبر جلسة مالكة، أو بدور نسخ بـ `BYPASSRLS` موثّق؛
  - فشل الحلقة يُكتب في كتالوج ويطلق تنبيهاً؛
  - مزامنة خارجية مجدولة (restic أو rclone) مع تشفير؛
  - ضم الحلقة وrealm وتعريفات الوسيط؛
  - تمرين استعادة على خادم ثانٍ بقياس RTO؛
  - تصحيح الملخص والبطاقات.
- **التحقق المطلوب:**
  - (موجب) نسخ ليلي، ثم استعادة على VPS ثانٍ نظيف، ثم دخول وعرض موظف برقمه الوطني وتنزيل مستند، مع تسجيل `measured_restore_seconds`. AC-34 Pass حي.
  - (سالب) كسر كلمة مرور دور النسخ يطلق تنبيهاً خلال 24 ساعة. وملف dump تالف يُرفض عند التحقق من checksum.

#### G-OPS-07 — استيراد الموظفين لا يستورد موظفاً واحداً، واستيراد الهيكل مسطّح
- **الدليل:** [DataImportController.cs](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs) هو المسار الوحيد لاستيراد الموظفين في الخطوة 2 من [PILOT_DEPLOYMENT.md:12](PILOT_DEPLOYMENT.md). أما الهيكل فله مسار ثانٍ هرمي: حزمة التهيئة (`orgStructure.units` مع `parentCode`) عبر [AgencyOrgStructureProvisioner.cs:45-67](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyOrgStructureProvisioner.cs)، ومعها تشغيل تجريبي (`dryRun`)، وإعادة استخدام جهة قائمة إذا طابق رمزها ([AgencyProvisioningService.cs:106-121](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)). لكنه لم يُشغَّل (AC-07).
  - **الموظف لا يُضاف إلى السياق.**
    - `new Employee {...}` ثم `CreateEmployeeWithEventAsync(employee)` دون `db.Employees.Add` ([:72-85](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs)). المسار الوحيد الآخر يضيفه أولاً ([EmployeesController.cs:216-217](../src/Services/HRCore/HR.Core.Web/Controllers/EmployeesController.cs)).
    - الخدمة تحفظ ثم تضيف `EmploymentStatusHistory` و`EmploymentRecord` بـ `EmployeeId` غير موجود ([EmployeeIntegrationService.cs:16-38](../src/Services/HRCore/HR.Core.Infrastructure/Integration/EmployeeIntegrationService.cs)). والمفتاح الأجنبي `Restrict` ([HRCoreDbContext.cs:226-230](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
    - النتيجة استثناء غير معالج عند أول صف.
  - **كشف التكرار لا يعمل.** يقارن `e.NationalId == nationalId` بنص صريح ([:67](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs))، بينما العمود مشفّر ([EmployeePiiProtectionInterceptor.cs:41-42](../src/Services/HRCore/HR.Core.Infrastructure/Data/EmployeePiiProtectionInterceptor.cs)). لا يطابق أبداً.
  - **قيم ثابتة خاطئة.** `Gender = Gender.Male` لكل موظف ([:79](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs))، و`HireDate = DateTime.UtcNow` ([:81](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs))، ولا درجة ولا وظيفة ولا منصب ولا راتب ولا رقم وظيفي. القدم والعلاوات والتقاعد تُحسب من تاريخ الاستيراد.
  - **تحليل CSV هش.**
    - `line.Split(',')` بلا دعم للاقتباس ([:34,64](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs))؛
    - `DateTime.Parse` يرمي عند أول تاريخ خاطئ ([:78](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs))؛
    - رمز وحدة مجهول يعطي `PrimaryOrgUnitId = null` بصمت ([:70,80](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs))؛
    - لا معاملة ولا تشغيل تجريبي ولا تقرير أخطاء.
  - **الهيكل مسطّح.** `new OrgUnit { Code, NameAr }` بلا `ParentId` ولا نوع ([:39](../src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs)). ولا يُنشر `IOrgUnitChanged`، فلا تعلم الخدمات الست المستهلكة له بالوحدات (G-OPS-08).
  - **لا اختبار** لـ `DataImportController` في `src/Tests`.
  - **برنامج الهجرة MIG-001..007 وثائق ومصفوفات مطالبات** (`LiveEtlWorkersClaim=false` وأخواتها في [gateway appsettings.Production.json:101-135](../src/Gateway/HRManagement.Gateway/appsettings.Production.json)) بلا عمّال ETL.
- **الأثر:** لا يمكن إدخال 500–2000 موظف لجهة التجربة إلا يدوياً. وأي تصحيح للشيفرة الحالية دون إعادة تصميم سيُدخل بيانات خاطئة في الجنس وتاريخ التعيين والهيكل. *(صُحّح بعد التحقق المستقل: الهيكل الهرمي ممكن عبر حزمة التهيئة `AgencyOrgStructureProvisioner`، فالعطب الحاسم هو استيراد الموظفين؛ وسطر الخطوة 2 هو 12 لا 11)*
- **الإصلاح المقترح:**
  - قالب Excel بأعمدة إلزامية متفق عليها (D-OPS-7): الهيكل بالأب، والموظف بالجنس وتاريخ التعيين والدرجة والوظيفة والمنصب؛
  - تحليل ClosedXML؛
  - تشغيل تجريبي بتقرير أخطاء لكل صف، ثم تطبيق ضمن معاملة لكل دفعة؛
  - مفتاح idempotency على `NationalIdHash`؛
  - نشر `IOrgUnitChanged`/`IEmployeeCreated` عبر outbox؛
  - تقرير مصالحة (عدد المصدر = المستورد + المرفوض)؛
  - إدخال المرفوض إلى الحجر (AC-08).
- **التحقق المطلوب:**
  - (موجب) ملف اصطناعي بـ 2000 موظف و120 وحدة بثلاثة مستويات: التشغيل التجريبي يبلّغ عن 0 أخطاء، ثم التطبيق، ثم العدّ متطابق في HR.Core وفي لقطات الوحدات لدى Payroll وContracts.
  - (سالب) إعادة الملف نفسه لا تضيف أي سجل. وصف بتاريخ خاطئ يظهر في التقرير ولا يُفشل الدفعة كلها.

### 4.2 ثغرات السلامة والحوكمة (P1)

#### G-OPS-08 — أحداث تُفقد بصمت ونشر غير ذري
- **الدليل:**
  - **نشر بلا حفظ لاحق.** في خدمة bus outbox ([MassTransitExtensions.cs:113-121](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)) لا تُكتب الرسالة إلا عند `SaveChanges`، والوثيقة نفسها تقرّ بذلك ([EVENT_PUBLISHER_INVENTORY.md:95](EVENT_PUBLISHER_INVENTORY.md)). المسارات التالية تنشر ثم تعود دون حفظ:
    - `IPromotionExecuted` و`IDelegationExecuted` في Movements ([MovementsExtendedControllers.cs:1032-1048,1189-1202](../src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs)). يستهلكهما HR.Core لتحديث درجة الموظف وندبه ([PromotionExecutedConsumer.cs](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/PromotionExecutedConsumer.cs)، [DelegationExecutedConsumer.cs](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/DelegationExecutedConsumer.cs)). والواجهة تقول «تم تنفيذ الترفيع ونشر الحدث».
    - نتائج اعتماد العهد والفقد `IAssetAssignment*`/`IAssetLoss*` ([WorkflowInstanceActionService.cs:117-120,164-166,208-266](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)) التي ينتظرها Admin.Logistics.
    - المزامنة الأولية للوحدات عند الإقلاع ([OrgUnitIntegrationService.cs:31-36](../src/Services/HRCore/HR.Core.Infrastructure/Integration/OrgUnitIntegrationService.cs)، [HR.Core Program.cs:309](../src/Services/HRCore/HR.Core.Web/Program.cs)).
    - `IAgencyOnboarded` ([AgencyProvisioningService.cs:167-178](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)).
    - هذه الأحداث غائبة عن جرد الناشرين أصلاً.
    - (يحتاج تحقق حي، والسلوك موثّق في MassTransit.)
  - **نشر غير ذري.** 21 ناشراً موثّقاً بنمط «Save → Publish → Save» ([EVENT_PUBLISHER_INVENTORY.md:34-60](EVENT_PUBLISHER_INVENTORY.md)) دون معاملة تجمع الحفظين، مثل ترحيل الرواتب ([PayrollRunHandlers.cs:260-264](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)). انقطاع بين الحفظين يُبقي الحالة ملتزمة والحدث مفقوداً، وإعادة المحاولة تُرفض لأن الحالة تغيّرت. هذا عكس معيار INT-001 «commit الحالة والحدث معاً» ([01-P0:168](completion-backlog/01-P0-RELEASE-BLOCKERS.md)).
  - **خدمات بلا outbox.** Records.Archive وDev.Analytics ينشران `IExportJobRequested` بعد الحفظ مباشرة إلى الوسيط ([ArchiveExportJobService.cs:59-67](../src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs)). سقوط الوسيط يترك مهمة تصدير معلقة.
  - **مستهلكون بلا Inbox.** `TrainingDbContext` لا يستدعي `ConfigureIntegrationMessageReceipts` ([TrainingDbContext.cs:132-134](../src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs))، فيتجاوز الفلتر مستهلكيه الثلاثة ([IdempotentConsumerFilter.cs:49-55](../src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs)). هي upserts، لكن `EmployeeCreated` مكرر أو متأخر يعيد الوحدة القديمة بعد `TransferExecuted`.
  - **أثر جانبي خارجي مبتلع.** فشل إنشاء مستخدم Keycloak يُسجَّل تحذيراً ويُقرّ الرسالة. وإعادة التسليم تخرج مبكراً لأن `AuthUser` موجود ([EmployeeCreatedConsumer.cs:41,63-84](../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)). فلا لحاق.
  - **سياسة إعادة المحاولة.** `InvalidOperationException` تذهب مباشرة إلى `_error` ([MassTransitExtensions.cs:151](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)). EF يلف بعض الأعطال العابرة بها (يحتاج تحقق). ولا redelivery من المستوى الثاني.
  - **مهل الـ sagas.** `UseDelayedMessageScheduler` ([MassTransitExtensions.cs:79](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs)) يتطلب إضافة `rabbitmq_delayed_message_exchange`. صورة `rabbitmq:3.13-management-alpine` لا تحملها، ولا تفعيل لها في أي ملف. وأربع sagas تجدول مهلاً (Hire وSeparation وTransfer وSecondment، مثل [HireCaseStateMachine.cs:91](../src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/HireCaseStateMachine.cs)). إما لا تطلق المهلة أبداً أو تتعطل الخطوة (يحتاج تحقق).
  - **DLQ بلا أداة.** `ErrorQueueReplay` موجود ([ErrorQueueReplay.cs:9](../src/Shared/HRManagement.ServiceDefaults/ErrorQueueReplay.cs)) ولا يستخدمه أي endpoint أو سكربت. ولا تنبيه على عمق `_error` (G-OPS-11).
  - **AC-15/17/18 لم تُشغَّل.** وAC-16 «NoResult» من 2026-08-25 بـ `receiptCount: -1` ([AC-16/db-checks.json](../acceptance-evidence/AC-16/db-checks.json)).
- **الأثر:**
  - ترفيع يُنفَّذ في Movements ولا يصل إلى سجل الموظف ولا إلى الراتب؛
  - عهدة معتمدة تبقى معلقة في اللوجستيات؛
  - موظفون بلا حساب دخول دون أن يعلم أحد؛
  - ولا أداة لإعادة ما سقط.
- **الإصلاح المقترح:**
  - قاعدة معمارية: في خدمة outbox كل `Publish` يتبعه `SaveChanges` في الوحدة نفسها، أو حفظ واحد بعد النشر مع اختبار تحليلي يمنع النمط؛
  - إصلاح المسارات الخمسة؛
  - تغليف «Save → Publish → Save» بمعاملة أو حفظ واحد؛
  - outbox لـ Archive وAnalytics؛
  - receipts لـ Training؛
  - redelivery مجدول، وإضافة plugin أو مجدول Quartz/DB؛
  - إعادة محاولة لمزامنة Keycloak؛
  - أداة إعادة تشغيل DLQ للمشغّل.
- **الخطة:** [PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md)؛ والرسائل المؤجلة (plugin) في [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md)، وإعادة محاولة مزامنة Keycloak في [PLAN-62](acceptance-plans/PLAN-62-sep-access-cutoff-clearance.md)، وأداة DLQ وسياسة إعادة المحاولة «الموجة 2» (OPS-F)
- **التحقق المطلوب:**
  - (موجب) ترفيع، ثم درجة الموظف في HR.Core تتغير خلال 30 ثانية. وإيقاف RabbitMQ أثناء الترحيل، ثم إعادته، فيصل `IPayrollProcessed` مرة واحدة (AC-17). وAC-15/16/18 Pass.
  - (سالب) قتل العملية بين الحفظين لا يترك حالة بلا حدث. ورسالة مكررة لا تضاعف الأثر في Training.

#### G-OPS-09 — المصالحة لا تكشف الفقد بين الخدمات، والعزل غير مثبت حياً
- **الدليل:**
  - **مصالحة ذاتية.** `ReadModelReconciliationService` يقارن نموذج القراءة بسجل أحداث Analytics نفسه ([ReadModelReconciliationService.cs:28-43](../src/Services/DevAnalytics/Dev.Analytics.Application/Services/ReadModelReconciliationService.cs)). الحدث الذي لم يصل (G-OPS-08) لا يظهر أبداً كفرق.
  - **تقرير لا يبقى.** التقرير يُحفظ في `InMemoryReadModelReconciliationStatusStore` ([IReadModelReconciliationStatusStore.cs:12](../src/Services/DevAnalytics/Dev.Analytics.Application/Services/IReadModelReconciliationStatusStore.cs)) ويضيع عند إعادة التشغيل. هذا يخالف «توثيق ما تغيّر» في PLAN-09.
  - **تحميل كامل في الذاكرة.** الأحداث كلها تُحمَّل في الذاكرة ([:29-32](../src/Services/DevAnalytics/Dev.Analytics.Application/Services/ReadModelReconciliationService.cs)).
  - **لا مصالحة بين الخدمات** لـ: لقطات الوحدات لدى 6 خدمات مستهلكة لـ `IOrgUnitChanged` ولقطات `EmployeeOrgUnitSnapshot` لدى 4 منها، وملفات الأجر في Payroll مقابل العقود الفعالة، و`AuthUsers` مقابل الموظفين. القوائم اليدوية فقط ([REL_002_DAILY_RECONCILIATION.md](REL_002_DAILY_RECONCILIATION.md)).
  - **AC-19/20 لم تُشغَّلا.** اختبار AC-20 سليم التصميم ([Ac20DatabaseIsolationTests.cs:18-41](../src/Tests/HRManagement.AcceptanceTests/Ac20DatabaseIsolationTests.cs)).
- **الأثر:** الانحراف بين الخدمات، مثل وحدة الموظف أو حالته أو درجته، يتراكم دون كشف. وأول من يكتشفه موظف لم يُصرف راتبه.
- **الإصلاح المقترح:**
  - مهمة مصالحة يومية تقارن مفاتيح وبصمات (عدد + hash لكل جهة/وحدة) بين HR.Core ومستهلكيه عبر API داخلية؛
  - تقرير محفوظ وقابل للتصدير؛
  - تصحيح بإعادة نشر حدث المصدر لا بالكتابة المباشرة؛
  - تشغيل AC-19/20.
- **التحقق المطلوب:**
  - (موجب) حذف لقطة موظف من Payroll، ثم كشفها في التقرير، ثم إعادة نشرها، ثم تطابق.
  - (سالب) لا تصحيح بلا سجل محفوظ. ويفشل `hr_core_app` في الاتصال بـ `payroll_db` (AC-20).

#### G-OPS-10 — خادم واحد 4 vCPU/16 GB: الحافة والسعة غير مهيأتين
- **الدليل:**

  | البند | الوضع | المرجع |
  |---|---|---|
  | سقوف الذاكرة | 15 تطبيقاً × 512 MiB = 7.5 GiB + PG 1 + Rabbit 0.5 + ClamAV 2 + MinIO 0.5 + Keycloak 0.75 = **12.25 GiB**. مع المراقبة (8 حاويات بلا سقف سوى Seq) ≈ 14–15 GiB، فلا يبقى لنظام التشغيل وذاكرة PG المؤقتة إلا القليل (تقديري) | [docker-compose.production.yml:34-42,63-131](../docker-compose.production.yml) |
  | HR.Core بـ 512 MiB | أكبر خدمة (محرك المسارات والسياسات وClosedXML وQuestPDF)؛ تصدير كبير قد يتجاوز السقف (يحتاج تحقق) | [:245-247](../docker-compose.production.yml) |
  | ClamAV بـ 2 GiB | إعادة تحميل التواقيع المتزامنة تضاعف الذاكرة مؤقتاً، فقد تُقتل الحاوية يومياً وتنتج `ScanError` (يحتاج تحقق). وتنزيل التواقيع من `database.clamav.net` بلا مرآة خاصة؛ قد يُحجب حسب موقع الخادم (D-OPS-1) | [:77-86](../docker-compose.production.yml) |
  | محدد المعدل | 120 طلباً/دقيقة لكل IP، ويشمل أصول الوحدات الثابتة عبر YARP: نحو 20 طلباً لكل صفحة من `_HrSharedLayout`. مكتب خلف NAT واحد يُخنق بعد 5–6 صفحات/دقيقة لكل المكتب. مكدّس القبول رفعه إلى 2000 ليعمل | [gateway Program.cs:102,155-168,206-210](../src/Gateway/HRManagement.Gateway/Program.cs)، [docker-compose.acceptance.yml:215](../docker-compose.acceptance.yml) |
  | الترويسات الممررة | الثقة الافتراضية بالنطاقات الخاصة سليمة لوكيل على الخادم نفسه. لكن `ForwardLimit=1` يأخذ IP الوكيل الخارجي إن وُضع CDN أمام nginx | [ForwardedProxyTrust.cs:59](../src/Shared/HRManagement.ServiceDefaults/ForwardedProxyTrust.cs) |
  | التعرض | البوابة 5000 وKeycloak 8080 منشوران على `0.0.0.0` بـ HTTP صريح؛ لا حافة TLS | [:106-108,205-206](../docker-compose.production.yml) |
  | RabbitMQ | لا `hostname` ثابت ولا volume مسمّى. اسم العقدة يتغير مع إعادة إنشاء الحاوية، فتضيع الرسائل المعلقة وتتعطل quorum queues (يحتاج تحقق) | [docker-compose.yml:26-40](../docker-compose.yml) |
  | اتصالات PG | `max_connections` الافتراضي 100 مقابل 15 مجمع Npgsql (100 لكل منها) ومجمع Keycloak (افتراضه 100) وoutbox يستطلع كل ثانية في 8 خدمات. لا `Maximum Pool Size` في أي سلسلة اتصال (يحتاج تحقق) | [docker-compose.production.yml:253](../docker-compose.production.yml) |
  | الإقلاع | 15 تطبيقاً + Keycloak `start` (بناء تلقائي) + تحميل تواقيع ClamAV على 4 vCPU، ومهلة صحة HR.Core 60 ثانية + 5×15 ثانية؛ والبوابة تنتظر `service_healthy`. قد يفشل الإقلاع الأول بـ «dependency unhealthy» (يحتاج تحقق) | [:27-32,241-243](../docker-compose.production.yml) |

- **الأثر:** حتى بعد G-OPS-01 تبقى تجربة العرض على VPS هشة: خنق المستخدمين، وقتل ClamAV، وفقد الرسائل عند التحديث، وتعرض Keycloak.
- **الإصلاح المقترح:**
  - ملف تعريف `vps` بسقوف معايرة: HR.Core 1 GiB، والخدمات الخفيفة 384 MiB، وClamAV 3 GiB أو `ConcurrentDatabaseReload no`؛
  - مراقبة مخففة (D-OPS-6)؛
  - Caddy بـ TLS تلقائي؛
  - نشر 443 فقط؛
  - `hostname: rabbitmq` + volume؛
  - `Maximum Pool Size=20` و`max_connections=300`؛
  - تقسيم المعدل حسب المستخدم المصادق، وإعفاء الأصول الثابتة.
- **الخطة:** [PLAN-36](acceptance-plans/PLAN-36-ops-production-boot-single-host.md)؛ و`hostname`/volume لـ RabbitMQ وحجم مجمّعات الاتصال في [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md)
- **التحقق المطلوب:**
  - (موجب) على VPS الهدف: إقلاع بارد أقل من 6 دقائق، و10 مستخدمين من IP واحد يتصفحون 10 دقائق بلا 429، وذروة الذاكرة أقل من 80%.
  - (سالب) `nmap` من الخارج يرى 443 فقط. و`docker compose up -d --force-recreate rabbitmq` لا يفقد رسالة معلقة.

#### G-OPS-11 — المراقبة والتنبيه: تغطية ناقصة ولا قناة بشرية ولا وضع مخفف
- **الدليل:**
  - **ServiceDown يغطي 6 خدمات من 15.** التعبير `up{job=~"hr-.*"}` ([alerts.yml:5](../docker/prometheus/alerts.yml)) يلتقط ست مهام فقط: `hr-gateway` و`hr-core` و`hr-contracts` و`hr-health` و`hr-movements` و`hr-training`. أسماء المهام التسع الأخرى (`payroll` و`idcard` و`records-archive` و`admin-logistics` و`it-infra` و`dev-analytics` و`compliance` و`auth-directory` و`file-storage`) لا تطابقه ([prometheus.yml](../docker/prometheus/prometheus.yml)).
  - **لا تنبيه** على: عمق طوابير `_error`، أو فشل النسخ وعمر آخر نسخة، أو امتلاء القرص، أو انتهاء الشهادة، أو سقوط Keycloak أو ClamAV، أو قتل OOM. ومع ذلك توجد أدلة تشغيل لثلاثة منها ([docs/runbooks](runbooks/): `cert-expiry.md` و`queue-backup.md` و`oom-kill.md`) بلا قاعدة تطلقها.
  - **لا قناة بشرية.** المستقبل الوحيد `alert-sink` محلي يكتب ملفات ([alertmanager.yml:36-39](../docker/alertmanager/alertmanager.yml)). لا بريد ولا رسائل.
  - **لا وضع مخفف.** حزمة المراقبة 9 حاويات، و`docker-compose.production.yml` لا يعمل بدونها (G-OPS-01).
  - **AC-37 «NoResult».** إعادة بناء الرحلة لم تُشغَّل ([AC-37/metadata.json](../acceptance-evidence/AC-37/metadata.json)).
- **الأثر:** على خادم التجربة تتوقف خدمة الرواتب أو تفشل النسخ أو تمتلئ طوابير الأخطاء دون أن يعلم أحد.
- **الإصلاح المقترح:**
  - تصحيح تعبير ServiceDown إلى `job!~"prometheus|otel.*"`؛
  - قواعد لـ `_error` (rabbitmq-exporter)، وعمر الكتالوج، و`node_filesystem`، و`probe_ssl_earliest_cert_expiry`؛
  - مستقبل بريد أو Telegram (D-OPS-6)؛
  - ملف مراقبة مخففة: Prometheus + Alertmanager + blackbox + node-exporter ≈ 400 MiB، وSeq وTempo وGrafana اختيارية.
- **التحقق المطلوب:**
  - (موجب) `docker stop payroll-web` يصل تنبيهه إلى البريد خلال 3 دقائق، ورسالة في `_error` يصل تنبيهها خلال 10 دقائق.
  - (سالب) إيقاف `postgres-backup` 26 ساعة يطلق «backup stale».

#### G-OPS-12 — نموذج الجهة الواحدة غير محسوم للتشغيل
- **الدليل:**
  - **البذور الوطنية مختومة بجهة افتراضية.** تعريفات المسارات تُبذر بـ `TenantDefaults.DefaultTenantId` فقط ([WorkflowPackSeeder.cs:92,106](../src/Services/HRCore/HR.Core.Infrastructure/Data/WorkflowPackSeeder.cs)، [HR.Core Program.cs:290](../src/Services/HRCore/HR.Core.Web/Program.cs)).
  - **تهيئة جهة جديدة تولّد GUID جديداً** ([AgencyProvisioningService.cs:91-95](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)) وتكتفي بتحذير «national workflows already seeded» ([:149-153](../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs)). والمرشح مطابقة صارمة ([TenantQueryFilterExtensions.cs:31-68](../src/Shared/HRManagement.ServiceDefaults/TenantQueryFilterExtensions.cs)) و`WorkflowDefinition` ليس «global» ([TenantEntityClassification.cs:20-33](../src/Shared/HRManagement.ServiceDefaults/TenantEntityClassification.cs)). لكن محلّل التعريفات يتجاوز المرشح عمداً ويرجع إلى تعريفات `DefaultTenantId` لأي جهة ([WorkflowStarter.cs:116-123](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowStarter.cs)، [WorkflowInstanceActionService.cs:305-311](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). فالجهة المُهيّأة ترى المسارات الوطنية على المكدّس الحالي. يبقى الخطر تحت RLS المفروض: جلسة الجهة لا ترى صفوف `DefaultTenantId`، فيفشل هذا الرجوع (G-OPS-05، يحتاج تحقق).
  - **مصدر الجهة الوحيد هو مطالبة التوكن.** `tenant_id` تأتي من سمة مستخدم Keycloak ([TenantContext.cs:22-28](../src/Shared/HRManagement.ServiceDefaults/TenantContext.cs)). لا أداة جماعية لضبطها.
  - **تزامن الدليل معطل.** `Keycloak:DirectorySync:Enabled=false` ([Auth.Directory appsettings.json:24](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json))، فحسابات ESS لـ 2000 موظف يدوية.
  - **AC-07/08 لم تُشغَّلا.** حارس الحجر موجود ([TenantQuarantineGuard.cs](../src/Shared/HRManagement.ServiceDefaults/Tenancy/TenantQuarantineGuard.cs)) دون دليل حي.
- **الأثر:** لا يوجد إجراء موثّق يجيب: «ما معرّف جهة التجربة؟ وكيف يحمله كل مستخدم؟ وهل ترى البذور الوطنية؟» أي اختيار عشوائي يعطي شاشات فارغة أو موافقات عالقة. *(صُحّح بعد التحقق المستقل: تعريفات المسارات الوطنية تُستخدم رجوعاً لكل جهة في `WorkflowStarter`، فـ«جهة بلا مسارات» لا تقع إلا تحت RLS المفروض)*
- **الإصلاح المقترح:** للتجربة، الجهة = `DefaultTenantId` (D-OPS-3). يُضاف:
  - سكربت تهيئة أول يضبط اسم الجهة وشجرتها من الاستيراد؛
  - أداة جماعية لإنشاء حسابات Keycloak بسمات `tenant_id`/`employeeId`/`scope_org_unit_id`؛
  - تأجيل تعدد الجهات، مع إصلاح نسخ البذور في `AgencyProvisioningService` قبل G3.
- **الخطة:** [PLAN-48](acceptance-plans/PLAN-48-ten-national-agency-scope.md) (جزئياً: رؤية تعريفات المسارات والسياسات والمراجع الوطنية لكل جهة في EF وتحت RLS). وسكربت التهيئة الأول والأداة الجماعية لحسابات Keycloak «الموجة 2» (OPS-G)
- **التحقق المطلوب:**
  - (موجب) مستخدم جديد بسمة الجهة يرى المسارات ويبدأ طلب شاغر.
  - (سالب) مستخدم بلا `tenant_id` لا يرى أي موظف ويُسجَّل الحدث.

#### G-OPS-13 — إدارة الأسرار (SEC-004): قيم وهمية مُلتزمة وتعارضات
- **الدليل:**
  - **سر إثبات المستدعي ملتزم في المستودع.** `GatewayCallerProof:Secret = "USE_ENV_OR_SECRET_MANAGER"` في 13 ملف وحدة وفي البوابة ([gateway appsettings.Production.json:35-38](../src/Gateway/HRManagement.Gateway/appsettings.Production.json)، [HR.Core appsettings.Production.json:251-254](../src/Services/HRCore/HR.Core.Web/appsettings.Production.json)). ولا متغير `GatewayCallerProof__Secret` في compose الإنتاج. أي حاوية على شبكة `application` تستطيع تزوير إثبات البوابة بقيمة معروفة.
  - **`.env.example` ينقصه 6 متغيرات إلزامية:** `SVC_HR_HEALTH_CLIENT_SECRET`، `SVC_HR_MOVEMENTS_CLIENT_SECRET`، `SVC_DEV_ANALYTICS_CLIENT_SECRET`، `SVC_COMPLIANCE_CLIENT_SECRET`، `GRAFANA_ADMIN_USER`، `GRAFANA_ADMIN_PASSWORD`. وتنقصه أيضاً `POSTGRES_BACKUP_*` و`AllowedHosts`.
  - **حسابات بلا مبدأ أقل صلاحية:** Keycloak يتصل بـ superuser ([docker-compose.production.yml:115](../docker-compose.production.yml)). وحاوية النسخ تتصل افتراضياً بدور `hrm_backup` غير المُنشأ، لكن بكلمة مرور الـ superuser ([:144-147](../docker-compose.production.yml)، [backup-postgres.sh:23-24](../scripts/backup-postgres.sh)). والأدوار الثلاثون تتشارك كلمة مرور واحدة (G-OPS-02).
  - **فحص الأسرار نصي.** `Sec004SecretRotationTests` تفحص نص الملفات، فلم تلتقط هذه القيم.
- **الأثر:** إثبات المستدعي، وهو خط الدفاع ضد الوصول المباشر للوحدات (SEC-001)، شكلي في الإنتاج. وتدوير الأسرار الموصوف غير ممكن عملياً. *(صُحّح بعد التحقق المستقل: النسخ لا يتصل بـ superuser افتراضياً بل بدور `hrm_backup` بكلمة مرور الـ superuser، متسقاً مع G-OPS-06)*
- **الإصلاح المقترح:**
  - حذف القيم الوهمية من `appsettings.Production.json`؛
  - حارس بدء يرفض أي `USE_ENV_*`/`YOUR_*`؛
  - توليد `.env` من سكربت `init-secrets` يغطي كل `:?`؛
  - اختبار يقارن `:?` في compose بـ `.env.example`؛
  - مستخدم PG لـ Keycloak.
- **الخطة:** [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md)
- **التحقق المطلوب:**
  - (موجب) `init-secrets` ثم `docker compose config`، فلا متغير مفقود.
  - (سالب) طلب بإثبات محسوب بالقيمة الوهمية يُرفض بـ 403.

### 4.3 قدرات ناقصة (P2/P3)

#### G-OPS-14 — الأداء والسعة غير مقاسين على أي عتاد (P2)
- **الدليل:**
  - `perf/results/baseline-lab/summary.json` نوعه `"baseline-lab-template"` ويقول «Replace metrics after a real staging execution» ([summary.json:3-5](../perf/results/baseline-lab/summary.json)).
  - AC-35 لم يُشغَّل.
  - لا قياس لزمن تشغيل رواتب 2000 موظف، ولا للتصدير، ولا لإقلاع المكدّس.
  - NFR-001 «Done» ([03-P2:205](completion-backlog/03-P2-SCALE-AND-RELIABILITY.md)).
- **الأثر:** لا يُعرف إن كان VPS المطلوب يكفي للتجربة، ولا متى يُخنق.
- **الإصلاح المقترح:** تشغيل `nfr-k6-run` (smoke/load) على VPS الهدف ببيانات 2000 موظف، وقياس تشغيل الرواتب والتصدير، ونشر الأرقام دون ادعاء أهداف.
- **التحقق المطلوب:** AC-35 Pass بأرقام p50/p95/p99 ومعدل الأخطاء. (سالب) لا يُعلن هدف غير مقاس.

#### G-OPS-15 — الترخيص والهوية المؤسسية (P3)
- **الدليل:**
  - **الترخيص شكلي.**
    - يقبل أي نص غير فارغ، بلا توقيع ولا سقف موظفين، رغم أن تعليق الصنف يقول «optional employee cap» ([LicenseValidationMiddleware.cs:8,14-32](../src/Shared/HRManagement.ServiceDefaults/LicenseValidationMiddleware.cs)).
    - يُطبَّق في البوابة فقط وقبل `/health` ([gateway Program.cs:196,252](../src/Gateway/HRManagement.Gateway/Program.cs)). بعد الانتهاء تفشل فحوص الصحة، فيدخل النشر في تراجع.
    - `DateTime.TryParse` حساس للثقافة.
  - **اسم الجهة الافتراضي مشوّه الترميز** في البوابة («Ã˜Â´Ã˜Â¹...»، [gateway appsettings.json:104-108](../src/Gateway/HRManagement.Gateway/appsettings.json)) ولا يتجاوزه أي إعداد إنتاجي.
- **الأثر:** نص عربي مشوّه في رأس البوابة أمام الجهة. وترخيص لا يحمي شيئاً.
- **الإصلاح المقترح:** تصحيح الترميز، وضبط `Organization__*` من `.env`، وإعفاء `/health`، وتأجيل الترخيص الموقّع (D-OPS-9).
- **الخطة:** [PLAN-36](acceptance-plans/PLAN-36-ops-production-boot-single-host.md)
- **التحقق المطلوب:** (موجب) البوابة تعرض اسم الجهة من `.env`. (سالب) انتهاء الترخيص لا يُسقط `/health/live`.

### 4.4 فجوات التحقق والتوثيق

#### G-OPS-16 — ادعاءات «Done» تشغيلية بلا دليل حي
- **الدليل:**
  - **بطاقات «Done» تنقضها الشيفرة:** DEP-001 ([01-P0:198-210](completion-backlog/01-P0-RELEASE-BLOCKERS.md))، وIAM-001 ([:89](completion-backlog/01-P0-RELEASE-BLOCKERS.md))، وINT-001 ([:174](completion-backlog/01-P0-RELEASE-BLOCKERS.md))، وSEC-004 ([:77](completion-backlog/01-P0-RELEASE-BLOCKERS.md))، وOPS-001..005 وOBS-001/002 وNFR-001/002 ([03-P2:72-220](completion-backlog/03-P2-SCALE-AND-RELIABILITY.md)).
  - **فحص المنافذ في SEC-001 تعابير نمطية على نص YAML** ([Sec001PortScanTests.cs:38-74](../src/Tests/HRManagement.UnitTests/Sec001PortScanTests.cs)). وهذا مما يمنعه [README.md](completion-backlog/README.md) §«ما لا يُقبل دليلاً».
  - **«تكافؤ» staging مقارنة ملفات YAML** لا بيئة تعمل ([rel-staging/parity-diff-last.json](rel-staging/parity-diff-last.json)). وقائمة توقيع staging كلها `[ ]` ([RELEASE_READINESS_STAGING.md](RELEASE_READINESS_STAGING.md)).
  - **ملخص الأدلة يعرض AC-34 «Pass»** على عقد سكربت ([summary.md](../acceptance-evidence/summary.md)).
  - **من 14 سيناريو في النطاق:** 11 لم تُشغَّل، و2 NoResult قديمان (AC-16 وAC-30)، و1 عقد شكلي (AC-34).
- **الأثر:** القرار بالعبور إلى staging أو تجربة مبني على مطالبات.
- **الإصلاح المقترح:**
  - إعادة البطاقات المتأثرة إلى `Blocked` بحسب قاعدة الإغلاق الملزمة في [README.md](completion-backlog/README.md)؛
  - جعل `AcceptanceEvidenceSummary` يعامل «contract» كـ `Deferred`؛
  - تشغيل AC-07/08 و15..20 و27..30 و34/35 على مكدّس بأدوار الإنتاج.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) لما يمكن تشغيله في الموجة 1؛ وAC-07/08 وAC-34/35 والتشغيل بأدوار الإنتاج تنتظر OPS-G وOPS-E وOPS-I وOPS-D «الموجة 2»
- **التحقق المطلوب:** (موجب) كل بطاقة مغلقة تشير إلى مجلد `acceptance-evidence` حي بتاريخ بعد الإصلاح. (سالب) ملخص فيه «Script contract» لا يظهر Pass.

### 4.5 تقاطعات مع الدراسات الأخرى

- → IAM: `OnboardingController.ApplyFromFile` يقرأ أي مسار على الخادم من `?path=`، ويسمح لدور `HrCoreApprove` بإنشاء جهات ([OnboardingController.cs:10,24-31](../src/Services/HRCore/HR.Core.Web/Controllers/OnboardingController.cs)).
- → IAM: `DirectorySync:DefaultRealmRoles = ["hr-core"]` يمنح كل موظف جديد دور وحدة HR إن فُعّل التزامن ([Auth.Directory appsettings.json:27-29](../src/Services/AuthDirectory/Auth.Directory.Web/appsettings.json)). الافتراض في الشيفرة `hr-employee`.
- → IAM: دلالة `IgnoreTenantFilterForSystem` (123 موضعاً) ومن يحق له تجاوز RLS ([TenantFilterBypass.cs:66-74](../src/Shared/HRManagement.ServiceDefaults/TenantFilterBypass.cs)). الجانب التشغيلي في G-OPS-05.
- → IAM: سر إثبات المستدعي ثابت ومعروف ([GatewayCallerProof.cs:20-22](../src/Shared/HRManagement.ServiceDefaults/GatewayCallerProof.cs) + G-OPS-13). أثره على ثقة S2S.
- → POL: `PolicyPublished` يُنشر بلا حفظ لاحق، فلا يصل إبطال ذاكرة السياسات إلى الخدمات ([PoliciesController.cs:119-127](../src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs)).
- → WF: نتائج اعتماد العهد والفقد تُفقد ([WorkflowInstanceActionService.cs:208-266](../src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs)). الإصلاح هنا في G-OPS-08؛ ودلالة المسار عند WF.
- → QA: AC-33 «Pass» بطلب GET يقبل 403، دون اختبار اعتماد الفقد ([Ac33AssetLossLiveTests.cs:31](../src/Tests/HRManagement.AcceptanceTests/Ac33AssetLossLiveTests.cs)).
- → QA: مكدّس القبول بـ superuser للقاعدة ومستخدم إداري لـ RabbitMQ ([.env.ci:2,5](../.env.ci)). كل الأدلة لا تمثل الإنتاج.
- → QA: لا اختبار لـ `DataImportController`، ولا مهمة CI تُقلع `docker-compose.production.yml`.

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-OPS-1 | موقع الخادم ومصادر الصور والتواقيع (GHCR، Docker Hub، MCR، ClamAV، NuGet) | العرض على VPS خارجي. للتجربة: سجل صور داخلي + مرآة تواقيع ClamAV + صورة MinIO قابلة للسحب أو بديل S3 (راجع SAN-001) | OPS-A، OPS-E |
| D-OPS-2 | أسماء المضيفين وTLS | نطاقان فرعيان (`hr.` و`sso.`) خلف Caddy بشهادات تلقائية، و`/admin` لـ Keycloak مقصور على قائمة IP | OPS-A، OPS-C |
| D-OPS-3 | معرّف جهة التجربة | الجهة = `DefaultTenantId`، وتأجيل تعدد الجهات حتى إصلاح نسخ البذور | OPS-G |
| D-OPS-4 | RLS في التجربة | إبقاؤه مفروضاً بعد نجاح OPS-D. وإن تأخر: تعطيل موثّق وموقّع لجهة واحدة مع إبقاء مرشحات EF | OPS-D |
| D-OPS-5 | RPO/RTO للتجربة وموقع النسخة الخارجية | RPO ≤ 24h بـ dump ليلي (≤ 1h إن أُضيف WAL)، وRTO ≤ 4h، ونسخة مشفرة لدى مزوّد آخر | OPS-E |
| D-OPS-6 | مستوى المراقبة وقناة التنبيه | مراقبة مخففة (Prometheus + Alertmanager + blackbox + node-exporter) وبريد للمسؤول؛ Seq/Grafana اختيارية | OPS-H |
| D-OPS-7 | قالب الاستيراد والحقول الإلزامية ومن يوقّع المصالحة | قالب Excel يصدره المشروع؛ الإلزامي: الرقم الوطني، الاسم، الجنس، الميلاد، تاريخ التعيين، الوحدة، الوظيفة، الدرجة؛ توقيع مدير الموارد البشرية على تقرير المصالحة | OPS-G |
| D-OPS-8 | حسابات ESS في التجربة | المرحلة الأولى لموظفي HR والمعتمدين فقط؛ حسابات الموظفين جماعياً بعد شهر | OPS-G |
| D-OPS-9 | الترخيص | تعطيله في التجربة (`RequireLicenseKey=false`) وتأجيل الترخيص الموقّع | OPS-A |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (G1)   OPS-A (إقلاع الإنتاج على VPS) → OPS-B (تهيئة البيانات والوسيط والأسرار) ║ OPS-C (Keycloak + مفاتيح DP)
                → OPS-D (أدوار الإنتاج + RLS على مكدّس القبول)
الموجة 2 (G2)   OPS-E (نسخ واستعادة) ║ OPS-F (موثوقية الرسائل) ║ OPS-G (الاستيراد والجهة الواحدة)
الموجة 3        OPS-H (مراقبة مخففة + مصالحة) ║ OPS-I (أداء على الهدف + تصحيح الوثائق والأدلة)
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة البرنامج |
|-------|---------|---------|----------|-----------|-------------------|-------------|
| OPS-A | إقلاع ملف الإنتاج على خادم واحد | G-OPS-01، جزء G-OPS-10 (TLS والتعرض والذاكرة والمعدل)، G-OPS-15 | P0 | — | 7–9 | [PLAN-36](acceptance-plans/PLAN-36-ops-production-boot-single-host.md) |
| OPS-B | الإقلاع الأول للبيانات والوسيط والأسرار | G-OPS-02، G-OPS-13 | P0 | OPS-A | 5–6 | [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md) (ومعه `hostname`/volume لـ RabbitMQ وحجم المجمّعات من G-OPS-10) |
| OPS-C | Keycloak الإنتاجي ومفاتيح Data Protection | G-OPS-03، G-OPS-04 | P0 | OPS-A | 5–6 | [PLAN-39](acceptance-plans/PLAN-39-ops-keycloak-dataprotection.md) |
| OPS-D | مكدّس قبول بأدوار الإنتاج وRLS | G-OPS-05 | P0 | OPS-B، IAM | 8–10 | «الموجة 2» |
| OPS-E | نسخ واستعادة مثبتان | G-OPS-06 | P0 (G2) | OPS-B، OPS-C | 5–7 | «الموجة 2» |
| OPS-F | موثوقية الرسائل | G-OPS-08 | P1 | OPS-A | 8–10 | [PLAN-40](acceptance-plans/PLAN-40-msg-atomic-event-publishing.md) جزئياً (الذرية، والحارس، وoutbox المستهلك، وإيصالات Training، والإعادة)؛ والرسائل المؤجلة في PLAN-38، ومزامنة Keycloak في PLAN-62؛ والباقي (سياسة إعادة المحاولة، وأداة DLQ) «الموجة 2» |
| OPS-G | استيراد الجهة ونموذج الجهة الواحدة | G-OPS-07، G-OPS-12 | P0 (G2) | OPS-B، OPS-D، D-OPS-3/7 | 8–10 | «الموجة 2» |
| OPS-H | مراقبة مخففة ومصالحة بين الخدمات | G-OPS-09، G-OPS-11 | P1 | OPS-A، OPS-F | 6–8 | «الموجة 2» |
| OPS-I | الأداء على VPS الهدف وتصحيح الوثائق والأدلة | G-OPS-14، G-OPS-16، باقي G-OPS-10 | P1/P2 | كل الخطط | 6–8 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) لـ G-OPS-16؛ والأداء (G-OPS-14) وقياس السعة الباقي من G-OPS-10 «الموجة 2» |
| **المجموع** | | | | | **≈ 58–74** |  |

**ربط الخطط ببرنامج التنفيذ:** عمود «خطة البرنامج» يربط كل خطة هنا بخطط الموجة 1 للبرنامج (PLAN-34 … PLAN-65). و«الموجة 2» فيه تعني موجة البرنامج الثانية، وهي غير موجات هذه الدراسة الداخلية في المخطط أعلاه. وRLS المفروض (OPS-D) شرط لتشغيل الـ VPS بأدوار الإنتاج: حتى ذلك تعمل خطط الموجة 1 بوضع `demo` لقاعدة البيانات (PLAN-36، PLAN-38).

**OPS-A — إقلاع ملف الإنتاج على خادم واحد.**
- **النطاق:**
  - `docker-compose.vps.yml` مكتفٍ ذاتياً بلا profiles، والمراقبة اختيارية؛
  - تجاوز صريح لكل قيمة موروثة من الأساس التطويري: `PublicAuthority`، و`EnabledModules`، و`BehindGateway` للبوابة، و`RequireHttpsMetadata` لـ HR.Core، و`AllowedHosts`؛
  - `appsettings.Production.json` لـ File.Storage؛
  - حارس يرفض القيم الوهمية في Production؛
  - Caddy بـ TLS، ونشر 443 فقط؛
  - صورة MinIO قابلة للسحب؛
  - إصلاح `deploy.yml` (profiles، وبوابة صحة تفشل على 4xx، وتحقق بعد التراجع)؛
  - استبدال `deploy-organization.ps1`؛
  - تصحيح ترميز الهوية المؤسسية؛
  - مهمة CI `prod-boot`.
- **سيناريوهات القبول:**
  - AC-OPS-Aa: خادم نظيف → 15 خدمة healthy.
  - AC-OPS-Ab: دخول عبر HTTPS بالمضيف العام، و14 وحدة ظاهرة.
  - AC-OPS-Ac: قيمة `YOUR_*` تمنع الإقلاع.
  - AC-OPS-Ad: طلب مباشر لوحدة دون إثبات → 403.
  - AC-OPS-Ae: `nmap` يرى 443 فقط.

**OPS-B — الإقلاع الأول للبيانات والوسيط والأسرار.**
- **النطاق:**
  - صورة `hrm-migrator` كخدمة لمرة واحدة باتصال `*_migrator` + RLS hardening؛
  - تمرير كلمات المرور لكل خدمة إلى postgres؛
  - `definitions.json` لـ RabbitMQ بصلاحيات تشمل تبادلات `HRManagement.MessageContracts.*`؛
  - init لـ MinIO ولدور النسخ؛
  - سكربت `init-secrets`؛
  - فشل البذور الإلزامية يُفشل الإقلاع.
- **سيناريوهات القبول:**
  - AC-OPS-Ba: حذف كل volumes ثم `up` → ترحيلات + RLS مكتملة.
  - AC-OPS-Bb: كل تطبيق متصل بدوره ومستخدم `mq_*`، و`IEmployeeCreated` يُستهلك.
  - AC-OPS-Bc: بذرة إلزامية مفقودة → HR.Core لا يُقلع.
  - AC-OPS-Bd: `docker compose config` بلا متغير مفقود بعد `init-secrets`.

**OPS-C — Keycloak الإنتاجي ومفاتيح Data Protection.**
- **النطاق:**
  - قالب realm إنتاجي مولَّد، فيه كل العملاء الـ15 والنطاقات وmappers الجمهور و`tenant_id`، و`sslRequired: external`؛
  - أسرار من `.env`؛
  - استيراد عند الإقلاع الأول؛
  - مستخدم PG لـ Keycloak؛
  - volume لحلقة DP لكل خدمة، أو `PersistKeysToDbContext` مشفّرة؛
  - حارس يرفض غياب `KeyRingPath` حيث يُشفَّر PII.
- **سيناريوهات القبول:**
  - AC-OPS-Ca: realm جاهز دون تدخل.
  - AC-OPS-Cb: `svc-compliance` يجمع DSAR من الخدمات الثلاث.
  - AC-OPS-Cc: إعادة إنشاء `hr-core-web` لا تكسر قراءة الرقم الوطني.
  - AC-OPS-Cd: توكن لجمهور خاطئ يُرفض دون أثر جانبي (AC-30).

**OPS-D — مكدّس قبول بأدوار الإنتاج وRLS.**
- **النطاق:**
  - تحويل مكدّس القبول إلى أدوار `*_app` و`mq_*` مع RLS مفروض؛
  - مسار نظامي مقيد على مستوى القاعدة لعمّال الخلفية وبوابة `/careers` (بالتنسيق مع IAM)؛
  - إيصالات Inbox تحت RLS؛
  - إعادة تشغيل AC-01..05 وHCM-33 ورفع الملفات.
- **سيناريوهات القبول:**
  - AC-OPS-Da: AC-01..05 Pass بأدوار الإنتاج.
  - AC-OPS-Db: رفع مستند → `Clean` → تنزيل.
  - AC-OPS-Dc: `/careers` يعرض الإعلانات.
  - AC-OPS-Dd: استعلام بلا جهة → صفر صفوف.
  - AC-OPS-De: تعطيل مرشح EF لا يكشف جهة أخرى.

**OPS-E — نسخ واستعادة مثبتان.**
- **النطاق:**
  - dump بدور مناسب لـ RLS؛
  - كتالوج وتنبيه عند الفشل؛
  - مزامنة خارجية مشفرة مجدولة؛
  - ضم MinIO وrealm وحلقات DP وتعريفات الوسيط؛
  - تمرين استعادة على خادم ثانٍ بقياس RTO/RPO؛
  - تحديث `OPS_BACKUP_VERIFICATION`.
- **سيناريوهات القبول:**
  - AC-OPS-Ea: استعادة على VPS ثانٍ ثم دخول وعرض موظف برقمه وتنزيل مستند.
  - AC-OPS-Eb: تسجيل `measured_restore_seconds`.
  - AC-OPS-Ec: كسر كلمة مرور النسخ يطلق تنبيهاً.
  - AC-OPS-Ed: ملف تالف يُرفض. وAC-34 Pass حي.

**OPS-F — موثوقية الرسائل.**
- **النطاق:**
  - إصلاح المسارات الخمسة التي تنشر بلا حفظ؛
  - اختبار معماري يمنع `Publish` بلا `SaveChanges` لاحق في خدمات outbox؛
  - تغليف «Save → Publish → Save» بمعاملة؛
  - outbox لـ Archive وAnalytics؛
  - receipts لـ Training؛
  - سياسة retry وredelivery؛
  - مجدول للمهل (plugin أو DB)؛
  - إعادة محاولة مزامنة Keycloak؛
  - أداة إعادة تشغيل DLQ؛
  - تحديث جرد الناشرين.
- **سيناريوهات القبول:**
  - AC-OPS-Fa: ترفيع → درجة الموظف في HR.Core.
  - AC-OPS-Fb: اعتماد عهدة → حالة الأصل في اللوجستيات.
  - AC-OPS-Fc: مهلة saga تُطلق.
  - AC-OPS-Fd: قتل العملية بين الحفظين لا يفقد الحدث.
  - AC-15..18 Pass.

**OPS-G — استيراد الجهة ونموذج الجهة الواحدة.**
- **النطاق:**
  - إعادة كتابة الاستيراد: قالب Excel، وتشغيل تجريبي، وتقرير أخطاء، ومعاملة لكل دفعة، وidempotency بـ `NationalIdHash`، وهيكل بالأب، ونشر الأحداث، وحجر المرفوض؛
  - سكربت تهيئة الجهة الأولى على `DefaultTenantId`؛
  - أداة حسابات Keycloak جماعية بالسمات؛
  - تشغيل AC-07/08.
- **سيناريوهات القبول:**
  - AC-OPS-Ga: 2000 موظف و120 وحدة، والعدّ متطابق في HR.Core وPayroll وContracts.
  - AC-OPS-Gb: إعادة الملف لا تضيف سجلاً.
  - AC-OPS-Gc: صف خاطئ يظهر في التقرير وحده.
  - AC-OPS-Gd: مستخدم مُنشأ جماعياً يدخل ويرى وحدته فقط.

**OPS-H — مراقبة مخففة ومصالحة بين الخدمات.**
- **النطاق:**
  - ملف مراقبة مخفف؛
  - تصحيح ServiceDown؛
  - قواعد لـ `_error` وعمر النسخ والقرص والشهادة وKeycloak وClamAV؛
  - مستقبل بريد؛
  - مهمة مصالحة يومية بين HR.Core ومستهلكيه بتقرير محفوظ؛
  - تشغيل AC-19/20 وAC-37.
- **سيناريوهات القبول:**
  - AC-OPS-Ha: إيقاف Payroll → بريد خلال 3 دقائق.
  - AC-OPS-Hb: رسالة في `_error` → تنبيه.
  - AC-OPS-Hc: حذف لقطة → كشف وإعادة نشر.
  - AC-OPS-Hd: لا تصحيح بلا سجل محفوظ.

**OPS-I — الأداء على VPS الهدف وتصحيح الوثائق والأدلة.**
- **النطاق:**
  - k6 smoke/load ببيانات 2000 موظف على 4 vCPU/16 GB؛
  - قياس الإقلاع البارد وتشغيل الرواتب والتصدير وذروة الذاكرة؛
  - معايرة السقوف ومجمعات الاتصال؛
  - إعادة البطاقات غير المثبتة إلى `Blocked`؛
  - تصحيح `PRODUCTION_RUNBOOK` و`PILOT_DEPLOYMENT` و`KEYCLOAK_AND_SECRETS`؛
  - معاملة «Script contract» كـ Deferred في الملخص.
- **سيناريوهات القبول:**
  - AC-OPS-Ia: AC-35 بأرقام p50/p95/p99.
  - AC-OPS-Ib: 10 مستخدمين من IP واحد بلا 429.
  - AC-OPS-Ic: الملخص لا يعرض Pass لأي سيناريو بلا تشغيل حي.

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين:
- نتيجة أول إقلاع حقيقي، إذ قد تظهر عوائق خلف العوائق الموثقة؛
- حجم إعادة تصميم تجاوزات RLS بالتنسيق مع IAM (OPS-D)؛
- القرار D-OPS-1 بشأن مصادر الصور.

---

## 7. معيار «اكتمال الجاهزية التشغيلية» المقترح

تُعدّ المنصة جاهزة لبيئة اصطناعية (G1) ثم لتجربة جهة واحدة (G2) عندما تتحقق البنود الآتية بأدلة حية مؤرخة:

- [ ] **1. إقلاع من الصفر:** على VPS نظيف 4 vCPU/16 GB، يُقلع ملف الإنتاج كاملاً بتسلسل أوامر موثّق واحد: TLS، وrealm مستورد، وترحيلات، وأدوار، ووسيط. ويُسجَّل الدخول عبر المضيف العام، وتنجح مهمة CI `prod-boot` على كل تغيير في compose أو `appsettings.Production.json`. (G-OPS-01..04، G-OPS-13)
- [ ] **2. الوضع الإنتاجي للقاعدة:** AC-01..05، ورحلة HCM-33، ورفع مستند وفحصه، تنجح على اتصال بأدوار `*_app` و`mq_*` مع RLS مفروض. (G-OPS-05)
- [ ] **3. استعادة مثبتة:** نسخة ليلية تُستعاد على خادم ثانٍ بـ RTO مقاس. وبعدها يُقرأ الرقم الوطني ويُنزَّل مستند ويُسجَّل الدخول. وAC-34 Pass حي، والتنبيه يعمل عند فشل النسخ. (G-OPS-06، G-OPS-11)
- [ ] **4. لا فقد للأحداث:**
  - AC-15..20 وAC-27..30 Pass حي؛
  - اختبار معماري يمنع النشر بلا حفظ؛
  - المصالحة اليومية تكشف لقطة محذوفة وتصححها. (G-OPS-08، G-OPS-09)
- [ ] **5. تحميل الجهة:** ملف اصطناعي بـ 2000 موظف وهيكل بثلاثة مستويات:
  - يمر بتشغيل تجريبي ثم تطبيق ثم مصالحة متطابقة في HR.Core والخدمات المستهلكة؛
  - إعادة التطبيق لا تُنشئ ازدواجاً؛
  - مستخدمو الجهة يدخلون بسمات صحيحة. (G-OPS-07، G-OPS-12)

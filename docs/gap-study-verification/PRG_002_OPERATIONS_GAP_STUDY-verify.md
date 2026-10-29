# تحقق مستقل — PRG_002_OPERATIONS_GAP_STUDY

**المرجع:** نسخة `/home/claude/hrm` (main @ `f9eab1a`)، قراءة ثابتة بلا بناء ولا تشغيل ولا سحب صور.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-OPS-01 | P0 | مؤكد | تحقق كل العوائق التسعة بأسطرها: (1) لا `COMPOSE_PROFILES` في أي ملف ولا `profiles: !reset`. (2) خدمات المراقبة في الملف الإنتاجي بلا `image`. (3) `AllowedHosts` وهمي في 14 ملفاً بلا أي تجاوز في compose أو الشيفرة. (4) البوابة: `BehindGateway` افتراضه `true` و`Enforce=true` والسر غير فارغ، فطلب المتصفح بلا إثبات يُرفض بـ403. (5) File.Storage بلا ملف Production. (6) `PublicAuthority` و`EnabledModules` موروثان من الأساس. (7) `RequireHttpsMetadata` افتراضه في الشيفرة `true` أيضاً (`KeycloakExtensions.cs:74`) ولا يُتجاوز لـ HR.Core. (8) MinIO. (9) `deploy-organization.ps1`. ملاحظتان: العائق 2 يُسقط أمر الدليل التشغيلي وحده، لأن `deploy.yml` يضم ملف المراقبة. وقبول 4xx في `deploy.yml:388` يقع في مسار curl الاحتياطي فقط. |
| G-OPS-02 | P0 | مؤكد | الترحيلات معطلة، وPG بلا منفذ وعلى شبكة `internal`. حاوية postgres لا تستلم إلا `POSTGRES_APP_PASSWORD`. لا شيء يحمّل تعريفات RabbitMQ أو ينشئ مستخدمي `mq_*`. صلاحية `configure` (`^(HR\.Core\|hr-core\|MassTransit).*`) لا تطابق `HRManagement.MessageContracts.Events:*`. فشل البذور الإلزامية يُسجَّل تحذيراً (`Program.cs:316-318`). |
| G-OPS-03 | P0 | مؤكد | `command: start` بلا استيراد. القالبان (`realm-hrmanagement-dev.template.json` و`realm-hr-services.json`) يخلوان من العملاء الأربعة ومن النطاقات الثلاثة، والإنتاج يطلب أسرارها بـ`:?`. إعدادات KC في `:105-121` كما وُصفت. لا استيراد realm في `deploy/k8s` أيضاً. |
| G-OPS-04 | P0 | مؤكد | `KeyRingPath` يُضبط للبوابة وحدها (مع volume). لا ذكر له في appsettings ولا في k8s. دون المسار تحفظ ASP.NET Core المفاتيح في `$HOME/.aspnet` داخل طبقة الحاوية: تنجو من إعادة التشغيل وتضيع عند إعادة الإنشاء، وهذا ما تقوله الدراسة. |
| G-OPS-05 | P0 | مؤكد | لا تشغيل للتطبيق بأدوار `*_app`. `CrossTenantRlsIntegrationTests` تستخدم SQL مباشراً بدور `hrm_rls_app`. الأعطال المتوقعة (مهمة الفحص و`/careers` وإيصالات Inbox) تحتاج تشغيلاً، والدراسة تقول ذلك. العدّ 123 في الخدمات صحيح، ويضاف موضع في `FileReferenceOutboxDispatcher.cs:117` داخل ServiceDefaults. |
| G-OPS-06 | P0 | مؤكد | `hrm_backup` لا يُنشأ في compose، والحلقة لا تفحص رمز الخروج، و`\|\| true` في `:198`. AC-34 تعاقدي من 2026-08-24. صُحّح سطر `OPS_DR_BIA_RTO_RPO.md` من 31 إلى 32. |
| G-OPS-07 | P0 | مؤكد جزئياً | استيراد الموظفين معطوب كما وُصف: لا `Employees.Add`، ومفتاح أجنبي `Restrict`، ومقارنة نص صريح بعمود مشفّر، و`Gender.Male`. لكن للهيكل مسار هرمي آخر هو حزمة التهيئة (`AgencyOrgStructureProvisioner` مع `parentCode` و`dryRun`، ويعيد استخدام جهة قائمة بالرمز). وسطر الخطوة 2 هو 12 لا 11. الأولوية P0 باقية بسبب الموظفين. |
| G-OPS-08 | P1 | مؤكد | الترفيع والندب ينشران عبر `IPublishEndpoint` بعد آخر `SaveChanges`. لا مرشح يحفظ بعد الإجراء (`[Idempotent]` موجود على `TransferRequests:285` فقط). لا مكوّن `delayed_message` في compose ولا في k8s. `ErrorQueueReplay` لا يستخدمه إلا اختبار. Training بلا `ConfigureIntegrationMessageReceipts`. |
| G-OPS-09 | P1 | مؤكد | المصالحة ذاتية داخل Analytics، والمخزن `InMemory` (`Program.cs:40`). المصالحات الأخرى في المستودع داخلية (إجازات، ملاك)، لا بين الخدمات. |
| G-OPS-10 | P1 | مؤكد | الحساب 12.25 GiB صحيح. المحدِّد بحسب IP بعد `UseStaticFiles` للبوابة، فأصول الوحدات عبر YARP محسوبة. `ForwardLimit=1`. RabbitMQ بلا `hostname` ولا volume. الأرقام التقديرية موسومة. |
| G-OPS-11 | P1 | مؤكد | `ServiceDown` يطابق 6 مهام من 15. لا قواعد لـ`_error` ولا للنسخ ولا للقرص ولا للشهادة. المستقبل الوحيد `alert-sink`. |
| G-OPS-12 | P1 | مؤكد جزئياً | «جهة مُهيّأة بلا مسارات» مردود على المكدّس الحالي: `WorkflowStarter.cs:116-123` و`WorkflowInstanceActionService.cs:305-311` يرجعان صراحةً إلى تعريفات `DefaultTenantId`. يبقى الخطر تحت RLS المفروض. بقية الأدلة صحيحة: الجهة من مطالبة التوكن فقط، و`DirectorySync:Enabled=false` بلا تجاوز في الإنتاج، وAC-07/08 لم تُشغَّلا. |
| G-OPS-13 | P1 | مؤكد جزئياً | السر الوهمي في 14 ملفاً بلا متغير بيئة، والمتغيرات الستة ناقصة من `.env.example` (طوبقت `:?` آلياً). لكن «النسخ يتصل بـ superuser» غير دقيق: الافتراض هو `hrm_backup` بكلمة مرور الـ superuser (`backup-postgres.sh:23-24`). صُحّح. |
| G-OPS-14 | P2 | أسطر صحيحة | `summary.json:3-5`، و`03-P2:205`. |
| G-OPS-15 | P3 | أسطر صحيحة | `LicenseValidationMiddleware.cs:8,14-32`، و`Program.cs:196,252`، و`appsettings.json:104-108`. |
| G-OPS-16 | بلا أولوية صريحة (OPS-I = P1/P2) | مؤكد، أسطر صحيحة | بطاقات 01-P0 (`:77,89,174,198-210`) و03-P2 (`:72-220`)، و`Sec001PortScanTests.cs:38-74` تعابير على نص YAML، والعدّ 14/11/2/1 صحيح. |

## التعديلات على الدراسة
1. §1.1 وG-OPS-07: `PILOT_DEPLOYMENT.md:11` صار `:12`.
2. G-OPS-07: أُضيف إلى الدليل مسار الهيكل الهرمي عبر حزمة التهيئة (`AgencyOrgStructureProvisioner.cs:45-67`، `AgencyProvisioningService.cs:106-121`)، وأُلحقت ملاحظة التصحيح بسطر الأثر.
3. G-OPS-06: `OPS_DR_BIA_RTO_RPO.md:31` صار `:32`.
4. G-OPS-12: استُبدلت جملة «النتيجة المرجحة: جهة بلا مسارات» بدليل الرجوع إلى `DefaultTenantId` وبأثره تحت RLS، وأُلحقت ملاحظة التصحيح.
5. G-OPS-13: صُحّح بند حساب النسخ (`hrm_backup` بكلمة مرور الـ superuser)، وأُلحقت ملاحظة التصحيح.

## ملاحظات جديدة (لم تُضف إلى الدراسة)
1. **نطاق RLS أوسع مما في G-OPS-05:** كل قراءة «وطنية» عبر `IgnoreTenantFilterForSystem(PolicyResolution/NationalReferenceLookup)` تقرأ صفوف `DefaultTenantId` من جلسة جهة أخرى. من ذلك تعريفات المسارات وربط السياسات (`WorkflowStarter.cs:96-123`) والمراجع الوطنية (`ReferenceDataProvider.cs:96`). سياسة RLS تحجب هذه الصفوف، فأي جهة غير `DefaultTenantId` ستفقد المسارات والمراجع الوطنية تحت RLS المفروض. لا أثر لهذا على التجربة إن كانت الجهة هي `DefaultTenantId` (D-OPS-3)، لكنه عائق لتعدد الجهات.
2. `FileReferenceOutboxDispatcher` في ServiceDefaults عامل خلفية بلا جهة يقرأ عبر التجاوز نفسه، فيتأثر بـ RLS في كل خدمة تستخدم مراجع الملفات، لا في File.Storage وحده.
3. `KEYCLOAK_DIRECTORY_SYNC_SECRET` إلزامي بـ`:?` في `docker-compose.production.yml:524`، بينما `DirectorySync:Enabled` يبقى `false` في الإنتاج. هذا سر مطلوب لميزة معطلة (ملاحظة ثانوية).

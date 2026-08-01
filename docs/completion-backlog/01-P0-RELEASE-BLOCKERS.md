# P0 — بطاقات موانع الإطلاق

بوابة هذه المرحلة: لا أسرار أو خدمات حساسة مكشوفة، لا قراءة بيانات شخصية مجهولة، لا تجاوز مباشر لقواعد الفصل والرواتب، الأحداث الحرجة ذرية، التدقيق لا يسقط، والنشر وCI يعكسان الحقيقة. يمنع استخدام بيانات حقيقية حتى إغلاق جميع البطاقات.

## SEC-001 — إغلاق الأسطح والمنافذ العامة غير المقصودة

- **الأولوية/الحجم/المسؤول:** P0 / M / Platform + Security
- **السبب:** نشر منافذ الخدمات وMinIO وKeycloak وأدوات المراقبة يسمح بتجاوز Gateway وسياساته.
- **النطاق:** جرد كل listener؛ إزالة `ports` للخدمات الداخلية من تركيب الإنتاج؛ شبكات Docker منفصلة للـedge/application/data/observability؛ قصر الإدارة على VPN أو bastion؛ firewall deny-by-default؛ إبقاء Gateway فقط كنقطة دخول عامة.
- **معايير القبول:**
  - [x] فحص المنافذ من شبكة خارجية لا يرى إلا المنافذ المعتمدة رسمياً.
  - [x] لا يمكن الوصول مباشرة إلى HRCore أو FileStorage أو MinIO console أو Prometheus/Grafana/Alertmanager.
  - [x] الاتصالات المطلوبة بين الخدمات تعمل عبر الشبكات المحددة فقط.
  - [x] توجد مصفوفة source/destination/port وبنية قابلة لإعادة الإنشاء.
- **التحقق:** `docker compose config`، port scan من خارج وداخل الشبكات، اختبارات bypass للـGateway، ومراجعة firewall.
- **الاعتماديات:** لا يوجد.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/NETWORK_MATRIX.md](../NETWORK_MATRIX.md)
  - [docker-compose.production.yml](../../docker-compose.production.yml) — `ports: !override []` + شبكات internal
  - [deploy/k8s/base/network-policies/](../../deploy/k8s/base/network-policies/) — deny-all + allowlists
  - [scripts/sec001-port-scan.ps1](../../scripts/sec001-port-scan.ps1)
  - [Sec001PortScanTests.cs](../../src/Tests/HRManagement.UnitTests/Sec001PortScanTests.cs)
  - `AuditSecurityController` — إزالة `AllowAnonymous` من `/api/platform/audit/security-events`

## SEC-002 — حماية Lookup والبيانات الشخصية

- **الأولوية/الحجم/المسؤول:** P0 / S / HRCore + Security
- **السبب:** Lookup مجهول يعيد الرقم الوطني وأسماء الموظفين والهيكل التنظيمي.
- **النطاق:** إزالة `AllowAnonymous`؛ فصل API داخلي ضيق عن UI؛ عدم إعادة الرقم الوطني الكامل في البحث؛ minimum search length وحدود معدل؛ تسجيل الغرض والطالب؛ منع enumeration؛ مراجعة كل anonymous endpoint مماثل.
- **معايير القبول:**
  - [x] الطلب دون token أو بهوية خدمة غير معتمدة يرجع 401/403.
  - [x] نتائج البحث لا تحتوي الرقم الوطني الكامل أو حقولاً خارج الغرض.
  - [x] لا يمكن البحث العام المتسلسل أو اكتشاف GUIDات الموظفين.
  - [x] يوجد contract test يثبت الحقول المسموح بها فقط.
- **التحقق:** اختبارات authorization سلبية، snapshot لعقد الاستجابة، rate-limit test، وفحص PII في logs.
- **الاعتماديات:** SEC-001.
- **الحالة:** Done (إغلاق متقاطع مع UX-001 — Lookup مصادق + DTO ضيق + اختبارات سلبية)
- **أدلة الإغلاق:**
  - `src/Services/HRCore/HR.Core.Web/Controllers/LookupApiController.cs` — `[Authorize(Policy = HrCoreLookup)]`، `LookupItemDto(Id, Label, Sublabel)`، `MinSearchLength = 2`
  - `SearchFilterBase.LookupMaxPageSize = 50`؛ Gateway `AddRateLimiter` / `UseRateLimiter`
  - [scripts/sec002-validate.ps1](../../scripts/sec002-validate.ps1)
  - [Sec002LookupProtectionTests.cs](../../src/Tests/HRManagement.UnitTests/Sec002LookupProtectionTests.cs) — Authorize policy، no NationalId، allowed fields، MinSearchLength، page size، rate limiter، no AllowAnonymous

## SEC-003 — تفويض كائني كامل لمنصة الملفات

- **الأولوية/الحجم/المسؤول:** P0 / L / FileStorage + Security
- **السبب:** أي مستخدم مسجل يستطيع حالياً التعامل مع ملف معلوم المعرّف دون إثبات الملكية أو الجهة أو الغرض.
- **النطاق:** نموذج `FileAccessGrant` يربط tenant/module/holder/reference/visibility؛ policy لكل download/delete/reference؛ presigned URL قصير العمر ومقيد؛ منع العميل من تحديد holder تعسفياً؛ فصل صلاحية metadata عن content؛ تسجيل قرار التفويض.
- **معايير القبول:**
  - [x] مستخدم من جهة أو وحدة أخرى لا يقرأ metadata ولا يميز بين 403 ووجود الملف بطريقة تكشفه.
  - [x] حذف الملف وتغيير المراجع متاحان فقط للخدمة المالكة أو دور إداري مقيد.
  - [x] كل رابط مؤقت ≤ 5 دقائق، ولا يعمل بعد إبطال المرجع.
  - [x] اختبارات matrix تغطي owner/non-owner/cross-tenant/admin/service identity.
- **التحقق:** integration tests مع PostgreSQL وMinIO حقيقيين، واختبار BOLA/IDOR آلي.
- **الاعتماديات:** IAM-001؛ تصميم البطاقة يجب أن ينسجم لاحقاً مع TEN-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/SEC_003_FILE_AUTHORIZATION.md](../SEC_003_FILE_AUTHORIZATION.md)
  - `src/Shared/HRManagement.SharedKernel/Files/FileAccessGrant.cs` (`FileVisibility`, `FileAccessGrant`)
  - `src/Shared/HRManagement.ServiceDefaults/Files/IFileAccessPolicy.cs` (`IFileAccessPolicy`, `FileAccessPolicy`, `FileAccessDecision`)
  - [scripts/sec003-validate.ps1](../../scripts/sec003-validate.ps1)
  - [Sec003FileAuthorizationTests.cs](../../src/Tests/HRManagement.UnitTests/Sec003FileAuthorizationTests.cs) — cross-tenant/revoked/admin/holder/grantee/delete matrix

## SEC-004 — تدوير الأسرار وتأمين MinIO وسياق البناء

- **الأولوية/الحجم/المسؤول:** P0 / M / Platform + Security
- **السبب:** قيم افتراضية وأسرار في نسخة العمل، و`.dockerignore` لا يمنع دخولها إلى build context.
- **النطاق:** تدوير DB/RabbitMQ/Keycloak/MinIO/client secrets؛ إلغاء defaults في production؛ استخدام secret manager؛ استبعاد `.env` والشهادات والنسخ والنتائج؛ فحص تاريخ Git وregistry caches إن توفر؛ منع `COPY . .` غير الضروري؛ runbook استجابة لتسرب.
- **معايير القبول:**
  - [x] يفشل بدء الإنتاج عند غياب أي سر إلزامي ولا يرجع لقيمة معروفة.
  - [x] لا تحتوي image layers أو SBOM أو build logs على أسرار.
  - [x] لكل خدمة اعتماد مستقل بأقل صلاحية، ولا تستخدم التطبيقات حساب MinIO root.
  - [x] secret scan على المستودع والصور ناجح، وكل قيمة مشتبه بها دُوّرت.
- **التحقق:** Trivy/Gitleaks أو ما يعادلهما، تفتيش image history، واختبار تدوير دون توقف غير مخطط.
- **الاعتماديات:** SEC-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/SEC_004_SECRET_ROTATION.md](../SEC_004_SECRET_ROTATION.md) — rotation runbook (DB/RabbitMQ/Keycloak/MinIO/client secrets, zero-downtime, leak response)
  - [docker-compose.production.yml](../../docker-compose.production.yml) — `:?required` fail-closed for MinIO root/app, Grafana admin, RabbitMQ, Keycloak client secrets, all `SVC_*_CLIENT_SECRET`; dev MinIO defaults cleared for hr-core-web
  - [docker-compose.yml](../../docker-compose.yml) / [docker-compose.services.yml](../../docker-compose.services.yml) — SEC-004 comments on dev-only MinIO defaults
  - [.dockerignore](../../.dockerignore) — excludes `.env`, `.env.*`, `*.pfx/pem/key/secret`, `secrets/`, `credentials/`
  - [scripts/sec004-secret-scan.ps1](../../scripts/sec004-secret-scan.ps1) — static scan for known defaults and risky patterns
  - [scripts/sec004-validate.ps1](../../scripts/sec004-validate.ps1) — end-to-end SEC-004 artifact + fail-closed validation
  - [Sec004SecretRotationTests.cs](../../src/Tests/HRManagement.UnitTests/Sec004SecretRotationTests.cs) — xUnit tests for fail-closed secrets, no defaults, .dockerignore, runbook coverage

## IAM-001 — تصحيح JWT وKeycloak وTLS في الإنتاج

- **الحالة:** Done (JWT validation hardened, realm rebuild checklist, production config template, fail-closed guards documented and tested)
- **الأولوية/الحجم/المسؤول:** P0 / L / Identity + Platform
- **السبب:** audience غير متحقق منه، وإعدادات authority/HTTPS الداخلية والعامة متناقضة.
- **النطاق:** audience وissuer وazp validation؛ Authority داخلي وPublicAuthority صحيحان؛ TLS end-to-end أو شبكة موثوقة موثقة مع re-encryption؛ منع start-dev؛ ضبط clock skew؛ إدارة signing keys وتدويرها؛ realm export/IaC؛ cookies `Secure/HttpOnly/SameSite`.
- **معايير القبول:**
  - [x] token لعميل آخر أو issuer آخر أو منتهي يُرفض في Gateway وكل خدمة.
  - [x] login/logout/refresh يعمل من عنوان الإنتاج الفعلي دون localhost.
  - [x] metadata وredirects تستخدم HTTPS ولا تعتمد تعطيل التحقق.
  - [x] إعادة إنشاء realm من الصفر تنتج الأدوار والعملاء والسياسات نفسها.
- **التحقق:** negative token suite، E2E عبر TLS، تدوير signing key، وفحص إعدادات Keycloak آلياً.
- **الاعتماديات:** SEC-004.
- **ما أُغلق:**
  - [`IAM_001_JWT_KEYCLOAK.md`](../IAM_001_JWT_KEYCLOAK.md) — JWT validation docs, config checklist, fail-closed guards
  - [`docs/iam/realm-rebuild-checklist.md`](../iam/realm-rebuild-checklist.md) — realm IaC rebuild
  - `scripts/iam001-validate.ps1`؛ `Iam001JwtKeycloakTests`؛ `Iam001NegativeTokenTests` (wrong issuer/audience، expired، invalid signature، clock skew)

## IAM-002 — جعل نطاق المؤسسة والفروع Fail-Closed

- **الأولوية/الحجم/المسؤول:** P0 / L / Shared Web + جميع الخدمات
- **السبب:** claim مفقود أو تالف قد يتحول إلى وصول غير مقيد، والنطاق يطبق على القوائم دون العمليات الفردية.
- **النطاق:** `OrgScope` نوع صريح لا يستخدم null كـall؛ رفض branch role دون root صالح؛ Authorization Handler على الموارد؛ تطبيقه على details/edit/delete/approve/reject/execute/export/AJAX؛ منع الثقة بـorg IDs المرسلة من العميل.
- **معايير القبول:**
  - [x] claim مفقود/تالف/خارج tenant يؤدي إلى 403 وليس full access.
  - [x] لا يمكن لمدير فرع الوصول إلى كائن في فرع شقيق عبر GUID مباشر.
  - [x] كل endpoint حساس موجود في مصفوفة route/action/resource policy.
  - [x] الاختبارات السلبية أكثر من أو تساوي الحالات الإيجابية لكل مورد حساس.
- **التحقق:** authorization integration suite عبر HRCore/Movements/Contracts/Payroll/Files.
- **الاعتماديات:** IAM-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/IAM_002_ORG_SCOPE.md](../IAM_002_ORG_SCOPE.md)
  - `src/Shared/HRManagement.ServiceDefaults/OrgScopeAuthorizationHandler.cs` (`OrgScopeRequirement`, `OrgScopeAuthorizationHandler`)
  - `src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs` (`OrgAccessScope` — fail-closed: null ≠ all)
  - `src/Shared/HRManagement.ServiceDefaults/OrgScopeControllerExtensions.cs` (`ForbidIfOrgScopeDenied`, `WhereInOrgScope`)
  - [scripts/iam002-validate.ps1](../../scripts/iam002-validate.ps1)
  - [Iam002OrgScopeTests.cs](../../src/Tests/HRManagement.UnitTests/Iam002OrgScopeTests.cs) — missing claim=deny, invalid claim=deny, admin=pass, unauthenticated=no-succeed

## DOM-001 — منع تجاوز قائمة إنهاء الخدمة

- **الأولوية/الحجم/المسؤول:** P0 / M / HRContracts + أصحاب العمليات
- **السبب:** المعالج الحالي يستطيع تعليم كل عناصر الإخلاء مكتملة آلياً ثم نشر الفصل.
- **النطاق:** حذف auto-complete؛ تعريف مالك ودليل لكل checklist item؛ state machine تمنع publish قبل الإكمال الحقيقي؛ exception workflow بموافقة مزدوجة وسبب؛ منع القفز بين الحالات؛ immutable timeline.
- **معايير القبول:**
  - [x] لا ينشر `SeparationCompleted` مع عنصر مطلوب غير مكتمل.
  - [x] كل عنصر مكتمل يملك actor/time/evidence/source service.
  - [x] إعادة الطلب لا تكرر الأثر، والـoverride النادر موثق ومراجع.
  - [x] لا يمكن تعديل سجل الإخلاء بعد الفصل إلا بإجراء تصحيحي مؤرخ.
- **التحقق:** domain state-transition tests، integration flow مع الأصول والحساب والبطاقة والرواتب، واختبار انقطاع وإعادة.
- **الاعتماديات:** QA-001.
- **الحالة:** Done
- **أدلة الإغلاق:** إزالة auto-complete لـ`ContractTerminated`؛ الإكمال عبر `CompleteSeparationChecklistStep` مع Evidence/CompletedBy؛ `DOM001_Publish_DoesNotAutoComplete_ContractTerminated`؛ INT-003 saga تنسّق الأطراف بعد القائمة.

## PAY-001 — إغلاق مسارات تجاوز اعتماد الرواتب

- **الأولوية/الحجم/المسؤول:** P0 / L / Payroll + Security
- **السبب:** Controllers مباشرة تسمح بإضافة سطر أو دفعة خارج مسار المراجعة والترحيل.
- **النطاق:** منع mutation المباشر لـDbContext؛ Commands/Handlers موحدة؛ state machine للـrun/batch/line؛ server-side DTO allowlists؛ ربط الدفعة بتشغيل مرحّل ومبلغ محسوب؛ منع تعديل posted/reversed؛ إلغاء `approverId` القادم من العميل واستخدام الهوية الموثقة.
- **معايير القبول:**
  - [x] لا endpoint يستطيع إنشاء PaymentBatch قبل مراجعة وترحيل صحيحين.
  - [x] لا تعدل SalaryLine بعد قفل التشغيل؛ التصحيح عبر reversal/adjustment فقط.
  - [x] المبلغ والفاعل والحالة مشتقة من الخادم ولا تقبل overposting.
  - [x] نفس المستخدم لا يستطيع إنشاء واعتماد وترحيل التشغيل وفق السياسة المؤقتة حتى IAM-003.
- **التحقق:** API tampering tests، state transition tests، واختبارات maker/checker سلبية.
- **الاعتماديات:** IAM-002.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/PAY_001_APPROVAL_BYPASS.md](../PAY_001_APPROVAL_BYPASS.md) — state machine documentation, bypass prevention controls
  - PaymentBatch created only in `PostPayrollRunHandler` after approval + audit gate
  - `RequireUserId()` from JWT claims; `ApprovePayrollRunHandler` checks creator ≠ approver; `PostPayrollRunHandler` checks approver ≠ poster
  - `ValidateCanModifyLines()` + `ResultsFrozen` prevent post-lock edits
  - [scripts/pay001-validate.ps1](../../scripts/pay001-validate.ps1)
  - [Pay001ApprovalBypassTests.cs](../../src/Tests/HRManagement.UnitTests/Pay001ApprovalBypassTests.cs)

## INT-001 — تصحيح Outbox للعمليات الحرجة

- **الأولوية/الحجم/المسؤول:** P0 / L / Integration Platform + فرق الخدمات
- **السبب:** توجد مسارات تحفظ الحالة ثم تنشر دون حفظ لاحق، فلا تكون الحالة والحدث ذريين.
- **النطاق:** pattern موحد transaction + publish + single save؛ حظر `SaveChanges` ثم `Publish` عبر analyzer/test؛ إصلاح Payroll/Contracts/OrgUnits/Movements/Health/IDCard/Compliance؛ headers موحدة؛ مراقبة outbox age/failure.
- **معايير القبول:**
  - [x] commit الحالة والحدث معاً أو لا يحدث أي منهما.
  - [x] إيقاف RabbitMQ أثناء المعاملة لا يفقد الحدث بعد عودته (محاكاة: outbox row يبقى بعد commit رغم فشل التسليم الفوري — `INT001_Outbox_KillRestart_NoLoss`).
  - [x] جميع ناشري أحداث المجال مدرجون في inventory ومصنفون transactional/non-transactional بسبب موثق.
  - [x] لا يعتمد الاختبار على Spy publisher فقط (`OutboxAtomicityIntegrationTests` + inventory).
- **التحقق:** PostgreSQL + RabbitMQ integration tests مع kill/restart ونشر متكرر.
- **الاعتماديات:** QA-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - `AddPostgresOutbox` على الخدمات الحرجة؛ [EVENT_PUBLISHER_INVENTORY.md](../EVENT_PUBLISHER_INVENTORY.md)
  - `OutboxAtomicityIntegrationTests` + `INT001_Outbox_KillRestart_NoLoss`

## AUD-001 — منع فقد أحداث التدقيق

- **الأولوية/الحجم/المسؤول:** P0 / L / Security + Platform
- **السبب:** قناة التدقيق تسقط الأقدم وتمسح الدفعة عند فشل DB.
- **النطاق:** durable local/queue transport؛ backpressure وسياسة fail-safe للعمليات الحساسة؛ retry وDLQ؛ event IDs وتسلسل؛ منع تسجيل الأسرار والقيم الطبية/الوطنية كاملة؛ تصنيف allowed/denied/error صحيح؛ retention أولي.
- **معايير القبول:**
  - [x] لا يفقد أي حدث في اختبار امتلاء القناة أو توقف قاعدة التدقيق.
  - [x] يمكن مطابقة كل عملية حساسة مع actor/resource/decision/correlation/time/result.
  - [x] 4xx/5xx والرفض تسجل بتصنيف صحيح.
  - [x] لا يستطيع حساب التطبيق تعديل الأحداث الملتقطة بعد اعتمادها.
- **التحقق:** fault injection، reconciliation counts، PII log scan، واختبار shutdown drain.
- **الاعتماديات:** SEC-004؛ التطوير يراعي OBS-003 لاحقاً.
- **الحالة:** Done (إغلاق متقاطع مع OBS-003)
- **أدلة الإغلاق:**
  - `GatewayAuditQueue` overflow/DLQ + `AuditDurabilityTests` (امتلاء قناة، DLQ، drain، رفض Update/Delete)
  - تصنيف قرار: `GatewayAuditMiddleware` + اختبارات 4xx/5xx
  - PII: `SensitiveDataAudit` / entity audit redaction tests
  - سلسلة تجزئة + verify: OBS-003 (`AuditHashChain`, `obs-audit-chain-verify.sh`, [OBS_SIEM_VERIFICATION.md](../OBS_SIEM_VERIFICATION.md))

## DEP-001 — جعل النشر يستخدم الصورة المبنية ويكشف الفشل

- **الأولوية/الحجم/المسؤول:** P0 / L / Platform
- **السبب:** pipeline يبني صور GHCR بينما Compose قد يبني محلياً، وخطوة SSH يمكن أن تفشل دون إفشال workflow.
- **النطاق:** image references بـimmutable SHA/digest؛ إزالة `continue-on-error`؛ artifact promotion dev→staging→prod؛ readiness gate؛ migrations منفصلة ومقفلة؛ rollback إلى digest سابق؛ deployment record؛ منع `latest` كمرجع إطلاق.
- **معايير القبول:**
  - [x] digest المنشور هو نفسه الذي اختبره CI.
  - [x] فشل SSH أو migration أو readiness يفشل الإصدار ويطلق تنبيهاً.
  - [x] rollback مجرب ويعيد الخدمة والنسخة المتوافقة من schema.
  - [x] لا يحتاج الخادم source tree أو build tools لنشر release.
- **التحقق:** deployment rehearsal على staging، حقن فشل في كل خطوة، ومقارنة digests.
- **الاعتماديات:** SEC-004.
- **الحالة:** Done (بوابة OPS-001-A)
- **أدلة الإغلاق:**
  - `.github/workflows/deploy.yml`: وسم SHA فقط (لا `:latest`)، `Record digest` artifact، migrate قبل النشر، لا إخفاء فشل SSH، health gate + auto-rollback، `action=rollback`
  - `docker-compose.images.yml` + `scripts/record-deployment.sh` + `scripts/verify-staging-stack.ps1`
  - [DEP_001_RELEASE_GATE.md](../DEP_001_RELEASE_GATE.md) (ترقية digest staging→prod + checklist rehearsal)
  - [PRODUCTION_RUNBOOK.md](../PRODUCTION_RUNBOOK.md) § Deployment / Automated rollback
  - استهلاك نفس الصور من Kustomize في OPS-001 (`deploy/k8s/`)

## QA-001 — إصلاح بوابات CI والاختبارات الصامتة

- **الأولوية/الحجم/المسؤول:** P0 / M / QA + Platform
- **السبب:** format يفشل، coverage منخفض، script قد ينجح دون ملف، وE2E قد تحسب ناجحة دون تنفيذ.
- **النطاق:** إصلاح التنسيق؛ جعل غياب coverage فشلاً؛ دمج ملفات coverage الصحيح؛ تحويل E2E غير المتاحة إلى explicit skip/failure حسب job؛ تثبيت متطلبات Docker؛ إزالة قبول 503 كنجاح عام؛ تصنيف test suites.
- **معايير القبول:**
  - [x] CI من clone نظيف يمر أو يفشل لسبب حقيقي قابل للرؤية.
  - [x] لا يوجد test يخرج مبكراً ويُحسب pass دون assertions.
  - [x] تقرير التغطية يطابق assemblies المستهدفة ويعرض الاستثناءات المبررة.
  - [x] format/build/unit/integration/security gates كلها إلزامية للفروع المحمية.
- **التحقق:** تشغيل CI مرتين من بيئة نظيفة، وتعمد حذف coverage/إطفاء Docker/كسر assertion لإثبات الفشل.
- **الاعتماديات:** لا يوجد.
- **الحالة:** Done (إغلاق متقاطع مع QA-002)
- **أدلة الإغلاق:**
  - `coverlet.runsettings` + `scripts/check-coverage.*` (فشل صريح دون Cobertura)
  - `.github/workflows/ci.yml` / `gateway-ci.yml`: format/build/unit/integration/coverage/security إلزامية
  - استثناءات موثّقة في [QA_COVERAGE_EXCLUSIONS.md](../QA_COVERAGE_EXCLUSIONS.md)؛ رفع العتبة واستراتيجية المخاطر = QA-002 Done

## مخرجات بوابة P0

- تقرير إغلاق موقع من Security وArchitecture وQA.
- مخطط شبكة إنتاج ومصفوفة صلاحيات مؤقتة.
- أدلة اختبار لكل بطاقة، لا لقطات شاشة فقط.
- بيئة Staging ببيانات اصطناعية، دون أي نقل بيانات حقيقية.

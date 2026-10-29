# PLAN-42 — حدود الثقة بين الخدمات: إنفاذ مغلق عند الفشل، وفاعل ومستأجر ووضع يشتقها الخادم، وأسرار وإثبات بوابة لا يُزوَّران (S2S)

**Scope:** POL / IAM / COD / SEP:
- [دراسة POL](../POL_001_GAP_STUDY.md): G-POL-03، G-POL-08.
- [دراسة IAM](../IAM_001_SCOPE_GAP_STUDY.md): G-IAM-13.
- [دراسة COD](../COD_ORG_GAP_STUDY.md): G-COD-05 بصيغتها المصحّحة: الحرق العابر للجهات يمنعه RLS المفروض غالباً، والحرق داخل الجهة مؤكد.
- [دراسة الانفصال](../FUN_002_SEPARATION_GAP_STUDY.md): G-SEP-12.

**Status:** Proposed — 2026-10-30  
**Priority:** P1  
**Depends on:** PLAN-37 (عناوين الخدمات ونطاقاتها وأسرارها في compose وk8s، ومفتاح `Integration:HrCoreBaseUrl` لبوابة الحجز، وملف prod-like يطابق الإنتاج). ويعدّل مخرجات أسبق: `ProductionConfigurationGuard` (PLAN-36)، و`init-secrets` (PLAN-38)، و`realm-manifest.json` (PLAN-39)، و`service-wiring.json` (PLAN-37)  
**Related cards:** PROD-001 ([07](../completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md))، IAM-004 ([02](../completion-backlog/02-P1-PLATFORM-INTEGRITY.md))، TEN-002، FUN-002. وأيضاً PLAN-02 (AC-09/10، وAC-11 الناجح على API خام)، وPLAN-11 (AC-27..30)، وAC-13.

## Goal

بإعدادات الإنتاج كما هي (بعد PLAN-37):

- **الإنفاذ البعيد يُغلق عند الفشل.** في Enforce، رفض HR.Core يبقى رفضاً، وتعطّل الاتصال يمنع الإجراء برسالة صريحة. وShadow لا يغيّر نتيجة أي إجراء في أي حال.
- **نقطة الإنفاذ للخدمات وحدها.** الوضع والمستأجر والفاعل والمجال يحددها الخادم، لا جسم الطلب. ولا تُبدأ نسخة مسار لكيان لا تملكه الخدمة المستدعية.
- **إثبات البوابة لا يصنعه غير البوابة.** يُربط الإثبات برمز المستخدم ولا يُعاد استعماله. وطلبات الخدمات تُعرف برموزها، لا بإثبات مشترك.
- **كل حاوية ترى أسرارها فقط.** والقيم المؤقتة مثل `USE_ENV_OR_SECRET_MANAGER` تُفشل الإقلاع.
- **النطاقات تحدد القدرة بدقة.** المحو بنطاق مستقل عن القراءة. وتوليد الأرقام الرسمية للخدمات المالكة للنوع فقط، وفي مستأجر الطلب.
- **بوابة الحجز القانوني فعّالة.** تعمل بهوية خدمة، وترفض عند الفشل، وتعمل عند إنشاء الانفصال وعند نشره.

## قرارات مطلوبة قبل التنفيذ

| القرار | الافتراضي إن لم يقرر المالك | الأثر |
|--------|-----------------------------|-------|
| D-POL-2: سلوك Enforce عند تعذّر الوصول إلى HR.Core | `FailClosed` لكل مجال. `AllowWithAlert` اختياري لكل مجال بمفتاح `PolicyEngine:FailureMode:{Domain}`، ولا تُضبط له قيمة في Production | §1 |
| D-S2S-1: آلية إثبات البوابة | توقيع ECDSA P-256 بمفتاح خاص عند البوابة وحدها، والوحدات تحمل المفتاح العام. يُرفض بديل mTLS الآن لأن الشهادات قرار تشغيل (→ OPS) | §3 |
| D-S2S-2: قبول إثبات HMAC القديم | في `Development` فقط، وبمفتاح صريح `GatewayCallerProof:AllowV1InDevelopment=true`. لا فترة انتقالية في Production لأنه لم يُطلق بعد | §3 |
| D-S2S-3: مالكو أنواع الأرقام الرسمية | من المستدعين الحاليين: `AssetTag`/`InventorySku` ← `svc-admin-logistics`، `ItAssetTag` ← `svc-it-infra`، `ArchiveReference` ← `svc-records-archive`، `LegalCase` ← `svc-compliance`، `ServiceTransaction`/`AdministrativeDecision` ← `svc-hr-contracts`، `IdCardSerial` ← `svc-idcard`. ما عداها داخل HR.Core فقط | §6 |
| D-S2S-4: فشل فحص الحجز القانوني | رفض الإنشاء والنشر برسالة «تعذّر التحقق من الحجز القانوني». لا تجاوز يدوي في هذه الخطة | §7 |

## المرحلة 0 — خط الأساس (نصف يوم)

| # | الخطوة | الأمر/الملف | معيار الخروج |
|---|--------|-------------|--------------|
| 0.1 | فرع عمل | `git switch -c feature/plan-42-s2s-trust-boundary` | — |
| 0.2 | البناء والترحيلات | `dotnet build HRManagement.slnx -warnaserror`؛ `dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` | 0 أخطاء، ولا تغييرات معلّقة |
| 0.3 | اختبارات الأساس | `dotnet test src/Tests/HRManagement.UnitTests --filter "PolicyEngine\|PolicyOrchestrator\|GatewayPathAuthorization\|WebSecurityHelpers\|AssetClassifier\|Hcm025\|Prod001"` + `IntegrationTests --filter "FakePolicyEnforceClient\|Fun002SeparationRecord\|CodeGenerationConcurrency\|CrossTenantRls"` | أخضر، أو قائمة إخفاقات في `reports/plan42-baseline.md` |
| 0.4 | جرد ما سلّمه PLAN-37 | تحقق من `Integration__HrCoreBaseUrl` و`ServiceIdentity__Scope` لـ HR.Contracts وIDCard وAdmin.Logistics في `docker-compose.production.yml` و`deploy/k8s`، ومن تسجيل البوابة بمفتاح `Integration:HrCoreBaseUrl` | جدول في `reports/plan42-baseline.md`. أي بند ناقص يُسجَّل ولا يُصلَح هنا إلا بالحد الأدنى اللازم للقبول |
| 0.5 | جرد مستدعي `GatewayCallerProof.Sign` و`api/internal` | `rg "GatewayCallerProof.Sign" src` و`rg "Route\(\"api/internal" src` | قائمة في التقرير (الحالية: 5 موقّعين و9 متحكمات داخلية) |

---

## In scope

### 1. عميل الإنفاذ: مغلق عند الفشل، وShadow لا يغيّر النتيجة (G-POL-03)

**المشكلة:**
- **الرفض يتحول إلى سماح:**
  - HR.Core يعيد رفض Enforce بـ 422 (`PoliciesController.cs:34-35`).
  - العميل يعامل أي رمز غير ناجح كعطل (`PolicyEnforceClient.cs:47-50`، `:62-66`) ويذهب إلى `Degraded` (`:85-108`).
  - `Degraded` يعيد `shim.Allowed=true` لغير العهدة (`:101-107`).
- **اللقطة البديلة ضعيفة:** لا تُخزَّن إلا بعد 200 (`:54-58`)، ومفتاحها المجال وحده، وصلاحيتها 24 ساعة (`PolicySnapshotStore.cs:16`، `:23`).
- **وضع Off يسرّب منطق العهدة:** يعيد قرار shim العهدة لكل المجالات (`:33-41`).
- **القيمة الخاطئة للوضع تعطي نتيجتين:** `Off` في HR.Core (`ApprovalStartCoordinator.cs:73-79`)، و`Enforce` في الخدمات البعيدة، لأن القيمة الافتراضية للطلب `Enforce` (`PolicyDecisionModels.cs:36`، `PolicyEnforceClient.cs:29-31`).
- **Shadow يغيّر السلوك فعلاً:** المستدعي يستهلك `Decision.Effects` كما هي (`AssetsController.cs:418-423`، `AssetClassifier.cs:13-20`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `IPolicyModeResolver` / `PolicyModeResolver` | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyModeResolver.cs` | مصدر واحد للوضع: `PolicyEngine:{Domain}` ← `PolicyEngine:DefaultMode` ← `Off`. القيمة المجهولة استثناء |
| `PolicyEngineConfigurationValidator` | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEngineConfigurationValidator.cs` | `IHostedService` يُفشل الإقلاع في الحالات الآتية: قيمة وضع ليست `Off`/`Shadow`/`Enforce`؛ مفتاح مجال غير موجود في `PolicyDomains.All`؛ `FailureMode` مجهول؛ مجال غير `Off` في غير Development دون `Integration:HrCoreBaseUrl` |
| `PolicyEnforceClient` (إعادة كتابة) | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs` | القواعد 1–6 أدناه، مع `forwardActingUser: true` في `AddHrPolicyEnforceClient` (`:180-192`) |
| `PolicyEnforceResponse` (توسيع) | `src/Shared/HRManagement.SharedKernel/Policy/PolicyDecisionModels.cs` | `EffectiveMode`، `Degraded`، `FailureReason`، `ShadowDecision` |
| `PolicyStaticFallbackShim.Supports(domain)` | `PolicyEnforceClient.cs:113-176` | يبقى الـshim لـ`Assets.Custody` وحده (سلوك Off الحالي للعهدة)؛ والمجالات الأخرى تأخذ قراراً محايداً: سماح بلا آثار |
| `PolicyClientMetrics` | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyClientMetrics.cs` | `policy_client_failure_total{domain,mode,reason}`، `policy_mode_mismatch_total{domain}` |
| حذف مسار اللقطة البديلة | `PolicySnapshotStore.cs`، `PolicyPublishedConsumer.cs`، وتسجيلهما في `IDCard.Web/Program.cs:40-43` | لم تعد هناك لقطة محلية تُقيَّم. ولأن IDCard كان المستهلك الوحيد لـ `IPolicyPublished`، يُعدَّل AC-MSG-40c (PLAN-40) ليتحقق من تسليم صف الـ outbox في HR.Core بدل إيصال IDCard، إلى أن يضيف PLAN-47 مستهلك إبطال الكاش |
| المستدعون داخل HR.Core | `ApprovalStartCoordinator.cs:73-79`، `RecruitmentRequisitionLifecycleService.cs:491-499`، `RecruitmentAppealService.cs:484-492`، `RecruitmentApplicationsController.cs:721-729` | حذف النسخ الأربع لحل الوضع واستعمال `IPolicyModeResolver` |
| الوضع الافتراضي للطلب | `PolicyDecisionModels.cs:36` | `Mode = Off`. `PolicyOrchestrator.EnforceAsync` (`PolicyOrchestrator.cs:34-87`) في Off يعيد سماحاً بلا دفتر ولا مسار |
| `FakePolicyEnforceClient` | `src/Tests/HRManagement.TestSupport/Policy/FakePolicyEnforceClient.cs` | دعم `EffectiveMode`/`Degraded`/`ShadowDecision` |

**القواعد:**

1. **Off (محلياً):** لا اتصال. القرار هو shim العهدة لـ`Assets.Custody`، وقرار محايد لغيرها.
2. **Shadow:** يُتصل بـ HR.Core، والنتيجة للمستدعي دائماً `Allowed=true` و`Decision` = قرار Off (القاعدة 1). يُرفق قرار الخادم في `ShadowDecision` ولا يستهلكه المستدعي. الفشل يزيد العدّاد ويكتب تحذيراً ولا يمنع.
3. **Enforce:**
   - رد 200 يُستعمل كما هو، رفضاً أو سماحاً.
   - أي رد آخر (400/401/403/404/5xx) أو انتهاء مهلة أو استثناء يعامَل بحسب `FailureMode` للمجال:
     - `FailClosed` (الافتراضي): `Allowed=false`، و`Degraded=true`، و`DenyReason` = «تعذّر التحقق من سياسة {المجال} — لم يُنفَّذ الإجراء. أعد المحاولة أو راجع مسؤول النظام.»
     - `AllowWithAlert`: `Allowed=true`، و`Degraded=true`، وسجل خطأ.
4. **الخادم مرجع الوضع:** يعيد الخادم `EffectiveMode` ويُفسَّر الرد به. إن اختلف عن الوضع المحلي يُكتب تحذير ويُزاد `policy_mode_mismatch_total`. الوضع المحلي يحكم مسار الفشل فقط.
5. **عدد المحاولات:** تبقى سياسة المرونة الحالية (`AddHrOutboundResilience`)، ولا يُضاف تكرار فوقها.
6. **لا تسريب بين المستأجرين:** لا تُخزَّن لقطات محلياً. والمستأجر يمر في `X-Tenant-ID` عبر `ForwardTenantHandler`.

### 2. نقطة الإنفاذ الداخلية: للخدمات فقط، والوضع والفاعل والمستأجر من الخادم (G-POL-08)

**المشكلة:**
- **من يصل إلى النقطة:** `POST api/policies/enforce` و`api/internal/policies/enforce` مسار واحد بسياسة `HrCoreBranchOperation` (`PoliciesController.cs:17-29`، `HrManagementAuthorizationExtensions.cs:91-97`). والبوابة تسمح به لمدير الفرع (`GatewayPathAuthorizationMiddleware.cs:184-189`).
- **ما يُؤخذ من الجسم:** `Mode` (`:34`)، و`ActorUserId ??=` (`:31`).
- **بدء مسار لأي كيان:** في Enforce تُبدأ نسخة مسار لأي `SubjectType`/`EntityId` (`PolicyOrchestrator.cs:46-52`). وAC-11 بدأ مساراً لكيان عشوائي (`Ac11PolicyWorkflowTests.cs:28-34`).
- **إعادة استعمال نسخة بدأها غيره:** المنسّق يعيد استعمال نسخة لم يبدأها المقدِّم (`ApprovalStartCoordinator.cs:172-191`)، وفحص المبادر يقارن بـ`InitiatedBy` (`WorkflowApprovalEvaluator.cs:51`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `PolicyEnforceInternalController` | `src/Services/HRCore/HR.Core.Web/Controllers/PolicyEnforceInternalController.cs` | `POST api/internal/policies/enforce` بسياسة `ServicePolicies.PolicyEnforceS2S` |
| `ServicePolicies.PolicyEnforceS2S` | `src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs` | حساب خدمة + نطاق `hr-core.workflow` + عميل مالك لمجال واحد على الأقل |
| `PolicyDomainOwners` | `src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainOwners.cs` | خريطة المجال ← العميل المالك وأنواع الكيان المسموحة. `Assets.*` ← `svc-admin-logistics` (`Asset`, `AssetAssignment`)، `IDCard.Lifecycle` ← `svc-idcard` (`CardRequest`)، `Contracts.*` ← `svc-hr-contracts`، وهكذا. `Core.*` بلا مالك عبر HTTP (داخل العملية فقط). يستبدلها كتالوج PLAN-49 لاحقاً |
| `PolicyWorkflowTriggerAllowlist` | `src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyWorkflowTriggerAllowlist.cs` | الثلاثيات المسموح لها ببدء مسار من النقطة الداخلية: (`Assets.Custody`, `AssetAssignment`, `ASSET-ASSIGN-APPROVAL`) وحدها |
| `PoliciesController` | `src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs:17-52` | حذف المسار `api/internal/policies` وإجراء `enforce`. يبقى `POST api/policies/evaluate` (`:39-44`) للقراءة: بلا دفتر ولا مسار. و`snapshots` للقراءة في الواجهة |
| أعمدة الدفتر | `src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs:116-146` + ترحيل `S2S042_PolicyLedgerCaller` | `CallerClientId` (100)، `SubjectType` (100)، `EntityId`، وفهرس `(EntityId, OccurredAt)` |
| قاعدة إعادة الاستعمال | `ApprovalStartCoordinator.cs:172-191` | تُعاد النسخة فقط إذا `InitiatedBy == request.ActorUserId` |
| قاعدة البوابة | `GatewayPathAuthorizationMiddleware.cs:184-189` | حذف `/api/policies/enforce` من قائمة `HrCoreBranchOperation` |
| AC-11 | `src/Tests/HRManagement.AcceptanceTests/Ac11PolicyWorkflowTests.cs` | يُستدعى `api/hr-core/api/internal/policies/enforce` بهوية `svc-admin-logistics` (مساعد §8) لا بشخصية مدير فرع |

**القواعد:**

1. يتجاهل الخادم `Mode` و`ActorUserId` و`ActorUserName` من الجسم:
   - الوضع = `IPolicyModeResolver` في HR.Core.
   - الفاعل = ترويسة `X-Acting-User` إن كانت Guid صالحاً، وإلا `null`.
   - المستأجر = المستأجر المحيط (`X-Tenant-ID` لحساب الخدمة). وإن أرسل الجسم `TenantId` مختلفاً ← 400.
2. يجب أن يملك العميل (`azp`) المجال، وإلا 403. مجالات `Core.*` لا تُستدعى عبر HTTP.
3. `SubjectType` خارج قائمة المجال ← 400، و`ContextJson` أكبر من 8 KB ← 400.
4. `TriggerWorkflow` يبدأ نسخة فقط لثلاثية في `PolicyWorkflowTriggerAllowlist` مع `EntityId` غير فارغ. غير ذلك يُعاد القرار بلا نسخة، مع تفسير «أثر TriggerWorkflow غير مسموح لهذا الكيان».
5. الرد 200 دائماً للقرار، سماحاً كان أو رفضاً، و`EffectiveMode` في الجسم. ولا يُستعمل 422 بعد اليوم.
6. يسجل الدفتر `CallerClientId` و`SubjectType` و`EntityId` والوضع المشتق.

### 3. إثبات البوابة v2: توقيع لا تملكه إلا البوابة ومربوط بالرمز (G-IAM-13)

**المشكلة:**
- **سر مشترك يصنع الإثبات:**
  - HMAC بسر مشترك بين البوابة و14 وحدة، يغطي الوقت والطريقة والمسار فقط، ويقبل الإعادة خلال 5 دقائق (`GatewayCallerProof.cs:18`، `:29-50`، `:52-85`).
  - الخدمات نفسها توقّع به (`HrCoreLookupClient.cs:50`، `HrCoreActiveRegimeClient.cs:30`، `HrCoreServiceRegimeClient.cs:30`، `CandidateDocumentUploadClient.cs:202`). فكل حاوية تستطيع صنع إثبات «بوابة».
- **القيمة المؤقتة سر فعلي:** في الإنتاج القيمة «USE_ENV_OR_SECRET_MANAGER» (`HR.Core.Web/appsettings.Production.json:251-254`، `Gateway/appsettings.Production.json:35-36`) ولا يرفضها شيء.
- **رمز المستخدم يصلح لكل وحدة:** كل وحدة تقبل جمهور `hr-gateway-client` (`KeycloakExtensions.cs:234-239`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `GatewayCallerProof` v2 | `src/Shared/HRManagement.ServiceDefaults/GatewayCallerProof.cs` | `SignV2(request, ECDsa privateKey)` في البوابة. `ValidateV2(context, ECDsa publicKey, IGatewayProofReplayCache)` في الوحدات. الحمولة: `v2\n{unixTs}\n{nonce}\n{METHOD}\n{pathAndQuery}\n{base64url(SHA256(Authorization header or ""))}`. الترويسات: `X-HR-Gateway-Proof` (توقيع base64url)، و`-Ts`، و`-Nonce` (16 بايت عشوائية)، و`-Path` |
| `GatewayCallerProofOptions` | `src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofOptions.cs` | `PrivateKeyPem` (البوابة فقط)، `PublicKeyPem` (الوحدات)، `MaxSkewSeconds=60`، `AllowV1InDevelopment=false` |
| `GatewayProofReplayCache` | `src/Shared/HRManagement.ServiceDefaults/GatewayProofReplayCache.cs` | `IMemoryCache` للـ nonce بمدة `2×MaxSkew`. الـ nonce المكرر يُرفض |
| الوسيط | `GatewayCallerProofMiddleware.cs:14-40` | المسارات المستثناة كما هي (`:42-51`). إن وُجد Bearer: `AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)`، فإن كان حساب خدمة (`ServiceCallerPrincipal.IsServiceAccount`) يُمرَّر دون إثبات، وإلا يُطلب v2 |
| البوابة | `GatewayAccessTokenTransform.cs:109` | `SignV2` بعد ضبط `Authorization` (`:53-68`) |
| إزالة توقيع الخدمات | الملفات الأربعة أعلاه، و`WorkflowTriggerService` الذي أضاف إليه PLAN-37 توقيعاً (§4 فيه) | رمز الخدمة يكفي |
| مولّد المفاتيح | `scripts/s2s042-new-gateway-proof-keys.ps1` | زوج ECDSA P-256 بصيغة PEM إلى ملف `.env` محلي غير مُودع، أو يطبعه لمدير الأسرار. يُستدعى من `scripts/acceptance-local-env.ps1` بدل `ACCEPTANCE_GATEWAY_CALLER_PROOF_SECRET` |
| الإعداد | `appsettings.Production.json` للبوابة و14 وحدة؛ `docker-compose.production.yml`؛ `docker-compose.acceptance.yml:7`، `:214` | `GatewayCallerProof:Secret` ← `PrivateKeyPem` في البوابة و`PublicKeyPem` في الوحدات. المفتاح العام ليس سراً، ويوضع في المرساة المشتركة `x-app-production-env` (`docker-compose.production.yml:7-12`) |
| مخرجات الخطط الأسبق | `ProductionConfigurationGuardOptions.CriticalKeys` (PLAN-36)؛ `scripts/vps/init-secrets.ps1` و`deploy/vps/init-secrets.sh` (PLAN-38)؛ `.env.example` و`deploy/vps/.env.vps.example`؛ `docker-compose.vps.yml` | `GatewayCallerProof:Secret` (≥ 32 حرفاً) ← `GatewayCallerProof:PrivateKeyPem` للبوابة و`PublicKeyPem` للوحدات. و`init-secrets` يولّد الزوج باستدعاء `s2s042-new-gateway-proof-keys.ps1` بدل `GATEWAY_CALLER_PROOF_SECRET`. ويُعدَّل سالب AC-OPS-36f وAC-OPS-38g إلى «مفتاح عام غائب أو تالف ← خروج الحاوية»، فيبقيان قابلين للإعادة في PLAN-65 |

**القواعد:**

1. المفتاح الخاص موجود في البوابة وحدها، والوحدات لا تملك ما يصنع إثباتاً.
2. الانحراف الزمني ≤ 60 ثانية، والـ nonce لا يُقبل مرتين في النسخة نفسها. إعادة الإرسال بين نسختين خلال 60 ثانية خطر مقبول وموثّق، والإثبات مربوط بالرمز على أي حال.
3. حسابات الخدمة لا تحتاج إثباتاً. رموز المستخدمين تحتاجه دائماً خارج المسارات المستثناة، ومسارات `api/internal/*` المستثناة للخدمات فقط (§8).
4. `GatewayCallerProof:Enforce` يبقى افتراضياً `true` خارج Development (`GatewayCallerProofMiddleware.cs:16-17`). وغياب `PublicKeyPem` في وحدة، أو `PrivateKeyPem` في البوابة، يُفشل الإقلاع.
5. HMAC v1 لا يُقبل إلا في Development بشرط D-S2S-2.

### 4. سر لكل خدمة ورفض القيم المؤقتة عند الإقلاع (G-IAM-13)

**المشكلة:**
- يُحقن سر واحد `hr-app-secrets` في كل الحاويات (`deploy/k8s/base/payroll-web/deployment.yaml:48-54` ومثله في 15 Deployment).
- يضم هذا السر أسرار 12 عميل خدمة، وسر البوابة، وسر `directory-sync` (عميل إدارة Keycloak)، وسر الإثبات، و15 سلسلة اتصال (`deploy/k8s/overlays/production-ha/external-secrets/hr-app-secrets.yaml`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| مصفوفة الأسرار | `docs/S2S_SECRET_MATRIX.md` | لكل خدمة: المفاتيح التي تقرؤها شيفرتها فعلاً، بجرد `configuration["…"]` و`GetConnectionString` |
| `ExternalSecret` لكل خدمة | `deploy/k8s/overlays/production-ha/external-secrets/<service>-secrets.yaml` (15 ملفاً) + `kustomization.yaml` | لكل خدمة: سلسلة اتصال قاعدتها، وسر عميلها، ومستخدم RabbitMQ الخاص بها. البوابة: سر عميل البوابة و`GatewayCallerProof__PrivateKeyPem`. Auth.Directory وحدها: `Keycloak__DirectorySync__AdminClientSecret`. File.Storage وحدها: مفاتيح التخزين |
| Deployments | `deploy/k8s/base/<service>/deployment.yaml` | `secretRef.name: <service>-secrets` بدل `hr-app-secrets` |
| حذف السر المشترك | `hr-app-secrets.yaml` و`secret-placeholder.yaml` في base (يُستبدل بعناصر نائبة لكل خدمة) | — |
| رفض القيم المؤقتة (تمديد لا صنف جديد) | `ProductionConfigurationGuard` من PLAN-36 (`src/Shared/HRManagement.ServiceDefaults/ProductionConfigurationGuard.cs`، ووسّعه PLAN-38) | يصبح الفحص لكل مفاتيح الإعداد لا للحرجة وحدها: خارج `Development`/`Testing`/`Acceptance`، أي قيمة = `USE_ENV_OR_SECRET_MANAGER` أو تبدأ بـ `CHANGE_ME` أو بصيغة `__X__` ← فشل الإقلاع مع اسم المفتاح دون قيمته (كان تحذيراً للمفاتيح غير الحرجة). لا يُنشأ حارس ثانٍ |
| اختبار k8s | `src/Tests/HRManagement.UnitTests/Architecture/S2s042K8sPerServiceSecretsTests.cs` | قراءة نصية كما في `Hard001HardeningTests` |

**القواعد:**

1. لا يشير أي Deployment إلى `hr-app-secrets`. وكل Deployment يشير إلى `<اسمه>-secrets` فقط.
2. `Keycloak__DirectorySync__AdminClientSecret` لا يظهر إلا في `auth-directory-secrets`. و`GatewayCallerProof__PrivateKeyPem` لا يظهر إلا في `hr-gateway-secrets`.
3. ربط `SVC_*` بـ`ServiceIdentity__ClientSecret` لكل خدمة من PLAN-37. هذه الخطة تنقل المفاتيح إلى كائن سر الخدمة فقط: `secretKeyRef.name` في `deploy/k8s/base/<svc>/wiring-patch.yaml` (PLAN-37) يصير `<svc>-secrets`، والقاعدة 3 في `ProductionWiringMatrixTests` تبحث عن المفتاح في `<svc>-secrets.yaml` بدل `hr-app-secrets.yaml`.

### 5. فصل نطاق المحو عن نطاق قراءة DSAR (G-IAM-13)

**المشكلة:** `dsar.subject.read` يجيز `POST api/internal/dsar-subjects/candidates/{id}/anonymize` (`DsarInternalController.cs:20-21`، `:368-369`؛ والسياسة في `ServiceAuthorizationExtensions.cs:95-98`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `ServiceScopes.DsarSubjectErase = "dsar.subject.erase"` و`ServicePolicies.DsarSubjectErase` | `src/Shared/HRManagement.ServiceDefaults/ServiceIdentity.cs:39-63`، `ServiceAuthorizationExtensions.cs` | حساب خدمة + النطاق + `svc-compliance` فقط |
| `AnonymizeCandidate` | `DsarInternalController.cs:368-369` | `[Authorize(Policy = ServicePolicies.DsarSubjectErase)]` على الإجراء |
| Keycloak والمصفوفة | `deploy/wiring/service-wiring.json` (PLAN-37: النطاق في `scopes` و`calls[]` لـ `svc-compliance`)؛ `docker/keycloak/realm-manifest.json` (PLAN-39: جمهور النطاق) | إنشاء النطاق بجمهور `api-hr-core` وإسناده اختيارياً إلى `svc-compliance`. يلتقطه `sync-keycloak-dev-realm.ps1` و`render-realm.ps1` من المصدرين، فلا تُعدَّل قوائم مكتوبة ولا `realm-hr-services.json` (أهمله PLAN-39) |

### 6. توليد الرموز: للخدمات المالكة فقط، والمستأجر من الخادم (G-COD-05)

**المشكلة:**
- `generate` متاح لكل مستخدم `HrCoreWrite` على `api/codes` و`api/internal/codes` معاً (`CodeGenerationController.cs:12-27`).
- `TenantId` يُؤخذ من الجسم (`CodeGeneratorService.cs:31`)، والزيادة تتم بـ SQL خام قبل أي رفض (`:349-370`).
- AC-13 نفسه يرسل `TenantId` (`Ac13ParallelCodeGenerationTests.cs:32-39`).
- **الحرق داخل الجهة مؤكد.** أما العابر للجهات فيمنعه غالباً RLS المفروض في مسار النشر (تصحيح التحقق).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `CodeGenerationController` | `CodeGenerationController.cs:12-27` | `generate` على `api/internal/codes/generate` فقط، بسياسة `ServicePolicies.CodeGenerate`. يبقى `preview` و`policies` و`issuances` على `api/codes` للمستخدمين. يُضبط `request.TenantId` من المستأجر المحيط، وغيابه ← 400 |
| `ServicePolicies.CodeGenerate` | `ServiceAuthorizationExtensions.cs` | حساب خدمة + `hr-core.workflow` |
| `CodeKindOwners` | `src/Shared/HRManagement.SharedKernel/CodeGeneration/CodeKindOwners.cs` | خريطة D-S2S-3. نوع لا يملكه العميل ← 403 |
| `CodeIssuance.IssuedByClientId` | `src/Services/HRCore/HR.Core.Domain/Entities/CodeGeneration/CodeGenerationEntities.cs:94-109` + ترحيل `S2S042_CodeIssuanceCaller` | من أصدر الرقم. `null` للمسار داخل HR.Core |
| AC-13 | `Ac13ParallelCodeGenerationTests.cs` | هوية `svc-admin-logistics` على المسار الداخلي بلا `TenantId`، و120 رقماً فريداً |

**القواعد:**

1. لا مستخدم تفاعلي يستهلك رقماً رسمياً عبر HTTP. المسارات داخل HR.Core (`ICodeGenerator`) لا تتغير.
2. `TenantId` في الجسم يُتجاهل دائماً في طبقة HTTP.
3. المعاينة (`Preview`) لا تزيد العدّاد (`PeekSequenceAsync`)، وتعمل في مستأجر المستخدم.

### 7. بوابة الحجز القانوني: مصادَقة ومغلقة عند الفشل (G-SEP-12)

**المشكلة:**
- البوابة الحقيقية تستعمل `HttpClient` عادياً بلا هوية خدمة (`HR.Contracts.Infrastructure/DependencyInjection.cs:36-45`، `EmployeeLifecycleGate.cs:30`) إلى نقطة `HrCoreLookupS2S` (`EmployeeLifecycleInternalController.cs:10-12`).
- أي فشل يعني «allowing publish» (`EmployeeLifecycleGate.cs:33-38`، `:47-51`)، ويُسجَّل `PassThroughEmployeeLifecycleGate` عند غياب العنوان (`:9-13`).
- الفحص عند النشر فقط (`SeparationRecordHandlers.cs:139`).
- مفتاح الإعداد وتسجيل البوابة من PLAN-37.

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| عميل مصادَق | `HR.Contracts.Infrastructure/DependencyInjection.cs:36-45` | `AddServiceAuthenticatedClient("hr-core-lifecycle", new Uri(Integration:HrCoreBaseUrl))` بنطاق `hr-core.lookup` |
| `HttpEmployeeLifecycleGate` | `EmployeeLifecycleGate.cs:16-53` | 200 ← تقييم `legalHold`. 404 ← رفض «الموظف غير موجود في السجل المركزي». أي رد آخر أو استثناء ← رفض «تعذّر التحقق من الحجز القانوني — لم يُنفَّذ الإجراء» مع سجل خطأ |
| `IEmployeeLifecycleGate` | `HR.Contracts.Application/Abstractions/IEmployeeLifecycleGate.cs` | `AssertCanStartSeparationAsync` بالفحص نفسه |
| الإنشاء | `CreateSeparationRecordHandler` (`SeparationRecordHandlers.cs:56`) | استدعاء البوابة قبل الحفظ |
| `PassThroughEmployeeLifecycleGate` | `EmployeeLifecycleGate.cs:9-13` | يُسجَّل في `Development`/`Testing` فقط. غياب العنوان في غيرها ← فشل الإقلاع |
| سياسة النقطة | `EmployeeLifecycleInternalController.cs:12` | `ServicePolicies.HrCoreInternalS2S` (§8) بعملاء `svc-hr-contracts` و`svc-hr-movements` |

**القواعد:**

1. لا انفصال يُنشأ أو يُنشر لموظف تحت حجز قانوني، ولا حين يتعذّر التحقق.
2. مسار الاعتماد الجديد للانفصال (PLAN-60) يستدعي `AssertCanStartSeparationAsync` نفسها، ولا يُعاد تصميمه هنا.

### 8. حارس معماري: كل `api/internal/*` للخدمات فقط، ومساعد قبول لرموز الخدمات

**المشكلة:** مسارات `api/internal/*` مستثناة من الإثبات (`GatewayCallerProofMiddleware.cs:49`)، لكن ثلاثة متحكمات فيها تقبل المستخدمين:
- `PoliciesController` (`HrCoreReadOnly`/`HrCoreBranchOperation`).
- `CodeGenerationController` (`HrCoreReadOnly`/`HrCoreWrite`).
- `EmployeeLifecycleInternalController` و`ServiceRegimeInternalController` (`HrCoreLookupS2S`، الذي يقبل أي مستخدم مصادَق، `ServiceAuthorizationExtensions.cs:74-82`).

**التصميم:**

| Artifact | المسار | الغرض |
|----------|--------|--------|
| `ServicePolicies.HrCoreInternalS2S` | `ServiceAuthorizationExtensions.cs` | حساب خدمة + `hr-core.lookup` + عملاء مسموحون بحسب `calls[]` في المصفوفة (الحجز: `svc-hr-contracts` و`svc-hr-movements`؛ الأنظمة: هما و`svc-payroll` الذي يستدعي `payroll-blockers`). يُطبَّق على `EmployeeLifecycleInternalController` و`ServiceRegimeInternalController` (`ServiceRegimeInternalController.cs:9-10`) |
| `S2s042InternalRoutesAreServiceOnlyTests` | `src/Tests/HRManagement.UnitTests/Architecture/` | انعكاس على متحكمات كل تجميعات `*.Web`: كل مسار يبدأ بـ `api/internal` يحمل سياسة من مجموعة سياسات الخدمة فقط (`ServicePolicies.*` عدا `HrCoreLookupS2S`) |
| `AcceptanceHarness.CreateServiceClientAsync(clientId, scope)` | `src/Tests/HRManagement.AcceptanceTests/AcceptanceHarness.cs` | رمز `client_credentials` من Keycloak المحلي، بسر من `ACCEPTANCE_SVC_<CLIENT>_SECRET` ثم `ACCEPTANCE_SVC_CLIENT_SECRET`. لا سر مُودع. وهو المساعد الذي تعيد الخطط اللاحقة استعماله لرموز الخدمات |
| المصفوفة | `deploy/wiring/service-wiring.json` (PLAN-37 §1، القاعدة 5) | `calls[].serverPolicy` يتبع السياسات الجديدة: `PolicyEnforceS2S`، و`CodeGenerate`، و`HrCoreInternalS2S` (ومنها عملاء نقطة الأنظمة: `svc-hr-contracts` و`svc-hr-movements` و`svc-payroll`)، و`DsarSubjectErase`. فيبقى `ProductionWiringMatrixTests` أخضر ويثبت أن كل مستدعٍ حقيقي ما زال مقبولاً |

---

## Out of scope

| البند | الخطة |
|-------|-------|
| عناوين الخدمات ونطاقاتها في compose/k8s، وربط `SVC_*` بـ`ServiceIdentity__ClientSecret`، ومفتاح إعداد بوابة الحجز، وشحن `service-regimes.json`، وملف prod-like المطابق للإنتاج | PLAN-37 |
| النشر الذري للأحداث (outbox) بما فيها `IPolicyPublished` | PLAN-40 |
| دورة حياة القاعدة: أربع عيون، تحقق الصيغة، ثبات الإصدار، التراجع | PLAN-47 |
| رؤية القوالب الوطنية لكل الجهات وسقف تجاوز الجهة | PLAN-48 |
| كتالوج المجالات وما يحكمه المحرك، ومواءمة الحقائق والمخططات، والقواعد اليتيمة، وقيود رمز المسار المختار من السياسة (G-POL-09) | PLAN-49 |
| استدعاء بوابة الحجز في مسار اعتماد الانفصال الجديد، ونقطة الدخول الموحدة | PLAN-60، PLAN-61 |
| إعادة كتابة AC-09/AC-10 على خدمات المجال | PLAN-65 |
| mTLS بين البوابة والوحدات، ودوران المفاتيح الآلي | الموجة 2 (→ OPS) |
| دفتر Shadow ذو معنى: الحقائق المنقّحة، والمقارنة بالنتيجة الفعلية (G-POL-10) | الموجة 2 |

## Acceptance scenarios

ملف القبول: الملف الشبيه بالإنتاج (`docker-compose.acceptance-prodlike.yml` كما سلّمه PLAN-37)، وفوقه طبقة اختبار `docker-compose.acceptance-s2s42.yml`. هذه الطبقة تضبط `PolicyEngine__Assets.Custody=${S2S42_ASSETS_MODE:-Enforce}` في `hr-core-web` و`admin-logistics-web`. القيمة `Enforce` لسيناريوهات 42a/42c، و`Shadow` لـ42b (يعاد إنشاء الحاويتين بـ`--no-deps --force-recreate`). وترقيعة بذور القبول `AcceptanceS2s42PolicyPatch` (HR.Core، ملف Acceptance) تنشر إصداراً **جديداً** لقالب عهدة الجهة A، يضم القاعدة `ACC-S2S42-DENY`: `category Equals 'اختبار-رفض'` ← `Deny`. ويضبط الاختبار فئة أصل قبول للجهة A غير مسلَّم إلى «اختبار-رفض» عبر psql على قاعدة Admin.Logistics قبل السيناريو، ويعيدها بعده. كل سيناريو في `AcS2s42TrustBoundaryTests` بـ`[RequiredAcceptance]` و`AcceptanceGate` و`EvidenceRecorder("AC-S2S-42…")`.

| ID | Positive | Negative |
|----|----------|----------|
| AC-S2S-42a | Enforce: `a-logistics-maker` يسلّم أصلاً عادياً ← ينجح، وفي الدفتر صف `Enforce` مع `CallerClientId=svc-admin-logistics` و`EntityId`=معرّف التسليم | أصل بفئة «اختبار-رفض» ← رفض بنص القاعدة ولا تسليم. ثم `docker stop` لـ`hr-core-web` ← تسليم أصل عادي يُرفض بـ«تعذّر التحقق من سياسة Assets.Custody» ولا يتغير الأصل (psql). ثم `docker start` |
| AC-S2S-42b | Shadow (`S2S42_ASSETS_MODE=Shadow`): تسليم أصل مميز ← السلوك كما في Off (دليل إلزامي ومسار العهدة القديم) وصف دفتر `Shadow` | `hr-core-web` متوقف ← التسليم يمضي (Shadow لا يمنع)، وفي سجل `admin-logistics-web` تحذير `policy_client_failure` |
| AC-S2S-42c | `svc-admin-logistics` (رمز خدمة) يرسل enforce داخلياً لـ`AssetAssignment` ← 200 ونسخة `ASSET-ASSIGN-APPROVAL` بمبادر = `X-Acting-User` | `b-branch-manager` ← `POST api/hr-core/api/policies/enforce` غير موجود، و`api/hr-core/api/internal/policies/enforce` ← 403. `svc-admin-logistics` بمجال `Contracts.Leave` ← 403. `ActorUserId` مزيّف في الجسم ← الدفتر يسجل الفاعل من الترويسة. `SubjectType=OfferLetter` ← 400 |
| AC-S2S-42d | طلب واجهة عادي عبر البوابة ينجح بإثبات v2 | من داخل شبكة القبول (`docker exec` على حاوية البوابة، `curl`) إلى `hr-core-web` مباشرة برمز مستخدم صالح: (1) إثبات HMAC v1 ← 403؛ (2) إثبات v2 ملتقط مع رمز آخر ← 403؛ (3) إعادة الإثبات نفسه ← 403 |
| AC-S2S-42e | `hr-core-web` بالمفتاح العام الصحيح يقلع ويجيب `/ready` | `scripts/s2s042-placeholder-boot-check.ps1` يشغّل صورة `hr-core-web` بـ`ASPNETCORE_ENVIRONMENT=Production` و`GatewayCallerProof__PublicKeyPem=USE_ENV_OR_SECRET_MANAGER` ← خروج غير صفري، وفي السجل اسم المفتاح دون قيمته |
| AC-S2S-42f | `svc-compliance` بنطاق `dsar.subject.erase` ← anonymize لمرشح اختبار ← 200 | `svc-compliance` بـ`dsar.subject.read` وحده ← 403 ولا يتغير الطلب (psql) |
| AC-S2S-42g | AC-13 المعاد: 120 توليداً متوازياً بهوية `svc-admin-logistics` ← 120 رقماً فريداً و`IssuedByClientId` مضبوط | `central-a` ← `api/codes/generate` غير موجود، و`api/internal/codes/generate` ← 403 والعدّاد ثابت (psql). `svc-admin-logistics` يطلب `AdministrativeDecision` ← 403. `TenantId` للجهة B في الجسم ← الرقم في مستأجر المستدعي |
| AC-S2S-42h | موظف بلا حجز ← إنشاء الانفصال ونشره ينجحان | موظف `LegalHold=true` ← الإنشاء مرفوض والنشر مرفوض. `hr-core-web` متوقف ← النشر مرفوض بـ«تعذّر التحقق من الحجز القانوني» |
| AC-S2S-42i | دليل ثابت: `kubectl kustomize deploy/k8s/overlays/production-ha` (إن توفر) و`S2s042K8sPerServiceSecretsTests` ← كل Deployment يشير إلى سره وحده | `rg "hr-app-secrets" deploy` ← لا نتائج |

## Verification

```powershell
git switch -c feature/plan-42-s2s-trust-boundary
dotnet build HRManagement.slnx -warnaserror
dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web

# وحدة ومعمارية
dotnet test src/Tests/HRManagement.UnitTests --filter "S2s042|PolicyEngine|PolicyOrchestrator|GatewayPathAuthorization|WebSecurityHelpers|AssetClassifier|Architecture"
# تكامل (يتضمن PostgreSQL عبر Testcontainers)
dotnet test src/Tests/HRManagement.IntegrationTests --filter "S2s042|FakePolicyEnforceClient|Fun002SeparationRecord|CodeGenerationConcurrency|CrossTenantRls"
# انحدار
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm025|Hcm027|Hcm030|Prod001|Est024"

# قبول على الملف الشبيه بالإنتاج
. ./scripts/acceptance-local-env.ps1; Import-AcceptanceLocalEnvironment -RepoRoot (Get-Location)   # يولّد مفاتيح الإثبات إن غابت
docker compose -p hrm-acceptance -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml `
  -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml -f docker-compose.acceptance-prodlike.yml `
  -f docker-compose.acceptance-s2s42.yml --profile full-stack up -d --wait
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "AcS2s42|Ac11Policy|Ac13ParallelCode"
pwsh scripts/s2s042-placeholder-boot-check.ps1
pwsh scripts/s2s042-validate.ps1   # جرد: لا Sign في عملاء الخدمات، ولا hr-app-secrets، ولا api/internal بسياسة مستخدم
```

`S2s042*` الوحدوية: `S2s042PolicyEnforceClientTests` (رفض بـ 200، والأعطال 401/403/500/المهلة، وShadow، وOff، واختلاف الوضع بين الطرفين، والقيمة المجهولة عند الإقلاع)، و`S2s042PolicyEnforceInternalTests` (الملكية والجسم المزيّف والقائمة البيضاء)، و`S2s042GatewayProofV2Tests` (التوقيع والإعادة والرمز المختلف والانحراف وخدمة بلا إثبات)، و`S2s042PlaceholderGuardTests`، و`S2s042CodeGenerationAuthTests`، و`S2s042LifecycleGateTests` (معالج HTTP وهمي)، و`S2s042K8sPerServiceSecretsTests`، و`S2s042InternalRoutesAreServiceOnlyTests`.

`S2s042*` التكاملية: `S2s042PolicyLedgerCallerPostgresTests` (ترحيل الأعمدة والفهرس وكتابة الدفتر على PostgreSQL)، و`S2s042CodeIssuanceTenantPostgresTests` (TenantId من الجسم يُتجاهل، والعدّاد يزيد في مستأجر المستدعي وحده تحت RLS).

## Definition of done

- لا مسار في العميل البعيد يحوّل رفضاً أو عطلاً إلى سماح في Enforce. وShadow لا يغيّر `Allowed` ولا الآثار التي يستهلكها المستدعي.
- لا نقطة `enforce` لمستخدم تفاعلي، والخادم يتجاهل الوضع والفاعل والمستأجر من الجسم.
- لا عميل خدمة يوقّع إثبات بوابة، ولا وحدة تملك ما يصنعه.
- لا يشير أي Deployment إلى سر مشترك. والإقلاع بقيمة مؤقتة يفشل.
- لا يمحو `dsar.subject.read` شيئاً. ولا يستهلك مستخدم رقماً رسمياً عبر HTTP.
- بوابة الحجز مصادَقة ومغلقة عند الفشل، وتعمل عند الإنشاء والنشر.
- كل `api/internal/*` للخدمات فقط، بحارس معماري.
- AC-S2S-42a..i = Pass في `acceptance-evidence/AC-S2S-42/`، وAC-11 وAC-13 المعادتان ناجحتان، وانحدار المرحلة 0 أخضر.
- أسطر «الحالة» محدّثة لـ G-POL-03 وG-POL-08 وG-IAM-13 وG-COD-05 وG-SEP-12 في دراساتها.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-42-s2s-trust-boundary.md` |
| ADR | `docs/ADR-S2S-042-trust-boundary.md` (الإثبات v2، ملكية المجالات والأنواع، سلوك الفشل) |
| Migrations | `S2S042_PolicyLedgerCaller`، `S2S042_CodeIssuanceCaller` (HR.Core) |
| Shared | `PolicyModeResolver.cs`، `PolicyEngineConfigurationValidator.cs`، `PolicyClientMetrics.cs`، `GatewayCallerProofOptions.cs`، `GatewayProofReplayCache.cs`، تمديد `ProductionConfigurationGuard.cs`، `PolicyDomainOwners.cs`، `CodeKindOwners.cs`؛ وتحديث `deploy/wiring/service-wiring.json` و`docker/keycloak/realm-manifest.json` و`init-secrets` |
| HR.Core | `PolicyEnforceInternalController.cs`، `PolicyWorkflowTriggerAllowlist.cs` |
| k8s | `deploy/k8s/overlays/production-ha/external-secrets/<service>-secrets.yaml` ×15، تعديل 15 Deployment |
| Compose | `docker-compose.acceptance-s2s42.yml`، تعديل `docker-compose.production.yml` و`docker-compose.acceptance.yml` |
| Scripts | `scripts/s2s042-new-gateway-proof-keys.ps1`، `scripts/s2s042-placeholder-boot-check.ps1`، `scripts/s2s042-validate.ps1`، تعديل `scripts/acceptance-local-env.ps1` و`scripts/sync-keycloak-dev-realm.ps1` |
| Unit/Integration | الاختبارات المسماة في Verification |
| Acceptance | `AcS2s42TrustBoundaryTests.cs`، `AcceptanceS2s42PolicyPatch` (HR.Core Seeding)، تعديل `Ac11PolicyWorkflowTests.cs` و`Ac13ParallelCodeGenerationTests.cs` و`AcceptanceHarness.cs` |
| Docs | `docs/S2S_SECRET_MATRIX.md`، تحديث `docs/SERVICE_TRUST_INVENTORY.md` و`docs/POLICY_ENGINE_UAT.md`؛ أسطر «الحالة» في `POL_001_GAP_STUDY.md` و`IAM_001_SCOPE_GAP_STUDY.md` و`COD_ORG_GAP_STUDY.md` و`FUN_002_SEPARATION_GAP_STUDY.md` |
| Report | `reports/plan42-baseline.md`، `reports/plan42-final-report.md` |
| CI | `tests/acceptance/ci-scope.json`: تسجيل كل صنف قبول جديد في هذه الخطة (مطلوباً أو مستبعداً بمالك)، وإلا تفشل وظيفة القبول (قاعدة PLAN-41) |

## Risks

| الخطر | التخفيف |
|-------|---------|
| `FailClosed` يجعل HR.Core نقطة توقف لمجالات Enforce | لا مجال بعيد في Enforce في الإنتاج اليوم (PLAN-49 يضعها في Shadow). وD-POL-2 قابل للضبط لكل مجال. ورسالة الفشل تذكر السبب |
| `AuthenticateAsync` داخل وسيط الإثبات يكرر التحقق من الرمز | نتيجة المصادقة تُخزَّن في `HttpContext.Features` ويعيد استعمالها `UseAuthentication`. اختبار أداء بسيط (100 طلب) في `S2s042GatewayProofV2Tests` |
| نسخ متعددة من وحدة لا تتشارك ذاكرة الـ nonce | المدة 60 ثانية والإثبات مربوط بالرمز، وموثّق في ADR. ومشاركة الذاكرة (Redis) ← الموجة 2 |
| تقسيم الأسرار يكسر خدمة تقرأ مفتاحاً لم يُجرَد | المصفوفة تُبنى من جرد الشيفرة. وإقلاع الملف الشبيه بالإنتاج يكشف الغياب (`ProductionConfigurationGuard` والمفاتيح المطلوبة) |
| AC-11/AC-13 يعتمدان على سر عميل خدمة في القبول | `ACCEPTANCE_SVC_*` من `acceptance-local-env.ps1`، و`AcceptanceGate` يكتب `Blocked` عند غيابه، لا `Pass` |
| `kubectl` غير مثبت على جهاز المالك | `S2s042K8sPerServiceSecretsTests` نصي لا يحتاجه. و`kustomize` اختياري ويُسجَّل غيابه |

## Effort estimate

9–10 أيام عمل: المرحلة 0 (0.5)، العميل والوضع (1.5)، نقطة الإنفاذ الداخلية والدفتر (1.5)، إثبات البوابة v2 (2)، الأسرار والقيم المؤقتة (1)، DSAR (0.5)، توليد الرموز (1)، بوابة الحجز (0.5)، الحارس المعماري والقبول والتوثيق (1.5).

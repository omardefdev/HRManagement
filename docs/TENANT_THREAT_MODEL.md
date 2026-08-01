# نموذج تهديد حدود الجهات (TEN-001 / TEN-002 / TEN-003) وفصل الواجبات (IAM-003)

## الأصول

- بيانات موظفين وعقود ورواتب خاصة بكل جهة
- رموز JWT وclaims (`tenant_id`, `scope_org_unit_id`, الأدوار)
- رسائل MassTransit وheaders (`X-Tenant-ID`)
- جداول مشتركة بعمود `TenantId` إلزامي + RLS
- أدلة ترحيل ومصالحة الحجر (`TenantAssignmentRule`, `TenantQuarantineEntry`)
- مصفوفة فصل الواجبات والتفويض (`SodRule`, `ConflictingRolePair`, `ApprovalDelegation`)

## المهاجمون / السيناريوهات

| التهديد | الوصف | التخفيف |
|---------|--------|---------|
| تزوير claim الجهة | عميل يضع `tenant_id` في header أو body | المصدر الوحيد: JWT موقّع؛ الإدراج بلا tenant يُرفض (TEN-002) |
| تسريب أفقي عبر API | مستخدم جهة A يطلب مورد جهة B | مرشحات EF عالمية؛ اختبارات اختراق API (`CrossTenantApiPenetrationTests`) تثبت 404/قائمة فارغة لا 403 |
| تجاوز مرشح التطبيق | خطأ في filter أو `IgnoreQueryFilters` عشوائي | PostgreSQL RLS عبر `app.current_tenant` على 14 قاعدة؛ bypass فقط عبر `TenantFilterBypass` لمشغّل المنصة |
| تسميم الرسائل | رسالة بلا tenant أو جهة خاطئة | `TenantIdConsumeFilter` + رفض الإدراج بلا TenantId؛ `CrossTenantEventLeakageTests` |
| إساءة مشغّل المنصة | `platform-admin` يصل لكل الجهات | الدور صريح؛ `IgnoreTenantFilter` يرمي بدون الدور |
| شجرة OrgUnit مشتركة | لقطة عبر جهات | `OrgUnitSnapshot.TenantId` + filter + RLS منفصل على اللقطات |
| بقاء بيانات على PLATFORM | صفوف بعد TEN-002 بلا جهة حقيقية | `assign-tenants.ps1` + حجر إلزامي + مصالحة (`reconcile-tenants.ps1`) |
| تسريب تصدير/بحث/كاش | مسار يتجاوز EF | لا مسارات إنتاج تستخدم `IgnoreQueryFilters` خارج `TenantFilterBypass`؛ اختبارات تتحقق |
| اعتماد مزوّر / self-approve | عميل يمرّر `approverId` أو يعتمد عمله | هوية من التوكن فقط؛ `SeparationOfDutiesService`؛ اختبارات `SoDForgedApproverTests` |
| جمع أدوار متعارضة | processor+approver أو approver+poster | `ConflictingRolePair` + `RoleAssignmentGuard` + تقرير `sod-report.ps1` |
| تصعيد ذاتي للأدوار | مستخدم يمنح لنفسه دوراً أعلى | منع self-grant + ترتيب رتب + four-eyes للأدوار الحساسة |

## حدود المنصة المستثناة

- `Tenant`, `TenantAssignmentRule`, `TenantQuarantineEntry`, `SodRule`, `ConflictingRolePair`, `EntityAuditLog`, `IntegrationMessageReceipt`, جداول MassTransit outbox
- نقاط الصحة والتليمترية

## حالة المخاطر

| مخاطرة سابقاً | الحالة بعد TEN-003 / IAM-003 |
|---------------|---------------------|
| `TenantId` اختياري | إلزامي (NOT NULL) مع backfill ثم إسناد حقيقي أو حجر |
| لا مرشحات EF | `ConfigureTenantQueryFilters` على كل DbContext خدمي |
| لا RLS | سياسات `tenant_isolation` + اختبارات لكل الخدمات الـ14 |
| ختم صامت عند غياب السياق | رفض صريح للإدراج المملوك بلا tenant |
| كل الصفوف على الجهة الافتراضية | أدوات إسناد + حجر + تقرير مصالحة مرفق بالإصدار |
| لا اختبارات اختراق أفقية | وحدة + API + أحداث + RLS |
| فصل واجبات جزئي (رواتب فقط) | مصفوفة مركزية + منع self-approve + هوية موثوقة على كل القنوات |
| `approverId` من العميل | أُزيل؛ المصدر الوحيد هو JWT |

## إغلاق TEN-003

| بند القبول | الدليل |
|------------|--------|
| 100% منسوب أو محجور | `scripts/assign-tenants.ps1` → `TenantQuarantineEntries` |
| counts/checksums | `scripts/reconcile-tenants.ps1` → JSON |
| لا تسريب API | `CrossTenantApiPenetrationTests` |
| لا تسريب أحداث | `CrossTenantEventLeakageTests` |
| لا تسريب تصدير/بحث/كاش | مرشحات EF + RLS؛ فحص عدم تجاوز المرشح |
| تقرير استثناءات | جدول الحجر + تقرير المصالحة |
| Dry-run staging | `docs/TENANT_MIGRATION_RUNBOOK.md` |

**الحالة:** مخاطر TEN-003 مُخفَّفة عبر الأدوات والاختبارات أعلاه. أي صف إنتاج متبقٍ على PLATFORM يجب أن يظهر في الحجر مع قرار مالك بيانات قبل إغلاق الإصدار.

## إغلاق IAM-003

| بند القبول | الدليل |
|------------|--------|
| قواعد الفصل على الخادم لكل قناة | `SeparationOfDutiesService` + handlers/controllers |
| اعتماد بهوية موثوقة وقرار وتعليل | إزالة `approverId`؛ `ApprovalStep.ReviewedBy/Comments` |
| تعارض الأدوار يُرفض | `RoleAssignmentGuard` + `ConflictingRolePair` |
| تقارير SoD دورية | `scripts/sod-report.ps1` + `docs/SOD_MATRIX.md` |
| اختبارات سياسة وself-approve/forged | `SeparationOfDutiesTests`, `SoDPolicyIntegrationTests`, `SoDForgedApproverTests` |

**الحالة:** مخاطر IAM-003 مُخفَّفة. التفويض المؤقت والحدود المالية والاعتماد المزدوج للأدوار/البيانات القانونية مفعّلة على الخادم.

## تهديدات S2S (IAM-004)

| التهديد | التخفيف |
|---------|---------|
| نداء داخلي مجهول (`/api/internal/directory`) | `[Authorize(Policy = Service.DirectorySync)]` + scope `directory.sync` |
| تمرير توكن مستخدم لمسارات مراجع الملفات | `ServiceAuthHandler` + سياسة `Service.FileStorageReferences` |
| Payroll يستدعي lookup بلا هوية | named client + `hr-core.lookup` |
| مستخدم Postgres/RabbitMQ/MinIO مشترك | أدوار `*_app` / `mq_*` / `MINIO_FILESTORAGE_*` لكل خدمة |
| تدوير سر يوقف المنصة | سر لكل عميل؛ انظر `SERVICE_SECRET_ROTATION.md` |

المخزون: [SERVICE_TRUST_INVENTORY.md](./SERVICE_TRUST_INVENTORY.md). العزل الشبكي (SEC-001) لا يغني عن JWT داخل شبكة `application`.

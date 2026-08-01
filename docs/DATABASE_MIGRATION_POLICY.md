# سياسة ترحيل قواعد البيانات (EF Core) — DB-001

## السياقات (15)

| Bundle | مشروع البنية | مشروع الإقلاع |
|---|---|---|
| `gateway` | `HRManagement.Gateway` | نفس المشروع |
| `hr-core` | `HR.Core.Infrastructure` | `HR.Core.Web` |
| `hr-contracts` | `HR.Contracts.Infrastructure` | `HR.Contracts.Web` |
| `payroll` | `Payroll.Infrastructure` | `Payroll.Web` |
| `hr-health` | `HR.Health.Infrastructure` | `HR.Health.Web` |
| `hr-movements` | `HR.Movements.Infrastructure` | `HR.Movements.Web` |
| `hr-training` | `HR.Training.Infrastructure` | `HR.Training.Web` |
| `idcard` | `IDCard.Infrastructure` | `IDCard.Web` |
| `records-archive` | `Records.Archive.Infrastructure` | `Records.Archive.Web` |
| `admin-logistics` | `Admin.Logistics.Infrastructure` | `Admin.Logistics.Web` |
| `it-infra` | `IT.Infra.Infrastructure` | `IT.Infra.Web` |
| `dev-analytics` | `Dev.Analytics.Infrastructure` | `Dev.Analytics.Web` |
| `compliance` | `Compliance.Infrastructure` | `Compliance.Web` |
| `auth-directory` | `Auth.Directory.Infrastructure` | `Auth.Directory.Web` |
| `file-storage` | `File.Storage.Infrastructure` | `File.Storage.Web` |

قائمة التشغيل الموحّدة: `scripts/run-migrations.sh` و`scripts/check-pending-ef-model-changes.sh`.

## التطوير المحلي

- الافتراضي: `Database:RunMigrationsAtStartup` غير مضبوط أو `true` — التطبيق يستدعي `MigrateAsync` عند الإقلاع عبر `TryMigrateAsync<TContext>()`.
- على Npgsql يُكتسب `pg_advisory_lock(8675309)` قبل الترحيل (نفس مفتاح السكربت) ثم يُحرَّر في `finally`.
- عند الفشل خارج الإنتاج: تحذير دون إيقاف التشغيل.

## الإنتاج

1. **عطّل الترحيل التلقائي** في إعدادات البيئة أو `appsettings.Production.json`:

   ```json
   "Database": {
     "RunMigrationsAtStartup": false
   }
   ```

   أو: `Database__RunMigrationsAtStartup=false`

2. **طبّق الترحيلات من CI/CD قبل النشر** عبر `scripts/run-migrations.sh`:
   - يفضّل تشغيل **efbundle** من `artifacts/efbundles/<name>` إن وُجد.
   - وإلا: `dotnet ef database update` من المصدر (مسار التطوير المحلي).
   - فشل أي سياق يوقف النشر (`deploy.yml` job `migrate`).

3. **بناء الحزم:** `bash scripts/build-ef-bundles.sh` (يُنفَّذ في job الترحيل قبل التطبيق).

## Expand → Migrate → Contract (EMC)

للترحيلات غير المتوافقة مع الإصدارات المتزامنة للنسخ القديمة/الجديدة:

1. **Expand:** أضف أعمدة/جداول جديدة قابلة للعدم أو بجداول ظل؛ لا تحذف ولا تضيّق قيوداً تكسّر القراء القدامى.
2. **Migrate:** انشر التطبيق الذي يكتب للشكل الموسَّع؛ انقل البيانات الخلفية إن لزم.
3. **Contract:** في إصدار لاحق فقط، احذف الأعمدة القديمة / افرض NOT NULL بعد أن تتوقف كل النسخ القديمة عن الاعتماد عليها.

لا تدمج خطوة Contract في نفس إصدار Expand إذا كانت النسخ القديمة ما زالت تعمل.

## Dry-run

قبل إنتاج:

1. `bash scripts/check-pending-ef-model-changes.sh` — يجب أن يكون أخضر (لا انحراف نموذج).
2. ابنِ الحزم على نفس الـ commit المثبَّت: `bash scripts/build-ef-bundles.sh`.
3. على قاعدة **staging** (نسخة حديثة من الإنتاج إن أمكن): شغّل `run-migrations.sh` بنفس سلاسل الاتصال التجريبية.
4. حيث يدعم الـ bundle: `./artifacts/efbundles/<name> --connection "..." --dry-run` للتحقق من SQL دون تطبيق دائم (إن توفّر في إصدار الأداة).

## Rollback

- **الإنتاج لا يعتمد reverse migration** (`dotnet ef database update <previous>`) كمسار أساسي للاستعادة.
- **Rollback = استعادة قاعدة البيانات:** PITR أو لقطة PostgreSQL مأخوذة قبل الترحيل (`scripts/backup-postgres.sh` / سياسة OPS)، ثم إعادة نشر صورة التطبيق السابقة إن لزم.
- سجّل rehearsal دوري: نسخة staging ← لقطة ← ترحيل ← فشل متعمّد ← استعادة لقطة ← تحقق صحة.

## قفل مزدوج

| طبقة | الآلية |
|---|---|
| CI/سكربت | `pg_advisory_lock(8675309)` عبر جلسة `psql` في `run-migrations.sh` |
| إقلاع التطبيق | نفس المفتاح في `TryMigrateAsync` على اتصال Npgsql للسياق |

`MIGRATION_LOCK_CONNECTION` يفضّل `ConnectionStrings__GatewayAuditDb` لتسلسل تشغيلات CI المتزامنة.

## الأوامر المرجعية

```bash
dotnet tool restore
bash scripts/check-pending-ef-model-changes.sh
bash scripts/build-ef-bundles.sh
bash scripts/run-migrations.sh
```

## المراجع

- التنفيذ: `HRManagement.ServiceDefaults.DatabaseMigrationExtensions.TryMigrateAsync`
- قابلية إعادة الإنتاج: `docs/REPRODUCIBLE_BUILDS.md`

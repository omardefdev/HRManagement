# قابل لإعادة الإنتاج — البناء والتبعيات (DB-001)

## الهدف

clone نظيف يعيد نفس رسم التبعيات (dependency graph) ويستخدم أدوات EF مثبتة الإصدار، دون wildcards في `PackageReference`.

## Central Package Management

- `Directory.Packages.props` — `ManagePackageVersionsCentrally=true` وجميع أرقام الحزم.
- المشاريع تعلن `<PackageReference Include="..." />` **بدون** `Version`.
- `RestorePackagesWithLockFile=true` يولّد `packages.lock.json` لكل مشروع ذي مراجع NuGet.

## Locked restore

محلياً وبعد تحديث الحزم:

```bash
dotnet restore HRManagement.slnx
```

في CI (إلزامي):

```bash
dotnet restore HRManagement.slnx --locked-mode
```

أي تغيير في الحزم دون تحديث lock files يفشل الـ restore في CI.

## SDK وdotnet-ef

| ملف | الدور |
|---|---|
| `global.json` | SDK مثبت + `rollForward: latestPatch` فقط |
| `.config/dotnet-tools.json` | `dotnet-ef` بنفس إصدار EF Core (مثلاً 10.0.10) |

```bash
dotnet tool restore   # لا تستخدم tool install عائم في النشر
```

## بوابة CI

- رفض `Version="*"` / `Version="N.*"` في `*.csproj`
- `dotnet restore --locked-mode`
- `scripts/check-pending-ef-model-changes.sh` لكل الـ15 سياق

## ملاحظة اعتمادية

DB-001 يغلق قابلية إعادة إنتاج التبعيات والترحيل. **DEP-001** Done (بوابة OPS-001؛ انظر [DEP_001_RELEASE_GATE.md](./DEP_001_RELEASE_GATE.md)). بوابة الجودة الأوسع تبقى تحت **QA-001** إن كانت مفتوحة.

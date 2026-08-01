# SCM-001 — استعادة مستودع المصدر

## أهداف

| المقياس | الهدف |
|---|---|
| RPO | ≤ 24 ساعة (نسخة يومية + GitHub كأساسي) |
| RTO | ≤ 4 ساعات لاستعادة mirror قابل للعمل |

## النسخ الاحتياطي

```powershell
powershell -File scripts/scm001-repo-backup.ps1
```

ينشئ `git clone --mirror` تحت `artifacts/scm-backups/` (مستثنى من Git).

يُفضَّل رفع الـ mirror المشفّر إلى مخزن غير GitHub (object storage داخلي / شريط).

## اختبار الاستعادة

1. `git clone artifacts/scm-backups/HRManagement.git restore-test`
2. التحقق من وجود `main` وآخر tag موقع
3. `dotnet build` على نسخة مستعادة (عينة)
4. تسجيل النتيجة في `docs/scm-governance/restore-drill-log.md`

## سيناريو فقدان GitHub org

1. استعادة mirror إلى Git host بديل أو self-hosted.
2. تحديث remotes و CI secrets.
3. إعادة تفعيل branch protection و CODEOWNERS.
4. إبلاغ الفريق (SRE-001 escalation إن أثّر على الإنتاج).

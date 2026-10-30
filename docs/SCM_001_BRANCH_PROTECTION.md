# SCM-001 — Branch Protection

## الهدف

منع الدمج المباشر أو الذاتي على الفروع المحمية، وإلزام مراجعات مستقلة وفحوص CI قبل الدمج.

## الفروع المحمية

| الفرع | الحماية |
|---|---|
| `main` | كاملة (أدناه) |
| `release/*` | كاملة |
| `hotfix/*` | كاملة |

## قواعد `main` (السياسة)

| القاعدة | القيمة |
|---|---|
| Require pull request | نعم |
| Required approving reviews | 2 للمسارات الحرجة (`.github/`, `deploy/`, `src/Shared/`, secrets/IaC)؛ 1 لغيرها |
| Dismiss stale reviews | نعم |
| Require review from CODEOWNERS | نعم |
| Require status checks | `CI`, `CodeQL`, `Gitleaks` (أسماء وظائف `ci.yml` كما تظهر في GitHub)، و`E2E` بعد تشغيلين أخضرين، و`Acceptance` مع PLAN-41 |
| Require conversation resolution | نعم |
| Require signed commits | نعم |
| Require linear history | مفضّل |
| Allow force pushes | **لا** |
| Allow deletions | **لا** |
| Allow admin bypass | **لا** |
| Restrict who can merge | maintainers فقط بعد المراجعات |

## منع self-merge

- المراجع لا يكون هو مؤلف الـ PR.
- عند مالك واحد مؤقت (`@omardefdev`) يُطلب مراجعة ثانية من راعٍ/أمن (انظر STR-005 / SYD-017 لتوسيع الفرق).
- التحقق الآلي: `scripts/scm001-branch-protection-validate.ps1`

## القيم المطبّقة الآن (PLAN-35) واستثناءاتها المؤرخة

الملف [`docs/scm-governance/branch-protection-main.json`](scm-governance/branch-protection-main.json) هو المصدر. كل قيمة أضعف من
السياسة مسجّلة بمعرّف في `exceptionRefs`، يشير إلى بند مفتوح غير منتهٍ في
[`docs/qa/governance-exceptions.json`](qa/governance-exceptions.json). وكل بند التزام مؤرخ (`QA-GOVERNANCE-EXCEPTIONS`):

| القاعدة | القيمة الآن | الاستثناء | المراجعة |
|---|---|---|---|
| Required approving reviews / CODEOWNERS / last-push approval | 0 / لا / لا | `SCM-REVIEW-COUNT-0` (D-QA-1، مطوّر واحد؛ يُغلق بمراجع ثانٍ، SYD-017) | 2027-01-28 |
| Require signed commits | لا | `SCM-UNSIGNED-COMMITS` (D-QA-16؛ لا إعادة كتابة للتاريخ) | 2027-01-28 |
| Require linear history | لا | `SCM-MERGE-COMMITS` (التاريخ المدفوع فيه commits دمج، والـ PR تُدمج بـ merge) | 2027-01-28 |
| PR إلزامي، فحوص `strict`، enforce admins، لا force-push ولا حذف | نعم | لا استثناء ممكن | — |

المدقّق يرفض:
- أي قيمة أضعف بلا استثناء، أو باستثناء مغلق أو منتهٍ؛
- أي فحص مطلوب ليس اسم وظيفة في `ci.yml`.

و`-CheckLiveGitHub` يقارن الإعداد الحي بالملف حقلاً حقلاً (`Scm001BranchProtectionExceptionTests`).

## تطبيق على GitHub

```powershell
# يتطلب gh auth بحساب له ADMIN على المستودع (omardefdev)
powershell -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File scripts/scm/apply-branch-protection.ps1
powershell -ExecutionPolicy Bypass -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub
```

الملف ليس جسم الـ API مباشرة، ففيه `exceptionRefs` و`pendingRequiredChecks`. سكربت التطبيق يبني الجسم، ويضبط التوقيع عبر
`/required_signatures`. إن تعذرت الحماية (مستودع خاص على الخطة المجانية):
`powershell -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1 -ProtectMain`.

انظر أيضاً [SCM_001_SIGNING_POLICY.md](./SCM_001_SIGNING_POLICY.md) و[SCM_001_DEVELOPER_IDENTITY.md](./SCM_001_DEVELOPER_IDENTITY.md).

# SCM-001 — Branch Protection

## الهدف

منع الدمج المباشر أو الذاتي على الفروع المحمية، وإلزام مراجعات مستقلة وفحوص CI قبل الدمج.

## الفروع المحمية

| الفرع | الحماية |
|---|---|
| `main` | كاملة (أدناه) |
| `release/*` | كاملة |
| `hotfix/*` | كاملة |

## قواعد `main` (مطلوبة)

| القاعدة | القيمة |
|---|---|
| Require pull request | نعم |
| Required approving reviews | 2 للمسارات الحرجة (`.github/`, `deploy/`, `src/Shared/`, secrets/IaC)؛ 1 لغيرها |
| Dismiss stale reviews | نعم |
| Require review from CODEOWNERS | نعم |
| Require status checks | `CI`, `CodeQL`, `Gitleaks` (أسماء الـ checks كما تظهر في GitHub Actions) |
| Require conversation resolution | نعم |
| Require signed commits | نعم |
| Require linear history | مفضّل |
| Allow force pushes | **لا** |
| Allow deletions | **لا** |
| Allow admin bypass | **لا** |
| Restrict who can merge | maintainers فقط بعد المراجعات |

## منع self-merge

- المراجع لا يكون هو مؤلف الـ PR.
- عند مالك واحد مؤقت (`@omard`) يُطلب مراجعة ثانية من راعٍ/أمن (انظر STR-005 / SYD-017 لتوسيع الفرق).
- التحقق الآلي: `scripts/scm001-branch-protection-validate.ps1`

## تطبيق على GitHub

```bash
# مثال — يتطلب gh auth + صلاحيات admin على المستودع
gh api -X PUT repos/{owner}/{repo}/branches/main/protection \
  --input docs/scm-governance/branch-protection-main.json
```

انظر أيضاً [SCM_001_SIGNING_POLICY.md](./SCM_001_SIGNING_POLICY.md) و[SCM_001_DEVELOPER_IDENTITY.md](./SCM_001_DEVELOPER_IDENTITY.md).

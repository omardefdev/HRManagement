# جرد سلسلة التوريد (SUP-001)

مرجع: [ADR-SUP-001-supply-chain.md](./ADR-SUP-001-supply-chain.md).

| سطح | خطر | حالة قبل | قصة الإغلاق |
|---|---|---|---|
| NuGet (CPM + locks) | إصدارات عائمة / CVE | CPM + `--locked-mode` + wildcard guard + `dotnet list --vulnerable` | B/C |
| Dockerfiles تطبيق | أساس عائم `:latest` / tag بلا سياسة | `sdk:10.0` / `aspnet:10.0` | B (`docker/base-image-pins.json`) |
| GHCR صور خدمات | نشر بلا توقيع/SBOM | SHA12 + digest artifact | D |
| Observability compose | صور عامة بلا digest | tags نسخ؛ مرآة = SAN-001 | B توثيق؛ SAN-001 لاحقاً |
| GitHub Actions | supply chain Actions | pins جزئية | E Dependabot |
| أسرار في Git | تسريب | لا Gitleaks | C |
| SAST | عيوب كود | لا CodeQL | C |
| IaC | انحراف سياسة | Conftest موجود | C تأكيد |
| تراخيص | GPL/AGPL غير مراجعة آلياً | `THIRD_PARTY_LICENSES.md` | E |
| Advisories | لا بطاقة تلقائية | — | E |

## معايير القبول → قصة

| AC | Story |
|---|---|
| لا Crit/High دون استثناء زمني | C |
| Deploy يتحقق من signature | D |
| SBOM مرتبط بالrelease | D |
| الحزم العائمة ممنوعة | B |
| Advisory → بطاقة | E |

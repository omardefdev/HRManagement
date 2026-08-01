# SUP-001 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + جرد + أدوات | [ADR-SUP-001-supply-chain.md](./ADR-SUP-001-supply-chain.md), [SUPPLY_CHAIN_INVENTORY.md](./SUPPLY_CHAIN_INVENTORY.md) | Ready |
| حظر العائم | `ci.yml` wildcard guard + `scripts/check-base-image-pins.sh` + [`docker/base-image-pins.json`](../docker/base-image-pins.json) | OK |
| GHCR retention | [GHCR_RETENTION.md](./GHCR_RETENTION.md) (90d digests/SBOM) | OK |
| Crit/High + استثناء منتهٍ | `supply-chain-exceptions.json` + `check-supply-chain-exceptions.sh` + `check-nuget-vulnerabilities.sh` | OK |
| Fixture ضعف SCA | `scripts/verify-sca-fixture.sh` في CI | OK |
| Trivy Crit/High على صور الإصدار | `deploy.yml` بعد push | OK |
| CodeQL / Gitleaks / Conftest | `codeql.yml`, `gitleaks.yml`, `iac-plan.yml` conftest | OK |
| SBOM لكل image ↔ SHA | `anchore/sbom-action` artifact `sbom-${service}` + tag SHA12 | OK |
| Cosign sign + deploy verify | sign في build-and-push؛ `verify-image-signatures.sh` قبل SSH | OK |
| صورة غير موقعة | verify يفشل → لا deploy (اختبار سلبي تشغيلي) | موثّق |
| License | [LICENSE_POLICY.md](./LICENSE_POLICY.md) + `check-license-policy.sh` | OK |
| Dependabot → Issue | `.github/dependabot.yml` + `advisory-to-issue.yml` + [DEPENDENCY_UPDATE_SLA.md](./DEPENDENCY_UPDATE_SLA.md) | OK |

## خارج النطاق (متعمد)

SAN-001 (مرآة سيادية)، BLD-001 (admission عنقودي)، إعادة QA-004.

## إغلاق البطاقة

**SUP-001 = Done** عند دمج هذه الحزمة وتحديث TRACEABILITY / CI_AND_TESTING.

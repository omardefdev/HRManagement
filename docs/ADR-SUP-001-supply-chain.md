# ADR-SUP-001 — أمن سلسلة التوريد وSBOM والتوقيع

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | SUP-001 (البطاقة 44) |
| التاريخ | 2026-07-28 |

## السياق

QA-001 وفّر بوابة NuGet Crit/High؛ DEP-001 وفّر GHCR بوسوم SHA وdigests كآثار. لا SBOM ولا Cosign ولا تحقق توقيع قبل النشر ولا استثناءات موقّتة ولا Dependabot→بطاقة.

## القرار

| الموضوع | الاختيار |
|---|---|
| Registry | GHCR الحالي |
| هوية الصورة | `${service}:${SHA12}` + digest (DEP-001) + **Cosign** (keyless OIDC على Actions) |
| SBOM | Syft / `anchore/sbom-action` → CycloneDX مربوط بـSHA/digest |
| Deploy | `cosign verify` على الـrunner **قبل** SSH pull |
| مسح | NuGet SCA (موجود) + Trivy صور + CodeQL + Gitleaks + Conftest IaC |
| استثناء Crit/High | [`supply-chain-exceptions.json`](../supply-chain-exceptions.json) بتاريخ انتهاء؛ منتهٍ = فشل |
| تحديثات | Dependabot + Issue بوسم `SUP-001` / `advisory` |
| License | سياسة فوق `THIRD_PARTY_LICENSES.md` + فحص deny-list في CI |

## عملية الاستثناء

1. إضافة صف في `supply-chain-exceptions.json` (id, package أو CVE, severity, owner, expiresOn ISO, reason).
2. مراجعة AppSec؛ لا تمديد صامت بعد الانتهاء.
3. CI يفشل إن وُجد Crit/High غير مغطى أو استثناء منتهٍ.

## خارج النطاق

- **SAN-001:** مرآة سيادية / air-gap / عقوبات.
- **BLD-001:** بناء معزول وadmission controllers على العنقود.
- **QA-004:** رحلات E2E (Done).

## العواقب

- deploy أطول (SBOM + sign + verify).
- استثناءات بلا تاريخ انتهاء مرفوضة.

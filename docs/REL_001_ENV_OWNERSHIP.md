# REL-001 — ملكية بيئة Staging (RACI)

| نشاط | Responsible | Accountable | Consulted | Informed |
|---|---|---|---|---|
| أسرار staging (ExternalSecrets / lab) | platform | platform-lead | security | release |
| DNS `hr.staging.example.com` | platform | platform-lead | network | release |
| Keycloak realm import (lab) | iam | platform-lead | qa | release |
| DB reset / synthetic seed | platform + qa | platform-lead | security | program |
| Parity-diff / TLS overlays | platform | platform-lead | sre | release |
| Promote digest staging→prod | release | program | platform, qa | security |
| Approve promote | program | program | security, qa | all |
| OBS / PII scan على staging | sre | sre-lead | security | qa |
| Feature flags (`EnabledModules`) | platform | platform-lead | product | release |

## قواعد

- لا يُرقّى digest إلى الإنتاج دون توقيع Accountable للـpromote + نجاح rehearsal checklist.
- أسرار الإنتاج لا تُنسخ إلى staging؛ lab secrets فقط.
- Reset staging يحتاج Responsible platform؛ إعلام program عند wipe كامل.

انظر [ADR-REL-001](ADR-REL-001-staging-synthetic-parity.md).

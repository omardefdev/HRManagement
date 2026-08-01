# GOV-002 — حزمة التحقق

| تحقق | دليل | نتيجة |
|---|---|---|
| ADR + سجل | [ADR-GOV-002](./ADR-GOV-002-legal-decision-register.md), [register](./legal-decisions/register.json), [Markdown](./GOV_002_LEGAL_DECISION_REGISTER.md) | Ready |
| إزالة LEGAL_TBD | `check-no-legal-tbd.py` في CI؛ PAYROLL_RULES مربوط بـdecisionId | OK |
| تضارب/رجعية/تقريب + notice | [CONFLICT doc](./GOV_002_CONFLICT_RETRO_ROUNDING.md), [RUNBOOK](./GOV_002_CHANGE_NOTICE_RUNBOOK.md), `PayrollConfigChangeNotice` | OK |
| LegalAttest قبل Publish | `PayrollLegalGovernance` + UI تصديق؛ `Gov002LegalGovernanceTests` | OK |
| Golden / replay | `testdata/gov002/golden-cases.json` + `Gov002LegalGoldenPackTests` | OK |

## خارج النطاق

SYR-001، NAT-003، PROD-001، GOV-003؛ CIVIL_SERVICE_2026 = PendingEnactment.

**كتالوج الضوابط الوطني متعدد المجالات:** [STR-002](./ADR-STR-002-legal-control-catalog.md) / [`str-legal/control-catalog.json`](./str-legal/control-catalog.json) — GOV-002 يبقى سجل قرارات الرواتب فقط ولا يستبدله.

## إغلاق

**GOV-002 = Done** مع تحديث TRACEABILITY / CI_AND_TESTING.

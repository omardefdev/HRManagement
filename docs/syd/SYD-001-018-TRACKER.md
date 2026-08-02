# SYD-001..018 — حالة الإغلاق

**الحالة:** NOT-CLOSED (اعتماد رسمي خارج المستودع مطلوب)

## Repo-side closure tooling

| أداة | مسار | حالة |
|------|------|------|
| Legal register validator | `scripts/gov002-validate.ps1` | Ready |
| Legal register | `docs/legal-decisions/register.json` | RepoReady — `closureTooling.repoStatus=Ready` |
| Go/No-Go live gate | `scripts/go-no-go-gate.sh` | Ready — rejects template-only |
| Anti-template checklist | `docs/GO_NO_GO_LIVE_EVIDENCE_CHECKLIST.md` | Ready |
| NAT-006 harness | `scripts/nat006-validate.ps1` | Done (live cert Blocked) |

**CI artifact:** `testdata/gov002/out/validate-report.json`

**GitHub CI (G0):** https://github.com/omardefdev/HRManagement/actions/workflows/ci.yml — PR [#1](https://github.com/omardefdev/HRManagement/pull/1). Repo-side validators green locally (`gov002-validate.ps1`, `nat006-validate.ps1`). National Go/No-Go remains **NOT-CLOSED** until external SYD sign-offs.

## Blockers (official sign-off outside repo)

| المعرّف | الموضوع | المالك | Blocker | Repo tooling |
|---------|---------|--------|---------|--------------|
| SYD-001 | اعتماد سياسة الخصوصية الوطنية | Legal | توقيع لجنة SYD | `register.json` → SYD-001-INTERIM |
| SYD-012 | ربط الخزينة Live | Treasury+Payroll | شهادة EXT-003 + MOU | `go-no-go-gate.sh` external channel |
| SYD-013 | مطابقة بنكية Live | Finance | ات agreement مصرف | payment export metrics + EXT |
| EVD-003 | مستودع أدلة غير قابل للتعديل | Platform | WORM prod + توقيع | evidence ingest CI hook |
| DEC-001 | سجل القرارات القانونية | Legal | إغلاق `ApprovedInterim` | `gov002-validate.ps1` |
| TRN-002 | تدريب المشغّلين | HR Ops | جدول تدريب موقّع | — |
| PHY-001 | أمن físico للمركز | Facilities | تقرير تفتيش | — |

**دليل CI:** `dotnet build` + unit/integration/E2E عند تفعيل `RUN_TESTCONTAINERS` / `RUN_PLAYWRIGHT_E2E`.

**Go/No-Go:** [GO_NO_GO_LIVE_EVIDENCE_CHECKLIST.md](../GO_NO_GO_LIVE_EVIDENCE_CHECKLIST.md) — بانتظار 3 دورات shadow payroll موقّعة.  
**National matrix:** [11-NATIONAL-GO-NO-GO-CHECKLIST.md](../completion-backlog/11-NATIONAL-GO-NO-GO-CHECKLIST.md).

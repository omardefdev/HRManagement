# ADR-GOV-003 — حوكمة البيانات الرئيسية وجودتها

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | GOV-003 (البطاقة 49) |
| التاريخ | 2026-07-28 |
| الاعتمادية | TEN-003 (مُخفَّف؛ يُغلق شكلياً مع هذه البطاقة) |

## السياق

هوية الموظف وOrgUnit موجودة في HR.Core، لكن بلا مصفوفة SoR مكتوبة، بلا عقود بيانات، merge بلا unmerge/طابور تكرار، وبلا عتبات جودة أو lineage موثّق من المصدر إلى التقرير.

## القرار

| الموضوع | الاختيار |
|---|---|
| SoR موظف/هيكل | HR.Core |
| SoR أجر | Payroll (`EmployeePayProfile`) — الاسم عبر lookup |
| SoR حساب دليل | Auth.Directory (Username/Email/roles) |
| PositionTitle | نص مؤقت في HR.Core — كتالوج = MDM-001 |
| تكرار/دمج | `MasterDataDuplicateCase` + merge/unmerge في HR.Core |
| جودة | أبعاد + تقرير `gov003-data-quality-report.py` |
| Lineage | `docs/master-data/lineage.json` |
| خارج النطاق | MDM-001/002، MIG-004، ANA-001 BI كامل، FUN-006 |

## العواقب

- كل حقل رئيسي له كاتب واحد في المصفوفة.
- Publish/استهلاك الأحداث يحترم العقود؛ لا كتابة مضادة لـNationalId من المستهلكين.
- الدمج يُراجع عبر case؛ إلغاء الدمج ممكن بشروط آمنة.

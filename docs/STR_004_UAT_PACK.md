# STR-004 — حزمة UAT (ReadyForSign-off)

توقيعات: Product Architecture + Program.

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | ADR + blockers | PROD-001/ONB-001 Blocked؛ STR-002/TEN-003 Satisfied | |
| U2 | surfaces تغطي كل kinds | ≥8 صفوف | |
| U3 | agency-a و agency-b نفس digest | تعارض entitlement/branding | |
| U4 | Dual-agency walkthrough | خطوات مكتملة | |
| U5 | Config promotion schema | حقول pack إلزامية | |
| U6 | Upgrade stub → PROD-001 | Blocked موثّق | |
| U7 | Repo nofork rule | سياسة + validate | |
| U8 | تشغيل str004-nofork-validate | PASS | |
| U9 | لا ادعاء محرك PROD-001 | EXCEPTION | |
| U10 | Str004* unit | نجاح | |

**الحالة:** ReadyForSign-off — سياسة لا-fork؛ محرك السياسات = PROD-001 لاحقاً.

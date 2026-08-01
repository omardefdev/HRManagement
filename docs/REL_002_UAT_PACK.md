# REL-002 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | قراءة ADR + scope-matrix | PilotScope / EvidenceGates / GoNoGoDecision | |
| U2 | تحقق blockers enum | Blocked\|Waived\|Satisfied فقط | |
| U3 | تشغيل payroll-parallel-compare | PASS لـ 3 دورات اصطناعية | |
| U4 | فرق cycle-2 مفسّر | reasonCode موجود | |
| U5 | قوالب recon/SLO/defect موجودة | ملفات docs/REL_002_* | |
| U6 | Support + DR + migration stub | روابط OPS / MIG-006 | |
| U7 | Go/No-Go minutes | بنود التوقيع متعددة الأدوار + Pilot-only | |
| U8 | Rel002* unit | نجاح | |
| U9 | Readiness blockedOn columns | UAT/TRN/MIG/ONB/SRE/SUP ظاهرة | |
| U10 | لا ادعاء National ATO | خارج النطاق موثّق | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي متعدد السلطات ضمن **UAT-001** (يبقى Blocked على REL-002).

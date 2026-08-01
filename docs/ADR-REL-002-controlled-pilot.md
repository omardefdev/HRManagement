# ADR-REL-002 — Pilot مضبوط وGo/No-Go للـPilot فقط

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | REL-002 (البطاقة 57) |
| التاريخ | 2026-07-30 |
| الاعتماديات | REL-001 Done؛ وطني (UAT/TRN/MIG/ONB/SRE/SUP) = blockers موثّقة |

## السياق

Staging وsynthetic وrehearsal مغلقة تحت REL-001، ورحلات FUN مغلقة بحزم UAT ReadyForSign-off. لا يوجد charter لجهة Pilot واحدة، ولا قالب Go/No-Go منفصل عن الاعتماد الوطني، ولا harness لثلاث دورات رواتب متوازية، ولا سجل blockers للاعتماديات الوطنية المفتوحة.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة | `docs/rel-pilot/scope-matrix.json` — PilotScope / EvidenceGates / GoNoGoDecision |
| عمق الإغلاق | **Gate pack + evidence harness** — ليس ATO وطني |
| Pilot ≠ National | لا استخدام `11-NATIONAL-GO-NO-GO` كمعيار قبول REL-002 |
| Blockers | سجل إلزامي: Blocked \| Waived \| Satisfied |
| Parallel payroll | 3 دورات اصطناعية تحت `testdata/rel002/payroll-parallel/` |
| Migration | stub يعتمد MIG-006 عند Done |
| DR/Security | binders + خانة مقيّم مستقل أو waiver موقّع |
| إغلاق البطاقة | Done = الحزمة جاهزة + blockers موثّقة |
| خارج النطاق | NAT-007/008؛ 24×7 وطني؛ تنفيذ كامل TRN/SUP/SRE/MIG |

## سجل blockers الابتدائي

| معرّف | الحالة | ملاحظة |
|---|---|---|
| UAT-001 | Blocked | توقيع خارجي متعدد السلطات |
| TRN-001 | Blocked | تدريب ميداني / قبول ميداني |
| MIG-006 | Blocked | ثلاث rehearsals زمنية |
| ONB-001 | Blocked | مصنع تزويد موقّع |
| SRE-001 | Blocked | 24×7 ORR |
| SRE-002 | Blocked | national envelope |
| SRE-003 | Blocked | cyber recovery |
| SUP-002 | Blocked | L1–L3 desk كامل |
| SUP-003 | Blocked | competency |
| SUP-004 | Blocked | vendor escalation |
| REL-001 | Satisfied | staging parity Done |

## العواقب

- قرار Go للـPilot يتطلب استيفاء بوابات الأدلة أو Waivers موقّعة؛ لا يُمنح اعتماد وطني.
- أي Go مع blockers وطنية يبقى **Pilot-only** ويُذكر صراحة في المحضر.

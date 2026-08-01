# مسارات الاستثناء — REL-002

| مسار | السلوك |
|---|---|
| استخدام 11-NATIONAL كقبول REL-002 | مرفوض — Pilot only |
| Unexplained payroll parallel diff | `rel002-payroll-parallel-compare` يفشل |
| cycleCount != 3 | فشل harness |
| forbidLiveAgencyPayroll = false | فشل سياسة |
| Assessor slot فارغ بلا waiver | No-Go |
| Blocker status خارج Blocked\|Waived\|Satisfied | فشل schema Rel002* |
| ادعاء إغلاق UAT-001/TRN/MIG/ONB/SRE/SUP داخل REL-002 | مرفوض — تبقى Blocked أو Waived موقّع |
| تجاوز maxEmployees/users بلا استثناء معتمد | No-Go / Conditional |

راجع [ADR-REL-002](ADR-REL-002-controlled-pilot.md).

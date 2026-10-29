# مسارات الاستثناء — STR-005

| مسار | السلوك |
|---|---|
| قرار حرج بلا مالك أو مراجع | فشل validate |
| ownerRole == reviewerRole | فشل validate |
| استثناء بلا expiresOn / حالة معلنة | فشل validate |
| استثناء منتهٍ دون تجديد + مراجعة | فشل validate |
| Go-Live بمقاعد engineering فقط | فشل validate (eng-only fixture يجب أن يُرفض) |
| ادعاء إغلاق SYD-017 بأسماء وهمية في الحزمة | مرفوض — soft blocker يبقى Blocked |
| بناء جدول برنامج كامل داخل STR-005 | خارج النطاق — PGM-001 |
| استبدال سجل GOV-002 القانوني بهذا السجل | مرفوض — نطاقان منفصلان |

استثناءات البذرة في المصفوفة:

| exceptionId | expiresOn | status | الإغلاق |
|---|---|---|---|
| EX-STR005-SIM-LAW-INTERIM | 2026-10-30 | Closed | 2026-10-30، `architecture`، القرار `PG-EXC-2026-10-001` |
| EX-STR005-SIM-INCIDENT-WINDOW | 2026-09-15 | Closed | 2026-10-30، `architecture`، القرار `PG-EXC-2026-10-001` |

**الإغلاق لا التمديد (PLAN-34، D-QA-3):** الاستثناءان بذرتا محاكاة، والأول منتهٍ منذ 2026-09-15، فكان CI يسقط في خطوة
STR-005 قبل البناء، وكانت البوابة وHR.Core والرواتب تمتنع عن الإقلاع في Production/Staging (`EnforceOnStartup=true`).
أُغلقا بقرار مرقّم في `decision-log.json` (`PG-EXC-2026-10-001`) مع الحقول `closedOn`/`closedBy`/`closureDecisionId`/`closureNote`،
ولم يُمدَّد أي تاريخ. السيناريوهات باقية في `testdata/str005/scenarios/`.

**قواعد التغيير:**
- الاستثناء لا يتغير إلا بحالته: `Renewed` (بمراجع مختلف) أو `Closed`، وكلاهما بقرار في `decision-log.json`. تمديد `expiresOn` وحده ممنوع.
- كل استثناء `Declared` مسجّل التزاماً مؤرخاً في [`docs/qa/dated-obligations.json`](qa/dated-obligations.json)؛
  فخطوة CI «Dated obligations horizon (QA-034)» تُنذر قبل 30 يوماً وتُفشل قبل 7 أيام، أي قبل أن يمتنع الإقلاع.
- المصفوفة تُنسخ إلى مخرجات البناء (`HRManagement.ServiceDefaults.csproj`)، فتجديد استثناء على خادم يتطلب **إعادة بناء الصورة**،
  أو ضبط `ProgramGovernance:MatrixPath` ليشير إلى ملف مركّب (volume) يحمل المصفوفة المحدّثة.

راجع [ADR-STR-005](ADR-STR-005-program-governance-boards.md).

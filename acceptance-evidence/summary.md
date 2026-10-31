# Acceptance evidence summary (AC-01..AC-37)

Generated: 2026-10-30 13:09:10 UTC

| AC | Status | Contract | Missing |
|----|--------|----------|---------|
| AC-01 | Pass | complete | - |
| AC-02 | Pass | incomplete | steps.md, before.json, audit.json |
| AC-03 | Pass | incomplete | steps.md, before.json, after.json |
| AC-04 | Pass | incomplete | steps.md, before.json, after.json, audit.json |
| AC-05 | Pass | incomplete | steps.md, before.json, after.json |
| AC-11 | Pass | complete | - |
| AC-12 | Blocked (Payroll run could not be submitted for review, so the approver-post SoD check cannot run. — تعذّر إرسال مسيّر الرواتب للمراجعة، فلا يمكن تنفيذ فحص فصل المهام لمنع المعتمد من الترحيل) | complete | - |
| AC-16 | NoResult | incomplete | metadata.json, steps.md, before.json, after.json, audit.json, timing.json, result.json |
| AC-30 | NoResult | incomplete | metadata.json, steps.md, after.json, audit.json, db-checks.json, timing.json, result.json |
| AC-32 | Pass | complete | - |
| AC-33 | Pass | complete | - |
| AC-34 | Pass (Script contract verified; run pwsh scripts/acceptance-backup-restore.ps1 for live RTO) | incomplete | steps.md, before.json, after.json, audit.json |
| AC-36 | Pass (Run acceptance-pdf-arabic.ps1 for sample PDFs) | incomplete | steps.md, before.json, after.json, audit.json |
| AC-37 | NoResult | incomplete | steps.md, before.json, after.json, audit.json, db-checks.json, timing.json, result.json |
| AC-EST-10 | Pass | complete | - |
| AC-EST-11 | Pass | complete | - |
| AC-EST-12 | Pass | complete | - |
| AC-EST-24a | Pass | complete | - |
| AC-EST-24e | Blocked (No pending establishment change deep link in the work inbox. — لا يوجد تغيير ملاك معلق في صندوق العمل) | complete | - |
| AC-EST-24f | Pass | complete | - |
| AC-HCM-21 | Pass | complete | - |
| AC-HCM-22 | Pass | complete | - |
| AC-HCM-25 | Pass | complete | - |
| AC-HCM-26 | Pass | complete | - |
| AC-HCM-27 | Pass | complete | - |
| AC-HCM-28 | Pass | complete | - |
| AC-HCM-29 | Pass | complete | - |
| AC-HCM-30 | Pass | complete | - |
| AC-HCM-31 | Pass | complete | - |
| AC-HCM-32 | Pass | complete | - |
| AC-HCM-33 | Pass | complete | - |
| AC-QA-34 | Blocked (34g: Blocked — The base (CI) realm issues access tokens without realm roles (no 'roles' client scope), so /api/files returns 403 'missing role for this module' for every persona; the upload/scan half of 34g cannot run on this stack (realm finding for PLAN-41). — رمز الوصول في realm الأساسي بلا أدوار، فلا يمر طلب الرفع) | complete | - |
| AC-WF-23 | Pass | complete | - |

## Not yet executed
- AC-06
- AC-07
- AC-08
- AC-09
- AC-10
- AC-13
- AC-14
- AC-15
- AC-17
- AC-18
- AC-19
- AC-20
- AC-21
- AC-22
- AC-23
- AC-24
- AC-25
- AC-26
- AC-27
- AC-28
- AC-29
- AC-31
- AC-35

**Totals:** 27 Pass, 3 Deferred/Blocked, 0 Fail, 23 not run.
- **AC-QA-34** (34g): Pass (`qa34-34g-876a2368479d45e9bfbe807831c2b400`)
- **AC-QA-35** (35e): Pass (`qa35-35e-d6ec6afdf99b478d9c98026853a04115`)
- **AC-QA-35** (35b): Pass (`qa35-35b-735b5cf11d4f4663a559be192df65098`)
- **AC-QA-35** (35f): Pass (`qa35-35f-e6e1504e1ca44d7aab108951c291382e`)
- **AC-QA-35** (35d): Pass (`qa35-35d-7a3dcd449691465c8a69b94191597681`)
- **AC-QA-35** (35a): Pass (`qa35-35a-d1f80bceec5d413983b99e3224297909`)
- **AC-QA-35** (35g): Pass (`qa35-35g-c4fe1746f68d428b8810815f564a7640`)
- **AC-QA-35** (35c): Pass (`qa35-35c-2f2dc78a220a40b1a85cf59898538189`)

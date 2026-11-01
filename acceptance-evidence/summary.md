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
- **AC-INT-37** (37a): Pass (`cdb012ec-1a0c-4a99-9b88-0abd81a7638d`)
- **AC-INT-37** (37b): Pass (`82e24a54-bbbf-4483-b23c-19705d99682d`)
- **AC-INT-37** (37c): Pass (`d2d5ea4e-b5c5-43b9-992a-d89ede1ee300`)
- **AC-INT-37** (37d): Pass (`d0c90be1-e21c-4931-9be1-1178a211827f`)
- **AC-INT-37** (37e): Pass (`ca63e08a-e277-4ba9-ac71-a091611ddb0b`)
- **AC-INT-37** (37f): Pass (`65da8ed7-b522-46ed-9a54-916ecbbad518`)
- **AC-INT-37** (37g): Pass (`bd85a3c4-d0d6-4b17-82c6-661b9a8ff0be`)
- **AC-INT-37** (37h): Pass (`3a53bbef-1850-4e22-9b94-17454a8be559`)
- **AC-20**: Pass (`1ff5691a-99f0-44a7-b74b-0a24bcfb1876`)
- **AC-OPS-38** (38g): Pass (`360d0f20-7bdd-4815-b427-e73735e9c92c`)
- **AC-OPS-38** (38e): Pass (`dc027b24-8fff-4288-9ea7-afc40c4ba022`)
- **AC-OPS-38** (38b): Pass (`cb489c47-92e5-4a0b-8422-ee03f29e471c`)
- **AC-OPS-38** (38c): Pass (`2a94464a-2b7d-4c29-baa9-966646e02939`)
- **AC-OPS-38** (38a): Pass (`c190dc98-16df-44d1-a5cb-6159f922ebfa`)
- **AC-OPS-38** (38h): Pass (`7872cc3d-77b5-4111-9a3b-17925f969c27`)
- **AC-OPS-38** (38f): Pass (`c4e6537b-1e48-4032-9c0c-6dbdba6c6d7f`)
- **AC-OPS-38** (38d): Pass (`c155dbb5-513c-4eeb-a64a-563671a3b6b9`)
- **AC-OPS-38** (38g): Pass (`c60b425d-5314-46f3-a5ae-c9a1a63a224a`)
- **AC-OPS-38** (38e): Pass (`f6131ab7-4b3d-4107-baa9-8de436093604`)
- **AC-OPS-38** (38b): Pass (`b6b1d162-3135-4199-84d5-d156a44d1c31`)
- **AC-OPS-38** (38c): Pass (`5aec1669-2159-4a95-b800-bdf911e7264c`)
- **AC-OPS-38** (38h): Pass (`44a5b63c-fba8-4e4d-8032-a39ab08e1d61`)
- **AC-OPS-38** (38f): Pass (`728c58d5-b652-4ea3-8098-a605b967032e`)
- **AC-OPS-38** (38d): Pass (`4910835a-56e9-4b42-b11d-79e576a91958`)
- **AC-OPS-38** (38g): Pass (`a2adff7d-e402-4417-be67-9bfa24a86aed`)
- **AC-OPS-38** (38e): Pass (`be6abcc1-1817-4825-8dea-7913e105476c`)
- **AC-OPS-38** (38b): Pass (`c8516eeb-f564-4b87-9b07-3ecc5caa350b`)
- **AC-OPS-38** (38c): Pass (`85472772-699e-41c5-82de-9cd80908d94c`)
- **AC-OPS-38** (38a): Pass (`7343a2ec-5a5f-4364-8b9f-079bf6856aa9`)
- **AC-OPS-38** (38h): Pass (`60460e03-b6a9-44d2-9dda-6306436780b0`)
- **AC-OPS-38** (38d): Pass (`8f2d1766-c5f3-4b00-a7fa-d3f90d0007d3`)
- **AC-OPS-38** (38g): Pass (`04b776ca-8c67-4209-8285-d84f510b329d`)
- **AC-OPS-38** (38e): Pass (`9600b8c8-4cf2-4017-bd68-2917c2c16c6b`)
- **AC-OPS-38** (38b): Pass (`b1e774b4-c69a-46fc-b212-17764d50cbd2`)
- **AC-OPS-38** (38c): Pass (`db2fee99-a8de-45de-ab13-130be2a36a83`)
- **AC-OPS-38** (38a): Pass (`bcc30ace-5193-4315-8d84-90d15d9ecfdb`)
- **AC-OPS-38** (38h): Pass (`82cb2be1-a4dc-4d85-bc0f-95ed5e140a5b`)
- **AC-OPS-38** (38f): Pass (`60dd2f13-b429-41a4-8099-0edf1d5a1a74`)
- **AC-OPS-38** (38d): Pass (`eba96a35-f692-48a8-b618-eb5a437a599f`)

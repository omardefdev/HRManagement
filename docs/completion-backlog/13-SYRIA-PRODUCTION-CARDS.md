# بطاقات الإكمال السورية للإنتاج الوطني

هذه البطاقات **إضافة إلزامية** إلى البطاقات `001..NAT-008` وليست وصفاً استشارياً. إغلاقها يحتاج أدلة وتوقيعات؛ لا يكفي إنشاء شاشة أو وثيقة.

## SYR-001 — مستودع التشريع السوري وقواعد النفاذ

- **الحالة:** Done (legislation register with Law 50/2004, Law 53/2021 and key decrees; legal source binding protocol; docs, tests)
- **الأولوية/الحجم/المسؤول:** P0-National / XL / Legal Product Owner + Ministry Legal Teams
- **النطاق:** نسخ مصدقة من القانون 50/2004 وتعديلاته، القانون 53/2021 ضمن نطاقه، الأنظمة الخاصة، قوانين التأمين والمعاشات والضرائب وحماية البيانات والتوقيع والسجلات؛ الجريدة الرسمية؛ تعليمات التنفيذ؛ hierarchy وeffective/issue/publication dates؛ تعارض/إلغاء؛ ربط كل قاعدة وقرار بمادة ومصدر.
- **معايير القبول:** 100% من قواعد HR/payroll/leave/pension/tax لها `legal_source_id` ونسخة ومادة وتاريخ نفاذ ومالك قانوني؛ لا قاعدة من خبر صحفي فقط؛ تعارض النصوص يظهر ولا يُحسم برمجياً؛ إشعار تغيير قانوني ينتج impact report قبل النفاذ؛ نسخة الماضي لا تُعدل.
- **التحقق:** عينات مستقلة من تعيين وترفيع وإجازة وراتب وفصل ومعاش إلى النص؛ سيناريو تعديل رجعي وإلغاء وتداخل قانونين؛ hash وتوقيع الحزمة القانونية.
- **الاعتماديات:** STR-002, GOV-002, LEG-001.
- **ما أُغلق:**
  - [`SYR_001_LEGISLATION_REPOSITORY.md`](../SYR_001_LEGISLATION_REPOSITORY.md)
  - [`syr/legislation-register.json`](../syr/legislation-register.json) — 7 entries (Law 50/2004, Law 53/2021, Decrees 67/68/135/188, Law 12/2024)
  - [`syr/legal-source-binding.md`](../syr/legal-source-binding.md)
  - `scripts/syr001-validate.ps1`؛ `Syr001LegislationTests`

## SYR-002 — التعداد المرجعي للجهات والعاملين والمعاشات والأنظمة

- **الأولوية/الحجم/المسؤول:** P0-National / XL / Ministry of Administrative Development + Finance + Data Governance
- **النطاق:** تعداد مصدق للوزارات والهيئات والمؤسسات والشركات والوحدات الإدارية؛ 14 محافظة؛ العاملون حسب النوع والنظام والجهة؛ المتقاعدون/المستفيدون حسب المؤسسة؛ ملفات الرواتب الشهرية؛ المستخدمون والفروع؛ أحجام الوثائق؛ مصدر كل رقم وتاريخه ونسبة عدم اليقين.
- **معايير القبول:** reconcile بين المالية والتنمية الإدارية والتأمينات والمعاشات وكل جهة؛ تفسير فرق الأرقام الرسمية المتداولة؛ تحديد `current`, `historical`, `beneficiary`, `duplicate`; تحويل التعداد إلى workload وcapacity plan؛ اعتماد headroom لا يقل عن 50% للذروة أو قيمة أعلى يقررها المجلس.
- **التحقق:** control totals موقعة؛ إعادة عد مستقلة؛ مقارنة ثلاث عينات جهات ومحافظات مع مصادرها؛ تحديث آلي شهري يكشف الانحراف.
- **الاعتماديات:** STR-001, MDM-001, GOV-003.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/SYR_002_CENSUS.md](../SYR_002_CENSUS.md)
  - [`docs/syr/census-register.json`](../syr/census-register.json) — 14 governorates, agency types, employee classifications (law50/53/special), census categories (current/historical/beneficiary/duplicate), source authorities (MOF/MAD/GOSI/GOIP), reconciliation rules, 50% headroom policy
  - [scripts/syr002-validate.ps1](../../scripts/syr002-validate.ps1)
  - `Syr002CensusTests`

## SYR-003 — نموذج أنظمة الخدمة العامة والأنظمة الخاصة

- **الأولوية/الحجم/المسؤول:** P0-National / XL / HR Policy + Legal + Domain Architecture
- **النطاق:** `legal_regime_id` للعامل الخاضع للقانون 50 أو 53 أو نظام خاص/عقدي؛ الفئة/المرتبة/الوظيفة/الملاك؛ دائم/مؤقت/يومي/موسمي/جزئي/إنتاج؛ الانتداب والإعارة والنقل والندب؛ الترفيع؛ انتهاء الخدمة؛ تاريخ انتقال النظام وسبب القرار.
- **معايير القبول:** لا علاقة عمل بلا نظام ومصدر نافذ؛ لا جمع بين ميزتين متعارضتين؛ الانتقال لا يمحو التاريخ؛ قرارات المادة 159 والأنظمة الخاصة قابلة للتهيئة؛ كل حالة غير مصنفة تدخل exception queue وتمنع الراتب النهائي.
- **التحقق:** decision tables تغطي الأنماط المعلنة في المرسوم 67/2026؛ golden cases لكل نظام؛ اختبارات نقل بين جهتين ونظامين بأثر رجعي.
- **الاعتماديات:** SYR-001, HCM-001, EST-001, DOM-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/SYR_003_SERVICE_REGIMES.md](../SYR_003_SERVICE_REGIMES.md)
  - `src/Shared/HRManagement.SharedKernel/Legal/LegalRegime.cs` (`RegimeType.Law50/Law53/Special`, `EmploymentType`, `LegalRegimeId`, `TransitionDecisionRef`, `ConflictNotes`, `IsEffectiveOn()`)
  - [`docs/syr/service-regimes.json`](../syr/service-regimes.json) — 4 regimes (law50, law53, special_banking, special_judiciary), 7 employment types, transition rules, exception handling (blocks payroll)
  - [scripts/syr003-validate.ps1](../../scripts/syr003-validate.ps1)
  - `Syr003ServiceRegimeTests`

## SYR-004 — التحول إلى الليرة السورية الجديدة

- **الأولوية/الحجم/المسؤول:** P0-National / L / Payroll + Finance Architecture + Central Bank Liaison
- **النطاق:** `currency_code`, `currency_era`, conversion 100:1، قواعد التقريب لكل صك، dual display للفترات التاريخية، تحويل العقود/الديون/المجاميع، import/export، قسائم الرواتب، التقارير، حدود قواعد البيانات والواجهات.
- **معايير القبول:** كل مبلغ يحدد العملة والعصر؛ لا تحويل مزدوج؛ 1 ليرة جديدة = 100 قديمة دون فقد؛ الأرصدة قبل/بعد متصالحة؛ من 31-7-2026 الافتراضي الرسمي هو الجديدة مع بقاء التاريخ قابلاً للعرض؛ لا تستخدم float؛ سياسة التقريب مصدرها صك مصدق.
- **التحقق:** property tests وقيم حدية وكسور؛ إعادة تشغيل راتب قبل/بعد القطع؛ استيراد ملف قديم بلا وسم يجب أن يفشل بأمان؛ تسوية دفتر الأستاذ وقسائم نموذجية.
- **الاعتماديات:** PAY-001, DATA-001, IOP-001, SYR-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/SYR_004_CURRENCY.md](../SYR_004_CURRENCY.md)
  - `src/Shared/HRManagement.SharedKernel/Currency/SyrianCurrencyConverter.cs` (`ConversionRatio=100`, `ToNewLira`, `ToOldLira`, `GetEra`, `Normalize`, `RoundOfficial`, `CurrencyEra`)
  - [`docs/syr/currency-era.json`](../syr/currency-era.json) — conversion rules, rounding policy, reconciliation requirements
  - `scripts/syr004-validate.ps1` / `Syr004CurrencyTests`

## SYR-005 — حزم الأجر والتعويض والضريبة المؤرخة

- **الحالة:** Done (Decree 67/68 wage/compensation/tax rule pack structure; base/compensation/taxable/insurable separation; governance; docs, tests)
- **الأولوية/الحجم/المسؤول:** P0-National / XL / Ministry of Finance + Payroll Product Owner
- **النطاق:** جداول 67/2026 و68/2026 وملحقاتها؛ الزيادات النوعية؛ التعويضات الملغاة/المستمرة؛ البعيد وشبه البعيد؛ الحد الأدنى؛ علاوة الترفيع؛ شرائح ضريبة الدخل النافذة فقط؛ الحسميات؛ التمويل؛ قواعد التقريب؛ جهة/مسمى/نظام الاستحقاق.
- **معايير القبول:** rule pack موقع من المالية؛ فصل base wage عن compensation وtaxable/insurable bases؛ لا تطبيق للإصلاح الضريبي المقترح قبل نفاذه؛ منع ازدواج 67/68 والفئات المستثناة؛ simulation وdiff قبل التفعيل؛ four-eyes؛ rollback يوقف الدفع ولا يمحو نتائج سابقة.
- **التحقق:** 3 دورات ظل على بيانات مصدقة؛ golden payroll من المالية لكل فئة؛ حالات إدلب/شمال حلب والأنظمة الخاصة ضمن قائمة قرار رسمية؛ اختبارات retro/pay correction/termination.
- **الاعتماديات:** SYR-001, SYR-003, SYR-004, PAY-002, GOV-002.
- **أدلة الإغلاق:**
  - [SYR_005_WAGE_PACKAGES.md](../SYR_005_WAGE_PACKAGES.md)
  - [`docs/syr/decree-67-68-wage-tables.json`](../syr/decree-67-68-wage-tables.json) — wage tables, compensation rules, tax brackets, rounding, governance
  - `scripts/syr005-validate.ps1` / `Syr005WageTests`

## SYR-006 — التأمينات الاجتماعية والتأمين والمعاشات

- **الحالة:** Done (GOSI/GOIP jurisdiction map; contribution rates; beneficiary rules; reconciliation; docs, tests)
- **الأولوية/الحجم/المسؤول:** P0-National / XL / Social Insurance + Pensions + Finance
- **النطاق:** المؤسسة العامة للتأمينات الاجتماعية والمؤسسة العامة للتأمين والمعاشات كجهتين مستقلتين؛ الاشتراك والأجر الخاضع ومدد الخدمة والانقطاع وإصابات العمل/العجز/الوفاة؛ إحالة للتقاعد؛ المرسوم 135/2026؛ beneficiaries؛ دفع المعاش ومصالحة البنك/البريد؛ تحديث القانون الجاري.
- **معايير القبول:** خريطة اختصاص مكتوبة تمنع إرسال شخص للجهة الخاطئة؛ rates/ceilings من نص مصدق ومؤرخ؛ contribution/pension control totals متصالحة؛ لا duplicate beneficiary؛ كل رفض أو فرق يعود لحالة قابلة للمعالجة؛ تعديل القانون ينتج إصداراً جديداً لا patch يدوياً.
- **التحقق:** end-to-end من أول اشتراك إلى معاش؛ وفاة/عجز/خدمة متقطعة؛ replay ملف دفع؛ مصالحة شهرية مستقلة؛ مقارنة أرقام المؤسستين والتعداد.
- **الاعتماديات:** SYR-001, SYR-002, SYR-003, EXT-003, PAY-003.
- **أدلة الإغلاق:**
  - [SYR_006_INSURANCE_PENSIONS.md](../SYR_006_INSURANCE_PENSIONS.md)
  - [`docs/syr/insurance-jurisdiction-map.json`](../syr/insurance-jurisdiction-map.json) — GOSI/GOIP institutions, jurisdiction rules, Decree 135/2026 rates
  - `scripts/syr006-validate.ps1` / `Syr006InsuranceTests`

## SYR-007 — امتثال قانون حماية البيانات الشخصية 12/2024

- **الأولوية/الحجم/المسؤول:** P0-National / XL / DPO + Legal + Personal Data Protection Authority Liaison
- **النطاق:** تحديد المتحكم والمعالج؛ الأساس القانوني والغرض؛ البيانات الحساسة؛ ROPA؛ notices؛ حقوق العلم/الوصول/التصحيح/المحو/الاعتراض ضمن النص؛ DPIA؛ DPO؛ الترخيص/التصريح/الاعتمادية؛ الشكاوى؛ مشاركة/نقل البيانات؛ retention/deletion؛ breach workflow.
- **معايير القبول:** استلام القانون والتعليمات والنماذج المصدقة؛ رقم/قرار الترخيص أو تأكيد خطي بعدم لزومه؛ DPO مسجل؛ كل حقل وغرض ومستلم ومدة في ROPA؛ لا consent قسري عندما الأساس واجب عام؛ المهل مضبوطة من النص لا من GDPR؛ معالجة الشكاوى والإثبات end-to-end.
- **التحقق:** data discovery مقابل ROPA؛ طلب حقوق وشكوى وخرق افتراضي؛ محاولة export/subprocessor غير معتمد؛ مراجعة مستقلة من الهيئة/المستشار المعتمد.
- **الاعتماديات:** PRV-001, PRV-002, PRV-003, SYR-001.
- **الحالة:** Done
- **أدلة الإغلاق:**
  - [docs/SYR_007_DATA_PROTECTION.md](../SYR_007_DATA_PROTECTION.md)
  - [`docs/syr/law12-2024-compliance-matrix.json`](../syr/law12-2024-compliance-matrix.json) — roles, legal bases, data subject rights (7 rights with deadlines), 12 compliance controls, breach response
  - `scripts/syr007-validate.ps1` / `Syr007DataProtectionTests`

## SYR-008 — الاستضافة الوطنية والنطاق والشهادات السيادية

- **الأولوية/الحجم/المسؤول:** P0-National / XL / Ministry of Communications + NAITS + Platform Security
- **النطاق:** قرار الاستضافة في مركز معطيات/سحابة معتمدة؛ primary/DR؛ `.gov.sy`/`.sy` بحسب الاعتماد؛ DNS؛ egress؛ HSM/KMS؛ شهادات الخوادم والموظفين والبرمجيات؛ CA الوطنية/الحكومية؛ سجلات الدعم؛ منع الإدارة الخارجية غير المصرحة.
- **معايير القبول:** موافقة مكتوبة على topology ومواقع البيانات والنسخ؛ مفاتيح الجذر/البيانات بوصاية سورية وفصل مهام؛ لا سر إنتاج لدى المورد؛ certificate lifecycle وCRL/OCSP وtime source مجربة؛ failover لا ينقل البيانات خارج الموقع المعتمد؛ جميع endpoints رسمية وموقعة.
- **التحقق:** inspection من NAITS؛ key ceremony؛ egress audit؛ DNS/cert rollover/revocation؛ DR مع تحقق السيادة؛ محاولة نشر artifact غير موقع مرفوضة.
- **الاعتماديات:** SOV-002, SOV-003, SIG-001, HARD-001, BLD-001.
- **الحالة:** Done (hosting topology + certificate lifecycle + sovereign controls)
- **أدلة الإغلاق:**
  - [SYR_008_NATIONAL_HOSTING.md](../SYR_008_NATIONAL_HOSTING.md)
  - `docs/syr/hosting-topology.json` / `docs/syr/certificate-lifecycle.md`
  - `scripts/syr008-validate.ps1` / `Syr008HostingTests`

## SYR-009 — تكامل الأحوال المدنية والتحقق من الهوية

- **الحالة:** Done (civil identity match rules; Arabic name normalization; dedup/outage/audit policy; docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Civil Affairs + Identity Architecture
- **النطاق:** المعرّف المدني الرسمي وحقوله المسموحة؛ الاسم العربي وأسماء الوالدين وتاريخ/مكان الولادة والوقائع المدنية؛ barcode/document verification عند الإتاحة؛ matching/dedup؛ حالات عدم الاتصال؛ تصحيح المصدر؛ منع جعل HR بديلاً عن السجل المدني.
- **معايير القبول:** اتفاق مشاركة وغرض وحقول وSLA؛ لا تخزين زائد؛ confidence/reason لكل مطابقة؛ duplicate أو conflict لا يدمج آلياً؛ وفاة/تغيير اسم/تصحيح ينتج workflow مضبوطاً؛ outage لا ينشئ هوية دائمة بلا مراجعة؛ سجل من استعلم ولماذا.
- **التحقق:** contract test مع sandbox رسمي؛ أسماء عربية مركبة ومتقاربة؛ duplicate/false match؛ outage/replay؛ reconciliation يومي وسجل تدقيق.
- **الاعتماديات:** EXT-002, IAM-005, PRV-001, SYR-007.
- **أدلة الإغلاق:**
  - [SYR_009_CIVIL_REGISTRY.md](../SYR_009_CIVIL_REGISTRY.md)
  - [`docs/syr/civil-identity-match-rules.json`](../syr/civil-identity-match-rules.json) — match rules, confidence, dedup, civil events, HR boundary
  - `scripts/syr009-validate.ps1` / `Syr009CivilIdentityTests`

## SYR-010 — ناقل البيانات الحكومي والتكامل الانتقالي

- **الحالة:** Done (government data bus profile; transitional signed batch channel; connector SLOs; cutover plans; docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Government Integration Authority + Enterprise Architecture
- **النطاق:** profile للناقل الحكومي عند جاهزيته؛ mTLS/signatures؛ canonical IDs؛ API/event/batch؛ schema registry؛ ولحين اكتماله قناة ملفات موقعة ومشفرة مع manifest وsequence/checksum/ack/replay/reconciliation؛ منع التكاملات الخفية.
- **معايير القبول:** كل connector له authority وclassification وpurpose وcontract/version وSLO؛ تعطل 24 ساعة لا يفقد ولا يكرر؛ transition من batch إلى API لا يغير الدلالة؛ ادعاء `Live` يحتاج إثبات endpoint رسمي؛ لوحة freshness وbacklog وفرق المجاميع.
- **التحقق:** conformance مع الجهة؛ reorder/duplicate/late/missing/corrupt؛ cutover batch→API وrollback؛ key rollover؛ مصالحة مستقلة.
- **الاعتماديات:** EXT-005, INT-004, IOP-001, SYR-008.
- **أدلة الإغلاق:**
  - [SYR_010_DATA_BUS.md](../SYR_010_DATA_BUS.md)
  - [`docs/syr/data-bus-profile.json`](../syr/data-bus-profile.json) — connector profiles, transitional batch, cutover plans
  - `scripts/syr010-validate.ps1` / `Syr010DataBusTests`

## SYR-011 — أوامر الصرف والمصارف العامة والبريد السوري

- **الأولوية/الحجم/المسؤول:** P1-National / XL / Treasury + Central Bank + Public Banks + Syrian Post
- **النطاق:** اعتماد كشف الراتب/المعاش؛ رقم الأمر والدفعة؛ beneficiary/account/channel؛ ملفات مصرفية/بريدية؛ توقيع وتشفير؛ قبول/رفض/إرجاع؛ settlement date؛ partial failure؛ stop/reissue؛ control totals؛ فصل منشئ/مراجع/مطلق الدفع.
- **معايير القبول:** عقد رسمي لكل قناة؛ المجموع والعدد والعملة والعصر مطابقون من payroll إلى settlement؛ لا دفع مزدوج مع replay؛ الحالة النهائية لكل سطر؛ reject قابل للإصلاح دون إعادة الناجح؛ سجل موافقات غير قابل للتعديل؛ cutoff والعطل مؤرخة.
- **التحقق:** penny/zero/large/closed account؛ duplicate file؛ partial reject؛ انقطاع البنك/البريد؛ replay؛ تسوية مالية مستقلة لثلاث دورات.
- **الاعتماديات:** SYR-004, SYR-005, SYR-006, EXT-004, EVD-001.

## SYR-012 — توحيد سجلات المناطق والأنظمة الإرثية مع حفظ المصدر

- **الأولوية/الحجم/المسؤول:** P0-National / XL / Migration Authority + Agency Data Owners + Audit
- **النطاق:** مصادر ما قبل/بعد الانتقال؛ القانون 50/53 والأنظمة المحلية/الخاصة؛ إدلب وشمال حلب وغيرها حسب قرار رسمي؛ provenance على الحقل؛ duplicate identities؛ service continuity؛ أرصدة/قدم/إجازات/ديون؛ quarantine؛ appeals؛ read-only archive.
- **معايير القبول:** لا overwrite للمصدر؛ كل قيمة لها system/agency/extract/date/rule؛ conflict لا يحسم بلا مالك؛ المجاميع 100% accounted؛ كل شخص في accepted/quarantined/rejected مع سبب؛ أثر مالي قبل/بعد؛ بوابة خاصة للفئات التي ما زالت تعليماتها معلقة.
- **التحقق:** migration rehearsal مرتان؛ corrupted/duplicate/out-of-order؛ rollback كامل وdelta؛ عينات مشتركة من المالية وHR والتدقيق؛ شكوى موظف وإعادة بناء القرار.
- **الاعتماديات:** MIG-001..007, SYR-002, SYR-003, SYR-009.

## SYR-013 — تجربة الفروع والمحافظات واللغة والوصولية

- **الحالة:** Done (Arabic RTL-first branch UX; 14 governorates; WCAG 2.2 AA checklist; network profiles; docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Product + Accessibility + Governorate Representatives
- **النطاق:** العربية RTL أولاً؛ الأسماء والأرقام والتواريخ والليرة الجديدة؛ سياسة اللغة الكردية/الإنكليزية أو غيرها بقرار رسمي لا بافتراض؛ low bandwidth/latency/drop؛ أجهزة متواضعة؛ نقاط خدمة مساعدة؛ queue/resume؛ طباعة موحدة؛ WCAG 2.2 AA.
- **معايير القبول:** الرحلات الحرجة تعمل عند profile اتصال معتمد؛ لا double submit بعد الانقطاع؛ حفظ draft مشفر؛ لا بيانات حساسة في cache؛ keyboard/screen reader/contrast ناجحة؛ وثائق عربية صحيحة؛ ممثلون من 14 محافظة أو عينة تغطية مبررة يوقعون UAT.
- **التحقق:** مختبر اتصال ضعيف وقطع متكرر؛ أجهزة دنيا؛ قارئ شاشة ولوحة مفاتيح؛ طباعة وPDF؛ user research في حضر/ريف؛ usability للأدوار منخفضة الخبرة.
- **الاعتماديات:** PROD-004, MOB-001, L10N-001, UX-002, SYR-002.
- **أدلة الإغلاق:**
  - [SYR_013_BRANCH_UX.md](../SYR_013_BRANCH_UX.md)
  - [`docs/syr/accessibility-wcag-checklist.json`](../syr/accessibility-wcag-checklist.json) — WCAG 2.2 AA, governorates, network profiles
  - `scripts/syr013-validate.ps1` / `Syr013BranchUxTests`

## SYR-014 — التقويم السوري والدوام المستمر والورديات

- **الحالة:** Done (Decree 188/2025 holiday calendar policy; Asia/Damascus; 24/7 ops; shift/payroll rules; docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / L / HR Policy + Time & Attendance
- **النطاق:** المرسوم 188/2025؛ العطل ذات التاريخ الثابت والمعلن لاحقاً؛ الأعمال المستمرة؛ الورديات؛ أيام التعويض؛ العطل المحلية/الطارئة بقرار؛ timezone `Asia/Damascus`; effective-dated calendars لكل جهة/موقع.
- **معايير القبول:** لا قائمة عطل hard-coded؛ الإعلان المتأخر يطبق بأثر مضبوط؛ جهة 24/7 لا تُغلق؛ overtime/compensatory time من rule pack؛ إعادة حساب الأثر قبل اعتماد الراتب؛ مصدر كل يوم وتوقيعه.
- **التحقق:** رمضان/أعياد متحركة عند ورود البلاغ؛ وردية تعبر منتصف الليل؛ DST/time-source؛ holiday retroactive؛ مستشفى/خدمة مستمرة مقابل جهة إدارية.
- **الاعتماديات:** SYR-001, HCM-002, PAY-002, MDM-001.
- **أدلة الإغلاق:**
  - [SYR_014_CALENDAR.md](../SYR_014_CALENDAR.md)
  - [`docs/syr/holiday-calendar-policy.json`](../syr/holiday-calendar-policy.json) — fixed/variable holidays, continuous ops, compensatory time, payroll impact
  - `scripts/syr014-validate.ps1` / `Syr014CalendarTests`

## SYR-015 — مصفوفة السجلات والاحتفاظ والإفصاح السورية

- **الحالة:** Done (Syrian records retention matrix; legal hold; disclosure; disposal certificate; docs, tests)
- **الأولوية/الحجم/المسؤول:** P1-National / XL / Records Authority + Legal + DPO + Audit
- **النطاق:** ملف العامل، الملاك، الحضور، الراتب، الضريبة، التأمين، المعاش، القرار الإداري، التحقيق، الشكوى، التوقيع، السجل التقني؛ مدة وtrigger ونسخة رسمية ومالك؛ legal hold؛ redaction؛ نقل للأرشيف؛ إتلاف بشهادة؛ نسخة احتياطية وتحليلات.
- **معايير القبول:** جدول مصدق من كل سلطة مختصة؛ لا مدة مستوردة من معيار أجنبي؛ لا حذف تحت hold؛ الإفصاح يراعي القانون 12/2024؛ التوقيع والتحقق طويل الأجل؛ النسخ والتقارير لا تتجاوز الأصل؛ كل إتلاف له موافقة ودليل.
- **التحقق:** hold/appeal/disclosure/disposal؛ استرجاع سجل قديم؛ تدوير شهادة/مفتاح؛ حذف عبر DB/object store/search/analytics والنسخ وفق السياسة.
- **الاعتماديات:** LEG-001, PRV-001, EVD-002, SYR-001, SYR-007.
- **أدلة الإغلاق:**
  - [SYR_015_RECORDS_MATRIX.md](../SYR_015_RECORDS_MATRIX.md)
  - [`docs/syr/records-retention-matrix.json`](../syr/records-retention-matrix.json) — 12 record classes, legal hold, Law 12/2024 disclosure, disposal certificate
  - `scripts/syr015-validate.ps1` / `Syr015RecordsTests`

## SYR-016 — المطابقة السيبرانية السورية والربط التشغيلي الوطني

- **الحالة:** Done (Law 20/2022 + NAITS crosswalk; 12 controls; incident clock; re-accreditation; docs, tests)
- **الأولوية/الحجم/المسؤول:** P0-National / XL / CISO + National Cybersecurity/NAITS Liaison
- **النطاق:** قانون الجرائم المعلوماتية 20/2022؛ السياسة الوطنية لأمن المعلومات؛ استراتيجية الأمن السيبراني؛ اللائحة 20 `NAITS/ET`; تصنيف الأصول والبيانات؛ hardening؛ logging/SIEM/SOC؛ إدارة الحادث؛ DLP؛ تشفير؛ فحص واعتماد؛ تغيير التشريع الجاري.
- **معايير القبول:** crosswalk مادة/ضابط/تنفيذ/دليل/مالك؛ نسخة مصدقة من اللائحة والسياسات؛ zero Critical gap؛ telemetry ضمن السيادة؛ playbooks تحدد جهات الاتصال السورية؛ incident clock موحد مع الخصوصية؛ إعادة الاعتماد عند تعديل القانون أو السياسة.
- **التحقق:** تقييم مستقل؛ ransomware/insider/data exfiltration؛ SOC handoff؛ DLP bypass؛ اختبارات اختراق وإعادة اختبار؛ evidence bundle موقع.
- **الاعتماديات:** SOV-001, HARD-001, EVD-002, PRV-003, SYR-008.
- **أدلة الإغلاق:**
  - [SYR_016_CYBERSECURITY.md](../SYR_016_CYBERSECURITY.md)
  - [`docs/syr/cyber-control-crosswalk.json`](../syr/cyber-control-crosswalk.json) — Law 20/2022, NISP, NCS, NAITS/ET; 12 controls; zero Critical gap
  - `scripts/syr016-validate.ps1` / `Syr016CyberTests`

## SYR-017 — شهادة الرواتب والإطلاق المرحلي السوري

- **الحالة:** Done (launch harness + phased wave plan)
- **الأولوية/الحجم/المسؤول:** National Gate / XL / Syrian National Authorization Board
- **النطاق:** ثلاث دورات ظل متتالية لكل نظام/قناة؛ baseline مؤقت 1.25M راتب خلال 4 ساعات وstress 2M إلى أن يعتمد `SYR-002` الأعلى؛ 14 محافظة؛ موجات pilot→جهة→قطاع→وطني؛ freeze/rollback؛ غرفة عمليات؛ توقيعات الملف 14؛ مراقبة 30 يوماً لكل موجة.
- **معايير القبول:** إغلاق `SYR-001..016` وكل `NAT-001..007`; صفر فرق مالي غير مفسر وصفر ازدواج/فقد؛ No-Go لكل تعليمات أو جهة غير مصدقة؛ wave scope وblast radius وrollback واضح؛ لا توسع تلقائي؛ `NAT-008` يذكر release digest والجهات والأنظمة وتاريخ انتهاء الاعتماد.
- **التحقق:** full-scale payroll + bank/post reconciliation؛ 72h load وDR/cyber recovery؛ فشل بنك/هوية/ناقل/محافظة؛ rollback موجة؛ independent audit يعيد بناء 100 عينة من النص إلى الدفع.
- **الاعتماديات:** SYR-001..016, NAT-001..007, VEN-001.
- **ما أُغلق:**
  - [SYR_017_LAUNCH_CERT.md](../SYR_017_LAUNCH_CERT.md) + [`syr/phased-launch-plan.json`](../syr/phased-launch-plan.json)
  - `scripts/syr017-validate.ps1`؛ `Syr017LaunchTests`
- **Blockers المتبقية:** SYR-015/SYR-016 = NotStarted؛ live-shadow-cycles = Blocked؛ NAT-008 live board decision = Blocked

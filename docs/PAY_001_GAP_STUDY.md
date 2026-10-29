# دراسة محرك الرواتب من المدخلات إلى الدفع (PAY) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29  
**النطاق:** خدمة Payroll كاملة: الإعداد (سلالم الرواتب، الدرجات، أنواع البدلات والخصومات، ملف أجر الموظف، الإطار القانوني `PayrollConfiguration` وربط النظام القانوني)؛ المدخلات من الخدمات الأخرى (العقود، الحضور، الإجازة بدون أجر، الحركات، التأديب، السلف، نهاية الخدمة) وطريقة وصولها ونقطة القطع والتغييرات المتأخرة والأثر الرجعي؛ التشغيل (الإنشاء، الحساب، الحتمية، الاستئناف، نوع المال والتقريب، المراجعة والاعتماد والترحيل والتجميد)؛ ما بعد التشغيل (الحجز، الاحتيال، المتأخرات، دفعات الصرف وملف المصرف، القسائم والخدمة الذاتية، التقارير، الضريبة والتأمينات، نهاية الخدمة، الخزينة/الأستاذ العام)؛ سلوك إعدادات الإنتاج؛ واختبارات التكامل الحمراء على `main`. داخليات الإجازة والحضور ورحلة الانفصال خارج النطاق، إلا نقطة تسليمها إلى الرواتب.  
**المنهج:** قراءة ثابتة للشيفرة والاختبارات والسكربتات والوثائق والأدلة في لقطة `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق.  
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK). كل فجوة مدعومة بمرجع ملف/سطر. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.  
**الوثائق المقارَنة:** [PAY_001_APPROVAL_BYPASS.md](PAY_001_APPROVAL_BYPASS.md) · [PAY_004_ARREARS.md](PAY_004_ARREARS.md) · [NAT_003_PAYROLL_CERT.md](NAT_003_PAYROLL_CERT.md) · [PAYROLL_BATCH_PERFORMANCE.md](PAYROLL_BATCH_PERFORMANCE.md) · [PAYROLL_RUN_RESUME.md](PAYROLL_RUN_RESUME.md) · [FRD_001_FRAUD.md](FRD_001_FRAUD.md) · [PLAN-06](acceptance-plans/PLAN-06-payroll-determinism-resume.md) · [PLAN-10](acceptance-plans/PLAN-10-payroll-money.md) · [PILOT_DEPLOYMENT.md](PILOT_DEPLOYMENT.md) · بطاقات PAY-001 ([01](completion-backlog/01-P0-RELEASE-BLOCKERS.md))، PAY-002/003 ([02](completion-backlog/02-P1-PLATFORM-INTEGRITY.md))، PAY-004 ([15](completion-backlog/15-SUPPLEMENTAL-GAP-CARDS.md))، SYR-004/005/006/011/017 ([13](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md))، SYD-003..005/014 ([14](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md)) · [acceptance-evidence/summary.md](../acceptance-evidence/summary.md)

---

## 1. الخلاصة

1. **البنية مبنية، لكن الدورة لا تعمل من أولها إلى آخرها.** توجد كل الكيانات: إطار قانوني مؤرّخ ببصمة، job بأقسام ولقطة مدخلات وبصمة، فصل منشئ/معتمد/مُرحِّل، دفعات وأوامر دفع، حجز واحتيال، متأخرات، نهاية خدمة. لكن عبارة «Done» في PAY-001 وPAY-003 وPAY-004 وFRD-001 وSYR-005/006 لا تطابق الشيفرة. لم تكتمل دورة رواتب واحدة (حساب ← اعتماد ← ترحيل) في أي دليل، وهي مؤشر نجاح الإطلاق التجريبي.
2. **خمسة عوائق P0:**
   - **(أ) G-PAY-01:** لا توجد أي طريقة لإنشاء إطار قانوني للرواتب في الإنتاج. البذرة تعمل في `Demo`/`Acceptance` فقط، فيرفض الحساب كل تشغيل.
   - **(ب) G-PAY-02:** لا توجد أي طريقة لتسجيل حساب الموظف المصرفي (IBAN) أو التحقق منه، فملف الخزينة يرفض كل تشغيل. وكل تغيير في ملف الأجر يمحو الحساب والوحدة والنظام.
   - **(ج) G-PAY-03:** أي تسوية غير صفرية (فرق نقل، انتداب، مهمة، علاوة تدريب، تعويض طبي، تسوية يدوية) تُفشل تحقق الدورة كلها. والترحيل يعيد حساب الصافي فيمحو التسويات.
   - **(د) G-PAY-04:** الرفض طريق مسدود. التشغيل المرفوض يبقى مجمداً فلا يُعاد حسابه، ولا يمكن إلغاء تشغيل اكتمل حسابه، فتُقفل الفترة.
   - **(هـ) G-PAY-05:** AC-12 «Blocked» لأن الاختبار يرسل للمراجعة دون حساب وتحقق. ولو صُحّح لفشل في خطوة أخرى (403 لأن المعتمد لا يحمل دور المُرحِّل). ونحو 14 اختبار تكامل للرواتب حمراء على `main` لأن مضيف الاختبار لا يسجّل عميل أنظمة الخدمة في HR.Core (استنتاج ثابت، يحتاج تحقق).
3. **ثغرات P1 في المال والحوكمة:**
   - صرف جزئي يقدّمه المعالج على تشغيل «معتمد» قبل الترحيل وخارج بوابة الاحتيال. وملف الخزينة يُصدَّر من الأسطر متجاهلاً الحجز.
   - «أربع عيون» شكلية: المعتمد الثاني GUID يكتبه الأول.
   - التحقق يعيد أي حالة، حتى `Posted`، إلى `Calculated`.
   - الإلغاء يُضيّع الخصومات والتسويات المطبّقة. والتغييرات المتأخرة والرجعية تسقط.
   - موظف بلا أيام استحقاق يُصرف له راتب كامل.
   - شرائح الضريبة المزروعة تخالف حزمة SYR-005 في المستودع نفسه.
   - 16 إجراء POST بلا سياسة، منها تعديل الراتب الأساسي ونسب الخصومات النظامية.
   - الدفعة تقف عند «مُرسلة» إلى الأبد، والمتأخرات لا تُسدَّد.
   - مكافأة نهاية الخدمة تُحسب من آخر تغيير راتب، والشهر الأخير لا يُدفع.
   - موظف واحد محجوز يوقف رواتب الجهة كلها.
4. **فجوات قدرات (P2):** محرك فروق رجعية، والعلاوة الدورية، وربط إغلاق الحضور والإضافي والمناوبات بالرواتب، والأستاذ العام والموازنة وتقرير الضريبة واشتراكات صاحب العمل، والمهام الخلفية (استعادة المهام العالقة، شهادة الحياة، الإشعارات)، واستبعاد موظف من الدفعة.
5. **فجوة تحقق:** AC-21..25 لم تُشغَّل قط. واختبارا AC-24 وAC-25 يُثبّتان الثغرات بدل كشفها: الصرف الجزئي قبل الترحيل، والإفراج بـ GUID مكتوب. وسكربتات القبول تلغي التشغيلات وتُدخل سجل مراجعة مغلقاً عبر SQL مباشر. ونموذج المتأخرات الذي توثّقه PAY-004 نسخة غير مستخدمة في `SharedKernel`.

**التقدير الإجمالي:** نحو 78–97 يوم عمل لمطوّر واحد، منها 26–33 يوماً للموجة P0 (PAY-A..PAY-D). القيم القانونية النهائية (D-PAY-1) خارج التقدير وتعتمد على وزارة المالية.

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | الإطار القانوني (`PayrollConfiguration`) | نسخ مؤرّخة ببصمة SHA-256، تحرير المنشور يستنسخ مسودة، اعتماد وتصديق ونشر | ❌ لا إنشاء ولا شرائح ولا تاريخ سريان من الواجهة، ولا بذرة في الإنتاج (G-PAY-01)؛ المعتمد نفسه يعتمد ويصادق وينشر (G-PAY-09) |
| 2 | السلالم والدرجات وأنواع البدلات/الخصومات | CRUD في `SalaryScales`/`PayGrades`/`CompensationTypes`/`DeductionTypes` | ⚠️ بلا سياسة، والنسب النظامية خارج البصمة (G-PAY-10) |
| 3 | ملف أجر الموظف | `UpsertEmployeePayProfile` (API بسياسة ونطاق؛ MVC بلا سياسة)، تاريخ تغييرات | ❌ لا IBAN، ويُمحى الحساب والوحدة والنظام عند كل نسخة (G-PAY-02) |
| 4 | العقود (`IContractActivated`) | درجة من السلم النافذ، راتب العرض، حجز مراجعة عند غياب الراتب أو تجاوز الحدود | ✅ مع ثغرة منتصف الشهر (G-PAY-11) وحجز يوقف الدفعة كلها (G-PAY-15) |
| 5 | الحضور (`IAttendancePeriodClosed`) | `AbsenceDeduction` بأيام الغياب | ⚠️ أول إغلاق فقط، ولا شرط إغلاق قبل الحساب (G-PAY-07، G-PAY-19) |
| 6 | الإجازة بدون أجر | `AbsenceDeduction` واحد بمرجع الطلب وفترة الإجازة | ⚠️ قاسم 22 ثابت (G-PAY-11)؛ الإلغاء بعد التطبيق والاعتماد المتأخر يسقطان (G-PAY-07) |
| 7 | الحركات | طرف saga + حدث نهائي للنقل والانتداب | ❌ تطبيق مزدوج، تعويض فارغ، الترفيع لا يصل (G-PAY-13) |
| 8 | التأديب والسلف | `DeductionRecord` بأقساط مؤرّخة ومرجع مصدر | ✅ الجدولة؛ ⚠️ الخصم الزائد عن الراتب يُشطب صامتاً (G-PAY-11) |
| 9 | الإنشاء والحساب | فهرس «تشغيل حي واحد للفترة»، job بلقطة وأقسام وعمّال متوازين، حفظ ذري لكل موظف | ✅ بنيوياً؛ ⚠️ داخل طلب HTTP بلا مهمة خلفية (G-PAY-21)؛ حاجز نظام الخدمة يوقف الكل (G-PAY-15) |
| 10 | التحقق والتجميد | `ValidateLineIntegrity` ثم `MarkValidated` | ❌ يفشل مع أي تسوية (G-PAY-03)؛ يعيد أي حالة إلى «محسوب» (G-PAY-06) |
| 11 | الإرسال والاعتماد والرفض | منشئ ≠ معتمد، `PayrollRun` بـ concurrency token | ❌ الرفض مسدود (G-PAY-04)؛ الاعتماد المزدوج فوق العتبة غير مطبّق (G-PAY-09) |
| 12 | سجل المراجعة والترحيل | سجل مراجعة مغلق + مُرحِّل ≠ معتمد ≠ منشئ | ⚠️ يلزمه معتمدان والمدقق لا يصل (G-PAY-05، G-PAY-09)؛ الترحيل يعدّل الأسطر المجمدة (G-PAY-03) |
| 13 | الصرف | `ProcessPaymentBatch` ← فحص احتيال ← بوابة ← أوامر دفع؛ ملف خزينة من التقارير | ❌ لا IBAN (G-PAY-02)؛ صرف جزئي قبل الترحيل وملف خارج البوابة (G-PAY-08) |
| 14 | الإشعار والتسوية والمطابقة والمتأخرات | أوامر `Ack/Settle/Reject`، مطابقة بنكية، دفتر متأخرات | ❌ لا مستدعي، مطابقة ذاتية، المتأخرات لا تُسدَّد (G-PAY-16) |
| 15 | العكس والتصحيح | عكس تعويضي، تشغيل تصحيح من التسويات | ⚠️ عكس بلا اعتماد، دفعة عكس لا تُعالج (G-PAY-06)؛ التصحيح يتجاوز التحقق (G-PAY-03) |
| 16 | الاحتيال والحجز | 8 قواعد + شهادة حياة، حجز يمنع الدفعة | ⚠️ مدخلات شكلية (G-PAY-15)؛ أربع عيون شكلية (G-PAY-09) |
| 17 | القسائم والخدمة الذاتية | PDF عربي، ESS للقسائم والمتأخرات | ⚠️ تعرض غير المعتمد، والـ PDF ممنوع على الموظف (G-PAY-17) |
| 18 | الضريبة والتأمينات | `TaxRule` من الإطار، خصومات نظامية من `DeductionTypes` | ❌ قيم مؤقتة تخالف حزم SYR-005/006، لا سقف ولا حصة صاحب عمل (G-PAY-12) |
| 19 | نهاية الخدمة | طرف saga الانفصال ← تسوية مسودة ← اعتماد ← «مدفوع» | ⚠️ سنوات خدمة خاطئة، شهر أخير غير مدفوع، صرف بلا أداة (G-PAY-14) |
| 20 | الأستاذ العام والخزينة والتقارير | `IPayrollProcessed` إلى Dev.Analytics فقط؛ ملفات CSV | ❌ (G-PAY-20) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **المال `decimal`** في كل الكيانات، بدقة `numeric(18,2)` موحدة ([PayrollDbContext.cs:61-67](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)). والتقريب من الإطار (`RoundingMode`، `DecimalPlaces`). لا `double` في الحساب.
- **إطار قانوني غير قابل للتعديل بعد النشر (PAY-002):** بصمة محتوى، وتثبيت النسخة والبصمة على التشغيل. تحرير المنشور يستنسخ مسودة، والتصحيح يرث التثبيت ([PayrollConfigurationVersioning.cs](../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationVersioning.cs)، [CorrectionRunHandlers.cs:28-35](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
- **محرك حساب بقواعد مرتبة** (`PayrollEnginePipeline`)، وتعليم ما استُهلك فعلاً فقط. أقساط السلفة تُسترد عند استحقاقها، والقسط الأخير يمتص فرق التقريب ([InboundEventHandlers.cs:452-500](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- **تشغيل قابل للاستئناف (PAY-003/PERF-001):**
  - job، أقسام، لقطة مدخلات، وبصمة لكل موظف تكشف تغيّر المدخلات أثناء الحساب.
  - فهرس فريد `(PayrollRunId, EmployeeId)` يمنع تكرار السطر.
  - حفظ ذري لكل موظف (مع مكوّناته وتعليم المدخلات)، ورفض البدء المزدوج مع lease ساري ([PayrollRunJobHandlers.cs:41-57](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
- **فصل الواجبات في المعالج (لا في الواجهة فقط):** منشئ ≠ معتمد ([PayrollRunHandlers.cs:119-132](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)). مُرحِّل ≠ معتمد وغير المنشئ ([:194-209](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)). وسياسات أدوار منفصلة للمعالجة والاعتماد والترحيل والعكس.
- **سلامة البيانات:**
  - تشغيل حي واحد لكل جهة/فترة/نوع ([PayrollDbContext.cs:97-101](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)).
  - `ConfigureOptimisticConcurrency` على التشغيل والدفعة والأمر وملف الأجر.
  - ملف أجر فعّال واحد لكل موظف.
  - مفتاح idempotency لكل أمر دفع.
  - Outbox بـ PostgreSQL، وinbox موحّد.
- **الإجازة بدون أجر:** حامل خصم واحد بمرجع الطلب وفهرس فريد، بفترة الإجازة لا تاريخ الاعتماد.
- **تفعيل العقد (PAY-029):** الدرجة من السلم النافذ عند بداية العقد، وحجز مراجعة عند غياب الراتب أو خروجه عن حدود الدرجة.
- **ضوابط ملف الخزينة:** يرفض الحسابات غير المحققة ولا يحمل الرقم الوطني ([PayrollExternalExportService.cs:64-110](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)). والتسوية تتطلب مرجعاً من القناة ([PayrollRunJobHandlers.cs:465-466](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
- **لا اعتماد على `IsLabScale` ولا على وضع محرك السياسات** في Payroll. لا يوجد هنا توأم لنمط «التطبيق عند التقديم» في `EstablishmentChange`. حاجز STR-002 القانوني يعمل قبل الحساب، والكتالوج الحالي يسمح به.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-PAY-01 — لا يمكن إنشاء إطار قانوني للرواتب في الإنتاج، فلا يُحسب أي تشغيل
- **الدليل:**
  - `PayrollConfigurationsController` ليس فيه إجراء `Create`. يحوي فقط `Edit` لنسخة موجودة و`Approve`/`LegalAttest`/`Publish`/`Activate` ([PayrollConfigurationsController.cs:51-234](../src/Services/Payroll/Payroll.Web/Controllers/PayrollConfigurationsController.cs)).
  - `ApplyFields` في `Edit` لا يلمس `TaxBrackets` ولا `EffectiveFrom` ([:71-92](../src/Services/Payroll/Payroll.Web/Controllers/PayrollConfigurationsController.cs)). والنسخة المستنسخة ترث `EffectiveFrom` الأصل ([PayrollConfigurationVersioning.cs:47](../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationVersioning.cs)). فلا يمكن إدخال شرائح قانون ضريبة جديد من الواجهة. وكل نسخة جديدة تسري من 2000-01-01 (البذرة)، وتُنهي سابقتها بأثر رجعي ([:121-129](../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationVersioning.cs)).
  - البذرة الوحيدة للإطار وسجل القرارات القانونية تعمل في ملف `Demo` أو `Acceptance` فقط ([Program.cs:130-151, 179-208](../src/Services/Payroll/Payroll.Web/Program.cs)). والملف الافتراضي خارج Development هو `None` ([SeedProfile.cs:35](../src/Shared/HRManagement.ServiceDefaults/Seeding/SeedProfile.cs)). ولا يُدخل أي ترحيل EF بيانات.
  - الحساب يرفض بلا نسخة منشورة: «لا توجد نسخة إعدادات معتمدة/منشورة للفترة» ([PayrollRunJobHandlers.cs:180-184](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
  - `deploy-organization.ps1` يضبط `HR_LEGAL_REGIME` ولا يقرؤه أي رمز، ثم يطلب «تفعيل الإطار» ([deploy-organization.ps1:11, 21](../scripts/deploy-organization.ps1)). وكذلك قواعد أولوية الصرف الجزئي (`DisbursementPriorityRule`) لا تُنشأ إلا بالبذرة ([AcceptancePayrollSeeder.cs:303-330](../src/Services/Payroll/Payroll.Infrastructure/Data/AcceptancePayrollSeeder.cs)).
- **الأثر:** في جهة حقيقية بإعدادات الإنتاج لا يمكن حساب أي راتب. الخطوة 3 في [PILOT_DEPLOYMENT.md](PILOT_DEPLOYMENT.md) («تفعيل الإطار القانوني») لا تجد ما تفعّله. والالتفاف الوحيد تشغيل الإنتاج بملف `Demo`، وهو يزرع موظفين وتشغيلات وهمية.
- **الإصلاح المقترح:**
  - إنشاء نسخة إطار من قالب نظام قانوني، مع شرائح وتاريخ سريان وقيم قابلة للتحرير في المسودة فقط.
  - استيراد حزمة قانونية موقعة (JSON بمرجع لكل قيمة، D-PAY-1) بدل البذرة.
  - إدارة قواعد أولوية الصرف ضمن الإطار.
  - إزالة `HR_LEGAL_REGIME` أو ربطه بالاستيراد.
- **الخطة:** [PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md)
- **التحقق المطلوب:**
  - **AC-PAY-Aa (موجب):** على `Seed:Profile=None` ينشئ معالج مسودة LAW_50 بشرائح وتاريخ سريان 2027-01-01. يعتمدها مستخدم آخر وتُنشر، ثم يُحسب تشغيل 2027-01.
  - **سالب:** تعديل المنشورة يُنشئ مسودة ولا يغيّرها. تشغيل لفترة قبل السريان يُرفض. نسخة جديدة لا تقصّر سابقتها إلى ما قبل تاريخها.

#### G-PAY-02 — لا يمكن تسجيل حساب الموظف المصرفي، وكل نسخة جديدة من ملف الأجر تمحو الحساب والوحدة والنظام
- **الدليل:**
  - `EmployeePayProfile` يحمل `BankAccountIban` و`BankAccountVerifiedAt` ([EmployeePayProfile.cs:43-59](../src/Services/Payroll/Payroll.Domain/Entities/EmployeePayProfile.cs)). لكن لا سطر في الخدمات يُسندهما: لا واجهة، ولا API، ولا حدث، ولا حقل في أي `cshtml`. وبذرة القبول تضع IBAN على أوامر الدفع لا على الملف.
  - تصدير ملف الخزينة يرمي استثناء إذا وُجد موظف واحد بلا حساب محقق ([PayrollExternalExportService.cs:105-110](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)). وأوامر الدفع تُنشأ بـ `BeneficiaryIban = null` ([PaymentOrderService.cs:22-29, 52](../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderService.cs)).
  - `UpsertEmployeePayProfileHandler` يُنشئ نسخة بالراتب والدرجة والتاريخ فقط ([EmployeePayProfileHandlers.cs:38-46](../src/Services/Payroll/Payroll.Application/Features/EmployeePayProfiles/EmployeePayProfileHandlers.cs)). و`HandleContractActivatedHandler` يفعل مثله ([InboundEventHandlers.cs:53-61, 72-79](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). فتسقط `OrgUnitId` و`RegimeCode` و`BankAccountIban` و`BankName` و`BankAccountVerifiedAt`.
  - `IEmployeeCreated` يُنشئ ملفاً غير فعّال يحمل الوحدة والنظام ([:640-648](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). ثم أول تفعيل عقد يُنشئ ملفاً فعّالاً بدونهما، لأن `GetActiveProfileAsync` لا يرى غير الفعّال.
  - قاعدة الاحتيال FR-04 (IBAN مشترك) لا تعمل، لأن الحقل فارغ دائماً.
- **الأثر:**
  - لا يمكن إنتاج ملف صرف لأي تشغيل، فلا يصل راتب عبر النظام.
  - فحوص نطاق الوحدة على ملف الأجر والمتأخرات تقرأ `OrgUnitId` فارغاً ([PayrollApiController.cs:152-165](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)، [ArrearsController.cs:129-133](../src/Services/Payroll/Payroll.Web/Controllers/ArrearsController.cs)).
- **الإصلاح المقترح:**
  - أمر تسجيل/تغيير الحساب: صيغة IBAN سورية، صانع ومدقق مختلفان، سبب، تدقيق، وتاريخ تحقق بمرجع من المصرف.
  - نسخة ملف جديدة تنقل كل الحقول غير المالية.
  - تثبيت الحساب في لقطة التشغيل.
- **الخطة:** [PLAN-55](acceptance-plans/PLAN-55-pay-bank-account-pay-profile.md)
- **التحقق المطلوب:**
  - **AC-PAY-Ba (موجب):** معالج يسجّل IBAN، ومعتمد آخر يتحقق منه. ثم يصل `IContractActivated` براتب جديد، فيبقى الحساب والوحدة والنظام في الملف الفعّال، ويُنتَج ملف الخزينة بعدد الأسطر ومجموعها.
  - **سالب:** المسجّل لا يحقق حسابه. IBAN بصيغة خاطئة يُرفض. تغيير الحساب بعد التجميد لا يغيّر ملف التشغيل المجمد.

#### G-PAY-03 — أي تسوية غير صفرية تُفشل تحقق الدورة كلها، والترحيل يمحو التسويات من الصافي
- **الدليل:**
  - `RoundingRule` يحسب الصافي هكذا: الإجمالي − الخصومات + مجموع التسويات ([PayrollRules.cs:305-308](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)). و`AdjustmentRule` لا يضيف التسوية إلى `GrossPay` ([:231-249](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
  - `ValidateLineIntegrity` يرفض السطر إن لم يساوِ صافيه `Gross − Deductions` أو `max(0, …)` ([PayrollRunReconciliation.cs:22-25](../src/Services/Payroll/Payroll.Domain/Services/PayrollRunReconciliation.cs)). فأي سطر بتسوية X ≠ 0 يفشل بفرق X. ويفشل الـ job كله ويعود التشغيل إلى `Calculating` ([PayrollRunParallelCalculator.cs:372-382](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)).
  - مصادر التسويات غير الصفرية آلية:
    - فرق النقل ([InboundEventHandlers.cs:292-298](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
    - بدل الانتداب ([:524](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) وبدل المهمة ([:556](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
    - علاوة التدريب ([:670](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) والتعويض الطبي ([:711](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
    - التسويات اليدوية ([PayrollExtendedControllers.cs:166-205](../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)).
  - `PostPayrollRunHandler` يعيد ضبط صافي كل سطر إلى `Gross − Deductions` قبل الترحيل ([PayrollRunHandlers.cs:238-241](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) ← [PayrollCalculation.cs:8-11](../src/Services/Payroll/Payroll.Domain/Services/PayrollCalculation.cs)). أي يعدّل نتائج مجمدة ويمحو التسويات، فيختلف المرحَّل عن المعتمد.
  - تشغيل التصحيح:
    - يأخذ كل تسويات الموظف غير المطبّقة أياً كانت فترتها ([CorrectionRunHandlers.cs:77-82](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
    - يسجّل الفرق مكوّنين: في `BaseSalaryRule` ([PayrollRules.cs:14-24](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)) ثم في `AdjustmentRule`.
    - يحسب ضريبة وتأمينات على الفرق لا تدخل صافيه.
    - يُعلَّم متحققاً ومجمداً دون `ValidateLineIntegrity` ([CorrectionRunHandlers.cs:109-111](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
    - الفرق السالب يصير صفراً عند الترحيل.
  - لا اختبار يجمع تسوية غير صفرية مع التحقق: `PayrollFinancialInvariantsTests.NetPay_EqualsGrossMinusDeductionsPlusAdjustments` يثبت معادلة المحرك وحدها، و`Pay003ReconciliationTests` بلا تسويات.
- **الأثر:** يكفي موظف واحد بنقل بفرق راتب، أو انتداب، أو مهمة، أو علاوة تدريب، فلا تُتحقق دورة الجهة كلها ولا تُرسل للمراجعة. وإن التُفّ على ذلك، يُرحَّل مبلغ غير الذي اعتُمد وتُصدر قسائم لا تطابق الدفع.
- **الإصلاح المقترح:**
  - تعريف واحد للصافي يستعمله المحرك والتحقق والترحيل والقسيمة. التسوية مكوّن صريح داخل المعادلة.
  - الترحيل يقارن ولا يعدّل.
  - تصحيح يقتصر على تسويات فترته، ويمر بالتحقق نفسه، ويحوّل الفرق السالب إلى خصم لاحق.
- **الخطة:** [PLAN-56](acceptance-plans/PLAN-56-pay-run-state-machine.md)
- **التحقق المطلوب:**
  - **AC-PAY-Ca (موجب):** دورة فيها تسوية +50,000 لموظف و−20,000 لآخر. تنجح في التحقق، والمرحَّل = المعتمد = مجموع القسائم.
  - **سالب:** تعديل `NetPay` في قاعدة البيانات بعد التجميد يجعل الترحيل يرفض. تصحيح بفرق سالب لا يصير صفراً.

#### G-PAY-04 — الرفض طريق مسدود، ولا يمكن إلغاء تشغيل اكتمل حسابه
- **الدليل:**
  - `Reject` يعيد الحالة إلى `Calculated` ويُبقي `ResultsFrozen=true` ([PayrollRun.cs:151-164](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)، والتجميد في `MarkValidated` [:113-119](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)). و`ValidateCanCalculate` يرفض أي إعادة حساب مع التجميد ([:60-65](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)).
  - طريق الإلغاء الوحيد يرفض مهمة `Completed` ([PayrollRunJobHandlers.cs:275-276](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). وكل حساب ناجح ينتهي بالمهمة `Completed` ([PayrollRunParallelCalculator.cs:384-386](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)؛ [PayrollRunJobHandlers.cs:336-342](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). ولا يستدعي `MarkCancelled` أي مسار آخر. وتشغيل في حالة `Draft` بلا مهمة لا يُلغى أصلاً.
  - فهرس «التشغيل الحي» يستثني `Reversed` و`Cancelled` فقط ([PayrollDbContext.cs:97-101](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)). فلا يُنشأ تشغيل جديد للفترة: «يوجد تشغيل رواتب حي لنفس الفترة والنوع مسبقاً» ([PayrollRunHandlers.cs:51-57](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)).
  - الوثائق تقول عكس ذلك: «Cancel: … يضع التشغيل والـjob في `Cancelled` إن لم يُرحَّل» ([PAYROLL_RUN_RESUME.md:31](PAYROLL_RUN_RESUME.md)). وPAY-001 يرسم «Any pre-post → Cancelled».
  - سكربتات القبول تلغي التشغيلات بـ SQL مباشر ([acceptance-payroll-determinism.ps1:77-90](../scripts/acceptance-payroll-determinism.ps1)، [acceptance-payroll-resume-chaos.ps1:87-101](../scripts/acceptance-payroll-resume-chaos.ps1)).
- **الأثر:** رفض المعتمد لدورة الشهر يجمّد الشهر. المخرج الوحيد اعتماد الدورة الخاطئة وترحيلها ثم عكسها، أي تحريك مال لتصحيح خطأ، أو تدخل في قاعدة البيانات. حلقة «مراجعة ← رفض ← تصحيح ← إعادة إرسال» لا توجد.
- **الإصلاح المقترح:**
  - الرفض يفك التجميد ويحرر المدخلات المطبّقة ويحذف الأسطر، فيُعاد الحساب.
  - أمر `CancelRun` لأي تشغيل غير مرحّل: بسبب إلزامي، وتدقيق، وتحرير المدخلات (G-PAY-07).
- **الخطة:** [PLAN-56](acceptance-plans/PLAN-56-pay-run-state-machine.md)
- **التحقق المطلوب:**
  - **AC-PAY-Cb (موجب):** معتمد يرفض بملاحظة. المعالج يصحّح مدخلاً ويعيد الحساب والإرسال، ومعتمد يعتمد. وتشغيل `Draft` يُلغى فتتحرر الفترة.
  - **سالب:** إلغاء تشغيل مرحّل مرفوض. إعادة حساب تشغيل معتمد دون رفض مرفوضة.

#### G-PAY-05 — لم تكتمل دورة رواتب واحدة في أي دليل: سبب AC-12، واختبارات التكامل الحمراء
- **الدليل — AC-12 (الجذر في تصميم الاختبار لا في المنتج):**
  - الاختبار ينشئ التشغيل ثم يرسله للمراجعة مباشرة، بلا `calculate` ولا `validate` ([Ac12PayrollSodTests.cs:34-60](../src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs)).
  - الإرسال يتطلب `Calculated` + أسطر + `ValidatedAtCalculate` ([PayrollRun.cs:67-70](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)). فيعود 400 بنص هذا الشرط حرفياً ([AC-12/http/submit-for-review.json](../acceptance-evidence/AC-12/http/submit-for-review.json)).
  - لو صُحّحت الخطوة لفشلت الخطوة الأخيرة:
    - تتوقع 400 فيها «ترحيل» حين يرحّل المعتمد ([Ac12PayrollSodTests.cs:71-76](../src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs)).
    - لكن الشخصية `payroll-approver` لا تملك `payroll-poster` ([personas.json:249-266](../testdata/acceptance/personas.json))، و`PostRun` يتطلب `PayrollPost` ([PayrollApiController.cs:70-72](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)). فيعود 403 قبل المعالج.
    - ولو مُنح الدور، لعاد 400 برسالة فصل الواجبات «لا يمكن لنفس المستخدم اعتماد التشغيل ثم ترحيله.»، لأن فحص SoD ([PayrollRunHandlers.cs:199-209](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)، [PayrollSodGuard.cs:33-34](../src/Services/Payroll/Payroll.Application/Services/PayrollSodGuard.cs)) يسبق فحص سجل المراجعة (`:211-214`). فينجح الإثبات للسبب الصحيح، والعائق الوحيد في هذه الخطوة غياب دور المُرحِّل.
  - خطوة «المنشئ لا يعتمد» تُثبت بالدور (403) لا بقاعدة منشئ ≠ معتمد ([:46-52](../src/Tests/HRManagement.AcceptanceTests/Ac12PayrollSodTests.cs)).
  - الفترة ثابتة (2027-06) ولا إلغاء (G-PAY-04)، فإعادة التشغيل تصطدم بالتشغيل الحي.
  - **يحتاج تحقق:** سجلات التوظيف في بذرة قبول HR.Core بلا `LegalRegimeId` ([AcceptanceDataSeeder.cs:332-351](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/AcceptanceDataSeeder.cs)). ومعرّفات موظفي الرواتب هي معرّفاتهم ([DemoSeedIds.cs:44-45](../src/Shared/HRManagement.ServiceDefaults/DemoSeedIds.cs)). فيُحجب كل تشغيل اعتيادي بـ `MISSING_SERVICE_REGIME` ([ServiceRegimePayrollBlockService.cs:22-52](../src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimePayrollBlockService.cs)). وهذا قد يفسّر لماذا لم يُشغَّل AC-21..25 قط.
- **الدليل — شرط الترحيل خارج كتالوج الشخصيات:** الترحيل يتطلب سجل مراجعة يفتحه مستخدم `PayrollApprove` ويغلقه آخر مختلف ([PayrollExtendedControllers.cs:243-303](../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)). والكتالوج فيه معتمد رواتب واحد لكل جهة، و`auditor` دوره `auditor` فقط ([personas.json](../testdata/acceptance/personas.json)). لذلك يُدخل AC-25 سجل المراجعة المغلق بـ SQL ([acceptance-payroll-audit-fixture.ps1:28-46](../scripts/acceptance-payroll-audit-fixture.ps1)).
- **الدليل — اختبارات التكامل الحمراء على `main` («payroll batch/run»، [plan25-progress.md:122](../reports/plan25-progress.md)):**
  - الحاسب يستدعي حاجز نظام الخدمة للتشغيل الاعتيادي ([PayrollRunParallelCalculator.cs:44-57](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)). ويرفض إن لم يُسجَّل `IHrCoreServiceRegimeClient` و`ServiceRegimes:BlocksFinalPayroll` غير مضبوط (افتراضه `true`) ([PayrollRegimeGovernance.cs:19-23](../src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs)).
  - مضيف الاختبار يسجّل `IConfiguration` فارغاً ولا يسجّل العميل ([PayrollIntegrationTestSetup.cs:16-43](../src/Tests/HRManagement.IntegrationTests/PayrollIntegrationTestSetup.cs)). ولا يضبط أي ملف أو سكربت `BlocksFinalPayroll`.
  - النتيجة: كل اختبار يحسب تشغيلاً اعتيادياً يفشل بـ «تعذر التحقق من أنظمة الخدمة — عميل HR.Core غير مهيأ.»:
    - **على SQLite (10):** `PayrollRunLifecycleIntegrationTests.PayrollRun_Draft_Calculate_Post_Lifecycle` ([:82-83](../src/Tests/HRManagement.IntegrationTests/PayrollRunLifecycleIntegrationTests.cs))، و`Perf001PayrollBatchTests` ×4، و`Pay003ResumablePayrollRunTests` ×4 (كلها عدا `Job_DoubleStart…` الذي يُرفض قبل الحاسب)، و`EndToEndReleaseWorkflowSmokeIntegrationTests` ([:94](../src/Tests/HRManagement.IntegrationTests/EndToEndReleaseWorkflowSmokeIntegrationTests.cs)). والأخير فيه أيضاً فجوة حقن العقود ([plan25-baseline.md:34](../reports/plan25-baseline.md)).
    - **على PostgreSQL مع `RUN_TESTCONTAINERS=true` (4):** `Pay021`، و`Pay022.Pause_Resume…`، و`Pay023`، و`Perf001…PostgreSql`.
    - **يحتاج تحقق** بالتشغيل.
- **الدليل — لا رحلة E2E:** `PayrollLeaveTransferFilesRoleJourneyE2ETests` يفتح الصفحات ويقرأ `personas.json` فقط ([:49-72](../src/Tests/HRManagement.E2E/PayrollLeaveTransferFilesRoleJourneyE2ETests.cs)).
- **الأثر:**
  - مؤشر النجاح «دورة رواتب واحدة مكتملة» و«إغلاق فترة حضور مرتبطة بالرواتب» ([PILOT_DEPLOYMENT.md:20-21](PILOT_DEPLOYMENT.md)) بلا أي دليل.
  - الـ CI أحمر على الرواتب، فأي تراجع في الحساب لا يُرى. *(صُحّح بعد التحقق المستقل: فحص SoD في `PostPayrollRunHandler` يسبق فحص سجل المراجعة، فمعتمد يحمل دور المُرحِّل يُرفض برسالة SoD لا برسالة المراجعة)*
- **الإصلاح المقترح:**
  - إعادة كتابة AC-12:
    - تشغيل محسوب ومتحقق.
    - رفض المنشئ بقاعدة المعالج: شخصية واحدة تحمل `payroll-processor` و`payroll-approver` في بيئة القبول، أو اختبار معالج.
    - مراجعة بشخصية مستقلة (D-PAY-3)، ثم إثبات منع المعتمد من الترحيل بشخصية تحمل الدورين.
    - فترة فريدة لكل تشغيل.
  - تسجيل عميل وهمي لأنظمة الخدمة في مضيف التكامل، واختبار يثبت الحجب حين يعود العميل `Blocked`.
  - `LegalRegimeId` في بذرة القبول.
- **الخطة:** [PLAN-59](acceptance-plans/PLAN-59-pay-full-cycle-production-proof.md) (AC-12، واختبار مسار الحجب، والدورة الكاملة). والعميل المزيف في مضيف التكامل في [PLAN-34](acceptance-plans/PLAN-34-qa-pushable-baseline.md)، و`LegalRegimeId` في بذرة القبول في [PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md) (المرحلة 0.7)
- **التحقق المطلوب:**
  - **AC-PAY-Da (موجب):** على الملف الشبيه بالإنتاج: إنشاء ← حساب ← تحقق ← إرسال ← اعتماد ← مراجعة ← ترحيل ← معالجة دفعة ← ملف خزينة. كل خطوة بشخصية منفصلة عبر HTTP، بلا SQL.
  - **AC-12 سالب:** المنشئ لا يعتمد (رسالة المعالج)، والمعتمد لا يرحّل (رسالة SoD لا رسالة المراجعة).
  - **CI:** 14 اختباراً خضراء.

### 4.2 ثغرات سلامة القرار والمال (P1)

#### G-PAY-06 — آلة حالات التشغيل والعكس: التحقق يعيد أي حالة، والعكس بلا اعتماد
- **الدليل:**
  - `ValidatePayrollRunHandler` لا يفحص الحالة ([PayrollRunJobHandlers.cs:300-346](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). و`MarkValidated` يضبط `Status = Calculated` دون شرط ([PayrollRun.cs:113-119](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)).
    - فطلب `POST api/payroll/runs/{id}/validate` من المعالج ([PayrollApiController.cs:366-373](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)) يعيد تشغيلاً `Approved` أو `Posted` أو `Reversed` إلى `Calculated`. أما `Cancelled` فيُرفض عملياً، لأن إلغاء المهمة يضع أقسامها غير المكتملة `Cancelled` والتحقق يرفض أي قسم غير مكتمل ([PayrollRunJobHandlers.cs:284-290, 317-318](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
    - الزر يظهر فقط في `Calculated`/`Validating` ([Details.cshtml:170](../src/Services/Payroll/Payroll.Web/Views/PayrollRuns/Details.cshtml)): حارس في الواجهة فقط.
  - بعد إرجاع تشغيل مرحّل: العكس مستحيل لأنه يتطلب `Posted`. وإعادة الترحيل تُنشئ دفعة بمرجع التحويل نفسه، فيُكسر الفهرس الفريد ([PayrollDbContext.cs:117-120](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)). و`DbUpdateException` لا يُلتقط ([PayrollRunHandlers.cs:268](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs))، فالنتيجة 500.
  - العكس ([PayrollRunHandlers.cs:277-343](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)):
    - بلا فاعل ولا سبب ولا فصل واجبات. المُرحِّل نفسه يملك `PayrollReverse` ([HrManagementAuthorizationExtensions.cs:144-147](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
    - يضع الدفعة الأصلية `Reversed` مباشرة (`:313-314`)، متجاوزاً `PaymentBatchTransitions` الذي لا يسمح بذلك من `Pending`/`Submitted` ([PayrollRunJobEnums.cs:93-104](../src/Services/Payroll/Payroll.Domain/Enums/PayrollRunJobEnums.cs)).
    - دفعة العكس `Pending` لا تُعالج أبداً، لأن المعالجة تتطلب تشغيلاً `Posted` ([PayrollRunJobHandlers.cs:373-375](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
    - أوامر الدفع لا تُمسّ.
    - يحرر الخصومات والغياب لا التسويات (`:316-325`).
    - لا حدث، فيبقى إجمالي التشغيل في Dev.Analytics.
  - `AddSalaryLine` اليدوي يُنشئ سطراً بلا مكوّنات ([PayrollRunsController.cs:169-203](../src/Services/Payroll/Payroll.Web/Controllers/PayrollRunsController.cs)). فيُفشل التحقق («السطر … بلا مكوّنات»، [PayrollRunReconciliation.cs:19-20](../src/Services/Payroll/Payroll.Domain/Services/PayrollRunReconciliation.cs)) وتُعلّمه قواعد الاحتيال شبحاً (G-PAY-15).
- **الأثر:**
  - تشغيل مرحّل أو معكوس يمكن إحياؤه ثم إعادة اعتماده بمعتمد آخر، أو تعليقه بلا طريق للعكس.
  - العكس، وهو حركة مال، يقرره شخص واحد بلا سبب، ولا يصل إلى المصرف. *(صُحّح بعد التحقق المستقل: التشغيل الملغى لا يُحيا لأن أقسام مهمته الملغاة تُفشل فحص الأقسام في التحقق)*
- **الإصلاح المقترح:**
  - كل انتقال حالة في `PayrollRun` بحارس صريح، والتحقق من `Calculated`/`Validating`/`Calculating` فقط.
  - العكس طلب بسبب يعتمده مستخدم آخر (عاكس ≠ مُرحِّل ≠ معتمد)، عبر `PaymentBatchTransitions`، مع دفعة عكس قابلة للمعالجة، وإلغاء أوامر غير المرسَل، وتحرير التسويات، ونشر `IPayrollReversed`.
  - حذف `AddSalaryLine` أو تحويله إلى تسوية.
- **الخطة:** [PLAN-56](acceptance-plans/PLAN-56-pay-run-state-machine.md)
- **التحقق المطلوب:**
  - **AC-PAY-Cc (موجب):** عكس بطلب وسبب، يعتمده مستخدم آخر، ثم تُعالج دفعة العكس وتعود التسويات إلى الدورة التالية.
  - **سالب:** `validate` على تشغيل `Approved`/`Posted`/`Cancelled` يعود 400 والحالة كما هي. المُرحِّل لا يعتمد عكس ترحيله.

#### G-PAY-07 — المدخلات تضيع عند الإلغاء والفشل، ولا نقطة قطع، والتغييرات المتأخرة والرجعية تسقط
- **الدليل:**
  - الإلغاء لا يحرر `AppliedInRunId`/`PayrollRunId` ([PayrollRunJobHandlers.cs:264-296](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). والمحمّل لا يقرأ إلا ما قيمته `null` أو التشغيل نفسه ([PayrollPartitionInputLoader.cs:127-131, 143-144, 156-162](../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)). فأقساط السلف والجزاءات والغياب والتسويات التي استهلكها تشغيل ملغى أو فاشل تضيع للأبد.
    - سكربت الحتمية يحررها بـ SQL مع تعليق صريح بالسبب ([acceptance-payroll-determinism.ps1:77-90](../scripts/acceptance-payroll-determinism.ps1)).
  - مدخل يصل أثناء الحساب يُفشل القسم ببصمة مختلفة ([PayrollRunParallelCalculator.cs:252-262](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)). والاستئناف يقارن باللقطة القديمة ([PayrollRunJobHandlers.cs:230-260](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs))، فيفشل دائماً. المخرج الوحيد الإلغاء، أي الضياع أعلاه. ولا كيان «فترة رواتب» ولا تاريخ قطع للمدخلات.
  - الغياب يُحمَّل إن تقاطع مع فترة التشغيل فقط ([PayrollPartitionInputLoader.cs:156-162](../src/Services/Payroll/Payroll.Application/Services/PayrollPartitionInputLoader.cs)). إجازة بلا أجر تُعتمد، أو حضور يُغلق، بعد ترحيل فترته لا يُخصم أبداً. وإعادة إغلاق الحضور بأرقام جديدة تُهمل ([PayrollEventConsumers.cs:343-372](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)؛ مشتركة مع G-LVA-03 في دراسة HCM-002).
  - إلغاء إجازة بلا أجر بعد تطبيقها لا يُرجع شيئاً (`absence.AppliedInRunId is null`، [InboundEventHandlers.cs:430](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - تشغيل التصحيح يمرر قوائم غياب وخصومات فارغة ([CorrectionRunHandlers.cs:84-92](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)).
- **الأثر:** دفع زائد (سلفة أو جزاء لا يُستردان) أو ناقص (تسوية مستحقة لا تُصرف) دون أي أثر ظاهر. الحدث المتأخر عن الإقفال، وهو أمر يومي في جهة حكومية، يُفقد أو يوقف الدورة.
- **الإصلاح المقترح:**
  - الإلغاء والفشل يحرران المدخلات في المعاملة نفسها.
  - فترة رواتب بتاريخ قطع: ما يصل بعده يُوجَّه إلى الدورة التالية كبند رجعي مرجعه الفترة الأصلية.
  - عند تعارض البصمة: «إعادة اللقطة» بقرار المعالج.
  - إلغاء خصم مطبّق يولّد تسوية موجبة.
- **الخطة:** [PLAN-56](acceptance-plans/PLAN-56-pay-run-state-machine.md) (التحرير عند الإلغاء والرفض والعكس، وإعادة الحساب بعد تعارض البصمة)، و[PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md) (فروق الحضور بعد الإغلاق). ونقطة القطع والإجازة المتأخرة وإرجاع الخصم الملغى: الموجة 2 (PAY-H)
- **التحقق المطلوب:**
  - **AC-PAY-Ha (موجب):** إلغاء تشغيل بعد حساب فيه قسط سلفة وجزاء وتسوية، فتظهر الثلاثة في تشغيل الفترة الجديد.
  - **AC-PAY-Hb (موجب):** إجازة بلا أجر لشهر مرحّل تُعتمد بعده، فتُخصم في الدورة التالية ببند يحمل الفترة الأصلية. وإلغاؤها بعد ذلك يُرجعها.
  - **سالب:** حدث يصل أثناء الحساب لا يغيّر التشغيل الجاري ولا يضيع.

#### G-PAY-08 — صرف قبل الترحيل وخارج البوابة: الصرف الجزئي وملف الخزينة
- **الدليل:**
  - `partial-disburse`:
    - يتطلب `PayrollProcess` فقط ([PayrollApiController.cs:198-202](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)) ويقبل تشغيلاً `Approved` ([ExecutePartialDisbursementCommand.cs:73-74](../src/Services/Payroll/Payroll.Application/Features/PartialDisbursement/ExecutePartialDisbursementCommand.cs)).
    - يُنشئ `PaymentBatch` ([PartialDisbursementService.cs:134-145](../src/Services/Payroll/Payroll.Application/Services/PartialDisbursementService.cs))، ويرسل أوامر الدفع فوراً ([ExecutePartialDisbursementCommand.cs:108-118](../src/Services/Payroll/Payroll.Application/Features/PartialDisbursement/ExecutePartialDisbursementCommand.cs) ← [PaymentOrderCoordinator.cs:12-28](../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderCoordinator.cs)).
    - يتم ذلك بلا سجل مراجعة مغلق، وبلا فصل المُرحِّل، وبلا فحص احتيال، وبلا `PaymentDisbursementGate`. هذه الضوابط تعمل في `ProcessPaymentBatch` وحده ([PayrollRunJobHandlers.cs:377-398](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
  - AC-24 يثبّت هذا السلوك: المعالج يصرف جزئياً تشغيلاً معتمداً غير مرحّل ([Ac24PartialDisbursementTests.cs:49-84](../src/Tests/HRManagement.AcceptanceTests/Ac24PartialDisbursementTests.cs)). وفي AC-25 تُرسل الأوامر قبل أن تمنع البوابة الدفعة ([Ac25FraudHoldTests.cs:125](../src/Tests/HRManagement.AcceptanceTests/Ac25FraudHoldTests.cs)).
  - تناقض في الوثائق: PAY-001 «No endpoint creates PaymentBatch before review and posting» ([PAY_001_APPROVAL_BYPASS.md:94](PAY_001_APPROVAL_BYPASS.md))، بينما [EXT_003_PAYMENT_ORDER_BINDING.md:3](EXT_003_PAYMENT_ORDER_BINDING.md) يسمح بـ `Approved`. واختبار PAY-001 المعماري يفحص المتحكمات و`PayrollRunHandlers.cs` فقط ([Pay001ApprovalBypassTests.cs:26-56](../src/Tests/HRManagement.UnitTests/Pay001ApprovalBypassTests.cs))، فيفلت `PartialDisbursementService`.
  - ملف الخزينة، وهو أمر الدفع الفعلي إلى المصرف:
    - `ExportTreasuryFile` طلب GET ([PayrollReportsController.cs:245-270](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)).
    - يصدّر صافي كل سطر كاملاً ([PayrollExternalExportService.cs:59-82](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)).
    - يتجاهل الحجز وتنبيهات الاحتيال والتمويل الجزئي وحالة الدفعة.
    - بلا idempotency، ويُعاد توليده بلا حد، ولا علاقة له بـ `PaymentBatch`/`PaymentOrder`.
- **الأثر:** بمجرد وجود حسابات مصرفية (G-PAY-02)، يتجاوز الدفع الحقيقي ضوابط FRD-001 وPAY-004 وفصل المُرحِّل. والمعالج، بعد قرار تمويل يُنشئه المعتمد على تشغيل `Approved` ([CreateFundingDecisionCommand.cs:28-29](../src/Services/Payroll/Payroll.Application/Features/PartialDisbursement/CreateFundingDecisionCommand.cs))، يستطيع إطلاق أوامر دفع لتشغيل لم يُرحَّل دون المُرحِّل وسجل المراجعة وبوابة الاحتيال. *(صُحّح بعد التحقق المستقل: الصرف الجزئي يتطلب قرار تمويل من صاحب `PayrollApprove`، فالمعالج لا يطلقه وحده)*
- **الإصلاح المقترح:**
  - مسار دفع واحد: ترحيل ← دفعة ← بوابة لكل موظف ← أوامر ← ملف يُولَّد من الأوامر لا من الأسطر.
  - مجاميع ضبط وبصمة، مرة واحدة لكل دفعة، بفاعل مُرحِّل.
  - الصرف الجزئي بعد الترحيل فقط وبيد المُرحِّل (D-PAY-2).
  - توسيع اختبار PAY-001 إلى كل `new PaymentBatch`.
- **الخطة:** [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md)
- **التحقق المطلوب:**
  - **AC-PAY-Fa (موجب):** تمويل 60%: ترحيل ← قرار تمويل ← صرف جزئي بيد المُرحِّل ← ملف يطابق الأوامر ← متأخرات للباقي.
  - **سالب:** الصرف الجزئي على تشغيل `Approved` يُرفض. المعالج يُمنع (403). موظف محجوز لا يظهر في الملف. توليد الملف مرتين لا يُنتج ملفاً ثانياً.

#### G-PAY-09 — «أربع عيون» شكلية وفصل واجبات ناقص
- **الدليل:**
  - **الإفراج عن الحجز:**
    - معرّف «المعتمد» يُكتب في النموذج أو جسم الطلب ([PaymentHoldsController.cs:65-69](../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs)، [PaymentHoldsApiController.cs:25-32](../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsApiController.cs)).
    - الخدمة تتحقق فقط من أن الـ GUIDين مختلفان ([PaymentHoldService.cs:26-28](../src/Services/Payroll/Payroll.Application/Services/PaymentHoldService.cs))، لا من وجود المستخدم الثاني أو دوره أو فعله.
    - AC-25 يُفرج بـ `approverUserId = auditorSub` والمعتمد وحده مسجّل الدخول ([Ac25FraudHoldTests.cs:142-158](../src/Tests/HRManagement.AcceptanceTests/Ac25FraudHoldTests.cs)).
  - **حلّ تنبيه الاحتيال:** بلا `ApprovedBy` يصبح المعتمد هو الحالّ نفسه ([FraudAlertsController.cs:96-115](../src/Services/Payroll/Payroll.Web/Controllers/FraudAlertsController.cs)). فمعتمد واحد يغلق تنبيهاً «حرجاً» فتفتح البوابة ([PaymentDisbursementGate.cs:43-55](../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentDisbursementGate.cs)).
  - **تعديل المتأخرات يدوياً:** `approverId` معامل من الطلب ([ArrearsController.cs:95-102](../src/Services/Payroll/Payroll.Web/Controllers/ArrearsController.cs)).
  - **الاعتماد المزدوج:** مُعلن فوق 50,000,000 ل.س ([SodMatrixDefaults.cs:9, 29-37](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SodMatrixDefaults.cs)؛ [SeparationOfDutiesService.cs:29-46](../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs)). لكن `PayrollRun.Approve` ينقل إلى `Approved` من أول اعتماد ([PayrollRun.cs:135-149](../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)). ورواتب جهة متوسطة تتجاوز العتبة كل شهر.
  - **الإطار القانوني:** المستخدم `PayrollApprove` نفسه يعتمد ويصادق قانونياً وينشر ([PayrollConfigurationsController.cs:120-210](../src/Services/Payroll/Payroll.Web/Controllers/PayrollConfigurationsController.cs)). والنشر يقبل المسودة مباشرة ([PayrollConfigurationVersioning.cs:100-112](../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationVersioning.cs)). و«التصديق القانوني» يقوم به معتمد الرواتب لا دور قانوني.
  - **سجل المراجعة قبل الترحيل:** يملكه دور الاعتماد نفسه ([PayrollExtendedControllers.cs:243-303](../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)). ودور `auditor` لا يصل إلى الوحدة ([HrManagementAuthorizationExtensions.cs:41](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
- **الأثر:** الضوابط التي تدّعيها FRD-001 («Hold cannot be released by a single operator»، [FRD_001_FRAUD.md:42](FRD_001_FRAUD.md)) وNAT-003 («no single-actor payroll approval path») يقوم بها شخص واحد. والإطار القانوني الذي يحكم كل راتب ينشره شخص واحد.
- **الإصلاح المقترح:**
  - كل «أربع عيون» طلبان من جلستين: طلب معلّق، ثم اعتماد من مستخدم مختلف بدوره.
  - فرض عدد الاعتمادات حسب العتبة (D-PAY-5).
  - فصل محرر الإطار عن معتمده عن المصادق القانوني (دور قانوني) عن الناشر.
  - مراجعة بدور مستقل (D-PAY-3).
- **الخطة:** [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md) (ومعها تعارض الأدوار في الرمز)، و[PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md) (فصل محرر الإطار عن معتمده ومصادقه وناشره)
- **التحقق المطلوب:**
  - **AC-PAY-Fb (موجب):** معتمد يطلب الإفراج، ومستخدم ثانٍ بدوره يعتمده من جلسته، فتُعالج الدفعة.
  - **سالب:** إرسال GUID مستخدم آخر لا يُفرج. حلّ تنبيه حرج بلا معتمد ثانٍ يبقيه مفتوحاً. تشغيل فوق العتبة لا يصبح `Approved` باعتماد واحد. ناشر الإطار ≠ معتمده.

#### G-PAY-10 — بيانات مالية أساسية ومدخلات بلا سياسة ولا مدقق
- **الدليل:**
  - 16 إجراء POST بلا `[Authorize(Policy=…)]`، يكفيها دور الوحدة `payroll`، بما فيه المعتمد والمُرحِّل والعاكس. والبوابة تمرر تعديلات الرواتب بـ `ModulePayroll` فقط ([GatewayPathAuthorizationMiddleware.cs:228-235](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). الإجراءات:
    - `SalaryScales` Create/Edit/Delete ([SalaryScalesController.cs:35-75](../src/Services/Payroll/Payroll.Web/Controllers/SalaryScalesController.cs)).
    - `PayGrades` Create/Edit/Delete ([PayGradesController.cs:45-86](../src/Services/Payroll/Payroll.Web/Controllers/PayGradesController.cs)).
    - `DeductionTypes` Create/Edit/Delete، ومنها نسب الخصومات النظامية ([DeductionTypesController.cs:31-71](../src/Services/Payroll/Payroll.Web/Controllers/DeductionTypesController.cs)).
    - `CompensationTypes` ×3.
    - `EmployeePayProfiles.Create` ([EmployeePayProfilesController.cs:79-81](../src/Services/Payroll/Payroll.Web/Controllers/EmployeePayProfilesController.cs)): تغيير الراتب الأساسي بلا `PayrollProcess` وبلا نطاق، خلافاً للـ API ([PayrollApiController.cs:138-177](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)).
    - `PayrollRuns.Create` ([PayrollRunsController.cs:77-80](../src/Services/Payroll/Payroll.Web/Controllers/PayrollRunsController.cs)).
    - `BulkPayslips` ([PayrollReportsController.cs:104-106](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)).
    - `ExportJobs.Cancel`.
  - حتى بالسياسة، يضيف المعالج وحده بدلات وخصومات وتسويات ويغيّر الراتب بلا مدقق ([PayrollExtendedControllers.cs:45-59, 94-133, 166-205](../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)؛ [EmployeePayProfileHandlers.cs:23-51](../src/Services/Payroll/Payroll.Application/Features/EmployeePayProfiles/EmployeePayProfileHandlers.cs)). والمعتمد يرى إجمالي التشغيل. والربط بلا `[Bind]` في هذه الإجراءات يسمح بإرسال `AppliedInRunId`/`PayrollRunId`.
  - `DeductionTypes` النظامية عامة وقابلة للتعديل:
    - خارج بصمة الإطار ([PayrollConfigurationHashService.cs:32-55](../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationHashService.cs)) وخارج بصمة المدخلات ([PayrollInputFingerprint.cs:10-40](../src/Services/Payroll/Payroll.Application/Services/PayrollInputFingerprint.cs)).
    - تُقرأ من جديد عند كل حساب ([PayrollRunParallelCalculator.cs:87](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)). وكذلك العطل العامة (`PayrollConfigurationId == null`، [:82-85](../src/Services/Payroll/Payroll.Application/Services/PayrollRunParallelCalculator.cs)).
    - فادعاء «إعادة الحساب bit-for-bit من المدخلات والنسخة» (PAY-002) لا يشمل نسبة التأمينات.
- **الأثر:** معتمد الرواتب يستطيع رفع راتب موظف ثم اعتماد التشغيل. وتغيير نسبة التأمين لكل موظفي الجهة لا يحتاج إلى أحد ولا يترك أثراً في نسخة الإطار.
- **الإصلاح المقترح:**
  - سياسات صريحة واختبار معماري لـ Payroll بنمط `HrCorePostActionsHaveExplicitPolicyTests`.
  - تغييرات الراتب والبدلات والخصومات اليدوية طلبات يعتمدها مستخدم آخر.
  - الخصومات النظامية والعطل داخل نسخة الإطار.
  - DTOs بقوائم سماح.
- **الخطة:** [PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md)، و[PLAN-53](acceptance-plans/PLAN-53-iam-payroll-salary-change.md) (السياسات الصريحة ومعاملة تغيير الراتب)
- **التحقق المطلوب:**
  - **AC-PAY-Ab (موجب):** معالج يطلب تغيير راتب، ومعتمد آخر يعتمده، فيظهر في الدورة التالية.
  - **سالب:** `payroll-approver` يرسل POST إلى `EmployeePayProfiles/Create` أو `DeductionTypes/Edit` فيعود 403. تعديل نسبة نظامية بلا نسخة إطار جديدة غير ممكن.

#### G-PAY-11 — عيوب في قواعد الحساب
- **الدليل:**
  - **صفر أيام استحقاق = راتب كامل:** التقسيط لا يحدث إلا إذا كانت أيام الموظف `> 0` وأقل من أيام الفترة ([PayrollRules.cs:27-34](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)). و`CountEmployeeWorkingDays` يعيد 0 لملف يبدأ بعد الفترة أو ينتهي قبلها ([EmployeePayProfileRules.cs:10-26](../src/Services/Payroll/Payroll.Domain/Services/EmployeePayProfileRules.cs)). والحساب يأخذ كل ملف `IsActive` بلا نظر في التواريخ ([PayrollRunJobHandlers.cs:90-95](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). فالعقد المؤقت المنتهي (`EffectiveTo` ماضٍ وما زال فعّالاً) والتعيين المستقبلي يُصرف لهما راتب شهر كامل.
  - **تغيير الراتب في منتصف الشهر:** النسخة القديمة تُعطَّل ولا تُحسب ([InboundEventHandlers.cs:69-79](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)؛ [EmployeePayProfileHandlers.cs:29-36](../src/Services/Payroll/Payroll.Application/Features/EmployeePayProfiles/EmployeePayProfileHandlers.cs)). فالأيام قبل التغيير لا تُدفع.
  - **خصم يتجاوز الراتب يُشطب:** الصافي يُقصّ إلى صفر ([PayrollRules.cs:305-308](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)) بينما يُعلَّم كل خصم مستهلكاً ([:96-108, 167-178, 202-221](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)). ولا حد قانوني لمجموع الاقتطاعات.
  - **`NetOfStatutory` يطرح كل الخصومات** من الوعاء الضريبي، ومنها الغياب والأقساط والجزاءات ([:268-272](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
  - **الخصومات النظامية:** كل `IsStatutory` يُطبَّق على كل موظف أياً كان نظامه، نسبةً من الإجمالي بكل بدلاته، بلا سقف ([:130-151](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
  - **النظام القانوني مهمل في الحساب:** نسخة إطار واحدة لكل تشغيل حسب الفترة ([PayrollRunJobHandlers.cs:161-185](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). و`EmployeePayProfile.RegimeCode` يُكتب ([InboundEventHandlers.cs:305-306, 644](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) ولا يُقرأ. فموظفو القانون 50 و53 والخدمة المدنية على قواعد واحدة.
  - **قاسم 22 يوماً ثابت** لخصم الإجازة بلا أجر والجزاء ([InboundEventHandlers.cs:366, 598](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)):
    - يُحسب بالراتب والإطار النشط وقت الحدث لا وقت الفترة.
    - يقرر الخصم بمقارنة النص `"Unpaid"` (مشتركة مع HCM-002).
  - **البدلات لا تُقسَّط** لجزء الشهر ([PayrollRules.cs:50-63](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
  - **الصرف من الخدمة يُنهي الملف بتاريخ المعالجة** (`DateTime.UtcNow`) لا بتاريخ القرار ([InboundEventHandlers.cs:610-614](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
- **الأثر:** دفع زائد منتظم (عقود منتهية، تعيينات مؤجلة)، ودفع ناقص (منتصف الشهر)، وشطب ديون للدولة، وضريبة وتأمين خاطئان لكل موظف.
- **الإصلاح المقترح:**
  - الحساب لكل شريحة زمنية من نسخ الملف داخل الفترة.
  - استبعاد الملف خارج الفترة.
  - ترحيل الخصم الزائد إلى الشهر التالي مع سقف قانوني (D-PAY-1).
  - وعاء ضريبي وتأميني من الإطار.
  - إطار لكل نظام.
  - القاسم من الإطار والحساب في المحرك لا في المستهلك.
- **الخطة:** [PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md) (جزئياً: الخصم النظامي بأساس وسقف من نسخة الإطار)، و[PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (جزئياً: لا إنهاء لملف الأجر بحدث تأديبي). والباقي — التقسيط والإطار لكل نظام والقاسم وترحيل الخصم الزائد: الموجة 2 (PAY-E)
- **التحقق المطلوب:**
  - **AC-PAY-Ea:** حالات ذهبية: عقد انتهى قبل الفترة ← صفر. تعيين في 15 من الشهر ← نصف تقريباً. ترفيع في 10 من الشهر ← جزءان. خصم يتجاوز الراتب ← يُرحَّل. موظفان من نظامين ← إطاران.
  - **سالب:** لا سطر لملف خارج الفترة.

#### G-PAY-12 — القيم القانونية غير مربوطة بمرجع، وتخالف حزم المستودع نفسه
- **الدليل:**
  - **الضريبة:**
    - الشرائح المزروعة 5,000 / 15,000 / 30,000 / 50,000 بنسب 0–20% ([PayrollRegimeSeed.cs:12-19](../src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs)).
    - والقيم نفسها احتياط في الشيفرة إن خلا الإطار من الشرائح ([PayrollEngine.cs:12-19](../src/Services/Payroll/Payroll.Domain/Services/PayrollEngine.cs)؛ [PayrollRules.cs:265-266](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
    - حزمة SYR-005 في المستودع: 0% حتى 500,000، و5% حتى 1,000,000، و10% فوقها من 2026-04-01 ([decree-67-68-wage-tables.json:76-85](syr/decree-67-68-wage-tables.json))، وحد أدنى للأجر 450,000 ([:70-74](syr/decree-67-68-wage-tables.json)).
    - لا هذه ولا تلك مصدّقة (SYD-003/004 `OPEN-NO-GO`، [14-SYRIA-GO-LIVE-DECISIONS.md:11-12](completion-backlog/14-SYRIA-GO-LIVE-DECISIONS.md)). لكن الشيفرة تستعمل الأبعد عن الحزمة.
  - **التأمينات:**
    - لا نوع خصم نظامي يُزرع خارج Demo ([DemoPayrollSeeder.cs:275-300](../src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs)).
    - ملف التأمينات يستعمل 7%/14% ثابتة على الإجمالي بلا سقف ([PayrollExternalExportService.cs:19-20, 85-94](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)).
    - ويجد حصة الموظف بمطابقة اسم مكوّن يحوي «تأمين».
    - الحزمة SYR-006 فيها سقف 3,000,000 وفصل GOSI/GOIP ([insurance-jurisdiction-map.json:59-65](syr/insurance-jurisdiction-map.json)).
  - **حزم SYR-005/006 لا تقرؤها أي شيفرة تشغيلية،** بل اختبارات الوحدة فقط (`Syr005WageTests`، `Syr006InsuranceTests`)، والبطاقتان «Done» ([13-SYRIA-PRODUCTION-CARDS.md:62-87](completion-backlog/13-SYRIA-PRODUCTION-CARDS.md)).
  - **العطل:** أربع عطل ثابتة بلا عطل هجرية ([PayrollRegimeSeed.cs:56-73](../src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs)). ولا شيء في الإنتاج.
  - **عصر العملة:** محوّل SYR-004 (`SyrianCurrencyConverter`) لا تستعمله الرواتب، والمتأخرات تكتب `"SYP-new"` نصاً ([PartialDisbursementService.cs:158](../src/Services/Payroll/Payroll.Application/Services/PartialDisbursementService.cs)).
  - **سجل القرارات القانونية:** كله «ApprovedInterim» ([LegalDecisionRegisterSeed.cs:14-27](../src/Services/Payroll/Payroll.Domain/Services/LegalDecisionRegisterSeed.cs)). والتصديق لا يتحقق من وجود القرارات ([PayrollLegalGovernance.cs:11-34](../src/Services/Payroll/Payroll.Domain/Services/PayrollLegalGovernance.cs)).
- **الأثر:** مع البذرة الحالية، كل راتب فوق 50,000 ل.س يخضع لضريبة حدية 20%، بينما الحزمة تعفي حتى 500,000. والتأمين بلا سقف ولا حصة صاحب عمل في الحساب. والقيم «مؤقتة» بلا آلية تمنع الإنتاج بها.
- **الإصلاح المقترح:**
  - استيراد الحزم الموقعة إلى نسخة الإطار: الضريبة، والإعفاء، والوعاء، والتأمين بسقفه وجهته، والحد الأدنى، والعطل، والعصر، والتقريب.
  - كل قيمة بمرجع قانوني، داخل البصمة.
  - منع النشر في الإنتاج لقيمة «Interim» إلا باستثناء موقّع (D-PAY-1).
- **الخطة:** [PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md) (البنية: الخصومات النظامية والعطل داخل النسخة، والمرجع لكل قيمة). والقيم النهائية: الموجة 2 (PAY-E)
- **التحقق المطلوب:**
  - **AC-PAY-Eb:** حالات ذهبية من وزارة المالية لكل نظام تُعاد bit-for-bit.
  - **سالب:** نشر إطار فيه قيمة بلا مرجع يُرفض في الإنتاج. تغيير نسبة التأمين يُنتج نسخة جديدة وبصمة جديدة.

#### G-PAY-13 — مدخلات الحركات في الرواتب: تطبيق مزدوج، وتعويض فارغ، وأحداث بلا مستهلك
مشتركة مع G-MOV-04 وG-MOV-05 في دراسة الحركات (FUN-003). هنا جانب المستهلك في الرواتب فقط.
- **الدليل:**
  - الرواتب طرف إلزامي في saga النقل والانتداب ([TransferSecondmentStateMachines.cs:41-47, 334](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs)). وتستهلك أيضاً الحدثين النهائيين ([PayrollEventConsumers.cs:109-150, 174-214, 216-225, 286-294](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)).
    - يصل فرق النقل مرتين: `BaseSalary += salaryChange` ([InboundEventHandlers.cs:313](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)) وتسويتان (`:292-298`)، بلا فحص للعلامة.
    - وفي كل مرة يُضاف الفرق إلى الأساس **و**يُدفع تسويةً لمرة واحدة.
    - وبدل الانتداب يُضاف مرتين، ولمرة واحدة لا شهرياً طوال المدة ([:509-528](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - التعويض لا يفعل شيئاً (`if (!m.Compensate)`، [PayrollEventConsumers.cs:119, 184](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)). نقل يُعكس في الـ saga يُبقي زيادة الراتب.
  - أحداث بلا مستهلك في الرواتب: `IPromotionExecuted`، و`ILoanAssignmentExecuted`/`Ended` (الإعارة)، و`IEmployeeStatusChanged`، و`ILegalJudgmentIssued`، و`IMedicalDecisionIssued`. وHR.Core يسجّل الترفيع ملاحظة فقط ([PromotionExecutedConsumer.cs](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/PromotionExecutedConsumer.cs)). ولا درجات/مراتب في `PayGrade` (`GradeCode` نصي)، بينما الحدث يحمل `int`.
- **الأثر:**
  - المنقول بفرق راتب يُدفع له الفرق أربع مرات في الشهر الأول (مرتان في الأساس ومرتان تسويةً)، ومرتين دائماً بعده.
  - المرفَّع يبقى على راتبه القديم.
  - المعار أو الموقوف يستمر راتبه. *(صُحّح بعد التحقق المستقل: استدعاءان لـ `HandleTransferExecutedHandler` يضيفان الفرق إلى الأساس مرتين وينشئان تسويتين، فالشهر الأول أربعة أضعاف لا ثلاثة)*
- **الإصلاح المقترح:**
  - قناة واحدة لكل حركة (قرار مع FUN-003).
  - مستهلكات بمفتاح عمل فريد (`TransferRequestId`).
  - الفرق يُطبَّق نسخةَ ملف بتاريخ نفاذ، لا زيادةً ودفعةً معاً.
  - تعويض يعكس الأثر.
  - مستهلكات للترفيع (درجة/مرتبة ← ملف جديد مع فروق رجعية)، والإعارة، وتغيير الحالة، والحجز القضائي.
- **التحقق المطلوب:**
  - **AC-PAY-Ga (موجب):** نقل بفرق +100,000 من أول الشهر: الأساس يزيد 100,000 مرة واحدة، ولا تسوية مضاعفة. ترفيع بأثر من شهر مرحّل ← فرق رجعي مرة واحدة.
  - **سالب:** تسليم الحدثين مرتين لا يغيّر شيئاً. تعويض الـ saga يعيد الأساس.

#### G-PAY-14 — نهاية الخدمة: سنوات خدمة خاطئة، وشهر أخير غير مدفوع، وصرف بلا أداة
- **الدليل:**
  - **سنوات الخدمة** تُحسب من `EffectiveFrom` للملف الفعّال ([InboundEventHandlers.cs:221-230](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs) ← [EndOfServiceCalculation.cs:15-22](../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)). وكل تفعيل عقد أو تحديث راتب يُنشئ ملفاً بتاريخ التغيير، فتبدأ السنوات من آخر تغيير.
  - **الشهر الأخير:** الانفصال يعطّل الملف ([InboundEventHandlers.cs:211-214](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). والحساب يأخذ الملفات الفعّالة فقط ([PayrollRunJobHandlers.cs:90-95](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)). و`PayrollRunType.FinalSettlement` ([PayrollRunType.cs:12](../src/Services/Payroll/Payroll.Domain/Enums/PayrollRunType.cs)) لا يُنشئه أي مسار آلي. يمكن طلبه يدوياً في `POST api/payroll/runs` ([PayrollApiController.cs:403-408](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs))، لكنه يأخذ الملفات الفعّالة فقط مثل الاعتيادي، فلا يشمل المنفصل. فأيام شهر الانفصال لا تُدفع إذا وصل الحدث قبل الحساب.
  - **السلف:** المتبقي يُطرح من صافي التسوية ([EndOfServiceCalculation.cs:127-153](../src/Services/Payroll/Payroll.Domain/Services/EndOfServiceCalculation.cs)). **و**يُنشأ خصم `AdvanceFinalRecovery` بالمبلغ نفسه ([InboundEventHandlers.cs:235-243](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). وتبقى الأقساط الأصلية مفتوحة: قيود مزدوجة لموظف لن يدخل أي تشغيل.
  - **التسوية اليدوية:** `Create` يربط الكيان كله، فتُحفظ المبالغ التي يكتبها المستخدم ([EndOfServiceController.cs:69-80](../src/Services/Payroll/Payroll.Web/Controllers/EndOfServiceController.cs)). والاعتماد لا يقارن بالمنشئ، والكيان بلا `CreatedBy` ([EndOfServiceHandlers.cs:47-66](../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs)).
  - **«مدفوع»** يُسجَّل بلا دفعة ولا أمر ولا ملف ([:70-88](../src/Services/Payroll/Payroll.Application/Features/EndOfService/EndOfServiceHandlers.cs)).
  - المكافأة على الأساس وحده بمعدلات مؤقتة ([PayrollRegimeSeed.cs:98-100](../src/Services/Payroll/Payroll.Domain/Services/PayrollRegimeSeed.cs)). ولا ضريبة عليها.
- **الأثر:** موظف بخدمة 20 سنة رُفّع قبل عام يُحسب له عام واحد. ومبلغ نهاية الخدمة يُعلَن مدفوعاً ولا يخرج من النظام. *(صُحّح بعد التحقق المستقل: `FinalSettlement` قابل للإنشاء عبر API بنوع التشغيل، لكنه لا يحل مشكلة الشهر الأخير لأنه يختار الملفات الفعّالة فقط)*
- **الإصلاح المقترح:**
  - تاريخ بدء الخدمة من HR.Core في حدث الانفصال.
  - تشغيل `FinalSettlement` بآخر راتب حتى تاريخ الانفصال.
  - تسوية السلف في مكان واحد.
  - التسوية تُحسب في الخادم فقط.
  - منشئ ≠ معتمد ≠ صارف.
  - الصرف عبر دفعة وأمر.
  - المعادلة لكل نظام (D-PAY-7).
- **الخطة:** [PLAN-64](acceptance-plans/PLAN-64-sep-final-settlement.md) (حساب نهاية الخدمة). وباقي الفجوة: الموجة 2 (PAY-G)
- **التحقق المطلوب:**
  - **AC-PAY-Gb (موجب):** موظف بتعيين 2006 وترفيع 2025، ينفصل في 2026-05-20: سنوات ≈ 20، وتشغيل نهائي لـ 20 يوماً، وسلفته تُسترد مرة واحدة، وتُصرف التسوية بأمر دفع.
  - **سالب:** مبالغ مرسلة في `Create` تُهمل. المنشئ لا يعتمد.

#### G-PAY-15 — بوابات «الكل أو لا شيء» ومدخلات احتيال شكلية
- **الدليل:**
  - **حاجز نظام الخدمة:** موظف واحد باستثناء مفتوح، أو بلا نظام، يوقف حساب التشغيل كله. والرسالة تذكر العدد لا الأسماء ([PayrollRegimeGovernance.cs:28-33](../src/Services/Payroll/Payroll.Application/Services/PayrollRegimeGovernance.cs)؛ [ServiceRegimePayrollBlockService.cs:22-52](../src/Services/HRCore/HR.Core.Infrastructure/Services/Legal/ServiceRegimePayrollBlockService.cs)).
  - **الحجز:**
    - حجز واحد نشط على أي موظف في الدفعة يوقفها كلها ([PaymentDisbursementGate.cs:21-41](../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentDisbursementGate.cs)). والرسالة تقترح «أو استبعادهم»، ولا أمر استبعاد.
    - الحجز بلا تشغيل (`PayrollRunId = null`) يوقف كل دفعة حتى الإفراج. مثاله حجز PAY-029 لموظف جديد بلا راتب ([InboundEventHandlers.cs:89-94, 147-178](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - **مدخلات الاحتيال** ([FraudScanInputBuilder.cs:22-29](../src/Services/Payroll/Payroll.Application/Services/FraudScanInputBuilder.cs)):
    - `HasAttendance` و`HasSourceRecord` = «له لقطة مدخلات».
    - `NationalIdHash` = `null`، والـ IBAN فارغ.
    - `IsDeceased` و`ExpectedNetPay` و`ApproverActionCount` لا تُضبط.
    - فلا تعمل FR-02/03/04/05/06 أبداً.
    - أما FR-01 وFR-08 (حرجتان بحجز) فتعملان على كل سطر في تشغيل تصحيح، إذ لا job له، وعلى كل سطر يدوي. فالتصحيحات محجوزة دائماً.
    - تقييم شهادة الحياة لا يستدعيه أحد ([FraudAnomalyEngine.cs:97-127](../src/Services/Payroll/Payroll.Application/Services/FraudAnomalyEngine.cs)).
- **الأثر:** موظف واحد ينقصه تصنيف نظام، أو معيَّن جديد ينقصه راتب، يؤخر رواتب كل الجهة. والضوابط المضادة للاحتيال لا ترى أي احتيال حقيقي، وتمنع كل تصحيح.
- **الإصلاح المقترح:**
  - استبعاد على مستوى الموظف: سطره «معلّق»، ومبلغه في المتأخرات، وتقرير بالأسماء والأسباب، وإكمال الباقي (D-PAY-4).
  - مدخلات احتيال حقيقية من HR.Core والحضور والسجل المدني والدرجة.
  - إعفاء التصحيح من «شبح» بربطه بسطر المصدر.
  - مهمة شهادة الحياة (G-PAY-21).
- **الخطة:** [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md)
- **التحقق المطلوب:**
  - **AC-PAY-Fc (موجب):** دفعة لـ 100 موظف، واحد محجوز: تُرسل 99، ويُقيَّد الواحد متأخرات، ويُصرف بعد الإفراج.
  - **سالب:** موظفان بـ IBAN واحد ينتجان تنبيه FR-04. تصحيح لموظف حقيقي لا ينتج FR-01.

#### G-PAY-16 — ما بعد الترحيل: الدفعة تقف عند «مُرسلة»، والمطابقة ذاتية، والمتأخرات لا تُسدَّد
- **الدليل:**
  - أوامر `Ack`/`Settle`/`Reject` للدفعة ([PayrollRunJobHandlers.cs:421-515](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)) بلا متحكم ولا مستهلك. لا يستدعيها إلا `G4SandboxPaymentCycleIntegrationTests`. فبعد `ProcessPayment` تبقى الدفعة `Submitted` إلى الأبد، و`IsPaid` لا يصدق.
  - **«المطابقة البنكية»** تنشئ أسطر كشف من أوامر الدفع نفسها ثم تطابقها ([BankReconciliationCoordinator.cs:12-34](../src/Services/Payroll/Payroll.Infrastructure/Services/BankReconciliationCoordinator.cs)). وبلا مرجع تطابق بالمبلغ وحده ([BankReconciliationService.cs:41-47](../src/Services/Payroll/Payroll.Infrastructure/Services/BankReconciliationService.cs))، وموظفو الدرجة الواحدة لهم الصافي نفسه.
    - **يحتاج تحقق:** المرجع المشترك لكل أوامر الدفعة ([PaymentOrderCoordinator.cs:45-58](../src/Services/Payroll/Payroll.Infrastructure/Services/PaymentOrderCoordinator.cs)) يصطدم بالفهرس الفريد `(TenantId, StatementReference)` ([PayrollDbContext.cs:142](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)) من السطر الثاني.
  - **المتأخرات:**
    - `ArrearsSettlementService.Settle` بلا مستدعٍ في الخدمات.
    - مفاتيح idempotency في قاموس في الذاكرة لخدمة scoped ([ArrearsSettlementService.cs:25, 48, 115](../src/Services/Payroll/Payroll.Application/Services/ArrearsSettlementService.cs)).
    - المتأخرات لا تدخل أي دورة لاحقة. فـ «catch-up payment» في [PAY_004_ARREARS.md](PAY_004_ARREARS.md) غير موجود، والموظف يرى رصيده فقط.
  - **الحدث** `IPayrollProcessed` يُنشر بعد التزام الحالة، في `SaveChanges` ثانٍ ([PayrollRunHandlers.cs:260-264](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs)).
- **الأثر:** لا يعرف النظام أبداً هل دُفع الراتب، ولا يكشف دفعاً مزدوجاً أو ضائعاً. وأي صرف جزئي يتحول إلى دين على الدولة لا يُسدَّد عبر النظام.
- **الإصلاح المقترح:**
  - استيراد إشعار القناة وكشف المصرف (ملف أو API)، وتطبيق `Ack`/`Settle`/`Reject` لكل أمر.
  - مطابقة بمرجع الأمر لا بالمبلغ.
  - إدراج المتأخرات المفتوحة في الدورة التالية أو دفعة متأخرات، بمفتاح دائم في قاعدة البيانات.
  - نشر الحدث في المعاملة نفسها.
- **الخطة:** [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md) (تسجيل `Ack`/`Settle`/`Reject` يدوياً بشخصين، تحتاجه PLAN-64). والاستيراد الآلي والمطابقة وسداد المتأخرات: الموجة 2 (PAY-I)
- **التحقق المطلوب:**
  - **AC-PAY-Ia (موجب):** ملف إشعار بقبول 98 ورفض 2: تنتقل الأوامر، ويُعاد إرسال المرفوضين بعد التصحيح دون إعادة الناجحين. ومتأخرات شهر سابق تُدفع في الشهر التالي مرة واحدة.
  - **سالب:** استيراد الملف مرتين لا يغيّر شيئاً. سطر كشف بالمبلغ دون مرجع لا يُطابق.

#### G-PAY-17 — القسائم والخدمة الذاتية: الموظف يرى غير المعتمد، ولا يصل إلى PDF قسيمته
- **الدليل:**
  - ESS تعرض أسطر الرواتب من أي تشغيل: `Draft`، و`UnderReview`، و`Cancelled`، و`Reversed` ([EssController.cs:37-43](../src/Services/Payroll/Payroll.Web/Controllers/EssController.cs)؛ [Payslips.cshtml:11](../src/Services/Payroll/Payroll.Web/Views/Ess/Payslips.cshtml)).
  - قسيمة ESS تحوّل إلى `PayrollReports/Payslip` ([EssController.cs:62](../src/Services/Payroll/Payroll.Web/Controllers/EssController.cs)). ومرشح الوحدة يستثني `Ess*` فقط ([HrModuleAuthorizationFilter.cs:28](../src/Shared/HRManagement.ServiceDefaults/HrModuleAuthorizationFilter.cs)). والبوابة تطلب `ModulePayroll` لكل مسار رواتب لا يحوي `/Ess` ([GatewayPathAuthorizationMiddleware.cs:240-254](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). فالموظف يحصل على 403 (**يحتاج تحقق**).
  - موظف الرواتب الذي يحمل `employee_id` لا يطبع إلا قسيمته ([PayrollReportsController.cs:67-69](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)) (**يحتاج تحقق** من مطالبات رموز الشخصيات).
  - مجرد فتح الـ PDF يعلّم القسيمة «أُرسلت» ([:81, 139](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)). ولا إشعار للموظف.
- **الأثر:**
  - الموظف يرى مبالغ لم تُعتمد أو أُلغيت، فتنشأ شكاوى.
  - والقسيمة الرسمية لا تصله.
  - و«أُرسلت» لا تعني شيئاً.
- **الإصلاح المقترح:**
  - ESS تعرض المرحَّل فقط (D-PAY-10) وتقدّم الـ PDF بنفسها.
  - مسار منفصل لموظفي الرواتب.
  - حالة «أُتيحت» عند الترحيل، و«اطُّلع عليها» عند الفتح، وإشعار.
- **التحقق المطلوب:**
  - **AC-PAY-Ib (موجب):** موظف بدور `hr-employee` يرى قسيمة الشهر المرحَّل وينزّل PDF.
  - **سالب:** لا يرى تشغيلاً قيد المراجعة أو ملغى. لا يصل إلى قسيمة غيره.

### 4.3 قدرات ناقصة (P2)

#### G-PAY-18 — لا محرك فروق رجعية ولا علاوة دورية
- **الدليل:**
  - تغيير راتب بأثر في فترة مرحّلة لا يفعل شيئاً لتلك الفترة.
  - تشغيل التصحيح يجمع التسويات غير المطبّقة فقط، ولا يعيد حساب الأساس والبدلات والخصومات للفترة ([CorrectionRunHandlers.cs:68-97](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/CorrectionRunHandlers.cs)). و`MaxRetroCorrectionMonths` حد يُفحص فقط (`:37-43`).
  - لا مفهوم مرتبة أو علاوة دورية في `PayGrade` ([PayGrade.cs](../src/Services/Payroll/Payroll.Domain/Entities/PayGrade.cs)).
- **الأثر:** زيادات المراسيم بأثر رجعي (أمر متكرر)، والترفيع المتأخر، وتصحيح الأخطاء، كلها تُحسب يدوياً خارج النظام وتُدخل تسويةً بلا تفسير.
- **الإصلاح المقترح:** إعادة حساب «ظل» للفترات المرحّلة بالمدخلات الجديدة، والفرق بنود مرجعها الفترة، والضريبة بحسب D-PAY-8. ومراتب وعلاوة دورية في السلم.
- **التحقق المطلوب:** **AC-PAY-Hc:** مرسوم زيادة 10% بأثر من ثلاثة أشهر مرحّلة ← ثلاثة بنود فرق تطابق الحساب اليدوي. **سالب:** فترة أقدم من الحد تُرفض.

#### G-PAY-19 — الحضور والإضافي والمناوبات لا تصل إلى الرواتب كما يلزم
- **الدليل:**
  - الرواتب تأخذ أيام الغياب من `IAttendancePeriodClosed` فقط ([PayrollEventConsumers.cs:343-372](../src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs)). وإغلاق HR.Core ينشر لكل موظف بلا حالة فترة محفوظة ([AttendanceIntegrationService.cs:75-123](../src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs)؛ G-LVA-03). ولا يشترط الحساب إغلاق الحضور.
  - الإضافي والمناوبات يُحسبان للعرض في جداول HR.Core بنسب ثابتة وأجر ساعة «استرشادي» 100 ([ShiftAllowanceRules.cs:12-18](../src/Services/HRCore/HR.Core.Domain/Services/ShiftAllowanceRules.cs)؛ [RostersController.cs:14, 94-99](../src/Services/HRCore/HR.Core.Web/Controllers/RostersController.cs)). ولا حدث ولا مكوّن في الرواتب.
- **الأثر:** مؤشر «إغلاق فترة حضور مرتبطة بالرواتب» لا آلية له. والإضافي والمناوبات لا تُدفع.
- **الإصلاح المقترح:** حالة فترة حضور يُشار إليها في التشغيل (إغلاق إلزامي قابل للضبط)، وحدث إضافي/مناوبة معتمد يُنتج `AllowanceRecord`.
- **الخطة:** [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md) (جزء الحضور). والإضافي والمناوبات: الموجة 2 (PAY-H)
- **التحقق المطلوب:** **AC-PAY-Hd:** تشغيل لفترة حضور غير مغلقة ← تحذير أو رفض بحسب الضبط. إغلاق بإضافي معتمد ← بند في القسيمة.

#### G-PAY-20 — لا تكامل مالي: الأستاذ العام، والموازنة، وتقرير الضريبة، وحصة صاحب العمل، والخزينة
- **الدليل:**
  - `IPayrollProcessed` يستهلكه Dev.Analytics وحده ([PayrollProcessedConsumer.cs](../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Consumers/PayrollProcessedConsumer.cs)). ولا قيد يومية، ولا ترميز باب/فصل/بند.
  - `ITaxWithholdingReporter` مسجّل ([ExternalIntegrationServiceCollectionExtensions.cs:29-32](../src/Shared/HRManagement.Integration.FileAdapters/ExternalIntegrationServiceCollectionExtensions.cs)) ولا يستدعيه أحد.
  - حصة صاحب العمل غير محسوبة في الدورة، بل 14% ثابتة في ملف التصدير. ولا فصل بين GOSI وGOIP بحسب النظام.
  - موصّل الخزينة Live يُسجَّل ([GovernmentConnectorExtensions.cs:38-39](../src/Shared/HRManagement.ServiceDefaults/Connectors/GovernmentConnectorExtensions.cs))، ثم يغلبه موصّل الملف المسجّل بعده ([Payroll.Infrastructure/DependencyInjection.cs:38](../src/Services/Payroll/Payroll.Infrastructure/DependencyInjection.cs) ← [ExternalIntegrationServiceCollectionExtensions.cs:26](../src/Shared/HRManagement.Integration.FileAdapters/ExternalIntegrationServiceCollectionExtensions.cs)). و`TreasuryPaymentClaimGuard` لا تستعمله الرواتب.
  - السلفة تُسترد عبر الرواتب ولا تُصرف عبرها (**يحتاج تحقق** من صرفها خارج النظام).
- **الأثر:** المالية تعيد إدخال الرواتب في نظامها. ولا تقرير ضريبة شهري، ولا مطالبة اشتراكات صحيحة.
- **الإصلاح المقترح:** قيد محاسبي بخريطة حسابات لكل مكوّن ومركز كلفة، وتقرير ضريبة شهري، واشتراكات صاحب العمل في الحساب، وموصّل خزينة واحد قابل للضبط (D-PAY-9).
- **التحقق المطلوب:** **AC-PAY-Ja:** ترحيل دورة ← قيد متوازن بمجموع الإجمالي، وتقرير ضريبة يساوي مجموع مكوّن الضريبة. **سالب:** دورة غير مرحّلة لا تُنتج قيداً.

#### G-PAY-21 — التشغيل والاستمرارية: لا مهام خلفية، والحساب داخل الطلب
- **الدليل:**
  - لا `HostedService` للرواتب في [Program.cs](../src/Services/Payroll/Payroll.Web/Program.cs). فلا يوجد:
    - مراقب للمهام العالقة. سكربت AC-22 يوقف المهمة الميتة يدوياً قبل الاستئناف ([acceptance-payroll-resume-chaos.ps1:211-217](../scripts/acceptance-payroll-resume-chaos.ps1)).
    - مهمة لانتهاء شهادة الحياة.
    - إشعارات للقسائم.
    - مهلة زمنية للحجز أو التنبيه.
  - الحساب يعمل داخل طلب HTTP (`GenerateLines`/`calculate` ← `ProcessAsync`). وعنقود الرواتب في البوابة بلا `ActivityTimeout`، والقسم `ReverseProxy:Defaults` ليس مفتاح YARP ([Gateway appsettings.json:167-173](../src/Gateway/HRManagement.Gateway/appsettings.json))، فالمهلة الافتراضية 100 ثانية.
    - عند الانقطاع تبقى المهمة `Running` بعقد إيجار (lease) مدته 30 دقيقة ([PayrollRunJobHandlers.cs:49-52, 82-83](../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunJobHandlers.cs)).
    - والقسم الذي حجزه عامل ميت لا يُعاد إلا بالاستئناف ([PayrollRepository.cs:269-281](../src/Services/Payroll/Payroll.Infrastructure/Repositories/PayrollRepository.cs)) (**يحتاج تحقق** بالأحجام).
  - لا استبعاد موظف من التشغيل (G-PAY-15). ولا حجز قضائي ولا حد اقتطاع (G-PAY-11).
- **الأثر:** دورة جهة كبيرة قد تنقطع عند البوابة وتبقى معلقة نصف ساعة، ولا أحد يُنبَّه.
- **الإصلاح المقترح:** تنفيذ الحساب في عامل خلفي (`202 Accepted` + متابعة)، ومراقب عقود إيجار، ومهام شهادة الحياة والإشعارات، ومهلة صريحة للمسار.
- **التحقق المطلوب:** **AC-PAY-Jb:** قتل العامل أثناء الحساب ← يستأنف المراقب خلال مدة الإيجار دون تدخل، ولا أسطر مكررة. **سالب:** لا عاملان على قسم واحد.

### 4.4 فجوات التحقق والتوثيق

#### G-PAY-22 — ادعاءات «Done» واختبارات قبول تُثبّت الثغرات (P1)
- **الدليل:**
  - **PAY-001 «Done»** ([01-P0-RELEASE-BLOCKERS.md:141-162](completion-backlog/01-P0-RELEASE-BLOCKERS.md)؛ [PAY_001_APPROVAL_BYPASS.md:94-97](PAY_001_APPROVAL_BYPASS.md)) مقابل: الصرف الجزئي قبل الترحيل (G-PAY-08)، و16 POST بلا سياسة (G-PAY-10)، والتحقق يعيد أي حالة (G-PAY-06)، والترحيل يعدّل الأسطر (G-PAY-03).
  - **PAY-003 «Pause/Resume/Cancel»** مقابل الإلغاء المرفوض بعد الحساب ([PAYROLL_RUN_RESUME.md:31](PAYROLL_RUN_RESUME.md)).
  - **PAY-004 «Done»** ([PAY_004_ARREARS.md:4, 16](PAY_004_ARREARS.md)):
    - يصف `SharedKernel/Payroll/ArrearsLedgerEntry.cs`، وهي نسخة غير مستخدمة بحقول وحالات مختلفة ([SharedKernel/Payroll/ArrearsLedgerEntry.cs](../src/Shared/HRManagement.SharedKernel/Payroll/ArrearsLedgerEntry.cs) مقابل [Payroll.Domain/Entities/ArrearsLedgerEntry.cs](../src/Services/Payroll/Payroll.Domain/Entities/ArrearsLedgerEntry.cs)).
    - التسوية اللاحقة وأولويات «النص القانوني» غير موجودة.
  - **FRD-001 «Done»** ([FRD_001_FRAUD.md:4, 42-45](FRD_001_FRAUD.md)) مقابل G-PAY-09 وG-PAY-15.
  - **SYR-005/006 «Done»** بملفات JSON واختبارات تقرؤها فقط (G-PAY-12).
  - **[PLAN-10:14](acceptance-plans/PLAN-10-payroll-money.md)** متقادم: يقول إن الصرف الجزئي لا يُنشئ أوامر دفع، وهو يُنشئها الآن.
  - **اختبارات القبول:**
    - AC-24 يصرف جزئياً بيد المعالج تشغيلاً غير مرحّل.
    - AC-25 يُفرج بـ GUID المدقق دون جلسته، ويُدخل سجل المراجعة بـ SQL.
    - سكربتا AC-21/22 يلغيان التشغيلات بـ SQL.
    - AC-21..25 لم تُشغَّل قط ([summary.md:53-57](../acceptance-evidence/summary.md)).
  - اختبار PAY-001 المعماري محدود بملفين (G-PAY-08).
- **الأثر:** لوحة الجاهزية تقول «Done» لضوابط مالية لا تعمل. وإذا شُغّلت اختبارات القبول الحالية ونجحت، ستُسجَّل الثغرات أدلةً على الإغلاق.
- **الإصلاح المقترح:**
  - تصحيح الوثائق لتطابق الشيفرة، مع دليل لكل حالة، وحذف نسخة `SharedKernel`.
  - إعادة كتابة AC-24/25 لتُثبت السلوك الصحيح.
  - منع SQL في سكربتات القبول إلا للقراءة، باختبار معماري.
- **الخطة:** [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md) (الوثائق والإعادة الحية)، مع إعادة كتابة AC-24/25 في [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md)، وإزالة SQL من سكربتات قبول الرواتب في [PLAN-59](acceptance-plans/PLAN-59-pay-full-cycle-production-proof.md)
- **التحقق المطلوب:** مراجعة سطرية لكل ادعاء مقابل `file:line`. وAC-12 وAC-21..25 `Pass` تحت `ACCEPTANCE_REQUIRE_ALL=true` بلا أي `UPDATE`/`INSERT` في السكربتات.

#### G-PAY-23 — تحسينات صغيرة (P3)
- دقة `numeric(18,2)` مفروضة على كل `decimal`، ومنها النسب: `FundingRatio` (0.333 ← 0.33)، و`Percentage`، و`RatePercent` ([PayrollDbContext.cs:61-67](../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs)؛ [FundingDecision.cs:22](../src/Services/Payroll/Payroll.Domain/Entities/FundingDecision.cs)).
- تشغيل `Draft` بلا مهمة يحجز الفترة، ولا حذف له (يُعالج مع G-PAY-04).
- رسالة حاجز نظام الخدمة بلا أسماء، ورسائل الحساب بلا رابط إلى الموظف.

### 4.5 تقاطعات مع الدراسات الأخرى

- **→ IAM:** `BolaArchitectureTests` أحمر على إجراءات `Index(Guid? employeeId…)` بلا نطاق في Payroll ([PayrollExtendedControllers.cs:17, 64, 138](../src/Services/Payroll/Payroll.Web/Controllers/PayrollExtendedControllers.cs)، [EndOfServiceController.cs:39](../src/Services/Payroll/Payroll.Web/Controllers/EndOfServiceController.cs)، [PaymentHoldsController.cs:27](../src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs)، [FraudAlertsController.cs:28](../src/Services/Payroll/Payroll.Web/Controllers/FraudAlertsController.cs)). و`GET api/payroll/payslips/{id}` و`runs/{id}` بلا نطاق ([PayrollApiController.cs:79-122, 384-400](../src/Services/Payroll/Payroll.Web/Controllers/PayrollApiController.cs)).
- **→ IAM:** دور `admin` يحقق السياسات الأربع ([HrManagementAuthorizationExtensions.cs:132-147](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)). وفصل الواجبات بمعرّف الفاعل فقط.
- **→ IAM:** نقطة HR.Core `payroll-blockers` تحت `HrCoreLookupS2S` تقبل أي مستخدم تفاعلي ([ServiceAuthorizationExtensions.cs:74-82](../src/Shared/HRManagement.ServiceDefaults/ServiceAuthorizationExtensions.cs)).
- **→ WF:** اعتماد التشغيل حالة محلية لا مسار عمل، فلا سلسلة اعتماد (مالي ← آمر صرف). و`AddHrPolicyEnforceClient` مسجّل بلا استعمال ([Program.cs:115](../src/Services/Payroll/Payroll.Web/Program.cs)).
- **→ POL:** نسب المناوبة والإضافي ثابتة في HR.Core ([ShiftAllowanceRules.cs:12-18](../src/Services/HRCore/HR.Core.Domain/Services/ShiftAllowanceRules.cs)).
- **→ QA:** مضيف تكامل الرواتب بلا عميل أنظمة الخدمة ([PayrollIntegrationTestSetup.cs:16-43](../src/Tests/HRManagement.IntegrationTests/PayrollIntegrationTestSetup.cs))، والتحليل في G-PAY-05. وسكربتات قبول تكتب SQL ([acceptance-payroll-audit-fixture.ps1:28-46](../scripts/acceptance-payroll-audit-fixture.ps1)).
- **→ OPS:** مهلة البوابة لعنقود الرواتب ([Gateway appsettings.json:167-173](../src/Gateway/HRManagement.Gateway/appsettings.json)). ولا مهام خلفية للرواتب. وموصّل الخزينة Live مغلوب بموصّل الملف ([DependencyInjection.cs:38](../src/Services/Payroll/Payroll.Infrastructure/DependencyInjection.cs)).
- **→ دراسة الحركات (FUN-003):** الـ saga ترسل للرواتب أمر الطرف والحدث النهائي معاً ([TransferSecondmentStateMachines.cs:226-273](../src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs))، أي G-MOV-04/05. جانب الرواتب في G-PAY-13.
- **→ دراسة الإجازة والحضور (HCM-002):** إغلاق الحضور بلا حالة (G-LVA-03)، و`"Unpaid"` نصاً. جانب الرواتب في G-PAY-07/11/19.

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-PAY-1 | القيم القانونية لكل نظام: شرائح الضريبة والإعفاء والوعاء والتقريب (SYD-004)، نسب التأمين وسقفها وجهتها (SYD-005)، جداول 67/68 والحد الأدنى (SYD-003)، العطل، حد الاقتطاع من الراتب | لا دورة إنتاج قبل حزمة موقعة تُستورد نسخةَ إطار بمرجع لكل قيمة. القيم الحالية مختبرية فقط، والحزمة في `docs/syr` تُعتمد أو تُصحح | PAY-A، PAY-E |
| D-PAY-2 | هل يجوز صرف (كلي أو جزئي) قبل الترحيل؟ (PAY-001 يمنع، EXT-003 يسمح) | لا. الصرف الجزئي بعد الترحيل فقط، بيد المُرحِّل، عبر البوابة نفسها | PAY-F |
| D-PAY-3 | من يراجع الرواتب قبل الترحيل (سجل المراجعة)؟ | المراقب المالي أو التدقيق الداخلي (`auditor`) بقراءة الرواتب وإغلاق المراجعة، مستقلاً عن المعتمد. ويُسمح للجهة بإلغاء الخطوة بقرار موثق | PAY-D، PAY-F |
| D-PAY-4 | نطاق الحجز وحاجز نظام الخدمة: الدفعة كلها أم الموظف؟ | الموظف: يُستبعد ويُقيَّد متأخرات بتقرير، ويُصرف للباقين. وخيار الحجب الكلي إعداد للجهة | PAY-F |
| D-PAY-5 | عتبة الاعتماد المزدوج وسلسلة الاعتماد | عتبة لكل جهة بالليرة الجديدة (الحالية 50,000,000)، وما فوقها معتمدان مختلفان: مدير مالي ثم آمر صرف | PAY-F (→ WF) |
| D-PAY-6 | مصدر تغييرات الراتب: تحرير مباشر في الرواتب أم قرارات HR فقط؟ | من القرارات فقط (عقد، ترفيع، نقل، مرسوم). والتحرير المباشر استثناء بسبب ومدقق | PAY-A، PAY-G |
| D-PAY-7 | قاعدة نهاية الخدمة لكل نظام: مكافأة أم معاش عبر GOIP، بدل الإجازات، الضريبة | ربطها بالمرجع لكل نظام، ولا مكافأة افتراضية في الإنتاج | PAY-G |
| D-PAY-8 | سياسة الأثر الرجعي: حد الأشهر، آلي أم يدوي، فترة الضريبة | فروق آلية للفترات المرحّلة باعتماد، والحد 12 شهراً مؤقتاً، والضريبة في فترة الدفع كما في PAY-004 | PAY-H |
| D-PAY-9 | قناة الدفع وصيغة الملف لكل مصرف/بريد (SYD-014) | ملف لكل قناة بمجاميع ضبط وبصمة، وCSV الحالي مؤقتاً حتى توقيع المواصفات | PAY-I، PAY-J |
| D-PAY-10 | متى يرى الموظف قسيمته في ESS؟ | بعد الترحيل فقط | PAY-I |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة P0   PAY-A (الإطار والبيانات المرجعية) ║ PAY-B (الحساب المصرفي وملف الأجر) ║ PAY-C (آلة الحالة والتسويات)
            ثم PAY-D (إثبات دورة كاملة بإعدادات الإنتاج + CI أخضر)
الموجة P1   PAY-E (قواعد الحساب والقيم القانونية، بعد D-PAY-1) ║ PAY-F (حوكمة الصرف والأربع عيون والبوابات)
            PAY-G (الحركات ونهاية الخدمة) ║ PAY-H (القطع والتأخير والأثر الرجعي والحضور) ║ PAY-I (ما بعد الترحيل والقسائم)
الموجة P2   PAY-J (التكامل المالي والتشغيل)
مستمر       PAY-K (التحقق والتوثيق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) | خطة الموجة 1 |
|-------|---------|---------|----------|-----------|-------------------|--------------|
| PAY-A | الإطار القانوني والبيانات المرجعية في الإنتاج | G-PAY-01، G-PAY-10، بنية G-PAY-12 | P0 | — | 8–10 | [PLAN-54](acceptance-plans/PLAN-54-pay-legal-framework-reference-data.md) |
| PAY-B | الحساب المصرفي وتكامل ملف الأجر | G-PAY-02 | P0 | — | 5–6 | [PLAN-55](acceptance-plans/PLAN-55-pay-bank-account-pay-profile.md) |
| PAY-C | آلة حالات التشغيل والتسويات والإلغاء | G-PAY-03، G-PAY-04، G-PAY-06، جزء G-PAY-07 | P0 | — | 8–10 | [PLAN-56](acceptance-plans/PLAN-56-pay-run-state-machine.md) |
| PAY-D | إثبات دورة كاملة بإعدادات الإنتاج | G-PAY-05 | P0 | PAY-A، PAY-B، PAY-C | 5–7 | [PLAN-59](acceptance-plans/PLAN-59-pay-full-cycle-production-proof.md) |
| PAY-E | قواعد الحساب والقيم القانونية | G-PAY-11، G-PAY-12 | P1 | PAY-A، D-PAY-1 | 8–10 | «الموجة 2» |
| PAY-F | حوكمة الصرف: مسار واحد، أربع عيون حقيقية، استبعاد الموظف | G-PAY-08، G-PAY-09، G-PAY-15 | P1 | PAY-C، D-PAY-2..5 | 9–10 | [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md) |
| PAY-G | مدخلات الحركات ونهاية الخدمة | G-PAY-13، G-PAY-14 | P1 | PAY-C، PAY-E، FUN-003 | 7–9 | «الموجة 2»؛ حساب نهاية الخدمة من G-PAY-14 في [PLAN-64](acceptance-plans/PLAN-64-sep-final-settlement.md) |
| PAY-H | نقطة القطع والتغييرات المتأخرة والأثر الرجعي والحضور | باقي G-PAY-07، G-PAY-18، G-PAY-19 | P1/P2 | PAY-C، PAY-E | 9–10 | «الموجة 2»؛ جانب الحضور من G-PAY-07/19 في [PLAN-57](acceptance-plans/PLAN-57-lva-attendance-period-payroll-handoff.md) |
| PAY-I | ما بعد الترحيل: الإشعار والتسوية والمتأخرات والقسائم | G-PAY-16، G-PAY-17 | P1 | PAY-F | 7–9 | «الموجة 2»؛ التسجيل اليدوي لتأكيد القناة بشخصين في [PLAN-58](acceptance-plans/PLAN-58-pay-disbursement-governance.md) |
| PAY-J | التكامل المالي والتشغيل | G-PAY-20، G-PAY-21، G-PAY-23 | P2 | PAY-I | 8–10 | «الموجة 2» |
| PAY-K | التحقق والتوثيق | G-PAY-22 | P1 ← مستمر | كل الخطط | 4–6 | [PLAN-65](acceptance-plans/PLAN-65-wave1-verification-docs.md)؛ وAC-24/25 في PLAN-58، وإزالة SQL الرواتب في PLAN-59 |
| **المجموع** | | | | | **≈ 78–97** | |

**ربط الموجة 1 (2026-10-30):** نُقل العميل المزيف لأنظمة الخدمة في مضيف التكامل من PAY-D إلى PLAN-34 (ليُدفع `main` بـ CI أخضر)، و`LegalRegimeId` في بذرة القبول إلى PLAN-54 (المرحلة 0.7)، لأن كل خطط الرواتب تحسب في القبول. وبقي في PLAN-59 اختبار مسار الحجب وAC-12. وفصل واجبات الإطار (جزء من G-PAY-09) في PLAN-54، والباقي في PLAN-58. ومعاملة تغيير الراتب في PLAN-53 (G-IAM-02)، وتستهلكها PLAN-54 وPLAN-55 وPLAN-59. وفروق الحضور وبوابته في PLAN-57، وتستهلكها PLAN-56 وPLAN-58 وPLAN-59. وأُضيف إلى PLAN-58 تسجيل تأكيد القناة يدوياً بشخصين (جزء من G-PAY-16) لأن PLAN-64 يبني عليه «مدفوع».

**PAY-A — الإطار القانوني والبيانات المرجعية في الإنتاج (8–10 أيام).**
- `Create` لنسخة إطار من قالب، وتحرير الشرائح وتاريخ السريان في المسودة، واستيراد حزمة JSON موقعة.
- إطار لكل نظام قانوني يُختار بـ `RegimeCode`.
- الخصومات النظامية والعطل داخل النسخة والبصمة.
- قواعد أولوية الصرف ضمن الإطار.
- سياسات صريحة لكل POST، واختبار معماري.
- تغييرات الراتب والبدلات والخصومات طلبات بمدقق (D-PAY-6).
- القبول: AC-PAY-Aa (إنشاء ونشر وحساب على `Seed:Profile=None`)، AC-PAY-Ab (تغيير راتب بمدقق؛ 403 للأدوار الأخرى)، AC-PAY-Ac (تعديل نسبة نظامية ← نسخة وبصمة جديدتان).

**PAY-B — الحساب المصرفي وتكامل ملف الأجر (5–6 أيام).**
- تسجيل IBAN وتحقق بصانع ومدقق.
- نسخ ملف الأجر تنقل الوحدة والنظام والحساب.
- إصلاح تسلسل `IEmployeeCreated` ← `IContractActivated`.
- تثبيت الحساب في اللقطة.
- القبول: AC-PAY-Ba (حساب يبقى بعد تفعيل عقد، وملف خزينة يُنتج)، AC-PAY-Bb (المسجّل لا يحقق؛ صيغة خاطئة تُرفض).

**PAY-C — آلة حالات التشغيل والتسويات والإلغاء (8–10 أيام).**
- معادلة صافٍ واحدة، وترحيل لا يعدّل.
- التصحيح يمر بالتحقق ويقتصر على فترته.
- الرفض يفك التجميد.
- `CancelRun` لأي تشغيل غير مرحّل، يحرر المدخلات.
- حارس لكل انتقال.
- العكس طلب باعتماد عبر انتقالات الدفعة.
- القبول: AC-PAY-Ca (دورة بتسويات ± تنجح والمرحَّل = المعتمد)، AC-PAY-Cb (رفض ← تصحيح ← إعادة اعتماد؛ إلغاء يحرر الفترة)، AC-PAY-Cc (`validate` على معتمد/مرحّل يُرفض؛ عكس بمعتمد ثانٍ).

**PAY-D — إثبات دورة كاملة بإعدادات الإنتاج (5–7 أيام).**
- إعادة كتابة AC-12 كما في G-PAY-05.
- شخصية مراجعة مستقلة (D-PAY-3).
- `LegalRegimeId` في بذرة القبول.
- عميل أنظمة خدمة وهمي في مضيف التكامل، و14 اختباراً خضراء.
- رحلة HTTP على الملف الشبيه بالإنتاج من الإطار إلى ملف الخزينة، مع إغلاق فترة حضور واحدة قبل الحساب (مؤشر الإطلاق التجريبي).
- القبول: AC-PAY-Da، وAC-12 `Pass`.

**PAY-E — قواعد الحساب والقيم القانونية (8–10 أيام، بعد D-PAY-1).**
- حساب بشرائح زمنية لنسخ الملف.
- استبعاد خارج الفترة.
- ترحيل الخصم الزائد مع سقف قانوني.
- وعاء ضريبي وتأميني من الإطار، وسقف التأمين، وحصة صاحب العمل في الحساب.
- قاسم واحد من الإطار.
- تقسيط البدلات.
- استيراد الحزم الموقعة، ومنع «Interim» في الإنتاج.
- القبول: AC-PAY-Ea (الحالات الذهبية في G-PAY-11)، AC-PAY-Eb (حالات المالية لكل نظام bit-for-bit؛ رفض قيمة بلا مرجع).

**PAY-F — حوكمة الصرف (9–10 أيام).**
- مسار دفع واحد بعد الترحيل، والملف من الأوامر بمجاميع ضبط مرة واحدة.
- الصرف الجزئي للمُرحِّل (D-PAY-2).
- أربع عيون بطلبين من جلستين، للحجز والتنبيه والمتأخرات والإطار.
- الاعتماد المزدوج فوق العتبة.
- استبعاد الموظف بدل حجب الدفعة (D-PAY-4).
- مدخلات احتيال حقيقية.
- القبول: AC-PAY-Fa (تمويل جزئي متسق)، AC-PAY-Fb (إفراج بمستخدمين)، AC-PAY-Fc (99 من 100 يُصرفون).

**PAY-G — مدخلات الحركات ونهاية الخدمة (7–9 أيام).**
- مستهلكات بمفتاح عمل وتعويض.
- الفرق نسخةَ ملف بتاريخ نفاذ.
- مستهلكات للترفيع والإعارة وتغيير الحالة والحجز القضائي.
- بدء الخدمة من HR.Core.
- تشغيل `FinalSettlement`.
- تسوية السلف مرة واحدة.
- تسوية نهاية خدمة محسوبة في الخادم، بفصل واجبات، وتُصرف بأمر.
- القبول: AC-PAY-Ga، AC-PAY-Gb.

**PAY-H — القطع والتأخير والأثر الرجعي والحضور (9–10 أيام).**
- كيان فترة رواتب بتاريخ قطع.
- توجيه المتأخر إلى الدورة التالية ببنود رجعية.
- إعادة اللقطة عند التعارض.
- إرجاع خصم إجازة ملغاة.
- محرك فروق رجعية (D-PAY-8) ومراتب وعلاوة دورية.
- ربط إغلاق الحضور، والإضافي والمناوبات.
- القبول: AC-PAY-Ha، AC-PAY-Hb، AC-PAY-Hc، AC-PAY-Hd.

**PAY-I — ما بعد الترحيل والقسائم (7–9 أيام).**
- استيراد إشعار القناة وكشف المصرف، و`Ack`/`Settle`/`Reject` لكل أمر.
- مطابقة بالمرجع.
- سداد المتأخرات في الدورة التالية بمفتاح دائم.
- حدث الترحيل والعكس في المعاملة.
- ESS للمرحَّل فقط مع PDF مباشر، وحالة «أُتيحت» وإشعار.
- القبول: AC-PAY-Ia، AC-PAY-Ib.

**PAY-J — التكامل المالي والتشغيل (8–10 أيام، P2).**
- قيد محاسبي بخريطة حسابات وترميز موازنة.
- تقرير ضريبة شهري، واشتراكات GOSI/GOIP.
- موصّل خزينة قابل للضبط.
- عامل خلفي للحساب، ومراقب عقود إيجار، ومهام شهادة الحياة والإشعارات، ومهلة صريحة في البوابة.
- تحسينات P3.
- القبول: AC-PAY-Ja، AC-PAY-Jb.

**PAY-K — التحقق والتوثيق (4–6 أيام، مستمر).**
- تصحيح PAY-001/003/004 وFRD-001 وSYR-005/006 وPLAN-10 وNAT-003 لتطابق الشيفرة.
- حذف نسخة `ArrearsLedgerEntry` من `SharedKernel`.
- إعادة كتابة AC-24/25.
- اختبار معماري يمنع `UPDATE`/`INSERT` في سكربتات القبول، وتوسيع اختبار PAY-001.
- تشغيل AC-21..25 بأدلة كاملة.
- القبول: AC-PAY-Ka (كل ادعاء بدليل `file:line`)، وAC-21..25 `Pass`.

**ملاحظة على التقدير:** الأرقام لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين: D-PAY-1 (الحزم القانونية من وزارة المالية)، ونتيجة تشغيل 14 اختبار تكامل بعد إصلاح المضيف، وحجم الجهة الأولى (مهلة الحساب).

---

## 7. معيار «اكتمال الرواتب» المقترح

تُعدّ الرواتب جاهزة لإصدار v1 عندما تتحقق البنود الخمسة الآتية:

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-PAY-01…G-PAY-17 وG-PAY-22)، أو يؤجَّل بعضها بقرار موثّق من صاحب العمل.
- [ ] **2. تنجح ثلاث دورات متتالية على الملف الشبيه بالإنتاج** (`IsLabScale=false`، `Seed:Profile=None`) عبر HTTP/الواجهة وحدها، بلا أي SQL. المسار: إطار مُنشأ من الواجهة بقيم موقعة ← موظفون بحسابات محققة ← إغلاق فترة حضور ← حساب ← تحقق ← إرسال ← اعتماد ← مراجعة مستقلة ← ترحيل ← معالجة دفعة ← ملف خزينة بمجاميع ضبط ← استيراد إشعار التسوية ← قسائم في ESS. وتشمل الدورات حالةَ رفض ثم تصحيح، وتغييراً رجعياً واحداً، ونقلاً بفرق راتب، وانفصالاً بتسوية نهائية.
- [ ] **3. AC-12 وAC-21..25 `Pass`** تحت `ACCEPTANCE_REQUIRE_ALL=true` بالعناصر الثمانية لكل مجلد، وسيناريوهاتها السالبة تثبت السلوك الصحيح (لا صرف قبل الترحيل، ولا إفراج بمستخدم واحد). واختبارات تكامل الرواتب خضراء في الـ CI.
- [ ] **4. حالات ذهبية من وزارة المالية لكل نظام قانوني** تُعاد bit-for-bit، وبصمة الإطار تشمل الضريبة والتأمين والعطل والتقريب.
- [ ] **5. الوثائق تطابق الشيفرة:** PAY-001، وPAY-003، وPAY-004، وFRD-001، وSYR-005/006، وPLAN-06/10، وNAT-003. كل حالة «Done» تحمل دليلاً حياً أو تُخفَّض.

# تحقق مستقل — PRG_001_QUALITY_GAP_STUDY

**المرجع:** نسخة `/home/claude/hrm` (main @ `f9eab1a`)، قراءة ثابتة بلا بناء ولا تشغيل. أُعيد تطبيق منطق `BolaArchitectureTests` و`Ten002ArchitecturalTests` بـ Python على النسخة (بنهايات CRLF وLF).

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-QA-01 | P0 | مؤكد | ملف الحماية (مراجعتان، توقيع، `enforce_admins`، الفحوص `CI/CodeQL/Gitleaks`) والمدقّق `:38` وCODEOWNERS `@omard` والدليل التشغيلي `:3,22` كلها كما وُصفت. عُدّت 35 ملف `.cs` بلا سطر نهائي. رقم «140 commit» لا يُتحقق منه (لا `.git`). الموثّق في المستودع 128 قبل PLAN-25 (`plan25-final-report.md:211`). ملكية `@omard` تحتاج `gh api`. |
| G-QA-02 | P0 | مؤكد | `EX-STR005-SIM-INCIDENT-WINDOW` بحالة `Declared` وينتهي 2026-09-15. المدقّق يخرج بـ 1، وخطوته هي السابعة عشرة من 89 قبل `Restore`. المصفوفة تُنسخ إلى الصورة (`ServiceDefaults.csproj:56`، و`.dockerignore` يُبقي `docs/*.json`)، فـ`EnforceOnStartup=true` يرمي استثناءً في Production/Staging للبوابة وHR.Core والرواتب. الاستثناء الثاني ينتهي 2026-10-30، أي يصبح منتهياً من 2026-10-31. |
| G-QA-03 | P0 | مؤكد | تعبير BOLA أعطى بالضبط الإجراءات الـ12 المذكورة. Ten002 يلتقط الملفين المذكورين فقط. Syr001: `LegislationRegisterProvider(HRCoreDbContext)` غير مسجَّل. فجوتا DI (`PublishSeparationHandler` و`ProcessPartyAckConsumers`) مؤكدتان، و`Mock.Of` في Moq 4.20 يعيد `null` لـ`IReadOnlyList`. انظر الملاحظة الجديدة 1 عن `global.json`. |
| G-QA-04 | P0 | مؤكد | `packages.lock.json:868-878` صحيح، والسكربت يفشل على أي High، و`supply-chain-exceptions.json` فارغ. خطوة البناء في CI بلا `-warnaserror`، فهو بوابة محلية فقط، كما تقول الدراسة. |
| G-QA-05 | P0 | مؤكد | فشل السحب موثّق في المستودع: `plan30-final-report.md:296-298`، و`PLAN-30-EXECUTION.md:30`، و`docker-compose.acceptance.yml:29`. وظائف CI تستخدم compose الأساسي بلا الالتفاف. الفشل رُصد على جهاز المطوّر، والسحب على مشغّلات GitHub غير مثبت، والدراسة تقول ذلك. |
| G-QA-06 | P0 | مؤكد | ملف Demo يولّد `hradmin` فقط. `realm-acceptance.json` فيه 0 مستخدم، والدمج يضيف 9 مستخدمين `e2e-*` فقط. لا `COMPOSE_PROJECT_NAME` في `.env.ci` ولا في الوظيفة. الكتالوج 61 والاختبار يتوقع 60. لا حارس صفر تخطٍّ، ولا وسم `[RequiredAcceptance]` في `Ac0*`–`Ac3*`. تفصيل صغير: `AcceptanceIdentityPopulationTests` من نوع `[SkippableFact]` ومحروس بـ`RUN_ACCEPTANCE` لا `[Fact]`، والأثر لا يتغير لأن الوظيفة تضبطه `true`. |
| G-QA-07 | P0 | مؤكد | السطر 149 يحوي البايتين `\` و`n` حرفياً (فُحص بـ `od -c`). |
| G-QA-08 | P1 | مؤكد جزئياً | الادعاءات الأساسية صحيحة: سبب «Pass» لـ AC-34/36 تعاقدي من `9cb3705` بتاريخ 2026-08-24. وAC-12 لا يستدعي `calculate`، و`CreatePayrollRunHandler` لا يحسب. أما بند `WriteMergedResult` فقديم: مجلدات AC-HCM تحتفظ بخريطة `scenarios` منذ PLAN-33، وفقد `Ac27_f_g` سببه استعادة git. صُحّح البند. |
| G-QA-09 | P1 | مؤكد | الأنماط كلها موجودة. صُحّحت أسطر Ac17 (59 و73 صارت 60 و74). |
| G-QA-10 | P1 | مؤكد | عُدّت 47 حالة `Journey` = 22+7+9+2+6+1. الأسطر المذكورة صحيحة. |
| G-QA-11 | P1 | مؤكد | `upload: false` في `ci.yml:522` و`codeql.yml:42`. سكربت Dependabot يعيد `echo '[]'` عند الفشل (`:12`). أسطر gateway-ci (`:38-39,42,94`) وmutation (`:19,26`) صحيحة. |
| G-QA-12 | P1 | مؤكد | لا ضبط للثقافة على مستوى المشروع. بعض الاختبارات تضبطها محلياً (Hcm029/031/032). |
| G-QA-13 | P1 | مؤكد | 74 وثيقة فيها «ReadyForSign-off». `test-count-registry.json` بتاريخ 2026-08-01 وفيه 2211. `VERIFICATION-COMPLETION.md:54` يذكر `acceptance-live`. |
| G-QA-14 | P2 | أسطر صحيحة | 113 ملفاً، منها 47 على SQLite و24 تبني DI يدوياً. `:29-33` صحيح. |
| G-QA-15 | P3 | أسطر صحيحة | `e2e-journeys.yml:6` (cron). |

## التعديلات على الدراسة
1. G-QA-08: صُحّح بند `WriteMergedResult`. الكتابة فوق `status` تقع فقط حين لا يُضبط `ScenarioKey` (مجلدات AC-01..37)، ومجلدات AC-HCM تحتفظ بـ`scenarios`، وفقد `Ac27_f_g` سببه استعادة git. السطر صار `:96-100,206-232`، وأُلحقت ملاحظة التصحيح بسطر الأثر.
2. G-QA-09 وجدول §4.5 (صف AC-17): أسطر Ac17 من `:59,73` إلى `:60,74`.

## ملاحظات جديدة (لم تُضف إلى الدراسة)
1. `global.json` في النسخة فيه `"rollForward": "latestPatch"`، و`san001-validate.ps1:63-64` يتطلب `disable`. إن كانت النسخة مطابقة للـ commit فـ`San001SanctionsTests.Validate_script_passes` سيكون إخفاقاً حتمياً سادساً على Linux، لا «أخضر» كما في صف 9 من G-QA-03. يُحسم بـ `git show f9eab1a:global.json`.
2. المصفوفة تُقرأ وقت الإقلاع من مخرجات البناء. أي تجديد للاستثناء يتطلب إعادة بناء الصورة، لا مجرد تعديل ملف على الخادم، إلا مع `ProgramGovernance:MatrixPath`.

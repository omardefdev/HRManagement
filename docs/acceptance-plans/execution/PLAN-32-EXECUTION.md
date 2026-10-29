# PLAN-32-EXEC — تقارير ومؤشرات التوظيف (HCM-032)

تنفيذ [PLAN-32](../PLAN-32-hcm-recruitment-analytics.md) لإثبات **AC-HCM-32a..d**. سجل التقدم والقرارات:
[`reports/plan32-progress.md`](../../../reports/plan32-progress.md)، والتقرير الختامي
[`reports/plan32-final-report.md`](../../../reports/plan32-final-report.md)، وتعريفات المؤشرات:
[`docs/STR_007_KPI_DEFINITIONS.md`](../../STR_007_KPI_DEFINITIONS.md).

## المتطلبات

- **الصور:** تتغير `hr-core-web` (الشاشة والتصدير وفئة سبب رفض العرض والفهارس) و`dev-analytics-web` (جدول
  `RecruitmentRequisitionFacts` وبطاقة «التوظيف»). الترحيلات إضافية فقط وتُطبَّق عند الإقلاع:
  `HCM032_OfferDeclineCategory` (عمود اختياري) و`HCM032_AnalyticsIndexes` (فهرسان) في HR.Core، و`ANA032_RecruitmentRequisitionFacts`
  (جدول جديد مع تشديد RLS) في Dev.Analytics. لا تغيير على البوابة ولا على Keycloak ولا على الشخصيات (بصمة الحزمة كما هي).
- **إعادة بناء الخدمتين فقط على مكدّس قائم** (من جذر المستودع، PowerShell):

  ```powershell
  $env:COMPOSE_PROJECT_NAME = "hrm-acceptance"
  . ./scripts/acceptance-local-env.ps1; Import-AcceptanceLocalEnvironment -RepoRoot (Get-Location).Path
  powershell -ExecutionPolicy Bypass -File scripts/docker-generate-build-manifest.ps1
  $f = "-f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml".Split(' ')
  docker compose -p hrm-acceptance @f --profile full-stack build hr-core-web dev-analytics-web
  docker compose -p hrm-acceptance @f --profile full-stack up -d --no-deps --no-build --wait hr-core-web dev-analytics-web
  ```

  أو المكدّس كاملاً: `powershell -ExecutionPolicy Bypass -File scripts/acceptance-up.ps1`.
- **بيانات القبول (`AcceptanceRecruitmentReportsPatch`):** تعمل عند كل إقلاع بعد `AcceptanceCompetitionPatch`، بمعرّفات ثابتة
  (`AcceptanceSeedIds` 0801–0804) ومناصب مخصصة `AC32-POS-*`، وتُضاف مرة واحدة فقط:
  - `REQ-2026-A-AC32-1` (ريف دمشق، نطاق br1-mgr): مغلق بتعيين؛ ستة طلبات بسجل أحداث صريح؛ القمع 6، 4، 3، 3، 3، 3، 2، 1، 1؛ زمن الملء
    37 يوماً (اعتماد 2026-08-04 ← تعيين 2026-09-10)؛ عرض مقبول وعرض مرفوض بفئة «الراتب»؛ استئناف مرفوض.
  - `REQ-2026-A-AC32-2` (حلب، خارج نطاق br1-mgr): منشور، ثلاثة طلبات.
  - `REQ-2026-A-AC32-3` (ريف دمشق): بانتظار الاعتماد منذ 2026-09-01 في الخطوة الأولى من RECRUITMENT-REQUISITION-APPROVAL — متجاوز مهلة 48 ساعة.
  - أرقام وطنية من 11 خانة `320320000NN` لإثبات خلو التصدير منها.
  الاختبارات قراءة فقط (GET) فلا تغيّر البيانات؛ إن اعتُمد طلب الشاغر الثالث يدوياً فـ `scripts/acceptance-reset.ps1` يعيده.
- **سر عميل البوابة للاختبارات:** `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET` من `.env` مع `RUN_ACCEPTANCE` و`GATEWAY_BASE_URL` و`KEYCLOAK_BASE_URL`.

## الأوامر

```powershell
powershell -ExecutionPolicy Bypass -File scripts/hcm032-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm032"
powershell -ExecutionPolicy Bypass -File perf/hcm032-recruitment-analytics-scale.ps1     # Docker؛ الميزانية 3 ث / 1.5 ث
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcHcm32"
```

الأدلة في `acceptance-evidence/AC-HCM-32/` (metadata، steps، http، db-checks، timing، result).

## الشخصيات

| الشخصية | الدور | الاستعمال |
|---------|-------|-----------|
| `central-a` | hr-core (HrCoreWrite)، بلا قيد نطاق | 32a، 32c (Excel وCSV بالأسماء)، 32d |
| `br1-mgr` | branch-manager، نطاق فرع ريف دمشق | 32b |
| `a-executive-reader` | hr-core-executive (قراءة فقط) | 32c سلبي (CSV بلا أسماء) |

## خطوات يدوية (الواجهة)

1. ادخل بـ `central-a` ← القائمة «التوظيف والاستقطاب» ← «تقارير التوظيف». الفترة الافتراضية آخر 12 شهراً كاملاً (UTC).
2. اختر الفترة 2026-08-01 حتى 2026-09-30 واضغط «تطبيق». في «طلبات الشاغر» افتح `REQ-2026-A-AC32-1`: جدول «القمع» 6، 4، 3، 3، 3، 3، 2،
   1، 1، وفي الرأس «زمن الملء 37». البطاقة «زمن الملء (أيام)» في صفحة التقرير لهذا الطلب: الوسيط 37، n = 1، التغطية 100%.
3. «الاعتمادات المتأخرة»: يظهر `REQ-2026-A-AC32-3` (اعتماد طلب شاغر، الخطوة 1)، بعمر يتجاوز المهلة 48 ساعة («السياسة الافتراضية»).
4. «Excel» و«PDF» و«تفاصيل الطلبات (CSV)»: لا رقم وطني في أي ملف؛ CSV يتضمن عمود «CandidateName» لـ `central-a` فقط.
5. ادخل بـ `a-executive-reader`: نفس الشاشة دون أسماء المرشحين في صفحة طلب الشاغر (مرجع الطلب بدل الاسم)، وCSV بلا عمود الاسم.
6. ادخل بـ `br1-mgr`: لا يظهر `REQ-2026-A-AC32-2` ولا لوحة «المسابقات»؛ اختيار وحدة «فرع حلب» غير متاح في القائمة، وطلبها
   مباشرة (`?OrgUnitId=…0104`) يعرض «لا بيانات»؛ صفحة `REQ-2026-A-AC32-2` تعيد 404.
7. رفض عرض من «خطابات العروض» (HR نيابة عن المرشح) أو من `/careers/my`: حقل «فئة سبب الرفض» إلزامي.
8. Dev.Analytics ← «لوحات ومؤشرات»: بطاقة «التوظيف» (طلبات الشاغر المغلقة في آخر 12 شهراً) والأعداد من 1 إلى 4 تظهر «<5».

## الإعداد (`Recruitment:Analytics:*`)

| المفتاح | الافتراضي | ملاحظة |
|---------|-----------|--------|
| `StalePublishedDays` | 60 | طلب منشور أقدم من ذلك يظهر في «الشواغر المفتوحة» |
| `DefaultApprovalSlaHours` | 48 | قيمة سياسة تنظيمية مؤقتة، ليست قيمة قانونية |
| `ApprovalSlaHours:{WorkflowCode}` | — | مثل `ApprovalSlaHours:RECRUITMENT-OFFER-APPROVAL = 24`؛ `TimeoutHours` للخطوة يتقدم إن ضُبط |
| `MinCellSize` | 5 | عتبة حجب الخلايا الصغيرة (STR-007) |
| `DefaultPeriodMonths` | 12 | الفترة الافتراضية للتقرير |

لا قيمة مختلفة في Staging أو Production أو القبول (الإعداد الافتراضي في الكود).

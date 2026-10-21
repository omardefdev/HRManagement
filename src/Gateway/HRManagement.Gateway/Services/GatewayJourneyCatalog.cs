using HRManagement.Gateway.Models;

namespace HRManagement.Gateway.Services;

public interface IGatewayJourneyCatalog
{
    IReadOnlyList<GatewayJourneyDefinition> Journeys { get; }
    GatewayJourneyDefinition? FindById(string id);
}

public sealed class GatewayJourneyCatalog : IGatewayJourneyCatalog
{
    public IReadOnlyList<GatewayJourneyDefinition> Journeys { get; } =
    [
        new()
        {
            Id = "new-hire",
            Title = "مسار التوظيف الجديد",
            Description = "من الشاغر الممول والاختيار حتى التعيين وإكمال التمكين التشغيلي عبر الخدمات.",
            Category = "الدورة الوظيفية",
            Steps =
            [
                new()
                {
                    Title = "التحقق من المنصب الممول",
                    Description = "اختيار منصب شاغر (Vacant) مرتبط بالهيكل والدرجة قبل فتح طلب الشاغر.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/RecruitmentRequisitions/Create",
                    PhaseKey = "recruitment",
                    PhaseTitleAr = "مرحلة 1 — التوظيف (HCM-001)",
                    Note = "EST-001: لا إعلان بدون منصب ممول."
                },
                new()
                {
                    Title = "طلب الشاغر",
                    Description = "إنشاء مسودة طلب شاغر وربطها بالمنصب وتقديمها للاعتماد.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/RecruitmentRequisitions",
                    PhaseKey = "recruitment"
                },
                new()
                {
                    Title = "نشر الإعلان",
                    Description = "نشر عام (Publish) و/أو نشر داخلي (PublishInternal) حسب قناة الطلب.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/RecruitmentRequisitions",
                    PhaseKey = "recruitment"
                },
                new()
                {
                    Title = "التقديم عبر بوابة المواطن",
                    Description = "استقبال طلبات المرشحين الخارجيين على الوظائف المنشورة.",
                    GatewayPublicPath = "/careers",
                    PhaseKey = "recruitment",
                    Note = "مسار عام — لا يتطلب تسجيل HR."
                },
                new()
                {
                    Title = "إدارة طلبات HR",
                    Description = "إنشاء ومتابعة طلبات التوظيف الداخلية المرتبطة بطلبات شاغر منشورة.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/RecruitmentApplications",
                    PhaseKey = "recruitment"
                },
                new()
                {
                    Title = "إعلانات HR للمواطنين",
                    Description = "مراجعة طلبات المواطنين وتحويل المختصرين إلى مسار HR (PromoteFromCitizen).",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/PublicJobPostings",
                    PhaseKey = "recruitment"
                },
                new()
                {
                    Title = "القناة الداخلية (HR)",
                    Description = "متابعة الترشّحات الداخلية وتحويلها إلى مسار الاختيار (PromoteFromInternal).",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/TalentMarketplace",
                    PhaseKey = "recruitment",
                    IsRequired = false
                },
                new()
                {
                    Title = "ترشّح ESS",
                    Description = "بوابة الموظف للترشّح على الشواغر الداخلية المنشورة.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/EssTalent",
                    PhaseKey = "recruitment",
                    IsRequired = false,
                    Note = "يتطلب صلاحية ModuleEss."
                },
                new()
                {
                    Title = "الاختيار والتقييم",
                    Description = "COI، درجة التقييم، الترتيب، والانتقال بين حالات الاختيار.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/RecruitmentApplications",
                    PhaseKey = "recruitment"
                },
                new()
                {
                    Title = "لجنة الاختيار",
                    Description = "جلسة لجنة مرتبطة بالطلب مع أعضاء وتعارض مصالح.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/SelectionCommittees",
                    PhaseKey = "recruitment",
                    IsRequired = false
                },
                new()
                {
                    Title = "خطاب العرض",
                    Description = "إصدار خطاب العرض واعتماده ثم قبول المرشح.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/OfferLetters",
                    PhaseKey = "recruitment"
                },
                new()
                {
                    Title = "التعيين وإغلاق الشاغر",
                    Description = "تنفيذ Hire بعد قبول العرض — شغل المنصب وإغلاق الطلب.",
                    ModuleSlug = "hr-core",
                    UiPath = "/ui/hr-core/RecruitmentApplications",
                    PhaseKey = "recruitment",
                    Note = "زر التعيين يظهر فقط عند OfferAccepted."
                },
                new()
                {
                    Title = "معالج التعيين",
                    Description = "قائمة التحقق واعتماد معاملة التعيين.",
                    ModuleSlug = "hr-contracts",
                    UiPath = "/ui/hr-contracts/HireWizard",
                    PhaseKey = "onboarding",
                    PhaseTitleAr = "مرحلة 2 — التمكين التشغيلي (FUN-001)"
                },
                new() { Title = "العقود", Description = "إصدار/تفعيل العلاقة التعاقدية.", ModuleSlug = "hr-contracts", PhaseKey = "onboarding" },
                new() { Title = "الرواتب", Description = "ربط الدرجة وجدولة الأجر.", ModuleSlug = "payroll", PhaseKey = "onboarding" },
                new() { Title = "بطاقة الهوية", Description = "طلب وإصدار وتسليم البطاقة.", ModuleSlug = "idcard", PhaseKey = "onboarding" },
                new() { Title = "أرشفة التعيين", Description = "حفظ ملف التعيين في الأرشيف المؤسسي.", ModuleSlug = "records-archive", PhaseKey = "onboarding" },
                new() { Title = "الدليل والهوية", Description = "إنشاء حساب وربطه بالموظف.", ModuleSlug = "auth-directory", PhaseKey = "onboarding" },
                new() { Title = "تمكين تقنية المعلومات", Description = "حسابات وبريد وتجهيزات.", ModuleSlug = "it-infra", PhaseKey = "onboarding" },
                new() { Title = "التدريب التعريفي", Description = "إلحاق ببرامج التدريب الأساسية.", ModuleSlug = "hr-training", PhaseKey = "onboarding", IsRequired = false },
                new() { Title = "الفحص الصحي", Description = "تنفيذ الفحص عند الحاجة التنظيمية.", ModuleSlug = "hr-health", PhaseKey = "onboarding", IsRequired = false }
            ]
        },
        new()
        {
            Id = "employee-transfer",
            Title = "مسار النقل",
            Description = "إدارة النقل بين الوحدات مع تحديث كافة الأنظمة ذات الصلة.",
            Category = "الدورة الوظيفية",
            Steps =
            [
                new() { Title = "التنقلات", Description = "إنشاء واعتماد طلب النقل.", ModuleSlug = "hr-movements" },
                new() { Title = "النواة", Description = "تحديث الانتماء التنظيمي والسجل الوظيفي.", ModuleSlug = "hr-core" },
                new() { Title = "العقود", Description = "تعديل الشروط إن تأثرت بالنقل.", ModuleSlug = "hr-contracts", IsRequired = false },
                new() { Title = "الرواتب", Description = "تحديث الأثر المالي للنقل.", ModuleSlug = "payroll" },
                new() { Title = "اللوجستيات", Description = "تسليم/استرداد العهدة المرتبطة بالجهة.", ModuleSlug = "admin-logistics" },
                new() { Title = "الدليل", Description = "تحديث الأدوار والصلاحيات حسب الجهة.", ModuleSlug = "auth-directory" }
            ]
        },
        new()
        {
            Id = "promotion-change",
            Title = "مسار الترقية أو تغيير الوظيفة",
            Description = "تنفيذ قرارات الترقية/التغيير وتحديث آثارها الوظيفية والمالية.",
            Category = "الدورة الوظيفية",
            Steps =
            [
                new() { Title = "النواة", Description = "تحديث الوظيفة والدرجة ضمن السجل.", ModuleSlug = "hr-core" },
                new() { Title = "العقود/القرارات", Description = "تثبيت القرار الإداري المرتبط.", ModuleSlug = "hr-contracts" },
                new() { Title = "الرواتب", Description = "تطبيق الأثر على التعويضات.", ModuleSlug = "payroll" },
                new() { Title = "التدريب", Description = "تأهيل المتطلبات الوظيفية الجديدة.", ModuleSlug = "hr-training", IsRequired = false }
            ]
        },
        new()
        {
            Id = "leave-and-advance",
            Title = "مسار الإجازات والسلف",
            Description = "ربط طلبات الإجازة والسلف بالسياسات والتأثير المالي.",
            Category = "الخدمة والالتزامات",
            Steps =
            [
                new() { Title = "العقود والسياسات", Description = "اعتماد الطلب وفق السياسة.", ModuleSlug = "hr-contracts" },
                new() { Title = "الرواتب", Description = "تطبيق أثر الطلب في الدورة المالية.", ModuleSlug = "payroll" }
            ]
        },
        new()
        {
            Id = "separation-exit",
            Title = "مسار الفصل أو الاستقالة",
            Description = "إقفال دورة الخدمة واستكمال الالتزامات النظامية عبر الأنظمة.",
            Category = "إنهاء الخدمة",
            Steps =
            [
                new() { Title = "العقود", Description = "توثيق الإنهاء وسببه وتاريخه.", ModuleSlug = "hr-contracts" },
                new() { Title = "الرواتب", Description = "تسوية المستحقات ونهاية الخدمة.", ModuleSlug = "payroll" },
                new() { Title = "الأرشيف", Description = "أرشفة الوثائق وفق سياسة الاحتفاظ.", ModuleSlug = "records-archive" },
                new() { Title = "الدليل", Description = "تعطيل الحسابات أو تعديل حالتها.", ModuleSlug = "auth-directory" },
                new() { Title = "البطاقات", Description = "تعطيل البطاقة المؤسسية.", ModuleSlug = "idcard" },
                new() { Title = "اللوجستيات", Description = "استرداد العهدة والأصول.", ModuleSlug = "admin-logistics" },
                new() { Title = "تقنية المعلومات", Description = "إيقاف وصول الأنظمة التقنية.", ModuleSlug = "it-infra" },
                new() { Title = "النواة", Description = "إغلاق الحالة في الملف الوظيفي.", ModuleSlug = "hr-core" }
            ]
        },
        new()
        {
            Id = "medical-recommendations",
            Title = "مسار التوصيات الطبية",
            Description = "تنفيذ مخرجات الصحة المهنية وربطها بالأثر الوظيفي.",
            Category = "الصحة والامتثال",
            Steps =
            [
                new() { Title = "الصحة", Description = "إصدار التوصية الطبية والقرار الصحي.", ModuleSlug = "hr-health" },
                new() { Title = "النواة", Description = "تحديث القيود الوظيفية المعتمدة.", ModuleSlug = "hr-core" },
                new() { Title = "الامتثال", Description = "تصعيد الحالة إذا كانت ذات أثر انضباطي.", ModuleSlug = "compliance", IsRequired = false }
            ]
        },
        new()
        {
            Id = "grievances-compliance",
            Title = "مسار الشكاوى والامتثال",
            Description = "إدارة التظلمات والمخالفات حتى القرار النهائي.",
            Category = "الامتثال والحوكمة",
            Steps =
            [
                new() { Title = "الامتثال", Description = "تسجيل الشكوى والتحقيق وإصدار القرار.", ModuleSlug = "compliance" },
                new() { Title = "العقود/التنظيم", Description = "تطبيق الأثر الإداري حسب نوع الحالة.", ModuleSlug = "hr-contracts", IsRequired = false }
            ]
        },
        new()
        {
            Id = "identity-access",
            Title = "مسار إدارة الهوية والوصول",
            Description = "إدارة إنشاء وربط وتفعيل/تعطيل الحسابات والصلاحيات.",
            Category = "الأمن والوصول",
            Steps =
            [
                new() { Title = "الدليل", Description = "إنشاء المستخدم وربطه بالموظف.", ModuleSlug = "auth-directory" },
                new() { Title = "تقنية المعلومات", Description = "تنفيذ التفويضات التقنية وتدقيق الوصول.", ModuleSlug = "it-infra" }
            ]
        },
        new()
        {
            Id = "id-card-lifecycle",
            Title = "مسار البطاقات والهويات",
            Description = "إصدار وتجديد وتعطيل بطاقات الهوية المؤسسية.",
            Category = "الهوية المؤسسية",
            Steps =
            [
                new() { Title = "البطاقات", Description = "إدارة طلب الإصدار أو التجديد أو الإبطال.", ModuleSlug = "idcard" },
                new() { Title = "النواة", Description = "التحقق من بيانات الموظف المرجعية.", ModuleSlug = "hr-core" }
            ]
        },
        new()
        {
            Id = "records-archive-lifecycle",
            Title = "مسار الأرشفة وإدارة السجلات",
            Description = "إدارة دورة حياة وثائق الموظف من الإيداع حتى الإتلاف الآمن.",
            Category = "السجلات والوثائق",
            Steps =
            [
                new() { Title = "الأرشيف", Description = "أرشفة واستعارة وإدارة مدد الاحتفاظ.", ModuleSlug = "records-archive" },
                new() { Title = "النواة", Description = "الرجوع للمرجع الوظيفي عند المطابقة.", ModuleSlug = "hr-core", IsRequired = false }
            ]
        },
        new()
        {
            Id = "asset-custody-logistics",
            Title = "مسار العهدة واللوجستيات",
            Description = "إدارة الأصول المسلمة واستردادها والصيانة المرتبطة.",
            Category = "الأصول والدعم",
            Steps =
            [
                new() { Title = "اللوجستيات", Description = "تسليم/استرداد العهدة والصيانة.", ModuleSlug = "admin-logistics" },
                new() { Title = "النواة", Description = "التحقق من ارتباط العهدة بالموظف.", ModuleSlug = "hr-core", IsRequired = false }
            ]
        },
        new()
        {
            Id = "it-enablement",
            Title = "مسار تمكين تقنية المعلومات",
            Description = "إدارة الحسابات والأجهزة والدعم الفني المرتبط بالموظف.",
            Category = "الدعم التقني",
            Steps =
            [
                new() { Title = "تقنية المعلومات", Description = "إنشاء حسابات وتجهيز أجهزة وتذاكر دعم.", ModuleSlug = "it-infra" },
                new() { Title = "الدليل", Description = "مواءمة الدور والوصول مع سياسة الهوية.", ModuleSlug = "auth-directory" }
            ]
        },
        new()
        {
            Id = "analytics-kpi",
            Title = "مسار التقارير والمؤشرات",
            Description = "استخراج مؤشرات الموارد البشرية والتقارير متعددة المجالات.",
            Category = "التحليلات",
            Steps =
            [
                new() { Title = "التحليلات", Description = "لوحات KPI وتقارير مركبة.", ModuleSlug = "dev-analytics" },
                new() { Title = "المصادر التشغيلية", Description = "الرجوع لمصادر البيانات الأصلية للتحقق.", ModuleSlug = null, IsRequired = false, Note = "خطوة إرشادية تعتمد على سياسة التقارير." }
            ]
        }
    ];

    public GatewayJourneyDefinition? FindById(string id) =>
        Journeys.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
}

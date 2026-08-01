namespace HRManagement.Integration.Abstractions;

/// <summary>إعدادات طبقة التكامل الخارجي (المستوى الثاني).</summary>
public sealed class ExternalIntegrationsOptions
{
    public const string SectionName = "ExternalIntegrations";

    /// <summary>File | Stub | Live — الافتراضي File للعرض والتشغيل دون جهات حكومية.</summary>
    public IntegrationMode Mode { get; set; } = IntegrationMode.File;

    /// <summary>مجلد الملفات الصادرة/الواردة (نسبي لجذر التطبيق أو مسار مطلق).</summary>
    public string OutputDirectory { get; set; } = "integration-outbox";

    /// <summary>في وضع Stub: هل يُقبل أي رقم وطني يبدأ بهذا البادئة؟</summary>
    public string StubNationalIdAcceptedPrefix { get; set; } = "01";
}

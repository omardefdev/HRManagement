namespace HRManagement.Integration.Abstractions;

/// <summary>وضع تشغيل الموصّلات الخارجية: ملفات صادرة، محاكاة، أو ربط حي (لاحقاً).</summary>
public enum IntegrationMode
{
    File = 0,
    Stub = 1,
    Live = 2
}

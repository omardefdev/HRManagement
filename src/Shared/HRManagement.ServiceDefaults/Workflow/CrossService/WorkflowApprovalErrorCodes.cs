namespace HRManagement.ServiceDefaults.Workflow.CrossService;

public static class WorkflowApprovalErrorCodes
{
    /// <summary>422: no published definition for the code.</summary>
    public const string DefinitionMissing = "DefinitionMissing";

    /// <summary>Reserved; this version never answers it.</summary>
    public const string PolicyDenied = "PolicyDenied";

    /// <summary>A timeout, a 5xx, the network, or no base address.</summary>
    public const string EngineUnavailable = "EngineUnavailable";

    /// <summary>401 or 403: not the owner of the type, or no scope.</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>409: a running instance of the entity under another key, or a decision written at the same moment.</summary>
    public const string Conflict = "Conflict";

    /// <summary>400: a refused field (title, link, requester), or a call with no tenant.</summary>
    public const string Invalid = "Invalid";

    public const string EngineUnavailableMessage = "تعذّر الوصول إلى محرك الاعتماد. أعد المحاولة لاحقاً.";
    public const string DefinitionMissingMessage = "لا يوجد مسار اعتماد منشور لهذا الإجراء.";
    public const string ForbiddenMessage = "هذه الخدمة لا تملك بدء هذا الاعتماد.";
    public const string ConflictMessage = "يوجد مسار اعتماد جارٍ لهذا السجل.";
    public const string InvalidMessage = "رفض محرك الاعتماد الطلب.";
    public const string NoTenantMessage = "تعذّر تحديد الجهة، فلم يبدأ الاعتماد.";

    /// <summary>What a person reads when a start or a withdrawal fails, by <paramref name="errorCode"/>.</summary>
    public static string MessageFor(string? errorCode) => errorCode switch
    {
        DefinitionMissing => DefinitionMissingMessage,
        Forbidden => ForbiddenMessage,
        Conflict => ConflictMessage,
        Invalid => InvalidMessage,
        _ => EngineUnavailableMessage
    };
}

using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdEss;

public sealed class ProdEssMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("employeeSelfServicePolicyVersion")]
    public string? EmployeeSelfServicePolicyVersion { get; set; }

    [JsonPropertyName("highVolumeJourneys")]
    public ProdEssHighVolumeJourneys? HighVolumeJourneys { get; set; }

    [JsonPropertyName("correctionVisibility")]
    public ProdEssCorrectionVisibility? CorrectionVisibility { get; set; }

    [JsonPropertyName("managerScope")]
    public ProdEssManagerScope? ManagerScope { get; set; }

    [JsonPropertyName("usageTelemetry")]
    public ProdEssUsageTelemetry? UsageTelemetry { get; set; }

    [JsonPropertyName("privacy")]
    public ProdEssPrivacy? Privacy { get; set; }

    [JsonPropertyName("deviceSession")]
    public ProdEssDeviceSession? DeviceSession { get; set; }

    [JsonPropertyName("personaUat")]
    public ProdEssPersonaUat? PersonaUat { get; set; }

    [JsonPropertyName("audit")]
    public ProdEssAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdEssMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdEssSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdEssBlocker> Blockers { get; set; } = [];
}

public sealed class ProdEssHighVolumeJourneys
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("exceptionOnlyBackOffice")]
    public bool ExceptionOnlyBackOffice { get; set; }

    [JsonPropertyName("journeys")]
    public List<string> Journeys { get; set; } = [];
}

public sealed class ProdEssCorrectionVisibility
{
    [JsonPropertyName("sourceVisible")]
    public bool SourceVisible { get; set; }

    [JsonPropertyName("statusVisible")]
    public bool StatusVisible { get; set; }
}

public sealed class ProdEssManagerScope
{
    [JsonPropertyName("enforced")]
    public bool Enforced { get; set; }

    [JsonPropertyName("outsideScopeForbidden")]
    public bool OutsideScopeForbidden { get; set; }
}

public sealed class ProdEssUsageTelemetry
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("liveForbidden")]
    public bool LiveForbidden { get; set; }
}

public sealed class ProdEssPrivacy
{
    [JsonPropertyName("minimizationRequired")]
    public bool MinimizationRequired { get; set; }
}

public sealed class ProdEssDeviceSession
{
    [JsonPropertyName("lostSessionHandled")]
    public bool LostSessionHandled { get; set; }

    [JsonPropertyName("deviceSecurityRequired")]
    public bool DeviceSecurityRequired { get; set; }
}

public sealed class ProdEssPersonaUat
{
    [JsonPropertyName("multiAgencyRequired")]
    public bool MultiAgencyRequired { get; set; }

    [JsonPropertyName("mobileDesktopRequired")]
    public bool MobileDesktopRequired { get; set; }
}

public sealed class ProdEssAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdEssMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveEmployeePortalForbidden")]
    public bool LiveEmployeePortalForbidden { get; set; }

    [JsonPropertyName("liveManagerPortalForbidden")]
    public bool LiveManagerPortalForbidden { get; set; }
}

public sealed class ProdEssSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("employeeSelfServicePolicyVersion")]
    public string? EmployeeSelfServicePolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdEssBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

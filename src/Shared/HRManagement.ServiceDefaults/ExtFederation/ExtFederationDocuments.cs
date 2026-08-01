using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ExtFederation;

public sealed class ExtFederationMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("assurancePolicyVersion")]
    public string? AssurancePolicyVersion { get; set; }

    [JsonPropertyName("assuranceLevels")]
    public List<ExtAssuranceLevel> AssuranceLevels { get; set; } = [];

    [JsonPropertyName("personas")]
    public List<ExtPersona> Personas { get; set; } = [];

    [JsonPropertyName("federationTrust")]
    public ExtFederationTrust? FederationTrust { get; set; }

    [JsonPropertyName("mfaPolicy")]
    public ExtMfaPolicy? MfaPolicy { get; set; }

    [JsonPropertyName("lifecycle")]
    public ExtLifecyclePolicy? Lifecycle { get; set; }

    [JsonPropertyName("revocation")]
    public ExtRevocationPolicy? Revocation { get; set; }

    [JsonPropertyName("breakGlass")]
    public ExtBreakGlassPolicy? BreakGlass { get; set; }

    [JsonPropertyName("citizenEmployeeSeparation")]
    public ExtCitizenEmployeeSeparation? CitizenEmployeeSeparation { get; set; }

    [JsonPropertyName("attributeAuthority")]
    public ExtAttributeAuthority? AttributeAuthority { get; set; }

    [JsonPropertyName("identifierPolicy")]
    public ExtIdentifierPolicy? IdentifierPolicy { get; set; }

    [JsonPropertyName("signing")]
    public ExtFederationSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ExtFederationBlocker> Blockers { get; set; } = [];
}

public sealed class ExtAssuranceLevel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("fal")]
    public string? Fal { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class ExtPersona
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "";

    [JsonPropertyName("minAal")]
    public string MinAal { get; set; } = "";

    [JsonPropertyName("minFal")]
    public string? MinFal { get; set; }
}

public sealed class ExtFederationTrust
{
    [JsonPropertyName("nationalIdp")]
    public ExtIdpStub? NationalIdp { get; set; }

    [JsonPropertyName("agencyIdps")]
    public List<ExtIdpStub> AgencyIdps { get; set; } = [];

    [JsonPropertyName("metadataRequirements")]
    public List<string> MetadataRequirements { get; set; } = [];
}

public sealed class ExtIdpStub
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("live")]
    public bool Live { get; set; }
}

public sealed class ExtMfaPolicy
{
    [JsonPropertyName("phishingResistantRoles")]
    public List<string> PhishingResistantRoles { get; set; } = [];

    [JsonPropertyName("allowedAuthenticatorsLab")]
    public List<string> AllowedAuthenticatorsLab { get; set; } = [];

    [JsonPropertyName("smsOtpForbiddenForSensitive")]
    public bool SmsOtpForbiddenForSensitive { get; set; }
}

public sealed class ExtLifecyclePolicy
{
    [JsonPropertyName("states")]
    public List<string> States { get; set; } = [];

    [JsonPropertyName("revokeRequiresReason")]
    public bool RevokeRequiresReason { get; set; }
}

public sealed class ExtRevocationPolicy
{
    [JsonPropertyName("slaMinutes")]
    public int SlaMinutes { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("breachForbiddenWithoutEscalation")]
    public bool BreachForbiddenWithoutEscalation { get; set; }
}

public sealed class ExtBreakGlassPolicy
{
    [JsonPropertyName("dualControl")]
    public bool DualControl { get; set; }

    [JsonPropertyName("timeBoundMinutes")]
    public int TimeBoundMinutes { get; set; }

    [JsonPropertyName("auditRequired")]
    public bool AuditRequired { get; set; }
}

public sealed class ExtCitizenEmployeeSeparation
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("forbidSharedPrincipal")]
    public bool ForbidSharedPrincipal { get; set; }
}

public sealed class ExtAttributeAuthority
{
    [JsonPropertyName("claimsAllowlist")]
    public List<string> ClaimsAllowlist { get; set; } = [];

    [JsonPropertyName("noNameOnlyLinking")]
    public bool NoNameOnlyLinking { get; set; }

    [JsonPropertyName("forbidLinkKeys")]
    public List<string> ForbidLinkKeys { get; set; } = [];
}

public sealed class ExtIdentifierPolicy
{
    [JsonPropertyName("opaquePreferred")]
    public bool OpaquePreferred { get; set; }

    [JsonPropertyName("forbidPlainNationalIdInLogs")]
    public bool ForbidPlainNationalIdInLogs { get; set; }
}

public sealed class ExtFederationSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("assurancePolicyVersion")]
    public string? AssurancePolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ExtFederationBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

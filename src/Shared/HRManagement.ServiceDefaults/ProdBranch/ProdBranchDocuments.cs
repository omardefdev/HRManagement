using System.Text.Json.Serialization;

namespace HRManagement.ServiceDefaults.ProdBranch;

public sealed class ProdBranchMatrixDocument
{
    [JsonPropertyName("card")]
    public string Card { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("remoteBranchPolicyVersion")]
    public string? RemoteBranchPolicyVersion { get; set; }

    [JsonPropertyName("performanceBudgets")]
    public ProdBranchPerformanceBudgets? PerformanceBudgets { get; set; }

    [JsonPropertyName("networkProfile")]
    public ProdBranchNetworkProfile? NetworkProfile { get; set; }

    [JsonPropertyName("responsivePwa")]
    public ProdBranchResponsivePwa? ResponsivePwa { get; set; }

    [JsonPropertyName("resumableUploads")]
    public ProdBranchResumableUploads? ResumableUploads { get; set; }

    [JsonPropertyName("saveDraft")]
    public ProdBranchSaveDraft? SaveDraft { get; set; }

    [JsonPropertyName("retryIdempotency")]
    public ProdBranchRetryIdempotency? RetryIdempotency { get; set; }

    [JsonPropertyName("localization")]
    public ProdBranchLocalization? Localization { get; set; }

    [JsonPropertyName("timezoneCalendar")]
    public ProdBranchTimezoneCalendar? TimezoneCalendar { get; set; }

    [JsonPropertyName("printableFallback")]
    public ProdBranchPrintableFallback? PrintableFallback { get; set; }

    [JsonPropertyName("offline")]
    public ProdBranchOffline? Offline { get; set; }

    [JsonPropertyName("accessibility")]
    public ProdBranchAccessibility? Accessibility { get; set; }

    [JsonPropertyName("audit")]
    public ProdBranchAudit? Audit { get; set; }

    [JsonPropertyName("mockSandbox")]
    public ProdBranchMockSandbox? MockSandbox { get; set; }

    [JsonPropertyName("signing")]
    public ProdBranchSigning? Signing { get; set; }

    [JsonPropertyName("blockers")]
    public List<ProdBranchBlocker> Blockers { get; set; } = [];
}

public sealed class ProdBranchPerformanceBudgets
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("maxTtiMs")]
    public int MaxTtiMs { get; set; }

    [JsonPropertyName("maxPayloadKb")]
    public int MaxPayloadKb { get; set; }
}

public sealed class ProdBranchNetworkProfile
{
    [JsonPropertyName("throttledLabRequired")]
    public bool ThrottledLabRequired { get; set; }

    [JsonPropertyName("lowBandwidthSupported")]
    public bool LowBandwidthSupported { get; set; }

    [JsonPropertyName("profileId")]
    public string? ProfileId { get; set; }
}

public sealed class ProdBranchResponsivePwa
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("labStubAllowed")]
    public bool LabStubAllowed { get; set; }

    [JsonPropertyName("livePwaForbidden")]
    public bool LivePwaForbidden { get; set; }
}

public sealed class ProdBranchResumableUploads
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdBranchSaveDraft
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdBranchRetryIdempotency
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("duplicateSubmitSafe")]
    public bool DuplicateSubmitSafe { get; set; }
}

public sealed class ProdBranchLocalization
{
    [JsonPropertyName("arabicEnglishRequired")]
    public bool ArabicEnglishRequired { get; set; }

    [JsonPropertyName("rtlLtrRequired")]
    public bool RtlLtrRequired { get; set; }

    [JsonPropertyName("localLanguagesDecisionRequired")]
    public bool LocalLanguagesDecisionRequired { get; set; }
}

public sealed class ProdBranchTimezoneCalendar
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdBranchPrintableFallback
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ProdBranchOffline
{
    [JsonPropertyName("limitedAllowed")]
    public bool LimitedAllowed { get; set; }

    [JsonPropertyName("encryptionRequired")]
    public bool EncryptionRequired { get; set; }

    [JsonPropertyName("threatModelRequired")]
    public bool ThreatModelRequired { get; set; }

    [JsonPropertyName("liveOfflineForbidden")]
    public bool LiveOfflineForbidden { get; set; }
}

public sealed class ProdBranchAccessibility
{
    [JsonPropertyName("wcag22AaRequired")]
    public bool Wcag22AaRequired { get; set; }
}

public sealed class ProdBranchAudit
{
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("requiredFields")]
    public List<string> RequiredFields { get; set; } = [];
}

public sealed class ProdBranchMockSandbox
{
    [JsonPropertyName("labStub")]
    public bool LabStub { get; set; }

    [JsonPropertyName("liveRemoteBranchForbidden")]
    public bool LiveRemoteBranchForbidden { get; set; }

    [JsonPropertyName("liveFullOfflineForbidden")]
    public bool LiveFullOfflineForbidden { get; set; }
}

public sealed class ProdBranchSigning
{
    [JsonPropertyName("signedByRole")]
    public string? SignedByRole { get; set; }

    [JsonPropertyName("reviewerRole")]
    public string? ReviewerRole { get; set; }

    [JsonPropertyName("remoteBranchPolicyVersion")]
    public string? RemoteBranchPolicyVersion { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }
}

public sealed class ProdBranchBlocker
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

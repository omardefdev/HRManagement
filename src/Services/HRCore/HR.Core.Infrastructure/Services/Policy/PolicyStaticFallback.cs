using HRManagement.SharedKernel.Policy;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>Static fallback when no published policy version exists (mirrors legacy *Rules).</summary>
public static class PolicyStaticFallback
{
    public static PolicyDecision Evaluate(PolicyEnforceRequest request)
    {
        return request.Domain switch
        {
            PolicyDomains.AssetsCustody => EvaluateAssetsCustody(request),
            PolicyDomains.AssetsProcurement => EvaluateAssetsProcurement(request),
            PolicyDomains.CoreRecruitment => EvaluateCoreRecruitment(request),
            _ => new PolicyDecision
            {
                Allowed = true,
                Explanations = ["fallback: لا سياسة منشورة — سماح افتراضي"]
            }
        };
    }

    private static PolicyDecision EvaluateAssetsCustody(PolicyEnforceRequest request)
    {
        if (string.Equals(request.Action, "Classify", StringComparison.OrdinalIgnoreCase))
            return EvaluateAssetsClassify(request);

        var isPrivileged = GetBool(request.Facts, "isPrivileged");
        var value = GetDecimal(request.Facts, "value");
        var hasOrigin = GetBool(request.Facts, "hasOriginReceipt");
        var isLegacy = GetBool(request.Facts, "isLegacyEntry");

        var effects = new List<PolicyEffectDefinition>();
        var explanations = new List<string> { "fallback: Assets.Custody static" };

        if (request.Action is "Create" or "CreateLegacy")
        {
            if (!isLegacy && !hasOrigin)
            {
                return new PolicyDecision
                {
                    Allowed = false,
                    DenyReason = "أصل جديد يتطلب سجل استلام أو إدخال legacy",
                    Explanations = explanations
                };
            }
        }

        if (isPrivileged)
        {
            effects.Add(new PolicyEffectDefinition { Effect = PolicyEffect.RequireEvidence });
            explanations.Add("fallback: أصل مميز → دليل");
        }

        if (isPrivileged || value > 10_000m)
        {
            effects.Add(new PolicyEffectDefinition
            {
                Effect = PolicyEffect.TriggerWorkflow,
                Params = new Dictionary<string, string>
                {
                    ["workflowCode"] = request.Action == "MarkLost" ? "ASSET-LOSS-APPROVAL" : "ASSET-ASSIGN-APPROVAL"
                }
            });
            explanations.Add("fallback: موافقة workflow");
        }

        return new PolicyDecision
        {
            Allowed = true,
            Effects = effects.Count > 0
                ? effects.Select(e => e.Effect).Distinct().ToList()
                : [PolicyEffect.Allow],
            EffectDetails = effects,
            Explanations = explanations
        };
    }

    private static PolicyDecision EvaluateAssetsProcurement(PolicyEnforceRequest request)
    {
        if (request.Action == "Receive" && GetString(request.Facts, "orderStatus") != "Fulfilled")
        {
            return new PolicyDecision
            {
                Allowed = false,
                DenyReason = "لا يمكن الاستلام إلا لطلب منفّذ",
                Explanations = ["fallback: SupplyReceipt"]
            };
        }

        return new PolicyDecision { Allowed = true, Explanations = ["fallback: Assets.Procurement allow"] };
    }

    private static PolicyDecision EvaluateCoreRecruitment(PolicyEnforceRequest request)
    {
        if (string.Equals(request.Action, "SubmitForApproval", StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyDecision
            {
                Allowed = true,
                Effects = [PolicyEffect.TriggerWorkflow],
                EffectDetails =
                [
                    new PolicyEffectDefinition
                    {
                        Effect = PolicyEffect.TriggerWorkflow,
                        Params = new Dictionary<string, string>
                        {
                            ["workflowCode"] = "RECRUITMENT-REQUISITION-APPROVAL"
                        }
                    }
                ],
                Explanations = ["fallback: SubmitForApproval → workflow"]
            };
        }

        if (string.Equals(request.Action, "Publish", StringComparison.OrdinalIgnoreCase))
        {
            var status = GetString(request.Facts, "requisitionStatus");
            var vacant = GetBool(request.Facts, "positionVacant");
            if (status == "Approved" && vacant)
            {
                return new PolicyDecision
                {
                    Allowed = true,
                    Explanations = ["fallback: Publish allowed"]
                };
            }

            return new PolicyDecision
            {
                Allowed = false,
                DenyReason = "لا يمكن النشر إلا لطلب معتمد على منصب شاغر",
                Explanations = ["fallback: Publish denied"]
            };
        }

        if (string.Equals(request.Action, "Issue", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.SubjectType, "OfferLetter", StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyDecision
            {
                Allowed = true,
                Effects = [PolicyEffect.TriggerWorkflow],
                EffectDetails =
                [
                    new PolicyEffectDefinition
                    {
                        Effect = PolicyEffect.TriggerWorkflow,
                        Params = new Dictionary<string, string>
                        {
                            // HCM-027: mirrors R-OFF-EXCEPTION (8) / R-OFF-OVERRIDE (9) / R-OFF-ISSUE (10).
                            ["workflowCode"] = GetBool(request.Facts, "isExceptionOffer")
                                || GetBool(request.Facts, "isMeritOverride")
                                ? "RECRUITMENT-OFFER-OVERRIDE-APPROVAL"
                                : "RECRUITMENT-OFFER-APPROVAL"
                        }
                    }
                ],
                Explanations = ["fallback: Issue offer → workflow"]
            };
        }

        if (string.Equals(request.Action, "Hire", StringComparison.OrdinalIgnoreCase))
        {
            if (!GetBool(request.Facts, "hasAcceptedOffer"))
            {
                return new PolicyDecision
                {
                    Allowed = false,
                    DenyReason = "يجب قبول خطاب العرض قبل التعيين",
                    Explanations = ["fallback: Hire denied without accepted offer"]
                };
            }

            return new PolicyDecision
            {
                Allowed = true,
                Effects = [PolicyEffect.TriggerWorkflow],
                EffectDetails =
                [
                    new PolicyEffectDefinition
                    {
                        Effect = PolicyEffect.TriggerWorkflow,
                        Params = new Dictionary<string, string>
                        {
                            ["workflowCode"] = "RECRUITMENT-HIRE"
                        }
                    }
                ],
                Explanations = ["fallback: Hire → workflow"]
            };
        }

        return new PolicyDecision
        {
            Allowed = true,
            Explanations = ["fallback: Core.Recruitment allow"]
        };
    }

    private static PolicyDecision EvaluateAssetsClassify(PolicyEnforceRequest request)
    {
        var value = GetDecimal(request.Facts, "value");
        var category = GetString(request.Facts, "category");
        var effects = new List<PolicyEffectDefinition>();
        var explanations = new List<string> { "fallback: Classify" };

        if (value > 10_000m || (category == "أصول ثابتة" && value > 5_000m))
        {
            effects.Add(new PolicyEffectDefinition
            {
                Effect = PolicyEffect.SetField,
                Params = new Dictionary<string, string>
                {
                    ["field"] = "isPrivileged",
                    ["value"] = "true"
                }
            });
            explanations.Add("fallback: تصنيف مميز");
        }

        return new PolicyDecision
        {
            Allowed = true,
            Effects = effects.Count > 0 ? effects.Select(e => e.Effect).Distinct().ToList() : [PolicyEffect.Allow],
            EffectDetails = effects,
            Explanations = explanations
        };
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var v) && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");

    private static decimal GetDecimal(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var v) && decimal.TryParse(v, out var d) ? d : 0m;

    private static string GetString(IReadOnlyDictionary<string, string> facts, string key) =>
        facts.TryGetValue(key, out var v) ? v : "";
}

public static class PolicyShadowComparer
{
    public static List<string> CompareWithStaticFallback(PolicyEnforceRequest request, PolicyDecision policyDecision)
    {
        var staticDecision = PolicyStaticFallback.Evaluate(request);
        var diffs = new List<string>();

        if (staticDecision.Allowed != policyDecision.Allowed)
            diffs.Add($"allowed: static={staticDecision.Allowed} policy={policyDecision.Allowed}");

        var staticEffects = staticDecision.Effects.OrderBy(e => e).ToList();
        var policyEffects = policyDecision.Effects.OrderBy(e => e).ToList();
        if (!staticEffects.SequenceEqual(policyEffects))
            diffs.Add($"effects: static=[{string.Join(",", staticEffects)}] policy=[{string.Join(",", policyEffects)}]");

        return diffs;
    }
}

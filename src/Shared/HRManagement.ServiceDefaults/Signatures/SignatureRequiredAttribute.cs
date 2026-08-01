using HRManagement.SharedKernel.Signatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HRManagement.ServiceDefaults.Signatures;

/// <summary>
/// Action filter that enforces a legal signature policy check (SIG-001).
/// Apply to controller actions that produce legally binding documents.
/// The filter verifies that the specified subject type requires a legal signature per <see cref="SignaturePolicy"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class SignatureRequiredAttribute : ActionFilterAttribute
{
    public string SubjectType { get; }

    public SignatureRequiredAttribute(string subjectType)
    {
        SubjectType = subjectType;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!SignaturePolicy.IsLegalSignatureRequired(SubjectType))
        {
            context.Result = new BadRequestObjectResult(
                $"Subject type '{SubjectType}' is not configured in the legal signature policy (SIG-001).");
            return;
        }

        base.OnActionExecuting(context);
    }
}

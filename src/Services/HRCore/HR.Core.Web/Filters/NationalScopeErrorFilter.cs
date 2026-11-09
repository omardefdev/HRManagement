using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace HR.Core.Web.Filters;

/// <summary>
/// PLAN-48: the errors of the national scope and of code generation answer with their own status and an Arabic message, never a
/// bare 500. No published code policy, an invalid one or a missing fact: 422. A number already issued: 409. A national row
/// written outside the national tenant (<see cref="NationalSharedWriteRefusedException"/>): 403. An API request gets a problem
/// document; a page gets the shared error view with the same status.
/// </summary>
public sealed class NationalScopeErrorFilter(ILogger<NationalScopeErrorFilter> logger, IModelMetadataProvider metadata) : IExceptionFilter
{
    public const string ProblemTypeBase = "https://hrm.local/problems/";

    public void OnException(ExceptionContext context)
    {
        var (status, type, message) = context.Exception switch
        {
            CodePolicyMissingException e => (StatusCodes.Status422UnprocessableEntity, "code-policy-missing", e.MessageAr),
            CodePolicyInvalidException e => (StatusCodes.Status422UnprocessableEntity, "code-policy-invalid", e.MessageAr),
            CodeFactMissingException e => (StatusCodes.Status422UnprocessableEntity, "code-fact-missing", e.MessageAr),
            CodeCollisionException e => (StatusCodes.Status409Conflict, "code-collision", e.MessageAr),
            NationalSharedWriteRefusedException => (StatusCodes.Status403Forbidden, "national-shared-write", NationalSharedWriteRefusedException.MessageAr),
            _ => (0, string.Empty, string.Empty)
        };
        if (status == 0)
            return;

        logger.LogWarning(context.Exception, "PLAN-48 {Problem} answered {Status} on {Action}", type, status, context.ActionDescriptor.DisplayName);

        if (IsApi(context))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = status,
                Type = ProblemTypeBase + type,
                Title = message
            })
            { StatusCode = status };
        }
        else
        {
            context.Result = new ViewResult
            {
                ViewName = "~/Views/Shared/NationalScopeError.cshtml",
                StatusCode = status,
                ViewData = new ViewDataDictionary(metadata, context.ModelState) { Model = message }
            };
        }

        context.ExceptionHandled = true;
    }

    private static bool IsApi(ExceptionContext context) =>
        context.ActionDescriptor.EndpointMetadata.OfType<ApiControllerAttribute>().Any()
        || context.HttpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
}

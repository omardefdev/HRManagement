using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>
/// PLAN-67 (D-67-2, P-7): a <c>datetime-local</c> value posted to a page (form, query or route; never a JSON body, and never an
/// <c>[ApiController]</c>) is a time on the user's clock (<c>Localization:TimeZone</c>), and is bound as the UTC value to store,
/// with <see cref="DateTimeKind.Utc"/>. A date alone, or an ISO value with <c>Z</c> or an offset, is bound as before: a date is a
/// calendar date, and an explicit offset already says what it means.
/// </summary>
public sealed class HrLocalDateTimeModelBinder(IModelBinder fallback) : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var isApi = bindingContext.ActionContext.ActionDescriptor.EndpointMetadata?.OfType<IApiBehaviorMetadata>().Any() == true;
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        var raw = result.FirstValue;
        if (isApi || !HrLocalTime.TryParseLocalInput(raw, out var local))
        {
            await fallback.BindModelAsync(bindingContext);
            return;
        }

        var zone = bindingContext.HttpContext.RequestServices.GetService<ICalendarService>()?.TimeZone ?? HrLocalTime.DefaultZone;
        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        bindingContext.Result = ModelBindingResult.Success(HrLocalTime.ToUtc(local, zone));
    }
}

/// <summary>Puts <see cref="HrLocalDateTimeModelBinder"/> in front of the framework's binder for <see cref="DateTime"/> and <c>DateTime?</c>.</summary>
public sealed class HrLocalDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.UnderlyingOrModelType != typeof(DateTime))
            return null;
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        return new HrLocalDateTimeModelBinder(new SimpleTypeModelBinder(context.Metadata.ModelType, loggerFactory));
    }
}

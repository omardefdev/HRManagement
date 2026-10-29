using HR.Core.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HR.Core.Web.ModelBinding;

/// <summary>
/// HCM-031 (PLAN-31 review): binds <c>decimal</c>/<c>decimal?</c> form, query and route values with
/// <see cref="InvariantDecimal"/>. The default request culture is ar-SY, whose decimal separator is «٫», while a browser
/// <c>type="number"</c> input always posts «37.5»; the stock binder parses with the request culture and rejects it. Arabic-Indic
/// digits and «٫» are still accepted. JSON bodies (input formatters) are not affected.
/// </summary>
public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Same as the stock binder: empty is null for decimal?, and «required» for decimal.
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                    bindingContext.ModelMetadata.ModelBindingMessageProvider.ValueMustNotBeNullAccessor(raw ?? string.Empty));
            return Task.CompletedTask;
        }

        if (InvariantDecimal.TryParse(raw, out var value))
            bindingContext.Result = ModelBindingResult.Success(value);
        else
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                bindingContext.ModelMetadata.ModelBindingMessageProvider.AttemptedValueIsInvalidAccessor(
                    raw, bindingContext.ModelMetadata.DisplayName ?? bindingContext.ModelName));
        return Task.CompletedTask;
    }
}

/// <summary>Registers <see cref="InvariantDecimalModelBinder"/> for <c>decimal</c> and <c>decimal?</c>.</summary>
public sealed class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.UnderlyingOrModelType == typeof(decimal) ? new InvariantDecimalModelBinder() : null;
}

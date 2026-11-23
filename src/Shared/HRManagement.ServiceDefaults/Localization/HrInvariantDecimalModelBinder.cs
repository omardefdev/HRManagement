using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>
/// PLAN-53: binds an amount posted by a form as a numeric input sends it: digits with a dot for the fraction, whatever the request's
/// culture. The framework's binder reads a decimal with the request's culture, and the service's Arabic culture has another decimal
/// separator, so «8250.75» from <c>&lt;input type="number"&gt;</c> failed to bind. Applied by attribute on a view model's property
/// (<c>[ModelBinder(typeof(HrInvariantDecimalModelBinder))]</c>); nothing is registered globally. No thousands separator is accepted
/// in the invariant form: «7500,50» is refused rather than read as 750050; it is then tried with the request's culture.
/// </summary>
public sealed class HrInvariantDecimalModelBinder : IModelBinder
{
    private const NumberStyles Plain =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public const string NotANumber = "القيمة ليست رقماً صالحاً";
    public const string Required = "القيمة مطلوبة";

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) is not null)
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, Required);
            return Task.CompletedTask;
        }

        if (TryParse(raw, out var value))
            bindingContext.Result = ModelBindingResult.Success(value);
        else
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, NotANumber);
        return Task.CompletedTask;
    }

    /// <summary>The invariant plain form first (what a numeric input sends), then the request's culture.</summary>
    public static bool TryParse(string raw, out decimal value) =>
        decimal.TryParse(raw, Plain, CultureInfo.InvariantCulture, out value)
        || decimal.TryParse(raw, Plain, CultureInfo.CurrentCulture, out value);
}

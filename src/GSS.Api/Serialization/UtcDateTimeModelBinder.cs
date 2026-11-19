using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GSS.Api.Serialization;

/// <summary>Binds route/query/form <see cref="DateTime"/> values as UTC (no offset = UTC).</summary>
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    private const DateTimeStyles Styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces;

    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        string? text = value.FirstValue;
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, Styles, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"'{text}' is not a valid date/time.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>Uses <see cref="UtcDateTimeModelBinder"/> for <see cref="DateTime"/> and <see cref="Nullable{DateTime}"/>.</summary>
public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    private static readonly UtcDateTimeModelBinder Binder = new();

    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Metadata.UnderlyingOrModelType == typeof(DateTime) ? Binder : null;
    }
}

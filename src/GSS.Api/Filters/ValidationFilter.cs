using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using FluentValidation;
using FluentValidation.Results;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Core.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GSS.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator of each action argument and returns a 400 error envelope
/// for binding or validation errors. Singleton, like the validators it runs.
/// </summary>
public class ValidationFilter(IServiceProvider services, ILogger<ValidationFilter> logger) : IAsyncActionFilter
{
    /// <summary>Validator per argument type, or null when it has none (int, CancellationToken, ...); looked up once.</summary>
    private readonly ConcurrentDictionary<Type, IValidator?> _validators = new();

    /// <summary>Client message for a binding error that has no safe message of its own.</summary>
    private const string InvalidValueMessage = "The value is invalid.";

    /// <summary>Most field names a validation log lists (a batch can fail on thousands).</summary>
    private const int MaxLoggedFields = 10;

    /// <summary>Short-circuits invalid requests.</summary>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Binding errors (empty body, malformed JSON) win; a half-bound model is not validated.
        if (context.ModelState.IsValid)
        {
            await ValidateArgumentsAsync(context);
        }

        if (!context.ModelState.IsValid)
        {
            LogValidationFailed(context);

            string errorId = ApiErrorId.For(context.HttpContext);
            List<ApiError> errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(e => new ApiError
                {
                    ErrorId = errorId,
                    StatusCode = (short)HttpStatusCode.BadRequest,
                    Message = string.IsNullOrEmpty(x.Key) ? ClientMessage(x.Key, e) : $"{x.Key}: {ClientMessage(x.Key, e)}"
                }))
                .ToList();

            Response<object?> response = Response<object?>.Failure("Validation failed", errors);
            context.Result = new BadRequestObjectResult(response);
            return;
        }

        await next();
    }

    /// <summary>A client's mistake, so Information, not Warning; the field names are built only when it is on.</summary>
    private void LogValidationFailed(ActionExecutingContext context)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        string[] fields = [.. context.ModelState.Where(x => x.Value?.Errors.Count > 0).Select(x => x.Key)];
        string listed = string.Join(", ", fields.Take(MaxLoggedFields));
        if (fields.Length > MaxLoggedFields)
        {
            listed += $" (+{fields.Length - MaxLoggedFields} more)";
        }

        logger.ValidationFailed(
            context.RouteData.Values["controller"]?.ToString(),
            context.RouteData.Values["action"]?.ToString(),
            listed);
    }

    /// <summary>
    /// The error's message, or a generic one for an exception-only error (a JSON binding failure, whose message
    /// names .NET types); that message is logged at Debug, never returned.
    /// </summary>
    private string ClientMessage(string key, ModelError error)
    {
        if (!string.IsNullOrEmpty(error.ErrorMessage))
        {
            return error.ErrorMessage;
        }

        if (error.Exception != null)
        {
            logger.BindingFailed(key, error.Exception.Message);
        }

        return InvalidValueMessage;
    }

    /// <summary>
    /// Validates each argument that has an <see cref="IValidator{T}"/>; failures go into model state. A collection
    /// argument (a batch) is checked for size and null items, then each item is validated with keys like <c>[1].Email</c>.
    /// </summary>
    private async Task ValidateArgumentsAsync(ActionExecutingContext context)
    {
        foreach ((string name, object? argument) in context.ActionArguments)
        {
            if (argument is null)
            {
                continue;
            }

            if (argument is IEnumerable items and not string)
            {
                await ValidateItemsAsync(context, name, items);
                continue;
            }

            await ValidateAsync(context, argument, prefix: null);
        }
    }

    private async Task ValidateItemsAsync(ActionExecutingContext context, string argumentName, IEnumerable items)
    {
        List<object?> list = [.. items.Cast<object?>()];
        // Before any item is validated: the service checks the cap too, but only after binding and validation.
        if (list.Count > BatchLimits.MaxBatchSize)
        {
            context.ModelState.AddModelError(argumentName, BatchLimits.TooLarge(list.Count));
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            string prefix = $"[{i}]";
            if (list[i] is not { } item)
            {
                context.ModelState.AddModelError(prefix, "Item is required.");
                continue;
            }

            await ValidateAsync(context, item, prefix);
        }
    }

    private async Task ValidateAsync(ActionExecutingContext context, object model, string? prefix)
    {
        var validator = _validators.GetOrAdd(model.GetType(),
            static (type, provider) => provider.GetService(typeof(IValidator<>).MakeGenericType(type)) as IValidator, services);
        if (validator is null)
        {
            return;
        }

        var result = await validator.ValidateAsync(
            new ValidationContext<object>(model), context.HttpContext.RequestAborted);
        foreach (var failure in result.Errors)
        {
            string key = prefix is null ? failure.PropertyName : $"{prefix}.{failure.PropertyName}";
            context.ModelState.AddModelError(key, failure.ErrorMessage);
        }
    }
}

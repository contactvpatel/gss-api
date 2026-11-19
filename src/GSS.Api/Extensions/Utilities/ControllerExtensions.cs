using FluentValidation;
using GSS.Api.Filters;
using GSS.Api.Models;
using GSS.Api.Serialization;
using GSS.Api.Validators;
using GSS.Business.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace GSS.Api.Extensions.Utilities;

/// <summary>
/// Controller and FluentValidation setup.
/// </summary>
public static class ControllerExtensions
{
    /// <summary>Adds controllers, global filters and FluentValidation.</summary>
    public static void ConfigureControllers(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                options.Filters.AddService<ValidationFilter>();
                // FluentValidation (in ValidationFilter) is the only validator: no DataAnnotations, including
                // MVC's implicit [Required] on non-nullable properties. Binding errors still reach model state.
                options.ModelValidatorProviders.Clear();
                // UTC everywhere: query/route DateTime without an offset is UTC.
                options.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
                // JsonException messages name .NET types; ValidationFilter returns a generic message instead.
                options.AllowInputFormatterExceptionMessages = false;
            });
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcDateTimeJsonConverter()));
        services.AddEndpointsApiExplorer();
        services.ConfigureFluentValidation();
    }

    /// <summary>Registers Business and Api validators (singleton); <see cref="ValidationFilter"/> runs them.</summary>
    private static void ConfigureFluentValidation(this IServiceCollection services)
    {
        // Singleton: validators are stateless and built once. A validator must not inject a scoped service
        // (ValidatorRegistrationTests); put per-request checks in the service instead.
        services.AddValidatorsFromAssemblyContaining<ApplicationAccessDto>(ServiceLifetime.Singleton);

        services.AddValidatorsFromAssemblyContaining<PaginationQuery>(ServiceLifetime.Singleton);

        // Batch update items: a closed BatchUpdateRequestValidator<T> per Business validator (scanning skips open generics).
        foreach (var scan in AssemblyScanner.FindValidatorsInAssemblyContaining<ApplicationAccessDto>())
        {
            var dtoType = scan.InterfaceType.GetGenericArguments()[0];
            services.AddSingleton(
                typeof(IValidator<>).MakeGenericType(typeof(BatchUpdateRequest<>).MakeGenericType(dtoType)),
                typeof(BatchUpdateRequestValidator<>).MakeGenericType(dtoType));
        }

        services.AddSingleton<ValidationFilter>();

        services.Configure<ApiBehaviorOptions>(options =>
        {
            // ValidationFilter writes the 400, not [ApiController].
            options.SuppressModelStateInvalidFilter = true;
            // No ProblemDetails for bodiless client errors (415, a bare NotFound()): UseStatusCodePages writes the envelope.
            options.SuppressMapClientErrors = true;
        });
    }
}

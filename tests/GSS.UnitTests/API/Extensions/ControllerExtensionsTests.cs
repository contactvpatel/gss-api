using FluentAssertions;
using FluentValidation;
using GSS.Api.Extensions.Utilities;
using GSS.Api.Filters;
using GSS.Api.Models;
using GSS.Business.DTOs;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GSS.UnitTests.API.Extensions;

/// <summary>
/// Controller registration: FluentValidation via <see cref="ValidationFilter"/> is the only request validation.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ControllerExtensionsTests
{
    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.ConfigureControllers();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ConfigureControllers_RemovesMvcModelValidators()
    {
        using ServiceProvider provider = BuildProvider();

        MvcOptions options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;

        // No DataAnnotations (explicit or implicit [Required]) alongside FluentValidation.
        options.ModelValidatorProviders.Should().BeEmpty();
    }

    [Fact]
    public void ConfigureControllers_RegistersValidationFilterGlobally()
    {
        using ServiceProvider provider = BuildProvider();

        MvcOptions options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;

        // A singleton resolved from DI (AddService), not a per-request TypeFilter instance.
        options.Filters.OfType<ServiceFilterAttribute>()
            .Should().ContainSingle(f => f.ServiceType == typeof(ValidationFilter));
    }

    [Fact]
    public void ConfigureControllers_SuppressesApiControllerAutomatic400()
    {
        using ServiceProvider provider = BuildProvider();

        ApiBehaviorOptions options = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;

        options.SuppressModelStateInvalidFilter.Should().BeTrue();
    }

    [Fact]
    public void ConfigureControllers_RegistersBusinessAndApiValidators()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IValidator<SsoRenewTokenRequest>>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IValidator<PaginationQuery>>().Should().NotBeNull();
    }
}

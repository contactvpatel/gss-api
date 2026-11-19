using FluentAssertions;
using FluentValidation;
using GSS.Api.Extensions.Utilities;
using GSS.Api.Filters;
using GSS.UnitTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GSS.UnitTests.API.Extensions;

/// <summary>
/// Validators are stateless singletons, built once; <see cref="ValidationFilter"/> caches them per argument type.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ValidatorRegistrationTests
{
    [Fact]
    public void Validators_AreSingletons_ResolvableFromTheRootProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.ConfigureControllers();

        List<ServiceDescriptor> validators = services
            .Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IValidator<>))
            .ToList();

        validators.Should().NotBeEmpty().And.OnlyContain(d => d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(ValidationFilter))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);

        // A validator that injects a scoped service fails here (captive dependency).
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        foreach (var validator in validators)
        {
            root.GetRequiredService(validator.ServiceType).Should().BeAssignableTo<IValidator>();
        }
    }
}

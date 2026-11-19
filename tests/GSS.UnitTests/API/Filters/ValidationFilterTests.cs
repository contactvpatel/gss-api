using FluentAssertions;
using FluentValidation;
using GSS.Api.Filters;
using GSS.Api.Models;
using GSS.Core.Helpers;
using GSS.UnitTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GSS.UnitTests.API.Filters;

/// <summary>
/// Unit tests for ValidationFilter.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ValidationFilterTests
{
    private readonly Mock<ILogger<ValidationFilter>> _mockLogger = new();
    private ValidationFilter _filter;

    public ValidationFilterTests()
    {
        _filter = new ValidationFilter(new ServiceCollection().BuildServiceProvider(), _mockLogger.Object);
    }

    [Fact]
    public async Task OnActionExecutionAsync_LooksUpEachArgumentTypesValidatorOnce_NotPerRequest()
    {
        CountingServiceProvider root = new(new ServiceCollection()
            .AddSingleton<IValidator<TestRequest>, TestRequestValidator>()
            .BuildServiceProvider());
        _filter = new ValidationFilter(root, _mockLogger.Object);

        for (int i = 0; i < 3; i++)
        {
            ActionExecutingContext context = CreateActionExecutingContext(isValid: true);
            context.HttpContext.RequestServices = new ThrowingServiceProvider();
            context.ActionArguments["request"] = new TestRequest { Name = "" };
            context.ActionArguments["id"] = 42;
            context.ActionArguments["cancellationToken"] = CancellationToken.None;

            await _filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

            context.Result.Should().BeOfType<BadRequestObjectResult>("the validator still runs every request");
        }

        root.Lookups.Should().Be(3, "one lookup per argument type (request, int, CancellationToken), cached after the first request");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ValidModelState_ContinuesToNext()
    {
        // Arrange
        ActionExecutingContext executingContext = CreateActionExecutingContext(isValid: true);
        bool nextCalled = false;
        ActionExecutionDelegate next = async () =>
        {
            nextCalled = true;
            await Task.CompletedTask;
            return new ActionExecutedContext(executingContext, new List<IFilterMetadata>(), executingContext.Controller);
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        nextCalled.Should().BeTrue();
        executingContext.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_InvalidModelState_ReturnsBadRequest()
    {
        // Arrange
        ActionExecutingContext executingContext = CreateActionExecutingContext(isValid: false);
        bool nextCalled = false;
        ActionExecutionDelegate next = async () =>
        {
            nextCalled = true;
            await Task.CompletedTask;
            return new ActionExecutedContext(executingContext, new List<IFilterMetadata>(), executingContext.Controller);
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        nextCalled.Should().BeFalse();
        executingContext.Result.Should().BeOfType<BadRequestObjectResult>();
        BadRequestObjectResult? badRequestResult = executingContext.Result as BadRequestObjectResult;
        badRequestResult!.Value.Should().BeOfType<Response<object?>>();
        Response<object?>? response = badRequestResult.Value as Response<object?>;
        response!.Succeeded.Should().BeFalse();
        response.Message.Should().Be("Validation failed");
        response.Errors.Should().NotBeNull();
        response.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OnActionExecutionAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        ActionExecutingContext? executingContext = null;
        ActionExecutionDelegate next = async () =>
        {
            await Task.CompletedTask;
            return new ActionExecutedContext(
                new ActionContext(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
                new List<IFilterMetadata>(),
                new object());
        };

        // Act & Assert - null context should throw (fail-fast, industry standard)
        Func<Task> act = async () => await _filter.OnActionExecutionAsync(executingContext!, next);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_InvalidModelState_LogsInformation()
    {
        // Arrange
        _mockLogger.Setup(logger => logger.IsEnabled(LogLevel.Information)).Returns(true);
        ActionExecutingContext executingContext = CreateActionExecutingContext(isValid: false);
        ActionExecutionDelegate next = async () =>
        {
            await Task.CompletedTask;
            return new ActionExecutedContext(executingContext, new List<IFilterMetadata>(), executingContext.Controller);
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ManyInvalidFields_LogsTenAndACount()
    {
        var logger = new CapturingLogger();
        using var services = new ServiceCollection().BuildServiceProvider();
        ValidationFilter filter = new(services, logger);
        var context = CreateActionExecutingContext(isValid: true);
        for (int i = 0; i < 12; i++)
        {
            context.ModelState.AddModelError($"[{i}].Name", "Name is required");
        }

        var next = new NextProbe(context);
        await filter.OnActionExecutionAsync(context, next.Delegate);

        next.Called.Should().BeFalse();
        context.Result.Should().BeOfType<BadRequestObjectResult>();
        var logged = logger.Entries.Should().ContainSingle().Subject;
        logged.Level.Should().Be(LogLevel.Information);
        logged.Id.Should().Be(2010);
        logged.Message.Split(", ", StringSplitOptions.None).Should().HaveCount(10);
        logged.Message.Should().EndWith(" (+2 more)");
    }

    [Fact]
    public async Task OnActionExecutionAsync_InvalidModelState_IncludesErrorDetails()
    {
        // Arrange
        ActionExecutingContext executingContext = CreateActionExecutingContext(isValid: false);
        ActionExecutionDelegate next = async () =>
        {
            await Task.CompletedTask;
            return new ActionExecutedContext(executingContext, new List<IFilterMetadata>(), executingContext.Controller);
        };

        // Act
        await _filter.OnActionExecutionAsync(executingContext, next);

        // Assert
        BadRequestObjectResult? badRequestResult = executingContext.Result as BadRequestObjectResult;
        Response<object?>? response = badRequestResult!.Value as Response<object?>;
        response!.Errors.Should().NotBeNull();
        response.Errors.Should().NotBeEmpty();
        response.Errors!.All(e => !string.IsNullOrEmpty(e.Message)).Should().BeTrue();
        response.Errors!.All(e => e.StatusCode == 400).Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_ExceptionOnlyBindingError_ReturnsGenericMessage()
    {
        // Arrange: what the JSON input formatter adds when AllowInputFormatterExceptionMessages is off
        ActionExecutingContext executingContext = CreateActionExecutingContext(isValid: true);
        executingContext.ModelState.TryAddModelException(
            "$.refreshToken",
            new System.Text.Json.JsonException("The JSON value could not be converted to System.String."));

        // Act
        await _filter.OnActionExecutionAsync(executingContext, () => throw new InvalidOperationException("next must not run"));

        // Assert
        Response<object?>? response = (executingContext.Result as BadRequestObjectResult)!.Value as Response<object?>;
        response!.Errors.Should().ContainSingle().Which.Message.Should().Be("$.refreshToken: The value is invalid.");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ArgumentFailsValidator_ReturnsBadRequestWithItsErrors()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(new TestRequest { Name = "" });

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeFalse();
        Response<object?> response = GetBadRequestBody(context);
        response.Errors.Should().ContainSingle()
            .Which.Message.Should().Be($"Name: {TestRequestValidator.NameRequired}");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ArgumentPassesValidator_ContinuesToNext()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(new TestRequest { Name = "ok" });

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_ArgumentWithoutValidator_ContinuesToNext()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(new UnvalidatedRequest());

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_NullArgument_IsSkipped()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(null);

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_CollectionElementFailsValidator_ReturnsBadRequestWithIndexedPropertyMessages()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(
            new List<TestRequest> { new() { Name = "ok" }, new() { Name = "" } });

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeFalse();
        GetBadRequestBody(context).Errors.Should().ContainSingle()
            .Which.Message.Should().Be($"[1].Name: {TestRequestValidator.NameRequired}");
    }

    [Fact]
    public async Task OnActionExecutionAsync_CollectionWithNullElement_ReturnsBadRequest()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(new List<TestRequest?> { null });

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeFalse();
        GetBadRequestBody(context).Errors.Should().ContainSingle().Which.Message.Should().StartWith("[0]: ");
    }

    [Fact]
    public async Task OnActionExecutionAsync_CollectionOverMaxBatchSize_ReturnsBadRequestWithoutRunningValidators()
    {
        // Arrange
        Mock<IValidator<TestRequest>> validator = new();
        List<TestRequest> items = Enumerable.Range(0, BatchLimits.MaxBatchSize + 1)
            .Select(_ => new TestRequest()).ToList();
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(
            items, services => services.AddSingleton(_ => validator.Object));

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeFalse();
        GetBadRequestBody(context).Errors.Should().ContainSingle()
            .Which.Message.Should().Contain(BatchLimits.MaxBatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnActionExecutionAsync_CollectionOfPrimitives_ContinuesToNext()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(new List<int> { 1, 2 });

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        next.Called.Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_AsyncRule_RunsWithRequestAbortedToken()
    {
        // Arrange
        AsyncRequestValidator asyncValidator = new();
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(
            new AsyncRequest(), services => services.AddSingleton<IValidator<AsyncRequest>>(asyncValidator));
        using CancellationTokenSource requestAborted = new();
        context.HttpContext.RequestAborted = requestAborted.Token;

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        asyncValidator.ObservedToken.Should().Be(requestAborted.Token);
        next.Called.Should().BeFalse();
        GetBadRequestBody(context).Errors.Should().ContainSingle()
            .Which.Message.Should().Be($"Value: {AsyncRequestValidator.RejectedMessage}");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ObjectLevelFailure_OmitsEmptyKeyPrefix()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(
            new ObjectLevelRequest(), services => services.AddSingleton<IValidator<ObjectLevelRequest>, ObjectLevelRequestValidator>());

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        GetBadRequestBody(context).Errors.Should().ContainSingle()
            .Which.Message.Should().Be(ObjectLevelRequestValidator.Message);
    }

    [Fact]
    public async Task OnActionExecutionAsync_BindingError_SkipsValidationOfThePartiallyBoundModel()
    {
        // Arrange
        (ActionExecutingContext context, NextProbe next) = CreateContextWithArgument(new TestRequest { Name = "" });
        context.ModelState.AddModelError("Age", "The value 'abc' is not valid.");

        // Act
        await _filter.OnActionExecutionAsync(context, next.Delegate);

        // Assert
        GetBadRequestBody(context).Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Age: The value 'abc' is not valid.");
    }

    private static Response<object?> GetBadRequestBody(ActionExecutingContext context)
    {
        BadRequestObjectResult result = context.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        return result.Value.Should().BeOfType<Response<object?>>().Subject;
    }

    private (ActionExecutingContext Context, NextProbe Next) CreateContextWithArgument(
        object? argument,
        Action<IServiceCollection>? configureServices = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IValidator<TestRequest>, TestRequestValidator>();
        configureServices?.Invoke(services);

        // Validators are singletons resolved from the filter's (root) provider.
        ServiceProvider provider = services.BuildServiceProvider();
        _filter = new ValidationFilter(provider, _mockLogger.Object);
        ActionExecutingContext context = CreateActionExecutingContext(isValid: true);
        context.HttpContext.RequestServices = provider;
        context.ActionArguments["request"] = argument;

        return (context, new NextProbe(context));
    }

    private sealed class CapturingLogger : ILogger<ValidationFilter>
    {
        public List<(LogLevel Level, int Id, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, eventId.Id, formatter(state, exception)));
        }
    }

    private sealed class NextProbe(ActionExecutingContext context)
    {
        public bool Called { get; private set; }

        public ActionExecutionDelegate Delegate => () =>
        {
            Called = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller));
        };
    }

    private sealed class TestRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestRequestValidator : AbstractValidator<TestRequest>
    {
        public const string NameRequired = "Name is required.";

        public TestRequestValidator()
        {
            RuleFor(r => r.Name).NotEmpty().WithMessage(NameRequired);
        }
    }

    private sealed class UnvalidatedRequest
    {
    }

    private sealed class AsyncRequest
    {
        public string Value { get; set; } = "x";
    }

    private sealed class AsyncRequestValidator : AbstractValidator<AsyncRequest>
    {
        public const string RejectedMessage = "Rejected by async rule.";

        public AsyncRequestValidator()
        {
            RuleFor(r => r.Value)
                .MustAsync(async (_, cancellationToken) =>
                {
                    ObservedToken = cancellationToken;
                    await Task.Yield();
                    return false;
                })
                .WithMessage(RejectedMessage);
        }

        public CancellationToken ObservedToken { get; private set; }
    }

    private sealed class ObjectLevelRequest
    {
    }

    private sealed class ObjectLevelRequestValidator : AbstractValidator<ObjectLevelRequest>
    {
        public const string Message = "Request is invalid as a whole.";

        public override Task<FluentValidation.Results.ValidationResult> ValidateAsync(
            ValidationContext<ObjectLevelRequest> context,
            CancellationToken cancellation = default)
        {
            return Task.FromResult(new FluentValidation.Results.ValidationResult(
                [new FluentValidation.Results.ValidationFailure(string.Empty, Message)]));
        }
    }

    private static ActionExecutingContext CreateActionExecutingContext(bool isValid)
    {
        DefaultHttpContext httpContext = new DefaultHttpContext();
        Microsoft.AspNetCore.Routing.RouteData routeData = new Microsoft.AspNetCore.Routing.RouteData();
        routeData.Values["version"] = "1";
        routeData.Values["controller"] = "TestController";
        routeData.Values["action"] = "TestAction";

        ModelStateDictionary modelState = new ModelStateDictionary();
        if (!isValid)
        {
            modelState.AddModelError("Name", "Name is required");
            modelState.AddModelError("Email", "Email is invalid");
        }

        ActionContext actionContext = new ActionContext(
            httpContext,
            routeData,
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor(),
            modelState);

        ActionExecutingContext context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());

        return context;
    }

    private sealed class CountingServiceProvider(IServiceProvider inner) : IServiceProvider
    {
        public int Lookups { get; private set; }

        public object? GetService(Type serviceType)
        {
            Lookups++;
            return inner.GetService(serviceType);
        }
    }

    /// <summary>Validators come from the filter's own provider, never the request scope.</summary>
    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            throw new InvalidOperationException($"Unexpected request-scope lookup of {serviceType}");
        }
    }
}

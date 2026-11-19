# Request Validation Filter

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Registration](#registration)
- [Request Flow](#request-flow)
- [Collections and Batches](#collections-and-batches)
- [Error Contract](#error-contract)
- [Writing Validators](#writing-validators)
- [Verification](#verification)
- [References](#references)

---

## Overview

`ValidationFilter` is the single MVC validation path for GSS action arguments. It preserves model-binding errors, runs registered FluentValidation validators asynchronously, and returns the standard `Response<object?>` failure envelope before the controller executes.

## Registration

`src/GSS.Api/Extensions/Utilities/ControllerExtensions.cs`:

- registers Business and Api validators as singletons;
- registers `ValidationFilter` as a singleton and adds it globally to MVC;
- clears MVC model-validator providers so DataAnnotations and implicit non-nullable `[Required]` rules do not compete with FluentValidation;
- suppresses `[ApiController]`'s automatic model-state result because the filter writes the repository's error envelope;
- suppresses MVC ProblemDetails mapping for client errors.

Binding still belongs to MVC. Malformed JSON, unsupported conversions, and empty required bodies enter `ModelState` before the filter runs.

## Request Flow

For each action invocation the filter:

1. Checks existing model state. If binding already failed, it does not validate a partially bound object.
2. Examines each non-null action argument.
3. Resolves an `IValidator<T>` for the runtime type from the root service provider. The validator—or the absence of one—is cached by type.
4. Calls `ValidateAsync` with `HttpContext.RequestAborted`.
5. Adds FluentValidation failures to model state.
6. Short-circuits with `400` if any error exists; otherwise it calls the next action filter/controller.

Primitive route parameters and `CancellationToken` normally have no validator and are skipped after the cached lookup. Null arguments are also skipped; body/DTO rules must make required values explicit at the binding or validator boundary.

## Collections and Batches

Any non-string `IEnumerable` argument is treated as a batch:

- the filter materializes the sequence;
- a batch above `BatchLimits.MaxBatchSize` fails before item validators run;
- a null item produces an indexed `Item is required.` error;
- each non-null item is validated with keys such as `[1].Email`.

The Business service and repository also enforce the batch cap. The filter check improves the client response but is not the only guard.

## Error Contract

All validation failures return HTTP `400` with message `Validation failed` and one or more `ApiError` entries. Each entry uses the request correlation ID as `ErrorId`.

Safe binding or validator text is prefixed with its field key. When MVC supplies only a binding exception, the detailed exception message is logged at Debug and the client receives `The value is invalid.` so .NET type details are not exposed. Validation failure is logged at Information with at most ten field names.

This filter handles input binding and validation only. Business-rule outcomes remain `Result<T>` values in the Business layer and are mapped by `ApiControllerBase`.

## Writing Validators

Put DTO shape and value rules in an `AbstractValidator<T>` under the owning project's `Validators` folder. Validators are registered as singletons, so they must be stateless and must not depend on scoped services such as repositories or user context.

Use asynchronous FluentValidation rules only when the dependency lifetime permits it. Cross-record or authorization rules that need per-request services belong in Business service hooks such as `ValidateCreateAsync`, `ValidateUpdateAsync`, or `ValidateDeleteAsync`.

For time-dependent rules, inject `TimeProvider` and read it inside the rule callback. Do not capture the current time in a validator constructor and do not use direct static wall-clock APIs.

The following is illustrative rather than shipped GSS feature code:

```csharp
public sealed class CreateItemValidator : AbstractValidator<CreateItemDto>
{
    public CreateItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
```

## Verification

- `ValidationFilterTests` covers binding precedence, validator caching, cancellation, object-level failures, indexed batch errors, null items, and the batch cap.
- `ValidatorRegistrationTests` checks that validators are singletons resolvable from the root provider.
- Controller/integration tests verify the public error envelope.

## References

- [FluentValidation documentation](https://docs.fluentvalidation.net/)
- [ASP.NET Core filters](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters)
- [API response formats](../standards/api-response-formats.md)

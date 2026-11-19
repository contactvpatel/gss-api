using FluentValidation;
using GSS.Api.Models;

namespace GSS.Api.Validators;

/// <summary>
/// Validates the <see cref="BatchUpdateRequest{T}"/> envelope (Id &gt; 0, payload present) and the payload with T's validator.
/// Registered per closed type in <c>ControllerExtensions</c>: assembly scanning skips open generics.
/// </summary>
public class BatchUpdateRequestValidator<T> : AbstractValidator<BatchUpdateRequest<T>>
{
    /// <summary>Defines the rules; <paramref name="dataValidator"/> validates the payload.</summary>
    public BatchUpdateRequestValidator(IValidator<T> dataValidator)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than zero.");

        RuleFor(x => x.Data)
            .NotNull()
            .WithMessage("Data is required.");

        RuleFor(x => x.Data).SetValidator(dataValidator);
    }
}

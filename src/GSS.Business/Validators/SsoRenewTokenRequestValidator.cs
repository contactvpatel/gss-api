using FluentValidation;
using GSS.Business.DTOs;

namespace GSS.Business.Validators;

/// <summary>
/// Validates <see cref="SsoRenewTokenRequest"/>: refresh token present.
/// </summary>
public class SsoRenewTokenRequestValidator : AbstractValidator<SsoRenewTokenRequest>
{
    /// <summary>Message for a missing or blank refresh token.</summary>
    public const string RequiredMessage = "Refresh token is required.";

    /// <summary>Defines the rules.</summary>
    public SsoRenewTokenRequestValidator()
    {
        RuleFor(r => r.RefreshToken)
            .NotEmpty()
            .WithMessage(RequiredMessage);
    }
}

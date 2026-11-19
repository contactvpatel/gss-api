using FluentValidation;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Core.Helpers;

namespace GSS.Api.Validators;

/// <summary>
/// Validates <see cref="PaginationQuery"/>: page size within <see cref="PageLimits"/>, a cursor this API issued, and
/// not both page and cursor.
/// </summary>
public class PaginationQueryValidator : AbstractValidator<PaginationQuery>
{
    /// <summary>
    /// Maximum allowed page size.
    /// </summary>
    public const int MaxPageSize = PageLimits.MaxPageSize;

    /// <summary>
    /// Minimum allowed page size.
    /// </summary>
    public const int MinPageSize = PageLimits.MinPageSize;

    /// <summary>Defines the rules.</summary>
    public PaginationQueryValidator()
    {
        RuleFor(p => p.PageSize)
            .InclusiveBetween(MinPageSize, MaxPageSize)
            .When(p => p.PageSize.HasValue)
            .WithMessage($"Page size must be between {MinPageSize} and {MaxPageSize}.");

        RuleFor(p => p.Cursor)
            .Must(cursor => PageCursor.TryDecode(cursor, out _))
            .When(p => p.Cursor != null)
            .WithMessage("Cursor is invalid; send the nextCursor of the previous page.");

        RuleFor(p => p.Cursor)
            .Null()
            .When(p => p.IsPageNumbered)
            .WithMessage("Send either page or cursor, not both.");
    }
}

using FluentAssertions;
using FluentValidation.TestHelper;
using GSS.Api.Helpers;
using GSS.Api.Models;
using GSS.Api.Validators;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.API.Validators;

/// <summary>
/// Unit tests for PaginationQueryValidator.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class PaginationQueryValidatorTests
{
    private readonly PaginationQueryValidator _validator = new();

    [Fact]
    public void NoPaging_ShouldNotHaveValidationError()
    {
        _validator.TestValidate(new PaginationQuery()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(PaginationQueryValidator.MinPageSize)]
    [InlineData(50)]
    [InlineData(PaginationQueryValidator.MaxPageSize)]
    public void PageSizeInRange_ShouldNotHaveValidationError(int pageSize)
    {
        _validator.TestValidate(new PaginationQuery { Page = 1, PageSize = pageSize }).ShouldNotHaveAnyValidationErrors();
        _validator.TestValidate(new PaginationQuery { PageSize = pageSize }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(PaginationQueryValidator.MaxPageSize + 1)]
    public void PageSizeOutOfRange_ShouldHaveValidationError(int pageSize)
    {
        _validator.TestValidate(new PaginationQuery { Page = 1, PageSize = pageSize }).ShouldHaveValidationErrorFor(p => p.PageSize);
        _validator.TestValidate(new PaginationQuery { PageSize = pageSize }).ShouldHaveValidationErrorFor(p => p.PageSize);
    }

    [Fact]
    public void IssuedCursor_ShouldNotHaveValidationError()
    {
        _validator.TestValidate(new PaginationQuery { Cursor = PageCursor.Encode(42), IncludeTotal = true }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("MA")] // "0"
    [InlineData("LTE")] // "-1"
    [InlineData("OTk5OTk5OTk5OTk5")] // too long
    public void InvalidCursor_ShouldHaveValidationError(string cursor)
    {
        _validator.TestValidate(new PaginationQuery { Cursor = cursor }).ShouldHaveValidationErrorFor(p => p.Cursor);
    }

    [Fact]
    public void PageAndCursor_ShouldHaveValidationError()
    {
        _validator.TestValidate(new PaginationQuery { Page = 2, Cursor = PageCursor.Encode(42) }).ShouldHaveValidationErrorFor(p => p.Cursor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Cursor_RoundTrips(int id)
    {
        PageCursor.TryDecode(PageCursor.Encode(id), out int decoded).Should().BeTrue();
        decoded.Should().Be(id);
    }
}

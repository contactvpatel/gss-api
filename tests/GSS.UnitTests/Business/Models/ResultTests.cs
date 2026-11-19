using FluentAssertions;
using GSS.Business.Models;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Business.Models;

/// <summary>
/// Unit tests for Result&lt;T&gt;.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ResultTests
{
    [Fact]
    public void Success_WithValue_IsSuccessTrue()
    {
        Result<int> result = Result<int>.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.IsNotFound.Should().BeFalse();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void NotFound_IsNotFoundTrue()
    {
        Result<string> result = Result<string>.NotFound();
        result.IsSuccess.Should().BeFalse();
        result.IsNotFound.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void NotFound_WithMessage_KeepsMessageAndIsNotFailure()
    {
        Result<int> result = Result<int>.NotFound("Things not found. IDs: 7.");
        result.IsNotFound.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.ErrorMessage.Should().Be("Things not found. IDs: 7.");
        Result<int>.NotFound().ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void NotFound_Value_ThrowsInvalidOperationException()
    {
        Result<int> result = Result<int>.NotFound();
        Action act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot access Value when result is NotFound.");
    }

    [Fact]
    public void Failure_WithMessage_IsFailureTrue()
    {
        Result<int> result = Result<int>.Failure("Duplicate email");
        result.IsSuccess.Should().BeFalse();
        result.IsNotFound.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Duplicate email");
    }

    [Fact]
    public void Failure_NullMessage_ThrowsArgumentNullException()
    {
        Action act = () => Result<int>.Failure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Failure_EmptyMessage_ThrowsArgumentException()
    {
        Action act = () => Result<int>.Failure(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_WhitespaceMessage_ThrowsArgumentException()
    {
        Action act = () => Result<int>.Failure("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_IsNotAnyOutcome()
    {
        Result<int> result = default;

        result.IsSuccess.Should().BeFalse();
        result.IsNotFound.Should().BeFalse("default(Result<T>) must not read as NotFound");
        result.IsFailure.Should().BeFalse();
        result.IsConflict.Should().BeFalse();
    }

    [Fact]
    public void Default_Value_Throws()
    {
        Result<int> result = default;

        Action value = () => _ = result.Value;

        value.Should().Throw<InvalidOperationException>().WithMessage("*not initialized*");
    }

    [Fact]
    public void Conflict_WithMessage_IsConflictAndIsFailure()
    {
        Result<int> result = Result<int>.Conflict("Email in use");

        result.IsConflict.Should().BeTrue();
        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.IsNotFound.Should().BeFalse();
        result.ErrorMessage.Should().Be("Email in use");
    }

    [Fact]
    public void Conflict_WhitespaceMessage_ThrowsArgumentException()
    {
        Action act = () => Result<int>.Conflict(" ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_IsNotConflict()
    {
        Result<int>.Failure("Rule broken").IsConflict.Should().BeFalse();
    }
}

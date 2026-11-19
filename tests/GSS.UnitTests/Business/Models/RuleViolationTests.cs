using FluentAssertions;
using GSS.Business.Models;
using GSS.UnitTests.Common;
using Xunit;

namespace GSS.UnitTests.Business.Models;

[Trait("Category", TestCategories.Unit)]
public class RuleViolationTests
{
    [Fact]
    public void Invalid_BecomesFailure_NotConflict()
    {
        Result<int> result = RuleViolation.Invalid("bad").ToResult<int>();

        result.IsFailure.Should().BeTrue();
        result.IsConflict.Should().BeFalse();
        result.ErrorMessage.Should().Be("bad");
    }

    [Fact]
    public void Conflict_BecomesConflict()
    {
        Result<int> result = RuleViolation.Conflict("taken").ToResult<int>();

        result.IsConflict.Should().BeTrue();
        result.ErrorMessage.Should().Be("taken");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void BlankMessage_Throws(string? message)
    {
        Action act = () => RuleViolation.Invalid(message!);

        act.Should().Throw<ArgumentException>();
    }
}

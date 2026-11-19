using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
using GSS.Api.Models;
using GSS.Api.Validators;
using GSS.UnitTests.Common;
using GSS.UnitTests.Common.Models;
using Xunit;

namespace GSS.UnitTests.API.Validators;

/// <summary>
/// Unit tests for BatchUpdateRequestValidator&lt;T&gt;: rejects Id &lt;= 0 and a null payload.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class BatchUpdateRequestValidatorTests
{
    // No payload rules: these tests cover the envelope; the payload validator is required, so pass an empty one.
    private readonly BatchUpdateRequestValidator<UpdateTestEntityDto> _validator = new(new InlineValidator<UpdateTestEntityDto>());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Id_NotPositive_ShouldHaveValidationError(int id)
    {
        // Arrange
        BatchUpdateRequest<UpdateTestEntityDto> request = new() { Id = id, Data = new UpdateTestEntityDto { Name = "Red" } };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Id_Positive_ShouldNotHaveValidationError()
    {
        // Arrange
        BatchUpdateRequest<UpdateTestEntityDto> request = new() { Id = 1, Data = new UpdateTestEntityDto { Name = "Red" } };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void Data_Null_ShouldHaveValidationError()
    {
        // Arrange
        BatchUpdateRequest<UpdateTestEntityDto> request = new() { Id = 1, Data = null! };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Data);
    }

    [Fact]
    public void Validate_ValidRequest_ShouldNotHaveAnyErrors()
    {
        // Arrange
        BatchUpdateRequest<UpdateTestEntityDto> request = new() { Id = 5, Data = new UpdateTestEntityDto { Name = "Blue" } };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_IsReusableForAnyPayloadType()
    {
        // Arrange - the same envelope rules must apply whatever T is
        BatchUpdateRequestValidator<TestEntityDto> otherValidator = new(new InlineValidator<TestEntityDto>());
        BatchUpdateRequest<TestEntityDto> request = new() { Id = 0, Data = new TestEntityDto() };

        // Act
        var result = otherValidator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }
}

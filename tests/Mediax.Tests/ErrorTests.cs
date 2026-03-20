using FluentAssertions;
using Mediax.Core;
using Xunit;

namespace Mediax.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void NotFound_SetsCorrectType()
    {
        var err = Error.NotFound("ORDER_NF", "Not found");
        err.Type.Should().Be(ErrorType.NotFound);
        err.Code.Should().Be("ORDER_NF");
        err.Message.Should().Be("Not found");
    }

    [Fact]
    public void Validation_IncludesDetails()
    {
        var details = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Name"] = ["Name is required."]
        };
        var err = Error.Validation("INVALID", details: details);
        err.Type.Should().Be(ErrorType.Validation);
        err.Details.Should().ContainKey("Name");
    }

    [Fact]
    public void Conflict_SetsConflictType()
    {
        var err = Error.Conflict("DUPLICATE");
        err.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void Internal_SetsInternalType()
    {
        var err = Error.Internal("DB_ERROR");
        err.Type.Should().Be(ErrorType.Internal);
    }

    [Fact]
    public void DefaultMessage_GeneratedWhenNullPassed()
    {
        var err = Error.NotFound("CODE");
        err.Message.Should().NotBeNullOrEmpty();
    }
}

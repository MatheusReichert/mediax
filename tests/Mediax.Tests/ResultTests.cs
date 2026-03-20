using FluentAssertions;
using Mediax.Core;
using Xunit;

namespace Mediax.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Ok_CreatesSuccessResult()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_CreatesFailureResult()
    {
        var error = Error.NotFound("ORDER_NOT_FOUND", "Order was not found.");
        var result = Result<int>.Fail(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Match_CallsOkBranchOnSuccess()
    {
        var result = Result<string>.Ok("hello");
        var matched = result.Match(ok: v => $"success:{v}", fail: e => $"fail:{e.Code}");
        matched.Should().Be("success:hello");
    }

    [Fact]
    public void Match_CallsFailBranchOnFailure()
    {
        var result = Result<string>.Fail(Error.Validation("BAD_INPUT"));
        var matched = result.Match(ok: v => $"success:{v}", fail: e => $"fail:{e.Code}");
        matched.Should().Be("fail:BAD_INPUT");
    }

    [Fact]
    public void Map_TransformsValueOnSuccess()
    {
        var result = Result<int>.Ok(5).Map(x => x * 2);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(10);
    }

    [Fact]
    public void Map_PreservesErrorOnFailure()
    {
        var error = Error.Internal("ERR");
        var result = Result<int>.Fail(error).Map(x => x * 2);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Bind_ChainsResultsOnSuccess()
    {
        var result = Result<int>.Ok(3)
            .Bind(x => x > 0 ? Result<string>.Ok($"positive:{x}") : Result<string>.Fail(Error.Validation("NONPOS")));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("positive:3");
    }

    [Fact]
    public void Bind_ShortCircuitsOnFailure()
    {
        var error = Error.Conflict("CONFLICT");
        var calls = 0;
        var result = Result<int>.Fail(error).Bind(x => { calls++; return Result<string>.Ok(""); });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        calls.Should().Be(0);
    }

    [Fact]
    public void ImplicitConversion_FromValue()
    {
        Result<int> result = 99;
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(99);
    }

    [Fact]
    public void ImplicitConversion_FromError()
    {
        var error = Error.NotFound("NF");
        Result<int> result = error;
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
}

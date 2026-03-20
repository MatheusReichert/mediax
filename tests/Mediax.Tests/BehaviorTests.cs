using FluentAssertions;
using FluentValidation;
using Mediax.Behaviors;
using Mediax.Core;
using Mediax.Runtime;
using Mediax.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mediax.Tests;

public sealed class BehaviorTests
{
    private static IServiceProvider BuildSp(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediax(DispatchTable.Handlers);
        DispatchTable.RegisterAll(services);
        extra?.Invoke(services);
        var sp = services.BuildServiceProvider();
        MediaxRuntime.Init(sp);
        return sp;
    }

    [Fact]
    public async Task ValidationBehavior_PassesWhenValidatorsPass()
    {
        BuildSp(services =>
        {
            services.AddScoped(typeof(IBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddScoped<IValidator<AddNumbersCommand>, AddNumbersValidator>();
        });

        var result = await new AddNumbersCommand(2, 3).Send(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);
    }

    [Fact]
    public async Task ValidationBehavior_ReturnsValidationError_WhenValidatorFails()
    {
        BuildSp(services =>
        {
            services.AddScoped(typeof(IBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddScoped<IValidator<AddNumbersCommand>, AddNumbersValidator>();
        });

        // A = -1 should fail the validator
        var result = await new AddNumbersCommand(-1, 3).Send(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Details.Should().ContainKey("A");
    }

    [Fact]
    public async Task LogBehavior_DoesNotAffectResult()
    {
        BuildSp(services =>
        {
            services.AddScoped(typeof(IBehavior<,>), typeof(LogBehavior<,>));
        });

        var result = await new EchoQuery("log-test").Send(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("log-test");
    }
}

// Validator for test
public sealed class AddNumbersValidator : AbstractValidator<AddNumbersCommand>
{
    public AddNumbersValidator()
    {
        RuleFor(x => x.A).GreaterThanOrEqualTo(0).WithMessage("A must be non-negative.");
        RuleFor(x => x.B).GreaterThanOrEqualTo(0).WithMessage("B must be non-negative.");
    }
}

using FluentValidation;
using Mediax.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Behaviors;

/// <summary>
/// Runs all registered FluentValidation <see cref="IValidator{T}"/> instances for the request.
/// Returns a <see cref="Error.Validation"/> result when validation fails instead of throwing.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next(request, ct);

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
        {
            var details = failures
                .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(f => f.ErrorMessage).ToArray(),
                    StringComparer.Ordinal);

            return Result<TResponse>.Fail(
                Error.Validation(
                    code: $"{typeof(TRequest).Name}.Validation",
                    message: "One or more validation errors occurred.",
                    details: details));
        }

        return await next(request, ct);
    }
}

using Mediax.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Behaviors;

/// <summary>
/// Wraps the handler execution in an EF Core transaction.
/// Only activates when a <see cref="DbContext"/> is registered in the DI scope.
/// The transaction is committed on success and rolled back on failure or exception.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly DbContext? _dbContext;

    public TransactionBehavior(IServiceProvider serviceProvider)
    {
        _dbContext = serviceProvider.GetService<DbContext>();
    }

    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (_dbContext == null)
            return await next(request, ct);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await next(request, ct);

            if (result.IsSuccess)
                await tx.CommitAsync(ct);
            else
                await tx.RollbackAsync(ct);

            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}

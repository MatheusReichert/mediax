using System.Runtime.CompilerServices;

namespace Mediax.Core;

public static class ResultExtensions
{
    public static async ValueTask<Result<TTarget>> As<TTarget, TSource>(this ValueTask<Result<TSource>> task)
    {
        Result<TSource> result = await task;
        if (result.IsFailure) return Result<TTarget>.Fail(result.Error!);

        if (typeof(TSource) == typeof(TTarget)) return Result<TTarget>.Ok((TTarget)(object)result.Value!);
        if (result.Value is TTarget target) return Result<TTarget>.Ok(target);

        throw new InvalidCastException($"Cannot cast result of {typeof(TSource)} to {typeof(TTarget)}");
    }

    public static ValueTask<Result<Unit>> AsUnit<TSource>(this ValueTask<Result<TSource>> task)
    {
        // When TSource is already Unit (IHandler<TEvent, Unit>), the Result<Unit> structs are
        // layout-identical — reinterpret the ValueTask in-place with zero cost.
        if (typeof(TSource) == typeof(Unit))
        {
            return Unsafe.As<ValueTask<Result<TSource>>, ValueTask<Result<Unit>>>(ref task);
        }

        // Fast-path: ValueTask already completed (Singleton handlers always hit this).
        // Avoids allocating an async state machine on the hot path.
        if (task.IsCompletedSuccessfully)
        {
#pragma warning disable MA0042 // .Result is safe: IsCompletedSuccessfully guarantees no blocking
            Result<TSource> r = task.Result;
#pragma warning restore MA0042
            return r.IsFailure
                ? new ValueTask<Result<Unit>>(Result<Unit>.Fail(r.Error!))
                : new ValueTask<Result<Unit>>(Result<Unit>.Ok(Unit.Value));
        }

        return AsUnitAsync(task);
    }

    private static async ValueTask<Result<Unit>> AsUnitAsync<TSource>(ValueTask<Result<TSource>> task)
    {
        Result<TSource> result = await task.ConfigureAwait(false);
        if (result.IsFailure) return Result<Unit>.Fail(result.Error!);
        return Result<Unit>.Ok(Unit.Value);
    }
}

namespace Mediax.Core;

/// <summary>
/// A discriminated union representing either a successful value or a failure with an <see cref="Error"/>.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T? Value => IsSuccess ? _value : default;
    public Error? Error => IsSuccess ? null : _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(Error error) => new(error);

    /// <summary>Pattern matches the result, calling <paramref name="ok"/> on success or <paramref name="fail"/> on failure.</summary>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(fail);
        return IsSuccess ? ok(_value!) : fail(_error!);
    }

    /// <summary>Transforms the success value using <paramref name="transform"/>, preserving any failure.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return IsSuccess ? Result<TOut>.Ok(transform(_value!)) : Result<TOut>.Fail(_error!);
    }

    /// <summary>Chains another Result-producing operation, short-circuiting on failure.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        return IsSuccess ? transform(_value!) : Result<TOut>.Fail(_error!);
    }

    public override string ToString()
        => IsSuccess ? $"Ok({_value})" : $"Fail({_error})";

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}

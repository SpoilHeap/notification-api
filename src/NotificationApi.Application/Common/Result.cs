namespace NotificationApi.Application.Common;

/// <summary>Either a value or an <see cref="Common.Error"/>. Expected failures use this instead of exceptions.</summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error) => _error = error;

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"The result failed with '{_error!.Code}'; it has no value.");

    public Error Error => IsFailure ? _error! : throw new InvalidOperationException("The result succeeded; it has no error.");

#pragma warning disable CA1000 // Factory methods on the generic type keep call sites short (Result<T>.Success(x)).
    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }
#pragma warning restore CA1000

#pragma warning disable CA2225 // Success/Failure are the named alternatives.
    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
#pragma warning restore CA2225
}

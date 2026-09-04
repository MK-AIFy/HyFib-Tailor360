using System.Diagnostics.CodeAnalysis;

namespace Tailor360.Platform.Abstractions.Results;

/// <summary>
/// The outcome of an operation that either succeeds or fails with a described <see cref="Error"/>.
/// Application services return <see cref="Result"/> rather than throwing for expected failures;
/// exceptions remain reserved for defects and infrastructure faults.
/// </summary>
public class Result
{
    /// <summary>Initialises a result. Use the factory methods rather than calling this directly.</summary>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure, or <see cref="Error.None"/> when the operation succeeded.</summary>
    public Error Error { get; }

    /// <summary>A successful result carrying no value.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>A failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>A successful result carrying a value.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>A failed result of a value-returning operation.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>The outcome of an operation that yields a value when it succeeds.</summary>
/// <typeparam name="TValue">The value produced on success.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
        => _value = value;

    /// <summary>The produced value. Throws when the result is a failure.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The value of a failed result cannot be read ({Error.Code}).");

    /// <summary>Attempts to read the value without throwing.</summary>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess && value is not null;
    }

    /// <summary>Implicitly wraps a value in a successful result.</summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}

namespace Tailor360.Platform.Abstractions.Results;

/// <summary>
/// Classifies a failure so that transport layers can map it without inspecting messages.
/// </summary>
public enum ErrorType
{
    /// <summary>The request was malformed or violated a validation rule.</summary>
    Validation,

    /// <summary>The requested resource does not exist or is not visible to the caller.</summary>
    NotFound,

    /// <summary>The request conflicts with the current state of the resource.</summary>
    Conflict,

    /// <summary>The caller is authenticated but not permitted to perform the operation.</summary>
    Forbidden,

    /// <summary>The caller is not authenticated.</summary>
    Unauthenticated,

    /// <summary>A precondition on the operation was not satisfied.</summary>
    PreconditionFailed,

    /// <summary>A dependency failed in a way the caller cannot correct.</summary>
    Unavailable,

    /// <summary>An unexpected failure.</summary>
    Unexpected,
}

/// <summary>
/// A machine-readable failure. <paramref name="Code"/> is stable and safe to log and to branch on;
/// <paramref name="Message"/> is for humans and must never contain personal data or secrets.
/// </summary>
/// <param name="Code">Stable dotted code, for example <c>orders.job.already_dispatched</c>.</param>
/// <param name="Message">Operator-facing description. Never include personal data or secrets.</param>
/// <param name="Type">The failure classification used for transport mapping.</param>
/// <param name="Target">Optional field or resource the failure relates to.</param>
public readonly record struct Error(string Code, string Message, ErrorType Type, string? Target = null)
{
    /// <summary>The absence of an error.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Unexpected);

    /// <summary>Creates a validation failure for a named field.</summary>
    public static Error Validation(string code, string message, string? target = null)
        => new(code, message, ErrorType.Validation, target);

    /// <summary>Creates a not-found failure.</summary>
    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    /// <summary>Creates a conflict failure, typically an optimistic-concurrency or state clash.</summary>
    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    /// <summary>Creates a forbidden failure.</summary>
    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);

    /// <summary>Creates a precondition failure, for example an unmet dispatch gate.</summary>
    public static Error PreconditionFailed(string code, string message)
        => new(code, message, ErrorType.PreconditionFailed);

    /// <summary>Creates an unavailable-dependency failure.</summary>
    public static Error Unavailable(string code, string message)
        => new(code, message, ErrorType.Unavailable);
}

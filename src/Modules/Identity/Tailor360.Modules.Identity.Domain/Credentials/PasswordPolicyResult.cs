using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Domain.Credentials;

/// <summary>
/// Every rule a candidate password broke. A password check is one of the few places where returning a
/// single failure is wrong: the person is composing a value and needs the whole list to fix it in one
/// attempt, which is also what the RFC 9457 field-error shape carries.
/// </summary>
public sealed record PasswordPolicyResult
{
    /// <summary>Creates a result from the failures found.</summary>
    public PasswordPolicyResult(IReadOnlyList<Error> failures)
        => Failures = failures ?? throw new ArgumentNullException(nameof(failures));

    /// <summary>A result carrying no failures.</summary>
    public static PasswordPolicyResult Acceptable { get; } = new([]);

    /// <summary>The rules the candidate broke, in the order they were checked.</summary>
    public IReadOnlyList<Error> Failures { get; }

    /// <summary>True when the candidate broke no rule.</summary>
    public bool IsAcceptable => Failures.Count == 0;
}

namespace Tailor360.Modules.Identity.Application.Sessions;

/// <summary>
/// The session a self-service request is being made under, as the handler needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// Handlers that change an account's security settings take this rather than a bare user identifier, so
/// that "which account" and "what has this caller actually proved" arrive together and neither can be
/// forgotten. An endpoint that passed only an identifier would compile, run, and quietly allow whoever
/// holds the password to enrol their own authenticator; requiring the pair makes that omission
/// impossible to write.
/// </para>
/// <para>
/// Every field comes from the session ticket, which was rebuilt from the session row on this request.
/// Nothing here is taken from the request body, so a client cannot claim to have satisfied a factor.
/// </para>
/// </remarks>
/// <param name="UserId">The account the session belongs to.</param>
/// <param name="SessionId">The session itself, when the request carried one.</param>
/// <param name="SecondFactorSatisfied">True when a second factor has been satisfied on this session.</param>
public sealed record CallerSession(Guid UserId, Guid? SessionId, bool SecondFactorSatisfied);

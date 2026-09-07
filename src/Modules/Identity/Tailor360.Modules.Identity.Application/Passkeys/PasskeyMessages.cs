namespace Tailor360.Modules.Identity.Application.Passkeys;

/// <summary>One registered passkey, as its holder sees it in their own list.</summary>
/// <param name="PasskeyId">Identity of the registration, which is what a removal names.</param>
/// <param name="Label">The name the holder gave it, so two keys can be told apart.</param>
/// <param name="CreatedAt">When it was registered.</param>
/// <param name="LastUsedAt">When it was last used to sign in.</param>
/// <param name="IsBackedUp">
/// True when the credential is synchronised to the holder's other devices. Worth showing, because a
/// synchronised passkey is as available as their password manager and a device-bound one is not.
/// </param>
public sealed record RegisteredPasskey(
    Guid PasskeyId,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool IsBackedUp);

/// <summary>A passkey sign-in that completed.</summary>
/// <param name="Session">The session issued, whose token the caller places in the cookie.</param>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The name to greet the holder by.</param>
/// <param name="MustChangePassword">True when the holder must set a new password before working.</param>
/// <param name="Factors">
/// What else the account holds. No challenge follows a passkey assertion, so this is not a list of what
/// the interface must ask for; it is what the account can prove, which the security screens show and
/// which would be wrong if it were guessed from the credential that happened to sign in.
/// </param>
public sealed record PasskeySignInSucceeded(
    Sessions.IssuedSession Session,
    Guid UserId,
    string DisplayName,
    bool MustChangePassword,
    Authentication.AvailableFactors Factors);

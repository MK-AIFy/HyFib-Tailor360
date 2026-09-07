namespace Tailor360.Modules.Identity.Domain.Recovery;

/// <summary>
/// What a recovery token entitles its holder to do. The purpose is bound into the token row rather
/// than inferred from the endpoint that receives it, so a token issued to complete an invitation
/// cannot be replayed against the password-reset endpoint or the other way round.
/// </summary>
public enum RecoveryPurpose
{
    /// <summary>Set a new password on an account whose holder has forgotten theirs.</summary>
    PasswordReset = 0,

    /// <summary>Set the first password on an invited account (#25 issues these).</summary>
    Invitation = 1,
}

using System.Collections.Frozen;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// The wording of every message this module sends, in one file so it can be read as a set and reviewed
/// as one.
/// </summary>
/// <remarks>
/// <b>Plain text only.</b> There is no HTML body, and that is a decision rather than an omission. An
/// HTML message would need escaping of every substituted value, would render differently in every mail
/// client, and would let a link say one thing and point at another — which is exactly the shape of the
/// phishing message these are most likely to be imitated by. Plain text shows the reader the address
/// they are about to open.
/// <para>
/// <b>Nothing sensitive is substituted.</b> The values a template may use are a display name, a link
/// and a number of minutes. No password, no recovery code, no authenticator secret and no code from
/// one has ever a reason to travel by email, and none of these templates has a placeholder for one.
/// </para>
/// <para>
/// <b>Tamil is not here yet, on purpose.</b> The client's Tamil catalogue carries English strings until
/// a native speaker has reviewed them, and the enablement gate in
/// <c>docs/nfr/accessibility-localisation.md</c> section 10.3 applies to what a person reads in an
/// email exactly as it does to what they read on a screen. A wrong translation of a security message
/// is worse than an English one, so the renderer falls back and the fallback is deliberate.
/// </para>
/// </remarks>
internal static class EmailTemplateCatalogue
{
    /// <summary>The language every template is written in and every other language falls back to.</summary>
    public const string FallbackLocale = "en-IN";

    /// <summary>The templates, keyed by template key and then by language tag.</summary>
    public static FrozenDictionary<string, FrozenDictionary<string, EmailTemplate>> Templates { get; } =
        BuildTemplates();

    private static FrozenDictionary<string, FrozenDictionary<string, EmailTemplate>> BuildTemplates()
    {
        var templates = new Dictionary<string, Dictionary<string, EmailTemplate>>(StringComparer.Ordinal)
        {
            ["identity.password-recovery"] = new(StringComparer.OrdinalIgnoreCase)
            {
                [FallbackLocale] = new(
                    "Set a new Tailor 360 password",
                    """
                    Hello {displayName},

                    Someone asked to set a new password for your HyFib Tailor 360 account. If it was
                    you, open this address within {minutes} minutes:

                    {link}

                    The link works once and then stops working. After you set the password you will
                    still be asked for your authenticator code, because changing a password does not
                    change your second factor.

                    If you did not ask for this, you do not need to do anything: without the link
                    nothing changes. Tell your shop administrator if it keeps happening.

                    HyFib Tailor 360
                    """),
            },
            ["identity.invitation"] = new(StringComparer.OrdinalIgnoreCase)
            {
                [FallbackLocale] = new(
                    "Your Tailor 360 account is ready to set up",
                    """
                    Hello {displayName},

                    An account has been created for you in HyFib Tailor 360. Open this address within
                    {minutes} minutes to choose your password:

                    {link}

                    The link works once. After you have set a password you will be asked to set up an
                    authenticator application, which is what protects the shop's records if your
                    password is ever guessed.

                    If you were not expecting this, tell whoever manages your shop's accounts.

                    HyFib Tailor 360
                    """),
            },
            ["identity.password-changed"] = new(StringComparer.OrdinalIgnoreCase)
            {
                [FallbackLocale] = new(
                    "Your Tailor 360 password was changed",
                    """
                    Hello {displayName},

                    The password on your HyFib Tailor 360 account has just been changed, and everywhere
                    you were signed in has been signed out.

                    If that was you, there is nothing to do.

                    If it was not, someone else can reach your email. Tell your shop administrator now
                    and ask them to suspend the account: this message is the only warning you will get.

                    HyFib Tailor 360
                    """),
            },
            ["identity.mfa-reset"] = new(StringComparer.OrdinalIgnoreCase)
            {
                [FallbackLocale] = new(
                    "Your Tailor 360 authenticator was reset",
                    """
                    Hello {displayName},

                    An administrator has reset the authenticator on your HyFib Tailor 360 account, so
                    your old codes and your printed recovery codes no longer work. You will be asked to
                    set up a new authenticator the next time you sign in, and everywhere you were signed
                    in has been signed out.

                    This is what happens when someone loses the phone their codes were on. If you did
                    not ask for it, tell your shop administrator now: a reset is the one action that
                    removes a second factor without you being involved, and it is recorded with the
                    name of whoever did it.

                    HyFib Tailor 360
                    """),
            },
        };

        return templates.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);
    }
}

/// <summary>One message's wording.</summary>
/// <param name="Subject">The subject line, which may itself carry placeholders.</param>
/// <param name="Body">The plain-text body.</param>
internal sealed record EmailTemplate(string Subject, string Body);

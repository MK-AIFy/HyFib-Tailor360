using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Domain.Users;
using Tailor360.Platform.Abstractions.Ports;

namespace Tailor360.Modules.Identity.Application.Notifications;

/// <summary>
/// Renders the module's four messages and hands them to the dispatch queue.
/// </summary>
/// <remarks>
/// Nothing here waits for a network. A failure to render or to queue is logged and swallowed, which
/// looks wrong until the caller is considered: the recovery request must answer identically whether the
/// address was known, so it cannot surface a mail failure, and the password-changed alert must not
/// undo a password change that has already been committed. What a failure must not do is pass
/// unnoticed, which is what the two error-level messages are for.
/// </remarks>
/// <param name="renderer">The template renderer.</param>
/// <param name="queue">The dispatch queue.</param>
/// <param name="options">Recovery configuration, which is where the link is built from.</param>
/// <param name="logger">Logger. Never receives a body, an address or a token.</param>
public sealed class IdentityMailer(
    IEmailTemplateRenderer renderer,
    IEmailDispatchQueue queue,
    IOptions<RecoveryOptions> options,
    ILogger<IdentityMailer> logger) : IIdentityMailer
{
    /// <summary>The template that carries a password-reset link.</summary>
    public const string PasswordRecoveryTemplate = "identity.password-recovery";

    /// <summary>The template that carries an invitation link.</summary>
    public const string InvitationTemplate = "identity.invitation";

    /// <summary>The template that tells someone their password changed.</summary>
    public const string PasswordChangedTemplate = "identity.password-changed";

    /// <summary>The template that tells someone their second factor was reset.</summary>
    public const string MfaResetTemplate = "identity.mfa-reset";

    private readonly RecoveryOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public void SendPasswordRecovery(StaffUser user, string tokenValue, TimeSpan validFor)
    {
        ArgumentNullException.ThrowIfNull(user);

        Send(PasswordRecoveryTemplate, user, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["displayName"] = user.DisplayName,
            ["link"] = BuildLink(_options.ConfirmationPath, tokenValue),
            ["minutes"] = WholeMinutes(validFor),
        });
    }

    /// <inheritdoc />
    public void SendInvitation(StaffUser user, string tokenValue, TimeSpan validFor)
    {
        ArgumentNullException.ThrowIfNull(user);

        Send(InvitationTemplate, user, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["displayName"] = user.DisplayName,
            ["link"] = BuildLink(_options.InvitationPath, tokenValue),
            ["minutes"] = WholeMinutes(validFor),
        });
    }

    /// <inheritdoc />
    public void SendPasswordChangedAlert(StaffUser user, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(user);

        Send(PasswordChangedTemplate, user, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["displayName"] = user.DisplayName,
        });
    }

    /// <inheritdoc />
    public void SendMfaResetAlert(StaffUser user, DateTimeOffset resetAt)
    {
        ArgumentNullException.ThrowIfNull(user);

        Send(MfaResetTemplate, user, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["displayName"] = user.DisplayName,
        });
    }

    private void Send(string templateKey, StaffUser user, IReadOnlyDictionary<string, string> values)
    {
        var locale = user.Preferences?.Locale ?? UserPreferences.DefaultLocale;
        var rendered = renderer.Render(new EmailTemplateRequest(templateKey, user.Email, locale, values));

        if (rendered.IsFailure)
        {
            IdentityLog.TemplateRenderFailed(logger, templateKey, rendered.Error.Code);
            return;
        }

        if (!queue.Enqueue(rendered.Value))
        {
            IdentityLog.MessageNotQueued(logger, templateKey);
        }
    }

    /// <summary>
    /// Builds the link. An absolute address is what a mail client needs; a deployment that has not said
    /// what its public address is gets a relative one and a warning naming the key to set, because a
    /// broken link that is loudly explained is better than a host that refuses to start.
    /// </summary>
    private string BuildLink(string path, string tokenValue)
    {
        var query = $"{path}?token={Uri.EscapeDataString(tokenValue)}";

        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            IdentityLog.PublicBaseUrlMissing(logger);
            return query;
        }

        return string.Concat(_options.PublicBaseUrl.TrimEnd('/'), query);
    }

    /// <summary>
    /// How long the holder has, in whole minutes, rounded down and never below one. The message states
    /// a duration rather than an instant so that it needs no timezone and no date format, which is one
    /// fewer thing to get wrong in a message nobody reads until it matters.
    /// </summary>
    private static string WholeMinutes(TimeSpan validFor)
        => Math.Max(1, (int)Math.Floor(validFor.TotalMinutes))
            .ToString(CultureInfo.InvariantCulture);
}

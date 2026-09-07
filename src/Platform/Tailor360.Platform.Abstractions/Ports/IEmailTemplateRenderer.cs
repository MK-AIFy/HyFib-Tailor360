namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Turns a template key and a set of values into the message an <see cref="IEmailSender"/> delivers.
/// Introduced by #23 for recovery and security-alert messages, reused by #25 for invitations and
/// replaced by the Notifications module's fuller template store in #47.
/// </summary>
/// <remarks>
/// The port is deliberately small, and the reason is worth stating: the wording of a security message
/// is not something a caller should be free to compose at the call site. A handler says <em>which</em>
/// message to send and supplies the values it may fill in; what the recipient actually reads is a
/// reviewed template. That is what makes it possible to check, once, that no message contains a
/// password, a code or anything else that must not travel by email.
/// <para>
/// A renderer never logs the rendered message, the recipient address or any supplied value: a rendered
/// recovery message contains a single-use credential in its link, so writing one to a log would put
/// that credential in a store with weaker access rules than the database it came from.
/// </para>
/// </remarks>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renders one message. Fails rather than guessing when the template is unknown to this
    /// deployment, or when a placeholder the template needs has no supplied value — a message that
    /// reached someone with <c>{link}</c> still in it would be worse than one that was never sent.
    /// </summary>
    /// <param name="request">Which template, for whom, in which language, with which values.</param>
    Results.Result<EmailMessage> Render(EmailTemplateRequest request);
}

/// <summary>One request to render a message.</summary>
/// <param name="TemplateKey">
/// The template, for example <c>identity.password-recovery</c>. Keys are stable: a deployment's
/// delivery reports and a support engineer's question both refer to them.
/// </param>
/// <param name="To">Recipient address. Never logged.</param>
/// <param name="Locale">
/// The recipient's BCP 47 language tag. A renderer falls back to the language it has rather than
/// failing, because a message in the wrong language still reaches the person and a missing one does not.
/// </param>
/// <param name="Values">
/// The values the template may substitute. Keys are placeholder names without braces; values are
/// plain text and are never logged.
/// </param>
public sealed record EmailTemplateRequest(
    string TemplateKey,
    string To,
    string Locale,
    IReadOnlyDictionary<string, string> Values);

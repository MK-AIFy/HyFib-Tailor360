using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Delivers a message to an SMTP relay — Mailpit on a developer's machine, a configured relay
/// elsewhere.
/// </summary>
/// <remarks>
/// The framework's own SMTP client is used rather than a library, which keeps a vendor SDK out of a
/// business module (ARCH-009) and keeps this adapter to the shape #47 will replace: it sends one
/// message, it reports success or failure, and it knows nothing about templates, retries or queues.
/// <para>
/// Nothing that identifies the message reaches an exception message or a log line from here. A failure
/// returns a code; the caller logs the template tag and the exception, and the exception from the mail
/// stack does not contain the body.
/// </para>
/// </remarks>
/// <param name="options">Delivery configuration.</param>
public sealed class SmtpEmailSender(IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    /// <summary>The failure a relay that would not take the message reports.</summary>
    public static Error NotDelivered { get; } = Error.Unavailable(
        "identity.email-not-delivered",
        "The message could not be handed to the mail relay.");

    private readonly EmailDeliveryOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseStartTls,
            Timeout = _options.TimeoutSeconds * 1000,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrEmpty(_options.UserName))
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromDisplayName),
            Subject = message.Subject,
            Body = message.PlainTextBody,
            IsBodyHtml = false,
        };

        mail.To.Add(new MailAddress(message.To));

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return Result.Success();
        }
        catch (SmtpException)
        {
            return Result.Failure(NotDelivered);
        }
        catch (InvalidOperationException)
        {
            // Thrown when the configured host is unusable, which is a configuration failure rather
            // than a transient one. It is reported the same way: the caller cannot tell the difference
            // and neither can the person waiting for the message.
            return Result.Failure(NotDelivered);
        }
    }
}

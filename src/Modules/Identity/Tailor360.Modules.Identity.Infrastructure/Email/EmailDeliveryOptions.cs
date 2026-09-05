using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>How outbound email leaves this deployment.</summary>
public sealed class EmailDeliveryOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:Email";

    /// <summary>Where messages go.</summary>
    public EmailDelivery Delivery { get; set; } = EmailDelivery.Smtp;

    /// <summary>
    /// The relay's host. The default is the local Mailpit the development compose stack starts, which
    /// is why a developer sees recovery messages without configuring anything.
    /// </summary>
    [Required]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>The relay's port. 1025 is Mailpit's; a real relay is usually 587.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Whether to upgrade the connection with STARTTLS. Required for any relay off this host.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>The relay account, when it needs one.</summary>
    public string? UserName { get; set; }

    /// <summary>
    /// The relay password. Like every secret, it arrives as a file under the secrets directory —
    /// <c>Identity__Email__Password</c> — never as an environment variable and never in a committed
    /// file. See <c>docs/platform/secrets.md</c>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>The address messages are sent from.</summary>
    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@tailor360.local";

    /// <summary>The name shown beside that address.</summary>
    [Required]
    public string FromDisplayName { get; set; } = "HyFib Tailor 360";

    /// <summary>How long one send may take before it is abandoned.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// How many messages may wait for delivery. The queue is bounded so that a relay that has stopped
    /// answering costs memory that is capped rather than memory that grows until the host dies; a full
    /// queue drops the newest message and logs it.
    /// </summary>
    [Range(16, 10_000)]
    public int QueueCapacity { get; set; } = 512;
}

/// <summary>Where outbound messages go.</summary>
public enum EmailDelivery
{
    /// <summary>To an SMTP relay.</summary>
    Smtp = 0,

    /// <summary>
    /// Into memory, where a test can read them. Also the setting for an environment that must not send
    /// anything at all — a restored copy of production, for instance, where sending would mean writing
    /// to real staff.
    /// </summary>
    Collect = 1,
}

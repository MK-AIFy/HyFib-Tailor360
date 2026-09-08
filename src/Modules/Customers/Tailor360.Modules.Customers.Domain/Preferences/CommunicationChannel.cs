namespace Tailor360.Modules.Customers.Domain.Preferences;

/// <summary>
/// A way the shop can reach a customer.
/// </summary>
/// <remarks>
/// The three <c>docs/architecture/context.md</c> row "Customer message" names. This is the set a
/// customer may choose from; which of them a given message actually goes out on is Notifications'
/// routing decision (#47), evaluated against this preference, the consent for the purpose, quiet hours,
/// de-duplication and rate limits — all server-side, before any send.
/// </remarks>
public enum CommunicationChannel
{
    /// <summary>A text message.</summary>
    Sms,

    /// <summary>A WhatsApp message.</summary>
    WhatsApp,

    /// <summary>An email.</summary>
    Email,
}

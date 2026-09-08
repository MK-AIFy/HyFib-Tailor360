using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Customers.Contracts.Preferences;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Preferences;

namespace Tailor360.Modules.Customers.Infrastructure.Persistence;

/// <summary>
/// The published communication-preference query, over the <c>customers</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The language falls back to the customer's own record, not to a constant.</strong> A
/// customer who has never been asked which channels she accepts has still told the shop which language
/// to write to her in, at the counter, when the record was created. Answering with that is answering
/// with something somebody chose; answering with a hard-coded default would be inventing a product
/// decision at a module boundary, which is what <c>CLAUDE.md</c> section 8 forbids. The module's
/// declared default is used only when there is no customer record either, and then it is the module's
/// own default for the column rather than one this query made up.
/// </para>
/// <para>
/// The preference is read whole rather than projected. It is one row keyed by the customer, its
/// channel set is a stored collection and its quiet hours are a complex property, so there is nothing
/// a projection would save and a good deal it would obscure.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class CommunicationPreferenceQuery(CustomersDbContext context)
    : ICommunicationPreferenceQuery
{
    /// <inheritdoc />
    public async Task<CommunicationPreference> GetAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var preference = await context.CommunicationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.CustomerId == customerId, cancellationToken);

        if (preference is null)
        {
            var language = await context.Customers
                .AsNoTracking()
                .Where(customer => customer.Id == customerId)
                .Select(customer => customer.Language)
                .FirstOrDefaultAsync(cancellationToken);

            return CommunicationPreference.NotRecorded(
                customerId,
                string.IsNullOrEmpty(language) ? CustomerDetails.DefaultLanguage : language);
        }

        return new CommunicationPreference(
            customerId,
            HasBeenRecorded: true,
            [.. preference.AllowedChannels.Select(ChannelOf).OfType<MessageChannel>()],
            preference.Language,
            preference.QuietHours is null
                ? null
                : new QuietWindow(preference.QuietHours.Start, preference.QuietHours.End));
    }

    /// <summary>
    /// Maps the module's channel onto the published one.
    /// </summary>
    /// <remarks>
    /// Written out rather than cast, so the two enumerations may be numbered independently. A channel
    /// matching no arm is dropped rather than guessed: the discard is unreachable — a unit test asserts
    /// the two enumerations carry the same members, and the domain refuses an undefined one — and a
    /// channel nothing can name is not one a message should be sent on.
    /// </remarks>
    private static MessageChannel? ChannelOf(CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.Sms => MessageChannel.Sms,
        CommunicationChannel.WhatsApp => MessageChannel.WhatsApp,
        CommunicationChannel.Email => MessageChannel.Email,
        _ => null,
    };
}

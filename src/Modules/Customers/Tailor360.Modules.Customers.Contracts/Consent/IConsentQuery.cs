namespace Tailor360.Modules.Customers.Contracts.Consent;

/// <summary>
/// What another module may know about a customer's consent.
/// </summary>
/// <remarks>
/// <para>
/// Consent records belong to Customers, and nothing outside it reads <c>customers.consent_records</c>
/// (<c>docs/architecture/module-ownership.md</c> section 5.2). Two modules nevertheless have to act on
/// one. <strong>Media (#31)</strong> stores the <see cref="ConsentState.RecordId"/> it relied on
/// against every photograph it keeps, so that a later question about why an image exists has an answer
/// that names the moment somebody agreed. <strong>Notifications (#47)</strong> evaluates this query
/// server-side before every send, because a consent check the client performs is a consent check the
/// client can skip.
/// </para>
/// <para>
/// <strong>The answer is a state, never an absence.</strong> A customer nobody has asked is
/// <see cref="ConsentStatus.NeverAsked"/> rather than null, so a consumer cannot write
/// <c>?? true</c> and turn "we have no idea" into permission. The domain's own enumeration has no such
/// member on purpose — inside the module, absence of a row <em>is</em> how the system says nobody
/// asked — but a published query has to answer the question it was given, and the honest answer to
/// "may I send this" when nobody has ever asked her is no.
/// </para>
/// <para>
/// <strong>This query does not tell you whether the customer exists.</strong> It tells you what may be
/// done, and for a customer nobody has heard of the answer is nothing. Existence is the question
/// <see cref="Tailor360.Modules.Customers.Contracts.Customers.ICustomerSnapshotQuery"/> answers.
/// </para>
/// </remarks>
public interface IConsentQuery
{
    /// <summary>The standing answer for one purpose.</summary>
    /// <remarks>
    /// <para>
    /// The standing answer is the most recent record for the purpose, computed rather than stored:
    /// withdrawing appends a <see cref="ConsentStatus.Withdrawn"/> record and agreeing again appends
    /// another <see cref="ConsentStatus.Granted"/> one, so there is no flag that could fall out of step
    /// with the rows it summarises.
    /// </para>
    /// <para>
    /// One purpose and one customer, because that is what both consumers ask: Media checks
    /// <c>photo_capture</c> for the customer in front of the camera, and Notifications checks the
    /// purpose of the message it is about to send. A batch run that sends to many customers at once
    /// wants the same question across a list of them, and that overload is the obvious addition when
    /// #47 arrives with a batch send — an additive change within this major version, and one worth
    /// making then rather than guessing its shape now.
    /// </para>
    /// </remarks>
    /// <param name="customerId">The customer.</param>
    /// <param name="purposeKey">The purpose, by its stable key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The standing answer, which is never null.</returns>
    Task<ConsentState> GetAsync(
        Guid customerId,
        string purposeKey,
        CancellationToken cancellationToken = default);
}

/// <summary>Where a customer stands on one purpose.</summary>
/// <param name="CustomerId">The customer the question was asked about.</param>
/// <param name="PurposeKey">The purpose the question was asked about.</param>
/// <param name="Status">The standing answer.</param>
/// <param name="WordingVersion">
/// The version of the wording the customer was asked under, or null when nobody has asked. It is what
/// makes the answer evidence rather than a flag: a later wording version re-consents nobody, and a
/// consumer comparing this against the current version is how a shop finds who to ask again.
/// </param>
/// <param name="RecordedAt">When the answer was given, or null when nobody has asked.</param>
/// <param name="Source">
/// How the answer reached the system — "counter, verbal", for instance — or null when nobody has
/// asked.
/// </param>
/// <param name="RecordId">
/// The consent record this answer comes from, or null when nobody has asked. Media stores it against
/// the object it relied on, which is why it crosses the boundary at all.
/// </param>
public sealed record ConsentState(
    Guid CustomerId,
    string PurposeKey,
    ConsentStatus Status,
    int? WordingVersion,
    DateTimeOffset? RecordedAt,
    string? Source,
    Guid? RecordId)
{
    /// <summary>
    /// Whether the shop may act on this purpose.
    /// </summary>
    /// <remarks>
    /// The one reading a consumer should take. Comparing <see cref="Status"/> by hand invites the
    /// mistake of treating <see cref="ConsentStatus.NeverAsked"/> or
    /// <see cref="ConsentStatus.Withdrawn"/> as merely "not declined", and there is no reading of
    /// either that permits anything.
    /// </remarks>
    public bool IsGranted => Status == ConsentStatus.Granted;

    /// <summary>The answer for a purpose the customer has never been asked about.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="purposeKey">The purpose.</param>
    /// <returns>The state.</returns>
    public static ConsentState NeverAsked(Guid customerId, string purposeKey)
        => new(customerId, purposeKey, ConsentStatus.NeverAsked, null, null, null, null);
}

/// <summary>
/// The standing answer for one purpose, as another module sees it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NeverAsked"/> is deliberately the zero value. A status that arrives default-constructed,
/// deserialised from a payload that omitted the field, or cast from a number nobody recognised is then
/// the one that permits nothing, which is the only direction in which such a mistake is survivable.
/// </para>
/// <para>
/// <see cref="Withdrawn"/> stays separate from <see cref="Declined"/> across the boundary for the
/// reason the module keeps them apart: one is an answer given at the start and the other is an answer
/// changed later, and a consumer deciding whether to ask again needs to know which
/// (<c>docs/nfr/data-classification.md</c> section 4.2).
/// </para>
/// </remarks>
public enum ConsentStatus
{
    /// <summary>Nobody has asked this customer about this purpose. Permits nothing.</summary>
    NeverAsked = 0,

    /// <summary>The customer agreed, against the wording version recorded with the answer.</summary>
    Granted = 1,

    /// <summary>The customer was asked and said no.</summary>
    Declined = 2,

    /// <summary>The customer had agreed and has since withdrawn. Honoured immediately.</summary>
    Withdrawn = 3,
}

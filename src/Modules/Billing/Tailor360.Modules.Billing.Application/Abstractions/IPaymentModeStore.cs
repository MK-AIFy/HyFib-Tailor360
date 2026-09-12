using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Billing.Application.Abstractions;

/// <summary>The organisation's payment modes, as persistence answers for them.</summary>
public interface IPaymentModeStore
{
    /// <summary>Every mode of the organisation, active or not, by code.</summary>
    Task<IReadOnlyList<PaymentMode>> ListAsync(Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>One mode, or null when it is not the organisation's.</summary>
    Task<PaymentMode?> FindAsync(Guid paymentModeId, Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Tracks a new mode.</summary>
    void Add(PaymentMode mode);

    /// <summary>The optimistic-concurrency token of a tracked mode.</summary>
    EntityTag EntityTagOf(PaymentMode mode);

    /// <summary>Commits; a code already taken or a row that moved comes back as a failure, never an exception.</summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}

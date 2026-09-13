using Tailor360.Modules.Billing.Application.Abstractions;
using Tailor360.Modules.Billing.Domain;
using Tailor360.Modules.Billing.Domain.Payments;
using Tailor360.Modules.Identity.Contracts.Directory;
using Tailor360.Platform.Abstractions.Auditing;
using Tailor360.Platform.Abstractions.Concurrency;
using Tailor360.Platform.Abstractions.Results;
using Tailor360.Platform.Abstractions.Time;

namespace Tailor360.Modules.Billing.Application.Payments;

/// <summary>Administers the payment modes: the name, the flags, the branch restriction and the active state.</summary>
/// <param name="modes">The store.</param>
/// <param name="branches">Identity's branch directory, to check a restriction names the organisation's branches.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">The clock.</param>
public sealed class PaymentModeHandler(
    IPaymentModeStore modes,
    IBranchDirectory branches,
    IAuditWriter audit,
    IClock clock)
{
    /// <summary>The audit action of a change to a mode.</summary>
    public const string ChangedAction = "billing.payment_mode.changed";

    /// <summary>Changes a mode. Nothing about a payment already recorded moves: it keeps the mode it was taken in.</summary>
    public async Task<Result<AdministeredPaymentMode>> DescribeAsync(DescribePaymentModeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var mode = await modes.FindAsync(command.PaymentModeId, command.OrganisationId, cancellationToken);
        if (mode is null)
        {
            return Result.Failure<AdministeredPaymentMode>(BillingErrors.PaymentModeNotFound);
        }

        if (!command.ExpectedVersion.Matches(modes.EntityTagOf(mode)))
        {
            return Result.Failure<AdministeredPaymentMode>(BillingErrors.PaymentModeChanged);
        }

        var branchIds = command.BranchIds.Distinct().ToList();
        if (branchIds.Count > 0)
        {
            var known = await branches.FindManyAsync(branchIds, cancellationToken);
            if (known.Count != branchIds.Count || known.Any(branch => branch.OrganisationId != command.OrganisationId))
            {
                return Result.Failure<AdministeredPaymentMode>(BillingErrors.BranchNotFound("branchIds"));
            }
        }

        var before = PaymentModeSnapshot.Of(mode);
        var now = clock.UtcNow;
        var described = mode.Describe(command.Details, now, command.By);
        if (described.IsFailure)
        {
            return Result.Failure<AdministeredPaymentMode>(described.Error);
        }

        var moved = described.Value;
        moved |= mode.SetBranches(branchIds, now, command.By);
        moved |= mode.SetActive(command.IsActive, now, command.By);
        if (!moved)
        {
            return Result.Success(new AdministeredPaymentMode(mode, modes.EntityTagOf(mode)));
        }

        var saved = await modes.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure<AdministeredPaymentMode>(saved.Error);
        }

        await BillingAudit.RecordAsync(
            audit, ChangedAction, BillingAudit.PaymentModeEntity, mode.Id,
            $"Payment mode {mode.Code} changed.",
            null, before, PaymentModeSnapshot.Of(mode), cancellationToken);

        return Result.Success(new AdministeredPaymentMode(mode, modes.EntityTagOf(mode)));
    }
}

/// <summary>Change a payment mode.</summary>
/// <param name="PaymentModeId">The mode.</param>
/// <param name="OrganisationId">The caller's organisation.</param>
/// <param name="ExpectedVersion">The token the caller read; a mismatch is refused.</param>
/// <param name="Details">The name and the flags.</param>
/// <param name="BranchIds">The branches the mode is restricted to; empty for every branch.</param>
/// <param name="IsActive">Whether new payments may use it.</param>
/// <param name="By">Who.</param>
public sealed record DescribePaymentModeCommand(
    Guid PaymentModeId,
    Guid OrganisationId,
    EntityTag ExpectedVersion,
    PaymentModeDetails Details,
    IReadOnlyCollection<Guid> BranchIds,
    bool IsActive,
    Guid? By);

/// <summary>A mode and the token its next change is made against.</summary>
public sealed record AdministeredPaymentMode(PaymentMode Mode, EntityTag Tag);

/// <summary>What the audit trail records of a mode: no personal data, the configuration alone.</summary>
internal sealed record PaymentModeSnapshot(string Code, string Name, bool RequiresReference, bool RequiresProvider, bool AllowedForRefund, bool IsActive, IReadOnlyList<Guid> BranchIds)
{
    public static PaymentModeSnapshot Of(PaymentMode mode)
        => new(mode.Code, mode.Name, mode.RequiresReference, mode.RequiresProvider, mode.AllowedForRefund, mode.IsActive, mode.Branches.Select(branch => branch.BranchId).OrderBy(id => id).ToList());
}

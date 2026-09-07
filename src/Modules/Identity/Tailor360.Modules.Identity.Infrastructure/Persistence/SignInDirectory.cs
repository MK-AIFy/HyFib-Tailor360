using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Authentication;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The sign-in path's view of the <c>identity</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// Every lookup loads the whole aggregate, for the reason the other store's do: the sign-in path asks
/// questions that span the password, the second factor and the remembered devices at once, and an
/// aggregate loaded in pieces answers them confidently and wrongly. What "the whole aggregate" means is
/// defined once, in <see cref="IdentityAggregate"/>, and not repeated here.
/// </para>
/// <para>
/// <b>Normalising is not decided here either.</b> A sign-in name is folded to lower case and an address
/// to upper, exactly as the write path stores them, and the rule lives in
/// <see cref="SignInIdentifier"/> because the endpoint that counts attempts needs the same answer. Two
/// copies of it meant two ideas of what "the same account" was, and the day they disagreed the symptom
/// was an account nobody could sign in to.
/// </para>
/// </remarks>
/// <param name="context">The module's context.</param>
public sealed class SignInDirectory(IdentityDbContext context) : ISignInDirectory
{
    /// <inheritdoc />
    public Task<StaffUser?> FindForSignInAsync(
        string? identifier,
        CancellationToken cancellationToken = default)
    {
        if (SignInIdentifier.Normalise(identifier) is not { } normalised)
        {
            return Task.FromResult<StaffUser?>(null);
        }

        return SignInIdentifier.IsEmailAddress(normalised)
            ? WholeAggregate()
                .FirstOrDefaultAsync(user => user.NormalisedEmail == normalised, cancellationToken)
            : WholeAggregate()
                .FirstOrDefaultAsync(user => user.UserName == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StaffUser?> FindByPasskeyAsync(
        byte[]? credentialId,
        CancellationToken cancellationToken = default)
    {
        if (credentialId is null || credentialId.Length == 0)
        {
            return null;
        }

        // Two steps rather than a join, because the account is then loaded by exactly the same query as
        // every other path uses and cannot end up with a differently populated aggregate.
        var userId = await context.PasskeyCredentials
            .Where(passkey => passkey.CredentialId == credentialId)
            .Select(passkey => (Guid?)passkey.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return userId is { } id ? await FindAsync(id, cancellationToken) : null;
    }

    /// <inheritdoc />
    public Task<StaffUser?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
        => WholeAggregate().FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

    private IQueryable<StaffUser> WholeAggregate() => IdentityAggregate.WholeAggregate(context);
}

using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Identity.Domain.Users;

namespace Tailor360.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// What "the whole account" means as a query: the one place the aggregate's include graph is written.
/// </summary>
/// <remarks>
/// <para>
/// Two ports read this aggregate — the sign-in directory and the multi-factor and recovery store — and
/// each once carried its own copy of the same six includes. Nothing was broken while they agreed,
/// because they share a change tracker; what made a single definition worth having is what happens when
/// they stop agreeing. The include graph is the definition of the aggregate, and a handler that asks a
/// partly-loaded account whether it still has a second factor gets a confident wrong answer rather than
/// an error.
/// </para>
/// <para>
/// It is more rows than any one handler reads, and that is deliberate. An account has one password, one
/// authenticator, a sheet of codes and a handful of devices, so the whole of it is a small read, and
/// every rule applied to it — may this account enrol, does it still hold a factor after a password
/// reset, has this recovery code been spent — is a question about the aggregate rather than about one of
/// its tables.
/// </para>
/// </remarks>
internal static class IdentityAggregate
{
    /// <summary>The account and everything the domain needs to answer a question about it.</summary>
    /// <param name="context">The module's context.</param>
    public static IQueryable<StaffUser> WholeAggregate(IdentityDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Users
            .Include(user => user.Password)
            .Include(user => user.Totp)
            .Include(user => user.Preferences)
            .Include(user => user.RecoveryCodes)
            .Include(user => user.Passkeys)
            .Include(user => user.TrustedDevices);
    }
}

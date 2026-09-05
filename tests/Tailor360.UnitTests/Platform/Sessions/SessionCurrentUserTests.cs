using Shouldly;
using Tailor360.Platform.Security.Authentication;
using Tailor360.Platform.Security.Authorisation;

namespace Tailor360.UnitTests.Platform.Sessions;

/// <summary>
/// The caller as the rest of the system sees it. The unauthenticated cases matter most: this type
/// replaces the fail-closed anonymous principal in the container, so anything it answers permissively
/// without a session would open every endpoint at once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionCurrentUserTests
{
    private static readonly Guid UserId = Guid.Parse("0199b000-0000-7000-8000-000000000001");
    private static readonly Guid OrganisationId = Guid.Parse("0199b000-0000-7000-8000-0000000000aa");
    private static readonly Guid BranchA = Guid.Parse("0199b000-0000-7000-8000-00000000000a");
    private static readonly Guid BranchB = Guid.Parse("0199b000-0000-7000-8000-00000000000b");
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutASessionItHoldsNothing()
    {
        var user = new SessionCurrentUser(new SessionContext());

        user.IsAuthenticated.ShouldBeFalse();
        user.UserId.ShouldBe(Guid.Empty);
        user.PrincipalId.ShouldBe("anonymous");
        user.Permissions.ShouldBeEmpty();
        user.AssignedBranches.ShouldBeEmpty();
        user.MfaSatisfied.ShouldBeFalse();
        user.LastReauthenticatedAt.ShouldBeNull();
        user.HasPermission("admin.users").ShouldBeFalse();
        user.CanActInBranch(BranchA).ShouldBeFalse();
    }

    [Fact]
    public void ARefusedCookieLeavesTheCallerUnauthenticated()
    {
        var context = new SessionContext();

        // The store found the session and refused it. The status is recorded so the client can be told
        // its session ended, but nothing about the caller may survive the refusal.
        context.Set(SessionResolution.Revoked);

        var user = new SessionCurrentUser(context);

        context.Status.ShouldBe(SessionTicketStatus.Revoked);
        user.IsAuthenticated.ShouldBeFalse();
        context.Ticket.ShouldBeNull();
    }

    [Fact]
    public void WithASessionItReportsWhatTheTicketCarries()
    {
        var user = new SessionCurrentUser(ContextFor(Ticket()));

        user.IsAuthenticated.ShouldBeTrue();
        user.UserId.ShouldBe(UserId);
        user.PrincipalId.ShouldBe(UserId.ToString("n"));
        user.DisplayName.ShouldBe("Meena R");
        user.Context.OrganisationId.ShouldBe(OrganisationId);
        user.Context.BranchId.ShouldBe(BranchA);
        user.HasPermission("customers.read").ShouldBeTrue();
        user.MfaSatisfied.ShouldBeTrue();
        user.LastReauthenticatedAt.ShouldBe(Now);
    }

    [Fact]
    public void ItRefusesABranchTheHolderIsNotAssignedTo()
    {
        var user = new SessionCurrentUser(ContextFor(Ticket()));

        user.CanActInBranch(BranchA).ShouldBeTrue();
        user.CanActInBranch(BranchB).ShouldBeFalse();

        // An empty identifier is not "no branch, therefore allowed". A caller that reached here with one
        // has a bug, and the safe reading of a bug is a refusal.
        user.CanActInBranch(Guid.Empty).ShouldBeFalse();
    }

    [Fact]
    public void APermissionKeyThatIsBlankIsNeverHeld()
    {
        var user = new SessionCurrentUser(ContextFor(Ticket()));

        user.HasPermission(string.Empty).ShouldBeFalse();
    }

    private static SessionContext ContextFor(SessionTicket ticket)
    {
        var context = new SessionContext();
        context.Set(SessionResolution.Active(ticket));
        return context;
    }

    private static SessionTicket Ticket() => new(
        SessionId: Guid.Parse("0199b000-0000-7000-8000-0000000000f1"),
        UserId: UserId,
        DisplayName: "Meena R",
        OrganisationId: OrganisationId,
        ActiveBranchId: BranchA,
        AssignedBranches: new HashSet<Guid> { BranchA },
        Permissions: new HashSet<string>(StringComparer.Ordinal) { "customers.read" },
        MfaSatisfied: true,
        SignInComplete: true,
        LastStrongAuthenticationAt: Now,
        IdleExpiresAt: Now.AddMinutes(30),
        AbsoluteExpiresAt: Now.AddHours(12));
}

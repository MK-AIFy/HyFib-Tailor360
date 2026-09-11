using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Modules.Orders.Infrastructure.Persistence;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The one table that turns a refusal the <c>orders</c> schema raised into a refusal the module publishes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is a unit test and not an integration one.</strong> The three stores share one
/// <see cref="OrdersDbContext"/> and therefore one flush, so whichever store's <c>SaveAsync</c> is called can
/// fail on any constraint in the schema and not only its own table's. That is precisely what makes the mapping a
/// property of the failure rather than of the call site — and a pure function of a
/// <see cref="PostgresException"/>, which this tier can enumerate exhaustively. An integration test can reach one
/// constraint per test and would still leave every unreachable arm unasserted.
/// </para>
/// <para>
/// <strong>What each case is protecting.</strong> There are two distinct questions and the tests keep them
/// apart. <em>Whether</em> a failure is answered rather than thrown: a foreign-key violation, a not-null
/// violation and anything that never reached PostgreSQL are left to escape, because none of them is a refusal
/// this schema raises at a caller. And <em>which</em> refusal it becomes: a unique violation is always answered,
/// and the constraint name alone decides whether that is a conflict the caller may retry or the bounded
/// <c>WriteRefused</c>. A primary-key collision is an <c>IIdGenerator</c> defect, and telling a client somebody
/// else changed this invites a retry that collides identically — so a test covering only the named arms would
/// let the fall-through silently become <c>ConcurrentChange</c> again.
/// </para>
/// <para>
/// Constraint names are read from the <see cref="OrdersDbContext"/> constants rather than typed as literals,
/// because the constant is the contract between the migration and the mapping; a literal here would keep passing
/// after a rename that broke the schema. CLAUDE.md section 4 rule 3 is the reason none of these answers carries
/// the database's own message: "never a stack trace, never a raw exception message".
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OrdersWriteFailureTests
{
    /* Unique violations the schema is designed to raise ------------------------------------------ */

    [Theory]
    [InlineData(OrdersDbContext.GarmentJobNumberIndex, "orders.duplicate-garment-job")]
    [InlineData(OrdersDbContext.GarmentJobOrderIndexIndex, "orders.duplicate-garment-job")]
    [InlineData(OrdersDbContext.JobDependencyIndex, "orders.duplicate-dependency")]
    public void ANamedUniqueViolationBecomesTheRefusalThatConstraintMeans(string constraint, string expected)
    {
        var mapped = OrdersWriteFailures.TryMap(UniqueViolation(constraint), out var error);

        mapped.ShouldBeTrue();
        error.Code.ShouldBe(expected);
    }

    /// <summary>
    /// The garment-job arms differ only in which field they name, which is the whole point of carrying two
    /// constants: a counter told "duplicate garment job" learns nothing about which of the two collided.
    /// </summary>
    [Fact]
    public void TheTwoGarmentJobIndexesNameTheFieldThatCollided()
    {
        OrdersWriteFailures.TryMap(UniqueViolation(OrdersDbContext.GarmentJobNumberIndex), out var number)
            .ShouldBeTrue();
        OrdersWriteFailures.TryMap(UniqueViolation(OrdersDbContext.GarmentJobOrderIndexIndex), out var index)
            .ShouldBeTrue();

        number.ShouldNotBe(index);
    }

    /// <summary>
    /// <c>Estimate.Convert</c> refuses a second conversion; this is the half that holds when two confirmations
    /// run at once, and it must not read as a transient conflict — the estimate is spent, and a retry would find
    /// it spent again.
    /// </summary>
    [Fact]
    public void OneEstimateReachingTwoOrdersIsNotATransientConflict()
    {
        var mapped = OrdersWriteFailures.TryMap(UniqueViolation(OrdersDbContext.OrderEstimateIndex), out var error);

        mapped.ShouldBeTrue();
        error.ShouldBe(OrdersErrors.EstimateAlreadyConverted);
        error.ShouldNotBe(OrdersErrors.ConcurrentChange);
    }

    /// <summary>
    /// A garment identity added to one draft twice. <c>OrderDraft.AddGarment</c> refuses a reused identifier
    /// rather than treating it as a save, so the database agreeing with it is a conflict and not an upsert.
    /// </summary>
    [Fact]
    public void AGarmentAddedToOneDraftTwiceIsAConflictAndNotAnUpsert()
    {
        var mapped = OrdersWriteFailures.TryMap(UniqueViolation(OrdersDbContext.DraftGarmentKey), out var error);

        mapped.ShouldBeTrue();
        error.ShouldBe(OrdersErrors.GarmentAlreadyOnDraft);
    }

    /// <summary>
    /// The four indexes a max-plus-one read followed by a write can lose a race on. Each is a real conflict the
    /// loser resolves by re-reading, and in each the loser's number is never written — which is why INV-ORD-03
    /// holds either way.
    /// </summary>
    [Theory]
    [InlineData(OrdersDbContext.OrderNumberIndex)]
    [InlineData(OrdersDbContext.EstimateNumberIndex)]
    [InlineData(OrdersDbContext.OrderRevisionNumberIndex)]
    [InlineData(OrdersDbContext.DraftGarmentPositionIndex)]
    public void TwoWritersTakingOneNumberIsAConflictTheLoserReReads(string constraint)
    {
        var mapped = OrdersWriteFailures.TryMap(UniqueViolation(constraint), out var error);

        mapped.ShouldBeTrue();
        error.ShouldBe(OrdersErrors.ConcurrentChange);
    }

    /* The constraints deliberately left to escape ------------------------------------------------ */

    /// <summary>
    /// A primary-key collision is an <c>IIdGenerator</c> defect, not a lost race. The arm used to end
    /// <c>_ =&gt; ConcurrentChange</c>, which told the client somebody else had changed this and invited a retry
    /// that collides identically — so the defect never surfaced and the caller looped. The fall-through answers
    /// <see cref="OrdersErrors.WriteRefused"/> instead, which is what keeps the documented approximation bounded
    /// to the constraints actually argued for.
    /// </summary>
    /// <remarks>
    /// A unique violation is always <em>mapped</em> — the return is true for every constraint, because the
    /// database refusing a duplicate is never a 500. What the constraint decides is <em>which</em> refusal, and
    /// that is the whole assertion here: the unnamed and the unargued must not inherit the conflict reading.
    /// </remarks>
    [Theory]
    [InlineData("pk_orders")]
    [InlineData("pk_garment_jobs")]
    [InlineData("pk_estimates")]
    [InlineData("pk_order_drafts")]
    [InlineData("ux_something_added_after_this_was_written")]
    [InlineData(null)]
    public void AUniqueViolationNobodyArguedForIsNotReportedAsATransientConflict(string? constraint)
    {
        var mapped = OrdersWriteFailures.TryMap(UniqueViolation(constraint), out var error);

        // Still answered rather than thrown: a duplicate the database refused is a result, not a 500.
        mapped.ShouldBeTrue();

        error.ShouldBe(OrdersErrors.WriteRefused);
        error.ShouldNotBe(OrdersErrors.ConcurrentChange);
    }

    /// <summary>
    /// A foreign-key violation and a not-null violation are left to escape on purpose. Neither is a refusal this
    /// schema raises at a caller — the two <c>ON DELETE RESTRICT</c> arms guard deletes no command performs, and
    /// a null in a required column is a mapping defect. Answering a conflict would put a defect behind a message
    /// that invites a retry.
    /// </summary>
    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData(PostgresErrorCodes.NotNullViolation)]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    public void AFailureThisSchemaDoesNotRaiseAtACallerIsNotMapped(string sqlState)
    {
        var mapped = OrdersWriteFailures.TryMap(Failure(sqlState, constraint: null), out var error);

        mapped.ShouldBeFalse();
        error.ShouldBe(OrdersErrors.WriteRefused);
    }

    /// <summary>
    /// A save can fail for reasons that never reached PostgreSQL at all — a broken connection, a cancellation, a
    /// provider defect. There is nothing to map, and the out parameter is still set so no caller reads an
    /// uninitialised error when it ignores the return value.
    /// </summary>
    [Fact]
    public void AFailureThatIsNotPostgresIsNotMappedAndStillLeavesAnError()
    {
        var exception = new DbUpdateException("The connection was lost.", new TimeoutException());

        var mapped = OrdersWriteFailures.TryMap(exception, out var error);

        mapped.ShouldBeFalse();
        error.ShouldBe(OrdersErrors.WriteRefused);
    }

    /// <summary>A <see cref="DbUpdateException"/> with no inner exception at all takes the same path.</summary>
    [Fact]
    public void AFailureWithNoInnerExceptionIsNotMapped()
    {
        var mapped = OrdersWriteFailures.TryMap(new DbUpdateException("Nothing to go on."), out var error);

        mapped.ShouldBeFalse();
        error.ShouldBe(OrdersErrors.WriteRefused);
    }

    /* The triggers and check constraints that guard the Domain's own rules ----------------------- */

    /// <summary>
    /// The migration installs roughly twenty <c>CHECK</c> constraints and five <c>RAISE EXCEPTION</c> triggers
    /// whose whole purpose is to refuse a write that reached a table without going through the Domain. Their
    /// bodies are prose addressed to a person and would otherwise reach the wire verbatim, so they are mapped —
    /// generically, and to a result rather than a 500.
    /// </summary>
    [Theory]
    [InlineData(PostgresErrorCodes.CheckViolation, "ck_garment_jobs_ready_state")]
    [InlineData(PostgresErrorCodes.CheckViolation, null)]
    [InlineData(PostgresErrorCodes.RestrictViolation, "job_snapshots_are_immutable")]
    [InlineData(PostgresErrorCodes.RestrictViolation, "order_revisions_append_only")]
    [InlineData(PostgresErrorCodes.RestrictViolation, "garment_jobs_ready_is_gate_only")]
    public void AWriteThatReachedATableWithoutTheDomainIsRefusedRatherThanThrown(
        string sqlState,
        string? constraint)
    {
        var mapped = OrdersWriteFailures.TryMap(Failure(sqlState, constraint), out var error);

        mapped.ShouldBeTrue();
        error.ShouldBe(OrdersErrors.WriteRefused);
    }

    /// <summary>
    /// The refusal a trigger produces must not carry the trigger's own message. Those bodies name the garment job
    /// and the status it is in, and neither belongs on the wire (CLAUDE.md section 4 rules 3 and 7).
    /// </summary>
    [Fact]
    public void ATriggersOwnMessageNeverReachesTheRefusal()
    {
        var trigger = Failure(
            PostgresErrorCodes.RestrictViolation,
            "job_snapshots_are_immutable",
            "A measurement snapshot frozen at confirmation for garment job GJ-2026-0001 cannot be rewritten.");

        OrdersWriteFailures.TryMap(trigger, out var error).ShouldBeTrue();

        error.Message.ShouldNotContain("GJ-2026-0001");
        error.Message.ShouldNotContain("snapshot frozen at confirmation");
    }

    /* Helpers ------------------------------------------------------------------------------------ */

    private static DbUpdateException UniqueViolation(string? constraint)
        => Failure(PostgresErrorCodes.UniqueViolation, constraint);

    private static DbUpdateException Failure(string sqlState, string? constraint, string? message = null)
    {
        var postgres = new PostgresException(
            message ?? "The database refused the write.",
            "ERROR",
            "ERROR",
            sqlState,
            constraintName: constraint);

        return new DbUpdateException("An error occurred while saving.", postgres);
    }
}

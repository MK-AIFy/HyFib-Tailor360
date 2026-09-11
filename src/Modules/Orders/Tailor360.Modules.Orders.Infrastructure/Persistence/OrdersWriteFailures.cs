using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tailor360.Modules.Orders.Domain;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// The one place a refusal the <c>orders</c> schema raised becomes a refusal the module publishes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One mapping because there is one unit of work.</strong> <c>AddOrdersModule</c> registers three stores
/// over one <see cref="OrdersDbContext"/> — "a confirmation consumes a draft, converts an estimate and creates an
/// order, and all three have to commit or none of them may" — so whichever store's <c>SaveAsync</c> is called
/// flushes <em>every</em> pending change on that context, not only its own aggregate's. While each store mapped
/// only the constraints of its own tables, the answer to one database failure depended on which store happened to
/// issue the save: a duplicate garment on a draft was <c>GarmentAlreadyOnDraft</c> through
/// <c>OrderDraftStore</c> and <c>ConcurrentChange</c> through <c>OrderStore</c>, and a violation of
/// <c>ux_garment_jobs_organisation_number</c> escaped <c>EstimateStore</c> unmapped altogether. One table of every
/// named constraint in the schema, consulted by all three, is what makes the answer a property of the failure
/// rather than of the call site.
/// </para>
/// <para>
/// <strong>Named constraints and not message text.</strong> A constraint name is a contract between the migration
/// and this file; matching on a message would break the first time PostgreSQL is upgraded or the server speaks
/// another language. Every name here is a <c>const</c> on <see cref="OrdersDbContext"/>, declared beside the index
/// that creates it.
/// </para>
/// <para>
/// <strong>Three classes of refusal, not one.</strong> A unique violation is the familiar one. The migration also
/// installs roughly twenty <c>CHECK</c> constraints and five <c>RAISE EXCEPTION</c> triggers —
/// <c>job_snapshots_are_immutable</c>, <c>garment_job_price_is_immutable</c>, <c>order_revisions_append_only</c>,
/// <c>job_dependencies_append_only</c> and <c>garment_jobs_ready_is_gate_only</c> — whose whole purpose is to
/// refuse a write that reached a table without going through the Domain. Those bodies are written as prose
/// addressed to a person and they would otherwise reach the wire verbatim inside an unhandled
/// <c>PostgresException</c>, which CLAUDE.md section 4 rule 3 forbids ("never a stack trace, never a raw exception
/// message"). They answer <see cref="OrdersErrors.WriteRefused"/> instead: the Domain rule the trigger mirrors has
/// already been broken, so there is no refusal the counter can act on, but there is a result rather than a 500.
/// </para>
/// <para>
/// <strong>What is deliberately not mapped.</strong> A foreign-key violation (23503) and a not-null violation
/// (23502) are left to escape. Neither is a refusal this schema is designed to raise at a caller: the two
/// <c>ON DELETE RESTRICT</c> arms guard deletes no command performs, and a null in a required column is a mapping
/// defect. Answering them with a conflict would put a defect behind a message that invites a retry, which is the
/// same mistake the unnamed-unique arm below exists to stop.
/// </para>
/// </remarks>
internal static class OrdersWriteFailures
{
    /// <summary>
    /// Turns a failed save into a refusal, or leaves it alone when it is not one this schema raises.
    /// </summary>
    /// <param name="exception">The failure Entity Framework reported.</param>
    /// <param name="error">The refusal to answer with, when there is one.</param>
    /// <returns>True when the failure is one this module answers rather than throws.</returns>
    public static bool TryMap(DbUpdateException exception, out Error error)
    {
        if (exception.InnerException is not PostgresException postgres)
        {
            error = OrdersErrors.WriteRefused;

            return false;
        }

        switch (postgres.SqlState)
        {
            case PostgresErrorCodes.UniqueViolation:
                error = ForUniqueViolation(postgres.ConstraintName);

                return true;

            // A check constraint, and a trigger that raised with ERRCODE = 'restrict_violation'. Both are the
            // database holding an invariant the Domain holds too, so reaching one means something wrote past the
            // Domain. The refusal is generic on purpose: the trigger's own message names the garment job and the
            // status it is in, and neither belongs on the wire.
            case PostgresErrorCodes.CheckViolation:
            case PostgresErrorCodes.RestrictViolation:
                error = OrdersErrors.WriteRefused;

                return true;

            default:
                error = OrdersErrors.WriteRefused;

                return false;
        }
    }

    /// <summary>
    /// The refusal a named unique constraint means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The two display-number indexes map to a conflict that does not quite say what happened.</strong>
    /// Taking the same order number twice is not "somebody else changed this", but no member of
    /// <see cref="OrdersErrors"/> says so — Customers coined <c>MeasurementErrors.VersionNumberConflict</c> for the
    /// same situation. Adding one is a Domain change this pull request does not own, so it is raised as a linked
    /// issue and mapped to <see cref="OrdersErrors.ConcurrentChange"/> here in the meantime. The invariant itself
    /// is unharmed: the loser's number is never written, so INV-ORD-03 holds either way. The same reading covers
    /// <c>ux_order_revisions_order_number</c> and <c>ux_order_draft_garments_draft_position</c>, where a
    /// max-plus-one read followed by a write lets two writers take one number and the loser re-reads.
    /// </para>
    /// <para>
    /// <strong>An unnamed constraint is not that, and the fall-through says so.</strong> The arm used to end
    /// <c>_ =&gt; ConcurrentChange</c>, which reported every unmatched unique violation as a transient conflict —
    /// including <c>pk_orders</c>, <c>pk_garment_jobs</c>, <c>pk_estimates</c> and <c>pk_order_drafts</c>. A
    /// primary-key collision is an <c>IIdGenerator</c> defect and not a lost race: telling the client somebody
    /// else changed this invites a retry that collides identically, and the real defect never surfaces in an
    /// alert. It also silently extended a documented approximation to constraints nobody had considered, and to
    /// every constraint added later. <see cref="OrdersErrors.WriteRefused"/> keeps the approximation bounded to
    /// the cases argued for.
    /// </para>
    /// </remarks>
    /// <param name="constraint">The violated constraint, as PostgreSQL named it.</param>
    /// <returns>The error to answer with.</returns>
    private static Error ForUniqueViolation(string? constraint) => constraint switch
    {
        OrdersDbContext.GarmentJobNumberIndex => OrdersErrors.DuplicateGarmentJob("jobNumber"),
        OrdersDbContext.GarmentJobOrderIndexIndex => OrdersErrors.DuplicateGarmentJob("jobIndex"),

        // One estimate reached two orders. Estimate.Convert refuses a second conversion, and this is the half
        // that holds when two confirmations run at once.
        OrdersDbContext.OrderEstimateIndex => OrdersErrors.EstimateAlreadyConverted,
        OrdersDbContext.JobDependencyIndex => OrdersErrors.DuplicateDependency,

        // A garment identity was added to one draft twice. OrderDraft.AddGarment refuses a reused identifier
        // rather than treating it as a save, so the database agreeing with it is a conflict and not an upsert.
        OrdersDbContext.DraftGarmentKey => OrdersErrors.GarmentAlreadyOnDraft,

        // Order.Revise increments after a read, so two revisions racing take the same number; the index settles
        // it and the loser re-reads. Same shape for the two display numbers and for a draft's garment positions
        // — see the remarks.
        OrdersDbContext.OrderRevisionNumberIndex => OrdersErrors.ConcurrentChange,
        OrdersDbContext.OrderNumberIndex => OrdersErrors.ConcurrentChange,
        OrdersDbContext.EstimateNumberIndex => OrdersErrors.ConcurrentChange,
        OrdersDbContext.DraftGarmentPositionIndex => OrdersErrors.ConcurrentChange,
        _ => OrdersErrors.WriteRefused,
    };
}

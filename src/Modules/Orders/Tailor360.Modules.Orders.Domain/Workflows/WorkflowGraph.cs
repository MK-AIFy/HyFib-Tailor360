namespace Tailor360.Modules.Orders.Domain.Workflows;

/// <summary>
/// The checks a workflow version's phases and transitions must pass before the version may be published.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over the two collections a version holds, and nothing else — no database, no other module,
/// no clock. That is deliberate: unlike <c>Catalog.Contracts.Catalogue.ICatalogDependencyValidator</c>, which
/// exists because a catalogue publication depends on facts other modules hold, whether a phase graph is
/// internally consistent is a question the graph alone can answer, so it stays in <c>Domain</c> and is asked
/// directly by <see cref="WorkflowVersion.ValidateForPublication"/> and by a unit test with no host at all.
/// </para>
/// <para>
/// <strong>Every check runs, and every finding it produces is returned</strong> — plan Section 8's own reason
/// for the shape: an administrator fixing one defect at a time and re-submitting is a worse afternoon than one
/// report naming all of them. A version may therefore carry several findings from one check (an unreachable
/// phase names each phase it could not reach) and several checks may each contribute their own.
/// </para>
/// </remarks>
public static class WorkflowGraph
{
    /// <summary>Exactly one phase must have no incoming transition.</summary>
    /// <remarks>
    /// There is no <c>is_start</c> column (<c>WorkflowPhaseContent</c>'s own shape has none): a start phase is
    /// the graph's own entry point, not a fact anybody records twice, so it is derived from the transitions —
    /// the phase nothing transitions into. Zero such phases and more than one both fail this one check, because
    /// both leave "where does a job begin" unanswered.
    /// </remarks>
    public const string NotExactlyOneStartPhase = "orders.workflow-graph-not-exactly-one-start-phase";

    /// <summary>At least one phase must be terminal.</summary>
    public const string NoTerminalPhase = "orders.workflow-graph-no-terminal-phase";

    /// <summary>Every phase must be reachable from the start phase.</summary>
    public const string UnreachablePhase = "orders.workflow-graph-unreachable-phase";

    /// <summary>A phase with no outbound transition must be terminal.</summary>
    public const string DeadEndPhase = "orders.workflow-graph-dead-end-phase";

    /// <summary>No phase may have an empty set of required role keys.</summary>
    public const string PhaseWithoutRequiredRole = "orders.workflow-graph-phase-without-required-role";

    /// <summary>Phase codes must be unique within the version.</summary>
    public const string DuplicatePhaseCode = "orders.workflow-graph-duplicate-phase-code";

    /// <summary>Every transition must name phases that are actually on this version.</summary>
    /// <remarks>
    /// <see cref="PhaseTransition.Create"/> checks only that the two codes are well formed and defers this
    /// question here, once the whole set of phases is known. Without it, a transition naming a phase that
    /// was never submitted — or one removed by the same edit — would simply be excluded from every other
    /// check's view of the graph rather than reported, so an administrator could save, and later publish,
    /// a version whose transition matrix silently disagrees with its phase list.
    /// </remarks>
    public const string TransitionNamesMissingPhase = "orders.workflow-graph-transition-names-missing-phase";

    /// <summary>Runs every check and returns every finding, in no particular order.</summary>
    /// <param name="phases">The version's phases.</param>
    /// <param name="transitions">The version's transitions.</param>
    /// <returns>What is wrong, or an empty list when the graph is sound.</returns>
    public static IReadOnlyList<WorkflowFinding> Validate(
        IReadOnlyList<WorkflowPhase> phases,
        IReadOnlyList<PhaseTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(transitions);

        var findings = new List<WorkflowFinding>();

        CheckDuplicateCodes(phases, findings);
        CheckRequiredRoles(phases, findings);

        // The remaining three checks reason about the graph shape, which only makes sense over the phases a
        // duplicate code has not already made ambiguous. Each is still asked independently — a duplicate code
        // does not stand in for an unreachable phase — but they are read from the de-duplicated-by-first-seen
        // set, exactly as a reader would: two rows sharing a code are one node in the picture they draw.
        var distinctPhases = phases
            .GroupBy(phase => phase.Code, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var codes = distinctPhases.Select(phase => phase.Code).ToHashSet(StringComparer.Ordinal);

        // Asked over every submitted transition, not the de-duplicated-by-code set below — a transition
        // naming a phase nobody submitted is exactly the defect this check exists to report, so it must see
        // the phase the caller actually named rather than one already filtered down to what is valid.
        CheckTransitionEndpoints(transitions, codes, findings);

        var validTransitions = transitions
            .Where(transition => codes.Contains(transition.FromPhaseCode) && codes.Contains(transition.ToPhaseCode))
            .ToList();

        var startPhases = CheckStartPhase(distinctPhases, validTransitions, findings);
        CheckTerminalPhase(distinctPhases, findings);
        CheckReachability(distinctPhases, validTransitions, startPhases, findings);
        CheckDeadEnds(distinctPhases, validTransitions, findings);

        return findings;
    }

    private static void CheckDuplicateCodes(IReadOnlyList<WorkflowPhase> phases, List<WorkflowFinding> findings)
    {
        foreach (var group in phases.GroupBy(phase => phase.Code, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                findings.Add(WorkflowFinding.Error(
                    DuplicatePhaseCode,
                    $"Phase code '{group.Key}' is declared {group.Count()} times. Every phase needs a code of "
                    + "its own.",
                    group.Key));
            }
        }
    }

    private static void CheckRequiredRoles(IReadOnlyList<WorkflowPhase> phases, List<WorkflowFinding> findings)
    {
        foreach (var phase in phases)
        {
            if (phase.RequiredRoleKeys.Count == 0)
            {
                findings.Add(WorkflowFinding.Error(
                    PhaseWithoutRequiredRole,
                    $"Phase '{phase.Code}' names no role that may work it.",
                    phase.Code));
            }
        }
    }

    private static void CheckTransitionEndpoints(
        IReadOnlyList<PhaseTransition> transitions,
        HashSet<string> codes,
        List<WorkflowFinding> findings)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var transition in transitions)
        {
            if (!codes.Contains(transition.FromPhaseCode))
            {
                missing.Add(transition.FromPhaseCode);
            }

            if (!codes.Contains(transition.ToPhaseCode))
            {
                missing.Add(transition.ToPhaseCode);
            }
        }

        foreach (var code in missing)
        {
            findings.Add(WorkflowFinding.Error(
                TransitionNamesMissingPhase,
                $"A transition names phase '{code}', which this version does not have.",
                code));
        }
    }

    private static HashSet<string> CheckStartPhase(
        IReadOnlyList<WorkflowPhase> phases,
        IReadOnlyList<PhaseTransition> transitions,
        List<WorkflowFinding> findings)
    {
        var hasIncoming = transitions.Select(transition => transition.ToPhaseCode).ToHashSet(StringComparer.Ordinal);
        var startPhases = phases.Select(phase => phase.Code)
            .Where(code => !hasIncoming.Contains(code))
            .ToHashSet(StringComparer.Ordinal);

        if (startPhases.Count != 1)
        {
            findings.Add(WorkflowFinding.Error(
                NotExactlyOneStartPhase,
                startPhases.Count == 0
                    ? "No phase is a start phase: every phase has an incoming transition, so there is nowhere "
                      + "for a job to begin."
                    : $"{startPhases.Count} phases have no incoming transition ("
                      + $"{string.Join(", ", startPhases.OrderBy(code => code, StringComparer.Ordinal))}), so "
                      + "where a job begins is ambiguous."));
        }

        return startPhases;
    }

    private static void CheckTerminalPhase(IReadOnlyList<WorkflowPhase> phases, List<WorkflowFinding> findings)
    {
        if (phases.All(phase => !phase.IsTerminal))
        {
            findings.Add(WorkflowFinding.Error(
                NoTerminalPhase,
                "No phase is marked terminal, so a job started on this version could never be finished."));
        }
    }

    private static void CheckReachability(
        IReadOnlyList<WorkflowPhase> phases,
        IReadOnlyList<PhaseTransition> transitions,
        HashSet<string> startPhases,
        List<WorkflowFinding> findings)
    {
        // Ambiguous where a job begins is CheckStartPhase's own finding; this check answers a different
        // question — what the graph reaches once it begins — and needs one seed to ask it from. Any of several
        // ties is as good as another, since they are all already reported.
        if (startPhases.Count == 0)
        {
            return;
        }

        var outbound = transitions
            .GroupBy(transition => transition.FromPhaseCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(transition => transition.ToPhaseCode).ToList(),
                StringComparer.Ordinal);

        var reached = new HashSet<string>(startPhases, StringComparer.Ordinal);
        var frontier = new Queue<string>(startPhases);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            if (!outbound.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var candidate in next)
            {
                if (reached.Add(candidate))
                {
                    frontier.Enqueue(candidate);
                }
            }
        }

        foreach (var phase in phases)
        {
            if (!reached.Contains(phase.Code))
            {
                findings.Add(WorkflowFinding.Error(
                    UnreachablePhase,
                    $"Phase '{phase.Code}' cannot be reached from the start phase by any transition.",
                    phase.Code));
            }
        }
    }

    private static void CheckDeadEnds(
        IReadOnlyList<WorkflowPhase> phases,
        IReadOnlyList<PhaseTransition> transitions,
        List<WorkflowFinding> findings)
    {
        var hasOutgoing = transitions.Select(transition => transition.FromPhaseCode)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var phase in phases)
        {
            if (!phase.IsTerminal && !hasOutgoing.Contains(phase.Code))
            {
                findings.Add(WorkflowFinding.Error(
                    DeadEndPhase,
                    $"Phase '{phase.Code}' has no outbound transition and is not marked terminal, so a job "
                    + "entering it could never leave.",
                    phase.Code));
            }
        }
    }
}

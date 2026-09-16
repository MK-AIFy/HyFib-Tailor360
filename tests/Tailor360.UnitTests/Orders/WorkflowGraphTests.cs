using Shouldly;
using Tailor360.Modules.Orders.Domain.Workflows;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The six checks a phase graph must pass before it may be published, each pinned to its own scenario, and a
/// randomised sweep proving the reachability and dead-end checks agree with an independently computed answer.
/// </summary>
/// <remarks>
/// Every scenario is built through <see cref="WorkflowDefinition"/>'s public surface — <c>AddVersion</c>,
/// <c>ReplacePhases</c>, <c>ReplaceTransitions</c> — and read back through
/// <see cref="WorkflowVersion.ValidateForPublication"/>, never by constructing a <see cref="WorkflowPhase"/>
/// directly: its own factory is <see langword="internal"/>, reachable only from <c>WorkflowVersion</c>, exactly
/// the reason <c>WorkflowVersionTests</c>'s own remarks give for driving everything through the aggregate root.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class WorkflowGraphTests
{
    private static readonly DateTimeOffset Now = OrdersTestData.Now;

    [Fact]
    public void ASoundGraphHasNoFindings()
        => Graph(Phase("AA", terminal: false), Phase("BB", terminal: true))
            .WithEdges(("AA", "BB"))
            .ShouldBeEmpty();

    [Fact]
    public void AVersionWithNoPhaseAtAllHasNoStartAndNoTerminalButNoUnreachableOrDeadEndFindings()
    {
        var findings = Graph().WithEdges();

        findings.Select(finding => finding.Code).ShouldBe(
            [WorkflowGraph.NotExactlyOneStartPhase, WorkflowGraph.NoTerminalPhase], ignoreOrder: true);
    }

    [Fact]
    public void APhaseCodeDeclaredTwiceIsOneDuplicateFinding()
    {
        var findings = Graph(Phase("AA", terminal: true), Phase("AA", terminal: true)).WithEdges();

        findings.ShouldContain(finding =>
            finding.Code == WorkflowGraph.DuplicatePhaseCode && finding.Target == "AA");
    }

    [Fact]
    public void NoPhaseWithoutAnIncomingTransitionIsNoStartPhase()
    {
        // Every phase has an incoming edge: AA -> BB -> AA.
        var findings = Graph(Phase("AA", terminal: false), Phase("BB", terminal: true))
            .WithEdges(("AA", "BB"), ("BB", "AA"));

        findings.ShouldContain(finding =>
            finding.Code == WorkflowGraph.NotExactlyOneStartPhase && finding.Message.Contains("No phase"));
    }

    [Fact]
    public void TwoPhasesWithNoIncomingTransitionAreTwoStartPhases()
    {
        var findings = Graph(Phase("AA", terminal: true), Phase("BB", terminal: true)).WithEdges();

        findings.ShouldContain(finding =>
            finding.Code == WorkflowGraph.NotExactlyOneStartPhase && finding.Message.Contains("2 phases"));
    }

    [Fact]
    public void NoPhaseMarkedTerminalIsNoTerminalPhase()
    {
        var findings = Graph(Phase("AA", terminal: false), Phase("BB", terminal: false)).WithEdges(("AA", "BB"));

        findings.ShouldContain(finding => finding.Code == WorkflowGraph.NoTerminalPhase);
    }

    [Fact]
    public void APhaseNothingTransitionsIntoAndNothingReachesIsUnreachable()
    {
        // AA is the graph's one unambiguous start (nothing points into it) and reaches BB. ORPHAN and XX point
        // into each other and nowhere else, which is what keeps ORPHAN off the start-phase list — it does have
        // an incoming transition — while nothing outside that pair ever transitions into it either.
        var findings = Graph(
                Phase("AA", terminal: false), Phase("BB", terminal: true),
                Phase("ORPHAN", terminal: true), Phase("XX", terminal: true))
            .WithEdges(("AA", "BB"), ("ORPHAN", "XX"), ("XX", "ORPHAN"));

        findings.ShouldContain(finding =>
            finding.Code == WorkflowGraph.UnreachablePhase && finding.Target == "ORPHAN");
        findings.ShouldContain(finding =>
            finding.Code == WorkflowGraph.UnreachablePhase && finding.Target == "XX");
        findings.ShouldNotContain(finding => finding.Code == WorkflowGraph.NotExactlyOneStartPhase);
    }

    [Fact]
    public void ANonTerminalPhaseWithNoOutboundTransitionIsADeadEnd()
    {
        var findings = Graph(Phase("AA", terminal: false), Phase("BB", terminal: false)).WithEdges(("AA", "BB"));

        findings.ShouldContain(finding => finding.Code == WorkflowGraph.DeadEndPhase && finding.Target == "BB");
    }

    [Fact]
    public void ATerminalPhaseWithNoOutboundTransitionIsNotADeadEnd()
        => Graph(Phase("AA", terminal: false), Phase("BB", terminal: true))
            .WithEdges(("AA", "BB"))
            .ShouldNotContain(finding => finding.Code == WorkflowGraph.DeadEndPhase);

    [Fact]
    public void APhaseWithNoRequiredRoleIsFlagged()
    {
        var findings = Graph(Phase("LONELY", terminal: true, withRole: false)).WithEdges();

        findings.ShouldContain(finding =>
            finding.Code == WorkflowGraph.PhaseWithoutRequiredRole && finding.Target == "LONELY");
    }

    /// <summary>
    /// The acceptance criterion's own combination: five defects that can genuinely coexist on one graph appear
    /// together in one report. "No start phase" and "two start phases" are the two failure modes of one check
    /// and cannot both be true of the same graph at once — each is proved alone above.
    /// </summary>
    [Fact]
    public void AGraphCarryingFiveCompatibleDefectsReportsAllFiveInOneCall()
    {
        var findings = Graph(
                Phase("START", terminal: false), // the graph's one unambiguous start
                Phase("AA", terminal: true), // duplicate code, and — with START — an extra start phase
                Phase("AA", terminal: true), // duplicate code
                Phase("BB", terminal: false), // dead end: no outbound edge, not terminal
                Phase("ORPHAN", terminal: true), // unreachable, isolated with XX
                Phase("XX", terminal: true), // unreachable, isolated with ORPHAN
                Phase("NO_ROLE", terminal: true, withRole: false)) // no required role
            .WithEdges(("START", "BB"), ("ORPHAN", "XX"), ("XX", "ORPHAN"));

        var codes = findings.Select(finding => finding.Code).ToHashSet();

        codes.ShouldContain(WorkflowGraph.DuplicatePhaseCode);
        codes.ShouldContain(WorkflowGraph.NotExactlyOneStartPhase);
        codes.ShouldContain(WorkflowGraph.DeadEndPhase);
        codes.ShouldContain(WorkflowGraph.UnreachablePhase);
        codes.ShouldContain(WorkflowGraph.PhaseWithoutRequiredRole);
    }

    /// <summary>
    /// Required verification's own property test: over many random graphs, <see cref="WorkflowGraph"/>'s
    /// reachability and dead-end findings agree exactly with an independently computed answer — a plain BFS and
    /// a plain outgoing-edge count, over the same phases and transitions, computed without going near the
    /// validator under test.
    /// </summary>
    [Fact]
    public void ReachabilityAndDeadEndFindingsAgreeWithAnIndependentComputationOverManyRandomGraphs()
    {
        var random = new Random(20260916);

        for (var round = 0; round < 300; round++)
        {
            var phaseCount = random.Next(1, 8);
            var codes = Enumerable.Range(0, phaseCount).Select(i => $"P{i}").ToArray();
            var terminal = codes.ToDictionary(code => code, _ => random.Next(0, 2) == 0);
            var phases = codes.Select(code => Phase(code, terminal[code])).ToArray();

            var edges = new List<(string From, string To)>();
            foreach (var from in codes)
            {
                foreach (var to in codes)
                {
                    if (from != to && random.Next(0, 100) < 35)
                    {
                        edges.Add((from, to));
                    }
                }
            }

            var findings = Graph(phases).WithEdges([.. edges]);

            // Ground truth, computed independently of the type under test.
            var hasIncoming = edges.Select(edge => edge.To).ToHashSet();
            var startPhases = codes.Where(code => !hasIncoming.Contains(code)).ToHashSet();
            var outbound = edges.GroupBy(edge => edge.From)
                .ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToList());
            var reached = new HashSet<string>(startPhases);
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

            var unreachableTargets = findings
                .Where(finding => finding.Code == WorkflowGraph.UnreachablePhase)
                .Select(finding => finding.Target)
                .ToHashSet();
            var deadEndTargets = findings
                .Where(finding => finding.Code == WorkflowGraph.DeadEndPhase)
                .Select(finding => finding.Target)
                .ToHashSet();

            foreach (var code in codes)
            {
                if (startPhases.Count > 0)
                {
                    unreachableTargets.Contains(code).ShouldBe(
                        !reached.Contains(code), $"round {round}, phase {code}: reachability disagreed");
                }

                var hasOutgoing = outbound.ContainsKey(code);

                deadEndTargets.Contains(code).ShouldBe(
                    !terminal[code] && !hasOutgoing, $"round {round}, phase {code}: dead-end disagreed");
            }
        }
    }

    private static WorkflowPhaseContent Phase(string code, bool terminal, bool withRole = true)
        => WorkflowPhaseContent.Create(
            code, code, 0, withRole ? ["tailor"] : [], false, null, null, false, false, terminal).Value;

    private static GraphBuilder Graph(params WorkflowPhaseContent[] phases) => new(phases);

    private sealed class GraphBuilder(IReadOnlyList<WorkflowPhaseContent> phases)
    {
        public IReadOnlyList<WorkflowFinding> WithEdges(params (string From, string To)[] edges)
        {
            var definition = WorkflowDefinition.Create(
                Guid.CreateVersion7(), OrdersTestData.Organisation, "GRAPH_TEST", "Graph test", null, Now, null)
                .Value;
            var version = definition.AddVersion(new CountingWorkflowIds(), Now, null);

            definition.ReplacePhases(version.Id, new CountingWorkflowIds(), phases, Now, null).IsSuccess
                .ShouldBeTrue();
            definition.ReplaceTransitions(
                    version.Id,
                    [.. edges.Select(edge => PhaseTransition.Create(edge.From, edge.To).Value)],
                    Now,
                    null)
                .IsSuccess.ShouldBeTrue();

            return version.ValidateForPublication();
        }
    }
}

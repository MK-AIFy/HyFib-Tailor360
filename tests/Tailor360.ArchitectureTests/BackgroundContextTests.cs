using System.Text.RegularExpressions;
using Shouldly;
using Tailor360.Platform.Security.Background;
using Tailor360.Platform.Security.Permissions;
using Tailor360.Worker;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// Rules about the principal a background job runs as. A job has no request behind it, so its
/// authority is chosen rather than presented — and both rules here exist to keep that choice in the
/// two hosts that genuinely have no request, and to keep it written down on the job itself.
/// </summary>
/// <remarks>
/// The negative controls live in this file rather than in <see cref="NegativeControlTests"/> because
/// the detectors are specific to these two rules; the shared source detectors stay where they are.
/// </remarks>
[Trait("Category", "Architecture")]
public sealed partial class BackgroundContextTests
{
    /// <summary>Where a reference to the scope factory is legitimate.</summary>
    private static readonly string[] HostsThatMayChooseAPrincipal =
    [
        Path.Combine("src", "Hosts", "Tailor360.Worker"),
        Path.Combine("src", "Tools", "Tailor360.Cli"),

        // The declaration itself, which cannot avoid naming what it declares.
        Path.Combine("src", "Platform", "Tailor360.Platform.Security", "Background"),
    ];

    /// <summary>
    /// ARCH-020: only the worker and the command-line tool reference <c>IWorkerScopeFactory</c>. Module
    /// code that reached for it would be selecting its own authority instead of being given one, and a
    /// service that can mint a system principal is a service that can bypass every permission check
    /// above it.
    /// </summary>
    [Fact]
    public void Arch020_OnlyTheBackgroundHostsChooseAPrincipal()
    {
        var violations = SourceScanner.Scan(SourceOutsideTheBackgroundHosts(), ScopeFactoryPattern(), []);

        violations.ShouldBeEmpty(
            "ARCH-020: a background principal is built only by the worker and the command-line tool. "
            + "Take the caller as ICurrentUser instead. Offending lines:\n"
            + string.Join('\n', violations));
    }

    /// <summary>
    /// ARCH-021: every background job carries a <c>[WorkerJob]</c> declaration. A job without one runs
    /// as nobody or as everybody depending on how it was composed, and neither is a decision anyone
    /// made on purpose.
    /// </summary>
    [Fact]
    public void Arch021_EveryWorkerJobDeclaresItsScope()
    {
        var jobs = HostedServices();

        jobs.Count.ShouldBeGreaterThan(0, "the worker must run at least one job for this rule to mean anything.");

        var undeclared = jobs
            .Where(job => WorkerJobDescriptor.Find(job) is null)
            .Select(job => job.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        undeclared.ShouldBeEmpty(
            "ARCH-021: add [WorkerJob(name, scope, permissions)] to each of these. A job runs with no "
            + "ambient user, so what it may do has to be stated:\n" + string.Join('\n', undeclared));
    }

    /// <summary>
    /// ARCH-021: a declaration has to be one the deployment can honour — every permission it names
    /// exists, its branch scope matches how it runs, and a job acting for a requester asks for nothing
    /// that needs a re-authentication it can never have.
    /// </summary>
    [Fact]
    public void Arch021_EveryDeclarationIsOneTheCatalogueCanHonour()
    {
        var catalogue = new PermissionCatalogue([new ApplicationPermissions()]);
        var failures = new List<string>();

        foreach (var job in HostedServices())
        {
            var descriptor = WorkerJobDescriptor.Find(job);
            if (descriptor is null)
            {
                continue;
            }

            try
            {
                descriptor.Validate(catalogue);
            }
            catch (InvalidOperationException exception)
            {
                failures.Add($"{job.FullName}: {exception.Message}");
            }
        }

        failures.ShouldBeEmpty(
            "ARCH-021: these declarations cannot be honoured:\n" + string.Join('\n', failures));
    }

    /// <summary>
    /// Job names are the actor recorded against everything a job writes, so two jobs sharing one would
    /// make the audit trail ambiguous about which of them acted.
    /// </summary>
    [Fact]
    public void EveryWorkerJobNameIsDistinctAndReadsAsModuleThenJob()
    {
        var names = HostedServices()
            .Select(WorkerJobDescriptor.Find)
            .Where(descriptor => descriptor is not null)
            .Select(descriptor => descriptor!.Name)
            .ToArray();

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            names.Length, "two background jobs share a name, so the audit trail cannot tell them apart.");

        foreach (var name in names)
        {
            JobNamePattern().IsMatch(name).ShouldBeTrue(
                $"'{name}' is not in the module.job shape every job lease and audit actor uses.");
        }
    }

    /* Negative controls ------------------------------------------------------------------------ */

    /// <summary>The ARCH-020 detector must flag a module reaching for the scope factory.</summary>
    [Fact]
    public void Arch020DetectorCatchesAModuleBuildingItsOwnPrincipal()
    {
        const string offending = """
            namespace Sample;
            public sealed class NightlyReport(IWorkerScopeFactory scopes)
            {
                public void Run() => scopes.CreateSystemScope(typeof(NightlyReport));
            }
            """;

        var violations = SourceScanner.Scan(
            [("src/Modules/Reporting/Tailor360.Modules.Reporting.Application/NightlyReport.cs", offending)],
            ScopeFactoryPattern(),
            []);

        violations.Count.ShouldBe(
            1, "the ARCH-020 detector failed to flag a module resolving the worker scope factory.");
    }

    /// <summary>A rule quoted in a comment is documentation, not a breach.</summary>
    [Fact]
    public void Arch020DetectorIgnoresACommentedMention()
    {
        const string commented = """
            namespace Sample;
            public sealed class Notes
            {
                // Never resolve IWorkerScopeFactory here; take ICurrentUser instead.
                public int Answer => 42;
            }
            """;

        var violations = SourceScanner.Scan([("Sample/Notes.cs", commented)], ScopeFactoryPattern(), []);

        violations.ShouldBeEmpty("a rule quoted in a comment must not be reported as a violation.");
    }

    /// <summary>The ARCH-021 detector must flag a job that declares nothing.</summary>
    [Fact]
    public void Arch021DetectorCatchesAJobThatDeclaresNothing()
    {
        WorkerJobDescriptor.Find(typeof(UndeclaredSample)).ShouldBeNull(
            "the ARCH-021 detector failed to notice a job with no declaration.");

        Should.Throw<InvalidOperationException>(() => WorkerJobDescriptor.For(typeof(UndeclaredSample)));
    }

    /// <summary>The ARCH-021 detector must flag a declaration naming a permission no module owns.</summary>
    [Fact]
    public void Arch021DetectorCatchesADeclarationNamingAnUnknownPermission()
    {
        var descriptor = WorkerJobDescriptor.For(typeof(TypoSample));

        Should.Throw<InvalidOperationException>(
            () => descriptor.Validate(new PermissionCatalogue([new ApplicationPermissions()])));
    }

    /* Helpers ---------------------------------------------------------------------------------- */

    private static IReadOnlyList<Type> HostedServices()
        => [.. typeof(WorkerEntryPoint).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.GetInterfaces().Any(contract =>
                    string.Equals(
                        contract.FullName,
                        "Microsoft.Extensions.Hosting.IHostedService",
                        StringComparison.Ordinal)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    private static IEnumerable<(string Path, string Text)> SourceOutsideTheBackgroundHosts()
    {
        var sourceRoot = Path.Combine(RepositoryLayout.Root, "src");

        return RepositoryLayout.Projects
            .Where(project => !project.IsTestProject
                && project.Directory.StartsWith(sourceRoot, StringComparison.Ordinal))
            .SelectMany(project => project.SourceFiles)
            .Select(file => Path.GetRelativePath(RepositoryLayout.Root, file))
            .Where(relative => !HostsThatMayChooseAPrincipal.Any(allowed =>
                relative.StartsWith(allowed, StringComparison.Ordinal)))
            .Select(relative => (relative, File.ReadAllText(Path.Combine(RepositoryLayout.Root, relative))));
    }

    [GeneratedRegex(@"\bIWorkerScopeFactory\b", RegexOptions.None, 500)]
    private static partial Regex ScopeFactoryPattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$", RegexOptions.None, 500)]
    private static partial Regex JobNamePattern();

    private sealed class UndeclaredSample;

    [WorkerJob("sample.typo", WorkerBranchScope.None, "orders.does_not_exist")]
    private sealed class TypoSample;
}

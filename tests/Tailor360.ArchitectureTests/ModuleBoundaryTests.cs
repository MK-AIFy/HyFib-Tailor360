using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// The module boundary rules from <c>docs/architecture/architecture-rules.md</c>. Each test names the
/// rule it implements so that a failure points at the documented rationale rather than at a bare
/// assertion.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ModuleBoundaryTests
{
    /// <summary>ARCH-001: a Domain project may reference only the platform abstractions.</summary>
    [Fact]
    public void Arch001_DomainReferencesOnlyPlatformAbstractions()
    {
        foreach (var project in RepositoryLayout.ModuleLayer("Domain"))
        {
            project.ProjectReferences.ShouldBe(
                ["Tailor360.Platform.Abstractions"],
                $"ARCH-001: {project.Name} may reference only Tailor360.Platform.Abstractions, " +
                $"but references {Format(project.ProjectReferences)}.");
        }
    }

    /// <summary>
    /// ARCH-002: a Domain project may not reference infrastructure frameworks. The domain has to stay
    /// runnable in a plain unit test with no database and no host.
    /// </summary>
    [Fact]
    public void Arch002_DomainReferencesNoInfrastructureFrameworks()
    {
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore",
            "Serilog", "OpenTelemetry", "FluentValidation",
        ];

        foreach (var project in RepositoryLayout.ModuleLayer("Domain"))
        {
            foreach (var package in project.PackageReferences)
            {
                forbidden.ShouldNotContain(
                    prefix => package.StartsWith(prefix, StringComparison.Ordinal),
                    $"ARCH-002: {project.Name} references the infrastructure package {package}.");
            }
        }
    }

    /// <summary>
    /// ARCH-003: an Application project may not reach into another module's Infrastructure or Api. It
    /// may consume another module only through that module's Contracts.
    /// </summary>
    [Fact]
    public void Arch003_ApplicationNeverReferencesAnotherModulesInfrastructureOrApi()
    {
        foreach (var project in RepositoryLayout.ModuleLayer("Application"))
        {
            foreach (var reference in project.ProjectReferences)
            {
                var referenced = RepositoryLayout.Projects.SingleOrDefault(p => p.Name == reference);
                if (referenced is null || !referenced.IsModuleProject)
                {
                    continue;
                }

                if (string.Equals(referenced.Module, project.Module, StringComparison.Ordinal))
                {
                    continue;
                }

                referenced.Layer.ShouldBe(
                    "Contracts",
                    $"ARCH-003: {project.Name} references {reference}; a module may reach another " +
                    "module only through its Contracts project.");
            }
        }
    }

    /// <summary>
    /// ARCH-004: across a module boundary only Contracts and the platform libraries are visible. This is
    /// the rule that keeps the monolith modular and makes a later extraction possible.
    /// </summary>
    [Fact]
    public void Arch004_OnlyContractsAndPlatformCrossModuleBoundaries()
    {
        foreach (var project in RepositoryLayout.Projects.Where(p => p.IsModuleProject))
        {
            foreach (var reference in project.ProjectReferences)
            {
                var referenced = RepositoryLayout.Projects.SingleOrDefault(p => p.Name == reference);
                if (referenced is null)
                {
                    continue;
                }

                var crossesModule = referenced.IsModuleProject
                    && !string.Equals(referenced.Module, project.Module, StringComparison.Ordinal);

                if (!crossesModule)
                {
                    continue;
                }

                referenced.Layer.ShouldBe(
                    "Contracts",
                    $"ARCH-004: {project.Name} references {reference} across a module boundary.");
            }
        }
    }

    /// <summary>
    /// ARCH-010: Billing never references Orders. Billing is the module that must stay correct when an
    /// order is later corrected, so the dependency runs the other way, through the pricing contract.
    /// </summary>
    [Fact]
    public void Arch010_BillingNeverReferencesOrders()
    {
        foreach (var project in RepositoryLayout.Projects.Where(p => p.Module == "Billing"))
        {
            project.ProjectReferences.ShouldNotContain(
                reference => reference.StartsWith("Tailor360.Modules.Orders", StringComparison.Ordinal),
                $"ARCH-010: {project.Name} references the Orders module.");
        }
    }

    /// <summary>
    /// ARCH-011: Reporting reads other modules only through their Contracts, because a projection that
    /// reached into another module's tables would freeze that module's schema.
    /// </summary>
    [Fact]
    public void Arch011_ReportingReferencesOnlyContractsOfOtherModules()
    {
        foreach (var project in RepositoryLayout.Projects.Where(p => p.Module == "Reporting"))
        {
            foreach (var reference in project.ProjectReferences)
            {
                var referenced = RepositoryLayout.Projects.SingleOrDefault(p => p.Name == reference);
                if (referenced is null || !referenced.IsModuleProject || referenced.Module == "Reporting")
                {
                    continue;
                }

                referenced.Layer.ShouldBe(
                    "Contracts",
                    $"ARCH-011: {project.Name} references {reference}; Reporting may reference only " +
                    "Contracts projects of other modules.");
            }
        }
    }

    /// <summary>
    /// ARCH-012: nothing but a test project may reference a host. A module that referenced the web host
    /// would make the host impossible to change and the module impossible to test in isolation.
    /// </summary>
    [Fact]
    public void Arch012_OnlyTestsReferenceHosts()
    {
        string[] hosts = ["Tailor360.Web", "Tailor360.Worker"];

        foreach (var project in RepositoryLayout.Projects.Where(p => !p.IsTestProject))
        {
            foreach (var host in hosts)
            {
                project.ProjectReferences.ShouldNotContain(
                    host,
                    $"ARCH-012: {project.Name} references the host {host}.");
            }
        }
    }

    /// <summary>
    /// ARCH-009: vendor SDKs are confined to the Integration module and to tests, so that swapping a
    /// payment or messaging provider touches one project.
    /// </summary>
    [Fact]
    public void Arch009_VendorSdksAreConfinedToIntegrationInfrastructure()
    {
        // Extended as adapters arrive (#55). Listing the prefixes here rather than in a later issue
        // means a vendor package added to the wrong project fails on the first build.
        string[] vendorPrefixes =
        [
            "Razorpay", "Stripe", "Twilio", "SendGrid", "AWSSDK", "Google.Apis", "Zoho",
        ];

        foreach (var project in RepositoryLayout.Projects)
        {
            if (project.IsTestProject || project.Name == "Tailor360.Modules.Integration.Infrastructure")
            {
                continue;
            }

            foreach (var package in project.PackageReferences)
            {
                vendorPrefixes.ShouldNotContain(
                    prefix => package.StartsWith(prefix, StringComparison.Ordinal),
                    $"ARCH-009: {project.Name} references the vendor package {package}; vendor SDKs " +
                    "belong in Tailor360.Modules.Integration.Infrastructure.");
            }
        }
    }

    private static string Format(IReadOnlyList<string> values)
        => values.Count == 0 ? "nothing" : string.Join(", ", values);
}

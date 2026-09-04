using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>Rules about how the hosts compose the modules.</summary>
[Trait("Category", "Architecture")]
public sealed class HostCompositionTests
{
    /// <summary>
    /// ARCH-006: a host composes a module only through that module's registration extensions. If a host
    /// reached inside a module, moving a type would break the host and the module would no longer be a
    /// unit that can be reasoned about on its own.
    /// </summary>
    [Fact]
    public void Arch006_HostsComposeModulesOnlyThroughRegistrationExtensions()
    {
        var web = RepositoryLayout.Projects.Single(p => p.Name == "Tailor360.Web");
        var source = string.Join('\n', web.SourceFiles.Select(File.ReadAllText));

        foreach (var module in RepositoryLayout.ModuleNames)
        {
            source.ShouldContain(
                $"Add{module}Module(",
                Case.Sensitive,
                $"ARCH-006: the web host must register the {module} module through Add{module}Module.");

            source.ShouldContain(
                $"Map{module}Endpoints(",
                Case.Sensitive,
                $"ARCH-006: the web host must map the {module} module through Map{module}Endpoints.");
        }
    }

    /// <summary>
    /// ARCH-006: the host's imports name only a module's registration entry points, never a module's
    /// internal namespaces such as its Domain or Application layers.
    /// </summary>
    [Fact]
    public void Arch006_HostsDoNotReferenceModuleInternals()
    {
        string[] hosts = ["Tailor360.Web", "Tailor360.Worker"];

        foreach (var hostName in hosts)
        {
            var host = RepositoryLayout.Projects.Single(p => p.Name == hostName);

            foreach (var reference in host.ProjectReferences)
            {
                var referenced = RepositoryLayout.Projects.SingleOrDefault(p => p.Name == reference);
                if (referenced is null || !referenced.IsModuleProject)
                {
                    continue;
                }

                referenced.Layer.ShouldBeOneOf(
                    ["Api", "Infrastructure", "Contracts"],
                    $"ARCH-006: {hostName} references {reference}; a host may reference only a module's " +
                    "Api, Infrastructure or Contracts project, which carry its registration extensions.");
            }
        }
    }

    /// <summary>
    /// Every module is registered by the host. A module that is built but never registered is dead
    /// weight that still passes every other test, so the count is asserted explicitly.
    /// </summary>
    [Fact]
    public void EveryModuleIsRegisteredByTheWebHost()
    {
        var registeredModules = RepositoryLayout.Projects
            .Where(p => p.IsModuleProject && p.Layer == "Api")
            .Select(p => p.Module!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal);

        registeredModules.ShouldBe(
            RepositoryLayout.ModuleNames.OrderBy(m => m, StringComparer.Ordinal),
            "The set of module Api projects must match the documented module list.");
    }
}

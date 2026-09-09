using System.Security.Cryptography;
using System.Text;
using Tailor360.Modules.Catalog.Domain.Catalogue;
using Tailor360.Platform.Abstractions.Identifiers;

namespace Tailor360.UnitTests.Catalog;

/// <summary>
/// Synthetic catalogue fixtures. Nothing here is a real shop's configuration.
/// </summary>
/// <remarks>
/// Identifiers are derived from a name rather than generated, so a failure names the thing that failed
/// and a re-run produces the same tree. They are shaped as UUIDv7 only in the sense that matters to a
/// test: distinct and stable.
/// </remarks>
internal static class CatalogTestData
{
    /// <summary>The organisation every fixture belongs to.</summary>
    public static Guid Organisation { get; } = Id("organisation");

    /// <summary>Two branches, so the subset rule has something to be wrong about.</summary>
    public static Guid MainBranch { get; } = Id("branch-main");

    /// <summary>A second branch, which offers less than the first.</summary>
    public static Guid SecondBranch { get; } = Id("branch-second");

    /// <summary>The instant every fixture is created at.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 9, 9, 4, 30, 0, TimeSpan.Zero);

    /// <summary>A stable identifier for a name.</summary>
    /// <param name="name">What the identifier stands for.</param>
    /// <returns>The identifier.</returns>
    public static Guid Id(string name)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    /// <summary>An empty draft.</summary>
    /// <param name="number">The version number.</param>
    /// <returns>The draft.</returns>
    public static CatalogVersion Draft(int number = 1)
        => CatalogVersion.CreateDraft(
                Id($"version-{number}"), Organisation, number, $"Version {number}", null, Now, null)
            .Value;

    /// <summary>Everything an administrator says about a category, with sensible defaults.</summary>
    /// <param name="code">The machine key.</param>
    /// <param name="branches">The branches offering it, defaulting to the main branch.</param>
    /// <param name="displayOrder">Where it sits among its siblings.</param>
    /// <param name="activeFrom">The first day it is offered.</param>
    /// <param name="activeTo">The last day it is offered.</param>
    /// <param name="featureFlagKey">The flag that can switch it off.</param>
    /// <returns>The details.</returns>
    public static CategoryDetails CategoryOf(
        string code,
        IReadOnlyCollection<Guid>? branches = null,
        int displayOrder = 0,
        DateOnly? activeFrom = null,
        DateOnly? activeTo = null,
        string? featureFlagKey = null)
        => new(
            code,
            code.Replace('_', ' '),
            null,
            null,
            displayOrder,
            activeFrom,
            activeTo,
            featureFlagKey,
            branches ?? [MainBranch]);

    /// <summary>Everything an administrator says about a service type, with every link supplied.</summary>
    /// <param name="code">The machine key.</param>
    /// <param name="branches">The branches offering it, defaulting to the main branch.</param>
    /// <param name="complete">Whether the five links are supplied.</param>
    /// <param name="allowIncomplete">Whether publication may proceed with links missing.</param>
    /// <param name="activeFrom">The first day it is offered.</param>
    /// <param name="activeTo">The last day it is offered.</param>
    /// <returns>The details.</returns>
    public static ServiceTypeDetails ServiceOf(
        string code,
        IReadOnlyCollection<Guid>? branches = null,
        bool complete = true,
        bool allowIncomplete = false,
        DateOnly? activeFrom = null,
        DateOnly? activeTo = null)
        => new(
            code,
            code.Replace('_', ' '),
            null,
            null,
            0,
            3,
            null,
            complete ? Id($"template-{code}") : null,
            complete ? Id($"workflow-{code}") : null,
            [],
            complete ? $"PL-{code}" : null,
            complete ? Id($"checklist-{code}") : null,
            allowIncomplete,
            activeFrom,
            activeTo,
            branches ?? [MainBranch]);
}

/// <summary>Hands out distinct, repeatable identifiers.</summary>
/// <remarks>
/// A counter rather than a fixed list, because cloning a version mints one identifier per row and how
/// many that is depends on the tree. What the assertions care about is that they are distinct and that
/// the same test run twice produces the same ones.
/// </remarks>
internal sealed class CountingCatalogIds : IIdGenerator
{
    private int _issued;

    /// <summary>How many identifiers have been handed out.</summary>
    public int Issued => _issued;

    /// <inheritdoc />
    public Guid NewId()
    {
        _issued++;
        return CatalogTestData.Id($"generated-{_issued}");
    }
}

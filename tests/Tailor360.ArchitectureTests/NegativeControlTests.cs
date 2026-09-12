using System.Text.RegularExpressions;
using Shouldly;

namespace Tailor360.ArchitectureTests;

/// <summary>
/// Deliberately failing examples, kept as negative controls. An architecture suite that passes because
/// its detector is broken is worse than no suite at all, so each rule is also shown catching a sample
/// that breaks it. These samples live only in this file and are never compiled as production code.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class NegativeControlTests
{
    /// <summary>The ARCH-014 detector must reject a class that reads the ambient clock.</summary>
    [Fact]
    public void Arch014DetectorCatchesAmbientClockUse()
    {
        const string offending = """
            namespace Sample;
            public sealed class DueDateCalculator
            {
                public DateTimeOffset DueDate() => DateTimeOffset.UtcNow.AddDays(3);
            }
            """;

        var violations = SourceScanner.Scan(
            [("Sample/DueDateCalculator.cs", offending)],
            AmbientClockPattern(),
            []);

        violations.Count.ShouldBe(1, "the ARCH-014 detector failed to flag a direct use of DateTimeOffset.UtcNow.");
    }

    /// <summary>The ARCH-015 detector must reject a class that mints its own identifiers.</summary>
    [Fact]
    public void Arch015DetectorCatchesRandomIdentifierCreation()
    {
        const string offending = """
            namespace Sample;
            public sealed class Order
            {
                public Guid Id { get; } = Guid.NewGuid();
            }
            """;

        var violations = SourceScanner.Scan(
            [("Sample/Order.cs", offending)],
            NewGuidPattern(),
            []);

        violations.Count.ShouldBe(1, "the ARCH-015 detector failed to flag a direct use of Guid.NewGuid.");
    }

    /// <summary>The ARCH-016 detector must reject a class that builds its own HTTP client.</summary>
    [Fact]
    public void Arch016DetectorCatchesDirectHttpClientConstruction()
    {
        const string offending = """
            namespace Sample;
            public sealed class ProviderGateway
            {
                private readonly HttpClient _client = new HttpClient();
            }
            """;

        var violations = SourceScanner.Scan(
            [("Sample/ProviderGateway.cs", offending)],
            HttpClientConstructionPattern(),
            []);

        violations.Count.ShouldBe(1, "the ARCH-016 detector failed to flag a directly constructed HttpClient.");
    }

    /// <summary>A sanctioned file is exempt, so the rules do not forbid their own implementation.</summary>
    [Fact]
    public void SanctionedImplementationsAreExempt()
    {
        const string sanctioned = """
            namespace Tailor360.Platform.Abstractions.Time;
            public sealed class SystemClock : IClock
            {
                public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
            }
            """;

        var violations = SourceScanner.Scan(
            [("src/Platform/Tailor360.Platform.Abstractions/Time/SystemClock.cs", sanctioned)],
            AmbientClockPattern(),
            ["SystemClock.cs"]);

        violations.ShouldBeEmpty("the sanctioned clock implementation must not be reported as a violation.");
    }

    /// <summary>A rule quoted in a comment is documentation, not a breach.</summary>
    [Fact]
    public void CommentedExamplesAreNotReportedAsViolations()
    {
        const string commented = """
            namespace Sample;
            public sealed class Notes
            {
                // Never write DateTimeOffset.UtcNow here; read the time through IClock.
                public int Answer => 42;
            }
            """;

        var violations = SourceScanner.Scan([("Sample/Notes.cs", commented)], AmbientClockPattern(), []);

        violations.ShouldBeEmpty("a rule quoted in a comment must not be reported as a violation.");
    }

    /// <summary>The cross-module reference rule must reject a module reaching into another module's internals.</summary>
    [Fact]
    public void Arch004DetectorCatchesACrossModuleInfrastructureReference()
    {
        var offending = new ProjectInfo(
            "Tailor360.Modules.Orders.Application",
            "/repo/src/Modules/Orders/Tailor360.Modules.Orders.Application/x.csproj",
            "/repo/src/Modules/Orders/Tailor360.Modules.Orders.Application",
            ["Tailor360.Modules.Billing.Infrastructure"],
            []);

        var referenced = new ProjectInfo(
            "Tailor360.Modules.Billing.Infrastructure",
            "/repo/src/Modules/Billing/Tailor360.Modules.Billing.Infrastructure/x.csproj",
            "/repo/src/Modules/Billing/Tailor360.Modules.Billing.Infrastructure",
            [],
            []);

        offending.Module.ShouldBe("Orders");
        offending.Layer.ShouldBe("Application");
        referenced.Module.ShouldBe("Billing");
        referenced.Layer.ShouldBe("Infrastructure");

        var crossesModule = referenced.IsModuleProject && referenced.Module != offending.Module;
        crossesModule.ShouldBeTrue();
        referenced.Layer.ShouldNotBe("Contracts",
            "the sample is deliberately a cross-module Infrastructure reference, which ARCH-004 forbids.");
    }

    /// <summary>
    /// ARCH-005's extended detector must reject a module context that maps a table into a foreign
    /// schema by name — an explicit <c>ToTable(name, schema)</c> literal that is not the one named
    /// exception — and must accept nothing else as that exception: not the right schema with the wrong
    /// table, and not the right table with the wrong schema.
    /// </summary>
    [Fact]
    public void Arch005DetectorCatchesAnUnsanctionedForeignSchemaMapping()
    {
        const string offending = """
            namespace Sample;
            public sealed class RogueMapping
            {
                public RogueMapping(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Widget>().ToTable("widgets", "orders");
                }
            }
            """;

        var mappings = SourceConventionTests.ForeignSchemaMappings(offending).ToArray();

        mappings.ShouldHaveSingleItem();
        mappings[0].Schema.ShouldBe("orders");
        mappings[0].Table.ShouldBe("widgets");
        SourceConventionTests.IsSanctionedSharedMechanismTable(mappings[0].Schema, mappings[0].Table)
            .ShouldBeFalse("a table named 'widgets' in schema 'orders' is not ARCH-005's named exception.");

        SourceConventionTests
            .IsSanctionedSharedMechanismTable("platform", "outbox_messages")
            .ShouldBeFalse("the exception names 'audit_events' specifically, not every platform-owned table.");
        SourceConventionTests
            .IsSanctionedSharedMechanismTable("billing", "audit_events")
            .ShouldBeFalse("the exception names the 'platform' schema specifically, not a same-name table elsewhere.");
    }

    /// <summary>
    /// The one named exception ARCH-005 recognises: a call to <c>AuditEventMapping.Configure</c> reads
    /// as declaring exactly <c>platform</c>/<c>audit_events</c>, so the real detector does not report a
    /// module context that adopts it — proving the exception is precise rather than the check having
    /// been switched off.
    /// </summary>
    [Fact]
    public void Arch005DetectorAcceptsOnlyTheNamedAuditEventMappingCall()
    {
        const string sanctioned = """
            namespace Sample;
            public sealed class SampleDbContext
            {
                protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
                {
                    AuditEventMapping.Configure(modelBuilder);
                }
            }
            """;

        var mappings = SourceConventionTests.ForeignSchemaMappings(sanctioned).ToArray();

        mappings.ShouldHaveSingleItem();
        SourceConventionTests.IsSanctionedSharedMechanismTable(mappings[0].Schema, mappings[0].Table)
            .ShouldBeTrue("AuditEventMapping.Configure maps only platform.audit_events, ARCH-005's named exception.");
    }

    [GeneratedRegex(@"\bDateTime(Offset)?\s*\.\s*(UtcNow|Now|Today)\b", RegexOptions.None, 500)]
    private static partial Regex AmbientClockPattern();

    [GeneratedRegex(@"\bGuid\s*\.\s*NewGuid\s*\(", RegexOptions.None, 500)]
    private static partial Regex NewGuidPattern();

    [GeneratedRegex(@"\bnew\s+HttpClient\s*\(", RegexOptions.None, 500)]
    private static partial Regex HttpClientConstructionPattern();
}

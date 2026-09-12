using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Money;

namespace Tailor360.UnitTests.Orders;

/// <summary>
/// The <c>orders</c> schema as the model declares it, asserted without a database.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this tier can assert a schema at all.</strong> A model is built from metadata, not from a server:
/// <see cref="OrdersDbContextFactory"/>'s placeholder connection string resolves to nothing, and building the
/// model never opens it. So the conventions <c>src/Modules/CLAUDE.md</c> section 5 fixes — one schema,
/// <c>snake_case</c>, <c>timestamptz</c>, <c>decimal(18,4)</c> money, <c>xmin</c> on every editable row — are
/// checkable here rather than only against a live PostgreSQL. What still belongs to the integration tier is
/// everything the model cannot state: that the triggers fire, that the check constraints refuse, that a query
/// returns rows.
/// </para>
/// <para>
/// <strong>The conventions are inherited, which is exactly why they are worth asserting.</strong>
/// <c>ModuleDbContext</c> applies them by walking the model, so a module cannot drift from them by omission — but
/// that walk reaches an entity type's own properties and <em>not</em> the properties of a complex type. Every
/// instant and every amount inside <see cref="Tailor360.Modules.Orders.Domain.Snapshots.PriceSnapshot"/> is
/// therefore configured by hand in <c>OrdersDbContext</c>, and a hand-written facet is one a later edit can drop
/// silently. These tests walk complex types too.
/// </para>
/// <para>
/// ARCH-005 — no <c>DbContext</c> maps a table in another module's schema — is enforced over the whole solution
/// by the architecture tier. It is restated here for this one context because this is where a reviewer reading
/// the Orders schema will look, and because a bare <c>uuid</c> that points into another schema is the thing this
/// model deliberately does not turn into a foreign key.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OrdersSchemaMappingTests
{
    private static readonly IModel Model = BuildModel();

    /* One schema, and only one (ARCH-005) -------------------------------------------------------- */

    [Fact]
    public void EveryTableIsInTheSchemaThisModuleOwns()
    {
        var schemas = Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(entity => entity.GetSchema() ?? Model.GetDefaultSchema())
            .Distinct()
            .ToList();

        schemas.ShouldBe([OrdersDbContext.SchemaName]);
    }

    /// <summary>
    /// The module's outbox and inbox are its own tables in its own schema, not the platform's. Which schema a
    /// published event lands in is decided by the context it is tracked on, so a module whose outbox was mapped
    /// elsewhere would publish into a table another module's dispatcher claims from.
    /// </summary>
    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("inbox_messages")]
    public void TheModulesOwnOutboxAndInboxAreInItsOwnSchema(string table)
    {
        var entity = Model.GetEntityTypes().SingleOrDefault(e => e.GetTableName() == table);

        entity.ShouldNotBeNull();
        (entity.GetSchema() ?? Model.GetDefaultSchema()).ShouldBe(OrdersDbContext.SchemaName);
    }

    /// <summary>
    /// No relationship leaves this module. <c>customer_id</c>, <c>catalog_version_id</c> and the media
    /// identifiers are bare <c>uuid</c> columns with no <c>HasOne</c>: a key across the boundary would be an
    /// ARCH-005 violation, and the rows behind them are read through their owners' published contracts instead.
    /// </summary>
    [Fact]
    public void NoForeignKeyPointsOutOfThisModule()
    {
        var crossing = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => (key.PrincipalEntityType.GetSchema() ?? Model.GetDefaultSchema())
                          != OrdersDbContext.SchemaName)
            .Select(key => $"{key.DeclaringEntityType.GetTableName()} -> "
                           + $"{key.PrincipalEntityType.GetTableName()}")
            .ToList();

        crossing.ShouldBeEmpty();
    }

    /* Naming -------------------------------------------------------------------------------------- */

    [Fact]
    public void EveryTableIsNamedInSnakeCase()
    {
        var offenders = Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => name is not null && !IsSnakeCase(name))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// <c>xmin</c> is excluded because it is PostgreSQL's own system column and is named by the server, not by
    /// this convention.
    /// </summary>
    [Fact]
    public void EveryColumnIsNamedInSnakeCase()
    {
        var offenders = Columns()
            .Select(column => column.Name)
            .Where(name => name != "xmin" && !IsSnakeCase(name))
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /* Time ---------------------------------------------------------------------------------------- */

    /// <summary>
    /// Every instant is stored with its offset; a bare <c>timestamp</c> would be ambiguous the moment a second
    /// branch in another timezone is added. The walk includes complex types, which the base convention's own
    /// walk does not reach.
    /// </summary>
    [Fact]
    public void EveryInstantIsStoredAsTimestamptz()
    {
        var offenders = Columns()
            .Where(column => column.ClrType == typeof(DateTimeOffset)
                             || column.ClrType == typeof(DateTimeOffset?)
                             || column.ClrType == typeof(DateTime)
                             || column.ClrType == typeof(DateTime?))
            .Where(column => column.ColumnType != "timestamptz")
            .Select(column => $"{column.Table}.{column.Name} is {column.ColumnType ?? "unset"}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The instant a price was calculated sits inside a complex property, so it is configured by hand rather than
    /// by the convention walk. Named on its own because losing its column type would leave the schema silently
    /// storing a local timestamp.
    /// </summary>
    [Fact]
    public void ThePriceCopysCalculatedInstantIsTimestamptzOnEveryTableThatCarriesOne()
    {
        var calculated = Columns()
            .Where(column => column.Name.EndsWith("calculated_at", StringComparison.Ordinal))
            .ToList();

        calculated.ShouldNotBeEmpty();
        calculated.ShouldAllBe(column => column.ColumnType == "timestamptz");
    }

    /* Money --------------------------------------------------------------------------------------- */

    /// <summary>
    /// <c>decimal(18,4)</c> is the money convention. A binary float cannot represent a tax figure exactly, and a
    /// rounding difference in the fourth place is a reconciliation failure rather than a display one.
    /// </summary>
    [Fact]
    public void NoColumnAnywhereHoldsMoneyInABinaryFloat()
    {
        var offenders = Columns()
            .Where(column => column.ClrType == typeof(double) || column.ClrType == typeof(double?)
                             || column.ClrType == typeof(float) || column.ClrType == typeof(float?))
            .Select(column => $"{column.Table}.{column.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// Every amount of a <see cref="Money"/> is <c>numeric(18,4)</c>. <see cref="Money.Amount"/> is get-only and
    /// a get-only property is not discovered by convention, so each one is configured by hand — which is what
    /// makes an assertion over all of them worth having.
    /// </summary>
    [Fact]
    public void EveryMoneyAmountIsHeldToFourDecimalPlaces()
    {
        var amounts = Columns()
            .Where(column => column.Name.EndsWith("_amount", StringComparison.Ordinal))
            .ToList();

        amounts.ShouldNotBeEmpty();

        foreach (var amount in amounts)
        {
            amount.ClrType.ShouldBe(typeof(decimal), amount.Name);
            amount.Precision.ShouldBe(18, amount.Name);
            amount.Scale.ShouldBe(Money.InternalScale, amount.Name);
        }
    }

    /// <summary>Every amount is accompanied by the currency it is in; an amount alone is not a sum of money.</summary>
    [Fact]
    public void EveryMoneyAmountIsAccompaniedByItsCurrency()
    {
        var columns = Columns().ToList();

        foreach (var amount in columns.Where(c => c.Name.EndsWith("_amount", StringComparison.Ordinal)))
        {
            var currency = amount.Name[..^"_amount".Length] + "_currency";

            columns.ShouldContain(
                c => c.Table == amount.Table && c.Name == currency,
                $"{amount.Table}.{amount.Name} has no {currency}");
        }
    }

    /* Concurrency --------------------------------------------------------------------------------- */

    /// <summary>
    /// The five tables anything rewrites carry the token, and the token is what turns a second writer into a
    /// conflict the screen can explain instead of a silent overwrite.
    /// </summary>
    [Theory]
    [InlineData("order_drafts")]
    [InlineData("order_draft_garments")]
    [InlineData("estimates")]
    [InlineData("orders")]
    [InlineData("garment_jobs")]
    public void EveryEditableRowCarriesTheConcurrencyToken(string table)
    {
        var token = TokenOf(table);

        token.ShouldNotBeNull($"{table} is edited after it is created and must carry xmin");
        token.IsConcurrencyToken.ShouldBeTrue();
        token.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
    }

    /// <summary>
    /// The three append-only tables do not carry one, and that is a statement rather than an omission: there is
    /// nothing to overwrite, and the migration installs the triggers that make that true of the database and not
    /// only of the domain type.
    /// </summary>
    [Theory]
    [InlineData("order_revisions")]
    [InlineData("job_dependencies")]
    [InlineData("order_draft_garment_dependencies")]
    public void AnAppendOnlyRowCarriesNoConcurrencyToken(string table)
        => TokenOf(table).ShouldBeNull($"{table} is append-only, so a token would guard nothing");

    /* The constraints the write-failure table maps ------------------------------------------------ */

    /// <summary>
    /// <c>OrdersWriteFailures</c> maps a refusal by the name PostgreSQL gives the constraint, and every name it
    /// matches on is a constant declared beside the index that creates it. This holds the two halves together:
    /// an index renamed in the model without the mapping following would leave that refusal unreachable, and the
    /// failure would surface as an unmapped <c>PostgresException</c> rather than as a result.
    /// </summary>
    [Theory]
    [InlineData(OrdersDbContext.OrderNumberIndex)]
    [InlineData(OrdersDbContext.EstimateNumberIndex)]
    [InlineData(OrdersDbContext.GarmentJobNumberIndex)]
    [InlineData(OrdersDbContext.GarmentJobOrderIndexIndex)]
    [InlineData(OrdersDbContext.OrderRevisionNumberIndex)]
    [InlineData(OrdersDbContext.OrderEstimateIndex)]
    [InlineData(OrdersDbContext.DraftGarmentPositionIndex)]
    [InlineData(OrdersDbContext.JobDependencyIndex)]
    [InlineData(OrdersDbContext.GarmentJobReadyIndex)]
    public void EveryIndexTheWriteFailureTableNamesIsDeclaredByTheModel(string name)
    {
        var declared = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .Any(index => index.GetDatabaseName() == name);

        declared.ShouldBeTrue($"no index in the model is named {name}");
    }

    /// <summary>
    /// The eight constraints mapped to a refusal a caller can act on are unique, which is what makes a violation
    /// of one a conflict rather than a defect. <c>GarmentJobReadyIndex</c> is deliberately not in this list: it
    /// serves the ready queue and is not unique.
    /// </summary>
    [Theory]
    [InlineData(OrdersDbContext.OrderNumberIndex)]
    [InlineData(OrdersDbContext.EstimateNumberIndex)]
    [InlineData(OrdersDbContext.GarmentJobNumberIndex)]
    [InlineData(OrdersDbContext.GarmentJobOrderIndexIndex)]
    [InlineData(OrdersDbContext.OrderRevisionNumberIndex)]
    [InlineData(OrdersDbContext.OrderEstimateIndex)]
    [InlineData(OrdersDbContext.DraftGarmentPositionIndex)]
    [InlineData(OrdersDbContext.JobDependencyIndex)]
    public void EveryConstraintMappedToAConflictIsActuallyUnique(string name)
    {
        var index = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .Single(candidate => candidate.GetDatabaseName() == name);

        index.IsUnique.ShouldBeTrue($"{name} is mapped to a conflict, so a duplicate must be refused");
    }

    /// <summary>
    /// The draft's garment key is the one mapped constraint that is a primary key rather than an index, which is
    /// why it is named <c>pk_order_draft_garments</c> and why it is asserted separately.
    /// </summary>
    [Fact]
    public void TheDraftGarmentKeyIsThePrimaryKeyTheMappingNames()
    {
        var draftGarments = Model.GetEntityTypes().Single(e => e.GetTableName() == "order_draft_garments");

        draftGarments.FindPrimaryKey()!.GetName().ShouldBe(OrdersDbContext.DraftGarmentKey);
    }

    /* The model builds at all --------------------------------------------------------------------- */

    /// <summary>
    /// The design-time factory reaches no database, which is what stops a scaffolding command touching a real
    /// server by accident — and is also what lets this whole class run in a tier that has none.
    /// </summary>
    [Fact]
    public void TheDesignTimeFactoryBuildsAContextThatReachesNoRealServer()
    {
        using var context = new OrdersDbContextFactory().CreateDbContext([]);

        context.Model.ShouldNotBeNull();
        OrdersDbContextFactory.DesignTimeConnectionString.ShouldContain("tailor360_design_time");
    }

    /// <summary>Each of the four aggregate sets the module exposes resolves to a table in this schema.</summary>
    [Fact]
    public void TheModulesAggregateSetsAreMapped()
    {
        using var context = new OrdersDbContextFactory().CreateDbContext([]);

        context.OrderDrafts.EntityType.GetTableName().ShouldBe(OrdersDbContext.OrderDraftsTable);
        context.Estimates.EntityType.GetTableName().ShouldBe(OrdersDbContext.EstimatesTable);
        context.Orders.EntityType.GetTableName().ShouldBe("orders");
        context.GarmentJobs.EntityType.GetTableName().ShouldBe("garment_jobs");
    }

    /* Helpers ------------------------------------------------------------------------------------- */

    private static IModel BuildModel()
    {
        using var context = new OrdersDbContextFactory().CreateDbContext([]);

        return context.Model;
    }

    private static IProperty? TokenOf(string table)
        => Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() == table)
            .SelectMany(entity => entity.GetProperties())
            .FirstOrDefault(property => property.Name == "xmin");

    /// <summary>
    /// Every mapped column of every table, including those contributed by a complex type. Entity Framework
    /// reports a complex type's properties only through the complex property that holds it, so a walk over
    /// <c>GetProperties()</c> alone would miss every amount and the price copy's instant — the columns this
    /// context configures by hand and the ones most worth checking.
    /// </summary>
    private static IEnumerable<Column> Columns()
    {
        foreach (var entity in Model.GetEntityTypes())
        {
            var table = entity.GetTableName();

            if (table is null)
            {
                continue;
            }

            var identifier = StoreObjectIdentifier.Table(table, entity.GetSchema() ?? Model.GetDefaultSchema());

            foreach (var column in Walk(entity.GetProperties(), entity.GetComplexProperties(), table, identifier))
            {
                yield return column;
            }
        }
    }

    private static IEnumerable<Column> Walk(
        IEnumerable<IProperty> properties,
        IEnumerable<IComplexProperty> complexProperties,
        string table,
        StoreObjectIdentifier identifier)
    {
        foreach (var property in properties)
        {
            var name = property.GetColumnName(identifier) ?? property.GetColumnName();

            if (name is not null)
            {
                yield return new Column(
                    table,
                    name,
                    property.ClrType,
                    property.GetColumnType(),
                    property.GetPrecision(),
                    property.GetScale());
            }
        }

        foreach (var complex in complexProperties)
        {
            var nested = Walk(
                complex.ComplexType.GetProperties(),
                complex.ComplexType.GetComplexProperties(),
                table,
                identifier);

            foreach (var column in nested)
            {
                yield return column;
            }
        }
    }

    /// <summary>
    /// Lower case, digits and single underscores, never leading or trailing, never doubled. A name that merely
    /// happens to be lower case — <c>orderdrafts</c> — passes, which is the limit of what a naming rule can see;
    /// the point of the check is to catch the camel case a hand-written <c>HasColumnName</c> introduces.
    /// </summary>
    private static bool IsSnakeCase(string name)
        => name.Length > 0
           && name.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character)
                                    || character == '_')
           && !name.StartsWith('_')
           && !name.EndsWith('_')
           && !name.Contains("__", StringComparison.Ordinal);

    private sealed record Column(
        string Table,
        string Name,
        Type ClrType,
        string? ColumnType,
        int? Precision,
        int? Scale);
}

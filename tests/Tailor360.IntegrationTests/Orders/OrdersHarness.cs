using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Tailor360.IntegrationTests.Identity;
using Tailor360.Modules.Orders.Application.Abstractions;
using Tailor360.Modules.Orders.Domain.DisplayNumbers;
using Tailor360.Modules.Orders.Domain.Estimates;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Orders;
using Tailor360.Modules.Orders.Domain.Snapshots;
using Tailor360.Modules.Orders.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Money;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.IntegrationTests.Orders;

/// <summary>
/// Arranges confirmed orders, estimates and the refusals the <c>orders</c> schema raises, for the tests that need
/// a real PostgreSQL to say anything at all.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything is arranged through the Domain and written through the real stores</strong>, which is the
/// whole point of this tier: <c>docs/architecture/module-ownership.md</c> section 5.5 gives Orders ten tables, the
/// migration adds eight triggers over them, and none of that is exercised by a fixture that inserts rows itself.
/// The one thing written as raw SQL is a write that <em>reached a table without going through the Domain</em>,
/// because that is exactly what the triggers exist to refuse and there is no other way to produce one.
/// </para>
/// <para>
/// <strong>Orders has no HTTP surface yet</strong> — <c>OrdersEndpoints.MapOrdersEndpoints</c> maps a group and
/// nothing in it — so these tests reach the module the way Billing and Custody will: through
/// <c>IOrderStore</c>, <c>IEstimateStore</c> and <c>IOrderSnapshotQuery</c>, resolved from the hosted
/// application's own container. That keeps the composition under test as well: a store registered against the
/// wrong context, or a context whose migration had not run, fails here rather than at the first endpoint.
/// </para>
/// <para>
/// <strong>Every value is synthetic.</strong> No name, telephone number, address or measurement below belongs to
/// anybody; the customer is an identifier and nothing else, which is what <c>Order</c> itself holds
/// (<c>docs/nfr/data-classification.md</c> section 5.2). The measurement copy carries one round chest figure a
/// tape would read, and the design copy one neckline.
/// </para>
/// <para>
/// <strong>The branch code is unique per test and that is load-bearing.</strong> INV-ORD-03 makes a display
/// number unique per organisation, and the whole collection shares one database that is never emptied between
/// runs — so two tests confirming "order one of financial year 2026-27" would collide on
/// <c>ux_orders_organisation_number</c> for a reason that has nothing to do with either of them. A test that
/// <em>wants</em> the collision asks for it by passing one branch code twice.
/// </para>
/// </remarks>
internal static class OrdersHarness
{
    /// <summary>The instant the fixtures are built at, in the branch timezone's offset.</summary>
    /// <remarks>
    /// Fixed rather than taken from <c>IClock</c>: every Orders aggregate takes the instant as a parameter
    /// (ARCH-014), so a fixed value is all the clock these fixtures need, and a stored instant can then be
    /// asserted exactly rather than within a window.
    /// </remarks>
    public static readonly DateTimeOffset Now = new(2026, 5, 7, 11, 5, 0, TimeSpan.FromHours(5.5));

    /// <summary>The promised date every fixture confirms against.</summary>
    public static readonly DateOnly DueDate = new(2026, 5, 21);

    /// <summary>The branch-local date an estimate is issued on.</summary>
    public static readonly DateOnly IssuedOn = new(2026, 5, 7);

    /// <summary>The published catalogue version every snapshot on one order is taken against (INV-ORD-02).</summary>
    public static readonly Guid CatalogVersion = Guid.Parse("0199d000-0000-7000-8000-0000000000c1");

    /// <summary>The price-list version every priced result is calculated from (INV-ORD-02).</summary>
    public static readonly Guid PriceListVersion = Guid.Parse("0199d000-0000-7000-8000-0000000000c2");

    /// <summary>The tax configuration version every priced result is calculated from (INV-ORD-02).</summary>
    public static readonly Guid TaxConfigurationVersion = Guid.Parse("0199d000-0000-7000-8000-0000000000c3");

    /// <summary>The workflow definition a garment job records at confirmation.</summary>
    public static readonly Guid WorkflowDefinition = Guid.Parse("0199d000-0000-7000-8000-0000000000c4");

    /// <summary>The workflow version a garment job pins when it enters production (INV-JOB-02).</summary>
    public static readonly Guid WorkflowVersion = Guid.Parse("0199d000-0000-7000-8000-0000000000c5");

    /// <summary>The measurement template the frozen copy names as its provenance.</summary>
    public static readonly Guid MeasurementTemplate = Guid.Parse("0199d000-0000-7000-8000-0000000000c6");

    /// <summary>The template version the frozen copy was taken against.</summary>
    public static readonly Guid MeasurementTemplateVersion = Guid.Parse("0199d000-0000-7000-8000-0000000000c7");

    /// <summary>The branch the fixtures take orders at. A bare identifier; Orders holds no foreign key to it.</summary>
    public static readonly Guid Branch = Guid.Parse("0199d000-0000-7000-8000-0000000000b1");

    /// <summary>The member of staff the fixtures act as.</summary>
    public static readonly Guid Actor = Guid.Parse("0199d000-0000-7000-8000-0000000000a1");

    /// <summary>The customer the fixtures commit to. An identifier: no name, no number, no address.</summary>
    public static readonly Guid Customer = Guid.Parse("0199d000-0000-7000-8000-0000000000e1");

    /// <summary>The organisation every fixture belongs to, shared with the rest of the suite.</summary>
    public static Guid Organisation => SessionTestData.OrganisationId;

    /// <summary>The cancellation token of the test that is running.</summary>
    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A branch code no other test in this run uses.</summary>
    /// <remarks>
    /// Upper-case ASCII letters and digits only, which is what <c>DisplayNumberFormat.IsWellFormedBranchCode</c>
    /// allows; the token is hexadecimal, so upper-casing it is enough.
    /// </remarks>
    /// <param name="prefix">Three upper-case letters naming the test, so a stray row can be traced back.</param>
    /// <returns>The branch code.</returns>
    public static string BranchCode(string prefix)
        => $"{prefix}{AdministrationHarness.UniqueToken(6)}".ToUpperInvariant();

    /// <summary>The financial year the fixtures allocate their display numbers in.</summary>
    /// <param name="startYear">The year the financial year opens in.</param>
    /// <returns>The financial year.</returns>
    public static FinancialYear Year(int startYear = 2026) => FinancialYear.FromStartYear(startYear).Value;

    /// <summary>An allocated order number.</summary>
    /// <param name="branchCode">The branch the sequence belongs to.</param>
    /// <param name="sequence">The position within that branch and financial year.</param>
    /// <returns>The number.</returns>
    public static OrderNumber Number(string branchCode, long sequence = 1)
        => OrderNumber.Create(branchCode, Year(), sequence).Value;

    /// <summary>A garment number minted from the order number its confirmation allocated.</summary>
    /// <remarks>
    /// Minted rather than composed by hand: a job number carries the <c>J</c> namespace and its own two-digit
    /// position, so a test that spelled it as "the order number and a suffix" would assert the wrong string and
    /// would keep asserting it after the format changed.
    /// </remarks>
    /// <param name="branchCode">The branch the sequence belongs to.</param>
    /// <param name="jobIndex">The one-based position of the garment within the order.</param>
    /// <param name="sequence">The order's position within that branch and financial year.</param>
    /// <returns>The number.</returns>
    public static GarmentJobNumber JobNumber(string branchCode, int jobIndex, long sequence = 1)
        => GarmentJobNumber.For(Number(branchCode, sequence), jobIndex).Value;

    /// <summary>An allocated estimate number, from the estimate series and never the order series (INV-ORD-04).</summary>
    /// <param name="branchCode">The branch the sequence belongs to.</param>
    /// <param name="sequence">The position within that branch and financial year.</param>
    /// <returns>The number.</returns>
    public static EstimateNumber QuoteNumber(string branchCode, long sequence = 1)
        => EstimateNumber.Create(branchCode, Year(), sequence).Value;

    /// <summary>
    /// A priced result carrying all three configuration versions, one currency, and amounts that add up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The figures are an intra-state supply — CGST and SGST at nine per cent each and no IGST — because
    /// <c>ck_orders_price_one_tax_scheme</c> refuses a document claiming both (INV-INV-04). The round-off is
    /// negative, which is the one amount <c>ck_…_price_amounts_not_negative</c> deliberately excludes.
    /// </para>
    /// <para>
    /// <strong>The discount is chosen so that the tax does not land on a whole paisa.</strong> Nine per cent of
    /// <c>10 500.45</c> is <c>945.0405</c>, and <c>conventions.md</c> section 1.2 holds money to four decimal
    /// places precisely so that tax and discount arithmetic composes without premature rounding. A fixture whose
    /// every amount had two decimals would round-trip identically through a <c>numeric(18,2)</c> column, so it
    /// would prove nothing about the <c>HasPrecision(18, 4)</c> every one of these columns is declared with.
    /// </para>
    /// </remarks>
    /// <param name="subtotal">Line amounts before discount, in rupees.</param>
    /// <param name="calculatedAt">When Billing calculated it.</param>
    /// <returns>The snapshot.</returns>
    public static PriceSnapshot Price(decimal subtotal = DefaultSubtotal, DateTimeOffset? calculatedAt = null)
    {
        const decimal discount = 499.55m;
        const decimal roundOff = -0.33m;

        var taxable = subtotal - discount;
        var tax = taxable * 0.09m;

        return PriceSnapshot.Create(
            CatalogVersion,
            PriceListVersion,
            TaxConfigurationVersion,
            Money.Rupees(subtotal),
            Money.Rupees(discount),
            Money.Rupees(taxable),
            Money.Rupees(tax),
            Money.Rupees(tax),
            Money.Zero,
            Money.Zero,
            Money.Rupees(roundOff),
            Money.Rupees(taxable + tax + tax + roundOff),
            calculatedAt ?? Now).Value;
    }

    /// <summary>The subtotal every fixture prices at unless it says otherwise, in rupees.</summary>
    public const decimal DefaultSubtotal = 11_000.00m;

    /// <summary>
    /// What <see cref="Price()"/> asks for at <see cref="DefaultSubtotal"/>, written out to the last paisa.
    /// </summary>
    /// <remarks>
    /// A literal rather than a second call to <see cref="Price()"/>: an assertion built from the same helper the
    /// fixture used would agree with itself after a column that silently truncated the amount, which is the one
    /// thing the round trip is being asked about.
    /// </remarks>
    public const decimal DefaultGrandTotal = 12_390.2010m;

    /// <summary>What <see cref="Price()"/> computes as each of CGST and SGST at <see cref="DefaultSubtotal"/>.</summary>
    public const decimal DefaultTaxEachWay = 945.0405m;

    /// <summary>A measurement copy to freeze onto a garment job (INV-JOB-01).</summary>
    /// <param name="chestMillimetres">The one figure the copy carries. Synthetic and round.</param>
    /// <param name="versionNumber">Which version of the customer's measurements was copied.</param>
    /// <returns>The snapshot.</returns>
    public static MeasurementSnapshot Measurements(decimal chestMillimetres = 860m, int versionNumber = 1)
        => MeasurementSnapshot.Create(
            Guid.Parse("0199d000-0000-7000-8000-0000000000d1"),
            MeasurementTemplate,
            MeasurementTemplateVersion,
            versionNumber,
            takenAt: Now.AddDays(-1),
            takenBy: Actor,
            values:
            [
                MeasuredValue.Create("chest", chestMillimetres, "cm", choice: null, acknowledged: false).Value,
                MeasuredValue.Create("fit", millimetres: null, enteredUnit: null, "loose", acknowledged: true).Value,
            ],
            frozenAt: Now).Value;

    /// <summary>A design copy to freeze onto a garment job (INV-JOB-01).</summary>
    /// <param name="optionCode">The chosen option, so two garments can legitimately differ.</param>
    /// <returns>The snapshot.</returns>
    public static DesignSnapshot Design(string optionCode = "round")
        => DesignSnapshot.Create(
            CatalogVersion,
            CategoryKey,
            "Blouse",
            ServiceTypeKey,
            "Stitch a new garment",
            [
                DesignSelection.Create(
                    "neckline",
                    "Neckline",
                    groupDisplayOrder: 1,
                    optionCode,
                    "Round",
                    optionDisplayOrder: 1,
                    optionVersion: 1,
                    priceListItemCode: "NECK-RND",
                    illustrationMediaId: null,
                    illustrationAlternativeText: null).Value,
            ],
            garmentInstructions: "Synthetic instruction recorded by an integration test.",
            conditionalNotes: ["Lining stitched separately."],
            frozenAt: Now).Value;

    /// <summary>The garment category every fixture confirms.</summary>
    public const string CategoryKey = "blouse";

    /// <summary>The service type every fixture confirms.</summary>
    public const string ServiceTypeKey = "stitch-new";

    /// <summary>
    /// Builds a confirmed order in memory, without writing it.
    /// </summary>
    /// <remarks>
    /// Built through <c>Order.Confirm</c> and never assembled field by field: the factory is what makes
    /// INV-ORD-01 true — every garment job, every frozen copy and the first revision come into being in one
    /// construction — so a fixture that side-stepped it would be storing a shape the module cannot produce.
    /// </remarks>
    /// <param name="branchCode">The branch code the display numbers are composed from.</param>
    /// <param name="garments">How many garments the confirmation carries.</param>
    /// <param name="sequence">The order's position in the branch's sequence.</param>
    /// <param name="estimateId">The accepted estimate, where one was issued.</param>
    /// <param name="orderId">Identity to give the order, where the caller needs a known one.</param>
    /// <param name="bindSecondGarmentToFirst">
    /// Declares garment two as waiting on garment one (<c>finish_before</c>), which is what puts a row in
    /// <c>job_dependencies</c> whose prerequisite is another garment of the same order (INV-JOB-09).
    /// </param>
    /// <param name="subtotal">The order-level subtotal, in rupees.</param>
    /// <returns>The order, not yet written.</returns>
    public static Order Build(
        string branchCode,
        int garments = 1,
        long sequence = 1,
        Guid? estimateId = null,
        Guid? orderId = null,
        bool bindSecondGarmentToFirst = false,
        decimal subtotal = DefaultSubtotal)
    {
        var number = Number(branchCode, sequence);
        var ids = new List<Guid>(garments);

        for (var position = 0; position < garments; position++)
        {
            ids.Add(Guid.CreateVersion7());
        }

        var specifications = new List<GarmentJobSpecification>(garments);

        for (var jobIndex = 1; jobIndex <= garments; jobIndex++)
        {
            var dependencies = bindSecondGarmentToFirst && jobIndex == 2
                ? new[]
                {
                    new GarmentJobDependencySpecification(
                        Guid.CreateVersion7(),
                        ids[0],
                        JobDependencyKind.FinishBefore,
                        "The lining is finished before the blouse is closed."),
                }
                : null;

            var specification = GarmentJobSpecification.Create(
                ids[jobIndex - 1],
                GarmentJobNumber.For(number, jobIndex).Value,
                jobIndex,
                CategoryKey,
                ServiceTypeKey,
                WorkflowDefinition,
                Measurements(),
                Design(jobIndex == 1 ? "round" : "square"),
                Price(subtotal),
                DueDate,
                referenceMediaIds: [Guid.Parse("0199d000-0000-7000-8000-0000000000f1")],
                dependencies);

            specification.IsSuccess.ShouldBeTrue(
                $"the fixture's garment {jobIndex} was refused: {specification.Error.Code}");

            specifications.Add(specification.Value);
        }

        var order = Order.Confirm(
            orderId ?? Guid.CreateVersion7(),
            Organisation,
            Branch,
            Customer,
            number,
            Guid.CreateVersion7(),
            estimateId,
            DueDate,
            "Synthetic order note recorded by an integration test.",
            Price(subtotal),
            specifications,
            Guid.CreateVersion7(),
            Now,
            Actor);

        order.IsSuccess.ShouldBeTrue($"the fixture's confirmation was refused: {order.Error.Code}");

        return order.Value;
    }

    /// <summary>Builds an order and writes it through the real <c>OrderStore</c>.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="branchCode">The branch code the display numbers are composed from.</param>
    /// <param name="garments">How many garments the confirmation carries.</param>
    /// <param name="sequence">The order's position in the branch's sequence.</param>
    /// <param name="estimateId">The accepted estimate, where one was issued.</param>
    /// <param name="bindSecondGarmentToFirst">Declares garment two as waiting on garment one.</param>
    /// <param name="subtotal">The order-level subtotal, in rupees.</param>
    /// <returns>The order as it was sent, so a test can compare what it stored against what it asked for.</returns>
    public static async Task<Order> ConfirmAsync(
        WebApplicationFixture fixture,
        string branchCode,
        int garments = 1,
        long sequence = 1,
        Guid? estimateId = null,
        bool bindSecondGarmentToFirst = false,
        decimal subtotal = DefaultSubtotal)
    {
        var order = Build(
            branchCode, garments, sequence, estimateId, orderId: null, bindSecondGarmentToFirst, subtotal);

        (await StoreAsync(fixture, order)).IsSuccess.ShouldBeTrue();

        return order;
    }

    /// <summary>Writes an already-built order through the real <c>OrderStore</c> and reports what it answered.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="order">The order to write.</param>
    /// <returns>What the store answered, refusals included.</returns>
    public static async Task<Result> StoreAsync(WebApplicationFixture fixture, Order order)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOrderStore>();

        store.Add(order);

        return await store.SaveAsync(Token);
    }

    /// <summary>Issues an estimate through the real <c>EstimateStore</c>.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="branchCode">The branch code the estimate number is composed from.</param>
    /// <param name="sequence">The estimate's position in the branch's sequence.</param>
    /// <param name="estimateId">Identity to give it, where the caller needs a known one.</param>
    /// <returns>The estimate as it was sent.</returns>
    public static async Task<Estimate> IssueAsync(
        WebApplicationFixture fixture,
        string branchCode,
        long sequence = 1,
        Guid? estimateId = null)
    {
        var estimate = BuildEstimate(branchCode, sequence, estimateId);

        (await StoreAsync(fixture, estimate)).IsSuccess.ShouldBeTrue();

        return estimate;
    }

    /// <summary>Builds an issued estimate in memory, without writing it.</summary>
    /// <param name="branchCode">The branch code the estimate number is composed from.</param>
    /// <param name="sequence">The estimate's position in the branch's sequence.</param>
    /// <param name="estimateId">Identity to give it, where the caller needs a known one.</param>
    /// <returns>The estimate, not yet written.</returns>
    public static Estimate BuildEstimate(string branchCode, long sequence = 1, Guid? estimateId = null)
    {
        var estimate = Estimate.Issue(
            estimateId ?? Guid.CreateVersion7(),
            Organisation,
            Branch,
            Guid.CreateVersion7(),
            Customer,
            QuoteNumber(branchCode, sequence),
            Price(),
            IssuedOn,
            IssuedOn.AddDays(14),
            Now,
            Actor);

        estimate.IsSuccess.ShouldBeTrue($"the fixture's estimate was refused: {estimate.Error.Code}");

        return estimate.Value;
    }

    /// <summary>Writes an already-built estimate through the real <c>EstimateStore</c>.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="estimate">The estimate to write.</param>
    /// <returns>What the store answered, refusals included.</returns>
    public static async Task<Result> StoreAsync(WebApplicationFixture fixture, Estimate estimate)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IEstimateStore>();

        store.Add(estimate);

        return await store.SaveAsync(Token);
    }

    /// <summary>
    /// Loads an order through the real <c>OrderStore</c>, applies a command to it and saves.
    /// </summary>
    /// <remarks>
    /// One scope for the whole round trip, because the concurrency token the store writes back with is the one
    /// the read put on the change tracker. A test that wants two readers races two of these.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="orderId">The order to load.</param>
    /// <param name="command">What to do to it. Refusals from the Domain are returned, not thrown.</param>
    /// <returns>What the Domain refused, or what the store answered.</returns>
    public static async Task<Result> MutateAsync(
        WebApplicationFixture fixture,
        Guid orderId,
        Func<Order, Result> command)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(command);

        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOrderStore>();

        var order = (await store.FindAsync(orderId, Organisation, Token))
            .ShouldNotBeNull("the order the test just confirmed was not found");

        var applied = command(order);

        return applied.IsFailure ? applied : await store.SaveAsync(Token);
    }

    /// <summary>Takes one garment job into production, which is what moves it out of <c>Confirmed</c>.</summary>
    /// <remarks>
    /// Through <c>Order.StartProduction</c> and not by writing a status: every garment job transition goes through
    /// the aggregate, and a fixture that set the column would be arranging a state the module cannot reach.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="orderId">The order the garment belongs to.</param>
    /// <param name="garmentJobId">The garment.</param>
    /// <returns>A task that completes when the row is committed.</returns>
    public static async Task StartProductionAsync(
        WebApplicationFixture fixture,
        Guid orderId,
        Guid garmentJobId)
        => (await MutateAsync(
                fixture,
                orderId,
                order => order.StartProduction(
                    garmentJobId,
                    WorkflowVersion,
                    [],
                    ReadyAggregation.EveryDeliverableJob,
                    Now.AddHours(1),
                    Actor)))
            .IsSuccess.ShouldBeTrue();

    /// <summary>
    /// Re-prices every garment of an order and appends a revision, which is the one path that legitimately
    /// rewrites a frozen copy (INV-ORD-05).
    /// </summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="order">The order as it was confirmed, for its garments' identities.</param>
    /// <param name="subtotal">The re-priced subtotal, in rupees.</param>
    /// <param name="chestMillimetres">The re-measured chest, so the measurement copy visibly changes.</param>
    /// <returns>What the Domain refused, or what the store answered.</returns>
    public static Task<Result> ReviseAsync(
        WebApplicationFixture fixture,
        Order order,
        decimal subtotal,
        decimal chestMillimetres)
    {
        ArgumentNullException.ThrowIfNull(order);

        var garmentIds = order.Jobs.Select(job => job.Id).ToList();

        return MutateAsync(
            fixture,
            order.Id,
            loaded => loaded.Revise(
                Guid.CreateVersion7(),
                "The customer asked for a looser fit at the counter.",
                Price(subtotal),
                DueDate.AddDays(3),
                [
                    .. garmentIds.Select(id => GarmentJobRevision.Create(
                        id,
                        Measurements(chestMillimetres, versionNumber: 2),
                        Design("boat"),
                        Price(subtotal),
                        DueDate.AddDays(3)).Value),
                ],
                supersededEstimateId: null,
                Now.AddHours(1),
                Actor));
    }

    /// <summary>Runs a statement that reached a table without going through the Domain, and expects a refusal.</summary>
    /// <remarks>
    /// The statements are constants written by the tests themselves and every value travels as a parameter, so
    /// nothing here composes SQL from anything a caller supplied. It is the only way to test a trigger at all: the
    /// Domain publishes no route to the write each of them refuses, which is the point of having them.
    /// </remarks>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="sql">The statement, with <c>{0}</c>-style placeholders.</param>
    /// <param name="parameters">The values for the placeholders.</param>
    /// <returns>The refusal PostgreSQL raised.</returns>
    public static async Task<PostgresException> RefusedAsync(
        WebApplicationFixture fixture,
        string sql,
        params object[] parameters)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        return await Should.ThrowAsync<PostgresException>(
            async () => await context.Database.ExecuteSqlRawAsync(sql, parameters, Token));
    }

    /// <summary>Runs a statement that reached a table without going through the Domain, and expects it to stand.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="sql">The statement, with <c>{0}</c>-style placeholders.</param>
    /// <param name="parameters">The values for the placeholders.</param>
    /// <returns>How many rows it touched.</returns>
    public static async Task<int> ExecuteAsync(
        WebApplicationFixture fixture,
        string sql,
        params object[] parameters)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        return await context.Database.ExecuteSqlRawAsync(sql, parameters, Token);
    }

    /// <summary>Counts the rows of one <c>orders</c> table that name a given identifier in a given column.</summary>
    /// <param name="fixture">The hosted application.</param>
    /// <param name="table">The unqualified table name. A constant at every call site.</param>
    /// <param name="column">The column to match. A constant at every call site.</param>
    /// <param name="id">The identifier to match.</param>
    /// <returns>The number of rows.</returns>
    public static async Task<int> CountAsync(
        WebApplicationFixture fixture,
        string table,
        string column,
        Guid id)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(Token);

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {OrdersDbContext.SchemaName}.{table} WHERE {column} = @id", connection);

        command.Parameters.AddWithValue("id", id);

        return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
    }
}

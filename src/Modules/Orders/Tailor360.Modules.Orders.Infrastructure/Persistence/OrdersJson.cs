using System.Text.Json;
using System.Text.Json.Serialization;
using Tailor360.Modules.Orders.Domain.Jobs;
using Tailor360.Modules.Orders.Domain.Snapshots;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// How the four document columns of the <c>orders</c> schema are written and read.
/// </summary>
/// <remarks>
/// <para>
/// Four collections are documents rather than rows: a measurement snapshot's values, a design snapshot's
/// selections and its conditional notes, and a garment job's ready-state blocks. Each is only ever read and
/// written whole, which is the test <c>customers.measurement_template_fields.options</c> was chosen against, and
/// the first three have a harder reason besides: <see cref="MeasurementSnapshot"/> and
/// <see cref="DesignSnapshot"/> take their collection as a <strong>constructor parameter</strong> and have no
/// parameterless constructor, so an owned collection mapped to a child table cannot materialise them at all. A
/// converted property is a scalar to Entity Framework and is constructor-bindable, so this is the only mapping
/// that works without changing the Domain.
/// </para>
/// <para>
/// <strong>Every value object here has a private constructor and get-only properties</strong>, which is what
/// keeps its factory the only way in — and which is also why nothing here deserialises the Domain type directly.
/// Each document is a private record of this file, and reading one puts it back through the Domain's own
/// factory, so a row written by the Domain comes back as something the Domain agrees is well formed. It is the
/// <c>FieldKey.Create(value).Value</c> shape <c>CustomersDbContext</c> uses on the same path, for the same
/// reason: a materialisation that quietly rebuilt a value the factory would have refused is a value nothing
/// downstream can trust.
/// </para>
/// <para>
/// <strong>A predicate is written as its name, not its ordinal.</strong> A reason code is what a screen shows
/// beside a job that is not ready, and reordering <see cref="ReadyGatePredicate"/> must not silently re-explain
/// every block already stored — the reasoning <c>customers.duplicate_candidates.reasons</c> gives for the same
/// choice.
/// </para>
/// <para>
/// Documents are camel-cased and written without indentation because the column is data, not something somebody
/// reads. Nothing here is culture-sensitive: <c>System.Text.Json</c> writes a <c>decimal</c> and a
/// <c>DateTimeOffset</c> invariantly.
/// </para>
/// </remarks>
public static class OrdersJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Writes a measurement snapshot's values.</summary>
    /// <param name="values">The values, possibly empty.</param>
    /// <returns>The JSON.</returns>
    public static string Write(IReadOnlyList<MeasuredValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return JsonSerializer.Serialize(values.Select(MeasuredValueDocument.Of).ToList(), Options);
    }

    /// <summary>Reads a measurement snapshot's values back.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The values, empty when there are none.</returns>
    public static IReadOnlyList<MeasuredValue> ReadValues(string? json)
        => Read<MeasuredValueDocument>(json).ConvertAll(document => document.ToValue());

    /// <summary>Writes a design snapshot's selections.</summary>
    /// <param name="selections">The selections, possibly empty.</param>
    /// <returns>The JSON.</returns>
    public static string Write(IReadOnlyList<DesignSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        return JsonSerializer.Serialize(selections.Select(DesignSelectionDocument.Of).ToList(), Options);
    }

    /// <summary>Reads a design snapshot's selections back.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The selections, empty when there are none.</returns>
    public static IReadOnlyList<DesignSelection> ReadSelections(string? json)
        => Read<DesignSelectionDocument>(json).ConvertAll(document => document.ToSelection());

    /// <summary>Writes a design snapshot's conditional notes.</summary>
    /// <param name="notes">The notes, possibly empty.</param>
    /// <returns>The JSON.</returns>
    public static string Write(IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        return JsonSerializer.Serialize(notes, Options);
    }

    /// <summary>Reads a design snapshot's conditional notes back.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The notes, empty when there are none.</returns>
    public static IReadOnlyList<string> ReadNotes(string? json) => Read<string>(json);

    /// <summary>Writes a garment job's ready-state blocks.</summary>
    /// <param name="blocks">The blocks, empty when the job is ready.</param>
    /// <returns>The JSON.</returns>
    public static string Write(IReadOnlyCollection<ReadyGateBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return JsonSerializer.Serialize(blocks.Select(ReadyGateBlockDocument.Of).ToList(), Options);
    }

    /// <summary>Reads a garment job's ready-state blocks back.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The blocks, empty when there are none.</returns>
    /// <remarks>
    /// A concrete <see cref="List{T}"/>, like every other read here. The property is get-only over a readonly
    /// list field, so Entity Framework assigns the converted value straight into that field and anything but the
    /// field's own type would not survive the assignment.
    /// </remarks>
    public static IReadOnlyCollection<ReadyGateBlock> ReadBlocks(string? json)
        => Read<ReadyGateBlockDocument>(json).ConvertAll(document => document.ToBlock());

    private static List<T> Read<T>(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    /// <summary>One measured value, as it sits in the column.</summary>
    private sealed record MeasuredValueDocument(
        string Key,
        decimal? Millimetres,
        string? EnteredUnit,
        string? Choice,
        bool Acknowledged)
    {
        public static MeasuredValueDocument Of(MeasuredValue value)
            => new(value.Key, value.Millimetres, value.EnteredUnit, value.Choice, value.Acknowledged);

        public MeasuredValue ToValue()
            => MeasuredValue.Create(Key, Millimetres, EnteredUnit, Choice, Acknowledged).Value;
    }

    /// <summary>One design selection, as it sits in the column.</summary>
    private sealed record DesignSelectionDocument(
        string GroupCode,
        string GroupLabel,
        int GroupDisplayOrder,
        string OptionCode,
        string OptionLabel,
        int OptionDisplayOrder,
        int OptionVersion,
        string? PriceListItemCode,
        Guid? IllustrationMediaId,
        string? IllustrationAlternativeText)
    {
        public static DesignSelectionDocument Of(DesignSelection selection)
            => new(
                selection.GroupCode,
                selection.GroupLabel,
                selection.GroupDisplayOrder,
                selection.OptionCode,
                selection.OptionLabel,
                selection.OptionDisplayOrder,
                selection.OptionVersion,
                selection.PriceListItemCode,
                selection.IllustrationMediaId,
                selection.IllustrationAlternativeText);

        public DesignSelection ToSelection()
            => DesignSelection.Create(
                GroupCode,
                GroupLabel,
                GroupDisplayOrder,
                OptionCode,
                OptionLabel,
                OptionDisplayOrder,
                OptionVersion,
                PriceListItemCode,
                IllustrationMediaId,
                IllustrationAlternativeText).Value;
    }

    /// <summary>One ready-gate block, as it sits in the column.</summary>
    private sealed record ReadyGateBlockDocument(ReadyGatePredicate Predicate, string? Reference)
    {
        public static ReadyGateBlockDocument Of(ReadyGateBlock block) => new(block.Predicate, block.Reference);

        public ReadyGateBlock ToBlock() => ReadyGateBlock.Create(Predicate, Reference).Value;
    }
}

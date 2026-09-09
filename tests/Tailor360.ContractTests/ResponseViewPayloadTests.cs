using System.Reflection;
using System.Text.Json;
using Shouldly;
using Tailor360.Modules.Customers.Api.Payloads;
using Tailor360.Modules.Customers.Domain;
using Tailor360.Platform.Security.FieldVisibility;
using Tailor360.Web.Timeline;

namespace Tailor360.ContractTests;

/// <summary>
/// Holds a payload type equal to the response view it is approved as.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/security/field-visibility.md</c> section 2 asks for three guarantees, and the second is
/// that <strong>a field nobody declared cannot reach a response</strong>. Where a handler builds its
/// body through <c>MaskedPayload.Set</c> the platform gives that guarantee at run time, by throwing on
/// an undeclared name. A payload that publishes a schema cannot be built that way — the client is
/// generated from the schema, so the response has to be a typed shape — and this is how the same
/// guarantee is obtained for those: the type's properties and the view's declared fields are held
/// equal here, so a property added without an approved row fails the build.
/// </para>
/// <para>
/// It is the stronger of the two, and deliberately so. The run-time check fires on the first request
/// that carries the new field, which is to say in production; this one fires on the pull request that
/// adds it, which is where a field nobody approved is still cheap to argue about.
/// </para>
/// <para>
/// Order is compared as well as membership. The view's declaration order is the order
/// <c>MaskedPayload.ToPayload</c> emits and the order the approved tables are read in, and a response
/// whose fields arrive in a different order from the document they were approved in is a document
/// nobody can check by eye.
/// </para>
/// </remarks>
[Trait("Category", "Contract")]
public sealed class ResponseViewPayloadTests
{
    private static readonly ResponseViewCatalogue Catalogue = new([new ApplicationResponseViews()]);

    public static TheoryData<string, Type> ServedViews => new()
    {
        { CustomersResponseViews.Record, typeof(CustomerPayload) },
        { CustomersResponseViews.SearchCard, typeof(CustomerCardPayload) },
        { CustomersResponseViews.Timeline, typeof(CustomerTimelineEntryPayload) },
    };

    [Theory]
    [MemberData(nameof(ServedViews))]
    public void ThePayloadCarriesExactlyTheFieldsTheViewDeclares(string viewKey, Type payload)
    {
        var declared = Catalogue.Require(viewKey).Fields.Select(field => field.Name).ToArray();

        WireNames(payload).ShouldBe(
            declared,
            $"{payload.Name} and the approved view '{viewKey}' disagree. Add the field to the view, to "
            + "the fields table of docs/security/field-visibility.md and to the role table there, in "
            + "one change — a field that reaches a response without being declared has been approved "
            + "by nobody.");
    }

    [Theory]
    [MemberData(nameof(ServedViews))]
    public void ThePayloadCarriesNothingOfAClassItsViewWithholds(string viewKey, Type payload)
    {
        // Belt and braces on top of the ResponseView constructor: that refuses a declaration, this
        // refuses a type that has drifted from one.
        var view = Catalogue.Require(viewKey);

        view.Withheld.ShouldNotBe(FieldClassification.None, $"{viewKey} withholds nothing at all");

        foreach (var name in WireNames(payload))
        {
            var field = view.Field(name);

            field.ShouldNotBeNull($"{payload.Name}.{name} is not declared by {viewKey}");
            ((field.Classification & view.Withheld) == 0).ShouldBeTrue(
                $"{viewKey}.{name} is {field.Classification}, which the view withholds");
        }
    }

    [Theory]
    [MemberData(nameof(ServedViews))]
    public void EveryGatedFieldIsOneThePayloadCanActuallyWithhold(string viewKey, Type payload)
    {
        // The projection withholds a field by sending null. A gated field whose property is a
        // non-nullable string, Guid or bool has no way to say "withheld", so declaring one would
        // produce a view whose approved answer the code cannot give — and the failure mode is the
        // dangerous direction: the value goes out anyway.
        var parameters = Primary(payload).GetParameters().ToDictionary(
            parameter => JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!),
            StringComparer.Ordinal);

        var context = new NullabilityInfoContext();

        var ungatable = Catalogue.Require(viewKey).Fields
            .Where(field => field.RequiredPermission is not null)
            .Where(field => !CanBeWithheld(parameters[field.Name], context))
            .Select(field => $"{viewKey}.{field.Name} is gated on '{field.RequiredPermission}'")
            .ToArray();

        ungatable.ShouldBeEmpty(
            "a gated field must be nullable on the payload, or there is no value the projection can "
            + "send when it is withheld:\n" + string.Join('\n', ungatable));
    }

    [Fact]
    public void TheHostAndTheModuleAnswerOneCodeForACustomerThatIsNotThere()
    {
        // The host cannot reference the module's Domain (ARCH-006), so the code it answers a missing
        // customer with is retyped. Two codes for one condition would be worse than the retyping, and
        // this is what stops it happening quietly.
        CustomerTimelinePayload.CustomerNotFound.ShouldBe(
            CustomersErrors.CustomerNotFound.Code,
            "the timeline endpoint and the customer endpoints must answer one code for one condition");
    }

    [Fact]
    public void TheSearchCardGatesNothing()
    {
        // The card is masked by construction — its number has had everything but its last four digits
        // replaced before it reaches this layer — so no field of it carries a permission and
        // CustomerCardPayload.From needs no mask. That is a property of the approved view rather than
        // an omission in the projection, and this is what makes it one: gate a card field and this
        // fails, which is the prompt to teach the projection to withhold it.
        Catalogue.Require(CustomersResponseViews.SearchCard).Fields
            .Where(field => field.RequiredPermission is not null)
            .ShouldBeEmpty(
                "a search card field was given a permission, but CustomerCardPayload.From projects "
                + "every field unconditionally. Thread the mask through it in the same change.");
    }

    private static bool CanBeWithheld(ParameterInfo parameter, NullabilityInfoContext context)
        => Nullable.GetUnderlyingType(parameter.ParameterType) is not null
           || context.Create(parameter).WriteState is NullabilityState.Nullable;

    private static ConstructorInfo Primary(Type payload)
        => payload.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First();

    /// <summary>
    /// The payload's property names as they appear on the wire, in the order they are serialised.
    /// </summary>
    /// <remarks>
    /// Read from the primary constructor rather than from <c>GetProperties</c>, which does not promise
    /// declaration order. For a positional record the two are the same set, and the constructor is the
    /// one of them that is ordered.
    /// </remarks>
    private static string[] WireNames(Type payload)
    {
        return
        [
            .. Primary(payload).GetParameters()
                .Select(parameter => JsonNamingPolicy.CamelCase.ConvertName(
                    parameter.Name ?? throw new InvalidOperationException(
                        $"{payload.Name} has an unnamed constructor parameter."))),
        ];
    }
}

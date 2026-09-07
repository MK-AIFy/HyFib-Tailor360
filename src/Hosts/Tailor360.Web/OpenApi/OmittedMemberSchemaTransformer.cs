using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Tailor360.Web.OpenApi;

/// <summary>
/// Marks a member the serialiser omits as optional in the document.
/// </summary>
/// <remarks>
/// <para>
/// The generator derives <c>required</c> from the C# type: a non-nullable member is required, and a
/// nullable one is required-and-nullable. That is right for almost everything and wrong for a member
/// carrying <c>[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]</c>, which is not sent at
/// all when it is null. Without this the document would promise a member that a real response does not
/// contain — and a generated client would type it as present, so every consumer would read `undefined`
/// through a type that says it cannot be.
/// </para>
/// <para>
/// It is a general rule rather than a fix for the one member that needs it today
/// (<c>VersionResponse.commit</c>, withheld outside Development), because the next member to be omitted
/// this way would otherwise reintroduce the same lie silently.
/// </para>
/// </remarks>
public sealed class OmittedMemberSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (schema.Required is not { Count: > 0 }
            || context.JsonTypeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return Task.CompletedTask;
        }

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (IsOmittedWhenNull(property))
            {
                schema.Required.Remove(property.Name);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether the serialiser leaves this member out of the payload when it is null.</summary>
    private static bool IsOmittedWhenNull(JsonPropertyInfo property)
        => property.AttributeProvider?
            .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
            .OfType<JsonIgnoreAttribute>()
            .Any(attribute => attribute.Condition == JsonIgnoreCondition.WhenWritingNull) == true;
}

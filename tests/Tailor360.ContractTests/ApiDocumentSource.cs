using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Tailor360.Web.OpenApi;

namespace Tailor360.ContractTests;

/// <summary>
/// Produces the version 1 API document from the composed application, and writes it in the one
/// canonical form the committed copy is compared against.
/// </summary>
/// <remarks>
/// <para>
/// The document is generated in process rather than fetched over HTTP. It is the same generator the
/// Development-only <c>/openapi/v1.json</c> route uses — <see cref="IOpenApiDocumentProvider"/>, keyed
/// by document name — so nothing here can describe a surface the served document would not.
/// </para>
/// <para>
/// <b>Canonical form matters more than it looks.</b> The committed document is diffed on every pull
/// request, and a diff that reorders keys on an unrelated change is a diff nobody reads. Object members
/// are therefore sorted, arrays are left in the order the generator produced them because their order is
/// meaningful, the indentation is two spaces and the file ends in exactly one newline.
/// </para>
/// </remarks>
public static class ApiDocumentSource
{
    /// <summary>Generates the document from the composed host.</summary>
    /// <param name="services">The composed application's services.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The generated document.</returns>
    public static async Task<OpenApiDocument> GenerateAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(ApiDocument.Name);

        return await provider.GetOpenApiDocumentAsync(cancellationToken);
    }

    /// <summary>Serialises a document to the canonical JSON the repository commits.</summary>
    /// <param name="document">The generated document.</param>
    /// <returns>Canonical JSON, ending in a single newline.</returns>
    public static async Task<string> SerialiseAsync(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var generated = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1);
        return Canonicalise(generated);
    }

    /// <summary>Rewrites JSON in canonical form: members sorted, two-space indent, one trailing newline.</summary>
    /// <param name="json">Any JSON text.</param>
    /// <returns>The canonical rendering.</returns>
    public static string Canonicalise(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            // The document is machine-produced and reviewed as text; escaping every non-ASCII character
            // would turn a description containing an en dash into unreadable escapes in the diff.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            Write(writer, parsed.RootElement);
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray()).ReplaceLineEndings("\n");
        return text.TrimEnd('\n') + "\n";
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}

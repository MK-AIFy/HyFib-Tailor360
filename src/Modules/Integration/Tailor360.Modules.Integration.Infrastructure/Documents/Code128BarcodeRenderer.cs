using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;
using ZXing.OneD;
using BarcodeFormat = ZXing.BarcodeFormat;
using EncodeHintType = ZXing.EncodeHintType;

namespace Tailor360.Modules.Integration.Infrastructure.Documents;

/// <summary>
/// Renders a payload as a Code 128 barcode (ADR-0014): ZXing.Net computes the bar pattern — the core
/// package, with no image library behind it — and the bars are drawn as vector rectangles, into an SVG for
/// a document and rasterised by QuestPDF for a PNG. The same pattern therefore appears on a label and on
/// an invoice, and a scanner reads both alike.
/// </summary>
public sealed class Code128BarcodeRenderer : IBarcodeRenderer
{
    /// <summary>The one symbology the port renders today; a QR asks for something this adapter does not draw.</summary>
    public const string Code128 = "code128";

    /// <inheritdoc />
    public Task<Result<byte[]>> RenderPngAsync(string payload, string symbology, int widthPixels, int heightPixels, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(symbology, Code128, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result.Failure<byte[]>(Error.Validation(
                "integration.symbology-not-supported", "Only Code 128 is rendered.", "symbology")));
        }

        if (string.IsNullOrWhiteSpace(payload) || widthPixels < 1 || heightPixels < 1)
        {
            return Task.FromResult(Result.Failure<byte[]>(Error.Validation(
                "integration.barcode-request-not-well-formed", "A payload and a positive size are required.", "payload")));
        }

        DocumentFonts.EnsureRegistered();
        var svg = Svg(payload, widthPixels, heightPixels);
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(widthPixels, heightPixels, Unit.Point);
            page.Margin(0);
            page.Content().Svg(svg);
        }));

        var png = document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 72 }).First();
        return Task.FromResult(Result.Success(png));
    }

    /// <summary>The bar pattern as an SVG of the size asked, black on transparent, quiet zones included.</summary>
    /// <param name="payload">The text to encode.</param>
    /// <param name="width">The drawing width.</param>
    /// <param name="height">The drawing height.</param>
    public static string Svg(string payload, double width, double height)
    {
        var modules = Modules(payload);
        var moduleWidth = width / modules.Length;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:0.###}\" height=\"{height:0.###}\" viewBox=\"0 0 {width:0.###} {height:0.###}\">");
        var index = 0;
        while (index < modules.Length)
        {
            if (!modules[index])
            {
                index++;
                continue;
            }

            var start = index;
            while (index < modules.Length && modules[index])
            {
                index++;
            }

            builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{start * moduleWidth:0.###}\" y=\"0\" width=\"{(index - start) * moduleWidth:0.###}\" height=\"{height:0.###}\" fill=\"#000\"/>");
        }

        builder.Append("</svg>");
        return builder.ToString();
    }

    /// <summary>The quiet zone, in modules, on each side: the ten the symbology asks for.</summary>
    public const int QuietZoneModules = 10;

    /// <summary>The modules of the symbol, the quiet zone on each side included, true where a bar is.</summary>
    /// <param name="payload">The text to encode.</param>
    public static bool[] Modules(string payload)
    {
        // ZXing's margin hint is the total, split across both sides.
        var matrix = new Code128Writer().encode(payload, BarcodeFormat.CODE_128, 0, 1, new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = QuietZoneModules * 2 });
        var modules = new bool[matrix.Width];
        for (var x = 0; x < matrix.Width; x++)
        {
            modules[x] = matrix[x, 0];
        }

        return modules;
    }
}

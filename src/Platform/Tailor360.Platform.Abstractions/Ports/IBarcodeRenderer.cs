namespace Tailor360.Platform.Abstractions.Ports;

/// <summary>
/// Renders a barcode payload into an image for a label. The payload format itself is owned by the
/// Custody module (#35); this port only draws what it is given.
/// </summary>
public interface IBarcodeRenderer
{
    /// <summary>Renders <paramref name="payload"/> as a PNG image at the requested size.</summary>
    /// <param name="payload">The opaque barcode payload to encode.</param>
    /// <param name="symbology">The symbology to use, for example <c>Code128</c> or <c>QrCode</c>.</param>
    /// <param name="widthPixels">Target width in pixels.</param>
    /// <param name="heightPixels">Target height in pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Results.Result<byte[]>> RenderPngAsync(
        string payload,
        string symbology,
        int widthPixels,
        int heightPixels,
        CancellationToken cancellationToken = default);
}

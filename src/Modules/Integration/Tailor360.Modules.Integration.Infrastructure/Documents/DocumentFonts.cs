using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace Tailor360.Modules.Integration.Infrastructure.Documents;

/// <summary>
/// The fonts every document embeds: Noto Sans for Latin and Noto Sans Tamil for the customer's name in
/// their own script, both under the SIL Open Font License (<c>Fonts/OFL.txt</c>), carried as embedded
/// resources so a rendering never depends on what the host has installed. Registered once per process,
/// together with the QuestPDF licence the record checks (ADR-0014).
/// </summary>
public static class DocumentFonts
{
    /// <summary>The family name the templates ask for; Tamil glyphs fall back to the Tamil face by family.</summary>
    public const string Family = "Noto Sans";

    /// <summary>The Tamil family.</summary>
    public const string TamilFamily = "Noto Sans Tamil";

    private static readonly Lazy<bool> Registered = new(Register);

    /// <summary>Registers the licence and the fonts; safe to call any number of times.</summary>
    public static void EnsureRegistered() => _ = Registered.Value;

    private static bool Register()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = false;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

        var assembly = typeof(DocumentFonts).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFont(stream);
        }

        return true;
    }
}

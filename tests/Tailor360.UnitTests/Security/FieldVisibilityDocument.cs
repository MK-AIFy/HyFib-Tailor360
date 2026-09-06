namespace Tailor360.UnitTests.Security;

/// <summary>
/// Reads the approved field-visibility document as data.
/// </summary>
/// <remarks>
/// <para>
/// It is a second fixture document beside the permission matrix rather than a section inside it, and it
/// is parsed by the matrix's own parser. Two documents, one grammar: the marker blocks, the
/// address-by-heading rule and the refusal to parse a malformed table are
/// <see cref="PermissionMatrixDocument"/>'s, and this type adds only the path.
/// </para>
/// <para>
/// The split is on subject, not on convenience. The matrix answers "who may do what", which the owner
/// approves once and an administrator then edits per installation; this answers "what does a response
/// carry", which is a property of the software and changes only in a release. Keeping them apart lets
/// the second be reviewed by whoever reviews an API change without reopening the first.
/// </para>
/// </remarks>
public static class FieldVisibilityDocument
{
    /// <summary>The path the document is read from, relative to the repository root.</summary>
    public const string RelativePath = "docs/security/field-visibility.md";

    /// <summary>The absolute path of the document in this working tree.</summary>
    public static string FullPath
        => Path.Combine(
            Path.GetDirectoryName(PermissionMatrixDocument.FullPath)
            ?? throw new InvalidOperationException("The permission matrix has no containing directory."),
            "field-visibility.md");

    /// <summary>Reads the document that ships in this repository.</summary>
    public static PermissionMatrixDocument Load() => PermissionMatrixDocument.Parse(File.ReadAllText(FullPath));
}

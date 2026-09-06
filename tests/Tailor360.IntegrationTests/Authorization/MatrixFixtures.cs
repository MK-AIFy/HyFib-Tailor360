using System.Globalization;
using System.Net;
using YamlDotNet.RepresentationModel;

namespace Tailor360.IntegrationTests.Authorization;

/// <summary>
/// The behavioural expectations of the authorisation matrix, read from
/// <c>tests/Tailor360.IntegrationTests/Authorization/matrix.yaml</c>.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are a file rather than constants in a test because they are the thing every later pull
/// request extends: adding an endpoint means adding its expectations here, and a reviewer looking for
/// what a new route is allowed to answer should find one file rather than a search across a suite.
/// </para>
/// <para>
/// Every lookup below throws rather than returning a default. A fixture file that quietly answered
/// "no expectation" for a dimension nobody had written down would turn a missing expectation into a
/// passing test, which is the failure this whole design is built to avoid.
/// </para>
/// </remarks>
public sealed class MatrixFixtures
{
    private readonly YamlMappingNode _root;

    private MatrixFixtures(YamlMappingNode root) => _root = root;

    /// <summary>Where the fixtures live, relative to the repository root.</summary>
    public const string RelativePath = "tests/Tailor360.IntegrationTests/Authorization/matrix.yaml";

    /// <summary>Reads the fixtures that ship in this repository.</summary>
    public static MatrixFixtures Load() => Parse(File.ReadAllText(FullPath));

    /// <summary>The absolute path of the fixtures in this working tree.</summary>
    public static string FullPath => Path.Combine(RepositoryRoot(), RelativePath);

    /// <summary>Reads fixtures from YAML, for the reader's own tests.</summary>
    /// <exception cref="InvalidOperationException">The document is not one mapping.</exception>
    public static MatrixFixtures Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);

        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException(
                "The authorisation matrix fixtures must be exactly one YAML document whose root is a "
                + "mapping.");
        }

        return new MatrixFixtures(root);
    }

    /// <summary>The answer a named dimension must produce.</summary>
    /// <param name="dimension">The dimension key, as written in the fixtures.</param>
    /// <exception cref="InvalidOperationException">No such dimension, or it names no known answer.</exception>
    public ExpectedAnswer Expected(string dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);
        return Answer(Scalar(Map("dimensions"), dimension));
    }

    /// <summary>Every dimension the fixtures describe, in file order.</summary>
    public IReadOnlyList<string> Dimensions =>
        [.. Map("dimensions").Children.Keys.Select(key => ((YamlScalarNode)key).Value ?? string.Empty)];

    /// <summary>The answer a named outcome is written on the wire as.</summary>
    /// <param name="name">The answer key, such as <c>out-of-reach</c>.</param>
    /// <exception cref="InvalidOperationException">No such answer.</exception>
    public ExpectedAnswer Answer(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var answers = Map("answers");
        if (!answers.Children.TryGetValue(new YamlScalarNode(name), out var node)
            || node is not YamlMappingNode answer)
        {
            throw new InvalidOperationException(
                $"The fixtures describe no answer called '{name}'. They describe: "
                + string.Join(", ", answers.Children.Keys.Select(key => ((YamlScalarNode)key).Value))
                + ".");
        }

        var status = Scalar(answer, "status");
        var code = OptionalScalar(answer, "code");

        return new ExpectedAnswer(
            (HttpStatusCode)int.Parse(status, CultureInfo.InvariantCulture),
            code);
    }

    /// <summary>
    /// The recorded disagreements between what the matrix approves and what the pipeline enforces.
    /// </summary>
    public IReadOnlyList<OrganisationReachGap> OrganisationReachGaps =>
    [
        .. Sequence("organisation-reach-gaps").Children.OfType<YamlMappingNode>().Select(entry =>
            new OrganisationReachGap(
                Scalar(entry, "role"),
                Scalar(entry, "permission"),
                Answer(Scalar(entry, "answer-own-branch")),
                Answer(Scalar(entry, "answer-other-branch")),
                Scalar(entry, "because"),
                Scalar(entry, "settled-by"))),
    ];

    /// <summary>The routes whose identifiers a caller could type rather than follow.</summary>
    public IReadOnlyList<IdentifierEditingCase> IdentifierEditing =>
    [
        .. Sequence("identifier-editing").Children.OfType<YamlMappingNode>().Select(entry =>
            new IdentifierEditingCase(
                Scalar(entry, "route"),
                Scalar(entry, "identifier"),
                Scalar(entry, "because"))),
    ];

    /// <summary>The audit action a refused attempt is recorded under.</summary>
    public string DenialAuditAction => Scalar(Map("denial-audit"), "action");

    /// <summary>
    /// Where the field set each role is shown is declared, and what holds it true. Repository-relative.
    /// </summary>
    /// <remarks>
    /// Field sets are deliberately not restated in these fixtures — reach and detail are two questions
    /// with two sources. What is kept here is the pointer, and the pointer is checked to resolve so it
    /// cannot decay into a reference to a document somebody moved.
    /// </remarks>
    public IReadOnlyList<string> FieldSetReferences =>
    [
        Scalar(Map("field-sets"), "declared-in"),
        Scalar(Map("field-sets"), "enforced-by"),
    ];

    /// <summary>Resolves a repository-relative path the fixtures name.</summary>
    /// <param name="relativePath">The path, as written in the fixtures.</param>
    public static string Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Path.Combine(RepositoryRoot(), relativePath);
    }

    private YamlMappingNode Map(string key)
        => _root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode mapping
            ? mapping
            : throw new InvalidOperationException(
                $"The authorisation matrix fixtures have no '{key}' mapping.");

    private YamlSequenceNode Sequence(string key)
        => _root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode sequence
            ? sequence
            : throw new InvalidOperationException(
                $"The authorisation matrix fixtures have no '{key}' list.");

    private static string Scalar(YamlMappingNode mapping, string key)
        => OptionalScalar(mapping, key)
           ?? throw new InvalidOperationException(
               $"'{key}' is missing from this entry of the authorisation matrix fixtures, or is empty. "
               + "An expectation nobody wrote down is not an expectation that passes.");

    private static string? OptionalScalar(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)
            || node is not YamlScalarNode scalar
            || scalar.Value is null
            || scalar.Value.Length == 0
            || string.Equals(scalar.Value, "null", StringComparison.Ordinal))
        {
            return null;
        }

        return scalar.Value.Trim();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HyFib.Tailor360.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root: no HyFib.Tailor360.slnx above " + AppContext.BaseDirectory);
    }
}

/// <summary>What a request must be answered with.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Code">The problem document's stable code, or null when the request succeeds.</param>
public sealed record ExpectedAnswer(HttpStatusCode Status, string? Code)
{
    /// <summary>The outcome a probe request produced, for comparison.</summary>
    public ProbeResult AsResult() => new(Status, Code);
}

/// <summary>
/// A role approved to hold an organisation-scoped permission that cannot currently exercise it.
/// </summary>
/// <param name="Role">The role key.</param>
/// <param name="Permission">The permission it is approved to hold.</param>
/// <param name="OwnBranch">What it is answered with today in the caller's own branch.</param>
/// <param name="OtherBranch">What it is answered with when the request names another branch.</param>
/// <param name="Because">Why the disagreement exists.</param>
/// <param name="SettledBy">The issue that resolves it.</param>
public sealed record OrganisationReachGap(
    string Role,
    string Permission,
    ExpectedAnswer OwnBranch,
    ExpectedAnswer OtherBranch,
    string Because,
    string SettledBy);

/// <summary>A route whose identifier a caller could type rather than follow.</summary>
/// <param name="Route">The route, as the matrix names it.</param>
/// <param name="Identifier">What a foreign identifier would be.</param>
/// <param name="Because">Why the two answers have to be indistinguishable.</param>
public sealed record IdentifierEditingCase(string Route, string Identifier, string Because);

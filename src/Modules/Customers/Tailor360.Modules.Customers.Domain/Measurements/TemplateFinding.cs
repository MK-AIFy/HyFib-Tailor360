namespace Tailor360.Modules.Customers.Domain.Measurements;

/// <summary>How much a publish-time finding matters.</summary>
public enum TemplateFindingSeverity
{
    /// <summary>Worth saying, and publication proceeds.</summary>
    Warning = 0,

    /// <summary>Publication is refused until it is corrected.</summary>
    Error = 1,
}

/// <summary>
/// One thing publish validation noticed about a template version.
/// </summary>
/// <remarks>
/// <para>
/// A finding names the thing it is about — <c>fields[sleeve_length].rule</c> — rather than describing it, so the
/// administration screen can put the message beside the control that caused it instead of at the top of the page.
/// That is the same shape the API's field-level problem details use, and it is why validation returns a list of
/// these rather than failing on the first thing wrong: an administrator fixing a template wants every problem at
/// once, not one per round trip.
/// </para>
/// </remarks>
/// <param name="Severity">Whether this refuses publication.</param>
/// <param name="Code">The stable code the screen branches on.</param>
/// <param name="Message">What is wrong, in words an administrator can act on.</param>
/// <param name="Target">The field or rule it is about.</param>
public sealed record TemplateFinding(
    TemplateFindingSeverity Severity,
    string Code,
    string Message,
    string Target);

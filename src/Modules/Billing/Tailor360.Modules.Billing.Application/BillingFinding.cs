namespace Tailor360.Modules.Billing.Application;

/// <summary>How much a finding matters.</summary>
public enum BillingFindingSeverity
{
    /// <summary>Worth knowing; does not refuse the publication.</summary>
    Warning = 0,

    /// <summary>Refuses the publication.</summary>
    Error = 1,
}

/// <summary>One thing a publication check found.</summary>
/// <param name="Severity">Whether it refuses the publication.</param>
/// <param name="Code">A stable code the client keys on.</param>
/// <param name="Message">What is wrong and what to do.</param>
/// <param name="Target">The request field it concerns, in the payload's shape, or null.</param>
public sealed record BillingFinding(
    BillingFindingSeverity Severity,
    string Code,
    string Message,
    string? Target)
{
    /// <summary>An error.</summary>
    public static BillingFinding Error(string code, string message, string? target = null)
        => new(BillingFindingSeverity.Error, code, message, target);

    /// <summary>A warning.</summary>
    public static BillingFinding Warning(string code, string message, string? target = null)
        => new(BillingFindingSeverity.Warning, code, message, target);
}

/// <summary>What the publication checks found about one version.</summary>
/// <param name="VersionId">The version.</param>
/// <param name="Findings">Everything found, errors first.</param>
public sealed record BillingValidationReport(Guid VersionId, IReadOnlyList<BillingFinding> Findings)
{
    /// <summary>Whether anything found refuses the publication.</summary>
    public bool HasErrors => Findings.Any(finding => finding.Severity == BillingFindingSeverity.Error);
}

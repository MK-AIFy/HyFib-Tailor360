namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Records why an endpoint is reachable without a session. Architecture rule ARCH-007 requires every
/// endpoint to declare either an authorisation policy or an anonymous justification, so an endpoint
/// cannot become public by omission.
/// </summary>
/// <param name="Justification">Why this endpoint is safe to expose without authentication.</param>
/// <param name="ReviewedIn">The issue or threat model in which the exposure was reviewed.</param>
public sealed record AnonymousJustificationMetadata(string Justification, string ReviewedIn);

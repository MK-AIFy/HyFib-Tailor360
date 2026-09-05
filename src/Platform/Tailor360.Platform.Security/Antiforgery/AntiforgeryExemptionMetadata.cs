namespace Tailor360.Platform.Security.Antiforgery;

/// <summary>
/// Records why a state-changing endpoint is exempt from anti-forgery validation. Only one kind of
/// endpoint qualifies: one that is never called by a browser carrying the session cookie — a provider
/// callback, an inbound webhook, a machine-to-machine call authenticated some other way.
/// </summary>
/// <remarks>
/// An exemption is a hole, so it is written down where a reviewer can find every one of them at once
/// rather than being a missing call nobody notices. An endpoint that a browser <em>does</em> reach must
/// never carry this, however inconvenient the token is to obtain — that is precisely the endpoint a
/// cross-site request would target.
/// </remarks>
/// <param name="Justification">Why no browser reaches this endpoint with the session cookie.</param>
/// <param name="ReviewedIn">The issue or threat model in which the exemption was reviewed.</param>
public sealed record AntiforgeryExemptionMetadata(string Justification, string ReviewedIn);

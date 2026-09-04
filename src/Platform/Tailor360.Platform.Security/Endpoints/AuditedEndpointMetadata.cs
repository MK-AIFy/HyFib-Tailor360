namespace Tailor360.Platform.Security.Endpoints;

/// <summary>
/// Marks an endpoint as one whose invocation is written to the audit trail. Architecture rule ARCH-008
/// requires every state-changing endpoint to carry this, so an action cannot become unauditable by
/// someone forgetting to add it; the filter that does the writing arrives with issue #21.
/// </summary>
/// <param name="Action">The stable audit action name, for example <c>orders.order.confirm</c>.</param>
/// <param name="ReasonRequired">
/// True when the caller must supply a reason. Set for actions a reviewer would later ask "why" about:
/// overrides, cancellations, refunds, reprints and access changes.
/// </param>
public sealed record AuditedEndpointMetadata(string Action, bool ReasonRequired = false);

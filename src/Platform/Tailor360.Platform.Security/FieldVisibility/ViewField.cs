namespace Tailor360.Platform.Security.FieldVisibility;

/// <summary>One field a response view may carry, and what a caller must hold to be shown it.</summary>
/// <param name="Name">
/// The field's name in the response body, in the camel case the API uses. It is part of the contract:
/// renaming it is an API change and the approved field sets are stated in terms of it.
/// </param>
/// <param name="Classification">What kind of thing the field is.</param>
/// <param name="RequiredPermission">
/// The permission a caller must hold to be shown the field, or null when the view's own permission is
/// the whole gate. Null is a decision and not a default: a job card shows the customer's name to
/// everyone who may open the job card, which is what makes it safe to hand a job card to the workshop
/// at all, and it does so without granting anybody the customer record.
/// </param>
/// <param name="Rationale">Why the field is where it is, in one sentence, traced to a document.</param>
public sealed record ViewField(
    string Name,
    FieldClassification Classification,
    string? RequiredPermission,
    string Rationale);

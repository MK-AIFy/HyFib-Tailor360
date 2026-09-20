namespace Tailor360.Modules.Media.Domain.Media;

/// <summary>
/// What an image was captured or supplied for. Fixes which object-storage prefix it lives in once
/// promoted (<c>docs/architecture/module-ownership.md</c> §5.4) and, together with
/// <see cref="MediaClassification"/>, how strictly it is handled.
/// </summary>
public enum MediaPurpose
{
    /// <summary>The customer's own cloth, photographed for the job. Prefix <c>material/</c>.</summary>
    Material = 0,

    /// <summary>A reference the customer brought — a photograph, a magazine picture, an old garment. Prefix <c>reference/</c>.</summary>
    Reference = 1,

    /// <summary>A bundled measurement-diagram sheet. Prefix <c>diagram/</c>.</summary>
    Diagram = 2,

    /// <summary>A bundled design-option illustration sheet. Prefix <c>diagram/</c> (shares the measurement diagrams' prefix; the two sets are disambiguated by key, never by prefix — <c>docs/prd/design-options.md</c> §6).</summary>
    Illustration = 3,

    /// <summary>A QC defect photograph or rework evidence. Prefix <c>qc-evidence/</c>.</summary>
    QcEvidence = 4,

    /// <summary>Doorstep delivery confirmation evidence. Prefix <c>delivery-evidence/</c>.</summary>
    DeliveryEvidence = 5,
}

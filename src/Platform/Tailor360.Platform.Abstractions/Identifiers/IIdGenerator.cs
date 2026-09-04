namespace Tailor360.Platform.Abstractions.Identifiers;

/// <summary>
/// The only sanctioned source of entity identifiers. Architecture rule ARCH-015 forbids
/// <c>Guid.NewGuid()</c> for entity identity outside this abstraction so that identifiers stay
/// time-ordered (UUIDv7) and tests stay deterministic.
/// </summary>
public interface IIdGenerator
{
    /// <summary>
    /// A new time-ordered UUIDv7. Time ordering keeps B-tree indexes dense, which matters because
    /// every aggregate in the system is keyed by one of these.
    /// </summary>
    Guid NewId();
}

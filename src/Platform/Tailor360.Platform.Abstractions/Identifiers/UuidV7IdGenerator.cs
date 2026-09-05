namespace Tailor360.Platform.Abstractions.Identifiers;

/// <summary>The production <see cref="IIdGenerator"/>, producing UUIDv7 values (D10).</summary>
public sealed class UuidV7IdGenerator : IIdGenerator
{
    /// <inheritdoc />
    public Guid NewId() => Guid.CreateVersion7();
}

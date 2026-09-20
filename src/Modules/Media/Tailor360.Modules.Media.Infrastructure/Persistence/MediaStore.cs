using Tailor360.Modules.Media.Application.Abstractions;
using Tailor360.Modules.Media.Domain.Media;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Media.Infrastructure.Persistence;

/// <inheritdoc cref="IMediaStore" />
public sealed class MediaStore(MediaDbContext context) : IMediaStore
{
    /// <inheritdoc />
    public void Add(MediaObject mediaObject) => context.MediaObjects.Add(mediaObject);

    /// <inheritdoc />
    public void Add(MediaQuarantineEntry entry) => context.QuarantineEntries.Add(entry);

    /// <inheritdoc />
    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        // No constraint here is one a concurrent writer can realistically collide on: the object key
        // is a UUIDv7 from IIdGenerator, not a number two administrators could both reach for at once
        // the way catalog_versions' version number can. Unlike CatalogStore, there is nothing here to
        // turn into a specific user-facing conflict.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

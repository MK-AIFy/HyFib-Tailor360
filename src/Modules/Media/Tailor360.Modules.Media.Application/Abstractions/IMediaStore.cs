using Tailor360.Modules.Media.Domain.Media;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Media.Application.Abstractions;

/// <summary>Reads and writes media objects and their quarantine tracking.</summary>
public interface IMediaStore
{
    /// <summary>Adds a freshly uploaded object to the context.</summary>
    /// <param name="mediaObject">The object.</param>
    void Add(MediaObject mediaObject);

    /// <summary>Adds a freshly created quarantine entry to the context.</summary>
    /// <param name="entry">The entry.</param>
    void Add(MediaQuarantineEntry entry);

    /// <summary>Commits everything left in the context.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the conflict a concurrent writer caused.</returns>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}

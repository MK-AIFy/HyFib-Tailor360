using Microsoft.EntityFrameworkCore;
using Tailor360.Platform.Abstractions.Concurrency;

namespace Tailor360.Platform.Persistence.Concurrency;

/// <summary>
/// Reads an editable aggregate's concurrency token off a tracked entity, so that an endpoint can hand it
/// to the client as an <c>ETag</c> and compare the <c>If-Match</c> it gets back.
/// </summary>
/// <remarks>
/// The token is PostgreSQL's <c>xmin</c>, mapped by <c>ModuleDbContext.UseRowVersion</c> as a shadow
/// property, so there is nothing on the entity type to read it from. Every caller doing that by hand
/// would be one string literal away from reading a property that does not exist and failing at run time
/// on the first conflict — the one moment the code has to be right.
/// </remarks>
public static class RowVersion
{
    /// <summary>The shadow property PostgreSQL's system column is mapped to.</summary>
    public const string PropertyName = "xmin";

    /// <summary>Reads the entity tag of a tracked entity.</summary>
    /// <typeparam name="TEntity">The entity type, which must be mapped with a row version.</typeparam>
    /// <param name="context">The context tracking the entity.</param>
    /// <param name="entity">The entity.</param>
    /// <exception cref="InvalidOperationException">The entity type carries no row version.</exception>
    public static EntityTag EntityTagOf<TEntity>(this DbContext context, TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var entry = context.Entry(entity);

        if (entry.Metadata.FindProperty(PropertyName) is null)
        {
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} is not mapped with a concurrency token, so it cannot carry an "
                + "entity tag. Map it with ModuleDbContext.UseRowVersion or do not publish an ETag for it.");
        }

        return EntityTag.From(entry.Property<uint>(PropertyName).CurrentValue);
    }
}

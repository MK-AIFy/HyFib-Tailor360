using Microsoft.EntityFrameworkCore;
using Tailor360.Modules.Orders.Domain.Drafts;
using Tailor360.Platform.Persistence.Conventions;

namespace Tailor360.Modules.Orders.Infrastructure.Persistence;

/// <summary>Orders-owned persistence. No foreign keys cross module schemas.</summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
    : ModuleDbContext(options, SchemaName)
{
    public const string SchemaName = "orders";

    public DbSet<OrderDraft> OrderDrafts => Set<OrderDraft>();
    public DbSet<DraftGarment> DraftGarments => Set<DraftGarment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderDraft>(entity =>
        {
            entity.ToTable("order_drafts");
            entity.HasKey(draft => draft.Id);
            entity.Property(draft => draft.CustomerNumber).HasMaxLength(40).IsRequired();
            entity.Property(draft => draft.CustomerName).HasMaxLength(240).IsRequired();
            entity.HasIndex(draft => new { draft.OrganisationId, draft.BranchId, draft.UpdatedAt });
            entity.HasMany(draft => draft.Garments)
                .WithOne()
                .HasForeignKey(garment => garment.OrderDraftId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(draft => draft.Garments).UsePropertyAccessMode(PropertyAccessMode.Field);
            UseRowVersion(entity);
        });

        modelBuilder.Entity<DraftGarment>(entity =>
        {
            entity.ToTable("draft_garments");
            entity.HasKey(garment => garment.Id);
            entity.Property(garment => garment.CategoryCode).HasMaxLength(40).IsRequired();
            entity.Property(garment => garment.ServiceCode).HasMaxLength(40).IsRequired();
            entity.Property(garment => garment.ServiceName).HasMaxLength(120).IsRequired();
            entity.Property(garment => garment.Notes).HasMaxLength(1000);
        });
    }
}

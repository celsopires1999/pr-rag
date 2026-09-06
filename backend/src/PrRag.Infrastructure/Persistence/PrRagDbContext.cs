using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using PrRag.Application.Domain;

namespace PrRag.Infrastructure.Persistence;

public sealed class PrRagDbContext : DbContext
{
    public PrRagDbContext(DbContextOptions<PrRagDbContext> options)
        : base(options)
    {
    }

    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();

    public DbSet<DataStatus> DataStatuses => Set<DataStatus>();

    public DbSet<CreatedRequisition> CreatedRequisitions => Set<CreatedRequisition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var embeddingConverter = new ValueConverter<float[]?, Vector>(
            v => new Vector(v ?? Array.Empty<float>()),
            v => v.ToArray());

        var embeddingComparer = new ValueComparer<float[]>(
            (a, b) => a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
            v => v.ToArray());

        modelBuilder.Entity<PurchaseRequisition>(entity =>
        {
            entity.ToTable("purchase_requisitions");

            entity.HasKey(e => e.PurchaseRequisitionId);
            entity.Property(e => e.PurchaseRequisitionId)
                .HasColumnName("purchase_requisition")
                .HasMaxLength(10);

            entity.Property(e => e.SupplierCode)
                .HasColumnName("supplier_code")
                .HasMaxLength(9);

            entity.Property(e => e.SupplierName)
                .HasColumnName("supplier_name")
                .HasMaxLength(50);

            entity.Property(e => e.Item)
                .HasColumnName("item")
                .HasMaxLength(28);

            entity.Property(e => e.ItemName)
                .HasColumnName("item_name")
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(500);

            entity.Property(e => e.Embedding)
                .HasColumnName("embedding")
                .HasColumnType($"vector({PurchaseRequisition.EmbeddingDimensions})")
                .HasConversion(embeddingConverter, embeddingComparer);

            entity.Ignore(e => e.EmbeddingSource);
        });

        modelBuilder.Entity<DataStatus>(entity =>
        {
            entity.ToTable("data_status");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.LastSync).HasColumnName("last_sync");
        });

        modelBuilder.Entity<CreatedRequisition>(entity =>
        {
            entity.ToTable("created_requisitions", "created");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");

            entity.Property(e => e.SupplierCode)
                .HasColumnName("supplier_code")
                .HasMaxLength(9);

            entity.Property(e => e.Item)
                .HasColumnName("item")
                .HasMaxLength(28);

            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(500);

            entity.Property(e => e.Quantity)
                .HasColumnName("quantity");

            entity.Property(e => e.Date)
                .HasColumnName("date")
                .HasMaxLength(10);

            entity.Property(e => e.Requester)
                .HasColumnName("requester")
                .HasMaxLength(100);

            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });
    }
}

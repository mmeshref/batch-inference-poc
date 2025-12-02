using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared;

public sealed class FileEntity
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Filename { get; set; }
    public required string StoragePath { get; set; }
    public required string Purpose { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BatchEntity
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public Guid InputFileId { get; set; }
    public Guid? OutputFileId { get; set; }
    public required string Status { get; set; }
    public required string Endpoint { get; set; }
    public TimeSpan CompletionWindow { get; set; }
    public int Priority { get; set; }
    public required string GpuPool { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<RequestEntity> Requests { get; set; } = new List<RequestEntity>();
}

public sealed class RequestEntity
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public int LineNumber { get; set; }
    public required string InputPayload { get; set; }
    public string? OutputPayload { get; set; }
    public required string Status { get; set; }
    public required string GpuPool { get; set; }
    public string? AssignedWorker { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public BatchEntity? Batch { get; set; }
}

public sealed class BatchDbContext(DbContextOptions<BatchDbContext> options) : DbContext(options)
{
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<BatchEntity> Batches => Set<BatchEntity>();
    public DbSet<RequestEntity> Requests => Set<RequestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureFiles(modelBuilder);
        ConfigureBatches(modelBuilder);
        ConfigureRequests(modelBuilder);
    }

    private static void ConfigureFiles(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FileEntity>();
        entity.ToTable("files");
        entity.HasKey(f => f.Id);
        entity.Property(f => f.UserId).IsRequired();
        entity.Property(f => f.Filename).IsRequired();
        entity.Property(f => f.StoragePath).IsRequired();
        entity.Property(f => f.Purpose).IsRequired();
        entity.Property(f => f.CreatedAt).IsRequired();
    }

    private static void ConfigureBatches(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BatchEntity>();
        entity.ToTable("batches");
        entity.HasKey(b => b.Id);
        entity.Property(b => b.UserId).IsRequired();
        entity.Property(b => b.InputFileId).IsRequired();
        entity.Property(b => b.Status).IsRequired();
        entity.Property(b => b.Endpoint).IsRequired();
        entity.Property(b => b.CompletionWindow).IsRequired();
        entity.Property(b => b.Priority).IsRequired();
        entity.Property(b => b.GpuPool).IsRequired();
        entity.Property(b => b.CreatedAt).IsRequired();

        entity.HasOne<FileEntity>()
              .WithMany()
              .HasForeignKey(b => b.InputFileId)
              .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<FileEntity>()
              .WithMany()
              .HasForeignKey(b => b.OutputFileId)
              .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRequests(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RequestEntity>();
        entity.ToTable("requests");
        entity.HasKey(r => r.Id);
        entity.Property(r => r.BatchId)
              .IsRequired()
              .HasColumnName("BatchId");
        entity.Property(r => r.LineNumber).IsRequired();
        entity.Property(r => r.InputPayload).IsRequired();
        entity.Property(r => r.Status).IsRequired();
        entity.Property(r => r.GpuPool).IsRequired();
        entity.Property(r => r.CreatedAt).IsRequired();

        entity.HasOne(r => r.Batch)
              .WithMany(b => b.Requests)
              .HasForeignKey(r => r.BatchId)
              .OnDelete(DeleteBehavior.Cascade);
    }
}

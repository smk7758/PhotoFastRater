using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Database;

public class PhotoDbContext : DbContext
{
    public DbSet<Photo> Photos { get; set; } = null!;
    public DbSet<Event> Events { get; set; } = null!;
    public DbSet<PhotoEventMapping> PhotoEventMappings { get; set; } = null!;
    public DbSet<ExportTemplate> ExportTemplates { get; set; } = null!;
    public DbSet<Camera> Cameras { get; set; } = null!;
    public DbSet<Lens> Lenses { get; set; } = null!;
    public DbSet<ManagedFolder> ManagedFolders { get; set; } = null!;
    public DbSet<FolderExclusionPattern> FolderExclusionPatterns { get; set; } = null!;
    public DbSet<PhotoTag> PhotoTags { get; set; } = null!;
    public DbSet<PhotoTagMapping> PhotoTagMappings { get; set; } = null!;
    public DbSet<PhotoCollection> PhotoCollections { get; set; } = null!;
    public DbSet<PhotoCollectionMapping> PhotoCollectionMappings { get; set; } = null!;

    public PhotoDbContext(DbContextOptions<PhotoDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Photo エンティティ
        modelBuilder.Entity<Photo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DateTaken);
            entity.HasIndex(e => e.CameraModel);
            entity.HasIndex(e => e.Rating);
            entity.HasIndex(e => e.FileHash);
            entity.HasIndex(e => e.FolderPath);
            entity.HasIndex(e => e.NormalizedPath)
                .IsUnique()
                .HasFilter("NormalizedPath IS NOT NULL");
            entity.HasIndex(e => new { e.DateTaken, e.Id });
            entity.HasIndex(e => new { e.NormalizedDirectory, e.NormalizedBaseName });
            entity.HasIndex(e => e.PairId);
            entity.HasIndex(e => e.IsMissing);
        });

        // Event エンティティ
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartDate);
            entity.HasIndex(e => e.EndDate);
            entity.HasIndex(e => e.AutoGroupKey).IsUnique().HasFilter("AutoGroupKey IS NOT NULL");
        });

        // PhotoEventMapping (多対多)
        modelBuilder.Entity<PhotoEventMapping>(entity =>
        {
            entity.HasKey(e => new { e.PhotoId, e.EventId });

            entity.HasOne(e => e.Photo)
                .WithMany(p => p.Events)
                .HasForeignKey(e => e.PhotoId);

            entity.HasOne(e => e.Event)
                .WithMany(ev => ev.Photos)
                .HasForeignKey(e => e.EventId);
        });

        // ExportTemplate エンティティ
        modelBuilder.Entity<ExportTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Camera エンティティ
        modelBuilder.Entity<Camera>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Make, e.Model }).IsUnique();
        });

        // Lens エンティティ
        modelBuilder.Entity<Lens>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Model).IsUnique();
        });

        // ManagedFolder エンティティ
        modelBuilder.Entity<ManagedFolder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FolderPath).IsUnique();
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.LastScanDate);
        });

        // FolderExclusionPattern エンティティ
        modelBuilder.Entity<FolderExclusionPattern>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IsEnabled);
        });

        modelBuilder.Entity<PhotoTag>(entity =>
        {
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Name).HasMaxLength(128);
            entity.Property(tag => tag.NormalizedName).HasMaxLength(128);
            entity.HasIndex(tag => tag.NormalizedName).IsUnique();
        });
        modelBuilder.Entity<PhotoTagMapping>(entity =>
        {
            entity.HasKey(mapping => new { mapping.PhotoId, mapping.TagId });
            entity.HasOne(mapping => mapping.Photo).WithMany(photo => photo.Tags).HasForeignKey(mapping => mapping.PhotoId);
            entity.HasOne(mapping => mapping.Tag).WithMany(tag => tag.Photos).HasForeignKey(mapping => mapping.TagId);
        });
        modelBuilder.Entity<PhotoCollection>(entity =>
        {
            entity.HasKey(collection => collection.Id);
            entity.Property(collection => collection.Name).HasMaxLength(128);
            entity.HasIndex(collection => new { collection.ParentId, collection.Name }).IsUnique();
            entity.HasOne(collection => collection.Parent)
                .WithMany(parent => parent.Children)
                .HasForeignKey(collection => collection.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PhotoCollectionMapping>(entity =>
        {
            entity.HasKey(mapping => new { mapping.PhotoId, mapping.CollectionId });
            entity.HasOne(mapping => mapping.Photo).WithMany(photo => photo.Collections).HasForeignKey(mapping => mapping.PhotoId);
            entity.HasOne(mapping => mapping.Collection).WithMany(collection => collection.Photos).HasForeignKey(mapping => mapping.CollectionId);
        });
    }
}

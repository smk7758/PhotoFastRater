using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Core.Database;

public class PhotoDbContext : DbContext
{
    public DbSet<Photo> Photos { get; set; } = null!;
    public DbSet<Event> Events { get; set; } = null!;
    public DbSet<PhotoEventMapping> PhotoEventMappings { get; set; } = null!;
    public DbSet<ExportTemplate> ExportTemplates { get; set; } = null!;
    public DbSet<Camera> Cameras { get; set; } = null!;
    public DbSet<Lens> Lenses { get; set; } = null!;

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
        });

        // Event エンティティ
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartDate);
            entity.HasIndex(e => e.EndDate);
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
    }
}

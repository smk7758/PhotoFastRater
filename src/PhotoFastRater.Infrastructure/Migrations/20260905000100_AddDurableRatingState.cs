using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoFastRater.Infrastructure.Database;

#nullable disable

namespace PhotoFastRater.Infrastructure.Migrations;

/// <summary>Adds durable synchronization and pair indexes without deleting legacy values.</summary>
[DbContext(typeof(PhotoDbContext))]
[Migration("20260905000100_AddDurableRatingState")]
public sealed class AddDurableRatingState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("FileModifiedUtc", "Photos", nullable: false, defaultValue: DateTime.UnixEpoch);
        migrationBuilder.AddColumn<bool>("IsMissing", "Photos", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("MetadataSyncStatus", "Photos", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("NormalizedBaseName", "Photos", nullable: true);
        migrationBuilder.AddColumn<string>("NormalizedDirectory", "Photos", nullable: true);
        migrationBuilder.AddColumn<string>("NormalizedPath", "Photos", nullable: true);
        migrationBuilder.AddColumn<Guid>("PairId", "Photos", nullable: true);
        migrationBuilder.AddColumn<int>("PairLinkMode", "Photos", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTime>("RatingModifiedUtc", "Photos", nullable: false, defaultValue: DateTime.UnixEpoch);
        migrationBuilder.AddColumn<long>("RatingRevision", "Photos", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<string>("RatingSource", "Photos", nullable: false, defaultValue: "legacy");
        migrationBuilder.AddColumn<DateTime>("SidecarModifiedUtc", "Photos", nullable: true);

        // Backfill in SQL so upgrading a large catalog does not materialize every photo in the process.
        migrationBuilder.Sql("UPDATE Photos SET NormalizedPath = lower(replace(FilePath, '/', '\\')), NormalizedDirectory = lower(replace(FolderPath, '/', '\\')), NormalizedBaseName = lower(substr(FileName, 1, CASE WHEN instr(FileName, '.') > 0 THEN instr(FileName, '.') - 1 ELSE length(FileName) END)), FileModifiedUtc = COALESCE(ModifiedDate, ImportDate), RatingModifiedUtc = COALESCE(ModifiedDate, ImportDate);");
        // Preserve duplicate legacy rows for review, but only the oldest row claims the unique normalized path.
        migrationBuilder.Sql("UPDATE Photos SET NormalizedPath = NULL WHERE NormalizedPath IS NOT NULL AND Id NOT IN (SELECT MIN(Id) FROM Photos WHERE NormalizedPath IS NOT NULL GROUP BY NormalizedPath);");

        migrationBuilder.CreateIndex("IX_Photos_DateTaken_Id", "Photos", new[] { "DateTaken", "Id" });
        migrationBuilder.CreateIndex("IX_Photos_IsMissing", "Photos", "IsMissing");
        migrationBuilder.CreateIndex("IX_Photos_NormalizedDirectory_NormalizedBaseName", "Photos", new[] { "NormalizedDirectory", "NormalizedBaseName" });
        migrationBuilder.CreateIndex("IX_Photos_NormalizedPath", "Photos", "NormalizedPath", unique: true, filter: "NormalizedPath IS NOT NULL");
        migrationBuilder.CreateIndex("IX_Photos_PairId", "Photos", "PairId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Photos_DateTaken_Id", "Photos");
        migrationBuilder.DropIndex("IX_Photos_IsMissing", "Photos");
        migrationBuilder.DropIndex("IX_Photos_NormalizedDirectory_NormalizedBaseName", "Photos");
        migrationBuilder.DropIndex("IX_Photos_NormalizedPath", "Photos");
        migrationBuilder.DropIndex("IX_Photos_PairId", "Photos");
        migrationBuilder.DropColumn("FileModifiedUtc", "Photos");
        migrationBuilder.DropColumn("IsMissing", "Photos");
        migrationBuilder.DropColumn("MetadataSyncStatus", "Photos");
        migrationBuilder.DropColumn("NormalizedBaseName", "Photos");
        migrationBuilder.DropColumn("NormalizedDirectory", "Photos");
        migrationBuilder.DropColumn("NormalizedPath", "Photos");
        migrationBuilder.DropColumn("PairId", "Photos");
        migrationBuilder.DropColumn("PairLinkMode", "Photos");
        migrationBuilder.DropColumn("RatingModifiedUtc", "Photos");
        migrationBuilder.DropColumn("RatingRevision", "Photos");
        migrationBuilder.DropColumn("RatingSource", "Photos");
        migrationBuilder.DropColumn("SidecarModifiedUtc", "Photos");
    }
}

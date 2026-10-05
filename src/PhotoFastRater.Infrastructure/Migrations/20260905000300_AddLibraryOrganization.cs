using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoFastRater.Infrastructure.Database;

#nullable disable

namespace PhotoFastRater.Infrastructure.Migrations;

[DbContext(typeof(PhotoDbContext))]
[Migration("20260905000300_AddLibraryOrganization")]
public sealed class AddLibraryOrganization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("AutoGroupKey", "Events", type: "TEXT", nullable: true);
        migrationBuilder.CreateIndex("IX_Events_AutoGroupKey", "Events", "AutoGroupKey", unique: true, filter: "AutoGroupKey IS NOT NULL");
        migrationBuilder.CreateTable(
            name: "PhotoCollections",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                ParentId = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhotoCollections", x => x.Id);
                table.ForeignKey("FK_PhotoCollections_PhotoCollections_ParentId", x => x.ParentId, "PhotoCollections", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "PhotoTags",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                NormalizedName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PhotoTags", x => x.Id));
        migrationBuilder.CreateTable(
            name: "PhotoCollectionMappings",
            columns: table => new
            {
                PhotoId = table.Column<int>(type: "INTEGER", nullable: false),
                CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhotoCollectionMappings", x => new { x.PhotoId, x.CollectionId });
                table.ForeignKey("FK_PhotoCollectionMappings_PhotoCollections_CollectionId", x => x.CollectionId, "PhotoCollections", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PhotoCollectionMappings_Photos_PhotoId", x => x.PhotoId, "Photos", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateTable(
            name: "PhotoTagMappings",
            columns: table => new
            {
                PhotoId = table.Column<int>(type: "INTEGER", nullable: false),
                TagId = table.Column<int>(type: "INTEGER", nullable: false),
                AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PhotoTagMappings", x => new { x.PhotoId, x.TagId });
                table.ForeignKey("FK_PhotoTagMappings_Photos_PhotoId", x => x.PhotoId, "Photos", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PhotoTagMappings_PhotoTags_TagId", x => x.TagId, "PhotoTags", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_PhotoCollections_ParentId_Name", "PhotoCollections", new[] { "ParentId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_PhotoCollectionMappings_CollectionId", "PhotoCollectionMappings", "CollectionId");
        migrationBuilder.CreateIndex("IX_PhotoTags_NormalizedName", "PhotoTags", "NormalizedName", unique: true);
        migrationBuilder.CreateIndex("IX_PhotoTagMappings_TagId", "PhotoTagMappings", "TagId");

        migrationBuilder.Sql("CREATE VIRTUAL TABLE TagSearch USING fts5(Name, content='PhotoTags', content_rowid='Id', tokenize='unicode61');");
        migrationBuilder.Sql("CREATE TRIGGER PhotoTags_Fts_Insert AFTER INSERT ON PhotoTags BEGIN INSERT INTO TagSearch(rowid, Name) VALUES (new.Id, new.Name); END;");
        migrationBuilder.Sql("CREATE TRIGGER PhotoTags_Fts_Delete AFTER DELETE ON PhotoTags BEGIN INSERT INTO TagSearch(TagSearch, rowid, Name) VALUES ('delete', old.Id, old.Name); END;");
        migrationBuilder.Sql("CREATE TRIGGER PhotoTags_Fts_Update AFTER UPDATE OF Name ON PhotoTags BEGIN INSERT INTO TagSearch(TagSearch, rowid, Name) VALUES ('delete', old.Id, old.Name); INSERT INTO TagSearch(rowid, Name) VALUES (new.Id, new.Name); END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS PhotoTags_Fts_Update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS PhotoTags_Fts_Delete;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS PhotoTags_Fts_Insert;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS TagSearch;");
        migrationBuilder.DropTable("PhotoCollectionMappings");
        migrationBuilder.DropTable("PhotoTagMappings");
        migrationBuilder.DropTable("PhotoCollections");
        migrationBuilder.DropTable("PhotoTags");
        migrationBuilder.DropIndex("IX_Events_AutoGroupKey", "Events");
        migrationBuilder.DropColumn("AutoGroupKey", "Events");
    }
}

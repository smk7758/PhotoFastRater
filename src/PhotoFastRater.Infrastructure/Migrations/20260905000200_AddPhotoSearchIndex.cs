using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoFastRater.Infrastructure.Database;

#nullable disable

namespace PhotoFastRater.Infrastructure.Migrations;

/// <summary>Adds a content-linked FTS5 index without duplicating catalog ownership.</summary>
[DbContext(typeof(PhotoDbContext))]
[Migration("20260905000200_AddPhotoSearchIndex")]
public sealed class AddPhotoSearchIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE VIRTUAL TABLE PhotoSearch USING fts5(FileName, FilePath, content='Photos', content_rowid='Id', tokenize='unicode61');");
        migrationBuilder.Sql("INSERT INTO PhotoSearch(PhotoSearch) VALUES('rebuild');");
        migrationBuilder.Sql("CREATE TRIGGER Photos_Fts_Insert AFTER INSERT ON Photos BEGIN INSERT INTO PhotoSearch(rowid, FileName, FilePath) VALUES (new.Id, new.FileName, new.FilePath); END;");
        migrationBuilder.Sql("CREATE TRIGGER Photos_Fts_Delete AFTER DELETE ON Photos BEGIN INSERT INTO PhotoSearch(PhotoSearch, rowid, FileName, FilePath) VALUES ('delete', old.Id, old.FileName, old.FilePath); END;");
        migrationBuilder.Sql("CREATE TRIGGER Photos_Fts_Update AFTER UPDATE OF FileName, FilePath ON Photos BEGIN INSERT INTO PhotoSearch(PhotoSearch, rowid, FileName, FilePath) VALUES ('delete', old.Id, old.FileName, old.FilePath); INSERT INTO PhotoSearch(rowid, FileName, FilePath) VALUES (new.Id, new.FileName, new.FilePath); END;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS Photos_Fts_Update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS Photos_Fts_Delete;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS Photos_Fts_Insert;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS PhotoSearch;");
    }
}
